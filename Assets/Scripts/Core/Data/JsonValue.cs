// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Data/JsonValue.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Data; AsUInt32 added because save
// documents carry a CRC32 checksum that must survive a JSON round trip without going through double
// rounding. Tree shape, null-object behaviour and accessor names are otherwise unchanged.

using System.Collections.Generic;
using System.Globalization;

namespace ForgottenIsle.Core.Data
{
    /// <summary>
    /// The six shapes a <see cref="JsonValue"/> can take, mirroring the JSON specification exactly.
    /// </summary>
    public enum JsonType
    {
        /// <summary>Literal <c>null</c>, and also the value returned for any lookup that misses.</summary>
        Null,

        /// <summary>Literal <c>true</c> or <c>false</c>.</summary>
        Bool,

        /// <summary>A JSON number. Always held as a <see cref="double"/>; JSON has no integer type.</summary>
        Number,

        /// <summary>A JSON string, already unescaped.</summary>
        String,

        /// <summary>An ordered list of values.</summary>
        Array,

        /// <summary>An unordered set of name/value members.</summary>
        Object
    }

    /// <summary>
    /// Immutable JSON tree node. Missing members and out-of-range indices resolve to a shared Null node,
    /// so data readers can chain lookups and supply fallbacks without null checks.
    /// </summary>
    /// <remarks>
    /// WHY the null-object pattern rather than returning <c>null</c>: game data is authored by hand and is
    /// routinely incomplete during production. A reader written as
    /// <c>root["metadata"]["zoneId"].AsString(string.Empty)</c> must degrade to a default when any link in
    /// that chain is absent, not throw on a designer's half-finished file. The cost is that a typo in a key
    /// is silent, which is why every read site is expected to pass a deliberate fallback.
    /// </remarks>
    public sealed class JsonValue
    {
        /// <summary>
        /// The single shared null node. Reference-compare against it, or use <see cref="IsNull"/>; it is
        /// returned for literal nulls and for every failed lookup alike.
        /// </summary>
        public static readonly JsonValue Null = new JsonValue(JsonType.Null);

        private static readonly List<JsonValue> EmptyItems = new List<JsonValue>();
        private static readonly Dictionary<string, JsonValue> EmptyMembers = new Dictionary<string, JsonValue>();

        private readonly List<JsonValue> _items;
        private readonly Dictionary<string, JsonValue> _members;

        /// <summary>Which of the six JSON shapes this node holds.</summary>
        public JsonType Type { get; }

        /// <summary>Raw numeric payload. Meaningful only when <see cref="Type"/> is <see cref="JsonType.Number"/>.</summary>
        public double NumberValue { get; }

        /// <summary>Raw, already-unescaped text. Meaningful only when <see cref="Type"/> is <see cref="JsonType.String"/>.</summary>
        public string StringValue { get; }

        /// <summary>Raw boolean payload. Meaningful only when <see cref="Type"/> is <see cref="JsonType.Bool"/>.</summary>
        public bool BoolValue { get; }

        private JsonValue(JsonType type)
        {
            Type = type;
        }

        /// <summary>Creates a number node.</summary>
        public JsonValue(double number)
        {
            Type = JsonType.Number;
            NumberValue = number;
        }

        /// <summary>Creates a string node from already-unescaped text.</summary>
        public JsonValue(string text)
        {
            Type = JsonType.String;
            StringValue = text;
        }

        /// <summary>Creates a boolean node.</summary>
        public JsonValue(bool value)
        {
            Type = JsonType.Bool;
            BoolValue = value;
        }

        /// <summary>
        /// Creates an array node that takes ownership of <paramref name="items"/>.
        /// </summary>
        /// <remarks>
        /// The list is stored by reference, not copied: the parser builds each list exactly once and then
        /// hands it over, and copying would double the allocation cost of loading a catalog. Callers
        /// outside the parser must therefore not retain and mutate the list they pass in.
        /// </remarks>
        public JsonValue(List<JsonValue> items)
        {
            Type = JsonType.Array;
            _items = items;
        }

        /// <summary>
        /// Creates an object node that takes ownership of <paramref name="members"/>.
        /// See the array constructor for why the dictionary is not copied.
        /// </summary>
        public JsonValue(Dictionary<string, JsonValue> members)
        {
            Type = JsonType.Object;
            _members = members;
        }

        /// <summary>True for a literal null and for every lookup that found nothing.</summary>
        public bool IsNull => Type == JsonType.Null;

        /// <summary>True when this node has members.</summary>
        public bool IsObject => Type == JsonType.Object;

        /// <summary>True when this node has indexed items.</summary>
        public bool IsArray => Type == JsonType.Array;

