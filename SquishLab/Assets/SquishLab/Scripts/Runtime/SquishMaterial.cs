using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// スクイーズ素材 1 種類ぶんの設定（物性 + 見た目 + 音色）。
    /// Assets/SquishLab/Resources/SquishMaterials/ に置くとプリセットとして読み込まれる。
    /// プレイ中に Inspector やデバッグパネルで値を変えると即座に触感へ反映される。
    /// </summary>
    [CreateAssetMenu(menuName = "SQUISH LAB/Squish Material", fileName = "NewSquishMaterial")]
    public sealed class SquishMaterial : ScriptableObject
    {
        public string DisplayName = "Slow Rise Bread";

        public SquishParams Physics = SquishParams.SlowRiseBread;

        [Header("Look")]
        public Color CrumbColor = new Color(0.99f, 0.965f, 0.9f);
        public Color CrustColor = new Color(0.89f, 0.67f, 0.4f);
        [Tooltip("影側に入る暖かい色（フォームの透過感）")]
        public Color SubsurfaceColor = new Color(1f, 0.78f, 0.55f);
        [Range(5f, 80f)] public float PoreScale = 52f;
        [Range(0f, 1f)] public float PoreStrength = 0.45f;
        [Range(0f, 1f)] public float WrinkleStrength = 0.5f;
        [Range(0f, 1f)] public float Sheen = 0.35f;

        [Header("Sound")]
        [Tooltip("音の高さの基準（1 = 標準）。小さい・硬い素材ほど高く")]
        [Range(0.5f, 2f)] public float SoundPitch = 1f;
        [Tooltip("空気が抜ける音（ぷしゅ）の量")]
        [Range(0f, 1.5f)] public float AirAmount = 1f;
        [Tooltip("フォームの気泡がはじける細かい音の量")]
        [Range(0f, 1.5f)] public float CrackleAmount = 1f;
        [Tooltip("押した瞬間の低い「むにゅ」の量")]
        [Range(0f, 1.5f)] public float SquishAmount = 1f;

        public static SquishMaterial CreateDefault(string presetName = "SlowRiseBread")
        {
            var m = CreateInstance<SquishMaterial>();
            m.name = presetName;
            m.ApplyPreset(presetName);
            return m;
        }

        public static readonly string[] PresetNames = { "SlowRiseBread", "FastFoam", "Mochi" };

        /// <summary>見た目は食パンのまま、物性と音色だけ差し替える（触り比べ用）。</summary>
        public void ApplyPreset(string presetName)
        {
            switch (presetName)
            {
                case "FastFoam":
                    DisplayName = "Fast Foam";
                    Physics = SquishParams.FastFoam;
                    SoundPitch = 1.15f; AirAmount = 0.8f; CrackleAmount = 0.7f; SquishAmount = 0.8f;
                    break;
                case "Mochi":
                    DisplayName = "Mochi";
                    Physics = SquishParams.Mochi;
                    SoundPitch = 0.8f; AirAmount = 0.35f; CrackleAmount = 0.2f; SquishAmount = 1.4f;
                    break;
                default:
                    DisplayName = "Slow Rise Bread";
                    Physics = SquishParams.SlowRiseBread;
                    SoundPitch = 1f; AirAmount = 1f; CrackleAmount = 1f; SquishAmount = 1f;
                    break;
            }
        }
    }
}
