using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Replaces LineVisionController. This component does not own or move the camera at all — it
/// just reads wherever the camera (aimCameraTransform, or Camera.main if left empty) is
/// currently pointed (AimOrigin/AimDirection) and checks whether that's close to a LinePath. On
/// confirm, hand off to LineSwitcher exactly as before.
///
/// Deliberately does NOT touch the player's position or rotation, and does NOT touch the
/// camera's position or rotation — this is purely a read-only consumer of wherever the camera
/// already is, so it never fights whatever script (if any) is driving the camera.
/// </summary>
[RequireComponent(typeof(LineSwitcher))]
[RequireComponent(typeof(LineFollowController))]
public class LineAimSwitchController : MonoBehaviour {
  [Header("References")]
  [Tooltip("Camera used for aiming (position + forward). Defaults to Camera.main if left empty.")]
  public Transform aimCameraTransform;

  public LineSwitcher lineSwitcher;
  public LineFollowController followController;

  [Header("Aiming")]
  [Tooltip("Max distance along the camera's look direction searched for a candidate line.")]
  public float maxAimDistance = 25f;

  [Tooltip("How far apart (world units) sample points are taken along the look direction. Smaller = more accurate, more expensive.")]
  public float aimSampleStep = 0.25f;

  [Tooltip("Max distance from the look direction a line may be and still count as aimed-at.")]
  public float aimRadius = 0.75f;

  [Header("Input")]
  [Tooltip("If true, locks and hides the OS cursor while aiming, so mouse delta reads cleanly.")]
  public bool lockCursorWhileAiming = true;

  [Header("Aim Line")]
  [Tooltip("A LineRenderer only has a width, not a real cross-section — the aim beam is instead a small procedural cylinder mesh from the player to the aim point/target, auto-built and updated every frame while aiming. Optional material; a plain unlit one is created automatically if left empty.")]
  public Material aimLineMaterial;

  public Color validAimColor = Color.green;
  public Color invalidAimColor = Color.red;

  [Tooltip("Beam radius (world units).")]
  public float aimLineRadius = 0.03f;

  private const int AimBeamSegments = 8;

  private GameObject _aimBeamObject;
  private MeshRenderer _aimBeamRenderer;
  private Mesh _aimBeamMesh;
  private Material _aimBeamMaterialInstance;
  // Ring of AimBeamSegments vertices at the start, then the same ring at the end, then one
  // center vertex per end cap — [0..seg) start ring, [seg..2seg) end ring, 2seg = start cap
  // center, 2seg+1 = end cap center.
  private readonly Vector3[] _aimBeamVertices = new Vector3[AimBeamSegments * 2 + 2];

  // Fixed once — only _aimBeamVertices positions change per frame, so the mesh is updated with
  // SetVertices instead of rebuilt from scratch every frame.
  private static readonly int[] AimBeamTriangles = BuildAimBeamTriangles();

  private static int[] BuildAimBeamTriangles() {
    int seg = AimBeamSegments;
    int startCenter = seg * 2;
    int endCenter = seg * 2 + 1;
    var tris = new int[seg * 4 * 3]; // per segment: 2 side tris + 1 start-cap tri + 1 end-cap tri
    int idx = 0;

    for (int i = 0; i < seg; i++) {
      int i0 = i;
      int i1 = (i + 1) % seg;
      int j0 = seg + i;
      int j1 = seg + i1;

      tris[idx++] = i0; tris[idx++] = i1; tris[idx++] = j1;
      tris[idx++] = i0; tris[idx++] = j1; tris[idx++] = j0;

      tris[idx++] = startCenter; tris[idx++] = i1; tris[idx++] = i0;
      tris[idx++] = endCenter; tris[idx++] = j0; tris[idx++] = j1;
    }

    return tris;
  }

  [Header("Aim Visibility")]
  [Tooltip("If true, hides the player's sprite while aiming (after Aim Disappear Delay), and shows it again immediately as soon as aiming ends (cancelled, or the moment a confirmed switch's move finishes).")]
  public bool hidePlayerWhileAiming = false;

  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

  [Tooltip("Seconds after aiming begins before the player's sprite actually disappears. 0 = instant.")]
  public float aimDisappearDelay = 0f;

  [Tooltip("Instantiated once, aimVanishParticleDelay seconds after the sprite actually disappears (i.e. after Aim Disappear Delay has also elapsed). Left unfired (and cancelled) if aiming ends before then.")]
  public ParticleSystem aimVanishParticlesPrefab;

