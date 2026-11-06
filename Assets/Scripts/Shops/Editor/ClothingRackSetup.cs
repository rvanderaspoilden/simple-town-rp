using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimEditor {
    /// <summary>
    /// One-click wiring des portants de vêtements Synty de la City (SM_Prop_Clothes_Rack_*) :
    /// ouvrir la City puis « Tools/The BROZ/Setup City Clothing Racks ». Pour chaque portant,
    /// (re)crée un enfant « Rack Interactable » : BoxCollider (layer Interactable) couvrant le
    /// mesh + <see cref="ClothingRackBehaviour"/> (catalogue par défaut — champ laissé vide).
    /// Idempotent — relancer remplace l'enfant précédent. Pattern <see cref="AtmSetup"/>.
    /// </summary>
    public static class ClothingRackSetup {
        private const string RackNamePrefix = "SM_Prop_Clothes_Rack_";
        private const string ChildName = "Rack Interactable";

        [MenuItem("Tools/The BROZ/Setup City Clothing Racks")]
        public static void Setup() {
            // Racks collectés AVANT toute destruction (pattern AtmSetup).
            var racks = new List<Transform>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (t != null && t.gameObject.scene.IsValid() && t.name.StartsWith(RackNamePrefix)
                    && t.parent != null && t.parent.name != ChildName) {
                    racks.Add(t);
                }
            }

            int created = 0;
            foreach (Transform rack in racks) {
                Transform existing = rack.Find(ChildName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                GameObject child = new GameObject(ChildName);
                child.transform.SetParent(rack, false);
                child.layer = LayerMask.NameToLayer("Interactable");

                // Collider depuis les bounds locaux du mesh (enfant Synty à transform identité),
                // avec un peu de marge pour la visée centre-écran.
                Bounds bounds = LocalMeshBounds(rack);
                BoxCollider col = child.AddComponent<BoxCollider>();
                col.center = bounds.center;
                col.size = bounds.size + new Vector3(0.1f, 0.1f, 0.1f);

                child.AddComponent<ClothingRackBehaviour>();
                created++;
            }

            if (created > 0) {
                EditorSceneManager.MarkAllScenesDirty();
            }
            Debug.Log($"[ClothingRackSetup] {created} clothing racks wired (child '{ChildName}').");
        }

        private static Bounds LocalMeshBounds(Transform rack) {
            MeshFilter filter = rack.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) return filter.sharedMesh.bounds;
            return new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.6f, 2f, 0.8f));
        }
    }
}
