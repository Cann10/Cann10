// SQUISH LAB — 床と接地影の共通コード（Built-in / URP 共用）。
#ifndef SQUISH_FLOOR_COMMON_INCLUDED
#define SQUISH_FLOOR_COMMON_INCLUDED

float4 _BaseColor, _EdgeColor, _Center;
float _FadeRadius;
float4 _ShadowCenter, _ShadowHalfSize, _ShadowOffset;
float _ShadowCorner, _ShadowSoftness, _ShadowStrength, _ContactStrength;

float SqRoundBox(float2 p, float2 halfSize, float r)
{
    float2 q = abs(p) - (halfSize - r);
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float4 FloorColor(float3 positionWS)
{
    float2 p = positionWS.xz;
    float2 dc = p - _Center.xy;
    float fade = exp(-dot(dc, dc) / (_FadeRadius * _FadeRadius));
    float3 col = lerp(_EdgeColor.rgb, _BaseColor.rgb, fade);

    // 柔らかい本影（ライトと反対へ少しずらす）
    float dBroad = SqRoundBox(p - _ShadowCenter.xy - _ShadowOffset.xy, _ShadowHalfSize.xy, _ShadowCorner);
    float broad = 1.0 - smoothstep(-_ShadowSoftness * 0.6, _ShadowSoftness, dBroad);
    // 接地部の締まった暗さ
    float dContact = SqRoundBox(p - _ShadowCenter.xy, _ShadowHalfSize.xy, _ShadowCorner);
    float contact = 1.0 - smoothstep(-0.02, 0.12, dContact);

    col *= 1.0 - broad * broad * _ShadowStrength - contact * _ContactStrength * 0.6;
    return float4(col, 1.0);
}

#endif
