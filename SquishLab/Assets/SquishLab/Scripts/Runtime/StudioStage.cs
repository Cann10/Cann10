using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 商品写真風の撮影ブース: 望遠気味のカメラ、白〜薄いグレーの床、柔らかい接地影。
    /// 画面の縦横比が変わってもスクイーズが横幅いっぱい（FillWidth）に収まるようにカメラ距離を合わせる。
    /// </summary>
    [ExecuteAlways]
    public sealed class StudioStage : MonoBehaviour
    {
        public Camera Camera;
        public SquishyBody Body;

        [Header("Camera")]
        [Range(10f, 60f)] public float FieldOfView = 26f;
        [Tooltip("見下ろす角度（度）")]
        [Range(20f, 85f)] public float Elevation = 56f;
        [Tooltip("画面の横幅に対してスクイーズが占める割合")]
        [Range(0.4f, 1.1f)] public float FillWidth = 0.97f;
        [Tooltip("スクイーズを画面のどの高さに置くか（0 = 中央, 正 = 上）")]
        [Range(-0.3f, 0.3f)] public float VerticalOffset = 0.06f;

        [Header("Light")]
        public Vector3 KeyDirection = new Vector3(-0.45f, 0.82f, -0.38f);

        Renderer floorRenderer;
        Material floorMaterial;
        float lastAspect;
        bool appliedWithMesh;

        static readonly int ShadowCenterId = Shader.PropertyToID("_ShadowCenter");
        static readonly int ShadowHalfSizeId = Shader.PropertyToID("_ShadowHalfSize");
        static readonly int ShadowOffsetId = Shader.PropertyToID("_ShadowOffset");
        static readonly int CenterId = Shader.PropertyToID("_Center");
        static readonly int KeyDirId = Shader.PropertyToID("_KeyDir");

        void OnEnable()
        {
            EnsureFloor();
            Apply();
        }

        void LateUpdate()
        {
            if (Camera == null) return;
            bool meshReady = Body != null && Body.MeshData != null;
            if (!Mathf.Approximately(Camera.aspect, lastAspect) || (meshReady && !appliedWithMesh)) Apply();
        }

        void EnsureFloor()
        {
            if (floorRenderer != null) return;
            var existing = transform.Find("Floor");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Floor";
                go.transform.SetParent(transform, false);
                var col = go.GetComponent<Collider>();
                if (col != null) DestroySafe(col);
            }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Quad は -Z 向き → 上向きに寝かせる
            go.transform.localScale = new Vector3(60f, 60f, 1f);
            floorRenderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("SquishLab/Floor");
            if (shader != null)
            {
                floorMaterial = new Material(shader) { name = "SquishFloor (runtime)", hideFlags = HideFlags.DontSave };
                floorRenderer.sharedMaterial = floorMaterial;
            }
            floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            floorRenderer.receiveShadows = false;
        }

        /// <summary>カメラ位置・影の形・ライト方向を反映する。</summary>
        public void Apply()
        {
            if (Camera == null) Camera = Camera.main;
            if (Camera == null) return;
            lastAspect = Camera.aspect;

            // スクイーズの底面の形（影）と大きさ
            Bounds b = new Bounds(Vector3.zero, new Vector3(2.2f, 0.8f, 2.3f));
            Vector2 footMin = new Vector2(-1f, -1f), footMax = new Vector2(1f, 1f);
            appliedWithMesh = Body != null && Body.MeshData != null;
            if (appliedWithMesh)
            {
                var verts = Body.MeshData.Vertices;
                b = new Bounds(verts[0], Vector3.zero);
                footMin = new Vector2(float.MaxValue, float.MaxValue);
                footMax = new Vector2(float.MinValue, float.MinValue);
                foreach (var v in verts)
                {
                    b.Encapsulate(v);
                    if (v.y < 0.25f)
                    {
                        footMin = Vector2.Min(footMin, new Vector2(v.x, v.z));
                        footMax = Vector2.Max(footMax, new Vector2(v.x, v.z));
                    }
                }
                var t = Body.transform;
                b.center = t.TransformPoint(b.center);
                b.extents = Vector3.Scale(b.extents, t.lossyScale);
                footMin += new Vector2(t.position.x, t.position.z);
                footMax += new Vector2(t.position.x, t.position.z);
            }

            // ---- カメラ: 横幅に合わせて距離を決める（縦長の iPhone でも大きく映す）----
            Camera.fieldOfView = FieldOfView;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.93f, 0.93f, 0.935f);
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 200f;
            float vHalf = FieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(0.1f, Camera.aspect));
            float halfWidth = Mathf.Max(b.extents.x, b.extents.z * 0.8f) / Mathf.Max(0.1f, FillWidth);
            float distH = halfWidth / Mathf.Tan(hHalf) + b.extents.y;
            // 横長画面では縦が先に詰まるので、縦方向にも収まる距離を確保
            float halfHeight = (b.extents.z * Mathf.Sin(Elevation * Mathf.Deg2Rad) + b.extents.y) / Mathf.Max(0.1f, FillWidth);
            float distV = halfHeight / Mathf.Tan(vHalf);
            float dist = Mathf.Max(distH, distV);

            var target = b.center;
            var rot = Quaternion.Euler(Elevation, 0f, 0f);
            Camera.transform.rotation = rot;
            Camera.transform.position = target - rot * Vector3.forward * dist;
            // 画面内での上下位置の調整（カメラを上下に平行移動）
            Camera.transform.position -= Camera.transform.up * (VerticalOffset * 2f * dist * Mathf.Tan(vHalf));

            // ---- 床と影 ----
            if (floorMaterial != null)
            {
                var center = (footMin + footMax) * 0.5f;
                var half = (footMax - footMin) * 0.5f;
                var key = KeyDirection.normalized;
                var offset = new Vector2(-key.x, -key.z) * (b.size.y * 0.35f);
                floorMaterial.SetVector(ShadowCenterId, center);
                floorMaterial.SetVector(ShadowHalfSizeId, half);
                floorMaterial.SetVector(ShadowOffsetId, offset);
                floorMaterial.SetVector(CenterId, center);
            }

            if (Body != null && Body.Renderer != null && Body.Renderer.sharedMaterial != null)
                Body.Renderer.sharedMaterial.SetVector(KeyDirId, KeyDirection.normalized);
        }

        static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDestroy()
        {
            if (floorMaterial != null) DestroySafe(floorMaterial);
        }
    }
}
