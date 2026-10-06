// SQUISH LAB — フォーム素材の共通シェーダーコード（Built-in / URP 共用）。
//
// パイプライン依存の変換は呼び出し側で次のマクロを定義してから include する:
//   SQ_OBJECT_TO_CLIP(p)      オブジェクト座標 → クリップ座標
//   SQ_OBJECT_TO_WORLD(p)     オブジェクト座標 → ワールド座標
//   SQ_NORMAL_TO_WORLD(n)     オブジェクト法線 → ワールド法線
//   SQ_CAMERA_POS             カメラのワールド座標
//
// ライティングはパイプラインのライトを使わず、商品写真風のスタジオ照明を自前で計算する
// （どちらのパイプラインでも同じ見た目になり、モバイルでも軽い）。

#ifndef SQUISH_FOAM_COMMON_INCLUDED
#define SQUISH_FOAM_COMMON_INCLUDED

float4 _CrumbColor;
float4 _CrustColor;
float4 _SubsurfaceColor;
float _PoreScale;
float _PoreStrength;
float _WrinkleStrength;
float _Sheen;

float4 _KeyDir;      // ワールド: 表面 → ライト
float4 _KeyColor;
float4 _FillDir;
float4 _FillColor;
float4 _SkyColor;
float4 _GroundColor;
float _Wrap;
float _SpecStrength;

struct SqAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;      // r = 耳マスク, g = 圧縮量
    float3 rest       : TEXCOORD0;  // 静止位置（模様が変形で泳がないように）
};

struct SqVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float3 rest       : TEXCOORD2;
    float4 color      : TEXCOORD3;
};

SqVaryings SqVert(SqAttributes v)
{
    SqVaryings o;
    o.positionCS = SQ_OBJECT_TO_CLIP(v.positionOS.xyz);
    o.positionWS = SQ_OBJECT_TO_WORLD(v.positionOS.xyz);
    o.normalWS = SQ_NORMAL_TO_WORLD(v.normalOS);
    o.rest = v.rest;
    o.color = v.color;
    return o;
}

// ---- ノイズ（テクスチャ不要, 静止位置ベース） ----
float SqHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SqNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(SqHash(i + float3(0, 0, 0)), SqHash(i + float3(1, 0, 0)), f.x),
                     lerp(SqHash(i + float3(0, 1, 0)), SqHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(SqHash(i + float3(0, 0, 1)), SqHash(i + float3(1, 0, 1)), f.x),
                     lerp(SqHash(i + float3(0, 1, 1)), SqHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// 高さ場の画面微分から法線を摂動する（Mikkelsen, "Bump Mapping Unparametrized Surfaces"）
float3 SqPerturbNormal(float3 N, float3 posWS, float height, float scale)
{
    float3 dpdx = ddx(posWS);
    float3 dpdy = ddy(posWS);
    float dhdx = ddx(height);
    float dhdy = ddy(height);
    float3 r1 = cross(dpdy, N);
    float3 r2 = cross(N, dpdx);
    float det = dot(dpdx, r1);
    float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
    return normalize(abs(det) * N - grad * scale);
}

float4 SqFrag(SqVaryings i) : SV_Target
{
    float3 N = normalize(i.normalWS);
    float crust = saturate(i.color.r);
    float comp = saturate(i.color.g);
    float3 P = i.rest;

    // ---- スポンジの気孔: 2 オクターブのノイズを閾値で穴にする ----
    float scale = _PoreScale * lerp(1.0, 1.6, crust); // 耳は目が細かい
    float n1 = SqNoise(P * scale);
    float n2 = SqNoise(P * scale * 2.37 + 11.3);
    float poreField = n1 * 0.62 + n2 * 0.38;
    // 圧縮されると気孔が潰れて目が詰まる
    float pores = smoothstep(0.56 + comp * 0.08, 0.8, poreField);

    // ---- 圧縮シワ: 尾根型ノイズ。へこんだ所にだけ現れる ----
    float wr = SqNoise(P * float3(9.0, 13.0, 9.0) + 3.1);
    float ridge = 1.0 - abs(wr * 2.0 - 1.0);
    ridge = ridge * ridge * ridge;
    float wrinkle = ridge * smoothstep(0.08, 0.6, comp) * _WrinkleStrength;

    float height = -pores * _PoreStrength * 0.6 + (n2 - 0.5) * 0.15 - wrinkle * 0.9;
    N = SqPerturbNormal(N, i.positionWS, height, 0.012);

    // ---- アルベド ----
    float crustVar = SqNoise(P * 4.0 + 7.0);
    float3 crustCol = _CrustColor.rgb * lerp(0.9, 1.08, crustVar);
    float edge = smoothstep(0.35, 0.65, crust + (crustVar - 0.5) * 0.25);
    float3 albedo = lerp(_CrumbColor.rgb, crustCol, edge);
    albedo *= 1.0 - pores * _PoreStrength * 0.1;           // 気孔の奥はほんの少し暗い（主に陰影で見せる）
    albedo *= 1.0 - comp * 0.1 - wrinkle * 0.12;            // 押し潰された所は密になって少し濃い

    // ---- スタジオ照明 ----
    float3 V = normalize(SQ_CAMERA_POS - i.positionWS);
    float3 L = normalize(_KeyDir.xyz);
    float ndl = dot(N, L);
    float wrapK = _Wrap * (1.0 - comp * 0.5);
    float diff = saturate((ndl + wrapK) / (1.0 + wrapK));
    // 明暗の境界に暖色を入れて、光が少し中に入るフォームらしさを出す
    float terminator = saturate(1.0 - abs(ndl)) * saturate(ndl + 0.6);
    float3 sss = _SubsurfaceColor.rgb * terminator * 0.28;

    float3 F = normalize(_FillDir.xyz);
    float fill = saturate((dot(N, F) + 0.5) / 1.5);

    float hemi = N.y * 0.5 + 0.5;
    float3 ambient = lerp(_GroundColor.rgb, _SkyColor.rgb, hemi);

    // 机との接地部を暗く（接地感）+ へこみの奥のAO
    float contactAO = lerp(0.55, 1.0, saturate(P.y / 0.12));
    float dentAO = 1.0 - comp * 0.22;
    float ao = contactAO * dentAO;

    float3 H = normalize(L + V);
    float spec = pow(saturate(dot(N, H)), 18.0) * _SpecStrength * (1.0 - pores * 0.7) * (1.0 - comp * 0.5);
    float nv = saturate(dot(N, V));
    float sheen = pow(1.0 - nv, 4.0) * _Sheen * 0.35;       // 産毛のような柔らかい縁の明るさ

    float3 col = albedo * (_KeyColor.rgb * diff + _FillColor.rgb * fill * 0.35 + ambient * ao) +
                 albedo * sss +
                 _KeyColor.rgb * spec +
                 _SkyColor.rgb * sheen * ao;
    col *= lerp(1.0, ao, 0.5);
    return float4(col, 1.0);
}

#endif
