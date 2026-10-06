using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SquishLab
{
    /// <summary>
    /// スクイーズ本体。メッシュを生成し、毎フレーム SquishSolver を進めて頂点を GPU へ送る。
    ///
    /// ・描画用メッシュ（動的）と Raycast 用メッシュ（静止形状のまま）を分けている。
    ///   変形中の形に当たり判定を合わせると、へこみの中で接触点が沈んでさらにへこむ…という
    ///   正のフィードバックで暴れるため、指の位置は常に静止形状上で決める。
    /// ・頂点カラー: r = 耳マスク（静的）, g = 圧縮量（動的）。UV0 = 静止位置（模様が泳がない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquishyBody : MonoBehaviour
    {
        public SquishMaterial Material;

        [Tooltip("メッシュの細かさ。低スペック端末では Coarse にする")]
        public bool HighResolution = true;

        public SquishSolver Solver { get; private set; }
        public SquishMeshData MeshData { get; private set; }
        public MeshCollider Collider { get; private set; }
        public Renderer Renderer => meshRenderer;

        /// <summary>このフレームの接触（SquishInput が書き込む）。</summary>
        public readonly List<SquishContact> Contacts = new List<SquishContact>(SquishSolver.MaxContacts);

        // ---- デバッグ表示用の計測値 ----
        public float LastSolveMs { get; private set; }
        public float LastUploadMs { get; private set; }
        /// <summary>直近の「離してから 95% 戻るまで」の実測秒。未計測なら負。</summary>
        public float LastRecoverySeconds { get; private set; } = -1f;
        /// <summary>直近の押し込みの最大へこみ（MaxDeformation に対する割合）。</summary>
        public float LastPeakDepth { get; private set; }
        public bool Recovering => releaseTime >= 0f;

        Mesh mesh;
        Mesh colliderMesh;
        MeshRenderer meshRenderer;
        Material renderMaterial;
        Color32[] colors;
        readonly Stopwatch watch = new Stopwatch();

        float pressPeak;
        float releaseTime = -1f;
        float releasePeak;
        bool wasPressed;

        static readonly int CrumbColorId = Shader.PropertyToID("_CrumbColor");
        static readonly int CrustColorId = Shader.PropertyToID("_CrustColor");
        static readonly int SssColorId = Shader.PropertyToID("_SubsurfaceColor");
        static readonly int PoreScaleId = Shader.PropertyToID("_PoreScale");
        static readonly int PoreStrengthId = Shader.PropertyToID("_PoreStrength");
        static readonly int WrinkleId = Shader.PropertyToID("_WrinkleStrength");
        static readonly int SheenId = Shader.PropertyToID("_Sheen");

        void Awake()
        {
            if (Material == null)
            {
                Material = Resources.Load<SquishMaterial>("SquishMaterials/SlowRiseBread");
                if (Material == null) Material = SquishMaterial.CreateDefault();
            }
            Build();
        }

        public void Build()
        {
            var settings = HighResolution ? BreadMeshBuilder.Settings.Default : BreadMeshBuilder.Settings.Coarse;
            MeshData = BreadMeshBuilder.Build(settings);
            Solver = new SquishSolver(MeshData, Material.Physics);

            int n = MeshData.Vertices.Length;
            colors = new Color32[n];
            for (int i = 0; i < n; i++)
                colors[i] = new Color32((byte)Mathf.RoundToInt(MeshData.CrustMask[i] * 255f), 0, 0, 255);

            if (mesh == null)
            {
                mesh = new Mesh { name = "SquishBread (dynamic)" };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = MeshData.Vertices;
            mesh.normals = MeshData.Normals;
            mesh.colors32 = colors;
            mesh.SetUVs(0, MeshData.Vertices); // 静止位置 = 模様の座標
            mesh.triangles = MeshData.Triangles;
            // 変形しても境界が足りるように余裕を持たせる（毎フレームの境界再計算を省くため）
            var b = new Bounds();
            b.SetMinMax(Vector3.zero, Vector3.zero);
            foreach (var v in MeshData.Vertices) b.Encapsulate(v);
            b.Expand(0.6f);
            mesh.bounds = b;

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            if (renderMaterial == null)
            {
                var shader = Shader.Find("SquishLab/Foam");
                if (shader == null)
                {
                    UnityEngine.Debug.LogError("[SQUISH LAB] Shader 'SquishLab/Foam' が見つかりません。Resources/Shaders を確認してください。");
                    shader = Shader.Find("Standard");
                }
                renderMaterial = new Material(shader) { name = "SquishFoam (runtime)" };
            }
            meshRenderer.sharedMaterial = renderMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; // 影は床シェーダーで描く
            meshRenderer.receiveShadows = false;
            ApplyLook();

            // Raycast 用: 静止形状の別メッシュ
            if (colliderMesh == null) colliderMesh = new Mesh { name = "SquishBread (collider)" };
            colliderMesh.Clear();
            colliderMesh.indexFormat = mesh.indexFormat;
            colliderMesh.vertices = MeshData.Vertices;
            colliderMesh.triangles = MeshData.Triangles;
            Collider = GetComponent<MeshCollider>();
            if (Collider == null) Collider = gameObject.AddComponent<MeshCollider>();
            Collider.sharedMesh = null;
            Collider.sharedMesh = colliderMesh;

            releaseTime = -1f;
        }

        /// <summary>SquishMaterial の見た目の値をシェーダーへ反映する。</summary>
        public void ApplyLook()
        {
            if (renderMaterial == null || Material == null) return;
            renderMaterial.SetColor(CrumbColorId, Material.CrumbColor);
            renderMaterial.SetColor(CrustColorId, Material.CrustColor);
            renderMaterial.SetColor(SssColorId, Material.SubsurfaceColor);
            renderMaterial.SetFloat(PoreScaleId, Material.PoreScale);
            renderMaterial.SetFloat(PoreStrengthId, Material.PoreStrength);
            renderMaterial.SetFloat(WrinkleId, Material.WrinkleStrength);
            renderMaterial.SetFloat(SheenId, Material.Sheen);
        }

        /// <summary>入力より後、描画より前に進めたいので LateUpdate で回す（入力は Update）。</summary>
        void LateUpdate()
        {
            if (Solver == null) return;
            // Inspector / デバッグパネルの変更を毎フレーム反映（構造体コピーなので安い）
            Solver.Params = Material.Physics.Sanitized();

            watch.Restart();
            bool changed = Solver.Step(Time.deltaTime, Contacts);
            LastSolveMs = (float)watch.Elapsed.TotalMilliseconds;

            if (changed)
            {
                watch.Restart();
                Upload();
                LastUploadMs = (float)watch.Elapsed.TotalMilliseconds;
            }
            else
            {
                LastUploadMs = 0f;
            }

            TrackRecovery();
        }

        void Upload()
        {
            var comp = Solver.Compression;
            for (int i = 0; i < colors.Length; i++)
                colors[i].g = (byte)(comp[i] * 255f);

            mesh.SetVertices(Solver.Positions, 0, Solver.VertexCount,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetColors(colors, 0, colors.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            // 法線は変形に合わせて毎フレーム再計算（溶接済みメッシュなので継ぎ目が出ない）
            mesh.RecalculateNormals(MeshUpdateFlags.DontRecalculateBounds);
        }

        void TrackRecovery()
        {
            bool pressed = Contacts.Count > 0;
            float peak = Solver.PeakDepth;
            if (pressed)
            {
                pressPeak = Mathf.Max(pressPeak, peak);
                releaseTime = -1f;
            }
            else if (wasPressed)
            {
                releaseTime = Time.time;
                releasePeak = Mathf.Max(pressPeak, peak);
                LastPeakDepth = releasePeak;
                pressPeak = 0f;
            }
            else if (releaseTime >= 0f && peak <= releasePeak * 0.05f)
            {
                LastRecoverySeconds = Time.time - releaseTime;
                releaseTime = -1f;
            }
            wasPressed = pressed;
        }

        /// <summary>素材を差し替える（形状はそのまま、物性・見た目・音が変わる）。</summary>
        public void SetMaterial(SquishMaterial material)
        {
            if (material == null) return;
            Material = material;
            if (Solver != null) Solver.Params = material.Physics.Sanitized();
            ApplyLook();
            ResetShape();
        }

        public void ResetShape()
        {
            Solver?.Reset();
            if (Solver != null) Upload();
            releaseTime = -1f;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (colliderMesh != null) Destroy(colliderMesh);
            if (renderMaterial != null) Destroy(renderMaterial);
        }

        /// <summary>
        /// 静止形状上の点の補間法線。MeshCollider の hit.normal は面法線（カクカク）なので、
        /// 三角形の重心座標で頂点法線を補間する。
        /// </summary>
        public Vector3 InterpolatedRestNormal(int triangleIndex, Vector3 barycentric)
        {
            var t = MeshData.Triangles;
            int i = triangleIndex * 3;
            if (triangleIndex < 0 || i + 2 >= t.Length) return Vector3.up;
            var n = MeshData.Normals;
            var r = n[t[i]] * barycentric.x + n[t[i + 1]] * barycentric.y + n[t[i + 2]] * barycentric.z;
            return r.sqrMagnitude > 1e-8f ? r.normalized : Vector3.up;
        }
    }
}
