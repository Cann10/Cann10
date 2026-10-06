using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// ソルバーの結果を FeedbackMapper に通し、音（SquishAudio）と触覚（SquishHaptics）へ流す。
    /// SquishyBody.LateUpdate の後に動かしたいので実行順を遅らせている。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SquishFeedback : MonoBehaviour
    {
        public SquishyBody Body;
        public SquishAudio Audio;

        public FeedbackFrame Last { get; private set; }

        readonly FeedbackMapper mapper = new FeedbackMapper((uint)System.Environment.TickCount | 1u);
        bool hapticRunning;

        void Start()
        {
            SquishHaptics.Prepare();
        }

        void LateUpdate()
        {
            if (Body == null || Body.Solver == null || Body.Material == null) return;
            var s = Body.Solver;
            var m = Body.Material;
            var f = mapper.Update(Time.deltaTime, Body.Contacts.Count, s.DentVolumeRate, s.PeakDepth,
                s.Params, m.SoundPitch, m.AirAmount, m.CrackleAmount, m.SquishAmount);
            Last = f;

            // ---- 触覚 ----
            if (f.HapticActive)
            {
                if (!hapticRunning) SquishHaptics.StartContinuous(f.HapticIntensity, f.HapticSharpness);
                else SquishHaptics.UpdateContinuous(f.HapticIntensity, f.HapticSharpness);
                hapticRunning = true;
            }
            else if (hapticRunning)
            {
                SquishHaptics.StopContinuous();
                hapticRunning = false;
            }
            if (f.TransientIntensity > 0f) SquishHaptics.Transient(f.TransientIntensity, f.TransientSharpness);

            // ---- 音 ----
            if (Audio != null)
            {
                Audio.SetContinuous(f.Air, f.AirBrightness, f.CracklePerSecond, f.CrackleGain, m.SoundPitch);
                if (f.SquishStrength > 0f) Audio.TriggerSquish(f.SquishStrength, f.SquishPitch);
                if (f.PuffStrength > 0f) Audio.TriggerPuff(f.PuffStrength);
            }
        }

        void OnDisable()
        {
            if (hapticRunning) SquishHaptics.StopContinuous();
            hapticRunning = false;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && hapticRunning)
            {
                SquishHaptics.StopContinuous();
                hapticRunning = false;
            }
        }
    }
}
