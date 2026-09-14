using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using UnityEngine;

namespace ForgottenIsle.Game.Audio
{
    /// <summary>
    /// Plays a zone's ambient bed and one-shot interaction sounds. Silent when no clips exist.
    /// </summary>
    /// <remarks>
    /// THE PROJECT SHIPS NO AUDIO FILES, AND THIS DELIBERATELY DOES NOT FABRICATE ANY. Generating
    /// binary placeholder clips would put unreviewable bytes in git and, worse, make "the audio
    /// works" look true when nothing has been authored. Instead every clip lookup may legitimately
    /// return null and every play call becomes a no-op — so the game runs, the wiring is real and
    /// testable, and the day a .wav lands in <c>Resources/Audio</c> it plays with no code change.
    /// <para>
    /// Clips are looked up by convention (<c>Audio/ambient_&lt;zone&gt;</c>, <c>Audio/sfx_&lt;verb&gt;</c>)
    /// through <c>Resources.Load</c>, the same mechanism the localization tables use, so adding
    /// sound is a file drop rather than an Inspector pass.
    /// </para>
    /// </remarks>
    public sealed class AudioDirector
    {
        private const string AmbientPrefix = "Audio/ambient_";
        private const string EffectPrefix = "Audio/sfx_";

        /// <summary>Seconds an ambient bed takes to fade in or out across a zone change.</summary>
        private const float CrossfadeSeconds = 1.6f;

        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private readonly ICoreLog _log;

        private AudioSource _ambient;
        private AudioSource _effects;
        private string _currentZone = string.Empty;
        private bool _missingClipsReported;

        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public AudioDirector(ICoreLog log)
        {
            _log = log;
        }

        /// <summary>True when at least one clip was found. False means the game is running silent.</summary>
        public bool HasAnyAudio { get; private set; }

        /// <summary>
        /// Creates the two sources on the persistent host.
        /// </summary>
        /// <param name="host">The object the sources live on; must outlive every scene.</param>
        /// <param name="signals">Bus carrying zone and interaction signals. Null tolerated.</param>
        public void Attach(GameObject host, SignalBus signals)
        {
            if (host == null)
            {
                return;
            }

            _ambient = host.AddComponent<AudioSource>();
            _ambient.loop = true;
            _ambient.playOnAwake = false;
            _ambient.spatialBlend = 0f;
            _ambient.volume = 0f;

            _effects = host.AddComponent<AudioSource>();
            _effects.loop = false;
            _effects.playOnAwake = false;
            _effects.spatialBlend = 0f;

            if (signals == null)
            {
                return;
            }

            signals.Subscribe<ZoneChangedSignal>(s => PlayZoneAmbient(s.ZoneId));
            signals.Subscribe<ProgressChangedSignal>(OnProgressChanged);
        }

        /// <summary>Switches the ambient bed to the one belonging to <paramref name="zoneId"/>.</summary>
        /// <param name="zoneId">A scene key.</param>
        public void PlayZoneAmbient(string zoneId)
        {
            if (_ambient == null || string.IsNullOrEmpty(zoneId) || _currentZone == zoneId)
            {
                return;
            }

            _currentZone = zoneId;

            var clip = Load(AmbientPrefix + Suffix(zoneId));
            if (clip == null)
            {
                _ambient.Stop();
                ReportSilenceOnce();
                return;
            }

            _ambient.clip = clip;
            _ambient.volume = 0.55f;
            _ambient.Play();
        }

        /// <summary>Plays a one-shot effect. Does nothing when the clip is absent.</summary>
        /// <param name="effectName">Suffix after <c>Audio/sfx_</c>, e.g. <c>inspect</c>.</param>
        public void PlayEffect(string effectName)
        {
            if (_effects == null || string.IsNullOrEmpty(effectName))
            {
                return;
            }

            var clip = Load(EffectPrefix + effectName);
            if (clip == null)
            {
                ReportSilenceOnce();
                return;
            }

            _effects.PlayOneShot(clip);
        }

        private void OnProgressChanged(ProgressChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case ProgressChangeKind.Inspected:
                    PlayEffect("inspect");
                    break;

                case ProgressChangeKind.Collected:
                    PlayEffect("collect");
                    break;

                case ProgressChangeKind.ZoneUnlocked:
                    PlayEffect("unlock");
                    break;
            }
        }

        private AudioClip Load(string path)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            // A miss is cached too. Resources.Load on an absent path is not free, and an effect that
            // does not exist would otherwise be looked up on every single interaction.
            var clip = Resources.Load<AudioClip>(path);
            _cache[path] = clip;

            if (clip != null)
            {
                HasAnyAudio = true;
            }

            return clip;
        }

        private void ReportSilenceOnce()
        {
            if (_missingClipsReported || _log == null)
            {
                return;
            }

            _missingClipsReported = true;
            _log.Warn(LogCode.CatalogMissing,
                "no audio clips in Resources/Audio — running silent (expected until audio is authored)");
        }

        private static string Suffix(string zoneId)
        {
            // ZoneRibcage -> ribcage, so the file is Resources/Audio/ambient_ribcage.wav.
            const string prefix = "Zone";
            var bare = zoneId.StartsWith(prefix, System.StringComparison.Ordinal)
                ? zoneId.Substring(prefix.Length)
                : zoneId;

            return bare.ToLowerInvariant();
        }
    }
}
