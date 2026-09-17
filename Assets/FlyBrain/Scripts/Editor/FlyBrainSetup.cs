using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlyBrain.EditorTools
{
    /// <summary>Creates the demo scene on first import and provides menu items / batch entry points.</summary>
    [InitializeOnLoad]
    public static class FlyBrainSetup
    {
        public const string ScenePath = "Assets/Scenes/FlyBrainDemo.unity";
        const string SessionKey = "FlyBrain.SetupChecked";

        static FlyBrainSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += AutoSetup;
        }

        static void AutoSetup()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(SessionKey, true);
            if (File.Exists(ScenePath)) return;

            CreateScene();
            var active = SceneManager.GetActiveScene();
            bool untouched = !active.isDirty && (string.IsNullOrEmpty(active.path) || active.path.EndsWith("SampleScene.unity"));
            if (untouched) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[FlyBrain] Демо-сцена создана: " + ScenePath + ". Нажмите Play.");
        }

        [MenuItem("FlyBrain/Открыть демо-сцену", priority = 0)]
        public static void OpenDemo()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("FlyBrain/Пересоздать демо-сцену", priority = 1)]
        public static void RecreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            CreateScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("FlyBrain/Собрать Windows-версию", priority = 20)]
        public static void BuildMenu()
        {
            string path = EditorUtility.SaveFilePanel("FlyBrain build", "Builds", "FlyBrain", "exe");
            if (!string.IsNullOrEmpty(path)) Build(path);
        }

        public static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var previous = SceneManager.GetActiveScene();
            Scene scene;
            bool additive = previous.IsValid() && !string.IsNullOrEmpty(previous.path);
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var app = new GameObject("FlyBrainApp");
            SceneManager.MoveGameObjectToScene(app, scene);
            app.AddComponent<FlyBrainApp>();
            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null) RenderSettings.skybox = sky;

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (additive)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
        }

        public static BuildReport Build(string exePath)
        {
            if (!File.Exists(ScenePath)) CreateScene();
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[FlyBrain] build {report.summary.result}: {exePath} ({report.summary.totalSize / 1e6:F0} MB, {report.summary.totalErrors} errors)");
            return report;
        }

        // batch entry points: Unity.exe -batchmode -quit -projectPath ... -executeMethod FlyBrain.EditorTools.FlyBrainSetup.BatchBuild -flybrainBuildPath <exe>
        public static void BatchCreateScene()
        {
            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            CreateScene();
        }

        public static void BatchBuild()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-flybrainBuildPath");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Builds/FlyBrain.exe");
            BatchCreateScene();
            var report = Build(path);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
