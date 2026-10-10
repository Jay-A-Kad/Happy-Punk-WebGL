using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renderer feature that applies a full-screen punk rock + cartoon post-process effect.
/// Add this to the URP Renderer asset and assign the PunkCartoon material.
///
/// Supports both the RenderGraph path (Unity 6 default) and the legacy
/// Compatibility Mode path so the effect works on WebGL builds, which may
/// not support the RenderGraph intermediate-texture pattern.
/// </summary>
public class PunkCartoonRendererFeature : ScriptableRendererFeature
{
    [Tooltip("Material using the PunkCartoon shader.")]
    public Material punkCartoonMaterial;

    [Tooltip("When in the frame this pass runs.")]
    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    private PunkCartoonPass _pass;

    public override void Create()
    {
        _pass = new PunkCartoonPass(renderPassEvent);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (punkCartoonMaterial == null)
            return;

        _pass.SetMaterial(punkCartoonMaterial);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }

    // ── Inner Pass ──────────────────────────────────────────────────────────────

    private class PunkCartoonPass : ScriptableRenderPass, System.IDisposable
    {
        private Material _material;
        private RTHandle _tempRT;

        // ── RenderGraph data containers ──────────────────────────────────────────

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }

        public PunkCartoonPass(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
            // Required for the Compatibility Mode Execute() path.
            requiresIntermediateTexture = true;
        }

        /// <summary>Provides the material to blit with each frame.</summary>
        public void SetMaterial(Material mat)
        {
            _material = mat;
        }

        // ── RenderGraph path (desktop / consoles) ────────────────────────────────

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // On WebGL, isActiveTargetBackBuffer is always true and there is no
            // intermediate texture available for a double-blit. Skip the RenderGraph
            // path here — the Execute() Compatibility Mode path handles WebGL.
            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;

            // Create a temp texture matching the camera target.
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples     = 1;
            desc.depthBufferBits = 0;

            TextureHandle temp = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, name: "_PunkCartoonTemp", clear: false);

            // Pass 1 — apply the punk effect: cameraColor → temp
            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<PassData>("PunkCartoon_Effect", out PassData effectData))
            {
                effectData.source   = cameraColor;
                effectData.material = _material;

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.SetRenderAttachment(temp, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture(Shader.PropertyToID("_BlitTexture"), data.source);
                    Blitter.BlitTexture(ctx.cmd, new Vector2(1f, 1f), data.material, 0);
                });
            }

            // Pass 2 — copy temp back into the camera color buffer (passthrough)
            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<CopyPassData>("PunkCartoon_CopyBack", out CopyPassData copyData))
            {
                copyData.source = temp;

                builder.UseTexture(temp, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0, false);
                });
            }
        }

        // ── Compatibility Mode / Legacy path (WebGL) ─────────────────────────────

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // Allocate a temp RT the same size as the camera target for the blit swap.
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples     = 1;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _tempRT, desc, name: "_PunkCartoonTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null)
                return;

            CameraData cameraData = renderingData.cameraData;
            if (cameraData.camera.cameraType != CameraType.Game)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("PunkCartoon");

            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            // Blit camera → temp with the punk effect, then copy back.
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, _tempRT, _material, 0);
            Blitter.BlitCameraTexture(cmd, _tempRT, cameraColorTarget);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Nothing to release per-camera; _tempRT is reused across frames.
        }

        public void Dispose()
        {
            _tempRT?.Release();
        }
    }
}
