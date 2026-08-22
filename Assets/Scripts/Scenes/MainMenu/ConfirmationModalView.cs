using System;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Reusable presentation and lifecycle for parchment confirmation modals.
/// Scene-specific state changes remain in the caller-provided callbacks.
/// </summary>
public sealed class ConfirmationModalView : MonoBehaviour {
  private const string ResourceName = "ConfirmationModal";
  private const float PaperSoundStartOffset = 0.08f;

  [Header("Structure")]
  [SerializeField] private Canvas modalCanvas;
  [SerializeField] private PopupBackgroundBlur backgroundBlur;
  [SerializeField] private ModalAppearAnimation panelAnimation;

  [Header("Content")]
  [SerializeField] private TMP_Text titleLabel;
  [SerializeField] private TMP_Text messageLabel;
  [SerializeField] private Button cancelButton;
  [SerializeField] private TMP_Text cancelButtonLabel;
  [SerializeField] private Button confirmButton;
  [SerializeField] private TMP_Text confirmButtonLabel;

  [Header("Audio")]
  [SerializeField] private AudioClip clickSound;
  [SerializeField] private AudioClip paperSound;
  [SerializeField] private AudioMixerGroup mixerGroup;
  [SerializeField] private AudioSource paperAudioSource;

  private readonly System.Collections.Generic.List<Material> _depthOverrideMaterials = new();
  private Action _onCancel;
  private int _openedFrame;

  public bool IsClosing => panelAnimation != null && panelAnimation.IsClosing;

  public static ConfirmationModalView Create(
    string objectName,
    string title,
    string message,
    string cancelText,
    string confirmText,
    Action onCancel,
    Action onConfirm
  ) {
    ConfirmationModalView prefab = Resources.Load<ConfirmationModalView>(ResourceName);
    if (prefab == null) {
      Debug.LogError($"[ConfirmationModalView] Resources/{ResourceName}.prefab is missing.");
      return null;
    }

    ConfirmationModalView instance = Instantiate(prefab);
    instance.gameObject.name = objectName;
    instance.Initialize(title, message, cancelText, confirmText, onCancel, onConfirm);
    return instance;
  }

  private void Initialize(
    string title,
    string message,
    string cancelText,
    string confirmText,
    Action onCancel,
    Action onConfirm
  ) {
    _onCancel = onCancel;
    _openedFrame = Time.frameCount;
    titleLabel.text = title;
    messageLabel.text = message;
    cancelButtonLabel.text = cancelText;
    confirmButtonLabel.text = confirmText;
    ConfigureTextAppearance(titleLabel);
    ConfigureTextAppearance(messageLabel);
    ConfigureTextAppearance(cancelButtonLabel);
    ConfigureTextAppearance(confirmButtonLabel);
    ConfigureDepthIndependentRendering();

    cancelButton.onClick.RemoveAllListeners();
    confirmButton.onClick.RemoveAllListeners();
    cancelButton.onClick.AddListener(PlayClickSound);
    confirmButton.onClick.AddListener(PlayClickSound);
    cancelButton.onClick.AddListener(() => onCancel?.Invoke());
    confirmButton.onClick.AddListener(() => onConfirm?.Invoke());

    ConfigureCanvas();
    EnsureEventSystem();
    PlayPaperSound();
    backgroundBlur.Initialize(modalCanvas);

    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
  }

  private void Update() {
    if (Time.frameCount == _openedFrame || IsClosing) return;
    if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

    _onCancel?.Invoke();
  }

  public void Close(Action onClosed) {
    if (IsClosing) return;

    PlayPaperSound();
    if (panelAnimation == null) {
      CompleteClose(onClosed);
      return;
    }

    panelAnimation.PlayReverse(() => CompleteClose(onClosed));
  }

  private void CompleteClose(Action onClosed) {
    onClosed?.Invoke();
    Destroy(gameObject);
  }

  private void PlayClickSound() {
    SceneTransitionManager.PlayUiSound(clickSound, mixerGroup);
  }

  private static void ConfigureTextAppearance(TMP_Text text) {
    text.color = Color.black;
    text.outlineColor = Color.white;
    text.outlineWidth = 0.18f;
  }

  private void ConfigureDepthIndependentRendering() {
    Graphic[] graphics = modalCanvas.GetComponentsInChildren<Graphic>(true);
    foreach (Graphic graphic in graphics) {
      Material source = graphic.material != null ? graphic.material : graphic.defaultMaterial;
      if (source == null) continue;

      Material material = new(source) {
        name = $"{source.name} (Modal Depth Override)",
        hideFlags = HideFlags.DontSave
      };
      if (material.HasProperty("unity_GUIZTestMode")) {
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
      }
      if (material.HasProperty("_ZTestMode")) {
        material.SetInt("_ZTestMode", (int)CompareFunction.Always);
      }
      if (material.HasProperty("_ZTest")) {
        material.SetInt("_ZTest", (int)CompareFunction.Always);
      }
      graphic.material = material;
      _depthOverrideMaterials.Add(material);
    }
  }

  private void PlayPaperSound() {
    if (paperAudioSource == null || paperSound == null) return;

    paperAudioSource.Stop();
    paperAudioSource.clip = paperSound;
    paperAudioSource.time = Mathf.Clamp(
      PaperSoundStartOffset,
      0f,
      Mathf.Max(0f, paperSound.length - 0.001f)
    );
    paperAudioSource.Play();
  }

  private void ConfigureCanvas() {
    Camera renderCamera = Camera.main;
    if (renderCamera == null) renderCamera = FindFirstObjectByType<Camera>();

    if (renderCamera != null) {
      modalCanvas.renderMode = RenderMode.ScreenSpaceCamera;
      modalCanvas.worldCamera = renderCamera;
      modalCanvas.planeDistance = 0.30003f;
    } else {
      modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    modalCanvas.sortingOrder = 1000;
  }

  private void OnDestroy() {
    foreach (Material material in _depthOverrideMaterials) {
      if (material != null) Destroy(material);
    }
    _depthOverrideMaterials.Clear();
  }

  private void EnsureEventSystem() {
    EventSystem eventSystem = EventSystem.current;
    if (eventSystem == null) {
      var eventSystemObject = new GameObject("ModalEventSystem");
      eventSystemObject.transform.SetParent(transform, false);
      eventSystem = eventSystemObject.AddComponent<EventSystem>();

      InputSystemUIInputModule inputModule =
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
      inputModule.AssignDefaultActions();
      return;
    }

    eventSystem.enabled = true;
    InputSystemUIInputModule existingInputModule =
      eventSystem.GetComponent<InputSystemUIInputModule>();
    if (existingInputModule != null) existingInputModule.enabled = true;
  }
}
