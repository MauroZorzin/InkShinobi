using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>Reserved rendering layer used to composite gameplay aim previews after lighting and vignette.</summary>
public static class AimPreviewRendering {
  public const int RenderingLayerIndex = 29;
  public const uint RenderingLayerMask = 1u << RenderingLayerIndex;
}

/// <summary>Reserved rendering layer for wall-switch previews that intentionally render through scene depth.</summary>
public static class WallSwitchPreviewRendering {
  public const int RenderingLayerIndex = 28;
  public const uint RenderingLayerMask = 1u << RenderingLayerIndex;
}

/// <summary>
/// Desaturates the world while restoring the original camera color wherever a renderer carries
/// the SelectiveColor Rendering Layer bit. The mask is produced by drawing the marked renderers
/// with their own shaders into an alpha-only target, which preserves sprite and particle shapes
/// without requiring duplicate materials.
/// </summary>
public sealed class SelectiveColorRendererFeature : ScriptableRendererFeature {
  [System.Serializable]
  public sealed class Settings {
    [Tooltip("Run after post-processing so later color grading cannot reintroduce color into monochrome objects.")]
    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

    [Tooltip("Fullscreen material using Hidden/InkShinobi/SelectiveColorComposite.")]
    public Material compositeMaterial;

    [Tooltip("Outline-only shader used to preserve each guard's per-renderer color while it is behind geometry.")]
    public Shader guardOccludedOutlineShader;
  }

  public Settings settings = new Settings();

  private SelectiveColorPass _pass;
  private Material _doorAccentMaskMaterial;

  public override void Create() {
    CoreUtils.Destroy(_doorAccentMaskMaterial);
    Shader doorAccentShader = Shader.Find("Hidden/InkShinobi/DoorAccentMask");
    if (doorAccentShader != null)
      _doorAccentMaskMaterial = CoreUtils.CreateEngineMaterial(doorAccentShader);
    _pass = new SelectiveColorPass();
  }

  protected override void Dispose(bool disposing) {
    CoreUtils.Destroy(_doorAccentMaskMaterial);
    _doorAccentMaskMaterial = null;
    base.Dispose(disposing);
  }

  public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
    if (settings.compositeMaterial == null) return;

    Camera camera = renderingData.cameraData.camera;
    if (camera == null
        || renderingData.cameraData.cameraType == CameraType.Preview
        || renderingData.cameraData.cameraType == CameraType.Reflection
        || !camera.TryGetComponent(out SelectiveColorCamera cameraSettings)
        || !cameraSettings.isActiveAndEnabled
        || !cameraSettings.effectEnabled
        || cameraSettings.intensity <= 0f) {
      return;
    }

