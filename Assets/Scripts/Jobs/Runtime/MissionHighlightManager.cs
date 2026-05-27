using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Singleton driving the mission highlight system. Listens to JobClientManager
    /// events and recomputes which world objects should be outlined based on the
    /// CURRENT STEP of every active mission — not on a JobPoint id.
    ///
    /// Ciblage hybride :
    ///  • par kind  : un step demande "tous les objets de ce kind" (filtrés par la
    ///                carrière de la mission). Cas courant (livreur : colis / machine / bac).
    ///  • par id    : un step peut demander un objet précis par id (bypass carrière).
    ///
    /// Met aussi à jour la variable shader globale `_MissionOutlinePulse` pour le pulse.
    /// </summary>
    public class MissionHighlightManager : MonoBehaviour {
        public static MissionHighlightManager Instance { get; private set; }

        private struct Entry {
            public MissionHighlightEffect effect;
            public MissionHighlightKind   kind;
            public string                 id;
            public JobCategory?           requiredJob;
            public MissionHighlightPhase  phase;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // Réutilisés à chaque refresh pour éviter les allocations.
        private readonly HashSet<string>       _activeIds      = new HashSet<string>();
        private readonly HashSet<JobCategory>  _activeCareers  = new HashSet<JobCategory>();

        // Colis de mission actuellement tenus par le JOUEUR LOCAL (clé = instanceId
        // de l'item). Pilote le gating "phase" : mains libres → on montre les colis à
        // ramasser ; un colis en main → on montre les bacs où le déposer.
        private static readonly HashSet<int> _localHeldPayloads = new HashSet<int>();
        public static bool LocalHoldsPayload => _localHeldPayloads.Count > 0;

        /// <summary>Émis quand l'état "le joueur local tient un colis" change. Les
        /// colis au sol s'y abonnent pour rafraîchir leur propre outline.</summary>
        public static event System.Action HoldStateChanged;

        [Header("Pulse Animation")]
        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float pulseMin = 0.85f;
        [SerializeField] private float pulseMax = 1.15f;

        private static readonly int PulseId = Shader.PropertyToID("_MissionOutlinePulse");

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start() {
            Subscribe();
            RefreshHighlight();
        }

        private void OnDestroy() {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        // ── Registration ──────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre un effet. <paramref name="id"/> peut être vide (matching par
        /// kind uniquement). <paramref name="requiredJob"/> filtre par carrière pour
        /// la voie kind ; null = pas de filtre (cas des colis).
        /// </summary>
        public static void Register(MissionHighlightKind kind, string id, MissionHighlightEffect effect,
                                    JobCategory? requiredJob,
                                    MissionHighlightPhase phase = MissionHighlightPhase.Always) {
            if (effect == null || kind == MissionHighlightKind.None) return;
            _entries.Add(new Entry { effect = effect, kind = kind, id = id, requiredJob = requiredJob, phase = phase });
            Instance?.RefreshHighlight();
        }

        public static void Unregister(MissionHighlightEffect effect) {
            if (effect == null) return;
            for (int i = _entries.Count - 1; i >= 0; i--) {
                if (_entries[i].effect == effect) _entries.RemoveAt(i);
            }
            if (effect.IsHighlighted) effect.Hide();
        }

        /// <summary>Recalcule l'état des highlights (ex. après qu'un colis change de main).</summary>
        public static void RequestRefresh() => Instance?.RefreshHighlight();

        /// <summary>
        /// Déclaré par les colis de mission (MissionItemBehaviour) quand le JOUEUR
        /// LOCAL les prend (held=true) ou les lâche/dépose (held=false). Met à jour
        /// le gating de phase et notifie les colis au sol.
        /// </summary>
        public static void SetLocalPayloadHeld(int itemKey, bool held) {
            bool changed = held ? _localHeldPayloads.Add(itemKey)
                                 : _localHeldPayloads.Remove(itemKey);
            if (!changed) return;
            Instance?.RefreshHighlight();   // (re)cache/montre les bacs
            HoldStateChanged?.Invoke();      // (re)cache/montre les colis au sol
        }

        // ── Events ────────────────────────────────────────────────────────────

        private void Subscribe() {
            var c = JobClientManager.Instance;
            if (c == null) {
                Debug.LogWarning("[MissionHighlightManager] JobClientManager not found — highlights won't work.");
                return;
            }
            c.JobOffered      += OnJobUpdate;
            c.JobStepAdvanced += OnJobUpdate;
            c.JobFinished     += OnJobUpdate;
        }

        private void Unsubscribe() {
            var c = JobClientManager.Instance;
            if (c == null) return;
            c.JobOffered      -= OnJobUpdate;
            c.JobStepAdvanced -= OnJobUpdate;
            c.JobFinished     -= OnJobUpdate;
        }

        private void OnJobUpdate(JobClientState _) => RefreshHighlight();

        // ── Core ──────────────────────────────────────────────────────────────

        private void RefreshHighlight() {
            MissionHighlightKind activeKinds = MissionHighlightKind.None;
            _activeIds.Clear();
            _activeCareers.Clear();

            var states = JobClientManager.Instance?.States;
            if (states != null) {
                foreach (var state in states.Values) {
                    if (state == null || state.Status != JobStatus.Active) continue;
                    var def = state.Definition;
                    if (def == null) continue;
                    int idx = state.CurrentStepIndex;
                    if (idx < 0 || idx >= def.Steps.Count) continue;
                    var step = def.Steps[idx];
                    if (step == null) continue;

                    activeKinds |= step.GetHighlightKinds();
                    var ids = step.GetHighlightTargetIds();
                    if (ids != null) {
                        for (int i = 0; i < ids.Count; i++) {
                            if (!string.IsNullOrEmpty(ids[i])) _activeIds.Add(ids[i]);
                        }
                    }
                    _activeCareers.Add(def.Category);
                }
            }

            for (int i = 0; i < _entries.Count; i++) {
                var e = _entries[i];
                if (e.effect == null) continue;
                if (ShouldShow(e, activeKinds)) e.effect.Show();
                else                            e.effect.Hide();
            }
        }

        private bool ShouldShow(Entry e, MissionHighlightKind activeKinds) {
            // Gating de phase selon l'état des mains du joueur local (générique,
            // réutilisé par tout métier ramasser→déposer : tri, nettoyage, …).
            //  • Holding   : cible de dépôt (bac, poubelle) → seulement en portant un colis.
            //  • HandsFree : cible de ramassage → seulement mains libres.
            // Une cible de dépôt masquée n'est pas non plus interactable (IsInteractable
            // dépend de IsHighlighted) → pas de dépôt mains vides.
            if (e.phase == MissionHighlightPhase.Holding   && !LocalHoldsPayload) return false;
            if (e.phase == MissionHighlightPhase.HandsFree &&  LocalHoldsPayload) return false;

            // Voie ciblage précis : bypass du filtre carrière.
            if (!string.IsNullOrEmpty(e.id) && _activeIds.Contains(e.id)) return true;

            // Voie kind : le kind doit être actif ET (pas de filtre carrière OU carrière active).
            if ((activeKinds & e.kind) == 0) return false;
            if (e.requiredJob == null) return true;
            return _activeCareers.Contains(e.requiredJob.Value);
        }

        private void Update() {
            float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Shader.SetGlobalFloat(PulseId, pulse);
        }
    }
}
