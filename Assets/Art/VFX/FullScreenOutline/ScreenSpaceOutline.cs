using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Two-pass screen space outline effect for URP, following Roystan's outline shader
// (https://roystan.net/articles/outline-shader/), rewritten for RecordRenderGraph since this
// project runs with Compatibility Mode disabled (the old Configure/Execute/ConfigureTarget API is
// deprecated and does nothing there).
//  1. ViewSpaceNormalsTexturePass redraws every opaque object with an override material
//     (Hidden/ViewSpaceNormals) that outputs its normal in view space instead of its usual color.
//  2. ScreenSpaceOutlinePass (Hidden/RoystanOutline) reads that texture plus the camera depth
//     texture to do the actual Roberts-cross edge detection and composites the outline over the
//     scene. _ClipToView (inverse GPU projection matrix) is set here every frame so the shader can
//     reconstruct a view-space ray per pixel for the grazing-angle correction.
public class ScreenSpaceOutline : ScriptableRendererFeature {
  [SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

  [Serializable]
  public class ViewSpaceNormalsTextureSettings {
    // Renamed from "colorFormat" (was RenderTextureFormat) — keeping the old field name here with
    // a new type let Unity's serializer reinterpret the old stored value (RenderTextureFormat.ARGB32
    // == 0) as the new enum's raw int, landing on GraphicsFormat.None (also 0) instead of the
    // default below, which is exactly what caused "texture has no format".
    public GraphicsFormat normalsColorFormat = GraphicsFormat.R8G8B8A8_UNorm;
    public FilterMode filterMode = FilterMode.Point;
  }

  [Serializable]
  public class OutlineSettings {
    public Color color = Color.black;
    public Color farColor = new Color(0, 0, 0, 0);
    public float fadeStartDistance = 15f;
    public float fadeEndDistance = 30f;
    public float scale = 1f;
    public float thicknessReferenceDistance = 10f;
    public float minScale = 0.5f;
    public float maxScale = 10f;
    public float depthThreshold = 1.5f;
    [Range(0, 1)] public float normalThreshold = 0.4f;
    [Range(0, 1)] public float depthNormalThreshold = 0.5f;
    public float depthNormalThresholdScale = 7f;
  }

  [SerializeField] private ViewSpaceNormalsTextureSettings viewSpaceNormalsTextureSettings = new ViewSpaceNormalsTextureSettings();
  [SerializeField] private OutlineSettings outlineSettings = new OutlineSettings();

  [Tooltip("Material using Hidden/ViewSpaceNormals.")]
  [SerializeField] private Material normalsMaterial;

  [Tooltip("Material using Hidden/RoystanOutline.")]
  [SerializeField] private Material outlineMaterial;

  private ViewSpaceNormalsTexturePass m_ViewSpaceNormalsTexturePass;
  private ScreenSpaceOutlinePass m_ScreenSpaceOutlinePass;

  public override void Create() {
    m_ViewSpaceNormalsTexturePass = new ViewSpaceNormalsTexturePass(renderPassEvent, viewSpaceNormalsTextureSettings, normalsMaterial);
    m_ScreenSpaceOutlinePass = new ScreenSpaceOutlinePass(renderPassEvent, outlineSettings, outlineMaterial);
    m_ScreenSpaceOutlinePass.ConfigureInput(ScriptableRenderPassInput.Depth);
  }

  public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
    if (normalsMaterial == null || outlineMaterial == null) return;
    if (renderingData.cameraData.cameraType == CameraType.Preview
        || renderingData.cameraData.cameraType == CameraType.Reflection) return;

    renderer.EnqueuePass(m_ViewSpaceNormalsTexturePass);
    renderer.EnqueuePass(m_ScreenSpaceOutlinePass);
  }

  // This pass redraws the scene's opaque objects with an override material that outputs their
  // normal in view space, into a dedicated texture the outline pass reads afterwards.
  private class ViewSpaceNormalsTexturePass : ScriptableRenderPass {
    private static readonly int NormalsTextureId = Shader.PropertyToID("_ScreenViewSpaceNormals");

