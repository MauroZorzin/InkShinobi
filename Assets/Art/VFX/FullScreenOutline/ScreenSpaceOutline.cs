using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Renderer Feature URP per l'outline a schermo intero, basata su Roystan's outline shader
// (https://roystan.net/articles/outline-shader/).
public class ScreenSpaceOutline : ScriptableRendererFeature {
  [SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

  public enum ThresholdFunction {
    Linear,
    Exponential,
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

    [Tooltip("Base depth threshold, multiplied per-pixel by the distance function below.")]
    public float depthThreshold = 1.5f;
    [Tooltip("Linear: multiplier ramps evenly between near/far distance. Exponential: ramp is reshaped by Threshold Curve Exponent (ease-in above 1, ease-out below 1).")]
    public ThresholdFunction thresholdFunction = ThresholdFunction.Linear;
    public float thresholdNearDistance = 5f;
    public float thresholdFarDistance = 40f;
    [Tooltip("Depth threshold multiplier for pixels at or nearer than Threshold Near Distance.")]
    public float thresholdMultiplierNear = 1f;
    [Tooltip("Depth threshold multiplier for pixels at or farther than Threshold Far Distance.")]
    public float thresholdMultiplierFar = 4f;
    [Tooltip("Only used when Threshold Function is Exponential.")]
    public float thresholdExponent = 2.5f;
  }

  [SerializeField] private OutlineSettings outlineSettings = new OutlineSettings();

  [Tooltip("Material using Hidden/RoystanOutline.")]
  [SerializeField] private Material outlineMaterial;

  [Tooltip("Objects on these layers never receive an outline, regardless of depth edges.")]
  [SerializeField] private LayerMask excludeLayers;

  private ScreenSpaceOutlinePass m_ScreenSpaceOutlinePass;

  public override void Create() {
    m_ScreenSpaceOutlinePass = new ScreenSpaceOutlinePass(renderPassEvent, outlineSettings, outlineMaterial, excludeLayers);
    // Senza questo, se la camera non ha altro post-processing attivo URP a volte disegna
    // direttamente sul backbuffer invece che su una texture intermedia e il pass va in errore
    // perche' non trova una texture su cui lavorare. Forziamo sempre una texture intermedia.
    m_ScreenSpaceOutlinePass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
  }

  public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
    if (outlineMaterial == null) return;
    if (renderingData.cameraData.cameraType == CameraType.Preview
        || renderingData.cameraData.cameraType == CameraType.Reflection) return;

    renderer.EnqueuePass(m_ScreenSpaceOutlinePass);
  }

  // Un solo pass: legge la depth texture della camera, ci fa sopra il test dei bordi
  // (Hidden/RoystanOutline) e ridisegna la scena con l'outline sovrapposto
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
    private static readonly int ThresholdFunctionId = Shader.PropertyToID("_ThresholdFunction");
    private static readonly int ThresholdNearDistanceId = Shader.PropertyToID("_ThresholdNearDistance");
    private static readonly int ThresholdFarDistanceId = Shader.PropertyToID("_ThresholdFarDistance");
    private static readonly int ThresholdMultiplierNearId = Shader.PropertyToID("_ThresholdMultiplierNear");
    private static readonly int ThresholdMultiplierFarId = Shader.PropertyToID("_ThresholdMultiplierFar");
    private static readonly int ThresholdExponentId = Shader.PropertyToID("_ThresholdExponent");
    private static readonly int ExcludedLayerDepthTextureId = Shader.PropertyToID("_ExcludedLayerDepthTexture");
    private static readonly Vector4 FullScaleBias = new Vector4(1, 1, 0, 0);
    private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();
    private static readonly List<ShaderTagId> ExcludedLayerShaderTags = new() { new ShaderTagId("DepthOnly") };

    private readonly OutlineSettings settings;
    private readonly Material outlineMaterial;
    private readonly LayerMask excludeLayers;

    public ScreenSpaceOutlinePass(RenderPassEvent renderPassEvent, OutlineSettings settings, Material outlineMaterial, LayerMask excludeLayers) {
      this.renderPassEvent = renderPassEvent;
      this.settings = settings;
      this.outlineMaterial = outlineMaterial;
      this.excludeLayers = excludeLayers;
      profilingSampler = new ProfilingSampler("Screen Space Outline");
    }

    private class PassData {
      public Material material;
      public TextureHandle colorCopy;
      public TextureHandle excludedDepth;
      public OutlineSettings settings;
    }

    private class ExcludedLayerDepthPassData {
      public RendererListHandle rendererList;
    }

