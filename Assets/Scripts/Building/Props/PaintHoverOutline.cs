using UnityEngine;

namespace Sim.Building {
    /// <summary>
    /// Lightweight child proxy that drives the white "Paint Hover" outline around a hovered
    /// wall face or ground while painting. Carries a copy of the host's geometry (full mesh
    /// for grounds, single submesh for walls) on the "Paint Hover" layer with a transparent
    /// material so it never draws in the regular pass — only the OutlineRendererFeature's
    /// mask pass picks it up and traces a screen-space silhouette around it.
    /// </summary>
    public class PaintHoverOutline : MonoBehaviour {
        public const string LayerName = "Paint Hover";

        private Mesh _ownedMesh;

        /// <summary>Build a proxy around the host's full mesh (ground) or a single submesh (wall).</summary>
        public static PaintHoverOutline Build(Transform host, Mesh srcMesh, int submeshIndex = -1) {
            if (host == null || srcMesh == null) return null;
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0) return null;
            Material transparent = DatabaseManager.Instance != null ? DatabaseManager.Instance.GetTransparentMaterial() : null;
            if (transparent == null) return null;

            Mesh proxyMesh = new Mesh { name = "PaintHoverProxyMesh" };
            Vector3[] verts = srcMesh.vertices;
            proxyMesh.vertices = verts;
            if (srcMesh.normals != null && srcMesh.normals.Length == verts.Length) proxyMesh.normals = srcMesh.normals;
            if (srcMesh.uv      != null && srcMesh.uv.Length      == verts.Length) proxyMesh.uv      = srcMesh.uv;
            int[] tris = (submeshIndex >= 0 && submeshIndex < srcMesh.subMeshCount)
                ? srcMesh.GetTriangles(submeshIndex)
                : srcMesh.triangles;
            proxyMesh.triangles = tris;
            proxyMesh.RecalculateBounds();

            GameObject go = new GameObject("PaintHoverProxy");
            go.transform.SetParent(host, false);
            go.layer = layer;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = proxyMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = transparent;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;

            PaintHoverOutline proxy = go.AddComponent<PaintHoverOutline>();
            proxy._ownedMesh = proxyMesh;
            return proxy;
        }

        public void Dispose() {
            if (_ownedMesh != null) Destroy(_ownedMesh);
            _ownedMesh = null;
            if (this != null && this.gameObject != null) Destroy(this.gameObject);
        }

        private void OnDestroy() {
            if (_ownedMesh != null) Destroy(_ownedMesh);
        }
    }
}
