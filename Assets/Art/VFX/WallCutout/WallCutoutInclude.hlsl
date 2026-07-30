#ifndef WALL_CUTOUT_INCLUDE
#define WALL_CUTOUT_INCLUDE

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _EdgeSoftness;
    half4 _EdgeColor;
    half _EdgeGlowIntensity;
    half _NoiseScale;
    half _EdgeNoiseStrength;
CBUFFER_END

// Pushed as GLOBAL properties by WallCutoutController, shared by every WallCutout material in
// the scene so one script can punch a hole in any/all walls between the camera and the player.
// The cutout is a CONE: its tip (radius 0) sits at the subject (the player), and it widens to
// _CutoutBaseRadius at its base (the viewer/camera). Nearby geometry needs a wide hole to clear
// the view, but right at the subject the hole can taper to nothing — this matches how much
// screen space a wall actually occludes at each point along the view line, unlike a uniform-width
// cylinder which cuts the same amount everywhere.
float3 _CutoutApex;       // subject (target) position — the cone's tip, radius 0
float3 _CutoutBase;       // viewer position
float _CutoutBaseRadius;  // radius of the cone at its base (the viewer end) — size of the base

// Small, self-contained value-noise hash (no texture dependency) used to perturb the cutout
// edge slightly so the hole reads as a soft dissolve boundary instead of a perfectly smooth cut.
float WallCutoutHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Signed distance (world units) from a surface point to the cutout boundary: negative INSIDE
// the hole, positive outside. Projects the point onto the apex->base axis to find both the
// closest point on the axis AND how far along it we are (t), then linearly interpolates the
// cone's radius at that point (0 at the apex, _CutoutBaseRadius at the base). Includes the small
// noise perturbation so the boundary isn't a perfectly smooth cone.
float WallCutoutSignedDistance(float3 positionWS)
{
    float3 axis = _CutoutBase - _CutoutApex;
    float axisLenSq = max(dot(axis, axis), 1e-6);
    float t = saturate(dot(positionWS - _CutoutApex, axis) / axisLenSq);

    float3 closestOnAxis = _CutoutApex + axis * t;
    float radiusAtT = _CutoutBaseRadius * t;

    float dist = distance(positionWS, closestOnAxis) - radiusAtT;
    float noise = (WallCutoutHash(positionWS * _NoiseScale) - 0.5) * _EdgeNoiseStrength;
    return dist + noise;
}

// Discards the fragment if it falls inside the cutout. Call at the top of frag() in every pass
// that should honor the hole (forward lit, shadow caster, ...).
void ClipWallCutout(float3 positionWS)
{
    clip(WallCutoutSignedDistance(positionWS));
}

// Additive glow tinting a thin band just outside the cutout edge — purely cosmetic, safe to call
// only from color-writing passes (not the shadow caster).
half3 WallCutoutEdgeGlow(float3 positionWS)
{
    float edge = WallCutoutSignedDistance(positionWS);
    float glow = 1.0 - saturate(edge / max(_EdgeSoftness, 0.0001));
    return _EdgeColor.rgb * _EdgeGlowIntensity * glow;
}

#endif
