using System.Collections.Generic;
using ForgottenIsle.Core.Audio;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Radio;
using UnityEngine;

namespace ForgottenIsle.Game.Audio
{
    /// <summary>
    /// Plays the zone's bed, the pressure cycle, the radio's hiss and carrier, the fire, and the
    /// one-shot cues. Authored clips when they exist; arithmetic when they do not.
    /// </summary>
    /// <remarks>
    /// THE PROJECT SHIPS NO AUDIO FILES, AND THIS DOES NOT FABRICATE ANY. For two phases every
    /// play call was a no-op, which kept the wiring honest and the game silent -- and the game is
    /// built on hearing: the radio's tolerance ladder, the knock and the ring, the surf under
    /// everything. So each cue now has an arithmetic stand-in (<see cref="Synth"/>, engine-free,
    /// deterministic) built at attach time with <c>AudioClip.Create</c> and <c>SetData</c>
    /// (⚠ VERIFY: https://docs.unity3d.com/ScriptReference/AudioClip.Create.html,
    /// https://docs.unity3d.com/ScriptReference/AudioClip.SetData.html). Nothing is written to
    /// disk and nothing pretends to be a recording. A clip in <c>Resources/Audio</c> under the
    /// same name wins over the stand-in with no code change. (ADR-0027)
    /// <para>
    /// The cues are the signals the game already publishes. The radio's mix follows
    /// <see cref="RadioMix"/> on every needle move, so what the ribbon shows and what the ear
    /// hears share one arithmetic. Knock and ring are keyed off what she says about the rock:
    /// narration is the game's event stream for those acts, and the cue table reads it.
    /// </para>
    /// </remarks>
    public sealed class AudioDirector
    {
        private const string AmbientPrefix = "Audio/ambient_";
        private const string EffectPrefix = "Audio/sfx_";

        /// <summary>The pressure cycle's mix (§2:00): -38 dBFS, under everything, from the first frame.</summary>
        public const float PressureDbfs = -38f;

        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private readonly ICoreLog _log;

        private AudioSource _ambient;
        private AudioSource _pressure;
        private AudioSource _hiss;
        private AudioSource _carrier;
        private AudioSource _fire;
        private AudioSource _effects;
        private RadioService _radio;
        private FireService _fireService;
        private string _currentZone = string.Empty;
        private bool _synthesisReported;
        private double _playSeconds;
        private bool _inGame;

        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public AudioDirector(ICoreLog log)
        {
            _log = log;
        }

        /// <summary>True when at least one authored clip was found in Resources.</summary>
        public bool HasAnyAudio { get; private set; }

        /// <summary>True once at least one cue was built from arithmetic rather than a file.</summary>
        public bool IsSynthesised { get; private set; }

        /// <summary>
        /// Creates the sources on the persistent host and wires the cues.
        /// </summary>
        /// <param name="host">The object the sources live on; must outlive every scene.</param>
        /// <param name="signals">Bus carrying the game's signals. Null tolerated.</param>
        /// <param name="radio">Read on entering the world, for a dial that is already open. Null tolerated.</param>
        /// <param name="fire">Read on entering the world, for a fire that already burns. Null tolerated.</param>
        public void Attach(GameObject host, SignalBus signals, RadioService radio = null, FireService fire = null)
        {
            if (host == null)
            {
                return;
            }

            _radio = radio;
            _fireService = fire;

            _ambient = Loop(host, 0f);
            _pressure = Loop(host, 0f);
            _hiss = Loop(host, 0f);
            _carrier = Loop(host, 0f);
            _fire = Loop(host, 0f);

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
            signals.Subscribe<GameStateChangedSignal>(OnStateChanged);
            signals.Subscribe<RadioChangedSignal>(OnRadioChanged);
            signals.Subscribe<FireChangedSignal>(OnFireChanged);
            signals.Subscribe<NarrationSignal>(OnNarration);
            signals.Subscribe<TickCompletedSignal>(_ => OnTick());
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
            var clip = Clip(AmbientPrefix + Suffix(zoneId), "ambient_" + Suffix(zoneId));
            if (clip == null)
            {
                Silence(_ambient);
                return;
            }

            _ambient.clip = clip;
            _ambient.volume = 0.55f;
            if (_inGame)
            {
                _ambient.Play();
            }
        }

        /// <summary>Plays a one-shot cue: an authored clip if one exists, else its stand-in.</summary>
        /// <param name="effectName">Suffix after <c>Audio/sfx_</c>, e.g. <c>inspect</c>.</param>
        public void PlayEffect(string effectName)
        {
            if (_effects == null || string.IsNullOrEmpty(effectName))
            {
                return;
            }

            var clip = Clip(EffectPrefix + effectName, effectName);
            if (clip != null)
            {
                _effects.PlayOneShot(clip);
            }
        }

