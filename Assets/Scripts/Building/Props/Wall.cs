using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class Wall : Props {
        [Header("Wall settings")]
        [Tooltip("Represent all id allowed to be modified")]
        [SerializeField]
        private int[] allowedSharedMaterialIds;

        [SerializeField]
        private List<WallFace> wallFaces;

        [SerializeField]
        private bool exteriorWall;

        private new MeshRenderer renderer;
        private MeshCollider meshCollider;
        private Dictionary<int, WallFace> wallFacesPreviewed;
        private BoxCollider[] boxColliders;

        protected override void Awake() {
            base.Awake();
            this.renderer = GetComponent<MeshRenderer>();
            this.meshCollider = GetComponent<MeshCollider>();
            this.boxColliders = GetComponents<BoxCollider>();

            this.wallFacesPreviewed = new Dictionary<int, WallFace>();

            this.EnableCollidersOfType(ColliderTypeEnum.BOX_COLLIDER);
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
            this.propsRenderer.SetupDefaultMaterials();
        }

        public void EnableCollidersOfType(ColliderTypeEnum type) {
            foreach (BoxCollider boxCollider in this.boxColliders) {
                boxCollider.enabled = type == ColliderTypeEnum.BOX_COLLIDER;
            }

            this.meshCollider.enabled = type == ColliderTypeEnum.MESH_COLLIDER;
        }

        public List<WallFace> GetWallFaces() {
            return this.wallFaces;
        }

        public void SetWallFaces(List<WallFace> faces, RpcTarget rpcTarget) {
            photonView.RPC("RPC_UpdateWallFaces", rpcTarget, JsonHelper.ToJson(faces.ToArray()));
        }

        public void SetWallFaces(List<WallFace> faces, Player targetPlayer) {
            photonView.RPC("RPC_UpdateWallFaces", targetPlayer, JsonHelper.ToJson(faces.ToArray()));
        }

        public void SetExteriorWall(bool value, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetExteriorWall", rpcTarget, value);
        }

        public void SetExteriorWall(bool value, Player targetPlayer) {
            photonView.RPC("RPC_SetExteriorWall", targetPlayer, value);
        }

        [PunRPC]
        public void RPC_SetExteriorWall(bool value) {
            this.exteriorWall = value;
        }

        public void ApplyModification() {
            this.wallFacesPreviewed.Clear();

            this.SetWallFaces(this.wallFaces, RpcTarget.Others);
        }

        public bool IsPreview() {
            return this.wallFacesPreviewed.Count > 0;
        }

        [PunRPC]
        public void RPC_UpdateWallFaces(string faces) {
            if (this.propsRenderer == null) {
                this.renderer = GetComponent<MeshRenderer>();
                this.meshCollider = GetComponent<MeshCollider>();

                this.wallFacesPreviewed = new Dictionary<int, WallFace>();
            }

            this.wallFaces = new List<WallFace>(JsonHelper.FromJson<WallFace>(faces));
            this.UpdateWallFaces();
            this.propsRenderer.SetupDefaultMaterials();
        }

        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);
            this.SetWallFaces(this.wallFaces, playerTarget);
            this.SetExteriorWall(this.exteriorWall, playerTarget);
        }

        public void PreviewMaterialOnFace(RaycastHit hit, PaintBucket paintBucket) {
            if (this.IsAnExteriorFace(hit)) {
                return;
            }

            Mesh mesh = meshCollider.sharedMesh;

            int limit = hit.triangleIndex * 3;
            int submesh;
            for (submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                int numIndices = mesh.GetTriangles(submesh).Length;
                if (numIndices > limit)
                    break;

                limit -= numIndices;
            }

            // Prevent to paint specific faces
            if (!Enumerable.Contains(allowedSharedMaterialIds, submesh)) {
                return;
            }

            WallFace face = this.wallFaces.Find(x => x.GetSharedMaterialIdx() == submesh);

            if (this.wallFacesPreviewed.ContainsKey(submesh)) {
                face.SetPaintConfigId(this.wallFacesPreviewed[submesh].GetPaintConfigId());
                face.SetAdditionalColor(this.wallFacesPreviewed[submesh].GetAdditionalColor());
                this.wallFacesPreviewed.Remove(submesh);
            } else {
                this.wallFacesPreviewed.Add(submesh, new WallFace(face));
                face.SetPaintConfigId(paintBucket.GetPaintConfig().GetId());
                face.SetAdditionalColor(paintBucket.GetColor());
            }

            this.UpdateWallFaces();

            this.propsRenderer.SetupDefaultMaterials();
        }

        private bool IsAnExteriorFace(RaycastHit hitFace) {
            return !Physics.Raycast(hitFace.point, hitFace.normal + (Vector3.down * 5), 3, 1 << 9);
        }

        public void CheckExteriorWall() {
            Vector3 position = this.transform.position + Vector3.up;
            bool forwardRaycast = Physics.Raycast(position, this.transform.forward + (Vector3.down * 5), 3, 1 << 9);
            bool backwardRaycast = Physics.Raycast(position, -this.transform.forward + (Vector3.down * 5), 3, 1 << 9);

            this.SetExteriorWall(!(forwardRaycast && backwardRaycast), PhotonNetwork.LocalPlayer);
        }

        public bool IsExteriorWall() {
            return this.exteriorWall;
        }

        private void UpdateWallFaces() {
            Material[] sharedMaterials = this.renderer.sharedMaterials;

            for (int i = 0; i < this.wallFaces.Count; i++) {
                WallFace face = this.wallFaces[i];
                Material materialToApply = new Material(face.GetPaintConfig().GetMaterial());

                if (face.GetPaintConfig().AllowCustomColor()) {
                    materialToApply.color = face.GetAdditionalColor();
                }


                if (face.GetSharedMaterialIdx() >= sharedMaterials.Length) {
                    Debug.LogError($"An error occured during material apply on wall {this.name}");
                } else {
                    sharedMaterials[face.GetSharedMaterialIdx()] = materialToApply;
                }
            }

            this.renderer.sharedMaterials = sharedMaterials;
        }
    }
}