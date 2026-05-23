using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class InventoryTestSuite {
  private const int InteractableLayer = 8;

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
  public void TryPickUp_AddsItem_SelectsIt_AndRaisesSelectionEvent() {
    PlayerInventory inventory = CreateInventory(maxItems: 2);
    ItemDefinition item = CreateItem("key", "Key");
    ItemDefinition selectedFromEvent = null;

    inventory.OnSelectedItemChanged += selectedItem => selectedFromEvent = selectedItem;

    Assert.IsTrue(inventory.TryPickUp(item));
    Assert.AreSame(item, inventory.SelectedItem);
    Assert.AreSame(item, selectedFromEvent);
    Assert.IsTrue(inventory.HasItems);
  }

  [Test]
  public void TryPickUp_RejectsNullAndFullInventory() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    ItemDefinition firstItem = CreateItem("first", "First");
    ItemDefinition secondItem = CreateItem("second", "Second");

    Assert.IsFalse(inventory.TryPickUp(null));
    Assert.IsTrue(inventory.TryPickUp(firstItem));

    LogAssert.Expect(LogType.Log, "Inventory is full.");

    Assert.IsFalse(inventory.TryPickUp(secondItem));
    Assert.AreSame(firstItem, inventory.SelectedItem);
  }

  [Test]
  public void HasItem_IsCaseInsensitive_AndEmptyRequirementPasses() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    inventory.TryPickUp(CreateItem("Door_Key", "Door Key"));

    Assert.IsTrue(inventory.HasItem("door_key"));
    Assert.IsTrue(inventory.HasItem(""));
    Assert.IsTrue(inventory.HasItem(null));
    Assert.IsFalse(inventory.HasItem("missing"));
  }

  [Test]
  public void Selection_WrapsAndUpdatesAfterRemoval() {
    PlayerInventory inventory = CreateInventory(maxItems: 3);
    ItemDefinition firstItem = CreateItem("first", "First");
    ItemDefinition secondItem = CreateItem("second", "Second");
    ItemDefinition thirdItem = CreateItem("third", "Third");

    inventory.TryPickUp(firstItem);
    inventory.TryPickUp(secondItem);
    inventory.TryPickUp(thirdItem);

    inventory.SelectNext();
    Assert.AreSame(firstItem, inventory.SelectedItem);

    inventory.SelectPrevious();
    Assert.AreSame(thirdItem, inventory.SelectedItem);

    inventory.RemoveSelectedItem();
    Assert.AreSame(secondItem, inventory.SelectedItem);
  }

  [UnityTest]
  public IEnumerator PickableItem_TransfersItemAndDestroysItself() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    ItemDefinition item = CreateItem("coin", "Coin");
    PickableItem pickableItem = CreatePickableItem(item);
    GameObject pickableGO = pickableItem.gameObject;

    pickableItem.Interact(inventory);

    yield return null;

    Assert.AreSame(item, inventory.SelectedItem);
    Assert.IsTrue(pickableGO == null, "The picked-up world item should be destroyed after a successful transfer.");
  }

  [UnityTest]
  public IEnumerator PickableItem_DoesNotDestroy_WhenInventoryRejectsItem() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    inventory.TryPickUp(CreateItem("held", "Held"));
    PickableItem pickableItem = CreatePickableItem(CreateItem("extra", "Extra"));

    LogAssert.Expect(LogType.Log, "Inventory is full.");

    pickableItem.Interact(inventory);

    yield return null;

    Assert.IsFalse(pickableItem == null, "A rejected pickup should remain in the scene.");
  }

  [Test]
  public void PlayerInteractor_UsesClosestInteractableInRange() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    PlayerInteractor interactor = CreatePlayerInteractor(inventory, out Transform interactionPoint);
    RecordingInteractable farInteractable = CreateRecordingInteractable("Far", new Vector3(0.7f, 0f, 0f));
    RecordingInteractable nearInteractable = CreateRecordingInteractable("Near", new Vector3(0.25f, 0f, 0f));
    Physics.SyncTransforms();

    InvokePrivate(interactor, "TryInteract");

    Assert.AreEqual(0, farInteractable.InteractionCount);
    Assert.AreEqual(1, nearInteractable.InteractionCount);
    Assert.AreSame(inventory, nearInteractable.LastInventory);
    Assert.AreEqual(Vector3.zero, interactionPoint.position);
  }

  [Test]
  public void PlayerInteractor_DoesNothing_WhenDependenciesAreMissing() {
    GameObject playerGO = CreateGameObject("Player");
    PlayerInteractor interactor = playerGO.AddComponent<PlayerInteractor>();

    Assert.DoesNotThrow(() => InvokePrivate(interactor, "TryInteract"));
  }

  [Test]
  public void ItemIconRenderer_ShowItem_CreatesPreviewAndConfiguresCamera() {
    ItemIconRenderer renderer = CreateIconRenderer(out Camera previewCamera, out Transform previewRoot, out RenderTexture renderTexture);
    ItemDefinition item = CreateItem("statue", "Statue");
    item.iconPreviewPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
    _createdObjects.Add(item.iconPreviewPrefab);
    item.iconOffset = new Vector3(0.1f, 0.2f, 0.3f);
    item.iconOrthographicSize = 2.5f;

    renderer.ShowItem(item);

    Assert.AreEqual(1, previewRoot.childCount);
    Assert.AreSame(renderTexture, previewCamera.targetTexture);
    Assert.AreEqual(item.iconOrthographicSize, previewCamera.orthographicSize);
    Assert.IsTrue(previewCamera.enabled);
  }

  [UnityTest]
  public IEnumerator ItemIconRenderer_ShowNull_ClearsPreviewAndDisablesCamera() {
    ItemIconRenderer renderer = CreateIconRenderer(out Camera previewCamera, out Transform previewRoot, out _);
    ItemDefinition item = CreateItem("statue", "Statue");
    item.iconPreviewPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
    _createdObjects.Add(item.iconPreviewPrefab);

    renderer.ShowItem(item);
    renderer.ShowItem(null);
    yield return null;

    Assert.AreEqual(0, previewRoot.childCount);
    Assert.IsFalse(previewCamera.enabled);
  }

  [Test]
  public void ItemSlotUI_ReflectsSelectedInventoryItem() {
    PlayerInventory inventory = CreateInventory(maxItems: 1);
    ItemIconRenderer iconRenderer = CreateIconRenderer(out _, out _, out RenderTexture renderTexture);
    RawImage rawImage = CreateRawImage();
    ItemSlotUI slotUI = CreateSlotUI(inventory, rawImage, iconRenderer);

    InvokePrivate(slotUI, "Awake");
    Assert.IsFalse(rawImage.enabled);
    Assert.AreSame(renderTexture, rawImage.texture);

    inventory.TryPickUp(CreateItem("map", "Map"));
    InvokePrivate(slotUI, "OnEnable");

    Assert.IsTrue(rawImage.enabled);
    Assert.AreSame(renderTexture, rawImage.texture);
  }

  private PlayerInventory CreateInventory(int maxItems) {
    GameObject inventoryGO = CreateGameObject("Inventory");
    PlayerInventory inventory = inventoryGO.AddComponent<PlayerInventory>();
    SetPrivateField(inventory, "maxItems", maxItems);
    return inventory;
  }

  private PickableItem CreatePickableItem(ItemDefinition item) {
    GameObject pickableGO = CreateGameObject("PickableItem");
    PickableItem pickableItem = pickableGO.AddComponent<PickableItem>();
    SetPrivateField(pickableItem, "item", item);
    return pickableItem;
  }

  private PlayerInteractor CreatePlayerInteractor(PlayerInventory inventory, out Transform interactionPoint) {
    GameObject playerGO = CreateGameObject("PlayerInteractor");
    PlayerInteractor interactor = playerGO.AddComponent<PlayerInteractor>();
    interactionPoint = new GameObject("InteractionPoint").transform;
    interactionPoint.parent = playerGO.transform;
    _createdObjects.Add(interactionPoint.gameObject);

    SetPrivateField(interactor, "inventory", inventory);
    SetPrivateField(interactor, "interactionPoint", interactionPoint);
    SetPrivateField(interactor, "interactionRadius", 1f);
    SetPrivateField(interactor, "interactableLayer", (LayerMask)(1 << InteractableLayer));

    return interactor;
  }

  private RecordingInteractable CreateRecordingInteractable(string name, Vector3 position) {
    GameObject interactableGO = CreateGameObject(name);
    interactableGO.layer = InteractableLayer;
    interactableGO.transform.position = position;
    interactableGO.AddComponent<SphereCollider>();
    return interactableGO.AddComponent<RecordingInteractable>();
  }

  private ItemIconRenderer CreateIconRenderer(out Camera previewCamera, out Transform previewRoot, out RenderTexture renderTexture) {
    GameObject rendererGO = CreateGameObject("ItemIconRenderer");
    ItemIconRenderer renderer = rendererGO.AddComponent<ItemIconRenderer>();
    previewCamera = CreateGameObject("PreviewCamera").AddComponent<Camera>();
    previewRoot = CreateGameObject("PreviewRoot").transform;
    renderTexture = new RenderTexture(32, 32, 16);
    _createdObjects.Add(renderTexture);

    SetPrivateField(renderer, "previewCamera", previewCamera);
    SetPrivateField(renderer, "previewRoot", previewRoot);
    SetPrivateField(renderer, "renderTexture", renderTexture);
    SetPrivateField(renderer, "previewLayer", (LayerMask)(1 << InteractableLayer));

    return renderer;
  }

  private RawImage CreateRawImage() {
    GameObject imageGO = CreateGameObject("RawImage");
    return imageGO.AddComponent<RawImage>();
  }

  private ItemSlotUI CreateSlotUI(PlayerInventory inventory, RawImage itemIcon, ItemIconRenderer itemIconRenderer) {
    GameObject slotGO = CreateGameObject("ItemSlotUI");
    slotGO.SetActive(false);
    ItemSlotUI slotUI = slotGO.AddComponent<ItemSlotUI>();

    SetPrivateField(slotUI, "inventory", inventory);
    SetPrivateField(slotUI, "itemIcon", itemIcon);
    SetPrivateField(slotUI, "itemIconRenderer", itemIconRenderer);

    return slotUI;
  }

  private ItemDefinition CreateItem(string itemId, string displayName) {
    ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
    item.itemId = itemId;
    item.displayName = displayName;
    _createdObjects.Add(item);
    return item;
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

  private static object InvokePrivate(object target, string methodName) {
    return target.GetType()
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(target, null);
  }

  private sealed class RecordingInteractable : MonoBehaviour, IInteractable {
    public int InteractionCount { get; private set; }
    public PlayerInventory LastInventory { get; private set; }

    public void Interact(PlayerInventory inventory) {
      InteractionCount++;
      LastInventory = inventory;
    }
  }
}
