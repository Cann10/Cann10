using System.Runtime.InteropServices;
using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// iOS Core Haptics への薄いラッパー（Plugins/iOS/SquishHaptics.mm）。
    /// エディタ・他プラットフォームでは何もしない。
    /// </summary>
    public static class SquishHaptics
    {
        public static bool Enabled = true;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern bool SquishHaptics_IsSupported();
        [DllImport("__Internal")] static extern void SquishHaptics_Prepare();
        [DllImport("__Internal")] static extern void SquishHaptics_StartContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void SquishHaptics_UpdateContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void SquishHaptics_StopContinuous();
        [DllImport("__Internal")] static extern void SquishHaptics_Transient(float intensity, float sharpness);

        public static bool IsSupported => SquishHaptics_IsSupported();
        public static void Prepare() => SquishHaptics_Prepare();
        public static void StartContinuous(float i, float s) { DebugLast(i, s); if (Enabled) SquishHaptics_StartContinuous(i, s); }
        public static void UpdateContinuous(float i, float s) { DebugLast(i, s); if (Enabled) SquishHaptics_UpdateContinuous(i, s); else SquishHaptics_StopContinuous(); }
        public static void StopContinuous() { DebugLast(0f, 0f); SquishHaptics_StopContinuous(); }
        public static void Transient(float i, float s) { LastTransientTime = Time.unscaledTime; if (Enabled) SquishHaptics_Transient(i, s); }
#else
        public static bool IsSupported => false;
        public static void Prepare() { }
        public static void StartContinuous(float i, float s) { DebugLast(i, s); }
        public static void UpdateContinuous(float i, float s) { DebugLast(i, s); }
        public static void StopContinuous() { DebugLast(0f, 0f); }
        public static void Transient(float i, float s) { LastTransientTime = Time.unscaledTime; }
#endif

        static void DebugLast(float i, float s) { LastIntensity = i; LastSharpness = s; }

        // エディタでハプティクスの出方をデバッグパネルに可視化するための値
        public static float LastIntensity { get; private set; }
        public static float LastSharpness { get; private set; }
        public static float LastTransientTime { get; private set; } = -10f;
    }
}
