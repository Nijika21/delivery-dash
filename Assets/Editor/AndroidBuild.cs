using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DeliveryDash.Editor
{
    // Build APK Delivery Dash. Batch:
    // "Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod DeliveryDash.Editor.AndroidBuild.BuildAndroid"
    public static class AndroidBuild
    {
        private const string Scene = "Assets/Game/DeliveryDash.unity";
        private const string IconFolder = "Assets/Art/AppIcon/DD1";
        private const string Output = "Builds/Android/DeliveryDash.apk";

        [MenuItem("Delivery Dash/Build APK Android")]
        public static void BuildAndroid()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Hentikan Play sebelum build.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Pasang Android Build Support, SDK/NDK, dan OpenJDK melalui Unity Hub dahulu.");
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            PlayerSettings.productName = "Delivery Dash";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.deliverydash.dd1");
            ApplyAndroidIcons();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = false;
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.Android,
                options = BuildOptions.CompressWithLz4HC
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Build Android gagal: " + report.summary.result);
            Debug.Log("APK_ANDROID_READY: " + report.summary.outputPath);
        }

        // Ikon: truk asli env1 miring di hijau rumput. legacy/round 512 px, bg/fg 432 px (lapisan adaptive).
        private static void ApplyAndroidIcons()
        {
            Texture2D Load(string name)
            {
                string path = IconFolder + "/" + name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new FileNotFoundException("Ikon aplikasi tidak ditemukan: " + path);
                if (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            Texture2D legacy = Load("legacy"), round = Load("round"), background = Load("bg"), foreground = Load("fg");
            foreach (PlatformIconKind kind in new[] { AndroidPlatformIconKind.Adaptive, AndroidPlatformIconKind.Round, AndroidPlatformIconKind.Legacy })
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (PlatformIcon icon in icons)
                {
                    if (kind == AndroidPlatformIconKind.Adaptive) icon.SetTextures(background, foreground);
                    else icon.SetTexture(kind == AndroidPlatformIconKind.Round ? round : legacy);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { legacy }, IconKind.Any);
        }
    }
}
