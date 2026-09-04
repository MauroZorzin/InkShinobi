using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>Contorno screen-space a due pass: un pass per la maschera dei bordi, una copia del colore, poi un pass composito che dilata la maschera sulla scena.</summary>
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
      // _TexelSize non viene generato automaticamente per una texture assegnata a runtime, va impostato esplicitamente per evitare campioni NaN nella dilatazione.
      s_PropertyBlock.SetVector(EdgeMaskTexelSizeId, edgeMaskTexelSize);
      s_PropertyBlock.SetVector(BlitScaleBiasId, FullScaleBias);
      cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (_edgeMaterial == null || _compositeMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

      // Basato sulla texture colore esistente, non su cameraTargetDescriptor, che sceglierebbe invece il formato depth-stencil.
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

      var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      colorDesc.msaaSamples = MSAASamples.None;
      colorDesc.clearBuffer = false;
      colorDesc.name = "_OutlineColorCopy";
      TextureHandle colorCopy = renderGraph.CreateTexture(colorDesc);
      renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: "Outline Copy Color");

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
