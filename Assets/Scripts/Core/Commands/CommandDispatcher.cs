using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Commands
{
    /// <summary>
    /// Routes commands to the single handler registered for their type, validating before it
    /// executes anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry is keyed on <c>typeof(TCommand)</c> and the values are stored as
    /// <see cref="object"/> because <c>ICommandHandler&lt;T&gt;</c> has no non-generic base — the
    /// cast back is safe by construction, since the only way a value enters the dictionary is
    /// through <see cref="Register{TCommand}"/> under its own key.
    /// </para>
    /// <para>
    /// This type never throws. Not on an unregistered command, not on a rejected one, not on a
    /// null handler at registration time. Everything that can go wrong here is a routine runtime
    /// condition — a screen dispatching before wiring completed, a save attempted in the wrong
    /// state — and a throw would turn a recoverable "nothing happened" into a lost session on a
    /// player's phone. Failures are reported as <see cref="CommandResult"/> values and logged by
    /// <see cref="LogCode"/> so they are visible without being fatal.
    /// </para>
    /// </remarks>
    public sealed class CommandDispatcher
    {
        private readonly Dictionary<Type, object> _handlers;
        private readonly ICoreLog _log;

        /// <summary>
        /// Creates an empty dispatcher that reports routing problems to <paramref name="log"/>.
        /// </summary>
        /// <param name="log">
        /// Diagnostics sink. A null log is tolerated and silently ignored rather than rejected:
        /// losing a warning is preferable to failing construction of the object that every input
        /// path in the game depends on.
        /// </param>
        public CommandDispatcher(ICoreLog log)
        {
            _log = log;
            _handlers = new Dictionary<Type, object>();
        }

        /// <summary>
        /// Binds <paramref name="handler"/> as the owner of <typeparamref name="TCommand"/>.
        /// </summary>
        /// <remarks>
        /// A second registration for the same command type replaces the first. That is deliberate:
        /// it lets a test or a debug tool swap a handler without tearing down the dispatcher, and
        /// the alternative — throwing on duplicates — would break the no-throw guarantee for a
        /// case that has an obvious, harmless resolution. A null handler is refused and logged,
        /// because storing it would turn every later dispatch into a silent no-op that reports
        /// success.
        /// </remarks>
        /// <typeparam name="TCommand">Command type being claimed.</typeparam>
        /// <param name="handler">The handler that owns it.</param>
        public void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
        {
            if (handler == null)
            {
                Warn(LogCode.UnknownCommand, "null handler registered for " + typeof(TCommand).Name);
                return;
            }

            _handlers[typeof(TCommand)] = handler;
        }

        /// <summary>
        /// True when a handler is registered for <typeparamref name="TCommand"/>. Lets bootstrap
        /// code assert its own wiring without issuing a command with real side effects.
        /// </summary>
        /// <typeparam name="TCommand">Command type to probe.</typeparam>
        public bool HasHandler<TCommand>() where TCommand : ICommand
        {
            return _handlers.ContainsKey(typeof(TCommand));
        }

        /// <summary>
        /// Validates and, if legal, executes <paramref name="command"/>.
        /// </summary>
        /// <remarks>
        /// The order is fixed and load-bearing: look up, then validate, then execute. A command
        /// that fails validation must leave the world byte-for-byte unchanged, which is what makes
        /// it safe for UI to dispatch optimistically and react to the result.
        /// </remarks>
        /// <typeparam name="TCommand">Command type being dispatched.</typeparam>
        /// <param name="command">The command value, passed by reference to avoid a copy.</param>
        /// <returns>
        /// <see cref="CommandResult.Ok"/> when the handler ran;
        /// <see cref="ResultCode.NoHandler"/> when nothing is registered for the type;
        /// otherwise the code the handler's validation returned.
        /// </returns>
        public CommandResult Dispatch<TCommand>(in TCommand command) where TCommand : ICommand
        {
            object registered;
            if (!_handlers.TryGetValue(typeof(TCommand), out registered))
            {
                Warn(LogCode.UnknownCommand, typeof(TCommand).Name);
                return CommandResult.Fail(ResultCode.NoHandler);
            }

            var handler = registered as ICommandHandler<TCommand>;
            if (handler == null)
            {
                // Unreachable through Register, which is generic and keys on the same type.
                // Kept because "unreachable" plus a hard cast is how a crash on a shipped build
                // gets written; a reported NoHandler is recoverable and self-describing.
                Warn(LogCode.UnknownCommand, "handler type mismatch for " + typeof(TCommand).Name);
                return CommandResult.Fail(ResultCode.NoHandler);
            }

            var code = handler.Validate(in command);
            if (code != ResultCode.Ok)
            {
                return CommandResult.Fail(code);
            }

            handler.Execute(in command);
            return CommandResult.Ok;
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log == null)
            {
                return;
            }

            _log.Warn(code, detail);
        }
    }
}
