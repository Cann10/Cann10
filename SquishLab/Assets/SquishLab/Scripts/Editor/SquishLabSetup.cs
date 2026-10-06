using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SquishLab.EditorTools
{
    /// <summary>
    /// プロジェクトを初めて開いた時に、プロトタイプのシーン・素材プリセット・iOS 向け設定を用意する。
    /// メニュー「SQUISH LAB」からいつでもやり直せる。
    /// </summary>
    [InitializeOnLoad]
    public static class SquishLabSetup
    {
        public const string ScenePath = "Assets/SquishLab/Scenes/SquishLab.unity";
        const string PresetFolder = "Assets/SquishLab/Resources/SquishMaterials";
        const string SetupKey = "SquishLab.Setup.v1";

        static SquishLabSetup()
        {
            EditorApplication.delayCall += FirstTimeSetup;
        }

        static void FirstTimeSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string key = SetupKey + ":" + Application.dataPath;
            if (EditorPrefs.GetBool(key, false) && File.Exists(ScenePath)) return;

            CreateMaterialPresets();
            ApplyPlayerSettings();
            // 開いているシーンに未保存の変更がある時は、勝手にシーンを切り替えない
            if (!File.Exists(ScenePath) && !SceneManager.GetActiveScene().isDirty)
                RebuildScene();
            EditorPrefs.SetBool(key, true);
        }

        [MenuItem("SQUISH LAB/Open Prototype Scene", priority = 0)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { RebuildScene(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("SQUISH LAB/Rebuild Prototype Scene", priority = 1)]
        public static void RebuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Assets/SquishLab/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = SquishLabBootstrap.BuildRig();
            var body = root.GetComponentInChildren<SquishyBody>();
            if (body != null)
                body.Material = AssetDatabase.LoadAssetAtPath<SquishMaterial>(PresetFolder + "/SlowRiseBread.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes;
            bool listed = false;
            foreach (var s in scenes) if (s.path == ScenePath) listed = true;
            if (!listed)
            {
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            Debug.Log("[SQUISH LAB] Prototype scene created: " + ScenePath + "  — Play を押して食パンを触ってください。");
        }

        [MenuItem("SQUISH LAB/Create Material Presets", priority = 20)]
        public static void CreateMaterialPresets()
        {
            EnsureFolder(PresetFolder);
            foreach (var name in SquishMaterial.PresetNames)
            {
                string path = PresetFolder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<SquishMaterial>(path) != null) continue;
                var m = SquishMaterial.CreateDefault(name);
                AssetDatabase.CreateAsset(m, path);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("SQUISH LAB/Apply Recommended Player Settings", priority = 21)]
        public static void ApplyPlayerSettings()
        {
            // フォームの陰影が自然に見えるようリニア色空間
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.statusBarHidden = true;
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == "SquishLab")
                PlayerSettings.productName = "SQUISH LAB";
            if (PlayerSettings.companyName == "DefaultCompany") PlayerSettings.companyName = "Cann10";

            var id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            if (string.IsNullOrEmpty(id) || id.StartsWith("com.DefaultCompany") || id.StartsWith("com.Unity"))
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.cann10.squishlab");
            // Core Haptics は iOS 13+。余裕を見て 15.0 を下限にする
            PlayerSettings.iOS.targetOSVersionString = "15.0";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
