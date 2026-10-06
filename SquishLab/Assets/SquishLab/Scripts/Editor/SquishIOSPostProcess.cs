#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace SquishLab.EditorTools
{
    /// <summary>
    /// iOS ビルド後の Xcode プロジェクトに CoreHaptics.framework をリンクする
    /// （Plugins/iOS/SquishHaptics.mm が使う）。
    /// </summary>
    public static class SquishIOSPostProcess
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            // ネイティブプラグインは UnityFramework ターゲットに入る
            string framework = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(framework, "CoreHaptics.framework", true);
            proj.WriteToFile(projPath);
        }
    }
}
#endif
