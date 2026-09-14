using System;
using System.Collections.Generic;
using System.IO;
using ForgottenIsle.Game.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Editor
{
    /// <summary>
    /// One-click project setup: creates the scene assets the build needs, registers them in Build
    /// Settings in the order <see cref="SceneKeys.All"/> declares, and verifies the localization
    /// resource. Idempotent — running it twice changes nothing the second time.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS: Unity scene assets are editor-serialized YAML full of GUID and fileID
    /// cross-references. Hand-authoring one outside the editor risks producing a corrupt asset,
    /// which is strictly worse than a missing one. So the scenes are created here, through
    /// <see cref="EditorSceneManager"/> — Unity's own API doing its own serialization — rather
    /// than checked in as text somebody wrote by hand.
    /// <para>
    /// WHY THE SCENES ARE EMPTY: every object the game needs is created at runtime
    /// (<c>AppBootstrap</c> builds the host and services, <c>UiInstaller</c> builds the UI document,
    /// <c>ZoneFurnisher</c> builds the player, camera, ground and light). An empty scene holds no
    /// serialized component references, so it cannot drift from the code, cannot be malformed, and
    /// can be regenerated at any time without losing authored work. When real zone art arrives it
    /// simply displaces what the furnisher would otherwise create.
    /// </para>
    /// </remarks>
    public static class VardholmProjectSetup
    {
        private const string MenuRoot = "Vardholm/";
        private const string SceneFolder = "Assets/Scenes";
        private const string LocalizationResource = "Assets/Resources/Localization/en.csv";
        private const string LocalizationAuthoring = "Assets/Localization/en.csv";

        /// <summary>Creates anything missing and repairs Build Settings. Safe to re-run.</summary>
        [MenuItem(MenuRoot + "Setup Project", priority = 0)]
        public static void SetupProject()
        {
            var report = new SetupReport();

            try
            {
                EnsureSceneFolder(report);
                EnsureScenes(report);
                EnsureBuildSettings(report);
                VerifyLocalization(report);
            }
            catch (Exception exception)
            {
                report.Fail("Setup aborted by an exception: " + exception.Message);
                Debug.LogException(exception);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Print("VARDHOLM PROJECT SETUP");

            if (report.HasFailures)
            {
                EditorUtility.DisplayDialog(
                    "Vardholm setup incomplete",
                    "Setup ran but reported failures. See the Console for the full report.",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Vardholm setup complete",
                report.ChangeCount == 0
                    ? "Everything was already in place. Open Assets/Scenes/Bootstrap.unity and press Play."
                    : report.ChangeCount + " item(s) created or repaired.\n\n" +
                      "Open Assets/Scenes/Bootstrap.unity and press Play.",
                "OK");
        }

        /// <summary>Reports on project setup without changing anything.</summary>
        [MenuItem(MenuRoot + "Validate Project", priority = 1)]
        public static void ValidateProject()
        {
            var report = new SetupReport { DryRun = true };

            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                if (File.Exists(ScenePath(key)))
                {
                    report.Pass("scene exists: " + key);
                }
                else
                {
                    report.Fail("scene MISSING: " + key + " — run Vardholm > Setup Project");
                }
            }

            CheckBuildSettingsOrder(report);
            VerifyLocalization(report);
            report.Print("VARDHOLM PROJECT VALIDATION");
        }

        /// <summary>Opens the boot scene, so "where do I press Play" has an answer in the menu.</summary>
        [MenuItem(MenuRoot + "Open Bootstrap Scene", priority = 20)]
        public static void OpenBootstrapScene()
        {
            var path = ScenePath(SceneKeys.Bootstrap);
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Bootstrap scene missing",
                    "Run Vardholm > Setup Project first.",
                    "OK");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
        }

        /// <summary>True when every scene asset exists. Used by the first-run check.</summary>
        internal static bool AllScenesExist()
        {
            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                if (!File.Exists(ScenePath(SceneKeys.All[i])))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string ScenePath(string sceneKey)
        {
            return SceneFolder + "/" + sceneKey + ".unity";
        }

        private static void EnsureSceneFolder(SetupReport report)
        {
            if (AssetDatabase.IsValidFolder(SceneFolder))
            {
                report.Pass("scene folder exists");
                return;
            }

            AssetDatabase.CreateFolder("Assets", "Scenes");
            report.Change("created folder " + SceneFolder);
        }

        private static void EnsureScenes(SetupReport report)
        {
            // The scene open when setup runs is saved and reopened afterwards, so a developer who had
            // work in progress does not silently lose it to a NewScene call.
            var reopen = SceneManager.GetActiveScene().path;

            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                var path = ScenePath(key);

                if (File.Exists(path))
                {
                    report.Pass("scene exists: " + key);
                    continue;
                }

                // DefaultGameObjectTypes would seed a camera and a light into every scene. The runtime
                // furnisher owns both, and a stray scene camera would fight it, so the scenes are empty.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, path))
                {
                    report.Fail("could not save scene: " + path);
                    continue;
                }

                report.Change("created scene " + key);
            }

            if (!string.IsNullOrEmpty(reopen) && File.Exists(reopen))
            {
                EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
            }
        }

        private static void EnsureBuildSettings(SetupReport report)
        {
            var desired = new List<EditorBuildSettingsScene>(SceneKeys.All.Count);
            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var path = ScenePath(SceneKeys.All[i]);
                if (!File.Exists(path))
                {
                    report.Fail("cannot register missing scene in Build Settings: " + path);
                    continue;
                }

                desired.Add(new EditorBuildSettingsScene(path, true));
            }

            if (BuildSettingsMatch(desired))
            {
                report.Pass("build settings already correct (" + desired.Count + " scenes)");
                return;
            }

            EditorBuildSettings.scenes = desired.ToArray();
            report.Change("wrote build settings: " + desired.Count + " scenes, Bootstrap at index 0");
        }

        private static bool BuildSettingsMatch(List<EditorBuildSettingsScene> desired)
        {
            var current = EditorBuildSettings.scenes;
            if (current == null || current.Length != desired.Count)
            {
                return false;
            }

            for (var i = 0; i < desired.Count; i++)
            {
                if (current[i].path != desired[i].path || !current[i].enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static void CheckBuildSettingsOrder(SetupReport report)
        {
            var current = EditorBuildSettings.scenes;
            if (current == null || current.Length == 0)
            {
                report.Fail("build settings are empty — run Vardholm > Setup Project");
                return;
            }

            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var expected = ScenePath(SceneKeys.All[i]);
                if (i >= current.Length)
                {
                    report.Fail("build settings missing entry " + i + ": " + expected);
                    continue;
                }

                if (current[i].path != expected)
                {
                    report.Fail("build settings index " + i + " is " + current[i].path + ", expected " + expected);
                    continue;
                }

                if (!current[i].enabled)
                {
                    report.Fail("build settings entry disabled: " + expected);
                    continue;
                }

                report.Pass("build index " + i + " = " + SceneKeys.All[i]);
            }
        }

        private static void VerifyLocalization(SetupReport report)
        {
            if (!File.Exists(LocalizationResource))
            {
                report.Fail("missing " + LocalizationResource + " — the UI would render #key# for every label");
                return;
            }

            report.Pass("localization resource present");

            if (!File.Exists(LocalizationAuthoring))
            {
                report.Warn("authoring copy " + LocalizationAuthoring + " is missing");
                return;
            }

            // The shipped copy is what Resources.Load reads; the authoring copy is what a translator
            // edits. A silent drift between them ships stale strings, so it is checked, not assumed.
            if (File.ReadAllText(LocalizationResource) != File.ReadAllText(LocalizationAuthoring))
            {
                report.Warn("localization copies differ — Assets/Localization/en.csv is not the shipped file");
                return;
            }

            report.Pass("localization copies are identical");
        }

        /// <summary>Collects PASS / WARN / FAIL lines and prints one readable block.</summary>
        private sealed class SetupReport
        {
            private readonly List<string> _pass = new List<string>();
            private readonly List<string> _warn = new List<string>();
            private readonly List<string> _fail = new List<string>();

            public bool DryRun { get; set; }

            public int ChangeCount { get; private set; }

            public bool HasFailures
            {
                get { return _fail.Count > 0; }
            }

            public void Pass(string message)
            {
                _pass.Add(message);
            }

            public void Warn(string message)
            {
                _warn.Add(message);
            }

            public void Fail(string message)
            {
                _fail.Add(message);
            }

            public void Change(string message)
            {
                ChangeCount++;
                _pass.Add(message);
            }

            public void Print(string title)
            {
                var builder = new System.Text.StringBuilder();
                builder.AppendLine(title);
                if (DryRun)
                {
                    builder.AppendLine("(read-only check — nothing was modified)");
                }

                Append(builder, "PASS", _pass);
                Append(builder, "WARN", _warn);
                Append(builder, "FAIL", _fail);

                var text = builder.ToString();
                if (_fail.Count > 0)
                {
                    Debug.LogError(text);
                }
                else if (_warn.Count > 0)
                {
                    Debug.LogWarning(text);
                }
                else
                {
                    Debug.Log(text);
                }
            }

            private static void Append(System.Text.StringBuilder builder, string label, List<string> lines)
            {
                if (lines.Count == 0)
                {
                    return;
                }

                builder.AppendLine();
                builder.AppendLine(label + ":");
                for (var i = 0; i < lines.Count; i++)
                {
                    builder.AppendLine("  - " + lines[i]);
                }
            }
        }
    }
}
