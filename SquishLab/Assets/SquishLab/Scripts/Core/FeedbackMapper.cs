using UnityEngine;

namespace SquishLab
{
    /// <summary>1フレーム分の音・触覚の指示。</summary>
    public struct FeedbackFrame
    {
        // ---- 触覚 ----
        public bool HapticActive;        // 連続振動を流すか
        public float HapticIntensity;    // 0..1
        public float HapticSharpness;    // 0..1
        public float TransientIntensity; // >0 ならこのフレームで単発を鳴らす
        public float TransientSharpness;

        // ---- 音 ----
        public float Air;                // 空気音の量 0..1
        public float AirBrightness;      // 0..1
        public float CracklePerSecond;
        public float CrackleGain;        // 0..1
        public float SquishStrength;     // >0 ならこのフレームで「むにゅ」
        public float SquishPitch;
        public float PuffStrength;       // >0 ならこのフレームで「ぷっ」
    }

    /// <summary>
    /// ソルバーの状態（指の本数・へこみ体積の変化率・最大へこみ）から音と触覚を決める。
    /// 音と触覚が同じ信号から出るので、見た目・音・振動が常に同期する。
    ///
    /// 触覚の段階:
    ///   押し始め … ごく弱く低い単発（ふにっ）+ 連続振動を開始
    ///   押し込み中 … 沈む速さに比例して強く、深いほど少し鋭く。細かいゆらぎを乗せてフォームの粒感に
    ///   限界付近 … 一度だけ「底付き」の単発 + 押し込み続ける間は細かいティック
    ///   離す … 連続振動を止め、粘着感に応じたごく弱い単発
    /// </summary>
    public sealed class FeedbackMapper
    {
        // 変化率の目安（DentVolume / 秒）: 指を素早く置くと 4〜8、ゆっくり押すと 1 前後
        const float FastPressRate = 5f;
        const float RecoveryRateRef = 0.5f;
        const float LimitThreshold = 0.88f;

        int prevContacts;
        bool atLimit;
        float tickTimer;
        float grain, grainTarget, grainTimer;
        uint rng;

        public FeedbackMapper(uint seed = 12345u) { rng = seed == 0 ? 1u : seed; }

        float Rand01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        float Range(float a, float b) => a + (b - a) * Rand01();

        public FeedbackFrame Update(float dt, int contacts, float dentRate, float peakDepth, in SquishParams p,
            float soundPitch, float airAmount, float crackleAmount, float squishAmount)
        {
            var f = new FeedbackFrame();
            float pressRate = Mathf.Clamp01(dentRate / FastPressRate);
            float recoverRate = Mathf.Clamp01(-dentRate / RecoveryRateRef);
            float depth = Mathf.Clamp01(peakDepth);

            // ---- イベント ----
            if (contacts > prevContacts)
            {
                bool first = prevContacts == 0;
                f.TransientIntensity = first ? Mathf.Lerp(0.26f, 0.4f, p.Softness) : 0.22f;
                f.TransientSharpness = first ? 0.1f : 0.18f;
                f.SquishStrength = squishAmount * Mathf.Lerp(0.6f, 1f, p.Softness) * Range(0.85f, 1f) *
                                   (first ? 1f : 0.7f);
                f.SquishPitch = soundPitch * Range(0.92f, 1.08f) * Mathf.Lerp(1.15f, 0.92f, p.Softness);
            }
            else if (contacts == 0 && prevContacts > 0)
            {
                f.TransientIntensity = Mathf.Lerp(0.08f, 0.22f, p.StickyAmount);
                f.TransientSharpness = 0.05f;
                f.PuffStrength = airAmount * Mathf.Lerp(0.15f, 0.5f, p.StickyAmount) * Range(0.8f, 1f);
            }
            prevContacts = contacts;

            // 限界付近（底付き）
            bool limitNow = contacts > 0 && depth > LimitThreshold;
            if (limitNow && !atLimit && f.TransientIntensity <= 0f)
            {
                f.TransientIntensity = 0.34f;
                f.TransientSharpness = 0.55f;
                f.SquishStrength = Mathf.Max(f.SquishStrength, squishAmount * 0.35f);
                f.SquishPitch = soundPitch * Range(0.78f, 0.88f);
                tickTimer = Range(0.07f, 0.12f);
            }
            atLimit = limitNow;

            // ---- 連続振動 ----
            if (contacts > 0)
            {
                // フォームの粒感: 30Hz 前後でランダムに揺らぐ値を滑らかに追う
                grainTimer -= dt;
                if (grainTimer <= 0f) { grainTarget = Range(-1f, 1f); grainTimer = Range(0.025f, 0.045f); }
                grain += (grainTarget - grain) * (1f - Mathf.Exp(-dt * 40f));

                float shaped = Mathf.Pow(pressRate, 0.7f);
                f.HapticActive = true;
                // 指を止めて押しているだけの時はほぼ消える（ずっと震えていると不自然）
                float moving = Mathf.Clamp01(shaped * 4f);
                f.HapticIntensity = Mathf.Clamp01(0.04f + 0.55f * shaped + 0.12f * depth * (0.2f + 0.8f * moving) +
                                                  0.07f * grain * (shaped + 0.15f) +
                                                  (atLimit ? 0.1f * moving : 0f));
                f.HapticSharpness = Mathf.Clamp01(0.05f + 0.25f * depth + 0.15f * shaped);

                if (atLimit && pressRate > 0.04f)
                {
                    tickTimer -= dt;
                    if (tickTimer <= 0f && f.TransientIntensity <= 0f)
                    {
                        f.TransientIntensity = Range(0.12f, 0.2f);
                        f.TransientSharpness = 0.5f;
                        tickTimer = Range(0.07f, 0.12f);
                    }
                }
            }

            // ---- 連続音 ----
            float press = Mathf.Pow(pressRate, 0.8f);
            f.Air = airAmount * (0.32f * press + 0.025f * recoverRate);
            f.AirBrightness = Mathf.Clamp01(0.25f + 0.75f * pressRate);
            f.CracklePerSecond = crackleAmount * (420f * press + 22f * recoverRate);
            f.CrackleGain = crackleAmount * (press > 0.01f ? 0.55f : 0.22f);
            return f;
        }
    }
}
