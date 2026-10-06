using System.Text;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 開発用の調整パネル（IMGUI）。触りながら物性をリアルタイムに変えられる。
    ///
    /// ・右下の「Tune」で開閉（開発ビルドとエディタのみ表示）
    /// ・パネルは画面下半分。上半分のスクイーズはパネルを開いたまま触れる
    /// ・「Copy」で現在の値を JSON でクリップボードへ（実機で調整 → エディタのアセットに貼る用）
    /// ・エディタでは SquishMaterial アセットを直接書き換えるので、プレイ終了後も値が残る
    /// </summary>
    public sealed class SquishDebugPanel : MonoBehaviour
    {
        public SquishyBody Body;
        public SquishAudio Audio;
        public SquishFeedback Feedback;

        [Tooltip("リリースビルドでも表示する")]
        public bool ShowInRelease;

        public bool Open;

        float fps = 60f;
        Vector2 scroll;
        float scale = 1f;
        Rect panelRectGui;
        Rect buttonRectGui;
        GUIStyle label, header, button, small;
        Texture2D bg;
        string copied;
        float copiedTime;

        bool Visible => ShowInRelease || Debug.isDebugBuild || Application.isEditor;

        void OnEnable()
        {
            SquishInput.IsScreenPointBlocked = BlocksScreenPoint;
        }

        void OnDisable()
        {
            if (SquishInput.IsScreenPointBlocked == BlocksScreenPoint) SquishInput.IsScreenPointBlocked = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) fps = Mathf.Lerp(fps, 1f / dt, 0.05f);
        }

        /// <summary>画面座標（左下原点）がパネル上か。</summary>
        bool BlocksScreenPoint(Vector2 screen)
        {
            if (!Visible) return false;
            var gui = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (buttonRectGui.Contains(gui)) return true;
            return Open && panelRectGui.Contains(gui);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.82f));
            bg.Apply();
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.15f, 0.15f, 0.17f) } };
            small = new GUIStyle(label) { fontSize = 11, normal = { textColor = new Color(0.35f, 0.35f, 0.38f) } };
            header = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }

        void OnGUI()
        {
            if (!Visible || Body == null || Body.Material == null) return;
            EnsureStyles();

            // 端末の大きさに関わらず指で操作できるサイズに（基準幅 390pt）
            scale = Mathf.Max(1f, Screen.width / 390f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;

            buttonRectGui = new Rect(w - 52f, h - 52f - (Open ? h * 0.5f : 0f), 44f, 44f);
            if (GUI.Button(buttonRectGui, Open ? "Close" : "Tune", button)) Open = !Open;

            if (!Open)
            {
                GUI.Label(new Rect(8, 8, 220, 20), $"{fps:F0} fps", small);
                return;
            }

            panelRectGui = new Rect(0f, h * 0.5f, w, h * 0.5f);
            GUI.DrawTexture(panelRectGui, bg);
            GUILayout.BeginArea(new Rect(panelRectGui.x + 10f, panelRectGui.y + 6f, panelRectGui.width - 20f, panelRectGui.height - 10f));
            scroll = GUILayout.BeginScrollView(scroll);

            DrawStats();
            DrawPresets();

            var m = Body.Material;
            var p = m.Physics;
            GUILayout.Label("Physics", header);
            p.Softness = Slider("Softness", p.Softness, 0f, 1f);
            p.RecoverySpeed = Slider("RecoverySpeed", p.RecoverySpeed, 0f, 1f, $"≈ {p.RecoverySeconds:F1} s");
            p.Elasticity = Slider("Elasticity", p.Elasticity, 0f, 1f);
            p.Damping = Slider("Damping", p.Damping, 0f, 1f);
            p.MaxDeformation = Slider("MaxDeformation", p.MaxDeformation, 0.05f, 0.95f, $"{p.MaxDeformation * 100f:F0}%");
            p.StickyAmount = Slider("StickyAmount", p.StickyAmount, 0f, 1f);
            p.Stretchiness = Slider("Stretchiness", p.Stretchiness, 0f, 1f);
            GUILayout.Label("Shape response", header);
            p.FingerRadius = Slider("FingerRadius", p.FingerRadius, 0.1f, 0.8f);
            p.Bulge = Slider("Bulge", p.Bulge, 0f, 0.5f);
            p.Coupling = Slider("Coupling", p.Coupling, 0f, 1f);
            p.Wobble = Slider("Wobble", p.Wobble, 0f, 1f);
            m.Physics = p;

            GUILayout.Label("Look", header);
            m.PoreStrength = Slider("Pores", m.PoreStrength, 0f, 1f);
            m.PoreScale = Slider("PoreScale", m.PoreScale, 5f, 80f);
            m.WrinkleStrength = Slider("Wrinkles", m.WrinkleStrength, 0f, 1f);
            m.Sheen = Slider("Sheen", m.Sheen, 0f, 1f);
            if (GUI.changed) Body.ApplyLook();

            GUILayout.Label("Sound / Haptics", header);
            m.SoundPitch = Slider("Pitch", m.SoundPitch, 0.5f, 2f);
            m.AirAmount = Slider("Air (ぷしゅ)", m.AirAmount, 0f, 1.5f);
            m.CrackleAmount = Slider("Crackle", m.CrackleAmount, 0f, 1.5f);
            m.SquishAmount = Slider("Squish (むにゅ)", m.SquishAmount, 0f, 1.5f);
            if (Audio != null)
            {
                Audio.MasterVolume = Slider("Volume", Audio.MasterVolume, 0f, 2f);
                Audio.Muted = GUILayout.Toggle(Audio.Muted, " Mute", label);
            }
            SquishHaptics.Enabled = GUILayout.Toggle(SquishHaptics.Enabled, " Haptics", label);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset shape", button, GUILayout.Height(34))) Body.ResetShape();
            if (GUILayout.Button(Application.targetFrameRate >= 120 ? "120fps → 60" : "60fps → 120", button, GUILayout.Height(34)))
                Application.targetFrameRate = Application.targetFrameRate >= 120 ? 60 : 120;
            if (GUILayout.Button("Copy JSON", button, GUILayout.Height(34)))
            {
                GUIUtility.systemCopyBuffer = JsonUtility.ToJson(m, true);
                copied = "copied!";
                copiedTime = Time.unscaledTime;
            }
            GUILayout.EndHorizontal();
            if (copied != null && Time.unscaledTime - copiedTime < 1.5f) GUILayout.Label(copied, small);

            GUILayout.Space(30);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStats()
        {
            var s = Body.Solver;
            var sb = new StringBuilder();
            sb.Append($"{fps:F0} fps   solve {Body.LastSolveMs:F2} ms   upload {Body.LastUploadMs:F2} ms   {s.VertexCount} verts");
            sb.Append(s.Asleep ? "   (sleep)" : "");
            GUILayout.Label(sb.ToString(), small);

            string rec = Body.Recovering ? "measuring…" :
                Body.LastRecoverySeconds >= 0f ? $"{Body.LastRecoverySeconds:F2} s" : "—";
            GUILayout.Label($"recovery (95%): {rec}   last peak: {Body.LastPeakDepth * 100f:F0}%   depth now: {s.PeakDepth * 100f:F0}%", small);

            if (Feedback != null)
            {
                var f = Feedback.Last;
                bool tick = Time.unscaledTime - SquishHaptics.LastTransientTime < 0.08f;
                GUILayout.Label($"haptic {SquishHaptics.LastIntensity:F2}/{SquishHaptics.LastSharpness:F2} {(tick ? "●" : "○")}   " +
                                $"air {f.Air:F2}   crackle {f.CracklePerSecond:F0}/s   dent rate {s.DentVolumeRate:F2}", small);
            }
        }

        void DrawPresets()
        {
            GUILayout.BeginHorizontal();
            foreach (var name in SquishMaterial.PresetNames)
            {
                if (GUILayout.Button(name, button, GUILayout.Height(30)))
                    Body.SetMaterial(LoadPreset(name));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Material: {Body.Material.DisplayName}", small);
        }

        readonly System.Collections.Generic.Dictionary<string, SquishMaterial> presets =
            new System.Collections.Generic.Dictionary<string, SquishMaterial>();

        /// <summary>Resources/SquishMaterials/{name} があればそれを、無ければ既定値のインスタンスを使う。</summary>
        SquishMaterial LoadPreset(string name)
        {
            if (presets.TryGetValue(name, out var m) && m != null) return m;
            m = Resources.Load<SquishMaterial>("SquishMaterials/" + name);
            if (m == null) m = SquishMaterial.CreateDefault(name);
            presets[name] = m;
            return m;
        }

        float Slider(string name, float value, float min, float max, string extra = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(118));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.Label(extra ?? v.ToString("F2"), small, GUILayout.Width(58));
            GUILayout.EndHorizontal();
            return v;
        }

        void OnDestroy()
        {
            if (bg != null) Destroy(bg);
        }
    }
}
