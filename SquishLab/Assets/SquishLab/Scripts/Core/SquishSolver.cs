using System;
using System.Collections.Generic;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 疑似ソフトボディ（2層の粘弾性モデル）。
    ///
    /// 各頂点は静止位置からの変位を2層で持つ:
    ///  1. 記憶層 mem  … スローライズフォームの「へこみの記憶」。押すと速く沈み、離すと
    ///     RecoverySeconds かけて指数的に戻る（粘性支配で反発しない）。隣接頂点へ拡散して
    ///     へこみの縁がなめらかに馴染む。
    ///  2. 表面層 disp … 記憶層を目標にばね・ダンパで追従する、実際に描画される変位。
    ///     押した瞬間のぷるっとした遅れや、戻りの最後のわずかな揺れはここから出る。
    ///
    /// さらに本体全体の小さな揺れ（wobble）と、へこんだ体積に比例する側面のふくらみを
    /// 表示用に足す。どれも頂点ごとの局所計算なので O(頂点数)、モバイルでも軽い。
    /// </summary>
    public sealed class SquishSolver
    {
        public const float MaxSubstep = 1f / 120f;
        public const int MaxContacts = 4;

        // へこみの形（指半径に対する比）
        const float DishRadius = 2.0f;   // 浅い皿の半径
        const float DishWeight = 0.28f;  // 皿の深さの割合
        const float BulgeInner = 1.6f;   // 盛り上がりの開始
        const float BulgeOuter = 2.9f;   // 盛り上がりの終了

        // ---- 静止形状 ----
        public readonly int VertexCount;
        public readonly Vector3[] RestPositions;
        public readonly Vector3[] RestNormals;
        readonly float[] vertexArea;
        readonly float[] heightFactor;   // 0 = 底面, 1 = 天面
        readonly float[] sideFactor;     // 側面ほど 1
        readonly int[] nbStart, nbList;
        public readonly float BodyHeight;

        // ---- 状態 ----
        readonly Vector3[] mem;
        readonly Vector3[] memScratch;
        readonly Vector3[] disp;
        readonly Vector3[] vel;
        readonly float[] lastPressTime;
        readonly float[] influence;      // このステップで指の影響下にあるか（0..1）

        /// <summary>描画用の最終位置（静止位置 + 変位 + 全体揺れ + 側面ふくらみ）。</summary>
        public readonly Vector3[] Positions;
        /// <summary>描画用の圧縮量 0..1（シェーダーの陰影・シワに使う）。</summary>
        public readonly float[] Compression;

        Vector3 wobble, wobbleVel;
        float time;
        float prevDentVolume;
        int contactCountPrev;

        public SquishParams Params;

        // ---- 出力（音・ハプティクス・デバッグ用） ----
        /// <summary>へこみの体積を「指1本で最大まで押した体積」で正規化した値。</summary>
        public float DentVolume { get; private set; }
        /// <summary>DentVolume の時間変化 [1/s]。正 = 押し込み中, 負 = 復元中。</summary>
        public float DentVolumeRate { get; private set; }
        /// <summary>最も深い頂点のへこみ / 最大へこみ量。</summary>
        public float PeakDepth { get; private set; }
        /// <summary>表面層の最大速度（ワールド単位/s）。</summary>
        public float MaxSurfaceSpeed { get; private set; }
        /// <summary>何も変化していない（描画更新を省略してよい）。</summary>
        public bool Asleep { get; private set; }

        public float MaxDepthWorld => Params.MaxDeformation * BodyHeight;

        public SquishSolver(SquishMeshData mesh, SquishParams p)
        {
            VertexCount = mesh.Vertices.Length;
            RestPositions = (Vector3[])mesh.Vertices.Clone();
            RestNormals = (Vector3[])mesh.Normals.Clone();
            vertexArea = SquishMeshUtil.ComputeVertexAreas(RestPositions, mesh.Triangles);
            SquishMeshUtil.BuildNeighbors(VertexCount, mesh.Triangles, out nbStart, out nbList);
            BodyHeight = Mathf.Max(1e-3f, mesh.Height);

            heightFactor = new float[VertexCount];
            sideFactor = new float[VertexCount];
            for (int i = 0; i < VertexCount; i++)
            {
                heightFactor[i] = Mathf.Clamp01(RestPositions[i].y / BodyHeight);
                var n = RestNormals[i];
                sideFactor[i] = Mathf.Clamp01(1f - Mathf.Abs(n.y) * 1.2f);
            }

            mem = new Vector3[VertexCount];
            memScratch = new Vector3[VertexCount];
            disp = new Vector3[VertexCount];
            vel = new Vector3[VertexCount];
            lastPressTime = new float[VertexCount];
            influence = new float[VertexCount];
            Positions = (Vector3[])RestPositions.Clone();
            Compression = new float[VertexCount];
            for (int i = 0; i < VertexCount; i++) lastPressTime[i] = -1000f;

            Params = p.Sanitized();
        }

        public void Reset()
        {
            Array.Clear(mem, 0, VertexCount);
            Array.Clear(disp, 0, VertexCount);
            Array.Clear(vel, 0, VertexCount);
            Array.Clear(Compression, 0, VertexCount);
            Array.Copy(RestPositions, Positions, VertexCount);
            wobble = wobbleVel = Vector3.zero;
            DentVolume = DentVolumeRate = PeakDepth = 0f;
            prevDentVolume = 0f;
            Asleep = false;
        }

        /// <summary>
        /// dt 秒進める。contacts はこのフレームで押している指（最大 MaxContacts）。
        /// 戻り値: 描画データ（Positions / Compression）を更新したか。
        /// </summary>
        public bool Step(float dt, IReadOnlyList<SquishContact> contacts)
        {
            if (dt <= 0f) return false;
            dt = Mathf.Min(dt, 0.1f); // 一時停止明けなどの巨大 dt で暴れないように
            int count = contacts == null ? 0 : Mathf.Min(contacts.Count, MaxContacts);

            if (count == 0 && Asleep)
            {
                time += dt;
                return false;
            }

            if (count > contactCountPrev) KickWobble(contacts, count, contactCountPrev);
            contactCountPrev = count;

            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep));
            float h = dt / steps;
            for (int s = 0; s < steps; s++) Substep(h, contacts, count);

            ComposeOutput(dt);

            if (count == 0 && PeakDepth < 0.002f && MaxSurfaceSpeed < 0.0005f &&
                wobble.sqrMagnitude < 1e-8f && wobbleVel.sqrMagnitude < 1e-7f)
            {
                // 完全に静止: 残差を消して以後の計算を止める
                Reset();
                Asleep = true;
            }
            else
            {
                Asleep = false;
            }
            return true;
        }

        // ------------------------------------------------------------------

        struct ContactCache
        {
            public Vector3 Point, Normal, Grab, GrabCenter;
            public float Radius, InvRadius, Depth, GrabRadius, DishScale;
        }

        readonly ContactCache[] cache = new ContactCache[MaxContacts];

        void Substep(float h, IReadOnlyList<SquishContact> contacts, int count)
        {
            time += h;
            var p = Params;
            float maxDepth = MaxDepthWorld;

            // --- つまみ（2本指）: アンカー間の距離が縮んだ分だけ間の素材を持ち上げる ---
            float pinch = 0f;
            Vector3 pinchCenter = Vector3.zero, pinchNormal = Vector3.up;
            float pinchRadius = 1f;
            if (count >= 2)
            {
                var c0 = contacts[0];
                var c1 = contacts[1];
                float dAnchor = Vector3.Distance(c0.Anchor, c1.Anchor);
                float dNow = Vector3.Distance(c0.Point, c1.Point);
                if (dAnchor > 1e-4f)
                    pinch = Mathf.Clamp01((dAnchor - dNow) / dAnchor * 2.2f);
                pinchCenter = (c0.Point + c1.Point) * 0.5f;
                pinchNormal = (c0.Normal + c1.Normal).normalized;
                if (pinchNormal.sqrMagnitude < 0.5f) pinchNormal = c0.Normal;
                pinchRadius = dNow * 0.5f + p.FingerRadius * 0.9f;
            }

            for (int c = 0; c < count; c++)
            {
                var ct = contacts[c];
                float radius = p.FingerRadius * p.RadiusScale * Mathf.Clamp(ct.RadiusMul, 0.6f, 1.6f);
                float hold = 1f - Mathf.Exp(-p.HoldSinkRate * Mathf.Max(0f, ct.HeldTime));
                float depthFrac = p.InitialDepthFraction + (1f - p.InitialDepthFraction) * hold;
                // つまんでいる時は「押す」より「掴む」なので押し込みを弱める
                float depth = maxDepth * depthFrac * (1f - 0.85f * pinch);

                var grab = ct.Point - ct.Anchor;
                grab -= ct.Normal * Vector3.Dot(grab, ct.Normal);
                float maxGrab = p.MaxStretch * radius;
                if (grab.magnitude > maxGrab) grab = grab.normalized * maxGrab;

                cache[c] = new ContactCache
                {
                    Point = ct.Point,
                    Normal = ct.Normal,
                    Radius = radius,
                    InvRadius = 1f / radius,
                    Depth = depth,
                    Grab = grab,
                    GrabCenter = ct.Point - grab,
                    GrabRadius = radius * 1.4f,
                    // つまんでいる間は周囲を引き込む皿を消す（間の素材は持ち上がる）
                    DishScale = 1f - pinch,
                };
            }

            float pinchLift = pinch * p.Stretchiness * BodyHeight * 0.45f;
            float sinkK = 1f - Mathf.Exp(-p.SinkRate * h);
            float recRate = p.RecoveryRate;
            float stickDelay = p.StickDelay;
            float bulge = p.Bulge;
            float maxT = maxDepth * 1.15f;
            float maxT2 = maxT * maxT;

            // --- 1. 指が作る目標変位 → 記憶層 ---
            for (int i = 0; i < VertexCount; i++)
            {
                if (count == 0)
                {
                    // 指が無い時は戻るだけ。変位ゼロの頂点（大半）は何もしなくてよい
                    influence[i] = 0f;
                    var m0 = mem[i];
                    if (m0.x == 0f && m0.y == 0f && m0.z == 0f) continue;
                }
                var rp = RestPositions[i];
                var rn = RestNormals[i];
                float tx = 0f, ty = 0f, tz = 0f;
                float infl = 0f;

                for (int c = 0; c < count; c++)
                {
                    ref var cc = ref cache[c];
                    float facing = Vector3.Dot(rn, cc.Normal);
                    if (facing < -0.3f) continue; // 裏側の面には効かせない
                    float face = Mathf.Clamp01((facing + 0.3f) / 0.6f);

                    float dx = rp.x - cc.Point.x, dy = rp.y - cc.Point.y, dz = rp.z - cc.Point.z;
                    float r2 = (dx * dx + dy * dy + dz * dz) * cc.InvRadius * cc.InvRadius;
                    if (r2 < BulgeOuter * BulgeOuter)
                    {
                        // 2段のカーネル: 指先の丸いお椀 + 周囲ごと引き込まれる浅い皿。
                        // (1-x²)² は中心ほど強く縁で滑らかにゼロ。皿があることで壁が切り立たず、
                        // 「穴」ではなくフォームが周りごと沈む見た目になる。
                        float k = 0f;
                        if (r2 < 1f)
                        {
                            float q = 1f - r2;
                            k = q * q * (1f - DishWeight);
                        }
                        float rd2 = r2 * (1f / (DishRadius * DishRadius));
                        if (rd2 < 1f)
                        {
                            float q = 1f - rd2;
                            k += q * q * DishWeight * cc.DishScale;
                        }
                        if (k > 0f)
                        {
                            k *= face;
                            // 押し込む方向: 接触法線と自分の法線の中間（縁でも自然に潰れる）
                            float nx_ = -(cc.Normal.x * 0.65f + rn.x * 0.35f);
                            float ny_ = -(cc.Normal.y * 0.65f + rn.y * 0.35f);
                            float nz_ = -(cc.Normal.z * 0.65f + rn.z * 0.35f);
                            float inv = 1f / Mathf.Max(1e-5f, Mathf.Sqrt(nx_ * nx_ + ny_ * ny_ + nz_ * nz_));
                            float a = cc.Depth * k * inv;
                            tx += nx_ * a; ty += ny_ * a; tz += nz_ * a;
                            infl = Mathf.Max(infl, k);
                        }
                        float x = Mathf.Sqrt(r2);
                        if (x > BulgeInner && bulge > 0f)
                        {
                            // へこみの外側のわずかな盛り上がり
                            float t = (x - BulgeInner) / (BulgeOuter - BulgeInner);
                            float b = Mathf.Sin(t * Mathf.PI);
                            b *= b * bulge * cc.Depth * face;
                            tx += rn.x * b; ty += rn.y * b; tz += rn.z * b;
                        }
                    }

                    // 横方向の引っ張り（掴んだ素材が指と一緒に動く）
                    if (cc.Grab.x != 0f || cc.Grab.y != 0f || cc.Grab.z != 0f)
                    {
                        float gx = rp.x - cc.GrabCenter.x, gy = rp.y - cc.GrabCenter.y, gz = rp.z - cc.GrabCenter.z;
                        float g2 = (gx * gx + gy * gy + gz * gz) / (cc.GrabRadius * cc.GrabRadius);
                        if (g2 < 1f)
                        {
                            float q = 1f - g2;
                            float k = q * q * face;
                            tx += cc.Grab.x * k; ty += cc.Grab.y * k; tz += cc.Grab.z * k;
                            infl = Mathf.Max(infl, k * 0.5f);
                        }
                    }
                }

                if (pinchLift > 0f)
                {
                    float facing = Vector3.Dot(rn, pinchNormal);
                    if (facing > -0.3f)
                    {
                        var d = rp - pinchCenter;
                        float x2 = d.sqrMagnitude / (pinchRadius * pinchRadius);
                        if (x2 < 1f)
                        {
                            float q = 1f - x2;
                            float k = q * q * Mathf.Clamp01((facing + 0.3f) / 0.6f);
                            float a = pinchLift * k;
                            tx += rn.x * a; ty += rn.y * a; tz += rn.z * a;
                            infl = Mathf.Max(infl, k);
                        }
                    }
                }

                float t2 = tx * tx + ty * ty + tz * tz;
                if (t2 > maxT2)
                {
                    float s = maxT / Mathf.Sqrt(t2);
                    tx *= s; ty *= s; tz *= s;
                }
                influence[i] = infl;

                ref var m = ref mem[i];
                // 法線方向（へこみ・持ち上がり）と接線方向（引っ張り）を別々に判定する。
                // 目標が「今より先」にある成分は指が動かしているので速く追従、
                // 目標がゼロや今より手前の成分はフォーム自身の力で戻るのでスローライズ。
                // （まとめて判定すると、なぞった時の横方向の引っ張りでへこみの記憶が消えてしまう）
                float tn = tx * rn.x + ty * rn.y + tz * rn.z;
                float mn = m.x * rn.x + m.y * rn.y + m.z * rn.z;
                float ttx = tx - rn.x * tn, tty = ty - rn.y * tn, ttz = tz - rn.z * tn;
                float mtx = m.x - rn.x * mn, mty = m.y - rn.y * mn, mtz = m.z - rn.z * mn;

                // 向きが反転した目標は、今より大きい時だけ「指が動かしている」とみなす
                // （つまみの持ち上げは追従、へこみの外周の小さな盛り上がりでは記憶を消さない）
                bool normalAhead = tn * mn >= 0f ? tn * (tn - mn) > 1e-9f : tn * tn > mn * mn;
                float tDotM = ttx * mtx + tty * mty + ttz * mtz;
                float tt2 = ttx * ttx + tty * tty + ttz * ttz;
                float mt2 = mtx * mtx + mty * mty + mtz * mtz;
                bool tangentAhead = tDotM >= 0f ? tt2 - tDotM > 1e-9f : tt2 > mt2;
                if (normalAhead) lastPressTime[i] = time;

                float slow;
                {
                    float since = time - lastPressTime[i];
                    float ramp = stickDelay <= 0f ? 1f : Smooth01(since / stickDelay);
                    slow = 1f - Mathf.Exp(-recRate * ramp * h);
                    // 指の下にまだいる間は戻らない（離した時から粘着の遅れが始まる）
                    if (!normalAhead && infl > 0f)
                        lastPressTime[i] = Mathf.Max(lastPressTime[i], time - stickDelay * (1f - infl));
                }
                float fn = normalAhead ? sinkK : slow;
                float ft = tangentAhead ? sinkK : slow;

                mn += (tn - mn) * fn;
                mtx += (ttx - mtx) * ft;
                mty += (tty - mty) * ft;
                mtz += (ttz - mtz) * ft;
                m.x = mtx + rn.x * mn;
                m.y = mty + rn.y * mn;
                m.z = mtz + rn.z * mn;
            }

            // --- 2. 記憶層の拡散（へこみの縁がフォームらしく馴染む） ---
            float kappa = 1f - Mathf.Pow(1f - p.DiffusionPerStep, h * 60f);
            if (kappa > 1e-4f)
            {
                for (int i = 0; i < VertexCount; i++)
                {
                    var m = mem[i];
                    if (m.x == 0f && m.y == 0f && m.z == 0f)
                    {
                        // 周囲もゼロなら計算不要（大半の頂点はここで抜ける）
                        bool any = false;
                        for (int e = nbStart[i]; e < nbStart[i + 1]; e++)
                        {
                            var nm = mem[nbList[e]];
                            if (nm.x != 0f || nm.y != 0f || nm.z != 0f) { any = true; break; }
                        }
                        if (!any) { memScratch[i] = m; continue; }
                    }
                    float ax = 0f, ay = 0f, az = 0f;
                    int s0 = nbStart[i], s1 = nbStart[i + 1];
                    for (int e = s0; e < s1; e++)
                    {
                        var nm = mem[nbList[e]];
                        ax += nm.x; ay += nm.y; az += nm.z;
                    }
                    float invN = 1f / Mathf.Max(1, s1 - s0);
                    // 指の真下は拡散させない（押している形は指が決める）
                    float kk = kappa * (1f - influence[i]);
                    memScratch[i] = new Vector3(
                        m.x + (ax * invN - m.x) * kk,
                        m.y + (ay * invN - m.y) * kk,
                        m.z + (az * invN - m.z) * kk);
                }
                Array.Copy(memScratch, mem, VertexCount);
            }

            // --- 3. 表面層: 記憶層へばね・ダンパで追従（半陰的オイラー） ---
            float k_ = p.SurfaceStiffness;
            float c_ = 2f * p.DampingRatio * Mathf.Sqrt(k_);
            for (int i = 0; i < VertexCount; i++)
            {
                var m = mem[i];
                ref var d = ref disp[i];
                ref var v = ref vel[i];
                v.x += (k_ * (m.x - d.x) - c_ * v.x) * h;
                v.y += (k_ * (m.y - d.y) - c_ * v.y) * h;
                v.z += (k_ * (m.z - d.z) - c_ * v.z) * h;
                d.x += v.x * h; d.y += v.y * h; d.z += v.z * h;
            }

            // --- 4. 本体全体の揺れ ---
            float wk = Mathf.Lerp(90f, 320f, p.Elasticity);
            float wc = 2f * Mathf.Lerp(0.12f, 0.55f, p.Damping) * Mathf.Sqrt(wk);
            wobbleVel += (-wk * wobble - wc * wobbleVel) * h;
            wobble += wobbleVel * h;
        }

        void KickWobble(IReadOnlyList<SquishContact> contacts, int count, int prevCount)
        {
            // 新しく触れた指ごとに、押した方向へ小さなインパルス
            for (int c = prevCount; c < count; c++)
            {
                var n = contacts[c].Normal;
                wobbleVel += -n * (0.55f * Params.Wobble * (0.6f + 0.4f * Params.Softness));
            }
        }

        void ComposeOutput(float dt)
        {
            float maxDepth = MaxDepthWorld;
            float invMax = 1f / Mathf.Max(1e-5f, maxDepth);
            float vol = 0f;
            float peak = 0f;
            float maxV2 = 0f;
            for (int i = 0; i < VertexCount; i++)
            {
                var d = disp[i];
                var n = RestNormals[i];
                float inward = -(d.x * n.x + d.y * n.y + d.z * n.z);
                if (inward > 0f)
                {
                    vol += inward * vertexArea[i];
                    if (inward > peak) peak = inward;
                }
                var v = vel[i];
                float v2 = v.x * v.x + v.y * v.y + v.z * v.z;
                if (v2 > maxV2) maxV2 = v2;
            }

            // カーネルを円板で積分した体積: (1-x²)² → πR²/3, 皿は半径2倍なので 4倍
            float refVol = Mathf.PI / 3f * ((1f - DishWeight) + DishWeight * DishRadius * DishRadius) * Params.FingerRadius * Params.FingerRadius *
                           Params.RadiusScale * Params.RadiusScale * maxDepth;
            float dent = vol / Mathf.Max(1e-6f, refVol);

            // 側面のふくらみ: へこんだ分だけ横に逃げる（全体を縮めるのではなく側面だけ）
            float side = Mathf.Min(dent, 2f) * Params.Bulge * 0.06f * BodyHeight;
            var wob = wobble;

            for (int i = 0; i < VertexCount; i++)
            {
                var d = disp[i];
                var n = RestNormals[i];
                float hf = heightFactor[i];
                float sb = side * sideFactor[i] * (1f - Mathf.Abs(hf * 2f - 1f) * 0.6f);
                float wf = hf * hf;
                Positions[i] = new Vector3(
                    RestPositions[i].x + d.x + n.x * sb + wob.x * wf,
                    RestPositions[i].y + d.y + n.y * sb + wob.y * wf,
                    RestPositions[i].z + d.z + n.z * sb + wob.z * wf);
                float inward = -(d.x * n.x + d.y * n.y + d.z * n.z);
                Compression[i] = Mathf.Clamp01(inward * invMax);
            }

            float rate = (dent - prevDentVolume) / Mathf.Max(1e-4f, dt);
            prevDentVolume = dent;
            // 音・触覚用に軽く平滑化
            DentVolumeRate = Mathf.Lerp(DentVolumeRate, rate, 1f - Mathf.Exp(-dt * 30f));
            DentVolume = dent;
            PeakDepth = peak * invMax;
            MaxSurfaceSpeed = Mathf.Sqrt(maxV2);
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>頂点 i の記憶層の変位（テスト・デバッグ用）。</summary>
        public Vector3 MemoryOf(int i) => mem[i];
        /// <summary>頂点 i の表面層の変位（テスト・デバッグ用）。</summary>
        public Vector3 DisplacementOf(int i) => disp[i];
    }
}