    private static void Execute(RasterCommandBuffer cmd, RTHandle colorCopy, RTHandle excludedDepth, OutlineSettings settings, Material material) {
      s_PropertyBlock.Clear();
      s_PropertyBlock.SetTexture(BlitTextureId, colorCopy);
      s_PropertyBlock.SetTexture(ExcludedLayerDepthTextureId, excludedDepth);
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
      s_PropertyBlock.SetFloat(ThresholdFunctionId, (float)settings.thresholdFunction);
      s_PropertyBlock.SetFloat(ThresholdNearDistanceId, settings.thresholdNearDistance);
      s_PropertyBlock.SetFloat(ThresholdFarDistanceId, settings.thresholdFarDistance);
      s_PropertyBlock.SetFloat(ThresholdMultiplierNearId, settings.thresholdMultiplierNear);
      s_PropertyBlock.SetFloat(ThresholdMultiplierFarId, settings.thresholdMultiplierFar);
      s_PropertyBlock.SetFloat(ThresholdExponentId, settings.thresholdExponent);
      // DrawProcedural con 3 vertici e nessuna mesh e' un qualche trucco
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (outlineMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

      // Cpio colore scena, non si puo scrivere e leggere la stessa texture nello stesso pass, quindi faccio un blit su una copia
      var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      colorDesc.msaaSamples = MSAASamples.None;
      colorDesc.clearBuffer = false;
      colorDesc.name = "_OutlineColorCopy";
      TextureHandle colorCopy = renderGraph.CreateTexture(colorDesc);
      // AddBlitPass copia una texture nell'altra per conto nostro (Vector2.one = scale, Vector2.zero
      // = bias, cioe' "copia tutto 1:1, senza ritagliare o spostare nulla").
      renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: "Outline Copy Color");

      UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
      UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
      UniversalLightData lightData = frameData.Get<UniversalLightData>();

      var excludedDepthDesc = renderGraph.GetTextureDesc(resourceData.activeDepthTexture);
      excludedDepthDesc.clearBuffer = false;
      excludedDepthDesc.name = "_ExcludedLayerDepth";
      TextureHandle excludedDepth = renderGraph.CreateTexture(excludedDepthDesc);

      FilteringSettings excludedFiltering = new(RenderQueueRange.opaque) { layerMask = excludeLayers.value };
      DrawingSettings excludedDrawing = RenderingUtils.CreateDrawingSettings(
        ExcludedLayerShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
      NativeArray<ShaderTagId> excludedTagValues = new(1, Allocator.Temp);
      excludedTagValues[0] = ShaderTagId.none;
      NativeArray<RenderStateBlock> excludedStateBlocks = new(1, Allocator.Temp);
      excludedStateBlocks[0] = new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = new RenderTargetBlendState(writeMask: (ColorWriteMask)0) },
        depthState = new DepthState(true, CompareFunction.LessEqual)
      };
      RendererListParams excludedListParams = new(renderingData.cullResults, excludedDrawing, excludedFiltering) {
        tagValues = excludedTagValues,
        stateBlocks = excludedStateBlocks,
        isPassTagName = false
      };
      RendererListHandle excludedRendererList = renderGraph.CreateRendererList(excludedListParams);

      using (var builder = renderGraph.AddRasterRenderPass<ExcludedLayerDepthPassData>("Outline Excluded Layer Depth", out var excludedPassData, profilingSampler)) {
        excludedPassData.rendererList = excludedRendererList;
        builder.UseRendererList(excludedPassData.rendererList);
        builder.SetRenderAttachmentDepth(excludedDepth, AccessFlags.Write);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (ExcludedLayerDepthPassData data, RasterGraphContext ctx) => {
          ctx.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
          ctx.cmd.DrawRendererList(data.rendererList);
        });
      }

      // AddRasterRenderPass apre un nuovo pass nel grafo: il builder che restituisce serve a
      // dichiarare in anticipo quali texture legge/scrive
      using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Space Outline", out var passData, profilingSampler)) {
        passData.material = outlineMaterial;
        passData.colorCopy = colorCopy;
        passData.excludedDepth = excludedDepth;
        passData.settings = settings;

        // UseTexture dichiara "questo pass legge da qui"
        builder.UseTexture(colorCopy);
        builder.UseTexture(excludedDepth);
        if (resourceData.cameraDepthTexture.IsValid())
          builder.UseTexture(resourceData.cameraDepthTexture);

        // SetRenderAttachment invece e' il target su cui si scrive davvero
        builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
          Execute(ctx.cmd, data.colorCopy, data.excludedDepth, data.settings, data.material));
      }
    }
  }
}
