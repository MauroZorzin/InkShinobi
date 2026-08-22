using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

#pragma warning disable UDR0001

[InitializeOnLoad]
public static class ConfirmationModalPrefabBuilder {
  private const string PrefabPath = "Assets/Resources/ConfirmationModal.prefab";

  static ConfirmationModalPrefabBuilder() {
    EditorApplication.delayCall += EnsurePrefabExists;
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
  }

  private static void OnPlayModeStateChanged(PlayModeStateChange state) {
    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += EnsurePrefabExists;
  }

  [MenuItem("Tools/Ink Shinobi/Rebuild Confirmation Modal Prefab")]
  public static void RebuildPrefab() {
    BuildPrefab();
  }

  private static void EnsurePrefabExists() {
    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
    if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;

    BuildPrefab();
  }

  private static void BuildPrefab() {
    Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

    TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
      "Assets/Art/UI/Fonts/Kipish_Regular_SDF.asset"
    );
    Sprite parchment = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/parchment.png");
    Sprite brush = AssetDatabase.LoadAssetAtPath<Sprite>(
      "Assets/Art/UI/Buttons/stroke_highlight.png"
    );
    AudioClip hoverSound = AssetDatabase.LoadAssetAtPath<AudioClip>(
      "Assets/Art/Audio/SFX/button_hover.mp3"
    );
    AudioClip clickSound = AssetDatabase.LoadAssetAtPath<AudioClip>(
      "Assets/Art/Audio/SFX/button_click.wav"
    );
    AudioClip paperSound = AssetDatabase.LoadAssetAtPath<AudioClip>(
      "Assets/Art/Audio/SFX/paper-uncrumpling.mp3"
    );
    AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(
      "Assets/Art/Audio/Music/Mixer_Main.mixer"
    );
    AudioMixerGroup mixerGroup = mixer != null
      ? System.Array.Find(mixer.FindMatchingGroups(string.Empty), group => group.name == "FX")
      : null;

    GameObject root = new("ConfirmationModal", typeof(RectTransform));
    SetLayerRecursively(root, 5);

    Canvas canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 1000;
    CanvasScaler scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    root.AddComponent<GraphicRaycaster>();
    PopupBackgroundBlur blur = root.AddComponent<PopupBackgroundBlur>();

    AudioSource paperSource = root.AddComponent<AudioSource>();
    ConfigureAudioSource(paperSource, mixerGroup);

    Image shade = CreateImage("Shade", root.transform, new Color(0f, 0f, 0f, 0.55f));
    Stretch(shade.rectTransform);

    Image panel = CreateImage("Panel", shade.transform, Color.white);
    panel.sprite = parchment;
    panel.type = Image.Type.Sliced;
    panel.pixelsPerUnitMultiplier = 2f;
    panel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    panel.rectTransform.sizeDelta = new Vector2(940f, 480f);
    panel.rectTransform.anchoredPosition = Vector2.zero;
    panel.gameObject.AddComponent<CanvasGroup>();
    ModalAppearAnimation animation = panel.gameObject.AddComponent<ModalAppearAnimation>();

    TextMeshProUGUI title = CreateText(
      "Title",
      panel.transform,
      font,
      72f,
      new Vector2(0f, 130f),
      new Vector2(840f, 110f),
      FontStyles.Bold | FontStyles.SmallCaps
    );
    TextMeshProUGUI message = CreateText(
      "Message",
      panel.transform,
      font,
      42f,
      new Vector2(0f, 20f),
      new Vector2(820f, 150f),
      FontStyles.Bold | FontStyles.SmallCaps
    );

    Button cancel = CreateButton(
      "Cancel",
      panel.transform,
      font,
      brush,
      hoverSound,
      mixerGroup,
      new Vector2(-205f, -145f),
      out TextMeshProUGUI cancelLabel
    );
    Button confirm = CreateButton(
      "Confirm",
      panel.transform,
      font,
      brush,
      hoverSound,
      mixerGroup,
      new Vector2(205f, -145f),
      out TextMeshProUGUI confirmLabel
    );

