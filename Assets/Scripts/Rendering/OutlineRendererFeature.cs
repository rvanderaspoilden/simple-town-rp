using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Screen-space outline for objects on a chosen layer. Renders those objects into a
/// solid mask, then a fullscreen pass draws the outline colour along the mask's outer
/// edge. Mesh-agnostic → a clean, complete silhouette contour (no hard-edge gaps).
///
/// Driven by HoverOutline, which moves the hovered target's renderers onto the
/// outline layer for one hover.
///
/// Editor setup:
///   1. Create a layer named "Outline" (Project Settings ▸ Tags and Layers).
///   2. Add this feature to the active URP Renderer asset and set Outline Layer = Outline.
///   3. (Build) add Hidden/OutlineMask + Hidden/OutlineEdgeDetect to Always Included Shaders.
/// </summary>
public class OutlineRendererFeature : ScriptableRendererFeature {
    [System.Serializable]
    public class Settings {
        public LayerMask outlineLayer = 0;
        [ColorUsage(true, true)] public Color outlineColor = new Color(0.25f, 0.8f, 1f, 1f);
        [Range(1, 8)] public int thickness = 2;
        public bool usePulse = false;

        [Header("Moving gradient (mission highlight)")]
        [Tooltip("Anime une bande lumineuse qui balaie la silhouette pour attirer l'œil.")]
        public bool useGradient = false;
        [ColorUsage(true, true)] public Color gradientColor = new Color(1f, 1f, 1f, 1f);
        public float gradientSpeed = 2f;
        public float gradientFrequency = 6f;

        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private Material _maskMaterial;
    private Material _edgeMaterial;
    private OutlinePass _pass;

    public override void Create() {
        if (_maskMaterial == null) {
            Shader s = Shader.Find("Hidden/OutlineMask");
            if (s != null) _maskMaterial = CoreUtils.CreateEngineMaterial(s);
        }
        if (_edgeMaterial == null) {
            Shader s = Shader.Find("Hidden/OutlineEdgeDetect");
            if (s != null) _edgeMaterial = CoreUtils.CreateEngineMaterial(s);
        }
        _pass = new OutlinePass(settings, _maskMaterial, _edgeMaterial) {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (_maskMaterial == null || _edgeMaterial == null) return;
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) {
        CoreUtils.Destroy(_maskMaterial);
        CoreUtils.Destroy(_edgeMaterial);
    }

    // ── Pass ────────────────────────────────────────────────────────────────────

    private class OutlinePass : ScriptableRenderPass {
        private static readonly int MaskTexId    = Shader.PropertyToID("_OutlineMaskTex");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int ThicknessId  = Shader.PropertyToID("_Thickness");
        private static readonly int UseGradientId    = Shader.PropertyToID("_UseGradient");
        private static readonly int GradientColorId  = Shader.PropertyToID("_GradientColor");
        private static readonly int GradientSpeedId  = Shader.PropertyToID("_GradientSpeed");
        private static readonly int GradientFreqId   = Shader.PropertyToID("_GradientFrequency");

        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId> {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("LightweightForward"),
        };

        private readonly Settings _settings;
        private readonly Material _maskMaterial;
        private readonly Material _edgeMaterial;

        public OutlinePass(Settings settings, Material maskMaterial, Material edgeMaterial) {
            _settings = settings;
            _maskMaterial = maskMaterial;
            _edgeMaterial = edgeMaterial;
        }

        private class MaskPassData     { public RendererListHandle rendererList; }
        private class CopyPassData     { public TextureHandle source; }
        private class CompositePassData { public TextureHandle source; public TextureHandle mask; public Material material; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            var resourceData  = frameData.Get<UniversalResourceData>();
            var cameraData    = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData     = frameData.Get<UniversalLightData>();

            if (resourceData.isActiveTargetBackBuffer) return;

            var camDesc = cameraData.cameraTargetDescriptor;

            // Mask texture. R = silhouette complète du prop, G = visibilité (0 si occludé).
            var maskDesc = new TextureDesc(camDesc.width, camDesc.height) {
                colorFormat     = GraphicsFormat.R8G8_UNorm,
                name            = "OutlineMask",
                clearBuffer     = true,
                clearColor      = Color.clear,
                depthBufferBits = DepthBits.None,
                msaaSamples     = MSAASamples.None,
            };
            TextureHandle maskTex = renderGraph.CreateTexture(maskDesc);

            // 1. Render outline-layer objects into the mask.
            var drawSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            drawSettings.overrideMaterial = _maskMaterial;
            drawSettings.overrideMaterialPassIndex = 0;
            var filter = new FilteringSettings(RenderQueueRange.all, _settings.outlineLayer);
            var rlParams = new RendererListParams(renderingData.cullResults, drawSettings, filter);
            RendererListHandle rlHandle = renderGraph.CreateRendererList(rlParams);

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Mask", out var passData)) {
                passData.rendererList = rlHandle;
                builder.UseRendererList(rlHandle);
                builder.SetRenderAttachment(maskTex, 0);
                // Le mask shader échantillonne _CameraDepthTexture pour écarter les fragments
                // occludés (ex. derrière le perso local). On déclare la dépendance pour que
                // RenderGraph garde la depth texture vivante et liée comme global.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((MaskPassData d, RasterGraphContext ctx) =>
                    ctx.cmd.DrawRendererList(d.rendererList));
            }

            // 2. Copy camera colour → temp (so the composite can read it while writing back).
            var tempDesc = new TextureDesc(camDesc.width, camDesc.height) {
                colorFormat     = camDesc.graphicsFormat,
                name            = "OutlineSceneCopy",
                depthBufferBits = DepthBits.None,
                msaaSamples     = MSAASamples.None,
            };
            TextureHandle tempColor = renderGraph.CreateTexture(tempDesc);
            TextureHandle cameraColor = resourceData.activeColorTexture;

            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Outline Copy", out var passData)) {
                passData.source = cameraColor;
                builder.UseTexture(cameraColor);
                builder.SetRenderAttachment(tempColor, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CopyPassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
            }

            // 3. Composite: edge-detect the mask, draw the outline over the camera colour.
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Outline Composite", out var passData)) {
                passData.source = tempColor;
                passData.mask = maskTex;
                passData.material = _edgeMaterial;
                builder.UseTexture(tempColor);
                builder.UseTexture(maskTex);
                builder.SetRenderAttachment(cameraColor, 0);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true); // required: we SetGlobalTexture below
                builder.SetRenderFunc((CompositePassData d, RasterGraphContext ctx) => {
                    float pulse = _settings.usePulse ? Shader.GetGlobalFloat("_MissionOutlinePulse") : 1.0f;
                    if (pulse <= 0.01f) pulse = 1.0f; // Handle uninitialized or zero

                    d.material.SetColor(OutlineColorId, _settings.outlineColor);
                    d.material.SetFloat(ThicknessId, _settings.thickness * pulse);
                    d.material.SetFloat(UseGradientId, _settings.useGradient ? 1f : 0f);
                    d.material.SetColor(GradientColorId, _settings.gradientColor);
                    d.material.SetFloat(GradientSpeedId, _settings.gradientSpeed);
                    d.material.SetFloat(GradientFreqId, _settings.gradientFrequency);
                    ctx.cmd.SetGlobalTexture(MaskTexId, d.mask);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
        }
    }
}
