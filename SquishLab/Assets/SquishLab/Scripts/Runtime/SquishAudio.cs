using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// SquishSynth を AudioSource に流す。OnAudioFilterRead はオーディオスレッドで呼ばれる。
    /// 録音素材を使いたい場合は PressClips に入れると、むにゅの代わりに
    /// ピッチ・音量をランダム化して再生する。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SquishAudio : MonoBehaviour
    {
        [Range(0f, 2f)] public float MasterVolume = 1f;
        public bool Muted;

        [Tooltip("任意: 押した瞬間に鳴らす録音素材。空ならプロシージャル合成")]
        public AudioClip[] PressClips;

        AudioSource source;
        SquishSynth synth;
        AudioClip silence;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            synth = new SquishSynth(sr, (uint)System.Environment.TickCount | 1u);
            // 無音のループを再生し続け、その出力を OnAudioFilterRead で丸ごと置き換える
            // （クリップ無しの AudioSource だとフィルターが呼ばれない環境があるため）
            silence = AudioClip.Create("SquishSilence", sr, 1, sr, false);
            source.clip = silence;
        }

        void OnEnable()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (!source.isPlaying) source.Play();
        }

        void Update()
        {
            if (synth != null) synth.Master = Muted ? 0f : MasterVolume;
        }

        public void SetContinuous(float air, float brightness, float cracklePerSecond, float crackle, float pitch)
            => synth?.SetContinuous(air, brightness, cracklePerSecond, crackle, pitch);

        public void TriggerSquish(float strength, float pitch)
        {
            if (Muted || synth == null) return;
            if (PressClips != null && PressClips.Length > 0)
            {
                var clip = PressClips[Random.Range(0, PressClips.Length)];
                if (clip != null)
                {
                    // ピッチはループ中の無音クリップにも効くが、無音なので影響しない
                    source.pitch = pitch * Random.Range(0.94f, 1.06f);
                    source.PlayOneShot(clip, Mathf.Clamp01(strength) * MasterVolume);
                    return;
                }
            }
            synth.TriggerSquish(strength, pitch);
        }

        public void TriggerPuff(float strength)
        {
            if (!Muted) synth?.TriggerPuff(strength);
        }

        void OnDestroy()
        {
            if (silence != null) Destroy(silence);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // data には無音ループ + PressClips のワンショットが入っている。そこへ合成音を足す
            synth?.Render(data, channels);
        }
    }
}
