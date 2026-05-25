using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class KillConeTestSuite {
  private const int PlayerLayer = 8;
  private const int OtherLayer = 9;

  private GameObject _killConeObject;
  private KillCone _killCone;
  private readonly List<GameObject> _colliderObjects = new();

  [SetUp]
  public void SetUp() {
    _killConeObject = new GameObject("KillCone");
    _killConeObject.AddComponent<BoxCollider>().isTrigger = true;
    _killCone = _killConeObject.AddComponent<KillCone>();
    SetField("playerLayer", (LayerMask)(1 << PlayerLayer));
    SetField("reloadDelay", 100f);
  }

  [TearDown]
  public void TearDown() {
    if (_killCone != null) {
      _killCone.CancelInvoke();
    }

    if (_killConeObject != null) {
      UnityEngine.Object.DestroyImmediate(_killConeObject);
    }

    foreach (GameObject colliderObject in _colliderObjects) {
      if (colliderObject != null) {
        UnityEngine.Object.DestroyImmediate(colliderObject);
      }
    }

    _colliderObjects.Clear();
  }

  [Test]
  public void OnTriggerEnter_IgnoresColliderOutsidePlayerLayer() {
    GameObject other = CreateColliderObject("Other", OtherLayer);

    InvokeTrigger(other.GetComponent<Collider>());

    Assert.IsFalse(_killCone.IsInvoking("ReloadScene"));
  }

  [Test]
  public void OnTriggerEnter_PlayerLayerSchedulesReloadWhenDelayIsConfigured() {
    GameObject player = CreateColliderObject("Player", PlayerLayer);

    InvokeTrigger(player.GetComponent<Collider>());

    Assert.IsTrue(_killCone.IsInvoking("ReloadScene"));
  }

  private GameObject CreateColliderObject(string name, int layer) {
    GameObject colliderObject = new(name) {
      layer = layer
    };

    colliderObject.AddComponent<BoxCollider>();
    _colliderObjects.Add(colliderObject);
    return colliderObject;
  }

  private void InvokeTrigger(Collider collider) {
    typeof(KillCone)
      .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
      .Invoke(_killCone, new object[] { collider });
  }

  private void SetField<T>(string fieldName, T value) {
    typeof(KillCone)
      .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
      .SetValue(_killCone, value);
  }
}