    ConfirmationModalView view = root.AddComponent<ConfirmationModalView>();
    SerializedObject serializedView = new(view);
    SetReference(serializedView, "modalCanvas", canvas);
    SetReference(serializedView, "backgroundBlur", blur);
    SetReference(serializedView, "panelAnimation", animation);
    SetReference(serializedView, "titleLabel", title);
    SetReference(serializedView, "messageLabel", message);
    SetReference(serializedView, "cancelButton", cancel);
    SetReference(serializedView, "cancelButtonLabel", cancelLabel);
    SetReference(serializedView, "confirmButton", confirm);
    SetReference(serializedView, "confirmButtonLabel", confirmLabel);
    SetReference(serializedView, "clickSound", clickSound);
    SetReference(serializedView, "paperSound", paperSound);
    SetReference(serializedView, "mixerGroup", mixerGroup);
    SetReference(serializedView, "paperAudioSource", paperSource);
    serializedView.ApplyModifiedPropertiesWithoutUndo();

    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    Object.DestroyImmediate(root);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    Debug.Log($"[ConfirmationModalPrefabBuilder] Created {PrefabPath}.");
  }

  private static Button CreateButton(
    string objectName,
    Transform parent,
    TMP_FontAsset font,
    Sprite brushSprite,
    AudioClip hoverSound,
    AudioMixerGroup mixerGroup,
    Vector2 position,
    out TextMeshProUGUI label
  ) {
    Image hitArea = CreateImage(objectName, parent, Color.clear);
    hitArea.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    hitArea.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    hitArea.rectTransform.sizeDelta = new Vector2(320f, 80f);
    hitArea.rectTransform.anchoredPosition = position;

    Button button = hitArea.gameObject.AddComponent<Button>();
    button.transition = Selectable.Transition.None;
    button.targetGraphic = hitArea;

    Image highlight = CreateImage(
      "Highlight",
      button.transform,
      new Color(0.7647059f, 0f, 0.11764706f, 1f)
    );
    Stretch(highlight.rectTransform);
    highlight.rectTransform.anchoredPosition = new Vector2(35f, 0f);
    highlight.sprite = brushSprite;
    highlight.type = Image.Type.Filled;
    highlight.fillMethod = Image.FillMethod.Horizontal;
    highlight.fillOrigin = (int)Image.OriginHorizontal.Left;
    highlight.fillAmount = 0f;
    highlight.raycastTarget = false;

    label = CreateText(
      "Label",
      button.transform,
      font,
      46f,
      Vector2.zero,
      hitArea.rectTransform.sizeDelta,
      FontStyles.SmallCaps
    );
    label.enableAutoSizing = true;
    label.fontSizeMin = 24f;
    label.fontSizeMax = 46f;

    AudioSource source = button.gameObject.AddComponent<AudioSource>();
    ConfigureAudioSource(source, mixerGroup);

    StrokeHighlight stroke = button.gameObject.AddComponent<StrokeHighlight>();
    SerializedObject serializedStroke = new(stroke);
    SetReference(serializedStroke, "button", button);
    SetReference(serializedStroke, "brushStroke", highlight);
    SetReference(serializedStroke, "buttonLabel", label);
    SetReference(serializedStroke, "audioSource", source);
    SetReference(serializedStroke, "hoverSound", hoverSound);
    serializedStroke.FindProperty("paintInDuration").floatValue = 0.25f;
    serializedStroke.FindProperty("fadeOutDuration").floatValue = 0f;
    serializedStroke.FindProperty("_useWhiteTextOnHover").boolValue = false;
    serializedStroke.FindProperty("_useWhiteTextOnSelection").boolValue = false;
    serializedStroke.ApplyModifiedPropertiesWithoutUndo();
    return button;
  }

  private static TextMeshProUGUI CreateText(
    string objectName,
    Transform parent,
    TMP_FontAsset font,
    float fontSize,
    Vector2 position,
    Vector2 size,
    FontStyles fontStyle
  ) {
    GameObject textObject = new(objectName, typeof(RectTransform));
    textObject.transform.SetParent(parent, false);
    textObject.layer = 5;

    TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
    text.font = font;
    text.fontSize = fontSize;
    text.fontStyle = fontStyle;
    text.color = Color.black;
    text.outlineColor = Color.white;
    text.outlineWidth = 0.18f;
    text.alignment = TextAlignmentOptions.Center;
    text.raycastTarget = false;

    RectTransform rect = text.rectTransform;
    rect.anchorMin = new Vector2(0.5f, 0.5f);
    rect.anchorMax = new Vector2(0.5f, 0.5f);
    rect.sizeDelta = size;
    rect.anchoredPosition = position;
    return text;
  }

  private static Image CreateImage(string objectName, Transform parent, Color color) {
    GameObject imageObject = new(objectName, typeof(RectTransform));
    imageObject.transform.SetParent(parent, false);
    imageObject.layer = 5;
    Image image = imageObject.AddComponent<Image>();
    image.color = color;
    return image;
  }

  private static void ConfigureAudioSource(AudioSource source, AudioMixerGroup mixerGroup) {
    source.playOnAwake = false;
    source.loop = false;
    source.spatialBlend = 0f;
    source.ignoreListenerPause = true;
    source.outputAudioMixerGroup = mixerGroup;
  }

  private static void Stretch(RectTransform rect) {
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
  }

  private static void SetReference(SerializedObject serialized, string property, Object value) {
    serialized.FindProperty(property).objectReferenceValue = value;
  }

  private static void SetLayerRecursively(GameObject root, int layer) {
    root.layer = layer;
    foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
  }
}

#pragma warning restore UDR0001