  [Tooltip("Seconds between the player's sprite disappearing and Aim Vanish Particles Prefab spawning.")]
  public float aimVanishParticleDelay = 0.1f;

  [Tooltip("Redirects where the vanish particle spawns — an anchor point instead of the player's own position. Leave empty to spawn at the player.")]
  public Transform aimVanishParticleSpawnPoint;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logAimHits = true;

  private bool _isAiming;
  private bool _isSwitching;

  private bool _hasAimHit;
  private LinePath _aimLinePath;
  private int _aimStrand;
  private Vector3 _aimPoint;
  private float _aimDistance;
  private bool _aimValid;

  private CursorLockMode _prevLockState;
  private bool _prevCursorVisible;

  private Coroutine _aimVanishParticleRoutine;

  public bool IsAiming => _isAiming;

  /// <summary>Fired when aiming begins.</summary>
  public event Action AimStarted;

  /// <summary>Fired whenever aiming visually stops — either cancelled with no switch, or right after a switch's move finishes.</summary>
  public event Action AimEnded;

  /// <summary>Fired the instant a confirmed switch starts moving, with (player position before the move, aimed target point) — for systems that care what the switch's path crosses (e.g. TakedownController).</summary>
  public event Action<Vector3, Vector3> SwitchStarted;

  /// <summary>Fired when a switch's move finishes, before AimEnded.</summary>
  public event Action SwitchFinished;

  private void Awake() {
    if (lineSwitcher == null) lineSwitcher = GetComponent<LineSwitcher>();
    if (followController == null) followController = GetComponent<LineFollowController>();

    if (aimCameraTransform == null && Camera.main != null) aimCameraTransform = Camera.main.transform;

    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

    BuildAimBeam();
    _aimBeamRenderer.enabled = false;
  }

  private void BuildAimBeam() {
    _aimBeamObject = new GameObject("LineSwitchAimBeam");
    _aimBeamObject.transform.SetParent(transform, false);
    // Vertices fed into the mesh every frame are world-space (matches the beam's start/end
    // points, which come straight from world-space transforms) — same reasoning as
    // LinePathVisualizer's ribbon meshes: a MeshFilter has no "useWorldSpace" escape hatch like
    // LineRenderer did, so this object's own transform has to actually BE world identity.
    _aimBeamObject.transform.position = Vector3.zero;
    _aimBeamObject.transform.rotation = Quaternion.identity;

    var meshFilter = _aimBeamObject.AddComponent<MeshFilter>();
    _aimBeamRenderer = _aimBeamObject.AddComponent<MeshRenderer>();
    _aimBeamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    _aimBeamRenderer.receiveShadows = false;

    _aimBeamMaterialInstance = aimLineMaterial != null ? new Material(aimLineMaterial) : new Material(Shader.Find("Sprites/Default"));
    _aimBeamMaterialInstance.hideFlags = HideFlags.DontSave;
    _aimBeamRenderer.sharedMaterial = _aimBeamMaterialInstance;

    _aimBeamMesh = new Mesh { name = "AimBeam" };
    _aimBeamMesh.MarkDynamic(); // rebuilt every frame while aiming — hint Unity to keep it GPU-writable
    _aimBeamMesh.vertices = _aimBeamVertices;

    var uvs = new Vector2[_aimBeamVertices.Length];
    for (int i = 0; i < AimBeamSegments; i++) {
      float v = i / (float)AimBeamSegments;
      uvs[i] = new Vector2(0f, v);
      uvs[AimBeamSegments + i] = new Vector2(1f, v);
    }
    uvs[AimBeamSegments * 2] = new Vector2(0f, 0.5f);
    uvs[AimBeamSegments * 2 + 1] = new Vector2(1f, 0.5f);
    _aimBeamMesh.uv = uvs;

    _aimBeamMesh.triangles = AimBeamTriangles;
    meshFilter.sharedMesh = _aimBeamMesh;
  }

