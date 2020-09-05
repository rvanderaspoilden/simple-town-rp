using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace Sim.Building {
    public class Wall : Props {
        [Header("Wall settings")]
        [Tooltip("Represent all id allowed to be modified")]
        [SerializeField] private int[] allowedSharedMaterialIds;

        [SerializeField] private List<WallFace> wallFaces;

        private new MeshRenderer renderer;
        private FoundationRenderer foundationRenderer;
        private MeshCollider collider;
        private Dictionary<int, WallFace> wallFacesPreviewed;

        protected override void Awake() {
            base.Awake();

            this.foundationRenderer = GetComponent<FoundationRenderer>();
            this.renderer = GetComponent<MeshRenderer>();
            this.collider = GetComponent<MeshCollider>();

            this.wallFacesPreviewed = new Dictionary<int, WallFace>();
        }

        protected override void Start() {
            base.Start();

            this.UpdateWallFaces();
        }

        public void Reset() {
            // rollback to wall face saved in wall face preview dictionnary
            foreach (KeyValuePair<int, WallFace> keyValuePair in this.wallFacesPreviewed) {
                this.wallFaces[keyValuePair.Key] = keyValuePair.Value;
            }

            this.wallFacesPreviewed.Clear();

            this.UpdateWallFaces();
            this.foundationRenderer.SetupDefaultMaterials();
        }

        public List<WallFace> GetWallFaces() {
            return this.wallFaces;
        }

        public void SetWallFaces(List<WallFace> wallFaces) {
            this.wallFaces = wallFaces;
            photonView.RPC("RPC_UpdateWallFaces", RpcTarget.OthersBuffered, JsonHelper.ToJson(this.wallFaces.ToArray()));
        }

        public void ApplyModification() {
            this.wallFacesPreviewed.Clear();

            photonView.RPC("RPC_UpdateWallFaces", RpcTarget.OthersBuffered, JsonHelper.ToJson(this.wallFaces.ToArray()));
        }

        public bool IsPreview() {
            return this.wallFacesPreviewed.Count > 0;
        }

        [PunRPC]
        public void RPC_UpdateWallFaces(string faces) {
            this.wallFaces = new List<WallFace>(JsonHelper.FromJson<WallFace>(faces));
            this.UpdateWallFaces();
            this.foundationRenderer.SetupDefaultMaterials();
        }

        public void PreviewMaterialOnFace(RaycastHit hit, PaintBucket paintBucket) {
            Mesh mesh = collider.sharedMesh;

            int limit = hit.triangleIndex * 3;
            int submesh;
            for (submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                int numIndices = mesh.GetTriangles(submesh).Length;
                if (numIndices > limit)
                    break;

                limit -= numIndices;
            }

            // Prevent to paint specific faces
            if (!allowedSharedMaterialIds.Contains(submesh)) {
                return;
            }

            WallFace face = this.wallFaces.Find(x => x.GetSharedMaterialIdx() == submesh);

            if (this.wallFacesPreviewed.ContainsKey(submesh)) {
                face.SetPaintConfigId(this.wallFacesPreviewed[submesh].GetPaintConfigId());
                face.SetAdditionalColor(this.wallFacesPreviewed[submesh].GetAdditionalColor());
                this.wallFacesPreviewed.Remove(submesh);
            } else if (face.GetPaintConfig() != paintBucket.GetPaintConfig()) { // if face is not still previewed and different from current so keep a backup then modify current one
                this.wallFacesPreviewed.Add(submesh, new WallFace(face));
                face.SetPaintConfigId(paintBucket.GetPaintConfig().GetId());
                face.SetAdditionalColor(paintBucket.GetColor());
            }

            this.UpdateWallFaces();

            this.foundationRenderer.SetupDefaultMaterials();
        }

        private void UpdateWallFaces() {
            Material[] sharedMaterials = this.renderer.sharedMaterials;

            for (int i = 0; i < this.wallFaces.Count; i++) {
                WallFace face = this.wallFaces[i];
                Material materialToApply = new Material(face.GetPaintConfig().GetMaterial());

                if (face.GetPaintConfig().AllowCustomColor()) {
                    materialToApply.color = face.GetAdditionalColor();
                }

                sharedMaterials[face.GetSharedMaterialIdx()] = materialToApply;
            }

            this.renderer.sharedMaterials = sharedMaterials;
        }
    }
}