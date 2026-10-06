using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SquishLab.Tests
{
    public static class Program
    {
        static int failures;

        public static int Main(string[] args)
        {
            int dumpAt = Array.IndexOf(args, "--dump");
            if (dumpAt >= 0 && dumpAt + 1 < args.Length) { DumpObj(args[dumpAt + 1]); return 0; }

            Run("mesh: 閉じていて外向き", MeshIsClosedAndOutward);
            Run("mesh: 食パンの見た目の属性", MeshHasBreadFeatures);
            Run("press: 局所的にへこむ", PressIsLocal);
            Run("press: 押した場所で形が変わる", PressPositionMatters);
            Run("press: 触れた瞬間に反応する", RespondsImmediately);
            Run("press: 押し続けるとじわっと深くなる", HoldingSinksDeeper);
            Run("release: スローライズで数秒かけて戻る", ReleaseIsSlowRise);
            Run("release: 強い反発をしない", ReleaseDoesNotBounce);
            Run("params: RecoverySpeed で戻り時間が変わる", RecoverySpeedIsMonotonic);
            Run("params: MaxDeformation で深さが変わる", MaxDeformationLimitsDepth);
            Run("drag: なぞった跡がゆっくり戻る", DragLeavesSlowTrail);
            Run("pinch: 2本指の間が持ち上がる", PinchLiftsBetweenFingers);
            Run("stability: ランダム操作で発散しない", RandomAbuseIsStable);
            Run("sleep: 戻りきったら計算を止める", SleepsAfterRecovery);
            Run("perf: 1フレームの計算時間", Performance);
            FeedbackTests.RunAll(Run);

            Console.WriteLine(failures == 0 ? "\nALL PASSED" : $"\n{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>目視確認用: 静止形状と、押した直後・離して1秒後の形状を OBJ で書き出す。</summary>
        static void DumpObj(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var s = NewSolver();
            void Write(string name)
            {
                var sb = new System.Text.StringBuilder();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                for (int i = 0; i < s.VertexCount; i++)
                {
                    var p = s.Positions[i];
                    // 頂点 + 独自行 "vc 耳マスク 圧縮量"（プレビュー用。一般的な OBJ ビューアは無視する）
                    sb.Append(string.Format(inv, "v {0} {1} {2}\nvc {3} {4}\n", p.x, p.y, p.z, Mesh.CrustMask[i], s.Compression[i]));
                }
                var t = Mesh.Triangles;
                for (int i = 0; i < t.Length; i += 3) sb.Append($"f {t[i] + 1} {t[i + 1] + 1} {t[i + 2] + 1}\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".obj"), sb.ToString());
            }
            Write("rest");
            if (Environment.GetEnvironmentVariable("SQUISH_SEQ") == "1")
            {
                // 押す(0.9秒) → 離す → 4.6秒 の連番（15fps）
                int pv = TopVertex(s, -0.2f, 0.05f);
                var one = new List<SquishContact> { default };
                int frame = 0;
                for (float t = 0; t < 5.5f; t += Dt)
                {
                    bool pressing = t < 0.9f;
                    if (pressing) { one[0] = ContactAt(s, pv, t); s.Step(Dt, one); }
                    else s.Step(Dt, null);
                    if (Math.Abs(t * 15f - Math.Round(t * 15f)) < 0.13f && (int)Math.Round(t * 15f) == frame)
                        Write($"seq{frame++:D3}");
                }
                return;
            }
            int v = TopVertex(s, -0.25f, 0.15f);
            Press(s, v, 1.0f);
            Write("pressed");
            Idle(s, 1.0f);
            Write("released1s");
            Idle(s, 2.0f);
            Write("released3s");
        }

        static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"  ok   {name}");
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine($"  FAIL {name}\n       {e.Message}");
            }
        }

        static void Check(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }

        // ------------------------------------------------------------------

        static SquishMeshData mesh;
        static SquishMeshData Mesh => mesh ??= BreadMeshBuilder.Build(BreadMeshBuilder.Settings.Default);

        const float Dt = 1f / 60f;

        static SquishSolver NewSolver(SquishParams? p = null) => new SquishSolver(Mesh, p ?? SquishParams.SlowRiseBread);

        /// <summary>天面で (x,z) に最も近い頂点。</summary>
        static int TopVertex(SquishSolver s, float x, float z)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < s.VertexCount; i++)
            {
                if (s.RestNormals[i].y < 0.9f) continue;
                var p = s.RestPositions[i];
                float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static SquishContact ContactAt(SquishSolver s, int v, float held, int id = 0) => new SquishContact
        {
            FingerId = id,
            Point = s.RestPositions[v],
            Normal = s.RestNormals[v],
            Anchor = s.RestPositions[v],
            HeldTime = held,
            RadiusMul = 1f,
        };

        static float Inward(SquishSolver s, int i) => -Vector3.Dot(s.DisplacementOf(i), s.RestNormals[i]);

        /// <summary>v を seconds 秒押し続ける。</summary>
        static void Press(SquishSolver s, int v, float seconds)
        {
            var list = new List<SquishContact>(1) { default };
            for (float t = 0; t < seconds; t += Dt)
            {
                list[0] = ContactAt(s, v, t);
                s.Step(Dt, list);
            }
        }

        static void Idle(SquishSolver s, float seconds)
        {
            for (float t = 0; t < seconds; t += Dt) s.Step(Dt, null);
        }

        // ------------------------------------------------------------------

        static void MeshIsClosedAndOutward()
        {
            var m = Mesh;
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i < m.Triangles.Length; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = m.Triangles[i + e], b = m.Triangles[i + (e + 1) % 3];
                    Check(a != b, "縮退三角形");
                    edges.TryGetValue((a, b), out int n);
                    edges[(a, b)] = n + 1;
                }
            }
            foreach (var kv in edges)
            {
                Check(kv.Value == 1, $"有向辺 {kv.Key} が {kv.Value} 回使われている（向きが不一致）");
                Check(edges.ContainsKey((kv.Key.Item2, kv.Key.Item1)), $"辺 {kv.Key} が穴になっている");
            }

            double vol = 0;
            for (int i = 0; i < m.Triangles.Length; i += 3)
            {
                var a = m.Vertices[m.Triangles[i]];
                var b = m.Vertices[m.Triangles[i + 1]];
                var c = m.Vertices[m.Triangles[i + 2]];
                vol += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            Check(vol > 0, $"符号付き体積が負（法線が内向き）: {vol}");
            Check(m.Vertices.Length < 12000, $"頂点数が多すぎる: {m.Vertices.Length}");
            Console.WriteLine($"       vertices={m.Vertices.Length} tris={m.Triangles.Length / 3} volume={vol:F3} height={m.Height:F3}");
        }

        static void MeshHasBreadFeatures()
        {
            var s = NewSolver();
            var m = Mesh;
            int top = TopVertex(s, 0, 0);
            Check(m.CrustMask[top] < 0.05f, "天面中央がクラスト扱いになっている");
            // 側面中央（x 最大, y 中央）は耳
            int side = -1; float bx = float.MinValue;
            for (int i = 0; i < m.Vertices.Length; i++)
            {
                var p = m.Vertices[i];
                if (Math.Abs(p.y - m.Height * 0.5f) < 0.05f && Math.Abs(p.z) < 0.05f && p.x > bx) { bx = p.x; side = i; }
            }
            Check(side >= 0 && m.CrustMask[side] > 0.95f, "側面が耳になっていない");
            // 奥（山側）の方が手前より長い = 食パンの山
            float maxZ = float.MinValue, minZ = float.MaxValue;
            foreach (var p in m.Vertices) { maxZ = Math.Max(maxZ, p.z); minZ = Math.Min(minZ, p.z); }
            Check(maxZ > -minZ + 0.1f, $"山の盛り上がりがない maxZ={maxZ} minZ={minZ}");
            // 天面はふっくら: 中央が縁より高い
            int edge = TopVertex(s, 0.6f, -0.55f);
            Check(m.Vertices[top].y > m.Vertices[edge].y + 0.03f, "天面がふっくらしていない");
        }

        static void PressIsLocal()
        {
            var s = NewSolver();
            int v = TopVertex(s, -0.35f, 0.1f);
            Press(s, v, 0.8f);
            float center = Inward(s, v);
            Check(center > 0.3f * s.MaxDepthWorld, $"中心が十分へこんでいない: {center:F3} / {s.MaxDepthWorld:F3}");

            // 距離ごとの平均へこみが単調減少（中心ほど強く、周囲ほど弱く）
            var rp = s.RestPositions[v];
            int bins = 8; float binW = 0.08f;
            var sum = new float[bins]; var cnt = new int[bins];
            float far = 0f;
            for (int i = 0; i < s.VertexCount; i++)
            {
                if (s.RestNormals[i].y < 0.8f) continue;
                float d = Vector3.Distance(s.RestPositions[i], rp);
                int b = (int)(d / binW);
                if (b < bins) { sum[b] += Inward(s, i); cnt[b]++; }
                if (d > 1.4f) far = Math.Max(far, Math.Abs(Inward(s, i)));
            }
            var avg = new float[bins];
            for (int b = 0; b < bins; b++) avg[b] = cnt[b] > 0 ? sum[b] / cnt[b] : 0f;
            for (int b = 1; b < 5; b++)
                Check(avg[b] < avg[b - 1] + 1e-4f, $"へこみが距離で単調減少していない: bin{b - 1}={avg[b - 1]:F4} bin{b}={avg[b]:F4}");
            Check(avg[1] > avg[0] * 0.5f, "周囲が追従していない（針で刺したような段差）");
            CheckNoInvertedTriangles(s);
            Check(far < center * 0.06f, $"遠くの頂点まで動いている: {far:F4} vs center {center:F4}");

            // 底面は動かない
            float bottom = 0f;
            for (int i = 0; i < s.VertexCount; i++)
                if (s.RestNormals[i].y < -0.9f) bottom = Math.Max(bottom, s.DisplacementOf(i).magnitude);
            Check(bottom < center * 0.05f, $"底面が動いている: {bottom:F4}");
            Console.WriteLine($"       center={center:F3} profile=[{string.Join(", ", Array.ConvertAll(avg, a => a.ToString("F3")))}]");
        }

        /// <summary>変形後に裏返った（めり込んで折れた）三角形がないこと。</summary>
        static void CheckNoInvertedTriangles(SquishSolver s)
        {
            var t = Mesh.Triangles;
            int bad = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                var n0 = Vector3.Cross(s.RestPositions[b] - s.RestPositions[a], s.RestPositions[c] - s.RestPositions[a]);
                var n1 = Vector3.Cross(s.Positions[b] - s.Positions[a], s.Positions[c] - s.Positions[a]);
                if (Vector3.Dot(n0, n1) <= 0f) bad++;
            }
            Check(bad == 0, $"裏返った三角形が {bad} 個ある");
        }

        static Vector3 DentCentroid(SquishSolver s)
        {
            Vector3 c = Vector3.zero; float w = 0;
            for (int i = 0; i < s.VertexCount; i++)
            {
                float d = Math.Max(0f, Inward(s, i));
                c += s.RestPositions[i] * d; w += d;
            }
            return c / Math.Max(w, 1e-6f);
        }

        static void PressPositionMatters()
        {
            var a = NewSolver(); var b = NewSolver();
            Press(a, TopVertex(a, -0.5f, -0.3f), 0.5f);
            Press(b, TopVertex(b, 0.45f, 0.4f), 0.5f);
            var ca = DentCentroid(a); var cb = DentCentroid(b);
            Check(Math.Abs(ca.x + 0.5f) < 0.08f && Math.Abs(ca.z + 0.3f) < 0.08f, $"左手前を押した重心がずれている {ca}");
            Check(Math.Abs(cb.x - 0.45f) < 0.08f && Math.Abs(cb.z - 0.4f) < 0.08f, $"右奥を押した重心がずれている {cb}");
        }

        static void RespondsImmediately()
        {
            // 「5秒以内に気持ちいい」には押した瞬間の手応えが要る: 0.1 秒で最終的な深さの半分以上
            var s = NewSolver();
            int v = TopVertex(s, 0f, 0f);
            Press(s, v, 0.1f);
            float early = Inward(s, v);
            Press(s, v, 2f);
            float settled = Inward(s, v);
            Console.WriteLine($"       0.1s={early / settled:P0} of settled depth");
            Check(early > settled * 0.45f, $"反応が遅い: 0.1秒で {early / settled:P0}");
        }

        static void HoldingSinksDeeper()
        {
            var s = NewSolver();
            int v = TopVertex(s, 0, 0);
            Press(s, v, 0.15f);
            float early = Inward(s, v);
            Press(s, v, 1.5f);
            float late = Inward(s, v);
            Check(late > early * 1.2f, $"押し続けても深くならない early={early:F3} late={late:F3}");
            Check(late <= s.MaxDepthWorld * 1.2f, $"最大へこみを超えている {late:F3} > {s.MaxDepthWorld:F3}");
        }

        /// <summary>離してから peak の frac 以下に戻るまでの秒数。</summary>
        static float RecoveryTime(SquishSolver s, int v, float frac, out float peak, out float at03, out float minInward)
        {
            peak = Inward(s, v);
            at03 = 0; minInward = float.MaxValue;
            float t = 0; float hit = -1;
            while (t < 30f)
            {
                s.Step(Dt, null);
                t += Dt;
                float cur = Inward(s, v);
                minInward = Math.Min(minInward, cur);
                if (Math.Abs(t - 0.3f) < Dt * 0.5f) at03 = cur;
                if (hit < 0 && cur <= peak * frac) hit = t;
            }
            return hit;
        }

        static void ReleaseIsSlowRise()
        {
            var s = NewSolver();
            int v = TopVertex(s, 0.1f, 0f);
            Press(s, v, 1.2f);
            float t95 = RecoveryTime(s, v, 0.05f, out float peak, out float at03, out _);
            Console.WriteLine($"       peak={peak:F3} at0.3s={at03 / peak:P0} t95={t95:F2}s (設定 {SquishParams.SlowRiseBread.RecoverySeconds:F2}s)");
            Check(at03 > peak * 0.7f, $"離して0.3秒で戻りすぎ（即座に戻っている）: {at03 / peak:P0}");
            Check(t95 >= 3f && t95 <= 6f, $"95%復元まで {t95:F2} 秒（目標 3〜6 秒）");
            float set = s.Params.RecoverySeconds;
            Check(Math.Abs(t95 - set) < set * 0.3f, $"実測 {t95:F2}s が RecoverySeconds {set:F2}s と大きくずれている");
        }

        static void ReleaseDoesNotBounce()
        {
            foreach (var p in new[] { SquishParams.SlowRiseBread, SquishParams.FastFoam, SquishParams.Mochi })
            {
                var s = NewSolver(p);
                int v = TopVertex(s, 0f, 0f);
                Press(s, v, 1f);
                RecoveryTime(s, v, 0.05f, out float peak, out _, out float minInward);
                float overshoot = Math.Max(0f, -minInward) / peak;
                Console.WriteLine($"       recovery={p.RecoverySeconds:F2}s overshoot={overshoot:P1}");
                Check(overshoot < 0.08f, $"反発が強すぎる: overshoot {overshoot:P1}");
            }
        }

        static void RecoverySpeedIsMonotonic()
        {
            float prev = float.MaxValue;
            foreach (float speed in new[] { 0.1f, 0.4f, 0.7f, 0.95f })
            {
                var p = SquishParams.SlowRiseBread; p.RecoverySpeed = speed;
                var s = NewSolver(p);
                int v = TopVertex(s, 0f, 0f);
                Press(s, v, 1f);
                float t = RecoveryTime(s, v, 0.1f, out _, out _, out _);
                Console.WriteLine($"       RecoverySpeed={speed:F2} → t90={t:F2}s");
                Check(t > 0 && t < prev, $"RecoverySpeed={speed} で遅くなった/戻らない: {t}");
                prev = t;
            }
        }

        static void MaxDeformationLimitsDepth()
        {
            float Depth(float maxDef)
            {
                var p = SquishParams.SlowRiseBread; p.MaxDeformation = maxDef;
                var s = NewSolver(p);
                int v = TopVertex(s, 0f, 0f);
                Press(s, v, 3f);
                float d = Inward(s, v);
                Check(d <= s.MaxDepthWorld * 1.2f, $"MaxDeformation={maxDef} を超えた {d} > {s.MaxDepthWorld}");
                return d;
            }
            float a = Depth(0.3f), b = Depth(0.8f);
            Check(b > a * 2f, $"MaxDeformation が効いていない {a} vs {b}");
        }

        static void DragLeavesSlowTrail()
        {
            var s = NewSolver();
            int start = TopVertex(s, -0.5f, 0f), end = TopVertex(s, 0.5f, 0f);
            var list = new List<SquishContact> { default };
            float held = 0;
            // 0.5秒押してから 0.6 秒で右へなぞる
            for (; held < 0.5f; held += Dt) { list[0] = ContactAt(s, start, held); s.Step(Dt, list); }
            Vector3 anchor = s.RestPositions[start];
            for (float t = 0; t < 0.6f; t += Dt, held += Dt)
            {
                float u = t / 0.6f;
                var p = Vector3.Lerp(s.RestPositions[start], s.RestPositions[end], u);
                int v = TopVertex(s, p.x, p.z);
                var c = ContactAt(s, v, held);
                // 伸びの限界でアンカーが滑る（入力層と同じ処理）
                float maxGrab = s.Params.MaxStretch * s.Params.FingerRadius * s.Params.RadiusScale;
                var g = c.Point - anchor;
                if (g.magnitude > maxGrab) anchor = c.Point - g.normalized * maxGrab;
                c.Anchor = anchor;
                list[0] = c;
                s.Step(Dt, list);
            }
            float trailStart = Inward(s, start);
            float now = Inward(s, end);
            Console.WriteLine($"       trail@start={trailStart:F3} under finger={now:F3}");
            Check(trailStart > now * 0.35f, "なぞった跡がすぐ消えている");
            Idle(s, 0.5f);
            Check(Inward(s, start) > trailStart * 0.4f, "離した直後に跡が消えている");
        }

        static void PinchLiftsBetweenFingers()
        {
            var p = SquishParams.SlowRiseBread; p.Stretchiness = 0.8f;
            var s = NewSolver(p);
            int a0 = TopVertex(s, -0.4f, 0f), b0 = TopVertex(s, 0.4f, 0f), mid = TopVertex(s, 0f, 0f);
            var list = new List<SquishContact> { default, default };
            var anchorA = s.RestPositions[a0]; var anchorB = s.RestPositions[b0];
            for (float t = 0; t < 0.8f; t += Dt)
            {
                float u = Math.Min(1f, t / 0.5f);
                int a = TopVertex(s, -0.4f + 0.2f * u, 0f), b = TopVertex(s, 0.4f - 0.2f * u, 0f);
                var ca = ContactAt(s, a, t, 0); ca.Anchor = anchorA;
                var cb = ContactAt(s, b, t, 1); cb.Anchor = anchorB;
                list[0] = ca; list[1] = cb;
                s.Step(Dt, list);
            }
            float lift = -Inward(s, mid);
            Console.WriteLine($"       mid lift={lift:F3}");
            Check(lift > 0.02f, $"つまんだ間が持ち上がっていない: {lift:F4}");
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                                           float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static void RandomAbuseIsStable()
        {
            var rng = new System.Random(1234);
            for (int trial = 0; trial < 6; trial++)
            {
                var p = new SquishParams
                {
                    Softness = (float)rng.NextDouble(),
                    RecoverySpeed = (float)rng.NextDouble(),
                    Elasticity = (float)rng.NextDouble(),
                    Damping = (float)rng.NextDouble(),
                    MaxDeformation = 0.05f + 0.9f * (float)rng.NextDouble(),
                    StickyAmount = (float)rng.NextDouble(),
                    Stretchiness = (float)rng.NextDouble(),
                    FingerRadius = 0.1f + 0.7f * (float)rng.NextDouble(),
                    Bulge = 0.5f * (float)rng.NextDouble(),
                    Coupling = (float)rng.NextDouble(),
                    Wobble = (float)rng.NextDouble(),
                };
                if (trial == 0) { p.Elasticity = 1; p.Damping = 0; p.Softness = 1; p.RecoverySpeed = 1; }
                var s = new SquishSolver(BreadMeshBuilder.Build(BreadMeshBuilder.Settings.Coarse), p);
                var list = new List<SquishContact>();
                var held = new float[3];
                for (int f = 0; f < 600; f++)
                {
                    // 不規則な dt（カクつき・一時停止明け）も混ぜる
                    float dt = f % 97 == 0 ? 0.25f : (float)(0.004 + rng.NextDouble() * 0.03);
                    list.Clear();
                    int n = (f / 40) % 4;
                    for (int c = 0; c < n && c < 3; c++)
                    {
                        int v = rng.Next(s.VertexCount);
                        held[c] += dt;
                        list.Add(new SquishContact
                        {
                            FingerId = c, Point = s.RestPositions[v], Normal = s.RestNormals[v],
                            Anchor = s.RestPositions[rng.Next(s.VertexCount)], HeldTime = held[c],
                            RadiusMul = 0.5f + (float)rng.NextDouble(),
                        });
                    }
                    s.Step(dt, list);
                    float bound = s.BodyHeight * 2f;
                    for (int i = 0; i < s.VertexCount; i++)
                    {
                        var d = s.Positions[i] - s.RestPositions[i];
                        Check(Finite(d), $"trial {trial}: NaN/Inf");
                        Check(d.magnitude < bound, $"trial {trial}: 変位が発散 {d.magnitude:F3}");
                    }
                }
            }
        }

        static void SleepsAfterRecovery()
        {
            var s = NewSolver();
            int v = TopVertex(s, 0, 0);
            Press(s, v, 0.5f);
            Idle(s, 30f);
            Check(s.Asleep, $"30秒後も静止しない peak={s.PeakDepth:F4} speed={s.MaxSurfaceSpeed:F5}");
            Check(!s.Step(Dt, null), "静止中なのに描画更新している");
            // 再び押せば起きる
            var list = new List<SquishContact> { ContactAt(s, v, 0f) };
            Check(s.Step(Dt, list), "押しても起きない");
        }

        static void Performance()
        {
            var s = NewSolver();
            int a = TopVertex(s, -0.3f, 0f), b = TopVertex(s, 0.3f, 0.2f);
            var list = new List<SquishContact> { ContactAt(s, a, 0, 0), ContactAt(s, b, 0, 1) };
            for (int i = 0; i < 30; i++) s.Step(Dt, list); // JIT ウォームアップ
            var sw = Stopwatch.StartNew();
            int frames = 300;
            for (int i = 0; i < frames; i++) s.Step(Dt, list);
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            Console.WriteLine($"       {ms:F2} ms/frame (2指, {s.VertexCount} 頂点, 120Hz サブステップ x2) on this CPU");
            Check(ms < 6.0, $"遅すぎる: {ms:F2} ms/frame");
        }
    }
}
