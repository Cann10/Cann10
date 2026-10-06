using System;
using System.Threading;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 変形に同期するプロシージャル ASMR シンセ（エンジン非依存, オーディオスレッドで Render を呼ぶ）。
    ///
    /// 録音済みの音を毎回同じように鳴らすのではなく、ソルバーの「へこみ体積の変化率」から
    /// 3 つの層をリアルタイムに合成する:
    ///   ・Air      … 押し込む速さに比例して空気が抜ける「ぷしゅ」（帯域ノイズ）
    ///   ・Crackle  … フォームの気泡がつぶれる細かいプチプチ（減衰する短い共振の粒）
    ///   ・Squish   … 触れた瞬間の低い「むにゅ」（下降する低音 + 掃引フォルマント）
    /// 復元中は Air と Crackle がごく小さく鳴り続ける。
    /// 音量・ピッチ・粒の周波数は毎回ランダムに揺らぐので同じ音にならない。
    /// </summary>
    public sealed class SquishSynth
    {
        public readonly float SampleRate;

        public SquishSynth(float sampleRate, uint seed = 0x9E3779B9u)
        {
            SampleRate = sampleRate > 0f ? sampleRate : 48000f;
            rng = seed == 0 ? 0x9E3779B9u : seed;
        }

        /// <summary>全体音量（0 で無音）。</summary>
        public float Master { get => master; set => master = Mathf.Max(0f, value); }

        // ---- メインスレッド → オーディオスレッド（float の読み書きは原子的）----
        volatile float airTarget;       // 0..1
        volatile float airBrightness;   // 0..1 （速いほど高い音）
        volatile float crackleRate;     // 粒/秒
        volatile float crackleGain;     // 0..1
        volatile float master = 1f;
        volatile float pitchBase = 1f;

        // むにゅのトリガー（最新のものだけ有効）
        int squishSerial, squishConsumed;
        volatile float squishStrength, squishPitch;
        int puffSerial, puffConsumed;
        volatile float puffStrength;

        // ---- オーディオスレッドの状態 ----
        uint rng;
        float airGain, airLow, airBand, flutter, burst;
        float airFc = 1500f;

        const int GrainCount = 16;
        readonly float[] gY1 = new float[GrainCount];
        readonly float[] gY2 = new float[GrainCount];
        readonly float[] gC = new float[GrainCount];   // 2 r cos(w)
        readonly float[] gR2 = new float[GrainCount];  // r²
        readonly float[] gLife = new float[GrainCount];
        int grainCursor;

        bool sqActive;
        float sqT, sqDur, sqF0, sqPhase, sqGain, sqPitch, sqLow, sqBand;
        float outLp;

        // ---------------- メインスレッド API ----------------

        /// <summary>連続音のパラメータ（毎フレーム呼ぶ）。</summary>
        public void SetContinuous(float air, float brightness, float cracklePerSecond, float crackle, float pitch)
        {
            airTarget = Mathf.Clamp01(air);
            airBrightness = Mathf.Clamp01(brightness);
            crackleRate = Mathf.Max(0f, cracklePerSecond);
            crackleGain = Mathf.Clamp01(crackle);
            pitchBase = Mathf.Clamp(pitch, 0.4f, 2.5f);
        }

        /// <summary>押した瞬間の「むにゅ」。</summary>
        public void TriggerSquish(float strength, float pitch)
        {
            if (strength <= 0f) return;
            squishStrength = strength;
            squishPitch = pitch;
            Interlocked.Increment(ref squishSerial);
        }

        /// <summary>離した瞬間などの短い空気の「ぷっ」。</summary>
        public void TriggerPuff(float strength)
        {
            if (strength <= 0f) return;
            puffStrength = strength;
            Interlocked.Increment(ref puffSerial);
        }

        // ---------------- オーディオスレッド ----------------

        float Noise()
        {
            // xorshift32 → [-1, 1)
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        float Rand01() => (Noise() + 1f) * 0.5f;

        /// <summary>data に合成音を「足し込む」（AudioSource の再生音を消さないため）。</summary>
        public void Render(float[] data, int channels)
        {
            float sr = SampleRate;
            float m = master;
            if (m <= 0f && !sqActive && airGain < 1e-4f) return;

            // トリガーの取り込み
            int sSerial = Volatile.Read(ref squishSerial);
            if (sSerial != squishConsumed)
            {
                squishConsumed = sSerial;
                StartSquish(squishStrength, squishPitch);
            }
            int pSerial = Volatile.Read(ref puffSerial);
            if (pSerial != puffConsumed)
            {
                puffConsumed = pSerial;
                burst = Mathf.Max(burst, puffStrength);
            }

            float aTarget = airTarget;
            float pitch = pitchBase;
            // 空気音の中心周波数: 速く押すほど高く。バッファごとに少しランダムに揺らす
            float fcTarget = Mathf.Lerp(700f, 2600f, airBrightness) * pitch * (0.9f + 0.2f * Rand01());
            airFc += (fcTarget - airFc) * 0.3f;
            float airF = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(airFc, sr / 7f) / sr);
            const float airQ = 1.1f;

            float attack = 1f - Mathf.Exp(-1f / (0.012f * sr));
            float release = 1f - Mathf.Exp(-1f / (0.09f * sr));
            float burstDecay = Mathf.Exp(-1f / (0.05f * sr));
            float flutterK = 1f - Mathf.Exp(-2f * Mathf.PI * 25f / sr);

            float grainProb = crackleRate / sr;
            float cGain = crackleGain;

            int frames = data.Length / channels;
            for (int f = 0; f < frames; f++)
            {
                float s = 0f;

                // --- Air ---
                float target = aTarget;
                airGain += (target - airGain) * (target > airGain ? attack : release);
                burst *= burstDecay;
                float ag = airGain + burst * 0.6f;
                if (ag > 1e-5f)
                {
                    float n = Noise();
                    float hp = n - airLow - airQ * airBand;
                    airBand += airF * hp;
                    airLow += airF * airBand;
                    flutter += (Noise() - flutter) * flutterK; // 不規則なゆらぎ
                    s += airBand * ag * (0.75f + 0.5f * flutter) * 1.0f;
                }

                // --- Crackle: 確率的に粒を生成 ---
                if (grainProb > 0f && Rand01() < grainProb)
                    SpawnGrain(sr, pitch, cGain);
                for (int g = 0; g < GrainCount; g++)
                {
                    if (gLife[g] <= 0f) continue;
                    float y = gC[g] * gY1[g] - gR2[g] * gY2[g];
                    gY2[g] = gY1[g];
                    gY1[g] = y;
                    s += y;
                    gLife[g] -= 1f;
                }

                // --- Squish（むにゅ）---
                if (sqActive) s += SquishSample(sr);

                // 角を取る軽いローパス + ソフトクリップ
                outLp += (s - outLp) * 0.55f;
                float o = outLp * m;
                o = o / (1f + Mathf.Abs(o));

                int idx = f * channels;
                for (int c = 0; c < channels; c++) data[idx + c] += o;
            }
        }

        void SpawnGrain(float sr, float pitch, float gain)
        {
            int g = grainCursor;
            grainCursor = (grainCursor + 1) % GrainCount;
            float freq = Mathf.Lerp(1800f, 6500f, Rand01() * Rand01()) * pitch;
            float w = 2f * Mathf.PI * Mathf.Min(freq, sr * 0.45f) / sr;
            float decaySec = Mathf.Lerp(0.0015f, 0.006f, Rand01());
            float r = Mathf.Exp(-1f / (decaySec * sr));
            gC[g] = 2f * r * Mathf.Cos(w);
            gR2[g] = r * r;
            float amp = gain * Mathf.Lerp(0.15f, 1f, Rand01() * Rand01()) * 0.4f;
            gY1[g] = amp;
            gY2[g] = 0f;
            gLife[g] = decaySec * sr * 6f;
        }

        void StartSquish(float strength, float pitch)
        {
            sqActive = true;
            sqT = 0f;
            sqPitch = pitch * (0.93f + 0.14f * Rand01());
            sqDur = Mathf.Lerp(0.14f, 0.24f, Rand01()) / Mathf.Sqrt(sqPitch);
            sqF0 = Mathf.Lerp(130f, 175f, Rand01()) * sqPitch;
            sqGain = Mathf.Clamp(strength, 0f, 1.5f) * (0.85f + 0.15f * Rand01());
            sqPhase = 0f;
            sqLow = sqBand = 0f;
        }

        float SquishSample(float sr)
        {
            float u = sqT / sqDur;
            if (u >= 1f) { sqActive = false; return 0f; }
            const float atk = 0.06f;
            float env = u < atk ? u / atk : Mathf.Exp(-(u - atk) * 4.6f / (1f - atk));

            // 低い「むっ」: 下がっていく丸い正弦
            float f0 = sqF0 * (1f - 0.28f * u);
            sqPhase += 2f * Mathf.PI * f0 / sr;
            if (sqPhase > 2f * Mathf.PI) sqPhase -= 2f * Mathf.PI;
            float body = Mathf.Sin(sqPhase) * 0.5f;

            // 湿った「にゅ」: 高→低へ掃引する帯域ノイズ
            float fc = Mathf.Lerp(950f, 300f, u) * sqPitch;
            float ff = 2f * Mathf.Sin(Mathf.PI * fc / sr);
            float n = Noise();
            float hp = n - sqLow - 0.3f * sqBand;
            sqBand += ff * hp;
            sqLow += ff * sqBand;
            float wet = sqBand * 0.35f;

            sqT += 1f / sr;
            return (body + wet) * env * sqGain * 0.9f;
        }
    }
}
