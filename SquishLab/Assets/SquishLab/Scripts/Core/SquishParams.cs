using System;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 素材の物性パラメータ。0..1 の正規化値で持ち、物理量への変換は
    /// このクラスのプロパティに集約する（ソルバー側に魔法の数値を散らさないため）。
    /// </summary>
    [Serializable]
    public struct SquishParams
    {
        [Header("Core feel")]
        [Tooltip("柔らかさ。押した時の沈み込みの速さ・へこみの広さ")]
        [Range(0f, 1f)] public float Softness;

        [Tooltip("指を離した後の戻る速さ。0 = 約12秒、1 = 約0.3秒（対数スケール）")]
        [Range(0f, 1f)] public float RecoverySpeed;

        [Tooltip("表面の弾力。高いほどプルッと追従し、全体の揺れも増える")]
        [Range(0f, 1f)] public float Elasticity;

        [Tooltip("揺れの減衰。低いほどオーバーシュートして揺れる")]
        [Range(0f, 1f)] public float Damping;

        [Tooltip("最大へこみ量（厚みに対する割合）")]
        [Range(0.05f, 0.95f)] public float MaxDeformation;

        [Tooltip("粘着感。離した直後に表面が指に張り付いて戻り始めが遅れる")]
        [Range(0f, 1f)] public float StickyAmount;

        [Tooltip("伸び。ドラッグ・つまみで表面が横に引っ張られる量")]
        [Range(0f, 1f)] public float Stretchiness;

        [Header("Shape response")]
        [Tooltip("指の当たる半径（ワールド単位）")]
        [Range(0.1f, 0.8f)] public float FingerRadius;

        [Tooltip("へこみの周囲がわずかに盛り上がる量（体積保存っぽさ）")]
        [Range(0f, 0.5f)] public float Bulge;

        [Tooltip("へこみが周囲に馴染んで広がる速さ（フォームの連続性）")]
        [Range(0f, 1f)] public float Coupling;

        [Tooltip("押した時の全体のぷるっとした揺れ")]
        [Range(0f, 1f)] public float Wobble;

        // ---- 物理量への変換 ----

        /// <summary>復元の時定数の目安（秒）。95%戻るまでがおよそこの値。</summary>
        public float RecoverySeconds => 12f * Mathf.Pow(0.3f / 12f, Mathf.Clamp01(RecoverySpeed));

        /// <summary>記憶層（スローライズ層）の復元レート [1/s]。exp(-rate*T)=5% になるよう 3/T。</summary>
        public float RecoveryRate => 3f / RecoverySeconds;

        /// <summary>押している間、記憶層がターゲットへ沈むレート [1/s]。</summary>
        public float SinkRate => Mathf.Lerp(14f, 45f, Softness);

        /// <summary>押し始めた瞬間に到達する深さの割合。残りは押し続けるとじわっと沈む。</summary>
        public float InitialDepthFraction => Mathf.Lerp(0.5f, 0.8f, Softness);

        /// <summary>押し続けて最深部に近づく速さ [1/s]。</summary>
        public float HoldSinkRate => Mathf.Lerp(0.8f, 4f, Softness);

        /// <summary>へこみ半径の倍率（柔らかいほど周囲も一緒に沈む）。</summary>
        public float RadiusScale => Mathf.Lerp(0.85f, 1.45f, Softness);

        /// <summary>表面（弾性層）のばね定数 [1/s^2]。</summary>
        public float SurfaceStiffness => Mathf.Lerp(250f, 1500f, Elasticity);

        /// <summary>減衰比 ζ。1 で臨界減衰。</summary>
        public float DampingRatio => Mathf.Lerp(0.18f, 1.4f, Damping);

        /// <summary>離した直後に戻り始めるまでの遅れ（秒）。</summary>
        public float StickDelay => Mathf.Lerp(0.02f, 0.7f, StickyAmount);

        /// <summary>表面がドラッグに引っ張られる最大距離（指半径に対する割合）。</summary>
        public float MaxStretch => Mathf.Lerp(0.05f, 0.9f, Stretchiness);

        /// <summary>ラプラシアン拡散の係数（1ステップあたり, 60Hz基準）。</summary>
        public float DiffusionPerStep => Mathf.Lerp(0f, 0.12f, Coupling);

        public static SquishParams SlowRiseBread => new SquishParams
        {
            Softness = 0.72f,
            RecoverySpeed = 0.28f,   // ≈ 4.5 秒
            Elasticity = 0.42f,
            Damping = 0.62f,
            MaxDeformation = 0.62f,
            StickyAmount = 0.35f,
            Stretchiness = 0.3f,
            FingerRadius = 0.36f,
            Bulge = 0.12f,
            Coupling = 0.45f,
            Wobble = 0.35f,
        };

        public static SquishParams FastFoam => new SquishParams
        {
            Softness = 0.5f,
            RecoverySpeed = 0.78f,   // ≈ 0.9 秒
            Elasticity = 0.62f,
            Damping = 0.45f,
            MaxDeformation = 0.5f,
            StickyAmount = 0.05f,
            Stretchiness = 0.2f,
            FingerRadius = 0.34f,
            Bulge = 0.18f,
            Coupling = 0.35f,
            Wobble = 0.55f,
        };

        public static SquishParams Mochi => new SquishParams
        {
            Softness = 0.9f,
            RecoverySpeed = 0.5f,    // ≈ 2 秒
            Elasticity = 0.3f,
            Damping = 0.35f,
            MaxDeformation = 0.75f,
            StickyAmount = 0.8f,
            Stretchiness = 0.8f,
            FingerRadius = 0.4f,
            Bulge = 0.3f,
            Coupling = 0.6f,
            Wobble = 0.7f,
        };

        /// <summary>範囲外の値（古いアセット・手入力）を安全側に丸める。</summary>
        public SquishParams Sanitized()
        {
            var p = this;
            p.Softness = Mathf.Clamp01(p.Softness);
            p.RecoverySpeed = Mathf.Clamp01(p.RecoverySpeed);
            p.Elasticity = Mathf.Clamp01(p.Elasticity);
            p.Damping = Mathf.Clamp01(p.Damping);
            p.MaxDeformation = Mathf.Clamp(p.MaxDeformation, 0.05f, 0.95f);
            p.StickyAmount = Mathf.Clamp01(p.StickyAmount);
            p.Stretchiness = Mathf.Clamp01(p.Stretchiness);
            p.FingerRadius = Mathf.Clamp(p.FingerRadius, 0.1f, 0.8f);
            p.Bulge = Mathf.Clamp(p.Bulge, 0f, 0.5f);
            p.Coupling = Mathf.Clamp01(p.Coupling);
            p.Wobble = Mathf.Clamp01(p.Wobble);
            return p;
        }
    }
}
