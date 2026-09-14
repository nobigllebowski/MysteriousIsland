using System;
using System.Collections.Generic;
using ForgottenIsle.Game.Scenes;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Guards <see cref="SceneKeys"/>, the single place a scene name is spelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this catches is mundane and expensive: a zone is added, the constant is declared,
    /// and the author forgets to append it to <see cref="SceneKeys.All"/> — or appends it twice, or
    /// pastes the wrong constant so two entries name the same scene. None of that fails to compile.
    /// Everything downstream trusts <c>All</c> as the authoritative list (it is what a build-settings
    /// check iterates and what <c>SceneLoader</c> validates a request against), so a list that has
    /// drifted from the constants shows up much later as a scene that will not load.
    /// </para>
    /// <para>
    /// Every constant is named explicitly below rather than discovered by reflection. Reflection would
    /// make the test auto-cover a new constant, which sounds like a feature and is not: it would also
    /// make the test silently stop asserting anything about a constant that was renamed out from under
    /// it. The explicit list is a second place the addition has to be made, and that is the point.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class SceneKeyTests
    {
        /// <summary>Every scene constant declared on <see cref="SceneKeys"/>, restated independently.</summary>
        private static readonly string[] DeclaredConstants =
        {
            SceneKeys.Bootstrap,
            SceneKeys.MainMenu,
            SceneKeys.ZoneRibcage,
            SceneKeys.ZoneFernmaw
        };

        [Test]
        public void SceneKeysAll_ContainsNoNullOrEmptyEntries()
        {
            Assert.IsNotNull(SceneKeys.All);
            Assert.Greater(SceneKeys.All.Count, 0, "The scene list is empty.");

            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                Assert.IsNotNull(key, "SceneKeys.All[" + i + "] is null.");
                Assert.AreNotEqual(string.Empty, key, "SceneKeys.All[" + i + "] is empty.");
                Assert.AreEqual(key.Trim(), key, "SceneKeys.All[" + i + "] has surrounding whitespace: '" + key + "'");
            }
        }

        [Test]
        public void SceneKeysAll_ContainsNoDuplicates()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in SceneKeys.All)
            {
                Assert.IsTrue(seen.Add(key), "SceneKeys.All lists '" + key + "' more than once.");
            }

            Assert.AreEqual(SceneKeys.All.Count, seen.Count);
        }

        /// <summary>
        /// The drift check in the direction that actually happens: a constant declared and then left
        /// out of the list.
        /// </summary>
        [TestCase(SceneKeys.Bootstrap)]
        [TestCase(SceneKeys.MainMenu)]
        [TestCase(SceneKeys.ZoneRibcage)]
        [TestCase(SceneKeys.ZoneFernmaw)]
        public void SceneKeysAll_ContainsEveryDeclaredConstant(string sceneKey)
        {
            CollectionAssert.Contains(new List<string>(SceneKeys.All), sceneKey, "'" + sceneKey + "' is declared but absent from SceneKeys.All.");
        }

        /// <summary>
        /// And the opposite direction: nothing in the list that is not a declared constant, which would
        /// mean a name spelled somewhere other than on its own constant.
        /// </summary>
        [Test]
        public void SceneKeysAll_ContainsNothingBeyondTheDeclaredConstants()
        {
            var declared = new HashSet<string>(DeclaredConstants, StringComparer.Ordinal);

            foreach (var key in SceneKeys.All)
            {
                Assert.IsTrue(declared.Contains(key), "SceneKeys.All contains '" + key + "', which matches no declared constant.");
            }

            Assert.AreEqual(DeclaredConstants.Length, SceneKeys.All.Count);
        }

        [Test]
        public void SceneKeysConstants_AreDistinctFromOneAnother()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in DeclaredConstants)
            {
                Assert.IsFalse(string.IsNullOrEmpty(key), "A scene constant is null or empty.");
                Assert.IsTrue(seen.Add(key), "Two scene constants share the value '" + key + "'.");
            }
        }

        /// <summary>
        /// Zones are a strict subset of the scene list, and the non-zone scenes are not zones.
        /// <c>ZoneRegistry</c> enforces a two-resident cap over exactly this set (ADR-0004), and
        /// <c>TravelToZoneHandler</c> uses it to reject travelling to the main menu — which would load
        /// the menu on top of a live run.
        /// </summary>
        [Test]
        public void SceneKeysZones_AreASubsetOfAllAndExcludeTheNonZoneScenes()
        {
            Assert.IsNotNull(SceneKeys.Zones);
            Assert.Greater(SceneKeys.Zones.Count, 0);

            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in SceneKeys.All)
            {
                all.Add(key);
            }

            foreach (var zone in SceneKeys.Zones)
            {
                Assert.IsTrue(all.Contains(zone), "Zone '" + zone + "' is not in SceneKeys.All.");
                Assert.IsTrue(SceneKeys.IsZone(zone));
                Assert.IsTrue(SceneKeys.IsKnown(zone));
            }

            Assert.IsFalse(SceneKeys.IsZone(SceneKeys.Bootstrap));
            Assert.IsFalse(SceneKeys.IsZone(SceneKeys.MainMenu));
            Assert.Less(SceneKeys.Zones.Count, SceneKeys.All.Count, "Every scene is a zone, which cannot be right.");
        }

        [Test]
        public void SceneKeysZones_ContainsNoDuplicates()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var zone in SceneKeys.Zones)
            {
                Assert.IsFalse(string.IsNullOrEmpty(zone), "SceneKeys.Zones holds a null or empty entry.");
                Assert.IsTrue(seen.Add(zone), "SceneKeys.Zones lists '" + zone + "' more than once.");
            }
        }

        [Test]
        public void SceneKeysIsKnown_RejectsUnknownNullAndEmptyNames()
        {
            Assert.IsFalse(SceneKeys.IsKnown("ZoneDoesNotExist"));
            Assert.IsFalse(SceneKeys.IsKnown(null));
            Assert.IsFalse(SceneKeys.IsKnown(string.Empty));
            Assert.IsFalse(SceneKeys.IsKnown("zoneribcage"), "Scene name matching must be ordinal and case-sensitive.");
        }

        /// <summary>
        /// The display key is derived rather than tabulated so that adding a zone cannot add a zone
        /// with no name. The result is always a KEY — an unknown scene yields empty, which the caller
        /// stores and which renders as a visible missing-key marker rather than a blank slot card.
        /// </summary>
        [TestCase(SceneKeys.ZoneRibcage, "zone.ribcage.name")]
        [TestCase(SceneKeys.ZoneFernmaw, "zone.fernmaw.name")]
        public void SceneKeysZoneDisplayKey_DerivesALocalizationKeyWithoutStuttering(string sceneKey, string expected)
        {
            Assert.AreEqual(expected, SceneKeys.ZoneDisplayKey(sceneKey));
        }

        [Test]
        public void SceneKeysZoneDisplayKey_EmptyOrNullSceneName_YieldsEmptyString()
        {
            Assert.AreEqual(string.Empty, SceneKeys.ZoneDisplayKey(null));
            Assert.AreEqual(string.Empty, SceneKeys.ZoneDisplayKey(string.Empty));
        }

        [Test]
        public void SceneKeysZoneDisplayKey_IsDistinctPerZone()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var zone in SceneKeys.Zones)
            {
                var key = SceneKeys.ZoneDisplayKey(zone);
                Assert.IsFalse(string.IsNullOrEmpty(key), "Zone '" + zone + "' derived an empty display key.");
                Assert.IsTrue(seen.Add(key), "Two zones share the display key '" + key + "'.");
            }
        }
    }
}