        /// <summary>Item count for an array, member count for an object, zero for every scalar.</summary>
        public int Count => Type == JsonType.Array ? _items.Count : Type == JsonType.Object ? _members.Count : 0;

        /// <summary>
        /// Member lookup by name. Returns <see cref="Null"/> — never throws — when this node is not an
        /// object or has no such member, which is what makes chained reads safe.
        /// </summary>
        public JsonValue this[string key]
        {
            get
            {
                if (Type == JsonType.Object && key != null && _members.TryGetValue(key, out var value))
                {
                    return value;
                }

                return Null;
            }
        }

        /// <summary>
        /// Item lookup by index. Returns <see cref="Null"/> — never throws — when this node is not an
        /// array or the index is out of range.
        /// </summary>
        public JsonValue this[int index]
        {
            get
            {
                if (Type == JsonType.Array && index >= 0 && index < _items.Count)
                {
                    return _items[index];
                }

                return Null;
            }
        }

        /// <summary>
        /// True when this object node has a member with that exact name.
        /// </summary>
        /// <remarks>
        /// Distinguishes "absent" from "present but null", which the indexer deliberately cannot: both
        /// resolve to <see cref="Null"/> there. Migrations need that distinction to tell an old document
        /// missing a field from a new one that explicitly cleared it.
        /// </remarks>
        public bool Has(string key) => Type == JsonType.Object && key != null && _members.ContainsKey(key);

        /// <summary>Items of an array node, or an empty list for anything else. Never null.</summary>
        public IReadOnlyList<JsonValue> Items => Type == JsonType.Array ? _items : EmptyItems;

        /// <summary>Members of an object node, or an empty sequence for anything else. Never null.</summary>
        public IEnumerable<KeyValuePair<string, JsonValue>> Members => Type == JsonType.Object ? _members : EmptyMembers;

        /// <summary>Reads this node as text, or returns <paramref name="fallback"/> if it is not a string.</summary>
        public string AsString(string fallback = null) => Type == JsonType.String ? StringValue : fallback;

        /// <summary>
        /// Reads this node as a double.
        /// </summary>
        /// <remarks>
        /// A numeric string is also accepted, parsed invariantly. WHY: hand-authored data files and some
        /// export pipelines quote numbers, and failing a whole catalog load over a pair of quotes helps
        /// nobody. Invariant culture is mandatory — a device set to a comma-decimal locale must read the
        /// same file the same way.
        /// </remarks>
        public double AsDouble(double fallback = 0)
        {
            if (Type == JsonType.Number)
            {
                return NumberValue;
            }

            if (Type == JsonType.String && double.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        /// <summary>Reads this node as a float, via <see cref="AsDouble"/>.</summary>
        public float AsFloat(float fallback = 0) => (float)AsDouble(fallback);

        /// <summary>Reads this node as an int, truncating toward zero. Strings are not coerced here.</summary>
        public int AsInt(int fallback = 0) => Type == JsonType.Number ? (int)NumberValue : fallback;

        /// <summary>Reads this node as a long, truncating toward zero.</summary>
        public long AsLong(long fallback = 0) => Type == JsonType.Number ? (long)NumberValue : fallback;

        /// <summary>
        /// Reads this node as an unsigned 32-bit value, for example a CRC32 checksum.
        /// </summary>
        /// <remarks>
        /// NEW vs Nation. A checksum near <c>uint.MaxValue</c> survives a double round trip exactly — a
        /// double holds every integer below 2^53 — but a plain <c>(uint)NumberValue</c> cast on a negative
        /// or out-of-range number is undefined in an unchecked context and would silently produce a
        /// different checksum, turning a healthy save into a corrupt one. Range is therefore checked and
        /// anything outside it falls back instead of wrapping.
        /// </remarks>
        public uint AsUInt32(uint fallback = 0)
        {
            if (Type != JsonType.Number)
            {
                return fallback;
            }

            if (NumberValue < 0d || NumberValue > uint.MaxValue)
            {
                return fallback;
            }

            return (uint)NumberValue;
        }

        /// <summary>Reads this node as a bool. Non-bool nodes are not coerced.</summary>
        public bool AsBool(bool fallback = false) => Type == JsonType.Bool ? BoolValue : fallback;

        /// <summary>
        /// Reads an array node as a string array, with non-string items rendered as empty strings.
        /// Returns an empty array — never null — for any other node type.
        /// </summary>
        public string[] AsStringArray()
        {
            if (Type != JsonType.Array)
            {
                return new string[0];
            }

            var result = new string[_items.Count];
            for (var i = 0; i < _items.Count; i++)
            {
                result[i] = _items[i].AsString(string.Empty);
            }

            return result;
        }
    }
}
