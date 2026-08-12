using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Single-pass screen space outline effect for URP, following Roystan's outline shader
// (https://roystan.net/articles/outline-shader/), rewritten for RecordRenderGraph since this
// project runs with Compatibility Mode disabled (the old Configure/Execute/ConfigureTarget API is
// deprecated and does nothing there).
// ScreenSpaceOutlinePass (Hidden/RoystanOutline) reads only the camera depth texture to do the
// Roberts-cross edge detection and composites the outline over the scene — there used to be a
// second pass here (ViewSpaceNormalsTexturePass) that redrew every opaque object into a
// view-space-normals texture for a normal-based edge test and grazing-angle correction; it has
// been removed so the effect no longer depends on per-object normals at all (and no longer
// restricts which layers get outlined the way that pass's mask did — every opaque object in the
// camera depth texture can now show an edge). The depth threshold is instead scaled by camera
// distance (see OutlineSettings.thresholdFunction below).
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

  private ScreenSpaceOutlinePass m_ScreenSpaceOutlinePass;

  public override void Create() {
    m_ScreenSpaceOutlinePass = new ScreenSpaceOutlinePass(renderPassEvent, outlineSettings, outlineMaterial);
    // Calls GetTextureDesc(resourceData.activeColorTexture), which throws ("does not have a valid
    // descriptor... system back buffer") whenever URP decides it can render straight to the
    // backbuffer instead of an intermediate texture (e.g. no active post-processing on the
    // camera). Declaring Color input here forces URP to always allocate a real intermediate color
    // texture for any camera this feature runs on, so activeColorTexture is never the raw backbuffer.
    m_ScreenSpaceOutlinePass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
  }

  public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
    if (outlineMaterial == null) return;
    if (renderingData.cameraData.cameraType == CameraType.Preview
        || renderingData.cameraData.cameraType == CameraType.Reflection) return;

    renderer.EnqueuePass(m_ScreenSpaceOutlinePass);
  }

  // Reads the camera depth texture, runs the Roberts cross edge detection (Hidden/RoystanOutline),
  // and composites the outline over the scene.
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
      public OutlineSettings settings;
    }

    private static void Execute(RasterCommandBuffer cmd, RTHandle colorCopy, OutlineSettings settings, Material material) {
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
      s_PropertyBlock.SetFloat(ThresholdFunctionId, (float)settings.thresholdFunction);
      s_PropertyBlock.SetFloat(ThresholdNearDistanceId, settings.thresholdNearDistance);
      s_PropertyBlock.SetFloat(ThresholdFarDistanceId, settings.thresholdFarDistance);
      s_PropertyBlock.SetFloat(ThresholdMultiplierNearId, settings.thresholdMultiplierNear);
      s_PropertyBlock.SetFloat(ThresholdMultiplierFarId, settings.thresholdMultiplierFar);
      s_PropertyBlock.SetFloat(ThresholdExponentId, settings.thresholdExponent);
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (outlineMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

      var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      colorDesc.msaaSamples = MSAASamples.None;
      colorDesc.clearBuffer = false;
      colorDesc.name = "_OutlineColorCopy";
      TextureHandle colorCopy = renderGraph.CreateTexture(colorDesc);
      renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: "Outline Copy Color");

      using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Space Outline", out var passData, profilingSampler)) {
        passData.material = outlineMaterial;
        passData.colorCopy = colorCopy;
        passData.settings = settings;

        builder.UseTexture(colorCopy);
        if (resourceData.cameraDepthTexture.IsValid())
          builder.UseTexture(resourceData.cameraDepthTexture);

        builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
          Execute(ctx.cmd, data.colorCopy, data.settings, data.material));
      }
    }
  }
}