  /// <summary>Rebuilds the beam's cylinder vertices between start and end, in place, and pushes them to the mesh.</summary>
  private void UpdateAimBeamMesh(Vector3 start, Vector3 end) {
    // _aimBeamObject is parented to the (moving) player purely so it gets destroyed/organized
    // with it — Awake() only pins its world transform to identity ONCE, and since the player
    // moves after that, the child's world transform silently drifts away from identity via the
    // parent-child relationship (Transform.position/.rotation setters only affect the CURRENT
    // instant, they don't keep a moving parent from carrying the child along afterwards). The
    // vertices below are plain world-space coordinates, so without re-pinning this every frame the
    // whole beam renders offset/misrotated by however far the player has moved since Awake —
    // exactly the "stuck near spawn, aimed wrong" symptom this was causing.
    _aimBeamObject.transform.position = Vector3.zero;
    _aimBeamObject.transform.rotation = Quaternion.identity;

    Vector3 delta = end - start;
    float length = delta.magnitude;
    if (length < 0.0001f) {
      _aimBeamRenderer.enabled = false;
      return;
    }

    Vector3 dir = delta / length;
    // Fall back to a different reference axis when the beam points nearly straight up/down, where
    // cross(dir, Vector3.up) would collapse to ~zero and right/up below would come out degenerate.
    Vector3 upRef = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
    Vector3 right = Vector3.Cross(dir, upRef).normalized;
    Vector3 up = Vector3.Cross(right, dir).normalized;

    float radius = Mathf.Max(0.0001f, aimLineRadius);

    for (int i = 0; i < AimBeamSegments; i++) {
      float angle = i / (float)AimBeamSegments * Mathf.PI * 2f;
      Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
      _aimBeamVertices[i] = start + offset;
      _aimBeamVertices[AimBeamSegments + i] = end + offset;
    }
    _aimBeamVertices[AimBeamSegments * 2] = start;
    _aimBeamVertices[AimBeamSegments * 2 + 1] = end;

    _aimBeamMesh.SetVertices(_aimBeamVertices);
    _aimBeamMesh.RecalculateNormals();
    _aimBeamMesh.RecalculateBounds();
    _aimBeamRenderer.enabled = true;
  }

#pragma warning disable IDE0051
  private void OnVision(InputValue value) {
    if (value.isPressed) BeginAim();
    else EndAim();
  }

  private void OnConfirm(InputValue value) {
    if (value.isPressed) TryConfirmSwitch();
  }
#pragma warning restore IDE0051

  public void BeginAim() {
    if (!enabled || _isAiming || _isSwitching || lineSwitcher == null || lineSwitcher.IsSwitching
        || aimCameraTransform == null) {
      return;
    }

    _isAiming = true;

    if (followController != null) followController.movementEnabled = false;
    _aimBeamRenderer.enabled = true;

    if (hidePlayerWhileAiming && spriteRenderer != null) {
      if (_aimVanishParticleRoutine != null) StopCoroutine(_aimVanishParticleRoutine);
      _aimVanishParticleRoutine = StartCoroutine(AimVanishRoutine());
    }

    if (lockCursorWhileAiming) {
      _prevLockState = Cursor.lockState;
      _prevCursorVisible = Cursor.visible;
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }

    AimStarted?.Invoke();
  }

  public void EndAim() {
    if (!_isAiming || _isSwitching) {
      return; // let an in-progress switch finish and call EndAim itself via OnSwitchMoveComplete
    }

    _isAiming = false;
    _hasAimHit = false;
    _aimValid = false;

    _aimBeamRenderer.enabled = false;

    if (hidePlayerWhileAiming && spriteRenderer != null) {
      spriteRenderer.enabled = true;
      if (_aimVanishParticleRoutine != null) {
        StopCoroutine(_aimVanishParticleRoutine);
        _aimVanishParticleRoutine = null;
      }
    }

    if (followController != null) followController.movementEnabled = true;

    if (lockCursorWhileAiming) {
      Cursor.lockState = _prevLockState;
      Cursor.visible = _prevCursorVisible;
    }

    AimEnded?.Invoke();
  }

  private IEnumerator AimVanishRoutine() {
    if (aimDisappearDelay > 0f) yield return new WaitForSeconds(aimDisappearDelay);

    spriteRenderer.enabled = false;

    if (aimVanishParticleDelay > 0f) yield return new WaitForSeconds(aimVanishParticleDelay);

    if (aimVanishParticlesPrefab != null) {
      Vector3 spawnPos = aimVanishParticleSpawnPoint != null ? aimVanishParticleSpawnPoint.position : transform.position;

      ParticleSystem instance = Instantiate(aimVanishParticlesPrefab, spawnPos, Quaternion.identity);
      ParticleSystem.MainModule main = instance.main;
      float lifetime = Mathf.Max(main.startLifetime.constant, main.startLifetime.constantMax);
      Destroy(instance.gameObject, main.duration + lifetime);
    }

    _aimVanishParticleRoutine = null;
  }

