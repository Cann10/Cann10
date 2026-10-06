using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// シーンにスクイーズが無ければ、実行時にプロトタイプ一式を組み立てる。
    /// 空のシーンで Play を押すだけで食パンを触れる（シーンファイルを手で組む必要がない）。
    /// </summary>
    public static class SquishLabBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBuild()
        {
            ConfigureApp();
            if (Object.FindAnyObjectByType<SquishyBody>() != null) return;
            BuildRig();
        }

        static void ConfigureApp()
        {
            // iPhone: 60fps 固定（ProMotion 端末はデバッグパネルで 120 に切替可）
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if ENABLE_LEGACY_INPUT_MANAGER
            Input.multiTouchEnabled = true;
#endif
        }

        /// <summary>カメラ・床・スクイーズ・入力・音・触覚・デバッグパネルを作る。</summary>
        public static GameObject BuildRig()
        {
            var root = new GameObject("SQUISH LAB");

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            else if (cam.GetComponent<AudioListener>() == null && Object.FindAnyObjectByType<AudioListener>() == null)
            {
                cam.gameObject.AddComponent<AudioListener>();
            }

            // 既存のライトは不要（自前のスタジオ照明）。残っていても見た目には影響しない。

            var bodyGo = new GameObject("Bread Squishy");
            bodyGo.transform.SetParent(root.transform, false);
            var body = bodyGo.AddComponent<SquishyBody>();

            var stage = root.AddComponent<StudioStage>();
            stage.Camera = cam;
            stage.Body = body;
            stage.Apply();

            var input = root.AddComponent<SquishInput>();
            input.Body = body;
            input.Camera = cam;

            var audioGo = new GameObject("Squish Audio");
            audioGo.transform.SetParent(root.transform, false);
            audioGo.AddComponent<AudioSource>();
            var audio = audioGo.AddComponent<SquishAudio>();

            var feedback = root.AddComponent<SquishFeedback>();
            feedback.Body = body;
            feedback.Audio = audio;

            var panel = root.AddComponent<SquishDebugPanel>();
            panel.Body = body;
            panel.Audio = audio;
            panel.Feedback = feedback;

            return root;
        }
    }
}
