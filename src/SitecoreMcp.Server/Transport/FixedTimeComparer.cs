namespace SitecoreMcp.Server.Transport
{
    /// <summary>Constant-time string comparison, so a wrong API key cannot be recovered by timing.</summary>
    public static class FixedTimeComparer
    {
        /// <summary>
        /// Compares two strings in time that depends only on the first argument's length, never
        /// short-circuiting on the first differing character. Null arguments never match.
        /// </summary>
        public static bool Equals(string expected, string actual)
        {
            if (expected == null || actual == null)
            {
                return false;
            }

            // Fold the length difference into the result instead of returning early, so callers
            // of different-length keys are not distinguishable from callers of wrong same-length keys.
            var difference = expected.Length ^ actual.Length;
            for (var i = 0; i < expected.Length; i++)
            {
                var actualChar = i < actual.Length ? actual[i] : 0;
                difference |= expected[i] ^ actualChar;
            }

            return difference == 0;
        }
    }
}
