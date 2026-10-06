using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace SquishLab
{
    /// <summary>
    /// 指（マウス）→ スクイーズ表面の接触点。
    /// 画面座標 → Ray → 静止形状の MeshCollider に Raycast → ローカル座標の SquishContact。
    ///
    /// エディタでは: 左クリック = 1本指で押す, Alt(Option)+左クリック = 中心対称の2本指（つまみ確認用）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SquishInput : MonoBehaviour
    {
        public SquishyBody Body;
        public Camera Camera;

        /// <summary>デバッグパネルなど、スクイーズを触らせたくない画面領域の判定。</summary>
        public static System.Func<Vector2, bool> IsScreenPointBlocked;

        struct Pointer
        {
            public int Id;
            public Vector2 Position;
            public float Radius; // ピクセル。0 = 不明
        }

        sealed class Finger
        {
            public Vector3 Anchor;
            public float StartTime;
            public SquishContact Contact;
        }

        readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        readonly HashSet<int> ignored = new HashSet<int>();
        readonly List<Pointer> pointers = new List<Pointer>(8);
        readonly List<int> scratchIds = new List<int>(8);
        readonly List<Finger> ordered = new List<Finger>(8);

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            EnhancedTouchSupport.Enable();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            Input.multiTouchEnabled = true;
#endif
        }

        void Update()
        {
            if (Body == null || Body.Collider == null) return;
            if (Camera == null) Camera = Camera.main;
            if (Camera == null) return;

            ReadPointers(pointers);

            // 離された指を消す
            scratchIds.Clear();
            foreach (var id in fingers.Keys) scratchIds.Add(id);
            foreach (var id in scratchIds)
                if (!Contains(pointers, id)) fingers.Remove(id);
            scratchIds.Clear();
            foreach (var id in ignored) scratchIds.Add(id);
            foreach (var id in scratchIds)
                if (!Contains(pointers, id)) ignored.Remove(id);

            var tr = Body.transform;
            var p = Body.Material != null ? Body.Material.Physics : SquishParams.SlowRiseBread;

            foreach (var ptr in pointers)
            {
                if (ignored.Contains(ptr.Id)) continue;
                bool tracked = fingers.TryGetValue(ptr.Id, out var finger);
                if (!tracked && IsScreenPointBlocked != null && IsScreenPointBlocked(ptr.Position))
                {
                    ignored.Add(ptr.Id);
                    continue;
                }

                var ray = Camera.ScreenPointToRay(ptr.Position);
                if (!Body.Collider.Raycast(ray, out var hit, 1000f))
                {
                    // 指がスクイーズの外へ滑った → 離したのと同じ扱い
                    if (tracked) fingers.Remove(ptr.Id);
                    continue;
                }

                var local = tr.InverseTransformPoint(hit.point);
                var normal = Body.InterpolatedRestNormal(hit.triangleIndex, hit.barycentricCoordinate);
                float radiusMul = RadiusMultiplier(ptr.Radius);

                if (!tracked)
                {
                    finger = new Finger { Anchor = local, StartTime = Time.time };
                    fingers.Add(ptr.Id, finger);
                }

                // 伸びの限界を超えたら掴んでいた位置が指の方へ滑る（摩擦スリップ）
                float maxGrab = p.MaxStretch * p.FingerRadius * p.RadiusScale * radiusMul;
                var g = local - finger.Anchor;
                float gl = g.magnitude;
                if (gl > maxGrab && gl > 1e-6f) finger.Anchor = local - g / gl * maxGrab;

                finger.Contact = new SquishContact
                {
                    FingerId = ptr.Id,
                    Point = local,
                    Normal = normal,
                    Anchor = finger.Anchor,
                    HeldTime = Time.time - finger.StartTime,
                    RadiusMul = radiusMul,
                };
            }

            // 先に触った指から順に（つまみ判定は先頭2本を使う）
            ordered.Clear();
            foreach (var f in fingers.Values) ordered.Add(f);
            ordered.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            Body.Contacts.Clear();
            for (int i = 0; i < ordered.Count && i < SquishSolver.MaxContacts; i++)
                Body.Contacts.Add(ordered[i].Contact);
        }

        static bool Contains(List<Pointer> list, int id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return true;
            return false;
        }

        /// <summary>指の接触面が大きい（腹で押した）ほどへこみを広く。iOS は touch.radius を返す。</summary>
        static float RadiusMultiplier(float radiusPixels)
        {
            if (radiusPixels <= 0f) return 1f;
            float dpi = Screen.dpi > 0f ? Screen.dpi : 326f;
            float inches = radiusPixels / dpi;
            return Mathf.Clamp(inches / 0.14f, 0.85f, 1.35f);
        }

        void ReadPointers(List<Pointer> list)
        {
            list.Clear();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            foreach (var t in ETouch.activeTouches)
            {
                var ph = t.phase;
                if (ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                list.Add(new Pointer { Id = t.touchId, Position = t.screenPosition, Radius = Mathf.Max(t.radius.x, t.radius.y) });
            }
            if (list.Count == 0 && Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                var pos = Mouse.current.position.ReadValue();
                bool alt = Keyboard.current != null && Keyboard.current.altKey.isPressed;
                AddMouse(list, pos, alt);
            }
#else
            int n = Input.touchCount;
            for (int i = 0; i < n; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                list.Add(new Pointer { Id = t.fingerId, Position = t.position, Radius = t.radius });
            }
            if (n == 0 && Input.GetMouseButton(0))
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                AddMouse(list, Input.mousePosition, alt);
            }
#endif
        }

        void AddMouse(List<Pointer> list, Vector2 pos, bool mirrored)
        {
            list.Add(new Pointer { Id = -1, Position = pos });
            if (!mirrored) return;
            // スクイーズ中心の画面位置を軸に点対称の2本目
            var center = Camera.WorldToScreenPoint(Body.Collider.bounds.center);
            var mirror = new Vector2(2f * center.x - pos.x, 2f * center.y - pos.y);
            list.Add(new Pointer { Id = -2, Position = mirror });
        }
    }
}
