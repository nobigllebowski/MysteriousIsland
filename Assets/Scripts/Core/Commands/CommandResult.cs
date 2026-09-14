using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Commands
{
    /// <summary>
    /// The outcome of dispatching a single command: did it run, and if not, why not.
    /// </summary>
    /// <remarks>
    /// This type exists so that a rejected command is an ordinary, inspectable value rather than
    /// an exception. Nearly every rejection in this game is expected traffic — travelling to a
    /// zone that is not adjacent, saving from a state that forbids saving, loading an empty slot —
    /// and exceptions would make the normal case expensive and the call sites unreadable.
    /// Callers that only care whether it worked read <see cref="Success"/>; callers that must
    /// surface a reason to the player map <see cref="Code"/> to a <see cref="LocKey"/>.
    /// </remarks>
    public readonly struct CommandResult
    {
        /// <summary>True when the command passed validation and its handler executed.</summary>
        public readonly bool Success;

        /// <summary>
        /// Why the command failed, or <see cref="ResultCode.Ok"/> when it succeeded.
        /// Never guess from this value alone that a handler ran — check <see cref="Success"/>.
        /// </summary>
        public readonly ResultCode Code;

        private CommandResult(bool success, ResultCode code)
        {
            Success = success;
            Code = code;
        }

        /// <summary>The single success value. Allocation-free; structs are copied, not shared.</summary>
        public static CommandResult Ok
        {
            get { return new CommandResult(true, ResultCode.Ok); }
        }

        /// <summary>
        /// Builds a failure carrying <paramref name="code"/>.
        /// </summary>
        /// <remarks>
        /// Passing <see cref="ResultCode.Ok"/> here would produce a result that claims failure
        /// while reporting success, which is the sort of contradiction that survives review and
        /// then misleads every reader afterwards. It is coerced to
        /// <see cref="ResultCode.InvalidArgument"/> instead of throwing, because the command path
        /// is required never to throw.
        /// </remarks>
        public static CommandResult Fail(ResultCode code)
        {
            if (code == ResultCode.Ok)
            {
                return new CommandResult(false, ResultCode.InvalidArgument);
            }

            return new CommandResult(false, code);
        }
    }
}
