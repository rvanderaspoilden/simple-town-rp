using Interaction;
using Mirror;
using Sim;
using Sim.Logging;
using Sim.UI;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Sac de butin laissé au sol par un joueur mort (objet réseau Mirror, spawné par
/// ServerItemManager.DropPlayerLootBag). Il contient le butin de la victime dans la place
/// backend "container:{lootKey}" et vit indépendamment d'elle : la victime respawn, le sac
/// reste (jusqu'à 10 min ou jusqu'à ce qu'il soit vidé) pour que n'importe qui — y compris
/// la victime revenue sur les lieux — puisse récupérer le contenu.
///
/// Interaction : action « Fouiller » → ouvre la grille conteneur standard (même panneau que
/// les PNJ / coffres). Purement un IInteractable ; toute la logique de butin et de durée de
/// vie est côté serveur (ServerItemManager).
/// </summary>
public class LootBag : NetworkBehaviour, IInteractable, IQuickActionProvider {
    /// <summary>Clé de la place de butin "container:{lootKey}" (assignée au spawn serveur).</summary>
    [SyncVar] public string lootKey;

    /// <summary>Titre affiché en haut de la grille (ex. « Sac de Marc »).</summary>
    [SyncVar] public string label;

    private Action _searchAction;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float GetRange() => 2f;
    public bool IsInteractable() => _searchAction != null && !string.IsNullOrEmpty(lootKey);
    public bool IsRightClickOnly() => false;
    public void StopInteraction() { }

    public Action[] GetActions(bool withPriority = false)
        => _searchAction != null ? new[] { _searchAction } : System.Array.Empty<Action>();

    /// <summary>E court = fouille directe (pas de radial pour une seule action).</summary>
    public bool TryGetQuickAction(Action[] available, out Action action) {
        action = _searchAction;
        return _searchAction != null;
    }

    private void Awake() {
        Action template = Resources.Load<Action>("Configurations/Actions/SEARCH");
        if (template != null) {
            _searchAction = Instantiate(template);
            _searchAction.OnExecute += OnSearchExecuted;
        } else {
            Debug.LogWarning("[LootBag] SEARCH action asset introuvable (Configurations/Actions/SEARCH)");
        }
    }

    private void OnDestroy() {
        if (_searchAction != null) {
            _searchAction.OnExecute -= OnSearchExecuted;
            Destroy(_searchAction);
            _searchAction = null;
        }
    }

    /// <summary>Ouvre optimistement la grille (panneau conteneur, canal Prop) et demande au
    /// serveur l'ouverture de la place de butin de CE sac.</summary>
    private void OnSearchExecuted(Action action) {
        if (!NetworkClient.isConnected) return;
        ClientLogger.Network("[LootBag] SEARCH requested bag={BagNetId}", netId);
        // Occupe le joueur (BUSY) AVANT d'ouvrir la grille : la sortie d'Idle referme
        // l'inventaire, donc l'ordre compte. L'état se termine seul à la fermeture de la grille.
        PlayerController.Local?.BeginBusy(ContainerPanelUI.IsPropLootOpen, transform);
        ContainerPanelUI.Prop?.OpenOptimistic(ServerItemManager.PlayerCorpseLootSlots,
            string.IsNullOrEmpty(label) ? "Sac de butin" : label);
        NetworkClient.Send(new C2S_SearchLootBag { BagNetId = netId });
    }
}
