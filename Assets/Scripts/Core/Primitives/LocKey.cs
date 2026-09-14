using System;

namespace ForgottenIsle.Core.Primitives
{
    /// <summary>
    /// A localization table key, for example <c>ui.menu.continue</c>.
    /// </summary>
    /// <remarks>
    /// WHY a struct instead of a bare string: the project bans user-facing string literals, and a distinct
    /// type makes that ban mechanically visible — a method taking a <see cref="LocKey"/> cannot be handed
    /// display text by accident, and code review can spot a raw string reaching the UI immediately. It is a
    /// readonly struct so passing keys around costs no allocation on the UI's per-frame paths.
    /// </remarks>
    public readonly struct LocKey : IEquatable<LocKey>
    {
        /// <summary>The raw key text. Never null for a constructed key; may be null only for <c>default(LocKey)</c>.</summary>
        public readonly string Value;

        /// <summary>The absent key. Resolving it is a caller error; check <see cref="IsEmpty"/> first.</summary>
        public static readonly LocKey Empty = new LocKey(string.Empty);

        /// <param name="value">
        /// Key text. A null is normalised to <see cref="string.Empty"/> so downstream lookups and hashing
        /// never have to null-check; an empty key is then indistinguishable from <see cref="Empty"/>.
        /// </param>
        public LocKey(string value)
        {
            Value = value ?? string.Empty;
        }

        /// <summary>True when this key names nothing and must not be sent to <c>ILocalizedText</c>.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>
        /// Returns the raw key text, never null.
        /// </summary>
        /// <remarks>
        /// This is the KEY, not the translation. It appears only in logs and in the <c>#key#</c> fallback
        /// that <c>StringTableLocalization</c> emits for a missing entry.
        /// </remarks>
        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        /// <summary>
        /// Ordinal comparison. WHY ordinal: keys are authored identifiers, not prose, so culture-aware
        /// casing rules (the Turkish dotless i in particular) must never change which row is matched.
        /// </summary>
        public bool Equals(LocKey other)
        {
            return string.Equals(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is LocKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Value ?? string.Empty).GetHashCode();
        }

        public static bool operator ==(LocKey left, LocKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LocKey left, LocKey right)
        {
            return !left.Equals(right);
        }
    }
}
