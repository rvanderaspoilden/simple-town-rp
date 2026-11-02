using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace SimEditor {
    /// <summary>
    /// One-click wiring of the City's Synty ATM walls (SM_Bld_Wall_ATM_01) : run
    /// "Tools/The BROZ/Setup City ATMs" with the City scene open. For each wall instance it
    /// (re)creates a child "ATM Interactable" : BoxCollider (layer Interactable) over the ATM
    /// unit + <see cref="AtmBehaviour"/> + a disabled "View Point" child holding the
    /// CinemachineCamera (priority 40 > vcam TPS) placed in front of the screen — activated
    /// during the ATM session, the brain blends the view onto it. Idempotent — re-running
    /// replaces the previously built child.
    ///
    /// Géométrie mesurée sur le mesh : bloc ATM centré en x=-1.25, saillie écran côté +Z
    /// (z=0.244), unité entre y=0.57 et y=2.12.
    /// </summary>
    public static class AtmSetup {
        private const string WallName  = "SM_Bld_Wall_ATM_01";
        private const string ChildName = "ATM Interactable";

        private const string CardFolder     = "Assets/Resources/Prefabs/Atm";
        private const string CardPrefabPath = CardFolder + "/AtmCard.prefab";
        private const string CashPrefabPath = CardFolder + "/AtmCash.prefab";

        [MenuItem("Tools/The BROZ/Setup City ATMs")]
        public static void Setup() {
            int created = 0;

            // Murs collectés AVANT toute destruction : le DestroyImmediate des anciens enfants
            // invaliderait des entrées de l'énumération en cours (MissingReferenceException).
            var walls = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.name == WallName && t.gameObject.scene.IsValid()) walls.Add(t);

            GameObject cardPrefab = EnsureCardPrefab();
            GameObject cashPrefab = EnsureCashPrefab();

            foreach (Transform t in walls) {

                Transform existing = t.Find(ChildName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                GameObject root = new GameObject(ChildName);
                root.transform.SetParent(t, false);
                root.transform.localPosition = new Vector3(-1.25f, 0f, 0f); // centre du bloc ATM
                root.layer = LayerMask.NameToLayer("Interactable");

                BoxCollider col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 1.35f, 0.15f);
                col.size   = new Vector3(1.3f, 1.8f, 0.45f);

                AtmBehaviour atm = root.AddComponent<AtmBehaviour>();

                // Ancre du canvas écran world-space (AtmScreenUI) : posée À LA MAIN dans la
                // scène (2026-10-31) sur la dalle écran du mesh — qui est en fait INCLINÉE
                // d'environ 17° vers le haut, d'où le tilt X et le z proche du plan du bloc
                // (le haut du canvas s'enfonce dans le renfoncement). Le canvas (~450×406 px
                // à l'échelle 0.001) épouse la dalle, bezel compris.
                GameObject anchor = new GameObject("Screen Anchor");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = new Vector3(0.1762007f, 1.256f, 0.0219977f);
                anchor.transform.localRotation = Quaternion.Euler(17.09071f, 180f, 0f);

                // Fente à carte (mesurée : ouverture vers (-0.31, 1.43)) : cliquable pendant
                // l'état « Insérez votre carte » pour simuler l'entrée de la CB.
                GameObject slot = new GameObject("Card Slot");
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = new Vector3(-0.31f, 1.43f, 0.21f);
                slot.layer = root.layer;
                BoxCollider slotCol = slot.AddComponent<BoxCollider>();
                slotCol.size = new Vector3(0.24f, 0.18f, 0.19f); // couvre aussi la CB qui dépasse (z≈0.30)

                // CB à moitié insérée dans la fente : visuel de session (désactivé au repos),
                // animé par AtmUI — outline clignotant → clic → glisse dedans ; ressort à la
                // fin. Posée à plat, +Z local = vers l'extérieur de la machine.
                GameObject card = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                card.name = "Card";
                card.transform.SetParent(slot.transform, false);
                // Fente noire du lecteur mesurée par marqueurs sans parallaxe (caméra axiale) :
                // centre bloc-local (-0.335, 1.435) ; face de la machine à z=0.244 (raycast).
                // Carte (profondeur 0.11) à moitié insérée : ~6 cm dedans, ~5 cm dehors.
                card.transform.localPosition = new Vector3(-0.01f, 0.005f, 0.029f);
                card.SetActive(false);

                // Fente à billets du bas (mesurée par marqueurs : centre (-0.334, 1.058),
                // largeur ~14,7 cm, face z=0.244) : la liasse y est animée par AtmUI —
                // elle en SORT au retrait, y est AVALÉE au dépôt. Pas de collider (pas de clic).
                GameObject cashSlot = new GameObject("Cash Slot");
                cashSlot.transform.SetParent(root.transform, false);
                cashSlot.transform.localPosition = new Vector3(-0.334f, 1.058f, 0.21f);
                GameObject cashVisual = (GameObject)PrefabUtility.InstantiatePrefab(cashPrefab);
                cashVisual.name = "Cash";
                cashVisual.transform.SetParent(cashSlot.transform, false);
                cashVisual.transform.localPosition = new Vector3(0f, 0f, 0.045f);
                cashVisual.SetActive(false);

                // Caméra de session : face à la machine, cadrée pour voir à la fois l'écran
                // (canvas) et la fente à carte, léger désaxage pour la perspective diégétique.
                // Surélevée (1.52) pour plonger légèrement sur la CB qui dépasse de la fente —
                // à hauteur d'œil on ne la verrait que par la tranche.
                GameObject view = new GameObject("View Point");
                view.transform.SetParent(root.transform, false);
                view.transform.localPosition = new Vector3(0.02f, 1.52f, 0.92f);
                view.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(0.0f, 1.30f, 0.17f) - view.transform.localPosition, Vector3.up);
                CinemachineCamera vcam = view.AddComponent<CinemachineCamera>();
                vcam.Priority = 40;
                vcam.Lens.FieldOfView = 45f;
                view.SetActive(false);

                SerializedObject so = new SerializedObject(atm);
                so.FindProperty("viewCamera").objectReferenceValue = view;
                so.FindProperty("screenAnchor").objectReferenceValue = anchor.transform;
                so.FindProperty("cardSlot").objectReferenceValue = slotCol;
                so.FindProperty("cardVisual").objectReferenceValue = card.transform;
                so.FindProperty("cashVisual").objectReferenceValue = cashVisual.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(t.gameObject);
                created++;
            }

            if (created > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log($"[AtmSetup] {created} ATM(s) configurés dans la scène.");
        }

        // ── Prefab de la CB (primitives + matériaux URP, reconstruit à chaque run) ──

        /// <summary>CB stylisée (pivot au centre, à plat, épaisseur en Y, +Z vers le bout qui
        /// dépasse de la fente) : corps bleu + puce dorée + logo clair, échelle « chunky »
        /// Synty pour rester lisible depuis la vcam de session. HoverOutline embarqué pour le
        /// clignotement d'incitation (AtmUI).</summary>
        private static GameObject EnsureCardPrefab() {
            if (!AssetDatabase.IsValidFolder(CardFolder))
                AssetDatabase.CreateFolder("Assets/Resources/Prefabs", "Atm");

            Material body = EnsureMaterial(CardFolder + "/AtmCard_Body.mat", new Color(0.10f, 0.28f, 0.75f));
            Material chip = EnsureMaterial(CardFolder + "/AtmCard_Chip.mat", new Color(0.85f, 0.69f, 0.23f));
            Material logo = EnsureMaterial(CardFolder + "/AtmCard_Logo.mat", new Color(0.92f, 0.93f, 0.95f));

            GameObject root = new GameObject("AtmCard");
            try {
                // Plus étroite que la fente du lecteur (10,7 cm) — insérée côté court.
                AddCardPart(root.transform, "Body", new Vector3(0f, 0f, 0f),              new Vector3(0.09f, 0.008f, 0.11f),  body);
                AddCardPart(root.transform, "Chip", new Vector3(-0.022f, 0.0055f, 0.026f), new Vector3(0.022f, 0.003f, 0.018f), chip);
                AddCardPart(root.transform, "Logo", new Vector3(0.024f, 0.0055f, 0.04f),   new Vector3(0.02f, 0.003f, 0.012f),  logo);
                root.AddComponent<HoverOutline>();
                return PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Liasse de billets stylisée (pivot au centre, à plat, +Z vers le bout qui
        /// dépasse de la fente à billets) : paquet vert + billet du dessus décalé + bande
        /// blanche — plus étroite que la fente (14,7 cm).</summary>
        private static GameObject EnsureCashPrefab() {
            if (!AssetDatabase.IsValidFolder(CardFolder))
                AssetDatabase.CreateFolder("Assets/Resources/Prefabs", "Atm");

            Material notes = EnsureMaterial(CardFolder + "/AtmCash_Notes.mat", new Color(0.42f, 0.62f, 0.42f));
            Material top   = EnsureMaterial(CardFolder + "/AtmCash_Top.mat",   new Color(0.55f, 0.72f, 0.52f));
            Material band  = EnsureMaterial(CardFolder + "/AtmCash_Band.mat",  new Color(0.93f, 0.93f, 0.90f));

            GameObject root = new GameObject("AtmCash");
            try {
                AddCardPart(root.transform, "Notes", new Vector3(0f, 0f, 0f),       new Vector3(0.12f, 0.018f, 0.10f),   notes);
                GameObject topNote = GameObject.CreatePrimitive(PrimitiveType.Cube);
                topNote.name = "TopNote";
                Object.DestroyImmediate(topNote.GetComponent<Collider>());
                topNote.transform.SetParent(root.transform, false);
                topNote.transform.localPosition = new Vector3(0.002f, 0.011f, -0.003f);
                topNote.transform.localRotation = Quaternion.Euler(0f, 5f, 0f);
                topNote.transform.localScale = new Vector3(0.115f, 0.004f, 0.095f);
                topNote.GetComponent<MeshRenderer>().sharedMaterial = top;
                AddCardPart(root.transform, "Band",  new Vector3(0f, 0.0025f, 0f),  new Vector3(0.034f, 0.024f, 0.102f), band);
                return PrefabUtility.SaveAsPrefabAsset(root, CashPrefabPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        private static Material EnsureMaterial(string path, Color color) {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            mat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void AddCardPart(Transform parent, string name, Vector3 pos, Vector3 size, Material mat) {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // zone de clic = collider de la fente
            part.transform.SetParent(parent, false);
            part.transform.localPosition = pos;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
