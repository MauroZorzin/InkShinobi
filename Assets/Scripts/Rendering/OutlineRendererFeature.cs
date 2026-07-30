using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Two-pass screen-space outline. Sampling an expensive multi-tap depth/normal edge test at many
/// points per output pixel (the previous single-shader approach) behaves like a binary "found an
/// edge within range" disc instead of a smoothly graduated stroke, and leaves gaps between samples
/// on thin edges at larger radii — because the sample count is fixed but the ring they're spread
/// over keeps growing. Splitting detection from stroke width into two real passes fixes both:
///  1. Edge pass (OutlineEdgeMask.shader): precise depth+normal Roberts-cross test, evaluated
///     exactly ONCE per pixel, thresholded, written into a small single-channel mask texture.
///  2. Copy pass: camera color -> a readable copy (a pass can't read the same target it writes to).
///  3. Composite pass (OutlineComposite.shader): dilates the CHEAP mask (a full small grid of
///     samples, not just a ring — no gaps) with a depth-scaled radius and a distance-based falloff
///     (a real graduated stroke, not on/off), then blends the outline color over the copied scene
///     color and writes the result back into the camera color target.
/// </summary>
public class OutlineRendererFeature : ScriptableRendererFeature {
  [System.Serializable]
  public class Settings {
    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    [Tooltip("Material using Hidden/OutlineEdgeMask — writes the precise, single-texel edge mask.")]
    public Material edgeMaterial;

    [Tooltip("Material using Hidden/OutlineComposite — dilates the mask and blends it over the scene.")]
    public Material compositeMaterial;
  }

  public Settings settings = new Settings();
  private OutlinePass _pass;

  public override void Create() {
    _pass = new OutlinePass();
  }

  public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
    if (settings.edgeMaterial == null || settings.compositeMaterial == null) return;
    if (renderingData.cameraData.cameraType == CameraType.Preview
        || renderingData.cameraData.cameraType == CameraType.Reflection) return;

    _pass.Setup(settings.edgeMaterial, settings.compositeMaterial);
    _pass.renderPassEvent = settings.renderPassEvent;
    _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    renderer.EnqueuePass(_pass);
  }

  private class OutlinePass : ScriptableRenderPass {
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
    private static readonly int EdgeMaskId = Shader.PropertyToID("_EdgeMask");
    private static readonly int EdgeMaskTexelSizeId = Shader.PropertyToID("_EdgeMask_TexelSize");
    private static readonly Vector4 FullScaleBias = new Vector4(1, 1, 0, 0);
    private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

    private Material _edgeMaterial;
    private Material _compositeMaterial;

    public OutlinePass() {
      profilingSampler = new ProfilingSampler("Full Screen Outline");
    }

    public void Setup(Material edgeMaterial, Material compositeMaterial) {
      _edgeMaterial = edgeMaterial;
      _compositeMaterial = compositeMaterial;
    }

    private class EdgePassData {
      public Material material;
    }

    private class CompositePassData {
      public Material material;
      public TextureHandle colorCopy;
      public TextureHandle edgeMask;
      public Vector4 edgeMaskTexelSize;
    }

    private static void ExecuteEdgePass(RasterCommandBuffer cmd, Material material) {
      s_PropertyBlock.Clear();
      s_PropertyBlock.SetVector(BlitScaleBiasId, FullScaleBias);
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    private static void ExecuteCompositePass(RasterCommandBuffer cmd, RTHandle colorCopy, RTHandle edgeMask, Vector4 edgeMaskTexelSize, Material material) {
      s_PropertyBlock.Clear();
      s_PropertyBlock.SetTexture(BlitTextureId, colorCopy);
      s_PropertyBlock.SetTexture(EdgeMaskId, edgeMask);
      // _EdgeMask isn't a serialized material Properties-block texture (it's bound purely at
      // runtime here), so Unity never auto-generates its _TexelSize companion the way it does for
      // textures assigned through the material Inspector — left unset, the shader read back
      // garbage/NaN, which corrupted every dilation sample (even the un-offset centre one, since
      // 0 * NaN is NaN, not 0). Set it explicitly from the size we allocated it at.
      s_PropertyBlock.SetVector(EdgeMaskTexelSizeId, edgeMaskTexelSize);
      s_PropertyBlock.SetVector(BlitScaleBiasId, FullScaleBias);
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (_edgeMaterial == null || _compositeMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

      // --- Pass 1: precise edge test (once per pixel) -> single-channel mask --------------
      // Base the descriptor on the EXISTING camera color texture (via GetTextureDesc) rather than
      // reconstructing one from cameraTargetDescriptor — that descriptor carries both the color
      // AND depth-stencil format together, and TextureDesc's RenderTextureDescriptor conversion
      // picks the depth-stencil format whenever one is present, which silently produced a "depth"
      // texture here instead of the color-format mask we wanted.
      var maskDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      maskDesc.msaaSamples = MSAASamples.None;
      maskDesc.format = GraphicsFormat.R8_UNorm;
      maskDesc.clearBuffer = false;
      maskDesc.name = "_OutlineEdgeMask";
      TextureHandle edgeMask = renderGraph.CreateTexture(maskDesc);
      var edgeMaskTexelSize = new Vector4(1f / maskDesc.width, 1f / maskDesc.height, maskDesc.width, maskDesc.height);

      using (var builder = renderGraph.AddRasterRenderPass<EdgePassData>("Outline Edge Detect", out var edgePassData, profilingSampler)) {
        edgePassData.material = _edgeMaterial;

        if (resourceData.cameraDepthTexture.IsValid())
          builder.UseTexture(resourceData.cameraDepthTexture);
        if (resourceData.cameraNormalsTexture.IsValid())
          builder.UseTexture(resourceData.cameraNormalsTexture);

        builder.SetRenderAttachment(edgeMask, 0, AccessFlags.Write);
        builder.SetRenderFunc((EdgePassData data, RasterGraphContext ctx) => ExecuteEdgePass(ctx.cmd, data.material));
      }

      // --- Pass 2: copy the camera color so pass 3 can read the pre-outline scene while
      //     writing the composited result back into that same camera color target -----------
      var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      colorDesc.msaaSamples = MSAASamples.None;
      colorDesc.clearBuffer = false;
      colorDesc.name = "_OutlineColorCopy";
      TextureHandle colorCopy = renderGraph.CreateTexture(colorDesc);
      renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: "Outline Copy Color");

      // --- Pass 3: dilate the mask (depth-scaled radius, distance-weighted falloff) and
      //     composite the outline color over the copied scene ------------------------------
      using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Outline Composite", out var compositePassData, profilingSampler)) {
        compositePassData.material = _compositeMaterial;
        compositePassData.colorCopy = colorCopy;
        compositePassData.edgeMask = edgeMask;
        compositePassData.edgeMaskTexelSize = edgeMaskTexelSize;

        builder.UseTexture(colorCopy);
        builder.UseTexture(edgeMask);
        if (resourceData.cameraDepthTexture.IsValid())
          builder.UseTexture(resourceData.cameraDepthTexture);

        builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderFunc((CompositePassData data, RasterGraphContext ctx) =>
          ExecuteCompositePass(ctx.cmd, data.colorCopy, data.edgeMask, data.edgeMaskTexelSize, data.material));
      }
    }
  }
}
