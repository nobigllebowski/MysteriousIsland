using UnityEditor;
using UnityEngine;

namespace ForgottenIsle.Editor
{
    /// <summary>
    /// On the first editor load after a fresh clone, notices that the scene assets do not exist yet
    /// and offers to run <see cref="VardholmProjectSetup.SetupProject"/>.
    /// </summary>
    /// <remarks>
    /// WHY A PROMPT RATHER THAN SILENT AUTO-SETUP: creating and saving asset files behind
    /// someone's back on project open is the kind of surprise that makes a toolchain untrustworthy,
    /// and it would fight source control the moment two people opened the project at once. The
    /// prompt is offered once per editor session, only when setup is genuinely missing, and
    /// declining it costs nothing — the menu item is always there.
    /// <para>
    /// The delay call matters: <c>[InitializeOnLoadMethod]</c> runs while the asset database is
    /// still settling, and showing a modal dialog in that window can deadlock the import. Deferring
    /// to the first editor update tick sidesteps it.
    /// </para>
    /// </remarks>
    internal static class VardholmFirstRunCheck
    {
        private static bool _checkedThisSession;

        [InitializeOnLoadMethod]
        private static void Arm()
        {
            if (_checkedThisSession)
            {
                return;
            }

            _checkedThisSession = true;
            EditorApplication.delayCall += RunCheck;
        }

        private static void RunCheck()
        {
            EditorApplication.delayCall -= RunCheck;

            if (Application.isPlaying || VardholmProjectSetup.AllScenesExist())
            {
                return;
            }

            Debug.LogWarning(
                "VARDHOLM: project setup has not been run in this clone — the scene assets do not exist yet.\n" +
                "Run  Vardholm > Setup Project  (or click Set up now on the dialog).");

            var setUpNow = EditorUtility.DisplayDialog(
                "Vardholm — first-time setup",
                "This clone has no scene assets yet.\n\n" +
                "Setup creates the four scenes, writes Build Settings in the right order, and " +
                "verifies the localization resource. It does not overwrite anything that already exists.\n\n" +
                "You can also run it any time from the Vardholm menu.",
                "Set up now",
                "Later");

            if (setUpNow)
            {
                VardholmProjectSetup.SetupProject();
            }
        }
    }
}
