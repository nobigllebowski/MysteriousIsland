using System.Text;

namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// Standard CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) over bytes or UTF-8 text.
    /// </summary>
    /// <remarks>
    /// WHY CRC-32 and not a cryptographic hash: this guards <c>SaveDocument.Checksum</c> against a
    /// TRUNCATED OR PARTIALLY FLUSHED FILE — the realistic failure on mobile, where the OS kills the app
    /// mid-write or storage fills up. CRC-32 detects every burst error shorter than 33 bits and is fast
    /// enough to run on the whole document without stalling the save. It is NOT tamper resistance: a
    /// player editing their own save can trivially recompute it, and that is deliberately not a threat
    /// this game defends against.
    /// <para>
    /// WHY the namespace is Core.Save while the file sits under Utilities/: the type is part of the save
    /// contract and is named as such by <c>SaveDocument</c> and <c>SaveMigrator</c>. Unity does not tie
    /// namespaces to folders, and the folder groups it with the other general-purpose helpers.
    /// </para>
    /// <para>
    /// Table-driven: the 256-entry lookup table is built once in the static constructor, costing 1 KB of
    /// managed memory to turn eight shift-and-test steps per byte into one.
    /// </para>
    /// </remarks>
    public static class Crc32
    {
        /// <summary>The reflected form of the IEEE 802.3 polynomial 0x04C11DB7.</summary>
        private const uint ReflectedPolynomial = 0xEDB88320u;

        /// <summary>All-ones seed, per the standard, so leading zero bytes still affect the result.</summary>
        private const uint InitialRegister = 0xFFFFFFFFu;

        private static readonly uint[] Table = BuildTable();

        /// <summary>
        /// Computes the CRC-32 of <paramref name="s"/> encoded as UTF-8 without a byte order mark.
        /// </summary>
        /// <remarks>
        /// The encoding is pinned to UTF-8 because the save codec writes UTF-8; using the platform's
        /// default encoding would make a save written on one device fail verification on another.
        /// A null or empty string yields 0, matching the CRC of zero bytes.
        /// </remarks>
        public static uint Compute(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Compute((byte[])null);
            }

            // Allocates the encoded buffer. Acceptable: this runs once per save or load, never per frame.
            return Compute(Encoding.UTF8.GetBytes(s));
        }

        /// <summary>
        /// Computes the CRC-32 of <paramref name="b"/>. A null or empty array yields 0.
        /// </summary>
        public static uint Compute(byte[] b)
        {
            if (b == null)
            {
                return Compute(null, 0, 0);
            }

            return Compute(b, 0, b.Length);
        }

        /// <summary>
        /// Computes the CRC-32 of a range of <paramref name="b"/>, for callers that hold a pooled buffer
        /// larger than the payload and must not slice a copy out of it.
        /// </summary>
        /// <param name="offset">First byte to include. Out-of-range values are clamped to the array.</param>
        /// <param name="count">Number of bytes to include. Clamped so the range never runs past the end.</param>
        public static uint Compute(byte[] b, int offset, int count)
        {
            var register = InitialRegister;

            if (b != null && count > 0)
            {
                var start = offset < 0 ? 0 : offset;
                if (start < b.Length)
                {
                    var available = b.Length - start;
                    var length = count > available ? available : count;

                    for (var i = 0; i < length; i++)
                    {
                        var index = (register ^ b[start + i]) & 0xFFu;
                        register = Table[index] ^ (register >> 8);
                    }
                }
            }

            // Final inversion, per the standard: without it, appending zero bytes would not change the CRC.
            return register ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// Precomputes the remainder for every possible byte value, folding eight bit steps into one
        /// table lookup per input byte.
        /// </summary>
        private static uint[] BuildTable()
        {
            var table = new uint[256];

            for (var i = 0u; i < 256u; i++)
            {
                var entry = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    // Reflected algorithm: the low bit leaves the register first, so the XOR is on a shift right.
                    entry = (entry & 1u) != 0
                        ? ReflectedPolynomial ^ (entry >> 1)
                        : entry >> 1;
                }

                table[i] = entry;
            }

            return table;
        }
    }
}