        private void OnStateChanged(GameStateChangedSignal signal)
        {
            _inGame = signal.To == GameStateId.InGame;
            if (!_inGame)
            {
                // Paused, or in the menu: the beds hold rather than stop, so resuming does not
                // restart the surf from its first sample.
                Pause(_ambient);
                Pause(_pressure);
                Pause(_hiss);
                Pause(_carrier);
                Pause(_fire);
                return;
            }

            Start(_ambient);

            if (_pressure != null && _pressure.clip == null)
            {
                _pressure.clip = Clip(EffectPrefix + "pressure", "pressure");
            }

            Start(_pressure);

            if (signal.From == GameStateId.Loading)
            {
                // Entering the world -- a new run, a load, or a zone crossing: what already burns,
                // crackles; a dial is never open on entry. The pressure cycle's clock is not
                // touched here: it is one continuous breath from the first frame of the run, and a
                // zone crossing is not a new run (it is reset on Replaced, below).
                SetFire(_fireService != null && _fireService.IsLit);
                SetRadio(_radio != null && _radio.IsOpen, _radio != null ? _radio.Mhz : 0f);
                return;
            }

            // Resuming from a pause: pick the loops back up where they were.
            Start(_hiss);
            Start(_carrier);
            Start(_fire);
        }

        private void OnTick()
        {
            if (!_inGame || _pressure == null)
            {
                return;
            }

            _playSeconds += 1d / Ticker.TargetTicksPerRealSecond;
            _pressure.volume = Synth.GainFor(PressureDbfs) * Synth.PressureCycle(_playSeconds);
        }

        private void OnRadioChanged(RadioChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case RadioChangeKind.PoweredUp:
                    // A click, heavy, phenolic.
                    PlayEffect("click");
                    break;

                case RadioChangeKind.Opened:
                    SetRadio(true, signal.Mhz);
                    break;

                case RadioChangeKind.Closed:
                    SetRadio(false, signal.Mhz);
                    break;

                case RadioChangeKind.Tuned:
                case RadioChangeKind.Heard:
                    if (signal.IsOpen)
                    {
                        ApplyMix(RadioMix.At(signal.Mhz));
                    }

                    break;

                case RadioChangeKind.MicSqueezed:
                    PlayEffect("click");
                    break;
            }
        }

        private void SetRadio(bool open, float mhz)
        {
            if (_hiss == null || _carrier == null)
            {
                return;
            }

            if (!open)
            {
                Silence(_hiss);
                Silence(_carrier);
                return;
            }

            if (_hiss.clip == null)
            {
                _hiss.clip = Clip(EffectPrefix + "hiss", "hiss");
            }

            ApplyMix(RadioMix.At(mhz));
            if (_hiss.clip != null && !_hiss.isPlaying)
            {
                _hiss.Play();
            }
        }

        private void ApplyMix(RadioLevels levels)
        {
            if (_hiss == null || _carrier == null)
            {
                return;
            }

            _hiss.volume = levels.Hiss * 0.5f;

            // The smudge is a warble; anything nearer is the carrier, pitched by the detune. Two
            // clips rather than a live modulator: the tremolo is baked into one of them.
            var wanted = levels.Warble ? "warble" : "carrier";
            var clip = Clip(EffectPrefix + wanted, wanted);
            if (_carrier.clip != clip)
            {
                _carrier.clip = clip;
                if (clip != null && _inGame)
                {
                    _carrier.Play();
                }
            }
            else if (clip != null && !_carrier.isPlaying && _inGame)
            {
                _carrier.Play();
            }

            _carrier.volume = levels.Carrier * 0.6f;
            _carrier.pitch = levels.Pitch;
        }

