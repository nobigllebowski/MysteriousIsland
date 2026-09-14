namespace ForgottenIsle.Core.Commands
{
    /// <summary>
    /// Marker for an intent to change game state. A command is a *request*, never a result:
    /// it carries only the data needed to describe what the player (or a system acting on the
    /// player's behalf) wants to happen, and it is always validated before anything mutates.
    /// </summary>
    /// <remarks>
    /// Commands are deliberately implemented as readonly structs and dispatched by
    /// <see cref="CommandDispatcher"/> through <c>in</c> parameters. That keeps the whole
    /// command path allocation-free, which matters because travel and save commands are issued
    /// from UI callbacks on mobile where a per-input allocation is a per-input GC risk.
    /// The marker interface causes one boxing conversion only if a command is ever stored as
    /// <see cref="ICommand"/>; nothing in the dispatch path does that, it stays generic end to end.
    /// </remarks>
    public interface ICommand
    {
    }
}
