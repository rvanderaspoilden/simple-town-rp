using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fullscreen "interior atmosphere" pass. While the local player is inside a building,
/// it blends every pixel whose reconstructed world position falls outside the current
/// room's bounds (and the sky) toward a cosy backdrop colour, sealing the interior from
/// the exterior city. All parameters arrive as globals published by <c>InteriorAtmosphere</c>;
/// when <c>_InteriorBlend</c> is ~0 the pass does no work, so the open city pays nothing.
///
/// Mirrors <see cref="OutlineRendererFeature"/>: a RenderGraph raster pass that copies the
/// camera colour to a temp (read-while-write), then composites the fog back over it, with a
/// declared dependency on the camera depth texture (bound as the global _CameraDepthTexture).
///
/// Editor setup:
///   1. Add this feature to the active URP Renderer asset (Assets/Settings/ForwardRenderer.asset).
///   2. (Build) add Hidden/InteriorFog to Always Included Shaders.
/// </summary>
public class InteriorFogFeature : ScriptableRendererFeature {
    [System.Serializable]
    public class Settings {
        // After skybox + opaque + transparents so scenery, dynamic actors AND the sky are all
        // covered; before post so tonemapping/bloom still apply to the fogged result.
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private Material _fogMaterial;
    private InteriorFogPass _pass;

    private static readonly int BlendId = Shader.PropertyToID("_InteriorBlend");

    public override void Create() {
        if (_fogMaterial == null) {
            Shader s = Shader.Find("Hidden/InteriorFog");
            if (s != null) _fogMaterial = CoreUtils.CreateEngineMaterial(s);
        }
        _pass = new InteriorFogPass(_fogMaterial) {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (_fogMaterial == null) return;
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;
        // Cheap CPU-side gate: nothing to do unless a client is inside a building.
        if (Shader.GetGlobalFloat(BlendId) < 0.01f) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) {
        CoreUtils.Destroy(_fogMaterial);
    }

    // ── Pass ────────────────────────────────────────────────────────────────────

    private class InteriorFogPass : ScriptableRenderPass {
        private readonly Material _material;

        public InteriorFogPass(Material material) {
            _material = material;
        }

        private class CopyPassData      { public TextureHandle source; }
        private class CompositePassData { public TextureHandle source; public Material material; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData   = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer) return;

            var camDesc = cameraData.cameraTargetDescriptor;
            TextureHandle cameraColor = resourceData.activeColorTexture;

            // 1. Copy camera colour → temp, so the composite can read it while writing back.
            var tempDesc = new TextureDesc(camDesc.width, camDesc.height) {
                colorFormat     = camDesc.graphicsFormat,
                name            = "InteriorFogSceneCopy",
                depthBufferBits = DepthBits.None,
                msaaSamples     = MSAASamples.None,
            };
            TextureHandle tempColor = renderGraph.CreateTexture(tempDesc);

            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Interior Fog Copy", out var passData)) {
                passData.source = cameraColor;
                builder.UseTexture(cameraColor);
                builder.SetRenderAttachment(tempColor, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CopyPassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
            }

            // 2. Composite: reconstruct world pos from depth, fog the exterior, write back.
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Interior Fog Composite", out var passData)) {
                passData.source = tempColor;
                passData.material = _material;
                builder.UseTexture(tempColor);
                // The fog shader samples _CameraDepthTexture; declare the dependency so
                // RenderGraph keeps it alive and bound as a global for this pass.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CompositePassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
            }
        }
    }
}
