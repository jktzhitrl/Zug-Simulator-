using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZugSim.EditorTools
{
    /// <summary>
    /// Build-Einstiegspunkte fuer Menue und CI.
    ///   Unity -batchmode -quit -projectPath . -executeMethod ZugSim.EditorTools.BuildScript.BuildAndroid -buildOutput Builds/ZugSim.apk
    ///   Unity -batchmode -quit -projectPath . -executeMethod ZugSim.EditorTools.BuildScript.BuildIos -buildOutput Builds/iOS
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("ZugSim/Build/Android (APK)")]
        public static void BuildAndroid() { Build(BuildTarget.Android, "Builds/ZugSim.apk", false); }

        [MenuItem("ZugSim/Build/Android (AAB für Play Store)")]
        public static void BuildAndroidBundle() { Build(BuildTarget.Android, "Builds/ZugSim.aab", true); }

        [MenuItem("ZugSim/Build/iPadOS (Xcode-Projekt)")]
        public static void BuildIos() { Build(BuildTarget.iOS, "Builds/iOS", false); }

        static void Build(BuildTarget target, string defaultOutput, bool bundle)
        {
            ProjectSetup.Configure();
            string output = GetArg("-buildOutput") ?? defaultOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            EditorUserBuildSettings.buildAppBundle = bundle;
            if (target == BuildTarget.Android) EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log(string.Format("[ZugSim] Build {0}: {1} ({2:0.0} MB)", target, report.summary.result, report.summary.totalSize / 1048576.0));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
