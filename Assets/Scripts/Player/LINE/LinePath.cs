using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines one or more disjoint traceable lines ("strands") in the scene. LineFollowController
/// locks the player's movement onto the nearest point of one strand at a time; LineSwitcher/
/// AimSwitch let the player jump to a different strand — either on a different
/// LinePath object, or a different, disconnected strand on this SAME LinePath object.
///
/// Authoring (useChildrenAsPoints = true, the default):
///  - SIMPLE: add child empty GameObjects directly under this object, in order. That's one
///    continuous strand (unchanged from before).
///  - MULTIPLE DISJOINT PATHS ON ONE LinePath: instead, add empty "group" GameObjects under
///    this object (e.g. "StrandA", "StrandB"), and put each strand's waypoints as children of
///    its own group. Each group becomes its own disjoint strand — they are never connected to
///    each other, even though they live on the same LinePath component. Optionally add a
///    LineStrandMarker to a group to override closedLoop/gizmo color for just that strand.
///  - You can freely mix: a LinePath can have some ungrouped stray points ignored with a
///    warning once grouping is detected — once ANY top-level child has its own children, this
///    component switches into "grouped" mode and expects every strand to be a group.
///
/// Or skip the hierarchy entirely and assign local-space points directly via the "points" field
/// (single strand, fallback when useChildrenAsPoints is false).
/// </summary>
[DisallowMultipleComponent]
public class LinePath : MonoBehaviour {
  [Header("Points")]
  [Tooltip("If true, builds strands from child Transforms. Group children under sub-objects to author multiple disjoint strands — see class summary.")]
  public bool useChildrenAsPoints = true;

  [Tooltip("Local-space points for a single strand. Only used when useChildrenAsPoints is false.")]
  public Vector3[] points = new Vector3[0];

  [Tooltip("Default closed-loop setting for strands that don't have their own LineStrandMarker.")]
  public bool closedLoop = false;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public Color gizmoColor = Color.cyan;

  private class Strand {
    public Vector3[] worldPoints;
    public float[] cumulativeLengths;
    public float length;
    public bool closedLoop;
    public Color gizmoColor;
  }

  private readonly List<Strand> _strands = new List<Strand>();

  /// <summary>Number of disjoint strands on this LinePath.</summary>
  public int StrandCount => _strands.Count;

  /// <summary>Length of a single strand, or 0 if the index is out of range.</summary>
  public float GetStrandLength(int strandIndex) => TryGetStrand(strandIndex, out var s) ? s.length : 0f;

  /// <summary>Whether a specific strand wraps (closed loop) rather than stopping at its ends.</summary>
  public bool IsStrandClosedLoop(int strandIndex) => TryGetStrand(strandIndex, out var s) && s.closedLoop;

  /// <summary>Number of straight segments composing a strand.</summary>
  public int GetSegmentCount(int strandIndex) {
    if (!TryGetStrand(strandIndex, out var strand) || strand.worldPoints.Length < 2) return 0;
    return strand.closedLoop ? strand.worldPoints.Length : strand.worldPoints.Length - 1;
  }

  /// <summary>
  /// Exposes one authored segment without exposing the mutable internal point arrays. This lets
  /// screen-space selection evaluate the real path geometry rather than approximating it with
  /// scene-wide ray samples.
  /// </summary>
  public bool TryGetSegment(
    int strandIndex,
    int segmentIndex,
    out Vector3 start,
    out Vector3 end,
    out float startDistance,
    out float length) {
    start = transform.position;
    end = transform.position;
    startDistance = 0f;
    length = 0f;

    if (!TryGetStrand(strandIndex, out var strand)) return false;
    int segmentCount = strand.closedLoop ? strand.worldPoints.Length : strand.worldPoints.Length - 1;
    if (segmentIndex < 0 || segmentIndex >= segmentCount) return false;

    int nextIndex = (segmentIndex + 1) % strand.worldPoints.Length;
    start = strand.worldPoints[segmentIndex];
    end = strand.worldPoints[nextIndex];
    startDistance = strand.cumulativeLengths[segmentIndex];
    length = Vector3.Distance(start, end);
    return length > 0.0001f;
  }

  private static readonly List<LinePath> _all = new List<LinePath>();

  /// <summary>All enabled LinePaths currently in the scene. Used by AimSwitch to find switch targets.</summary>
  public static IReadOnlyList<LinePath> All => _all;

  private void OnEnable() {
    _all.Add(this);
    Rebuild();
  }

  private void OnDisable() {
    _all.Remove(this);
  }

  private void Awake() {
    Rebuild();
  }

  private void OnValidate() {
    Rebuild();
  }

