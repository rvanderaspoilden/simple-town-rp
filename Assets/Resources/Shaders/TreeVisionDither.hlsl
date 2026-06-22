#ifndef TREE_VISION_DITHER_INCLUDED
#define TREE_VISION_DITHER_INCLUDED

// World-space "vision circle" dither for foliage, driven by the same global uniforms that
// CameraWallFader sets each frame for the wall vision circle, plus two tree-specific ones:
//   _VisionCenter    : target centre in normalized screen UV (xy, bottom-left origin)
//   _VisionRadius    : disc radius in screen-height-normalized units (aspect-corrected here)
//   _VisionSoftness  : 0..1 soft rim fraction
//   _VisionTargetDist: distance camera->target (only foliage IN FRONT of the target dissolves)
//   _VisionTreeStrength: 0 disables the tree effect, 1 = full
//
// Drop a Custom Function node (File mode -> this file, name "TreeVisionDither") into the
// Shader Graphs/Leaf graph, feed it Screen Position (Default).xy, Position(World) and the
// graph's current Alpha, and route OutAlpha into the Fragment Alpha (keep Alpha Clipping on).
// Inside the soft disc, and only for fragments closer to the camera than the target, an ordered
// 4x4 Bayer dither punches OutAlpha to 0 so those texels clip away — leaving the leaf cutout
// untouched everywhere else.

float4 _VisionCenter;
float  _VisionRadius;
float  _VisionSoftness;
float  _VisionTargetDist;
float  _VisionTreeStrength;

float _TreeBayer(float2 pixel)
{
    const float b[16] = {
        0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
       12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
        3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
       15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    };
    int x = (int)fmod(floor(pixel.x), 4.0);
    int y = (int)fmod(floor(pixel.y), 4.0);
    return b[y * 4 + x] + (0.5 / 16.0);
}

void TreeVisionDither_float(float2 ScreenUV, float3 WorldPos, float InAlpha, out float OutAlpha)
{
    OutAlpha = InAlpha;
    if (_VisionTreeStrength <= 0.0 || _VisionRadius <= 0.0) return;

    // Only dissolve foliage IN FRONT of the target so we never punch holes in trees behind it.
    float fragDist = distance(_WorldSpaceCameraPos.xyz, WorldPos);
    if (fragDist >= _VisionTargetDist) return;

    // Soft screen-space disc around the target (aspect-corrected -> true circle).
    float2 d = ScreenUV - _VisionCenter.xy;
    d.x *= (_ScreenParams.x / max(_ScreenParams.y, 1.0));
    float dist = length(d);
    float inner = _VisionRadius * (1.0 - saturate(_VisionSoftness));
    float fade = (1.0 - smoothstep(inner, _VisionRadius, dist)) * _VisionTreeStrength;

    float2 pixel = ScreenUV * _ScreenParams.xy;
    if (_TreeBayer(pixel) < fade) OutAlpha = 0.0; // dithered hole
}

#endif
