using System.Collections.Generic;
using UnityEngine;

namespace Sim.Building {
    public class Wall : Props {
        [Header("Only for debug")]
        [SerializeField] private new MeshRenderer renderer;

        [SerializeField] private MeshCollider collider;

        private Dictionary<int, Material> materialPerFace;

        protected override void Awake() {
            base.Awake();
            
            this.renderer = GetComponent<MeshRenderer>();
            this.collider = GetComponent<MeshCollider>();
        }

        public void ApplyMaterialOnFace(RaycastHit hit, Material materialToApply) {
            Mesh mesh = collider.sharedMesh;

            int limit = hit.triangleIndex * 3;
            int submesh;
            for (submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                int numIndices = mesh.GetTriangles(submesh).Length;
                if (numIndices > limit)
                    break;

                limit -= numIndices;
            }

            Material[] sharedMaterials = this.renderer.sharedMaterials;
            sharedMaterials[submesh] = materialToApply;
            this.renderer.sharedMaterials = sharedMaterials;
        }
    }
}