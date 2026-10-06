using System.Collections.Generic;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// エンジン非依存のメッシュデータ（テストからも使う）。
    /// </summary>
    public sealed class SquishMeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public int[] Triangles;
        /// <summary>0 = 白いクラム（パンの中身）, 1 = 耳（クラスト）。</summary>
        public float[] CrustMask;
        /// <summary>本体の高さ（底面 y=0 から天面まで, 中央付近）。</summary>
        public float Height;
        /// <summary>平均的な頂点間隔（ワールド単位）。</summary>
        public float Spacing;
    }

    /// <summary>
    /// 食パン1枚（厚切り・寝かせた状態）のスクイーズ形状を生成する。
    ///
    /// 立方体の格子の表面点だけを溶接済みで並べ、角丸ボックスへ射影し、
    /// その後 XZ 平面で食パンのシルエット（上部の山・肩の張り出し）に歪ませる。
    /// 格子ベースなので天面の頂点密度がほぼ均一になり、どこを押しても同じ品質でへこむ。
    /// 頂点は溶接済み（継ぎ目で分割しない）なので、法線・変形・拡散が継ぎ目で途切れない。
    /// </summary>
    public static class BreadMeshBuilder
    {
        public struct Settings
        {
            public float HalfWidth;      // X
            public float HalfLength;     // Z（奥が食パンの山側）
            public float HalfThickness;  // Y
            public Vector3 CornerRadius; // 各軸の角丸半径
            public int CellsX, CellsY, CellsZ;
            public float Puff;           // 天面のふっくら感
            public float Dome;           // 山の盛り上がり
            public float Shoulder;       // 肩の張り出し
            public float LobeNotch;      // 山の中央のわずかなくぼみ（2つ山）

            public static Settings Default => new Settings
            {
                HalfWidth = 1.0f,
                HalfLength = 0.98f,
                HalfThickness = 0.36f,
                CornerRadius = new Vector3(0.3f, 0.3f, 0.3f),
                CellsX = 50,
                CellsY = 16,
                CellsZ = 50,
                Puff = 0.2f,
                Dome = 0.2f,
                Shoulder = 0.09f,
                LobeNotch = 0.05f,
            };

            /// <summary>低スペック端末やテスト用の粗いメッシュ。</summary>
            public static Settings Coarse
            {
                get
                {
                    var s = Default;
                    s.CellsX = 24; s.CellsY = 8; s.CellsZ = 24;
                    return s;
                }
            }
        }

        public static SquishMeshData Build(Settings s)
        {
            int nx = Mathf.Max(2, s.CellsX), ny = Mathf.Max(2, s.CellsY), nz = Mathf.Max(2, s.CellsZ);
            int sx = nx + 1, sy = ny + 1, sz = nz + 1;
            var index = new int[sx * sy * sz];
            for (int i = 0; i < index.Length; i++) index[i] = -1;

            var verts = new List<Vector3>();
            var half = new Vector3(s.HalfWidth, s.HalfThickness, s.HalfLength);
            var r = new Vector3(
                Mathf.Min(s.CornerRadius.x, half.x * 0.95f),
                Mathf.Min(s.CornerRadius.y, half.y * 0.95f),
                Mathf.Min(s.CornerRadius.z, half.z * 0.95f));

            // 表面格子点だけに頂点を作る（溶接済み）
            for (int k = 0; k < sz; k++)
            for (int j = 0; j < sy; j++)
            for (int i = 0; i < sx; i++)
            {
                bool surface = i == 0 || i == nx || j == 0 || j == ny || k == 0 || k == nz;
                if (!surface) continue;
                var c = new Vector3(2f * i / nx - 1f, 2f * j / ny - 1f, 2f * k / nz - 1f);
                index[(k * sy + j) * sx + i] = verts.Count;
                verts.Add(Shape(c, half, r, s));
            }

            var tris = new List<int>(nx * nz * 12 + ny * nz * 12 + nx * ny * 12);
            // 6面: (固定軸, 側) ごとに四角形を張る
            for (int axis = 0; axis < 3; axis++)
            for (int side = 0; side < 2; side++)
            {
                int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                int[] n = { nx, ny, nz };
                int fixedVal = side == 0 ? 0 : n[axis];
                for (int u = 0; u < n[ua]; u++)
                for (int v = 0; v < n[va]; v++)
                {
                    int a = Idx(index, sx, sy, axis, ua, va, fixedVal, u, v);
                    int b = Idx(index, sx, sy, axis, ua, va, fixedVal, u + 1, v);
                    int c = Idx(index, sx, sy, axis, ua, va, fixedVal, u + 1, v + 1);
                    int d = Idx(index, sx, sy, axis, ua, va, fixedVal, u, v + 1);
                    // 立方体空間での外向きは +axis(side=1) / -axis(side=0)。
                    // (ua, va, axis) は右手系の巡回順なので u→v の向きの外積は +axis。
                    // Unity は Cross(b-a, c-a) が表向き法線なので side=1 は (a,b,c) の順。
                    // 対角線は面の中心から放射状にそろえ、角の歪みを左右対称にする。
                    bool flipDiag = ((u < n[ua] / 2) ^ (v < n[va] / 2));
                    if (side == 1)
                    {
                        if (flipDiag) { Tri(tris, a, b, d); Tri(tris, b, c, d); }
                        else { Tri(tris, a, b, c); Tri(tris, a, c, d); }
                    }
                    else
                    {
                        if (flipDiag) { Tri(tris, a, d, b); Tri(tris, b, d, c); }
                        else { Tri(tris, a, c, b); Tri(tris, a, d, c); }
                    }
                }
            }

            var data = new SquishMeshData
            {
                Vertices = verts.ToArray(),
                Triangles = tris.ToArray(),
            };
            data.Normals = SquishMeshUtil.ComputeNormals(data.Vertices, data.Triangles);

            // 底面を y=0 に置く
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in data.Vertices) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            for (int i = 0; i < data.Vertices.Length; i++) data.Vertices[i].y -= minY;
            data.Height = maxY - minY;

            data.CrustMask = new float[data.Vertices.Length];
            for (int i = 0; i < data.Vertices.Length; i++)
            {
                float ny_ = Mathf.Abs(data.Normals[i].y);
                data.CrustMask[i] = 1f - Smooth(0.42f, 0.78f, ny_);
            }

            data.Spacing = 2f * s.HalfWidth / nx;
            return data;
        }

        static int Idx(int[] index, int sx, int sy, int axis, int ua, int va, int fixedVal, int u, int v)
        {
            var c = new int[3];
            c[axis] = fixedVal; c[ua] = u; c[va] = v;
            return index[(c[2] * sy + c[1]) * sx + c[0]];
        }

        static void Tri(List<int> t, int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }

        /// <summary>立方体座標 c∈[-1,1]^3 → 食パン形状の点。</summary>
        static Vector3 Shape(Vector3 c, Vector3 half, Vector3 r, Settings s)
        {
            // 角丸ボックス（各軸で半径の異なる楕円丸め）
            var p = Vector3.Scale(c, half);
            var inner = new Vector3(
                Mathf.Clamp(p.x, -(half.x - r.x), half.x - r.x),
                Mathf.Clamp(p.y, -(half.y - r.y), half.y - r.y),
                Mathf.Clamp(p.z, -(half.z - r.z), half.z - r.z));
            var d = p - inner;
            var dn = new Vector3(d.x / r.x, d.y / r.y, d.z / r.z);
            float len = dn.magnitude;
            if (len > 1e-6f)
            {
                dn /= len;
                p = inner + Vector3.Scale(dn, r);
            }

            float u = p.x / half.x;   // -1..1 幅方向
            float w = p.z / half.z;   // -1(手前・底辺) .. 1(山)

            // 肩の張り出し: 上半分がわずかに幅広（食パンのきのこ型）
            float shoulder = Smooth(0.0f, 0.55f, w) * (1f - 0.6f * Smooth(0.75f, 1.05f, w));
            p.x *= 1f + s.Shoulder * shoulder;

            // 山: 奥側の中央が盛り上がり、角は丸く落ちる
            float dome = Smooth(0.1f, 1f, w) * (1f - u * u);
            p.z += s.Dome * half.z * dome * Smooth(-0.2f, 0.6f, w);
            // 山の中央をわずかにくぼませて2つ山っぽく
            p.z -= s.LobeNotch * half.z * Mathf.Exp(-(u / 0.22f) * (u / 0.22f)) * Smooth(0.6f, 1f, w);

            // 天面のふっくら感（底面は机に接するので平らのまま）
            if (p.y > 0f)
            {
                float pu = Mathf.Clamp01(1f - u * u);
                float pw = Mathf.Clamp01(1f - w * w);
                p.y *= 1f + s.Puff * pu * pw;
            }
            return p;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public static class SquishMeshUtil
    {
        /// <summary>面積重み付きの頂点法線。Unity と同じく Cross(b-a, c-a) を表向きとする。</summary>
        public static Vector3[] ComputeNormals(Vector3[] v, int[] t)
        {
            var n = new Vector3[v.Length];
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                var fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                n[a] += fn; n[b] += fn; n[c] += fn;
            }
            for (int i = 0; i < n.Length; i++)
            {
                float m = n[i].magnitude;
                n[i] = m > 1e-12f ? n[i] / m : Vector3.up;
            }
            return n;
        }

        /// <summary>頂点ごとの代表面積（隣接三角形の面積の1/3の合計）。</summary>
        public static float[] ComputeVertexAreas(Vector3[] v, int[] t)
        {
            var area = new float[v.Length];
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                float ar = Vector3.Cross(v[b] - v[a], v[c] - v[a]).magnitude * 0.5f / 3f;
                area[a] += ar; area[b] += ar; area[c] += ar;
            }
            return area;
        }

        /// <summary>辺でつながる隣接頂点を CSR 形式で返す。</summary>
        public static void BuildNeighbors(int vertexCount, int[] t, out int[] start, out int[] list)
        {
            var sets = new HashSet<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) sets[i] = new HashSet<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                sets[a].Add(b); sets[a].Add(c);
                sets[b].Add(a); sets[b].Add(c);
                sets[c].Add(a); sets[c].Add(b);
            }
            start = new int[vertexCount + 1];
            int total = 0;
            for (int i = 0; i < vertexCount; i++) { start[i] = total; total += sets[i].Count; }
            start[vertexCount] = total;
            list = new int[total];
            for (int i = 0; i < vertexCount; i++)
            {
                int o = start[i];
                foreach (var nb in sets[i]) list[o++] = nb;
            }
        }
    }
}
