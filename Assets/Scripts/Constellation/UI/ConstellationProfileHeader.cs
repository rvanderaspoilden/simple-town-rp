using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Constellation {
    // En-tête (haut-gauche). Affiche UNIQUEMENT la liste des compteurs de devise dépensable :
    //   - les branches racines (showInProfile)
    //   - les sous-branches ACTIVES (visibles une fois leur nœud racine débloqué).
    // Chaque ligne = un rond coloré (couleur de la devise) + un label « DeviseLabel  N ».
    // Le template (GameObject désactivé) contient deux enfants nommés « Icon » (Image) et
    // « Label » (TMP). Expose GetCounterRect(branchId) pour que l'anim de déblocage puisse
    // y attacher des effets de vol.
    public class ConstellationProfileHeader : MonoBehaviour {
        [Header("Compteurs par devise")]
        [SerializeField] private RectTransform branchCountersContainer;
        [SerializeField] private GameObject branchCounterTemplate;   // row: Icon (Image) + Label (TMP)

        // État d'une ligne de compteur : on garde le label séparément du nombre pour
        // pouvoir interpoler la valeur affichée pendant l'anim de déblocage sans
        // refaire le parsing « Label N ».
        private class CountRow {
            public RectTransform rt;
            public TextMeshProUGUI labelTmp;
            public string label;
            public Color color;
            public int displayedValue;
            public Tween tween;
        }

        // Keyspace unique : une ligne par devise, keyée par BranchConfig.id (racines
        // ET sous-branches confondues).
        private readonly Dictionary<string, CountRow> _rows = new Dictionary<string, CountRow>();

        public RectTransform GetCounterRect(string branchId) {
            if (string.IsNullOrEmpty(branchId)) return null;
            return _rows.TryGetValue(branchId, out var row) ? row.rt : null;
        }

        // Décrémente visuellement le compteur d'une branche de `amount`, sur `duration`
        // secondes. Utilisé pendant l'anim de déblocage pour que le compteur fonde au
        // rythme des icônes qui volent vers le nœud. La valeur réelle dans l'État ne
        // change qu'à la fin (TryUnlock) ; après Refresh, on snap sur l'autoritaire.
        public void AnimateCounterDelta(string branchId, int amount, float duration) {
            if (!string.IsNullOrEmpty(branchId) && _rows.TryGetValue(branchId, out var row))
                AnimateRow(row, amount, duration);
        }

        private void AnimateRow(CountRow row, int amount, float duration) {
            if (row == null || amount <= 0) return;
            row.tween?.Kill();
            int start = row.displayedValue;
            int end   = Mathf.Max(0, start - amount);
            row.tween = DOTween.To(() => row.displayedValue, v => {
                row.displayedValue = v;
                if (row.labelTmp != null) row.labelTmp.text = row.label + "  " + v;
            }, end, duration).SetEase(Ease.OutQuad);
        }

        public void Refresh(IConstellationDataProvider provider) {
            PopulateCurrencyCounters(provider.Graph, provider.State);
        }

        private void PopulateCurrencyCounters(ConstellationGraphConfig graph, ConstellationState state) {
            if (branchCountersContainer == null || branchCounterTemplate == null) return;

            // Nettoyage : on tue les tweens en cours puis supprime toutes les lignes
            // sauf le template (les valeurs autoritatives seront re-spawnées en
            // dessous, donc tout reste cohérent même en plein déblocage).
            foreach (var kv in _rows) kv.Value.tween?.Kill();
            for (int i = branchCountersContainer.childCount - 1; i >= 0; i--) {
                var child = branchCountersContainer.GetChild(i);
                if (child == branchCounterTemplate.transform) continue;
                Destroy(child.gameObject);
            }
            _rows.Clear();

            branchCounterTemplate.SetActive(false);

            // 1) Compteurs des branches RACINES (showInProfile). Ingénieux a son flag à false
            // (arbre Métier gratuit). Keyés par id.
            foreach (var branchCfg in Sim.Constellation.Branches.BranchDatabase.RootBranches()) {
                if (branchCfg == null || string.IsNullOrEmpty(branchCfg.id) || !branchCfg.showInProfile) continue;
                AddRow(branchCfg, state);
            }

            // 2) Sous-branches ACTIVES (un compteur n'apparaît qu'une fois sa racine débloquée,
            // via definesBranch). Couleur/label depuis le BranchConfig.
            foreach (var root in state.ActiveBranchRoots()) {
                var b = root.definesBranch;
                if (b == null || string.IsNullOrEmpty(b.id) || _rows.ContainsKey(b.id)) continue;
                AddRow(b, state);
            }
        }

        private void AddRow(Sim.Constellation.Branches.BranchConfig b, ConstellationState state) {
            var row = SpawnRow("Counter_" + b.id,
                string.IsNullOrEmpty(b.displayName) ? b.id : b.displayName,
                state.GetAvailable(b.id),
                b.color);
            _rows[b.id] = row;
        }

        private CountRow SpawnRow(string name, string label, int value, Color color) {
            var go = Instantiate(branchCounterTemplate, branchCountersContainer);
            go.SetActive(true);
            go.name = name;
            var row = new CountRow {
                rt = (RectTransform)go.transform,
                label = label,
                color = color,
                displayedValue = value,
            };
            var iconTr = go.transform.Find("Icon");
            if (iconTr != null) {
                var img = iconTr.GetComponent<Image>();
                if (img != null) img.color = color;
            }
            var labelTr = go.transform.Find("Label");
            if (labelTr != null) {
                row.labelTmp = labelTr.GetComponent<TextMeshProUGUI>();
                if (row.labelTmp != null) {
                    row.labelTmp.text = label + "  " + value;
                    row.labelTmp.color = color;
                }
            }
            return row;
        }
    }
}
