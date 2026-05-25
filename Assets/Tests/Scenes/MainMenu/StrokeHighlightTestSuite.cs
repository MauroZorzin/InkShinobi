using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class StrokeHighlightTestSuite {
  private readonly List<Object> _createdObjects = new();

  [TearDown]
  public void TearDown() {
    foreach (Object createdObject in _createdObjects) {
      if (createdObject != null) {
        Object.DestroyImmediate(createdObject);
      }
    }

    _createdObjects.Clear();
  }

  [Test]
  public void Reset_AssignsButtonAndAudioSource() {
    StrokeHighlight highlight = CreateStrokeHighlight(out Button button, out _, out AudioSource audioSource);
    SetPrivateField(highlight, "button", null);
    SetPrivateField(highlight, "audioSource", null);

    InvokePrivate(highlight, "Reset");

    Assert.AreSame(button, GetPrivateField<Button>(highlight, "button"));
    Assert.AreSame(audioSource, GetPrivateField<AudioSource>(highlight, "audioSource"));
  }

  [Test]
  public void Awake_HidesBrushStrokeImmediately() {
    StrokeHighlight highlight = CreateStrokeHighlight(out _, out Image brushStroke, out _);
    brushStroke.enabled = true;
    brushStroke.fillAmount = 1f;

    InvokePrivate(highlight, "Awake");

    Assert.IsFalse(brushStroke.enabled);
    Assert.AreEqual(0f, brushStroke.fillAmount, 0.001f);
  }

  [UnityTest]
  public IEnumerator OnPointerEnter_WhenButtonInteractable_PaintsStrokeIn() {
    StrokeHighlight highlight = CreateStrokeHighlight(out _, out Image brushStroke, out _);
    SetPrivateField(highlight, "paintInDuration", 0.01f);

    highlight.OnPointerEnter(CreatePointerEventData());

    yield return new WaitForSecondsRealtime(0.05f);

    Assert.IsTrue(brushStroke.enabled);
    Assert.AreEqual(Image.Type.Filled, brushStroke.type);
    Assert.AreEqual(Image.FillMethod.Horizontal, brushStroke.fillMethod);
    Assert.AreEqual(1f, brushStroke.fillAmount, 0.001f);
  }

  [UnityTest]
  public IEnumerator OnPointerExit_FadesStrokeOutAndDisablesIt() {
    StrokeHighlight highlight = CreateStrokeHighlight(out _, out Image brushStroke, out _);
    SetPrivateField(highlight, "fadeOutDuration", 0.01f);
    brushStroke.enabled = true;
    brushStroke.fillAmount = 1f;

    highlight.OnPointerExit(CreatePointerEventData());

    yield return new WaitForSecondsRealtime(0.05f);

    Assert.IsFalse(brushStroke.enabled);
    Assert.AreEqual(0f, brushStroke.fillAmount, 0.001f);
  }

  [UnityTest]
  public IEnumerator OnPointerEnter_WhenButtonNotInteractable_DoesNotShowStroke() {
    StrokeHighlight highlight = CreateStrokeHighlight(out Button button, out Image brushStroke, out _);
    button.interactable = false;

    highlight.OnPointerEnter(CreatePointerEventData());

    yield return null;

    Assert.IsFalse(brushStroke.enabled);
    Assert.AreEqual(0f, brushStroke.fillAmount, 0.001f);
  }

  private StrokeHighlight CreateStrokeHighlight(out Button button, out Image brushStroke, out AudioSource audioSource) {
    GameObject buttonGO = CreateGameObject("Button");
    button = buttonGO.AddComponent<Button>();
    audioSource = buttonGO.AddComponent<AudioSource>();
    StrokeHighlight highlight = buttonGO.AddComponent<StrokeHighlight>();

    GameObject brushGO = CreateGameObject("BrushStroke");
    brushGO.transform.SetParent(buttonGO.transform, false);
    brushStroke = brushGO.AddComponent<Image>();

    SetPrivateField(highlight, "button", button);
    SetPrivateField(highlight, "brushStroke", brushStroke);
    SetPrivateField(highlight, "audioSource", audioSource);
    InvokePrivate(highlight, "Awake");

    return highlight;
  }

  private PointerEventData CreatePointerEventData() {
    EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
    if (eventSystem == null) {
      eventSystem = CreateGameObject("EventSystem").AddComponent<EventSystem>();
    }

    return new PointerEventData(eventSystem);
  }

  private GameObject CreateGameObject(string name) {
    GameObject gameObject = new(name);
    _createdObjects.Add(gameObject);
    return gameObject;
  }

  private static void SetPrivateField(object target, string fieldName, object value) {
    target.GetType()
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(target, value);
  }

  private static T GetPrivateField<T>(object target, string fieldName) {
    return (T)target.GetType()
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(target);
  }

  private static object InvokePrivate(object target, string methodName, params object[] parameters) {
    return target.GetType()
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(target, parameters);
  }
}