  /// <summary>Recomputes cached strands/points/lengths. Called automatically; call manually if you move points at runtime.</summary>
  public void Rebuild() {
    _strands.Clear();

    if (useChildrenAsPoints && transform.childCount > 0) {
      bool anyGroup = false;
      for (int i = 0; i < transform.childCount; i++) {
        if (transform.GetChild(i).childCount > 0) { anyGroup = true; break; }
      }

      if (anyGroup) {
        // Grouped mode: every top-level child with its own children is a disjoint strand.
        for (int i = 0; i < transform.childCount; i++) {
          var group = transform.GetChild(i);
          if (group.childCount == 0) {
            Debug.LogWarning($"[LinePath] '{name}': child '{group.name}' has no waypoint children of its own and was ignored " +
                              "(this LinePath is in grouped/multi-strand mode because at least one other child has sub-children).", this);
            continue;
          }

          var marker = group.GetComponent<LineStrandMarker>();
          var pts = new List<Vector3>();
          for (int j = 0; j < group.childCount; j++) pts.Add(group.GetChild(j).position);

          AddStrand(pts,
            marker != null ? marker.closedLoop : closedLoop,
            marker != null && marker.overrideGizmoColor ? marker.gizmoColor : gizmoColor);
        }
      } else {
        // Flat mode: all top-level children are points of a single strand.
        var pts = new List<Vector3>();
        for (int i = 0; i < transform.childCount; i++) pts.Add(transform.GetChild(i).position);
        AddStrand(pts, closedLoop, gizmoColor);
      }
    } else if (points != null && points.Length > 0) {
      var pts = new List<Vector3>();
      foreach (var p in points) pts.Add(transform.TransformPoint(p));
      AddStrand(pts, closedLoop, gizmoColor);
    }
  }

  private void AddStrand(List<Vector3> pts, bool loop, Color color) {
    var strand = new Strand { closedLoop = loop, gizmoColor = color, worldPoints = pts.ToArray() };
    strand.cumulativeLengths = new float[Mathf.Max(1, strand.worldPoints.Length)];
    strand.length = 0f;

    if (strand.worldPoints.Length >= 2) {
      int segCount = loop ? strand.worldPoints.Length : strand.worldPoints.Length - 1;
      for (int i = 0; i < strand.worldPoints.Length; i++) {
        strand.cumulativeLengths[i] = strand.length;
        if (i < segCount) {
          Vector3 a = strand.worldPoints[i];
          Vector3 b = strand.worldPoints[(i + 1) % strand.worldPoints.Length];
          strand.length += Vector3.Distance(a, b);
        }
      }
    }

    _strands.Add(strand);
  }

  /// <summary>Returns the world position at the given distance along a specific strand.</summary>
  public Vector3 GetPointAtDistance(int strandIndex, float distance) {
    if (!TryGetStrand(strandIndex, out var s) || s.worldPoints.Length == 0) return transform.position;
    if (s.worldPoints.Length == 1) return s.worldPoints[0];

    distance = ClampOrWrap(s, distance);
    int segCount = s.closedLoop ? s.worldPoints.Length : s.worldPoints.Length - 1;

    for (int i = 0; i < segCount; i++) {
      int i0 = i, i1 = (i + 1) % s.worldPoints.Length;
      float segStart = s.cumulativeLengths[i0];
      float segLen = Vector3.Distance(s.worldPoints[i0], s.worldPoints[i1]);
      float segEnd = segStart + segLen;

      if (distance <= segEnd || i == segCount - 1) {
        float t = segLen > 0.0001f ? Mathf.Clamp01((distance - segStart) / segLen) : 0f;
        return Vector3.Lerp(s.worldPoints[i0], s.worldPoints[i1], t);
      }
    }

    return s.worldPoints[s.worldPoints.Length - 1];
  }

  /// <summary>Returns the normalized direction of travel (increasing distance) at the given distance along a specific strand.</summary>
  public Vector3 GetDirectionAtDistance(int strandIndex, float distance) {
    if (!TryGetStrand(strandIndex, out var s) || s.worldPoints.Length < 2) return transform.forward;

    distance = ClampOrWrap(s, distance);
    int segCount = s.closedLoop ? s.worldPoints.Length : s.worldPoints.Length - 1;

    for (int i = 0; i < segCount; i++) {
      int i0 = i, i1 = (i + 1) % s.worldPoints.Length;
      float segStart = s.cumulativeLengths[i0];
      float segLen = Vector3.Distance(s.worldPoints[i0], s.worldPoints[i1]);
      float segEnd = segStart + segLen;

      if (distance <= segEnd || i == segCount - 1) {
        return segLen > 0.0001f ? (s.worldPoints[i1] - s.worldPoints[i0]).normalized : transform.forward;
      }
    }

    return transform.forward;
  }

