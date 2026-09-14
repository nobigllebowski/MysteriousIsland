using UnityEditor;
using UnityEngine;

namespace ForgottenIsle.Editor
{
    /// <summary>
    /// On the first editor load after a fresh clone, notices that the scene assets do not exist yet
    /// and offers to run <see cref="VardholmProjectSetup.SetupProject"/>.
    /// </summary>
    /// <remarks>
    /// THIS USED TO ASK, AND ASKING FAILED. The original reasoning was that creating asset files on
    /// project open is a surprise, so a dialog offered the choice. Two things broke it in practice.
    /// A project that opens in Safe Mode never runs <c>[InitializeOnLoadMethod]</c> from its own
    /// assemblies at all, so through every compile-error round the dialog never appeared once. And a
    /// dialog dismissed with "Later" leaves a project whose NEW GAME button silently does nothing,
    /// because the zone it loads is not in Build Settings — which is exactly what happened.
    /// <para>
    /// Phase 1.5's whole goal was <c>clone → open → Play</c>. A prompt that can be missed or
    /// declined is not that. Setup now runs on its own and says what it did. It is safe to do
    /// unattended: it creates only files that do not exist, and never overwrites one that does.
    /// </para>
    /// <para>
    /// The delay call matters: <c>[InitializeOnLoadMethod]</c> runs while the asset database is
    /// still settling, and creating scenes in that window can deadlock the import. Deferring to the
    /// first editor update tick sidesteps it.
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

            if (Application.isPlaying)
            {
                return;
            }

            var scenesMissing = !VardholmProjectSetup.AllScenesExist();

            // Checked separately from the scenes, because a clone whose scenes were created before
            // this step existed has everything it needs EXCEPT the colour space -- and in Gamma the
            // island renders at 6.6% grey, which reads as a black screen with no error anywhere.
            var wrongColorSpace = PlayerSettings.colorSpace != ColorSpace.Linear;

            if (!scenesMissing && !wrongColorSpace)
            {
                return;
            }

            Debug.Log(
                "[Vardholm] project setup has something to do: " +
                (scenesMissing ? "the scene assets do not exist yet" : "the scenes are in place") +
                ", " +
                (wrongColorSpace ? "and the colour space is Gamma rather than Linear" : "colour space is Linear") +
                ". Running setup now. Nothing that already exists is overwritten. " +
                "Re-run any time from  Vardholm > Setup Project.");

            VardholmProjectSetup.SetupProject();
        }
    }
}
