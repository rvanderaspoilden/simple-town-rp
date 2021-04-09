using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Enums;
using Unity.Collections;
using UnityEngine;

namespace Sim.Building {
    public class Wall : Props {
        [Header("Wall settings")]
        [Tooltip("Represent all id allowed to be modified")]
        [SerializeField]
        private int[] allowedSharedMaterialIds;

        [ReadOnly]
        [SerializeField]
        private List<WallFace> wallFaces;

        [SerializeField]
        private bool exteriorWall;

        [SyncVar(hook = nameof(ParseWallFaces))]
        private string rawWallFaces;

        private new MeshRenderer renderer;
        private MeshCollider meshCollider;
        private Dictionary<int, WallFace> wallFacesPreviewed;
        private BoxCollider[] boxColliders;

        public override void OnStartClient() {
            base.OnStartClient();
            
            this.renderer = GetComponent<MeshRenderer>();
            this.meshCollider = GetComponent<MeshCollider>();
            this.boxColliders = GetComponents<BoxCollider>();

            this.wallFacesPreviewed = new Dictionary<int, WallFace>();

            this.EnableCollidersOfType(ColliderTypeEnum.BOX_COLLIDER);
            
            this.UpdateWallFaces();
        }

        [Server]
        public void Init(string faces) {
            this.rawWallFaces = faces;
        }

        [Client]
        public void Reset() {
            // rollback to wall face saved in wall face preview dictionnary
            foreach (KeyValuePair<int, WallFace> keyValuePair in this.wallFacesPreviewed) {
                this.wallFaces[keyValuePair.Key] = keyValuePair.Value;
            }

            this.wallFacesPreviewed.Clear();

            this.UpdateWallFaces();
            this.propsRenderer.SetupDefaultMaterials();
        }

        [Client]
        public void EnableCollidersOfType(ColliderTypeEnum type) {
            foreach (BoxCollider boxCollider in this.boxColliders) {
                boxCollider.enabled = type == ColliderTypeEnum.BOX_COLLIDER;
            }

            this.meshCollider.enabled = type == ColliderTypeEnum.MESH_COLLIDER;
        }

        public List<WallFace> GetWallFaces() {
            return this.wallFaces;
        }

        public void SetWallFaces(List<WallFace> faces) {
            this.CmdUpdateWallFaces(JsonHelper.ToJson(faces.ToArray()));
        }

        public void SetExteriorWall(bool value) {
            this.CmdSetExteriorWall(value);
        }

        [Command]
        public void CmdSetExteriorWall(bool value) {
            this.exteriorWall = value;
        }

        public void ApplyModification() {
            this.wallFacesPreviewed.Clear();

            this.SetWallFaces(this.wallFaces);
        }

        public bool IsPreview() {
            return this.wallFacesPreviewed.Count > 0;
        }

        [Command]
        public void CmdUpdateWallFaces(string faces) {
            this.rawWallFaces = faces;
        }

        [Client]
        private void ParseWallFaces(string oldFaces, string newFaces) {
            this.wallFaces = new List<WallFace>(JsonHelper.FromJson<WallFace>(newFaces));
            this.UpdateWallFaces();
            this.propsRenderer.SetupDefaultMaterials();
        }

        [Client]
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

        [Client]
        private bool IsAnExteriorFace(RaycastHit hitFace) {
            return !Physics.Raycast(hitFace.point, hitFace.normal + (Vector3.down * 5), 3, 1 << 9);
        }

        public void CheckExteriorWall() {
            Vector3 position = this.transform.position + Vector3.up;
            bool forwardRaycast = Physics.Raycast(position, this.transform.forward + (Vector3.down * 5), 3, 1 << 9);
            bool backwardRaycast = Physics.Raycast(position, -this.transform.forward + (Vector3.down * 5), 3, 1 << 9);
            
            this.SetExteriorWall(!(forwardRaycast && backwardRaycast));
        }

        public bool IsExteriorWall() {
            return this.exteriorWall;
        }

        private void UpdateWallFaces() {
            if (this.renderer == null) {
                this.renderer = GetComponent<MeshRenderer>();
            }
            
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