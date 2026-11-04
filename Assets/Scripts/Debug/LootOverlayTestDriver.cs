using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Harnais de test VISUEL de l'overlay « Récupération » (LootPanelUI), hors réseau.
/// Vit dans la scène scratch <c>Assets/Scenes/LootOverlayTest.unity</c> : instancie
/// DatabaseManager + HUD Manager, ouvre l'overlay avec un faux snapshot conteneur +
/// un faux snapshot poche (reflection sur les handlers privés — le wire réel reste
/// intouché), capture des screenshots, puis quitte le play mode tout seul.
/// Items factices : uniquement des ItemConfig existants (armes 103/104, munitions
/// 106/107, consommables 10-12/20, liasse 110, hache 111).
/// </summary>
public class LootOverlayTestDriver : MonoBehaviour
{
    [Tooltip("Dossier ABSOLU où écrire les captures (défaut : <projet>/TestScreenshots).")]
    [SerializeField] private string screenshotDir = "";

    private IEnumerator Start()
    {
        string dir = string.IsNullOrEmpty(screenshotDir)
            ? System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "TestScreenshots")
            : screenshotDir;
        System.IO.Directory.CreateDirectory(dir);

        Instantiate(Resources.Load<GameObject>("Prefabs/Managers/DatabaseManager"));
        Instantiate(Resources.Load<GameObject>("Prefabs/Managers/HUD Manager"));
        yield return null; // Awake des managers

        var panel = LootPanelUI.Instance;
        if (panel == null) {
            Debug.LogError("[LootOverlayTest] LootPanelUI.Instance null — overlay absent du prefab HUD ?");
            Quit();
            yield break;
        }

        // ── Faux snapshot CONTENEUR (gauche) — 16 slots / 13 items : la grille doit
        // plafonner à 3 rangées visibles + scrollbar. ─────────────────────────
        var open = new S2C_ContainerOpened {
            PropId    = 0,
            PlaceId   = "test-place",
            SlotCount = 16,
            Items     = new[] {
                Item(101, 104, 0, 1,  loadedAmmo: 30),   // AK-47
                Item(102, 107, 1, 24),                    // munitions medium (pile)
                Item(103, 10,  2, 2),                     // conso
                Item(104, 11,  3, 4),                     // conso
                Item(105, 110, 4, 1,  cash: 1250),        // liasse
                Item(106, 111, 5, 1),                     // hache
                Item(107, 12,  6, 1),                     // conso
                Item(108, 20,  7, 3),
                Item(109, 21,  8, 1),
                Item(110, 106, 9, 60),
                Item(111, 22,  10, 2),
                Item(112, 23,  11, 1),
                Item(113, 108, 13, 10),
            },
        };
        // Le titre vient du flux d'ouverture — simule l'optimistic open d'une fouille.
        Invoke(panel, "OpenSearchDelayed", 16, "Fouille — Bandit", 0f);
        yield return null;
        Invoke(panel, "OnContainerOpened", open);
        yield return null;

        // ── Faux snapshot POCHE (droite) ────────────────────────────────────
        var pocket = new S2C_PocketSync {
            PlaceId   = "test-pocket",
            SlotCount = 8,
            Items     = new[] {
                PItem(201, 103, 0, 1, loadedAmmo: 15),    // Glock
                PItem(202, 106, 1, 60),                   // munitions light
                PItem(203, 20,  2, 3),                    // conso
                PItem(204, 110, 3, 1, cash: 480),         // liasse
                PItem(205, 111, 4, 1),                    // hache
            },
        };
        Invoke(panel, "ApplyPocketSnapshot", pocket);

        yield return new WaitForSeconds(0.5f);
        yield return Capture(System.IO.Path.Combine(dir, "loot_overlay_1.png"));

        // Deuxième capture : grille scrollée en bas (vérifie le clipping + la scrollbar).
        var scrollRect = panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        yield return new WaitForSeconds(0.2f);
        yield return Capture(System.IO.Path.Combine(dir, "loot_overlay_2.png"));

        // Troisième capture : HUD INVENTAIRE (rangée POCHES + ses boutons tri/optimisation).
        panel.Close();
        yield return null;
        Sim.HUDManager.Instance.ShowInventory();
        yield return null;
        Invoke(Sim.HUDManager.Instance.InventoryUI, "ApplyPocketSnapshot", pocket);
        yield return new WaitForSeconds(0.3f);
        yield return Capture(System.IO.Path.Combine(dir, "inventory_hud.png"));

        Debug.Log("[LootOverlayTest] Captures écrites dans " + dir);
        yield return new WaitForSeconds(0.25f);
        Quit();
    }

    private static S2C_ContainerItem Item(int entity, int cfg, int slot, int qty, int loadedAmmo = -1, int cash = 0)
        => new S2C_ContainerItem { EntityId = entity, ConfigId = cfg, SlotIndex = slot, Quantity = qty,
                                   LoadedAmmo = loadedAmmo, CashAmount = cash };

    private static S2C_PocketItem PItem(int entity, int cfg, int slot, int qty, int loadedAmmo = -1, int cash = 0)
        => new S2C_PocketItem { EntityId = entity, ConfigId = cfg, SlotIndex = slot, Quantity = qty,
                                LoadedAmmo = loadedAmmo, CashAmount = cash };

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) { Debug.LogError($"[LootOverlayTest] Méthode introuvable : {method}"); return; }
        mi.Invoke(target, args);
    }

    private static IEnumerator Capture(string path)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        // CaptureScreenshot est asynchrone : attend l'apparition du fichier.
        for (int i = 0; i < 60 && !System.IO.File.Exists(path); i++) yield return null;
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