        private void OnFireChanged(FireChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case FireChangeKind.FirstSparks:
                case FireChangeKind.SparksFailed:
                    PlayEffect("spark");
                    break;

                case FireChangeKind.BlewOut:
                    PlayEffect("spark");
                    PlayEffect("slap");
                    break;

                case FireChangeKind.Lit:
                    PlayEffect("spark");
                    SetFire(true);
                    break;
            }
        }

        private void SetFire(bool lit)
        {
            if (_fire == null)
            {
                return;
            }

            if (!lit)
            {
                Silence(_fire);
                return;
            }

            if (_fire.clip == null)
            {
                _fire.clip = Clip(EffectPrefix + "fire", "fire");
            }

            _fire.volume = 0.5f;
            if (_fire.clip != null && !_fire.isPlaying && _inGame)
            {
                _fire.Play();
            }
        }

        private void OnNarration(NarrationSignal signal)
        {
            // The rock test is the one cue with no signal of its own: what she says about the
            // rock is the event. Basalt knocks; chert rings. Everything else stays with its signal.
            if (signal.LineKey == "narration." + ContentIds.RemarkRockKnock)
            {
                PlayEffect("knock");
            }
            else if (signal.LineKey == "narration." + ContentIds.RemarkRockRing)
            {
                PlayEffect("ring");
            }
        }

        private void OnProgressChanged(ProgressChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case ProgressChangeKind.Replaced:
                    // A new run, or a load: the breath starts again from its first frame.
                    _playSeconds = 0d;
                    break;

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

        /// <summary>An authored clip at <paramref name="path"/>, else the stand-in named <paramref name="cue"/>.</summary>
        private AudioClip Clip(string path, string cue)
        {
            AudioClip cached;
            if (_cache.TryGetValue(path, out cached))
            {
                return cached;
            }

            var clip = Resources.Load<AudioClip>(path);
            if (clip != null)
            {
                HasAnyAudio = true;
            }
            else
            {
                clip = Synthesise(cue);
            }

            _cache[path] = clip;
            return clip;
        }

        private AudioClip Synthesise(string cue)
        {
            float[] data;
            switch (cue)
            {
                case "ambient_ribcage":
                    // Surf: a low, wide rumble (§2:00, 60-90 Hz) with a little wind above it.
                    data = Buffer(4f);
                    Synth.Noise(data, 11, 1f);
                    Synth.LowPass(data, 110f);
                    Scale(data, 3.2f);
                    Synth.Tremolo(data, 0.12f, 0.5f);
                    break;
                case "ambient_fernmaw":
                    // Water in a channel: brighter, steadier.
                    data = Buffer(4f);
                    Synth.Noise(data, 13, 1f);
                    Synth.LowPass(data, 500f);
                    Scale(data, 1.4f);
                    break;
                case "pressure":
                    data = Buffer(2f);
                    Synth.Sine(data, 32f, 1f);
                    break;
                case "hiss":
                    data = Buffer(2f);
                    Synth.Noise(data, 17, 1f);
                    break;
                case "carrier":
                    data = Buffer(1f);
                    Synth.Sine(data, 620f, 1f);
                    break;
                case "warble":
                    data = Buffer(1f);
                    Synth.Sine(data, 620f, 1f);
                    Synth.Tremolo(data, 5f, 0.9f);
                    break;
                case "fire":
                    data = Buffer(4f);
                    Synth.Crackle(data, 19, 14f, 1f);
                    break;
                case "click":
                    data = Buffer(0.08f);
                    Synth.Strike(data, 23, 1200f, 0.015f, 0.7f, 0.8f);
                    break;
                case "knock":
                    // Basalt: low, short, mostly contact.
                    data = Buffer(0.2f);
                    Synth.Strike(data, 29, 180f, 0.05f, 0.8f, 0.9f);
                    break;
                case "ring":
                    // Chert: high, long, mostly tone. The whole clue.
                    data = Buffer(0.9f);
                    Synth.Strike(data, 31, 2400f, 0.35f, 0.15f, 0.7f);
                    break;
                case "spark":
                    data = Buffer(0.12f);
                    Synth.Strike(data, 37, 3000f, 0.03f, 0.9f, 0.6f);
                    break;
                case "slap":
                    data = Buffer(0.25f);
                    Synth.Strike(data, 41, 90f, 0.08f, 0.9f, 0.9f);
                    break;
                case "inspect":
                case "collect":
                case "unlock":
                    data = Buffer(0.06f);
                    Synth.Strike(data, 43, 900f, 0.02f, 0.5f, 0.35f);
                    break;
                default:
                    return null;
            }

            Synth.Clamp(data);
            IsSynthesised = true;
            ReportSynthesisOnce();

            // ⚠ VERIFY: AudioClip.Create(name, lengthSamples, channels, frequency, stream) and
            // SetData(float[], offset) — https://docs.unity3d.com/ScriptReference/AudioClip.Create.html
            var clip = AudioClip.Create("synth_" + cue, data.Length, 1, Synth.SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float[] Buffer(float seconds)
        {
            return new float[(int)(Synth.SampleRate * seconds)];
        }

        private static void Scale(float[] data, float by)
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] *= by;
            }
        }

        private static AudioSource Loop(GameObject host, float volume)
        {
            var source = host.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = volume;
            return source;
        }

        private static void Pause(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Pause();
            }
        }

        /// <summary>
        /// Stops a loop on purpose and forgets its clip, so a later <see cref="Start"/> after a
        /// pause cannot mistake it for a fresh loop and play it: a closed dial must stay silent
        /// across a pause, and a fire put out in the last run must not crackle in the next.
        /// </summary>
        private static void Silence(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
        }

        /// <summary>Plays a loop, or unpauses it where it stopped. Nothing without a clip.</summary>
        /// <remarks>
        /// ⚠ VERIFY: whether Play() after Pause() restarts from the first sample; UnPause() is
        /// documented to continue (https://docs.unity3d.com/ScriptReference/AudioSource.UnPause.html),
        /// so a paused source is unpaused and only a fresh one is played.
        /// </remarks>
        private static void Start(AudioSource source)
        {
            if (source == null || source.clip == null || source.isPlaying)
            {
                return;
            }

            if (source.time > 0f)
            {
                source.UnPause();
            }
            else
            {
                source.Play();
            }
        }

        private void ReportSynthesisOnce()
        {
            if (_synthesisReported || _log == null)
            {
                return;
            }

            _synthesisReported = true;
            _log.Warn(LogCode.CatalogMissing,
                "no authored clips in Resources/Audio — cues are synthesised stand-ins (ADR-0027)");
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