  /// <summary>
  /// Finds the closest point to worldPos across ALL strands on this LinePath (including
  /// disjoint ones), so callers don't need to know which strand a candidate is on ahead of time.
  /// </summary>
  /// <returns>Distance-along-strand at the closest point — pass this and strandIndex into GetPointAtDistance/SetLine.</returns>
  public float FindClosestDistance(Vector3 worldPos, out Vector3 closestPoint, out float distanceToLine, out int strandIndex) {
    closestPoint = transform.position;
    distanceToLine = float.MaxValue;
    strandIndex = -1;
    float bestDistAlong = 0f;

    for (int si = 0; si < _strands.Count; si++) {
      var s = _strands[si];
      if (s.worldPoints.Length == 0) continue;

      if (s.worldPoints.Length == 1) {
        float d = Vector3.Distance(worldPos, s.worldPoints[0]);
        if (d < distanceToLine) { distanceToLine = d; closestPoint = s.worldPoints[0]; strandIndex = si; bestDistAlong = 0f; }
        continue;
      }

      int segCount = s.closedLoop ? s.worldPoints.Length : s.worldPoints.Length - 1;
      for (int i = 0; i < segCount; i++) {
        int i0 = i, i1 = (i + 1) % s.worldPoints.Length;
        Vector3 a = s.worldPoints[i0], b = s.worldPoints[i1];
        Vector3 ab = b - a;
        float segLen = ab.magnitude;
        float t = segLen > 0.0001f ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / (segLen * segLen)) : 0f;
        Vector3 p = a + ab * t;
        float d = Vector3.Distance(worldPos, p);

        if (d < distanceToLine) {
          distanceToLine = d;
          closestPoint = p;
          strandIndex = si;
          bestDistAlong = s.cumulativeLengths[i0] + segLen * t;
        }
      }
    }

    return bestDistAlong;
  }

  /// <summary>Same as FindClosestDistance, but restricted to a strand you already know you're on (cheaper).</summary>
  public float FindClosestDistanceOnStrand(int strandIndex, Vector3 worldPos, out Vector3 closestPoint, out float distanceToLine) {
    closestPoint = transform.position;
    distanceToLine = float.MaxValue;
    if (!TryGetStrand(strandIndex, out var s) || s.worldPoints.Length == 0) return 0f;

    if (s.worldPoints.Length == 1) {
      closestPoint = s.worldPoints[0];
      distanceToLine = Vector3.Distance(worldPos, closestPoint);
      return 0f;
    }

    float bestDistAlong = 0f;
    int segCount = s.closedLoop ? s.worldPoints.Length : s.worldPoints.Length - 1;

    for (int i = 0; i < segCount; i++) {
      int i0 = i, i1 = (i + 1) % s.worldPoints.Length;
      Vector3 a = s.worldPoints[i0], b = s.worldPoints[i1];
      Vector3 ab = b - a;
      float segLen = ab.magnitude;
      float t = segLen > 0.0001f ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / (segLen * segLen)) : 0f;
      Vector3 p = a + ab * t;
      float d = Vector3.Distance(worldPos, p);

      if (d < distanceToLine) {
        distanceToLine = d;
        closestPoint = p;
        bestDistAlong = s.cumulativeLengths[i0] + segLen * t;
      }
    }

    return bestDistAlong;
  }

  private bool TryGetStrand(int index, out Strand strand) {
    if (index >= 0 && index < _strands.Count) { strand = _strands[index]; return true; }
    strand = null;
    return false;
  }

  private float ClampOrWrap(Strand s, float distance) {
    if (s.length <= 0f) return 0f;

    if (s.closedLoop) {
      distance %= s.length;
      if (distance < 0f) distance += s.length;
      return distance;
    }

    return Mathf.Clamp(distance, 0f, s.length);
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos) return;

    Rebuild();

    foreach (var s in _strands) {
      if (s.worldPoints.Length == 1) {
        Gizmos.color = s.gizmoColor;
        Gizmos.DrawSphere(s.worldPoints[0], 0.08f);
        continue;
      }

      if (s.worldPoints.Length < 2) continue;

      Gizmos.color = s.gizmoColor;
      int segCount = s.closedLoop ? s.worldPoints.Length : s.worldPoints.Length - 1;
      for (int i = 0; i < segCount; i++) {
        Gizmos.DrawLine(s.worldPoints[i], s.worldPoints[(i + 1) % s.worldPoints.Length]);
      }

      foreach (var p in s.worldPoints) {
        Gizmos.DrawSphere(p, 0.08f);
      }
    }
  }
#endif
}
