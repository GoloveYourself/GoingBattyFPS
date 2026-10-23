using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoingBatty.EditorTools
{
    /// <summary>Windows build, used both from the menu and by GitHub Actions.</summary>
    public static class Builder
    {
        [MenuItem("Going Batty/Build Windows")]
        public static void BuildWindowsMenu() => Build("Builds/Windows/GoingBatty.exe");

        // Called by GitHub Actions (game-ci) with -customBuildPath
        public static void BuildWindows()
        {
            string path = Arg("-customBuildPath") ?? "build/StandaloneWindows64/GoingBatty.exe";
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "GoingBatty.exe");
            Build(path);
        }

        static void Build(string exePath)
        {
            ProjectSetup.Run();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exePath)));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[GoingBatty] Build {report.summary.result} -> {exePath} (v{PlayerSettings.bundleVersion})");
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Build failed: " + report.summary.result);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
