using System.Collections.Generic;
using Sim.NPC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Modale de dialogue d'un NPC. Déroule ENTIÈREMENT côté client le graphe
    /// <see cref="DialogueConfig"/> rechargé par <see cref="ClientNpcView"/> depuis sa NpcConfig.
    /// Seules les actions à enjeu repassent par le serveur : <see cref="DialogueAction.OpenShop"/>
    /// délègue au pipeline marchand existant (<see cref="ClientNpcManager.RequestMerchantCatalog"/>).
    ///
    /// Même pattern que <see cref="MerchantShopUI"/> : singleton, panneau autoré ACTIF dans le prefab
    /// HUD puis caché en fin d'Awake, template de bouton de réponse désactivé cloné par nœud.
    /// </summary>
    public class DialogueUI : MonoBehaviour {
        [Header("Refs")]
        [SerializeField] private TMP_Text             titleLabel;     // nom du NPC
        [SerializeField] private TMP_Text             npcText;        // réplique du NPC
        [SerializeField] private Button               closeButton;
        [SerializeField] private Transform            optionsContainer;   // parent des réponses clonées
        [SerializeField] private DialogueOptionButton optionTemplate;     // template désactivé

        public static DialogueUI Instance;

        private int            _npcId = -1;
        private string         _npcName = string.Empty;
        private DialogueConfig _dialogue;
        private readonly List<DialogueOptionButton> _options = new List<DialogueOptionButton>();

        // Posé par la branche OpenShop avant Hide() pour éviter de libérer la session NPC : la
        // boutique reprend la même session, pas de release+reacquire serveur.
        private bool _suppressEndOnHide;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.closeButton != null) this.closeButton.onClick.AddListener(this.Hide);
            if (this.optionTemplate != null) this.optionTemplate.gameObject.SetActive(false);

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Ouvre la modale sur le nœud d'entrée du dialogue.</summary>
        public void Show(int npcId, string npcName, DialogueConfig dialogue) {
            if (dialogue == null || !dialogue.HasContent) return;

            this._npcId    = npcId;
            this._npcName  = npcName ?? string.Empty;
            this._dialogue = dialogue;

            if (this.titleLabel != null) this.titleLabel.text = this._npcName;

            this.RenderNode(dialogue.GetRootNode());
            this.gameObject.SetActive(true);
        }

        private void RenderNode(DialogueNode node) {
            if (node == null) { this.Hide(); return; }

            if (this.npcText != null)
                this.npcText.text = (node.npcText ?? string.Empty).Replace("{name}", this._npcName);

            this.BuildOptions(node);
        }

        private void BuildOptions(DialogueNode node) {
            this.ClearOptions();
            if (this.optionTemplate == null || this.optionsContainer == null) return;

            // Liste vide → un unique bouton « Au revoir » qui ferme (fermeture implicite du nœud).
            if (node.options == null || node.options.Count == 0) {
                this.SpawnOption("Au revoir", this.Hide);
                return;
            }

            foreach (DialogueOption opt in node.options) {
                if (opt == null) continue;
                DialogueOption captured = opt;
                this.SpawnOption(opt.text, () => this.OnOptionChosen(captured));
            }
        }

        private void SpawnOption(string text, System.Action onClick) {
            DialogueOptionButton btn = Instantiate(this.optionTemplate, this.optionsContainer);
            btn.gameObject.SetActive(true);
            btn.Bind(text, onClick);
            this._options.Add(btn);
        }

        private void OnOptionChosen(DialogueOption option) {
            switch (option.action) {
                case DialogueAction.GoToNode:
                    DialogueNode next = this._dialogue != null ? this._dialogue.GetNode(option.nextNodeId) : null;
                    this.RenderNode(next); // next == null → Hide
                    break;

                case DialogueAction.OpenShop:
                    int npcId = this._npcId;
                    this._suppressEndOnHide = true; // la session passe au shop, pas de release.
                    this.Hide();
                    ClientNpcManager.Instance?.RequestMerchantCatalog(npcId);
                    break;

                case DialogueAction.EndDialogue:
                default:
                    this.Hide();
                    break;
            }
        }

        private void ClearOptions() {
            foreach (DialogueOptionButton b in this._options) {
                if (b != null) Destroy(b.gameObject);
            }
            this._options.Clear();
        }

        public void Hide() {
            // Release la session d'interaction NPC avant de cleaner _npcId — sauf si la branche
            // OpenShop a posé le flag (la session passe à MerchantShopUI sans release intermédiaire).
            if (!this._suppressEndOnHide && this._npcId >= 0) {
                NpcInteractionSession.End(this._npcId);
            }
            this._suppressEndOnHide = false;

            this._npcId    = -1;
            this._dialogue = null;
            this.ClearOptions();
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
