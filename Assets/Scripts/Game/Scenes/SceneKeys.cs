// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Scenes/SceneNavigator.cs (SceneNames).
// Adapted for Vardholm: Nation's single "World" scene becomes one scene per zone, because ADR-0004 loads zones
// additively and keeps up to two resident; the list gained an All view and a zone/display-key lookup so nothing
// outside this file ever writes a scene name as a literal.

using System.Collections.Generic;

namespace ForgottenIsle.Game.Scenes
{
    /// <summary>
    /// Every scene name the game may ask for, and the only place any of them is spelled.
    /// </summary>
    /// <remarks>
    /// WHY constants rather than an enum: <c>SceneManager</c> takes a string, and an enum would only
    /// move the spelling into a conversion table that can drift from Build Settings just as easily.
    /// A constant plus the <see cref="All"/> list gives a test something to iterate and compare
    /// against the build scene list, which is the check that actually catches the mistake.
    /// </remarks>
    public static class SceneKeys
    {
        /// <summary>The boot scene: empty, holds nothing, exists so index 0 is cheap to load.</summary>
        public const string Bootstrap = "Bootstrap";

        /// <summary>The menu scene. Holds no zone geometry; the menu itself lives in the UI document.</summary>
        public const string MainMenu = "MainMenu";

        /// <summary>Act 1 opening zone — the wreck shelf.</summary>
        public const string ZoneRibcage = "ZoneRibcage";

        /// <summary>Act 1 second zone — the fern gully inland of the shelf.</summary>
        public const string ZoneFernmaw = "ZoneFernmaw";

        /// <summary>Localization key suffix appended to a zone id to name it on the save-slot card.</summary>
        private const string ZoneDisplayKeySuffix = ".name";

        /// <summary>Localization key prefix for zone display names.</summary>
        private const string ZoneDisplayKeyPrefix = "zone.";

        /// <summary>
        /// Scene-name prefix stripped before a key is built, so <c>ZoneRibcage</c> yields
        /// <c>zone.ribcage.name</c> rather than the stuttering <c>zone.zoneribcage.name</c>.
        /// </summary>
        private const string ScenePrefix = "Zone";

        private static readonly string[] AllScenes =
        {
            Bootstrap,
            MainMenu,
            ZoneRibcage,
            ZoneFernmaw
        };

        private static readonly string[] ZoneScenes =
        {
            ZoneRibcage,
            ZoneFernmaw
        };

        /// <summary>Every scene in the build, in build order.</summary>
        public static IReadOnlyList<string> All => AllScenes;

        /// <summary>Only the scenes that are zones — the ones <c>ZoneRegistry</c> is allowed to hold.</summary>
        public static IReadOnlyList<string> Zones => ZoneScenes;

        /// <summary>True when <paramref name="sceneKey"/> names a scene in this build.</summary>
        public static bool IsKnown(string sceneKey)
        {
            return IndexIn(AllScenes, sceneKey) >= 0;
        }

        /// <summary>
        /// True when <paramref name="sceneKey"/> names a zone.
        /// </summary>
        /// <remarks>
        /// Used by <c>TravelToZoneHandler</c> to reject a request to "travel" to the main menu before
        /// it reaches the loader, which would otherwise happily load the menu on top of a live run.
        /// </remarks>
        public static bool IsZone(string sceneKey)
        {
            return IndexIn(ZoneScenes, sceneKey) >= 0;
        }

        /// <summary>
        /// Returns the localization key that names <paramref name="sceneKey"/> to the player.
        /// </summary>
        /// <remarks>
        /// Derived rather than tabulated so adding a zone cannot add a zone with no name. The result
        /// is a key, never text: an unknown scene yields an empty string, and the caller stores that,
        /// which renders through <c>ILocalizedText</c> as a visible missing-key marker rather than as
        /// a blank slot card.
        /// </remarks>
        public static string ZoneDisplayKey(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                return string.Empty;
            }

            var bare = sceneKey.StartsWith(ScenePrefix, System.StringComparison.Ordinal)
                ? sceneKey.Substring(ScenePrefix.Length)
                : sceneKey;

            return ZoneDisplayKeyPrefix + bare.ToLowerInvariant() + ZoneDisplayKeySuffix;
        }

        private static int IndexIn(string[] source, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }

            for (var i = 0; i < source.Length; i++)
            {
                if (string.Equals(source[i], value, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
