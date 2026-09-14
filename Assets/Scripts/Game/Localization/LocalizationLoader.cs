// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Data/StaticDataLoader.cs
// (the LoadLocalization half).
// Adapted for Vardholm: reads the translator-facing two-column CSV through CsvTableParser instead of
// Nation's locale-tagged JSON tables, takes the locale from the file name rather than from inside the
// file, reports through ICoreLog instead of Debug.LogError, and never throws — a string table that fails
// to load must still leave the game able to reach the menu and show what is missing.

using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Data;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using UnityEngine;

namespace ForgottenIsle.Game.Localization
{
    /// <summary>
    /// Bridge from Unity's asset pipeline to the engine-free <see cref="StringTableLocalization"/>: finds
    /// the shipped CSV string tables, parses them, and installs them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LOADING MECHANISM: <b>Resources</b>, not StreamingAssets, and the choice is not arbitrary. The
    /// string table is needed before the first frame of the first screen — a main menu drawn with
    /// <c>#ui.menu.continue#</c> everywhere is not a main menu. <c>Resources.Load</c> is synchronous and
    /// behaves identically on every platform, so boot can be a straight line. StreamingAssets on Android
    /// lives inside the compressed APK and is reachable only through <c>UnityWebRequest</c>, which would
    /// force the entire boot sequence to become asynchronous in order to fetch one small text file. The
    /// cost of Resources is that the tables are always in the build and always loaded; for a few tens of
    /// kilobytes of UTF-8, that is the right trade.
    /// </para>
    /// <para>
    /// WHERE THE FILES LIVE. At runtime the loader looks for
    /// <c>Assets/Resources/Localization/&lt;locale&gt;.csv</c>, because Unity only serves
    /// <c>Resources.Load</c> from a folder literally named <c>Resources</c>. The authoring copy that
    /// translators edit is <c>Assets/Localization/en.csv</c>; in the Editor, and only in the Editor, this
    /// class reads that file straight off disk when the Resources lookup misses, so an edit is picked up
    /// without an import step. A player build has no such fallback: if the CSV is not under a
    /// <c>Resources</c> folder it is not in the build, and the loader says so through
    /// <see cref="LogCode.CatalogMissing"/> rather than failing quietly.
    /// </para>
    /// <para>
    /// Nothing here throws, for the same reason <see cref="CsvTableParser"/> skips malformed rows: this
    /// runs during boot, the file is the one asset most likely to have been edited by a non-programmer, and
    /// the worst acceptable outcome is a screen full of <c>#key#</c> placeholders — never a black screen.
    /// </para>
    /// </remarks>
    public static class LocalizationLoader
    {
        /// <summary>Folder inside <c>Resources</c> holding the shipped tables.</summary>
        public const string ResourceFolder = "Localization";

        /// <summary>The authoring language, always loaded, and the fallback for every other locale.</summary>
        public const string AuthoringLocale = "en";

        /// <summary>
        /// Path, relative to the project folder, of the translator-facing copy used by the Editor-only
        /// fallback described in the class remarks.
        /// </summary>
        public const string EditorAuthoringFolder = "Assets/Localization";

        /// <summary>
        /// Locales shipped with the build, in load order.
        /// </summary>
        /// <remarks>
        /// An explicit list rather than <c>Resources.LoadAll</c>: loading everything in the folder would
        /// also pick up any stray CSV a designer dropped there and register it as a locale the player can
        /// select. Adding a language is a deliberate act, and this array is where it happens.
        /// </remarks>
        public static readonly string[] ShippedLocales = { AuthoringLocale };

        /// <summary>
        /// Loads every shipped table into <paramref name="localization"/>.
        /// </summary>
        /// <remarks>
        /// The authoring locale is loaded first so that, if a later table fails, the fallback chain is
        /// already intact and every key still resolves to something readable.
        /// </remarks>
        /// <returns>The number of locales successfully installed. Zero means every lookup will be a placeholder.</returns>
        public static int LoadAll(StringTableLocalization localization, ICoreLog log)
        {
            if (localization == null)
            {
                return 0;
            }

            var loaded = 0;
            for (var i = 0; i < ShippedLocales.Length; i++)
            {
                if (LoadLocale(localization, ShippedLocales[i], log))
                {
                    loaded++;
                }
            }

            if (loaded == 0)
            {
                Warn(log, LogCode.CatalogMissing, "no localization tables were loaded");
            }

            return loaded;
        }

