using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Commands
{
    /// <summary>
    /// Handles exactly one command type: first deciding whether it is legal, then performing it.
    /// </summary>
    /// <typeparam name="TCommand">The command type this handler owns.</typeparam>
    /// <remarks>
    /// The split between <see cref="Validate"/> and <see cref="Execute"/> is the whole point of
    /// this interface. Validation is pure and repeatable, so UI can call it to grey out a button
    /// long before the player presses it, and the dispatcher can call it again at the moment of
    /// dispatch without risking a half-applied mutation. Execute is only ever reached once
    /// validation has returned <see cref="ResultCode.Ok"/>, which means implementations may assume
    /// their preconditions hold and must not re-report failure.
    /// </remarks>
    public interface ICommandHandler<TCommand> where TCommand : ICommand
    {
        /// <summary>
        /// Returns <see cref="ResultCode.Ok"/> if the command may run right now, otherwise the
        /// reason it may not. Must be free of side effects: it is called speculatively.
        /// </summary>
        ResultCode Validate(in TCommand command);

        /// <summary>
        /// Applies the command. Called only after <see cref="Validate"/> returned
        /// <see cref="ResultCode.Ok"/> for this same value, so it has no failure channel.
        /// </summary>
        void Execute(in TCommand command);
    }
}
