using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PassagewayTestSuite {
  private const string ProtoSceneName = "ProtoScene";
  private const float SceneLoadTimeoutSeconds = 5f;

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

  [UnityTest]
  public IEnumerator PassagewayDoor_TrySetOpen_AnimatesPanelsAndUpdatesBlockingCollider() {
    PassagewayDoor door = CreateDoor("SlidingDoor", Vector3.zero, out Transform leftPanel, out Transform rightPanel, out BoxCollider blockingCollider);

    Assert.IsTrue(door.TrySetOpen(true, null));
    yield return WaitForDoorState(door, true);

    Assert.IsTrue(door.IsOpen);
    Assert.AreEqual(0.75f, leftPanel.localPosition.x, 0.01f);
    Assert.AreEqual(-0.75f, rightPanel.localPosition.x, 0.01f);
    Assert.IsTrue(blockingCollider.isTrigger);

    Assert.IsTrue(door.TrySetOpen(false, null));
    yield return WaitForDoorState(door, false);

    Assert.IsFalse(door.IsOpen);
    Assert.AreEqual(0f, leftPanel.localPosition.x, 0.01f);
    Assert.AreEqual(0f, rightPanel.localPosition.x, 0.01f);
    Assert.IsFalse(blockingCollider.isTrigger);
  }

  [Test]
  public void PassagewayDoor_CanToggle_UsesItemRequirements() {
    PassagewayDoor door = CreateDoor("LockedDoor", Vector3.zero, out _, out _, out _);
    PlayerInventory inventory = CreateInventory(maxItems: 1);

    SetPrivateField(door, "requiresItemToOpen", true);
    SetPrivateField(door, "requiredItemId", "door_key");

    Assert.IsFalse(door.CanToggle(inventory));

    inventory.TryPickUp(CreateItem("DOOR_KEY"));

    Assert.IsTrue(door.CanToggle(inventory));
  }

  [Test]
  public void PassagewayDoor_TrySetOpen_RejectsMissingRequiredItem() {
    PassagewayDoor door = CreateDoor("LockedDoor", Vector3.zero, out _, out _, out _);

    SetPrivateField(door, "requiresItemToOpen", true);
    SetPrivateField(door, "requiredItemId", "door_key");

    LogAssert.Expect(LogType.Warning, "LockedDoor: This door requires item 'door_key' to open.");

    Assert.IsFalse(door.TrySetOpen(true, null));
    Assert.IsFalse(door.IsOpen);
  }

  [Test]
  public void PassagewayDoor_SetHighlighted_AppliesUsableAndLockedColors() {
    PassagewayDoor door = CreateDoor("HighlightDoor", Vector3.zero, out _, out _, out _);
    Renderer renderer = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
    _createdObjects.Add(renderer.gameObject);

    Color usableColor = new(0.1f, 0.8f, 0.2f);
    Color lockedColor = new(0.8f, 0.1f, 0.2f);
    SetPrivateField(door, "highlightRenderers", new[] { renderer });
    SetPrivateField(door, "usableHighlightColor", usableColor);
    SetPrivateField(door, "lockedHighlightColor", lockedColor);

    door.SetHighlighted(true, true);
    AssertColorsEqual(usableColor, renderer.material.color);

    door.SetHighlighted(true, false);
    AssertColorsEqual(lockedColor, renderer.material.color);
  }

  [Test]
  public void PassagewayDoor_GetInteractionDistance_UsesBlockingColliderClosestPoint() {
    PassagewayDoor door = CreateDoor("DistanceDoor", Vector3.zero, out _, out _, out _);

    float distance = door.GetInteractionDistance(new Vector3(3f, 0f, 0f));

    Assert.AreEqual(2.5f, distance, 0.05f);
  }

  [Test]
  public void PlayerDoorInteractor_FindsNearestDoorInRange() {
    PlayerDoorInteractor interactor = CreateDoorInteractor(Vector3.zero, out _);
    PassagewayDoor nearDoor = CreateDoor("NearDoor", new Vector3(0.25f, 0f, 0f), out _, out _, out _);
    PassagewayDoor farDoor = CreateDoor("FarDoor", new Vector3(1.25f, 0f, 0f), out _, out _, out _);

    SetPrivateField(interactor, "interactionRange", 2f);
    SetPrivateField(interactor, "doors", new[] { farDoor, nearDoor });

    PassagewayDoor foundDoor = (PassagewayDoor)InvokePrivate(interactor, "FindNearestDoorInRange");

    Assert.AreSame(nearDoor, foundDoor);
  }

  [Test]
  public void PlayerDoorInteractor_CanUseDoor_ReflectsDoorRequirements() {
    PlayerDoorInteractor interactor = CreateDoorInteractor(Vector3.zero, out PlayerInventory inventory);
    PassagewayDoor door = CreateDoor("RequiredDoor", new Vector3(0.25f, 0f, 0f), out _, out _, out _);
    SetPrivateField(door, "requiresItemToOpen", true);
    SetPrivateField(door, "requiredItemId", "door_key");

    Assert.IsFalse((bool)InvokePrivate(interactor, "CanUseDoor", door));

    inventory.TryPickUp(CreateItem("door_key"));

    Assert.IsTrue((bool)InvokePrivate(interactor, "CanUseDoor", door));
  }

  [UnityTest]
  public IEnumerator ProtoScenePlayer_WithoutMoving_CanOpenDoorAndCloseIt() {
    yield return LoadScene(ProtoSceneName);

    GameObject playerGO = GameObject.Find("Player");
    Assert.IsNotNull(playerGO, "ProtoScene should contain a root Player object.");

    Vector3 spawnPosition = playerGO.transform.position;
    PlayerDoorInteractor interactor = playerGO.GetComponent<PlayerDoorInteractor>();
    PlayerInventory inventory = playerGO.GetComponent<PlayerInventory>();

    Assert.IsNotNull(interactor, "ProtoScene Player should have a PlayerDoorInteractor.");
    Assert.IsNotNull(inventory, "ProtoScene Player should have a PlayerInventory.");

    PassagewayDoor door = CreateDoor("ProtoSceneTestDoor", spawnPosition + Vector3.right * 0.5f, out _, out _, out _);
    SetPrivateField(interactor, "inventory", inventory);
    SetPrivateField(interactor, "interactionRange", 2f);
    SetPrivateField(interactor, "doors", new[] { door });
    SetPrivateField(interactor, "passagewayLayer", (LayerMask)0);

    ToggleNearestDoorThroughInteractor(interactor, inventory);
    yield return WaitForDoorState(door, true);

    Assert.IsTrue(door.IsOpen);
    Assert.LessOrEqual(Vector3.Distance(spawnPosition, playerGO.transform.position), 0.001f, "The player should not move while opening the test door.");

    ToggleNearestDoorThroughInteractor(interactor, inventory);
    yield return WaitForDoorState(door, false);

    Assert.IsFalse(door.IsOpen);
    Assert.LessOrEqual(Vector3.Distance(spawnPosition, playerGO.transform.position), 0.001f, "The player should not move while closing the test door.");
  }

  private PassagewayDoor CreateDoor(string name, Vector3 position, out Transform leftPanel, out Transform rightPanel, out BoxCollider blockingCollider) {
    GameObject doorGO = CreateGameObject(name);
    doorGO.transform.position = position;

    leftPanel = CreateChild(doorGO.transform, "LeftPanel");
    rightPanel = CreateChild(doorGO.transform, "RightPanel");
    blockingCollider = CreateChild(doorGO.transform, "BlockingCollider").gameObject.AddComponent<BoxCollider>();
    blockingCollider.size = Vector3.one;

    PassagewayDoor door = doorGO.AddComponent<PassagewayDoor>();
    SetPrivateField(door, "blockingCollider", blockingCollider);
    SetPrivateField(door, "animationDuration", 0.02f);
    SetPrivateField(door, "panelSlideDistance", 0.75f);
    InvokePrivate(door, "Awake");

    return door;
  }

  private PlayerDoorInteractor CreateDoorInteractor(Vector3 position, out PlayerInventory inventory) {
    GameObject playerGO = CreateGameObject("DoorInteractorPlayer");
    playerGO.SetActive(false);
    playerGO.transform.position = position;

    inventory = playerGO.AddComponent<PlayerInventory>();
    RightAngleWallTurner turner = playerGO.AddComponent<RightAngleWallTurner>();
    PlayerDoorInteractor interactor = playerGO.AddComponent<PlayerDoorInteractor>();
    Transform cameraPivot = CreateGameObject("CameraPivot").transform;
    turner.camPivot = cameraPivot;

    SetPrivateField(interactor, "inventory", inventory);
    SetPrivateField(interactor, "rightAngleWallTurner", turner);
    SetPrivateField(interactor, "passagewayLayer", (LayerMask)0);
    SetPrivateField(interactor, "doors", new PassagewayDoor[0]);
    InvokePrivate(interactor, "Awake");
    playerGO.SetActive(true);

    return interactor;
  }

  private void ToggleNearestDoorThroughInteractor(PlayerDoorInteractor interactor, PlayerInventory inventory) {
    PassagewayDoor nearestDoor = (PassagewayDoor)InvokePrivate(interactor, "FindNearestDoorInRange");
    Assert.IsNotNull(nearestDoor, "The spawned player should have a reachable test door.");
    Assert.IsTrue((bool)InvokePrivate(interactor, "CanUseDoor", nearestDoor));
    Assert.IsTrue(nearestDoor.TryToggle(inventory));
  }

  private IEnumerator WaitForDoorState(PassagewayDoor door, bool isOpen) {
    var deadline = Time.realtimeSinceStartup + 1f;

    while (Time.realtimeSinceStartup < deadline) {
      if (door.IsOpen == isOpen) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting for door '{door.name}' to become {(isOpen ? "open" : "closed")}.");
  }

  private static IEnumerator LoadScene(string sceneName) {
    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

    Assert.IsNotNull(loadOperation, $"Failed to start loading scene '{sceneName}'.");

    yield return loadOperation;
    yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
  }

  private static IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds) {
    var deadline = Time.realtimeSinceStartup + timeoutSeconds;

    while (Time.realtimeSinceStartup < deadline) {
      if (SceneManager.GetActiveScene().name == sceneName) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting {timeoutSeconds:0.##} seconds for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'.");
  }

  private PlayerInventory CreateInventory(int maxItems) {
    GameObject inventoryGO = CreateGameObject("Inventory");
    PlayerInventory inventory = inventoryGO.AddComponent<PlayerInventory>();
    SetPrivateField(inventory, "maxItems", maxItems);
    return inventory;
  }

  private ItemDefinition CreateItem(string itemId) {
    ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
    item.itemId = itemId;
    _createdObjects.Add(item);
    return item;
  }

  private Transform CreateChild(Transform parent, string name) {
    Transform child = CreateGameObject(name).transform;
    child.parent = parent;
    child.localPosition = Vector3.zero;
    child.localRotation = Quaternion.identity;
    child.localScale = Vector3.one;
    return child;
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

  private static object InvokePrivate(object target, string methodName, params object[] parameters) {
    return target.GetType()
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(target, parameters);
  }

  private static void AssertColorsEqual(Color expected, Color actual) {
    Assert.AreEqual(expected.r, actual.r, 0.001f);
    Assert.AreEqual(expected.g, actual.g, 0.001f);
    Assert.AreEqual(expected.b, actual.b, 0.001f);
    Assert.AreEqual(expected.a, actual.a, 0.001f);
  }
}