  public bool TryConfirmSwitch() {
    if (lineSwitcher == null || !_isAiming || !_aimValid) {
      if (logAimHits) {
        string reason = !_isAiming ? "not aiming" : (_hasAimHit ? "path obstructed" : "no valid target");
        Debug.Log($"[LineAimSwitchController] Confirm denied: {reason}.");
      }
      return false;
    }

    _aimBeamRenderer.enabled = false;
    Vector3 fromPosition = transform.position;
    _isSwitching = true;

    var started = lineSwitcher.TrySwitchToLine(_aimLinePath, _aimStrand, _aimPoint, _aimDistance, OnSwitchMoveComplete);
    if (!started) {
      _aimBeamRenderer.enabled = true;
      _isSwitching = false;
      return false;
    }

    SwitchStarted?.Invoke(fromPosition, _aimPoint);
    return true;
  }

  private void Update() {
    if (_isAiming && !_isSwitching) {
      UpdateAim();
    }
  }

  private void UpdateAim() {
    if (aimCameraTransform == null) return;

    Vector3 origin = aimCameraTransform.position;
    Vector3 direction = aimCameraTransform.forward;

    var wasValid = _aimValid;
    _hasAimHit = false;
    _aimValid = false;
    float bestDist = float.MaxValue;

    LinePath bestLine = null;
    int bestStrand = -1;
    Vector3 bestPoint = Vector3.zero;
    float bestDistAlong = 0f;

    int sampleCount = Mathf.Max(4, Mathf.CeilToInt(maxAimDistance / Mathf.Max(0.05f, aimSampleStep)));
    for (int s = 1; s <= sampleCount; s++) {
      Vector3 samplePos = origin + direction * (s * aimSampleStep);

      foreach (var line in LinePath.All) {
        if (line == null || !line.isActiveAndEnabled) continue;

        var distAlong = line.FindClosestDistance(samplePos, out Vector3 cp, out float distToLine, out int strand);
        if (strand < 0 || distToLine > aimRadius || distToLine >= bestDist) continue;
        if (!lineSwitcher.IsValidSwitchTarget(line, strand)) continue;

        bestDist = distToLine;
        bestLine = line;
        bestStrand = strand;
        bestPoint = cp;
        bestDistAlong = distAlong;
      }
    }

    // Path-clear (collision) is only worth checking once, against the single closest candidate —
    // not per sample/line pair above, since that would run a physics sweep for every point along
    // the aim ray. A candidate that's a valid strand but has something blocking the path still
    // counts as "aimed at" (_hasAimHit, drawn/logged) so the player sees WHAT they're pointing at,
    // just not confirmable (_aimValid stays false, aim line reads invalidAimColor).
    _hasAimHit = bestLine != null;
    if (_hasAimHit) {
      _aimLinePath = bestLine;
      _aimStrand = bestStrand;
      _aimPoint = bestPoint;
      _aimDistance = bestDistAlong;
      _aimValid = lineSwitcher.IsSwitchPathClear(bestPoint);
    }

    // The SAMPLING above stays camera-based (origin/direction) since that's what the player is
    // actually looking at. The drawn line is purely visual and reads better anchored to the
    // player rather than floating from the camera, so it starts at the player instead.
    Vector3 lineOrigin = followController != null ? followController.transform.position : origin;

    Vector3 endPoint = _hasAimHit ? _aimPoint : lineOrigin + direction * maxAimDistance;
    UpdateAimBeamMesh(lineOrigin, endPoint);

    _aimBeamMaterialInstance.color = _aimValid ? validAimColor : invalidAimColor;

    if (logAimHits && _aimValid != wasValid) {
      if (_aimValid) {
        Debug.Log($"[LineAimSwitchController] Aiming at '{_aimLinePath.name}' strand={_aimStrand} point={_aimPoint:F2}");
      } else if (_hasAimHit) {
        Debug.Log($"[LineAimSwitchController] Aiming at '{_aimLinePath.name}' strand={_aimStrand} but path is obstructed.");
      } else {
        Debug.Log("[LineAimSwitchController] No valid line within aimRadius.");
      }
    }
  }

  private void OnSwitchMoveComplete() {
    _isSwitching = false;
    SwitchFinished?.Invoke();
    EndAim();
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || !_hasAimHit) return;
    Gizmos.color = _aimValid ? Color.green : Color.red;
    Gizmos.DrawSphere(_aimPoint, 0.05f);
  }
#endif
}
