// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Signals/ISignal.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Signals; the comment's reference to
// Nation's EventDefinition dropped, since Vardholm has no event-card system.

namespace ForgottenIsle.Core.Signals
{
    /// <summary>
    /// Marker for in-memory notifications published by the simulation and services.
    /// </summary>
    /// <remarks>
    /// Signals describe something that ALREADY HAPPENED and are never player-facing content. WHY the
    /// distinction matters: a subscriber may not veto or alter a signal, so publishers are free to fire
    /// one after the mutation is committed and need not care who is listening. Anything a subscriber is
    /// meant to be able to reject belongs in Core.Commands instead.
    /// <para>
    /// Implement as a <c>readonly struct</c> so publishing a signal costs no allocation.
    /// </para>
    /// </remarks>
    public interface ISignal
    {
    }
}
