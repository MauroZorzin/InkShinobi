using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

public class PatrolAreaTestSuite {
  private GameObject _areaGO;
  private PatrolArea _patrolArea;
  private BoxCollider _boxCollider;

  [SetUp]
  public void Setup() {
    _areaGO = new GameObject("PatrolArea");
    _areaGO.transform.position = new Vector3(10f, 0f, -5f);

    _boxCollider = _areaGO.AddComponent<BoxCollider>();
    _boxCollider.center = Vector3.zero;
    _boxCollider.size = new Vector3(4f, 2f, 6f);

    _patrolArea = _areaGO.AddComponent<PatrolArea>();
    SetPrivateField("boxCollider", _boxCollider);
  }

  [TearDown]
  public void TearDown() {
    if (_areaGO != null) {
      Object.Destroy(_areaGO);
    }
  }

  [Test]
  public void Reset_AssignsColliderAndSetsTrigger() {
    _boxCollider.isTrigger = false;
    SetPrivateField("boxCollider", null);

    InvokePrivate("Reset");

    BoxCollider assigned = GetPrivateField<BoxCollider>("boxCollider");
    Assert.AreSame(_boxCollider, assigned, "Reset should cache the existing BoxCollider.");
    Assert.IsTrue(_boxCollider.isTrigger, "Reset should configure the collider as a trigger.");
  }

  [Test]
  public void Awake_AssignsCollider_WhenFieldIsNull() {
    SetPrivateField("boxCollider", null);

    InvokePrivate("Awake");

    BoxCollider assigned = GetPrivateField<BoxCollider>("boxCollider");
    Assert.AreSame(_boxCollider, assigned, "Awake should cache the existing BoxCollider when missing.");
  }

  [Test]
  public void ContainsPoint_ReturnsTrue_ForPointInsideBounds_IgnoresY() {
    Vector3 insidePoint = _areaGO.transform.TransformPoint(new Vector3(1f, 100f, 2f));

    var result = _patrolArea.ContainsPoint(insidePoint);

    Assert.IsTrue(result, "ContainsPoint should only evaluate X/Z and accept points inside the area.");
  }

  [Test]
  public void ContainsPoint_ReturnsFalse_ForPointOutsideBounds() {
    Vector3 outsidePoint = _areaGO.transform.TransformPoint(new Vector3(2.1f, 0f, 0f));

    var result = _patrolArea.ContainsPoint(outsidePoint);

    Assert.IsFalse(result, "ContainsPoint should reject points outside collider X/Z bounds.");
  }

  [Test]
  public void ContainsPoint_IncludesBoundaryPoints() {
    Vector3 boundaryPoint = _areaGO.transform.TransformPoint(new Vector3(2f, 0f, -3f));

    var result = _patrolArea.ContainsPoint(boundaryPoint);

    Assert.IsTrue(result, "ContainsPoint should include points exactly on the boundary.");
  }

  [Test]
  public void TryGetRandomPointOnNavMesh_MaxAttemptsZero_ReturnsFalseAndFallbackPosition() {
    var found = _patrolArea.TryGetRandomPointOnNavMesh(out Vector3 point, 1f, NavMesh.AllAreas, 0);

    Assert.IsFalse(found, "The method should fail immediately when maxAttempts is zero.");
    Assert.AreEqual(_areaGO.transform.position, point, "Failure path should return the patrol area's transform position.");
  }

  private void SetPrivateField(string fieldName, object value) {
    typeof(PatrolArea)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_patrolArea, value);
  }

  private T GetPrivateField<T>(string fieldName) {
    return (T)typeof(PatrolArea)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_patrolArea);
  }

  private void InvokePrivate(string methodName) {
    typeof(PatrolArea)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_patrolArea, null);
  }
}