        /// <summary>
        /// Loads one locale's table and installs it.
        /// </summary>
        /// <param name="locale">Locale tag, matching the CSV's file name — <c>en</c> loads <c>en.csv</c>.</param>
        /// <returns>False when the table could not be found or contained no usable rows.</returns>
        public static bool LoadLocale(StringTableLocalization localization, string locale, ICoreLog log)
        {
            if (localization == null || string.IsNullOrEmpty(locale))
            {
                return false;
            }

            var csv = ReadTableText(locale, log);
            if (string.IsNullOrEmpty(csv))
            {
                Warn(log, LogCode.CatalogMissing, "localization table missing for locale " + locale);
                return false;
            }

            List<KeyValuePair<string, string>> rows;
            try
            {
                rows = CsvTableParser.Parse(csv);
            }
            catch (Exception exception)
            {
                // CsvTableParser is documented not to throw on malformed rows, so reaching here means
                // something pathological. Reported, not propagated: boot continues with placeholders.
                Warn(log, LogCode.CatalogMissing, "localization table " + locale + " failed to parse: " + exception.GetType().Name);
                return false;
            }

            if (rows.Count == 0)
            {
                Warn(log, LogCode.CatalogMissing, "localization table " + locale + " contained no rows");
                return false;
            }

            localization.AddTable(locale, rows);
            return true;
        }

        /// <summary>
        /// Chooses the locale to start in: the device's language when a table exists for it, otherwise the
        /// authoring locale.
        /// </summary>
        /// <remarks>
        /// Applied only when the player has expressed no preference. A stored choice must win over the
        /// device setting, because a player who deliberately switched to English on a non-English device
        /// did so for a reason and should not have it undone by an OS update.
        /// </remarks>
        public static string ResolveStartingLocale(StringTableLocalization localization)
        {
            var deviceLocale = ToLocaleTag(Application.systemLanguage);

            if (localization != null && !string.IsNullOrEmpty(deviceLocale))
            {
                foreach (var available in localization.AvailableLocales)
                {
                    if (string.Equals(available, deviceLocale, StringComparison.OrdinalIgnoreCase))
                    {
                        return deviceLocale;
                    }
                }
            }

            return AuthoringLocale;
        }

        /// <summary>
        /// Maps Unity's <see cref="SystemLanguage"/> to the two-letter tags used as table file names.
        /// </summary>
        /// <remarks>
        /// Only the languages Vardholm plans to ship are mapped; everything else returns the authoring
        /// locale. A complete mapping would be dead code that still has to be maintained, and a tag with no
        /// table behind it resolves to the fallback anyway.
        /// </remarks>
        public static string ToLocaleTag(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.English:
                    return "en";
                case SystemLanguage.French:
                    return "fr";
                case SystemLanguage.German:
                    return "de";
                case SystemLanguage.Spanish:
                    return "es";
                case SystemLanguage.Italian:
                    return "it";
                case SystemLanguage.Portuguese:
                    return "pt";
                case SystemLanguage.Russian:
                    return "ru";
                case SystemLanguage.Japanese:
                    return "ja";
                case SystemLanguage.Korean:
                    return "ko";
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.Chinese:
                    return "zh-Hans";
                case SystemLanguage.ChineseTraditional:
                    return "zh-Hant";
                default:
                    return AuthoringLocale;
            }
        }

        /// <summary>
        /// Returns the raw CSV text for a locale, from Resources, or — in the Editor only — from the
        /// authoring folder.
        /// </summary>
        /// <remarks>
        /// The <see cref="Resources.UnloadAsset"/> call matters on a phone: the <see cref="TextAsset"/>
        /// holds the whole file in native memory for as long as it is loaded, and once the rows are in the
        /// dictionary the asset is dead weight for the rest of the session.
        /// </remarks>
        private static string ReadTableText(string locale, ICoreLog log)
        {
            TextAsset asset = null;
            try
            {
                asset = Resources.Load<TextAsset>(ResourceFolder + "/" + locale);
                if (asset != null)
                {
                    return asset.text;
                }
            }
            catch (Exception exception)
            {
                Warn(log, LogCode.CatalogMissing, "Resources.Load failed for " + locale + ": " + exception.GetType().Name);
            }
            finally
            {
                if (asset != null)
                {
                    Resources.UnloadAsset(asset);
                }
            }

#if UNITY_EDITOR
            try
            {
                var authoringPath = System.IO.Path.Combine(EditorAuthoringFolder, locale + ".csv");
                if (System.IO.File.Exists(authoringPath))
                {
                    Warn(
                        log,
                        LogCode.CatalogMissing,
                        "locale " + locale + " loaded from the authoring folder; it is not under Resources and will be absent from a player build");
                    return System.IO.File.ReadAllText(authoringPath, System.Text.Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                Warn(log, LogCode.CatalogMissing, "authoring table read failed for " + locale + ": " + exception.GetType().Name);
            }
#endif

            return null;
        }

        private static void Warn(ICoreLog log, LogCode code, string detail)
        {
            if (log == null)
            {
                return;
            }

            log.Warn(code, detail);
        }
    }
}
