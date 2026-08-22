using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Captures and blurs the completed frame behind a modal, including screen-space overlay UI.
/// </summary>
public sealed class PopupBackgroundBlur : MonoBehaviour {
  private const int Downsample = 2;
  private const int BlurIterations = 3;
  private const float BlurRadius = 1.5f;

  private Canvas _modalCanvas;
  private RenderTexture _blurredTexture;
  private Material _blurMaterial;
  private Material _backgroundMaterial;
  private readonly List<Canvas> _hiddenOverlayCanvases = new();
  private readonly List<GraphicRaycaster> _disabledRaycasters = new();

  public void Initialize(Canvas modalCanvas) {
    if (modalCanvas == null || _modalCanvas != null) return;

    _modalCanvas = modalCanvas;
    _modalCanvas.enabled = false;
    DisableOtherRaycasters();
    StartCoroutine(CaptureAndBlur());
  }

  private IEnumerator CaptureAndBlur() {
    yield return new WaitForEndOfFrame();

    Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
    if (screenshot == null) {
      RevealModal();
      yield break;
    }

    Shader blurShader = Resources.Load<Shader>("PopupBlur");
    if (blurShader == null) {
      Debug.LogWarning("[PopupBackgroundBlur] Resources/PopupBlur.shader could not be loaded.");
      Destroy(screenshot);
      RevealModal();
      yield break;
    }

    _blurMaterial = new Material(blurShader);
    _blurMaterial.SetFloat("_BlurRadius", BlurRadius);

    int width = Mathf.Max(1, screenshot.width / Downsample);
    int height = Mathf.Max(1, screenshot.height / Downsample);
    RenderTextureFormat format = screenshot.format == TextureFormat.RGBAHalf
      ? RenderTextureFormat.ARGBHalf
      : RenderTextureFormat.ARGB32;

    _blurredTexture = RenderTexture.GetTemporary(width, height, 0, format);
    _blurredTexture.filterMode = FilterMode.Bilinear;
    RenderTexture scratch = RenderTexture.GetTemporary(width, height, 0, format);
    scratch.filterMode = FilterMode.Bilinear;

    Graphics.Blit(screenshot, _blurredTexture);
    for (var i = 0; i < BlurIterations; i++) {
      Graphics.Blit(_blurredTexture, scratch, _blurMaterial, 0);
      Graphics.Blit(scratch, _blurredTexture, _blurMaterial, 1);
    }

    RenderTexture.ReleaseTemporary(scratch);
    Destroy(screenshot);

    // Overlay canvases always render after a Screen Space - Camera canvas, regardless of its
    // sorting order. They are already present in the screenshot, so hide their live copy now.
    HideOtherOverlayCanvases();

    if (_modalCanvas == null) yield break;

    var backgroundObject = new GameObject("BlurredBackground", typeof(RectTransform));
    backgroundObject.transform.SetParent(_modalCanvas.transform, false);
    backgroundObject.transform.SetAsFirstSibling();

    RawImage background = backgroundObject.AddComponent<RawImage>();
    background.texture = _blurredTexture;
    background.raycastTarget = false;
    _backgroundMaterial = new Material(background.defaultMaterial) {
      name = "Modal Blurred Background (Depth Override)",
      hideFlags = HideFlags.DontSave
    };
    if (_backgroundMaterial.HasProperty("unity_GUIZTestMode")) {
      _backgroundMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
    }
    background.material = _backgroundMaterial;
    background.rectTransform.anchorMin = Vector2.zero;
    background.rectTransform.anchorMax = Vector2.one;
    background.rectTransform.offsetMin = Vector2.zero;
    background.rectTransform.offsetMax = Vector2.zero;

    RevealModal();
  }

  private void RevealModal() {
    if (_modalCanvas == null) return;

    _modalCanvas.enabled = true;
    ModalAppearAnimation[] animations =
      _modalCanvas.GetComponentsInChildren<ModalAppearAnimation>(true);
    foreach (ModalAppearAnimation animation in animations) animation.Play();
  }

  private void DisableOtherRaycasters() {
    GraphicRaycaster[] raycasters = FindObjectsByType<GraphicRaycaster>(
      FindObjectsInactive.Exclude,
      FindObjectsSortMode.None
    );
    foreach (GraphicRaycaster raycaster in raycasters) {
      if (
        raycaster == null ||
        !raycaster.enabled ||
        raycaster.transform == transform ||
        raycaster.transform.IsChildOf(transform)
      ) {
        continue;
      }

      raycaster.enabled = false;
      _disabledRaycasters.Add(raycaster);
    }
  }

  private void HideOtherOverlayCanvases() {
    Canvas[] canvases = FindObjectsByType<Canvas>(
      FindObjectsInactive.Exclude,
      FindObjectsSortMode.None
    );
    foreach (Canvas canvas in canvases) {
      if (canvas == null || !canvas.enabled || canvas == _modalCanvas) continue;
      if (canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

      canvas.enabled = false;
      _hiddenOverlayCanvases.Add(canvas);
    }
  }

  private void RestoreSuppressedUi() {
    foreach (Canvas canvas in _hiddenOverlayCanvases) {
      if (canvas != null) canvas.enabled = true;
    }
    _hiddenOverlayCanvases.Clear();

    foreach (GraphicRaycaster raycaster in _disabledRaycasters) {
      if (raycaster != null) raycaster.enabled = true;
    }
    _disabledRaycasters.Clear();
  }

  private void OnDestroy() {
    RestoreSuppressedUi();

    if (_blurredTexture != null) {
      RenderTexture.ReleaseTemporary(_blurredTexture);
      _blurredTexture = null;
    }

    if (_blurMaterial != null) Destroy(_blurMaterial);
    if (_backgroundMaterial != null) Destroy(_backgroundMaterial);
  }
}
