using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZugSim.EditorTools
{
    /// <summary>Richtet das Projekt fuer Tablets ein: Shader-Einbindung, Player-Einstellungen, Startszene.</summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        static readonly string[] RequiredShaders =
        {
            "Standard", "Skybox/Procedural", "Sprites/Default", "Particles/Standard Unlit", "Unlit/Color",
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit",
        };

        [MenuItem("ZugSim/Projekt für Tablets einrichten")]
        public static void Configure()
        {
            IncludeShaders();
            ConfigurePlayer();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ZugSim] Projekt eingerichtet (Shader, Player-Einstellungen, Szene).");
        }

        /// <summary>Shader werden zur Laufzeit per Shader.Find geladen und muessen deshalb im Build enthalten sein.</summary>
        static void IncludeShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) { Debug.LogWarning("[ZugSim] GraphicsSettings nicht gefunden - Shader bitte manuell unter Always Included Shaders eintragen."); return; }
            var so = new SerializedObject(gs);
            var array = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (string name in RequiredShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < array.arraySize; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;
                array.InsertArrayElementAtIndex(array.arraySize);
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "ZugSim";
            PlayerSettings.productName = "Zug-Simulator";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = true;

            // Android
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.zugsim.trainsimulator");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // iOS / iPadOS
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.zugsim.trainsimulator");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.iOS.hideHomeButton = true;

            QualitySettings.vSyncCount = 0;
        }

        public static string EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return ScenePath;
        }
    }
}
