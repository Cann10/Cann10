using System;
using System.Collections.Generic;
using UnityEngine;

namespace SquishLab.Tests
{
    /// <summary>音（SquishSynth）と触覚マッピング（FeedbackMapper）のテスト。</summary>
    public static class FeedbackTests
    {
        public static void RunAll(Action<string, Action> run)
        {
            run("synth: 無音時は無音、値は有限で ±1 に収まる", SynthIsSafe);
            run("synth: 押し込みの速さで空気音が大きくなる", SynthAirFollowsRate);
            run("synth: むにゅは毎回少しずつ違う", SynthSquishVaries);
            run("haptics: 押し始め・押し込み・限界・離す の段階", HapticStages);
        }

        static void Check(bool c, string m) { if (!c) throw new Exception(m); }

        static float[] Render(SquishSynth s, float seconds, int channels = 2)
        {
            int frames = (int)(s.SampleRate * seconds);
            var all = new float[frames * channels];
            var buf = new float[1024 * channels];
            for (int o = 0; o < all.Length; o += buf.Length)
            {
                Array.Clear(buf, 0, buf.Length);
                s.Render(buf, channels);
                Array.Copy(buf, 0, all, o, Math.Min(buf.Length, all.Length - o));
            }
            return all;
        }

        static float Rms(float[] x)
        {
            double a = 0; foreach (var v in x) a += v * v;
            return (float)Math.Sqrt(a / x.Length);
        }

        static void SynthIsSafe()
        {
            var s = new SquishSynth(48000);
            var silent = Render(s, 0.5f);
            Check(Rms(silent) < 1e-6f, $"何もしていないのに音が出ている rms={Rms(silent)}");

            s.SetContinuous(1f, 1f, 2000f, 1f, 2.5f);
            s.TriggerSquish(1.5f, 2f);
            s.TriggerPuff(1f);
            var loud = Render(s, 1f);
            foreach (var v in loud)
            {
                Check(!float.IsNaN(v) && !float.IsInfinity(v), "NaN/Inf");
                Check(Math.Abs(v) < 1f, $"クリップしている {v}");
            }
        }

        static void SynthAirFollowsRate()
        {
            float RmsAt(float air)
            {
                var s = new SquishSynth(48000, 7);
                s.SetContinuous(air, 0.5f, 0f, 0f, 1f);
                Render(s, 0.2f); // 立ち上がりを捨てる
                return Rms(Render(s, 0.5f));
            }
            float quiet = RmsAt(0.02f), loud = RmsAt(0.3f);
            Console.WriteLine($"       air rms: recovery-level={quiet:F4} press-level={loud:F4}");
            Check(loud > quiet * 5f, "空気音が押し込み量に追従していない");
            Check(quiet > 0f, "復元中のごく小さな音が出ていない");
        }

        static void SynthSquishVaries()
        {
            var s = new SquishSynth(48000, 99);
            var shots = new List<float[]>();
            for (int k = 0; k < 3; k++)
            {
                s.TriggerSquish(1f, 1f);
                shots.Add(Render(s, 0.3f, 1));
            }
            float r0 = Rms(shots[0]);
            Check(r0 > 0.01f, $"むにゅが鳴っていない rms={r0}");
            // 同じ音の使い回しではない
            double diff = 0;
            for (int i = 0; i < shots[0].Length; i++) diff += Math.Abs(shots[0][i] - shots[1][i]);
            Check(diff / shots[0].Length > 1e-3, "毎回まったく同じ波形");
            Console.WriteLine($"       squish rms: {string.Join(", ", shots.ConvertAll(x => Rms(x).ToString("F3")))}");
        }

        static void HapticStages()
        {
            var mesh = BreadMeshBuilder.Build(BreadMeshBuilder.Settings.Coarse);
            var p = SquishParams.SlowRiseBread;
            p.MaxDeformation = 0.5f;
            var solver = new SquishSolver(mesh, p);
            var map = new FeedbackMapper(3);
            int v = 0; float best = -1;
            for (int i = 0; i < solver.VertexCount; i++)
                if (solver.RestNormals[i].y > 0.95f && solver.RestPositions[i].y > best) { best = solver.RestPositions[i].y; v = i; }
            var list = new List<SquishContact>();
            const float dt = 1f / 60f;

            int transients = 0, limitHits = 0;
            float maxIntensityPress = 0, holdIntensity = 0;
            bool startTransient = false, releaseTransient = false, activeAfterRelease = false;
            for (int f = 0; f < 60 * 4; f++)
            {
                float t = f * dt;
                list.Clear();
                if (t < 2.5f)
                    list.Add(new SquishContact { Point = solver.RestPositions[v], Normal = solver.RestNormals[v], Anchor = solver.RestPositions[v], HeldTime = t, RadiusMul = 1f });
                solver.Step(dt, list);
                var fr = map.Update(dt, list.Count, solver.DentVolumeRate, solver.PeakDepth, solver.Params, 1f, 1f, 1f, 1f);
                if (fr.TransientIntensity > 0f)
                {
                    transients++;
                    if (f == 0) startTransient = true;
                    if (fr.TransientSharpness >= 0.5f) limitHits++;
                    if (t >= 2.5f && t < 2.5f + dt * 1.5f) releaseTransient = true;
                }
                if (t < 0.3f) maxIntensityPress = Math.Max(maxIntensityPress, fr.HapticIntensity);
                if (t > 2.2f && t < 2.5f) holdIntensity = fr.HapticIntensity;
                if (t > 2.6f && fr.HapticActive) activeAfterRelease = true;
            }
            Console.WriteLine($"       press={maxIntensityPress:F2} hold={holdIntensity:F2} transients={transients} limit={limitHits}");
            Check(startTransient, "押し始めの単発がない");
            Check(maxIntensityPress > holdIntensity * 2f, "押し込み中の方が、止めて押している時より強くない");
            Check(maxIntensityPress < 0.85f, "強すぎる（柔らかい触覚にしたい）");
            Check(limitHits >= 1, "限界付近の手応えがない");
            Check(releaseTransient, "離した瞬間の単発がない");
            Check(!activeAfterRelease, "離した後も連続振動が続いている");
        }
    }
}