    _pass.Setup(
      settings.compositeMaterial,
      cameraSettings,
      settings.guardOccludedOutlineShader,
      _doorAccentMaskMaterial);
    _pass.renderPassEvent = settings.renderPassEvent;
    _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    renderer.EnqueuePass(_pass);
  }

  private sealed class SelectiveColorPass : ScriptableRenderPass {
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
    private static readonly int PreserveMaskId = Shader.PropertyToID("_SelectiveColorMask");
    private static readonly int LightReceiverMaskId = Shader.PropertyToID("_LightReceiverMask");
    private static readonly int LightTintExclusionMaskId = Shader.PropertyToID("_LightTintExclusionMask");
    private static readonly int AimPreviewColorId = Shader.PropertyToID("_AimPreviewColor");
    private static readonly int IntensityId = Shader.PropertyToID("_SelectiveColorIntensity");
    private static readonly int SaturationId = Shader.PropertyToID("_SelectiveColorSaturation");
    private static readonly int PreserveStrengthId = Shader.PropertyToID("_SelectiveColorPreserveStrength");
    private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
    private static readonly int FixedLightCountId = Shader.PropertyToID("_FixedLightCount");
    private static readonly int FixedLightPositionsId = Shader.PropertyToID("_FixedLightPositions");
    private static readonly int FixedLightColorsId = Shader.PropertyToID("_FixedLightColors");
    private static readonly int FixedLightFeathersId = Shader.PropertyToID("_FixedLightFeathers");
    private static readonly int FixedLightLooksId = Shader.PropertyToID("_FixedLightLooks");
    private static readonly int FixedLightWorldToBoundsId = Shader.PropertyToID("_FixedLightWorldToBounds");
    private static readonly int FixedLightBoundsExtentsId = Shader.PropertyToID("_FixedLightBoundsExtents");
    private static readonly int FixedVisibilityRangesId = Shader.PropertyToID("_FixedVisibilityRanges");
    private static readonly int ConeLightCountId = Shader.PropertyToID("_ConeLightCount");
    private static readonly int ConeLightPositionsId = Shader.PropertyToID("_ConeLightPositions");
    private static readonly int ConeLightDirectionsId = Shader.PropertyToID("_ConeLightDirections");
    private static readonly int ConeLightColorsId = Shader.PropertyToID("_ConeLightColors");
    private static readonly int ConeLightFeathersId = Shader.PropertyToID("_ConeLightFeathers");
    private static readonly int ConeLightLooksId = Shader.PropertyToID("_ConeLightLooks");
    private static readonly int ConeVisibilityOriginsId = Shader.PropertyToID("_ConeVisibilityOrigins");
    private static readonly int ConeVisibilityRangesId = Shader.PropertyToID("_ConeVisibilityRanges");
    private static readonly int ConeEndWallPositionsId = Shader.PropertyToID("_ConeEndWallPositions");
    private static readonly int ConeEndWallNormalsId = Shader.PropertyToID("_ConeEndWallNormals");
    private const uint LightReceiverRenderingLayerMask = 1u << 30;
    private const int GuardLayerMask = 1 << 7;

    private static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);
    private static readonly MaterialPropertyBlock PropertyBlock = new();

    private static readonly List<ShaderTagId> ShaderTags = new() {
      new ShaderTagId("UniversalForward"),
      new ShaderTagId("UniversalForwardOnly"),
      new ShaderTagId("SRPDefaultUnlit"),
      new ShaderTagId("LightweightForward")
    };

    private Material _compositeMaterial;
    private float _intensity;
    private float _backgroundSaturation;
    private float _preservedColorStrength;
    private Shader _guardOccludedOutlineShader;
    private Material _doorAccentMaskMaterial;

    public SelectiveColorPass() {
      profilingSampler = new ProfilingSampler("Selective Color");
    }

    public void Setup(
      Material compositeMaterial,
      SelectiveColorCamera cameraSettings,
      Shader guardOccludedOutlineShader,
      Material doorAccentMaskMaterial) {
      _compositeMaterial = compositeMaterial;
      _intensity = cameraSettings.intensity;
      _backgroundSaturation = cameraSettings.backgroundSaturation;
      _preservedColorStrength = cameraSettings.preservedColorStrength;
      _guardOccludedOutlineShader = guardOccludedOutlineShader;
      _doorAccentMaskMaterial = doorAccentMaskMaterial;
    }

    private sealed class MaskPassData {
      public RendererListHandle opaqueRenderers;
      public RendererListHandle transparentRenderers;
      public RendererListHandle occludedGuardRenderers;
      public bool drawOccludedGuards;
      public Material doorAccentMaskMaterial;
    }

    private sealed class CompositePassData {
      public Material material;
      public TextureHandle sourceColor;
      public TextureHandle preserveMask;
      public TextureHandle lightReceiverMask;
      public TextureHandle lightTintExclusionMask;
      public TextureHandle aimPreviewColor;
      public TextureHandle depthTexture;
      public float intensity;
      public float saturation;
      public float preserveStrength;
      public int fixedLightCount;
      public Vector4[] fixedLightPositions;
      public Vector4[] fixedLightColors;
      public float[] fixedLightFeathers;
      public Vector4[] fixedLightLooks;
      public Matrix4x4[] fixedLightWorldToBounds;
      public Vector4[] fixedLightBoundsExtents;
      public Vector4[] fixedVisibilityRanges;
      public int coneLightCount;
      public Vector4[] coneLightPositions;
      public Vector4[] coneLightDirections;
      public Vector4[] coneLightColors;
      public Vector4[] coneLightFeathers;
      public Vector4[] coneLightLooks;
      public Vector4[] coneVisibilityOrigins;
      public Vector4[] coneVisibilityRanges;
      public Vector4[] coneEndWallPositions;
      public Vector4[] coneEndWallNormals;
    }

    private sealed class AimPreviewPassData {
      public RendererListHandle depthTestedRenderers;
      public RendererListHandle overlayRenderers;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
      if (_compositeMaterial == null) return;

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
      UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
      UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
      UniversalLightData lightData = frameData.Get<UniversalLightData>();

      TextureDesc sourceDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
      sourceDesc.msaaSamples = MSAASamples.None;
      sourceDesc.clearBuffer = false;
      sourceDesc.name = "_SelectiveColorSource";
      TextureHandle sourceColor = renderGraph.CreateTexture(sourceDesc);
      renderGraph.AddBlitPass(resourceData.activeColorTexture, sourceColor, Vector2.one, Vector2.zero, passName: "Selective Color Copy");

      TextureDesc maskDesc = sourceDesc;
      maskDesc.format = GraphicsFormat.R8G8B8A8_UNorm;
      maskDesc.clearBuffer = true;
      maskDesc.clearColor = Color.clear;
      maskDesc.name = "_SelectiveColorMask";
      TextureHandle preserveMask = renderGraph.CreateTexture(maskDesc);

      using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Selective Color Mask", out MaskPassData passData, profilingSampler)) {
        passData.opaqueRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.opaque,
          cameraData.defaultOpaqueSortFlags, CreateOpaqueMaskState(), SelectiveColor.RenderingLayerMask);
        passData.transparentRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.transparent,
          SortingCriteria.CommonTransparent, CreateTransparentMaskState(), SelectiveColor.RenderingLayerMask);
        passData.doorAccentMaskMaterial = _doorAccentMaskMaterial;
        passData.drawOccludedGuards = _guardOccludedOutlineShader != null;
        if (passData.drawOccludedGuards)
          passData.occludedGuardRenderers = CreateOccludedGuardRendererList(
            renderGraph, renderingData, cameraData, lightData, _guardOccludedOutlineShader);

        builder.UseRendererList(passData.opaqueRenderers);
        builder.UseRendererList(passData.transparentRenderers);
        if (passData.drawOccludedGuards) builder.UseRendererList(passData.occludedGuardRenderers);
        builder.SetRenderAttachment(preserveMask, 0, AccessFlags.Write);
        if (resourceData.activeDepthTexture.IsValid()) {
          builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
        }

        // The clear is semantically required even in a frame with no marked objects.
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) => {
          context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
          context.cmd.DrawRendererList(data.opaqueRenderers);
          context.cmd.DrawRendererList(data.transparentRenderers);
          DoorKeyColorVisual.DrawActiveAccentMasks(context.cmd, data.doorAccentMaskMaterial);
          if (data.drawOccludedGuards) context.cmd.DrawRendererList(data.occludedGuardRenderers);
        });
      }

      TextureDesc receiverDesc = maskDesc;
      receiverDesc.name = "_LightReceiverMask";
      TextureHandle lightReceiverMask = renderGraph.CreateTexture(receiverDesc);

      using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Light Receiver Mask", out MaskPassData passData, profilingSampler)) {
        passData.opaqueRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.opaque,
          cameraData.defaultOpaqueSortFlags, CreateOpaqueMaskState(), LightReceiverRenderingLayerMask);
        passData.transparentRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.transparent,
          SortingCriteria.CommonTransparent, CreateTransparentMaskState(), LightReceiverRenderingLayerMask);

        builder.UseRendererList(passData.opaqueRenderers);
        builder.UseRendererList(passData.transparentRenderers);
        builder.SetRenderAttachment(lightReceiverMask, 0, AccessFlags.Write);
        if (resourceData.activeDepthTexture.IsValid())
          builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) => {
          context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
          context.cmd.DrawRendererList(data.opaqueRenderers);
          context.cmd.DrawRendererList(data.transparentRenderers);
        });
      }

      TextureDesc exclusionDesc = maskDesc;
      exclusionDesc.name = "_LightTintExclusionMask";
      TextureHandle lightTintExclusionMask = renderGraph.CreateTexture(exclusionDesc);

      using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Light Tint Exclusion Mask", out MaskPassData passData, profilingSampler)) {
        passData.opaqueRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.opaque,
          cameraData.defaultOpaqueSortFlags, CreateOpaqueMaskState(), LightReceiverExclusion.RenderingLayerMask);
        passData.transparentRenderers = CreateRendererList(
          renderGraph, renderingData, cameraData, lightData, RenderQueueRange.transparent,
          SortingCriteria.CommonTransparent, CreateTransparentMaskState(), LightReceiverExclusion.RenderingLayerMask);

        builder.UseRendererList(passData.opaqueRenderers);
        builder.UseRendererList(passData.transparentRenderers);
        builder.SetRenderAttachment(lightTintExclusionMask, 0, AccessFlags.Write);
        if (resourceData.activeDepthTexture.IsValid())
          builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) => {
          context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
          context.cmd.DrawRendererList(data.opaqueRenderers);
          context.cmd.DrawRendererList(data.transparentRenderers);
        });
      }

      TextureDesc aimPreviewDesc = sourceDesc;
      aimPreviewDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
      aimPreviewDesc.clearBuffer = true;
      aimPreviewDesc.clearColor = Color.clear;
      aimPreviewDesc.name = "_AimPreviewColor";
      TextureHandle aimPreviewColor = renderGraph.CreateTexture(aimPreviewDesc);

      // Capture the authored aim visuals again after post-processing. The final composite uses
      // this clean color/alpha over the processed world, so fake lights and colored vignettes
      // cannot turn a valid trajectory into a misleading warning color.
      using (var builder = renderGraph.AddRasterRenderPass<AimPreviewPassData>(
               "Aim Preview Color", out AimPreviewPassData passData, profilingSampler)) {
        passData.depthTestedRenderers = CreateRendererList(
          renderGraph,
          renderingData,
          cameraData,
          lightData,
          RenderQueueRange.transparent,
          SortingCriteria.CommonTransparent,
          CreateAimPreviewState(),
          AimPreviewRendering.RenderingLayerMask);
        passData.overlayRenderers = CreateRendererList(
          renderGraph,
          renderingData,
          cameraData,
          lightData,
          RenderQueueRange.transparent,
          SortingCriteria.CommonTransparent,
          CreateWallSwitchPreviewState(),
          WallSwitchPreviewRendering.RenderingLayerMask);

        builder.UseRendererList(passData.depthTestedRenderers);
        builder.UseRendererList(passData.overlayRenderers);
        builder.SetRenderAttachment(aimPreviewColor, 0, AccessFlags.Write);
        if (resourceData.activeDepthTexture.IsValid())
          builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (AimPreviewPassData data, RasterGraphContext context) => {
          context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
          context.cmd.DrawRendererList(data.depthTestedRenderers);
          context.cmd.DrawRendererList(data.overlayRenderers);
        });
      }

      using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Selective Color Composite", out CompositePassData passData, profilingSampler)) {
        passData.material = _compositeMaterial;
        passData.sourceColor = sourceColor;
        passData.preserveMask = preserveMask;
        passData.lightReceiverMask = lightReceiverMask;
        passData.lightTintExclusionMask = lightTintExclusionMask;
        passData.aimPreviewColor = aimPreviewColor;
        passData.depthTexture = resourceData.activeDepthTexture;
        passData.intensity = _intensity;
        passData.saturation = _backgroundSaturation;
        passData.preserveStrength = _preservedColorStrength;
        passData.fixedLightPositions = new Vector4[FixedLightSource.MaximumVisibleLights];
        passData.fixedLightColors = new Vector4[FixedLightSource.MaximumVisibleLights];
        passData.fixedLightFeathers = new float[FixedLightSource.MaximumVisibleLights];
        passData.fixedLightLooks = new Vector4[FixedLightSource.MaximumVisibleLights];
        passData.fixedLightWorldToBounds = new Matrix4x4[FixedLightSource.MaximumVisibleLights];
        passData.fixedLightBoundsExtents = new Vector4[FixedLightSource.MaximumVisibleLights];
        passData.fixedVisibilityRanges = new Vector4[FixedLightSource.PackedVisibilityVectorCount];
        passData.fixedLightCount = FixedLightSource.FillShaderData(
          cameraData.camera,
          passData.fixedLightPositions,
          passData.fixedLightColors,
          passData.fixedLightFeathers,
          passData.fixedLightLooks,
          passData.fixedLightWorldToBounds,
          passData.fixedLightBoundsExtents,
          passData.fixedVisibilityRanges);
        passData.coneLightPositions = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneLightDirections = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneLightColors = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneLightFeathers = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneLightLooks = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneVisibilityOrigins = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneVisibilityRanges = new Vector4[ConeLightSource.PackedVisibilityVectorCount];
        passData.coneEndWallPositions = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneEndWallNormals = new Vector4[ConeLightSource.MaximumVisibleCones];
        passData.coneLightCount = ConeLightSource.FillShaderData(
          cameraData.camera,
          passData.coneLightPositions,
          passData.coneLightDirections,
          passData.coneLightColors,
          passData.coneLightFeathers,
          passData.coneLightLooks,
          passData.coneVisibilityOrigins,
          passData.coneVisibilityRanges,
          passData.coneEndWallPositions,
          passData.coneEndWallNormals);

        builder.UseTexture(sourceColor);
        builder.UseTexture(preserveMask);
        builder.UseTexture(lightReceiverMask);
        builder.UseTexture(lightTintExclusionMask);
        builder.UseTexture(aimPreviewColor);
        builder.UseTexture(passData.depthTexture);
        if (resourceData.cameraNormalsTexture.IsValid())
          builder.UseTexture(resourceData.cameraNormalsTexture);
        builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) => {
          PropertyBlock.Clear();
          PropertyBlock.SetTexture(BlitTextureId, data.sourceColor);
          PropertyBlock.SetTexture(PreserveMaskId, data.preserveMask);
          PropertyBlock.SetTexture(LightReceiverMaskId, data.lightReceiverMask);
          PropertyBlock.SetTexture(LightTintExclusionMaskId, data.lightTintExclusionMask);
          PropertyBlock.SetTexture(AimPreviewColorId, data.aimPreviewColor);
          PropertyBlock.SetTexture(CameraDepthTextureId, data.depthTexture);
          PropertyBlock.SetVector(BlitScaleBiasId, FullScaleBias);
          PropertyBlock.SetFloat(IntensityId, data.intensity);
          PropertyBlock.SetFloat(SaturationId, data.saturation);
          PropertyBlock.SetFloat(PreserveStrengthId, data.preserveStrength);
          PropertyBlock.SetInteger(FixedLightCountId, data.fixedLightCount);
          PropertyBlock.SetVectorArray(FixedLightPositionsId, data.fixedLightPositions);
          PropertyBlock.SetVectorArray(FixedLightColorsId, data.fixedLightColors);
          PropertyBlock.SetFloatArray(FixedLightFeathersId, data.fixedLightFeathers);
          PropertyBlock.SetVectorArray(FixedLightLooksId, data.fixedLightLooks);
          PropertyBlock.SetMatrixArray(FixedLightWorldToBoundsId, data.fixedLightWorldToBounds);
          PropertyBlock.SetVectorArray(FixedLightBoundsExtentsId, data.fixedLightBoundsExtents);
          PropertyBlock.SetVectorArray(FixedVisibilityRangesId, data.fixedVisibilityRanges);
          PropertyBlock.SetInteger(ConeLightCountId, data.coneLightCount);
          PropertyBlock.SetVectorArray(ConeLightPositionsId, data.coneLightPositions);
          PropertyBlock.SetVectorArray(ConeLightDirectionsId, data.coneLightDirections);
          PropertyBlock.SetVectorArray(ConeLightColorsId, data.coneLightColors);
          PropertyBlock.SetVectorArray(ConeLightFeathersId, data.coneLightFeathers);
          PropertyBlock.SetVectorArray(ConeLightLooksId, data.coneLightLooks);
          PropertyBlock.SetVectorArray(ConeVisibilityOriginsId, data.coneVisibilityOrigins);
          PropertyBlock.SetVectorArray(ConeVisibilityRangesId, data.coneVisibilityRanges);
          PropertyBlock.SetVectorArray(ConeEndWallPositionsId, data.coneEndWallPositions);
          PropertyBlock.SetVectorArray(ConeEndWallNormalsId, data.coneEndWallNormals);
          context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, PropertyBlock);
        });
      }
    }

    private static RendererListHandle CreateRendererList(
      RenderGraph renderGraph,
      UniversalRenderingData renderingData,
      UniversalCameraData cameraData,
      UniversalLightData lightData,
      RenderQueueRange queueRange,
      SortingCriteria sortingCriteria,
      RenderStateBlock stateBlock,
      uint renderingLayerMask) {

      FilteringSettings filtering = new(queueRange) {
        renderingLayerMask = renderingLayerMask
      };

      DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
        ShaderTags, renderingData, cameraData, lightData, sortingCriteria);

      NativeArray<ShaderTagId> tagValues = new(1, Allocator.Temp);
      tagValues[0] = ShaderTagId.none;
      NativeArray<RenderStateBlock> stateBlocks = new(1, Allocator.Temp);
      stateBlocks[0] = stateBlock;

      RendererListParams parameters = new(renderingData.cullResults, drawing, filtering) {
        tagValues = tagValues,
        stateBlocks = stateBlocks,
        isPassTagName = false
      };
      return renderGraph.CreateRendererList(parameters);
    }

    private static RenderStateBlock CreateOpaqueMaskState() {
      RenderTargetBlendState targetBlend = new(
        writeMask: ColorWriteMask.Alpha,
        sourceColorBlendMode: BlendMode.One,
        destinationColorBlendMode: BlendMode.Zero,
        sourceAlphaBlendMode: BlendMode.One,
        destinationAlphaBlendMode: BlendMode.Zero);

      return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = targetBlend },
        depthState = new DepthState(false, CompareFunction.LessEqual)
      };
    }

    private static RenderStateBlock CreateTransparentMaskState() {
      // Alpha accumulates as coverage: a + existing * (1 - a). RGB is intentionally disabled.
      RenderTargetBlendState targetBlend = new(
        writeMask: ColorWriteMask.Alpha,
        sourceColorBlendMode: BlendMode.Zero,
        destinationColorBlendMode: BlendMode.One,
        sourceAlphaBlendMode: BlendMode.One,
        destinationAlphaBlendMode: BlendMode.OneMinusSrcAlpha);

      return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = targetBlend },
        depthState = new DepthState(false, CompareFunction.LessEqual)
      };
    }

    private static RenderStateBlock CreateAimPreviewState() {
      RenderTargetBlendState targetBlend = new(
        writeMask: ColorWriteMask.All,
        sourceColorBlendMode: BlendMode.One,
        destinationColorBlendMode: BlendMode.Zero,
        sourceAlphaBlendMode: BlendMode.One,
        destinationAlphaBlendMode: BlendMode.Zero);

      return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = targetBlend },
        depthState = new DepthState(false, CompareFunction.LessEqual)
      };
    }

    private static RenderStateBlock CreateWallSwitchPreviewState() {
      RenderTargetBlendState targetBlend = new(
        writeMask: ColorWriteMask.All,
        sourceColorBlendMode: BlendMode.One,
        destinationColorBlendMode: BlendMode.Zero,
        sourceAlphaBlendMode: BlendMode.One,
        destinationAlphaBlendMode: BlendMode.Zero);

      return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = targetBlend },
        depthState = new DepthState(false, CompareFunction.Always)
      };
    }

    private static RendererListHandle CreateOccludedGuardRendererList(
      RenderGraph renderGraph,
      UniversalRenderingData renderingData,
      UniversalCameraData cameraData,
      UniversalLightData lightData,
      Shader overrideShader) {

      FilteringSettings filtering = new(RenderQueueRange.transparent, GuardLayerMask);
      DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
        ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
      drawing.overrideShader = overrideShader;
      drawing.overrideShaderPassIndex = 0;

      NativeArray<ShaderTagId> tagValues = new(1, Allocator.Temp);
      tagValues[0] = ShaderTagId.none;
      NativeArray<RenderStateBlock> stateBlocks = new(1, Allocator.Temp);
      stateBlocks[0] = CreateOccludedGuardMaskState();

      RendererListParams parameters = new(renderingData.cullResults, drawing, filtering) {
        tagValues = tagValues,
        stateBlocks = stateBlocks,
        isPassTagName = false
      };
      return renderGraph.CreateRendererList(parameters);
    }

    private static RenderStateBlock CreateOccludedGuardMaskState() {
      RenderTargetBlendState targetBlend = new(
        writeMask: ColorWriteMask.Alpha,
        sourceColorBlendMode: BlendMode.Zero,
        destinationColorBlendMode: BlendMode.One,
        sourceAlphaBlendMode: BlendMode.One,
        destinationAlphaBlendMode: BlendMode.OneMinusSrcAlpha);

      return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth) {
        blendState = new BlendState { blendState0 = targetBlend },
        depthState = new DepthState(false, CompareFunction.Greater)
      };
    }
  }
}