    private readonly ViewSpaceNormalsTextureSettings settings;
    private readonly Material normalsMaterial;
    private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId> {
      new ShaderTagId("UniversalForward"),
      new ShaderTagId("UniversalForwardOnly"),
      new ShaderTagId("SRPDefaultUnlit"),
    };

    public ViewSpaceNormalsTexturePass(RenderPassEvent renderPassEvent, ViewSpaceNormalsTextureSettings settings, Material normalsMaterial) {
      this.renderPassEvent = renderPassEvent;
      this.settings = settings;
      this.normalsMaterial = normalsMaterial;
      profilingSampler = new ProfilingSampler("View Space Normals");
    }

    private class PassData {
      public RendererListHandle rendererListHandle;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (normalsMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
      UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
      UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
      UniversalLightData lightData = frameData.Get<UniversalLightData>();

      // Based on the existing camera color texture (not cameraTargetDescriptor, which mixes color
      // and depth-stencil format together and would silently pick the wrong one — see the note in
      // OutlineRendererFeature.cs for the exact failure this caused there).
      // msaaSamples deliberately left as inherited from activeColorTexture (not forced to None) —
      // this pass binds the camera's real depth buffer alongside this texture as attachments in
      // the SAME native pass, and all attachments in a pass must share one MSAA sample count.
      // Forcing None here while the depth buffer uses MSAA (e.g. 4x from quality settings) throws
      // "Mismatch in number of MSAA samples" at graph compile time.
      var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      desc.format = settings.normalsColorFormat;
      desc.filterMode = settings.filterMode;
      desc.name = "_ScreenViewSpaceNormals";
      desc.clearBuffer = true;
      desc.clearColor = Color.black;
      TextureHandle normalsTexture = renderGraph.CreateTexture(desc);

      using (var builder = renderGraph.AddRasterRenderPass<PassData>("View Space Normals", out var passData, profilingSampler)) {
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, cameraData.camera.cullingMask);
        var drawingSettings = RenderingUtils.CreateDrawingSettings(shaderTagIds, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
        drawingSettings.overrideMaterial = normalsMaterial;

        var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
        passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);
        builder.UseRendererList(passData.rendererListHandle);

        builder.SetRenderAttachment(normalsTexture, 0, AccessFlags.Write);
        // Without a depth attachment, this redraw has nothing to depth-test against — ZTest in
        // ViewSpaceNormals.shader is a no-op with no bound depth buffer, so whichever object
        // happened to be drawn LAST at a given pixel wins, regardless of which one is actually in
        // front. Binding the camera's real depth buffer (already fully populated, since this pass
        // runs AfterRenderingOpaques) makes occluded objects correctly fail the depth test and never
        // overwrite the visible surface's normal. Read-only: depth is already correct, the shader
        // only needs to test against it (ZWrite Off), not write it again.
        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
        // Makes _ScreenViewSpaceNormals available as a global texture to passes enqueued after
        // this one (the outline pass), without having to thread the TextureHandle through manually.
        builder.SetGlobalTextureAfterPass(normalsTexture, NormalsTextureId);

        builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {
          ctx.cmd.DrawRendererList(data.rendererListHandle);
        });
      }
    }
  }

  // Reads the view-space normals texture above plus the camera depth texture, runs the Roberts
  // cross edge detection (Hidden/RoystanOutline), and composites the outline over the scene.
  private class ScreenSpaceOutlinePass : ScriptableRenderPass {
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FarColorId = Shader.PropertyToID("_FarColor");
    private static readonly int FadeStartDistanceId = Shader.PropertyToID("_FadeStartDistance");
    private static readonly int FadeEndDistanceId = Shader.PropertyToID("_FadeEndDistance");
    private static readonly int ScaleId = Shader.PropertyToID("_Scale");
    private static readonly int ThicknessReferenceDistanceId = Shader.PropertyToID("_ThicknessReferenceDistance");
    private static readonly int MinScaleId = Shader.PropertyToID("_MinScale");
    private static readonly int MaxScaleId = Shader.PropertyToID("_MaxScale");
    private static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    private static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    private static readonly int DepthNormalThresholdId = Shader.PropertyToID("_DepthNormalThreshold");
    private static readonly int DepthNormalThresholdScaleId = Shader.PropertyToID("_DepthNormalThresholdScale");
    private static readonly int ClipToViewId = Shader.PropertyToID("_ClipToView");
    private static readonly Vector4 FullScaleBias = new Vector4(1, 1, 0, 0);
    private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

    private readonly OutlineSettings settings;
    private readonly Material outlineMaterial;

    public ScreenSpaceOutlinePass(RenderPassEvent renderPassEvent, OutlineSettings settings, Material outlineMaterial) {
      this.renderPassEvent = renderPassEvent;
      this.settings = settings;
      this.outlineMaterial = outlineMaterial;
      profilingSampler = new ProfilingSampler("Screen Space Outline");
    }

    private class PassData {
      public Material material;
      public TextureHandle colorCopy;
      public Matrix4x4 clipToView;
      public OutlineSettings settings;
    }

    private static void Execute(RasterCommandBuffer cmd, RTHandle colorCopy, Matrix4x4 clipToView, OutlineSettings settings, Material material) {
      s_PropertyBlock.Clear();
      s_PropertyBlock.SetTexture(BlitTextureId, colorCopy);
      s_PropertyBlock.SetVector(BlitScaleBiasId, FullScaleBias);
      s_PropertyBlock.SetColor(ColorId, settings.color);
      s_PropertyBlock.SetColor(FarColorId, settings.farColor);
      s_PropertyBlock.SetFloat(FadeStartDistanceId, settings.fadeStartDistance);
      s_PropertyBlock.SetFloat(FadeEndDistanceId, settings.fadeEndDistance);
      s_PropertyBlock.SetFloat(ScaleId, settings.scale);
      s_PropertyBlock.SetFloat(ThicknessReferenceDistanceId, settings.thicknessReferenceDistance);
      s_PropertyBlock.SetFloat(MinScaleId, settings.minScale);
      s_PropertyBlock.SetFloat(MaxScaleId, settings.maxScale);
      s_PropertyBlock.SetFloat(DepthThresholdId, settings.depthThreshold);
      s_PropertyBlock.SetFloat(NormalThresholdId, settings.normalThreshold);
      s_PropertyBlock.SetFloat(DepthNormalThresholdId, settings.depthNormalThreshold);
      s_PropertyBlock.SetFloat(DepthNormalThresholdScaleId, settings.depthNormalThresholdScale);
      s_PropertyBlock.SetMatrix(ClipToViewId, clipToView);
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (outlineMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
      UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

      var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      colorDesc.msaaSamples = MSAASamples.None;
      colorDesc.clearBuffer = false;
      colorDesc.name = "_OutlineColorCopy";
      TextureHandle colorCopy = renderGraph.CreateTexture(colorDesc);
      renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: "Outline Copy Color");

      // Clip-to-view = inverse of the GPU-space projection matrix (GL.GetGPUProjectionMatrix
      // applies the same platform-specific adjustments URP's own matrices use), letting the
      // shader reconstruct a view-space ray through each pixel for the grazing-angle correction.
      Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false);
      Matrix4x4 clipToView = gpuProjection.inverse;

      using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Space Outline", out var passData, profilingSampler)) {
        passData.material = outlineMaterial;
        passData.colorCopy = colorCopy;
        passData.clipToView = clipToView;
        passData.settings = settings;

        builder.UseTexture(colorCopy);
        if (resourceData.cameraDepthTexture.IsValid())
          builder.UseTexture(resourceData.cameraDepthTexture);

        builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
          Execute(ctx.cmd, data.colorCopy, data.clipToView, data.settings, data.material));
      }
    }
  }
}
