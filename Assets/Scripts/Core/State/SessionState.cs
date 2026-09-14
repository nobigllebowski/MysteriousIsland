namespace ForgottenIsle.Core.State
{
    /// <summary>
    /// Everything about the run as a whole that is not the player's body: where the story is,
    /// where the player is in the world, and how long they have been at it.
    /// </summary>
    /// <remarks>
    /// Public mutable fields rather than properties, and a plain class rather than a record: this
    /// is a serialization payload that is written and read on the save path, and every property
    /// accessor on it would be a call that IL2CPP may or may not inline on a mid-range Android
    /// device. Mutation is not a licence to mutate from anywhere — <c>SessionService</c> is the
    /// only writer, by convention the Game layer enforces.
    /// </remarks>
    public sealed class SessionState
    {
        /// <summary>Id of the act the story is currently in. Empty before a run starts.</summary>
        public string ActId = string.Empty;

        /// <summary>Id of the zone the player currently occupies. Empty before a run starts.</summary>
        public string ZoneId = string.Empty;

        /// <summary>
        /// In-fiction hours elapsed since the run began. This is the clock the world is authored
        /// against; it is not wall-clock time and does not advance while paused.
        /// </summary>
        public double SimHours;

        /// <summary>
        /// Real seconds the player has spent in this run, accumulated across loads. Shown on the
        /// slot card, never used by the simulation.
        /// </summary>
        public double PlaytimeSeconds;

        /// <summary>
        /// Completion percentage last computed for this run, 0..100. Stored rather than derived so
        /// the save-slot list can show it without deserializing and re-scoring the whole save.
        /// </summary>
        public int RecordedPercent;

        /// <summary>
        /// Returns an independent copy. Every field here is a value type or an immutable string,
        /// so field-by-field assignment is a genuine deep copy — there is nothing left aliased.
        /// </summary>
        public SessionState Clone()
        {
            var copy = new SessionState();
            copy.ActId = ActId;
            copy.ZoneId = ZoneId;
            copy.SimHours = SimHours;
            copy.PlaytimeSeconds = PlaytimeSeconds;
            copy.RecordedPercent = RecordedPercent;
            return copy;
        }
    }
}
