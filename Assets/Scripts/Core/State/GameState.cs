namespace ForgottenIsle.Core.State
{
    /// <summary>
    /// The root of everything that varies between one run and another. If it is not reachable from
    /// here, it does not survive a save.
    /// </summary>
    /// <remarks>
    /// Holding the RNG state here, rather than inside a random service, is what makes a loaded
    /// game deterministic: restore this object and the next number drawn is the number that would
    /// have been drawn had the player never quit. A random service that seeded itself at
    /// construction would quietly re-roll the world on every load.
    /// </remarks>
    public sealed class GameState
    {
        /// <summary>Run-level state. Never null.</summary>
        public SessionState Session;

        /// <summary>Player-body state. Never null.</summary>
        public PlayerState Player;

        /// <summary>
        /// Serialized state word of the run's <c>PcgRandom</c>. Captured on save and pushed back
        /// through <c>SetState</c> on load.
        /// </summary>
        public ulong RngState;

        /// <summary>
        /// Creates a state with empty-but-valid children, so no consumer ever has to null-check
        /// its way down the tree. A freshly constructed <see cref="GameState"/> represents "a run
        /// that has not started yet", not "a missing run".
        /// </summary>
        public GameState()
        {
            Session = new SessionState();
            Player = new PlayerState();
            RngState = 0UL;
        }

        /// <summary>
        /// Returns a fully independent copy: the children are cloned, not re-referenced.
        /// </summary>
        /// <remarks>
        /// This is the method that makes save-then-keep-playing safe. Handing the save path a
        /// shallow copy would let the simulation keep mutating the object being serialized, and
        /// the resulting save would be a smear across several frames rather than a snapshot of
        /// one. Null children are tolerated and replaced with fresh instances rather than
        /// propagated, so a clone is always a valid state even if its source was tampered with.
        /// </remarks>
        public GameState Clone()
        {
            var copy = new GameState();
            copy.Session = Session != null ? Session.Clone() : new SessionState();
            copy.Player = Player != null ? Player.Clone() : new PlayerState();
            copy.RngState = RngState;
            return copy;
        }
    }
}
