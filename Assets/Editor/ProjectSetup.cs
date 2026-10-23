using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GoingBatty.EditorTools
{
    /// <summary>
    /// Runs automatically whenever the project opens (and before every CI build). It applies
    /// player settings, the version number from the VERSION file, the Unity Cloud project link,
    /// and makes sure the single (empty) scene exists — the game builds itself at runtime.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () => { if (!Application.isBatchMode) Run(); };
        }

        [MenuItem("Going Batty/Run Project Setup")]
        public static void Run()
        {
            if (PlayerSettings.productName != "Going Batty") PlayerSettings.productName = "Going Batty";
            if (PlayerSettings.companyName != "Going Batty") PlayerSettings.companyName = "Going Batty";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;

            string version = ReadVersion();
            if (!string.IsNullOrEmpty(version) && PlayerSettings.bundleVersion != version)
            {
                PlayerSettings.bundleVersion = version;
                Debug.Log("[GoingBatty] Version set to " + version);
            }

            ApplyCloudProject();
            EnsureScene();
        }

        public static string ReadVersion()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "VERSION");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }

        static void ApplyCloudProject()
        {
            string id = ServicesConfig.CloudProjectId;
            if (string.IsNullOrWhiteSpace(id) || CloudProjectSettings.projectId == id) return;
            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(ps);
            void Set(string prop, string value)
            {
                var p = so.FindProperty(prop);
                if (p != null && !string.IsNullOrEmpty(value)) p.stringValue = value;
            }
            Set("cloudProjectId", id);
            Set("organizationId", ServicesConfig.OrganizationId);
            Set("projectName", ServicesConfig.ProjectName);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[GoingBatty] Linked Unity Cloud project " + id);
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath || !scenes[0].enabled)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
