namespace ForgottenIsle.Core.Items
{
    /// <summary>
    /// What happened when an item was used on something.
    /// </summary>
    /// <remarks>
    /// The target decides, not the handler. A sluice knows a key fits it; a command handler has no
    /// business holding a table of which item opens which door, and a game that grows that table
    /// centrally ends up with one class that knows everything and cannot be changed safely.
    /// </remarks>
    public readonly struct UseOutcome
    {
        /// <summary>Nothing happened. The player is told so rather than left guessing.</summary>
        public static readonly UseOutcome Nothing = new UseOutcome(false, false, null);

        /// <summary>True when the target accepted the item and did something.</summary>
        public readonly bool Succeeded;

        /// <summary>True when the item was spent and should leave the inventory.</summary>
        public readonly bool ConsumesItem;

        /// <summary>Localization key of the line to show. Null falls back to a generic refusal.</summary>
        public readonly string NarrationKey;

        /// <param name="succeeded">Whether the target did something.</param>
        /// <param name="consumesItem">Whether the item was spent.</param>
        /// <param name="narrationKey">What to tell the player.</param>
        public UseOutcome(bool succeeded, bool consumesItem, string narrationKey)
        {
            Succeeded = succeeded;
            ConsumesItem = consumesItem;
            NarrationKey = narrationKey;
        }

        /// <summary>The target worked and kept the item — a key turned, not spent.</summary>
        /// <param name="narrationKey">What to tell the player.</param>
        public static UseOutcome Worked(string narrationKey)
        {
            return new UseOutcome(true, false, narrationKey);
        }

        /// <summary>The target worked and used the item up.</summary>
        /// <param name="narrationKey">What to tell the player.</param>
        public static UseOutcome Spent(string narrationKey)
        {
            return new UseOutcome(true, true, narrationKey);
        }

        /// <summary>The target refused, with something to say about it.</summary>
        /// <param name="narrationKey">What to tell the player.</param>
        public static UseOutcome Refused(string narrationKey)
        {
            return new UseOutcome(false, false, narrationKey);
        }
    }

    /// <summary>
    /// Asks the world what a target does with an item.
    /// </summary>
    /// <remarks>
    /// Engine-free on purpose: the handler that needs this lives above Core but must not reach into
    /// the scene to answer the question. The implementation is the interaction registry, which does
    /// know about scene objects, and it is handed in rather than looked up.
    /// </remarks>
    public interface IUseTargetResolver
    {
        /// <summary>
        /// Applies <paramref name="itemId"/> to <paramref name="targetId"/> and reports the result.
        /// </summary>
        /// <param name="itemId">The carried item.</param>
        /// <param name="targetId">A <c>ContentIds</c> id for the thing in the world.</param>
        /// <returns>What happened. <see cref="UseOutcome.Nothing"/> when the target is unknown.</returns>
        UseOutcome Use(string itemId, string targetId);
    }
}
