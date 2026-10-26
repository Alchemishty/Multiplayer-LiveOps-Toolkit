using System;
using System.Text.RegularExpressions;

namespace MultiplayerLiveOpsToolkit.Identity
{
    internal static class ProofWireFormat
    {
        private static readonly Regex IdentifierPattern = new Regex(@"\A[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Base64Pattern = new Regex(@"\A[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant);

        internal static bool IsIdentifier(string value) => value != null && IdentifierPattern.IsMatch(value);

        internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        internal static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || !Base64Pattern.IsMatch(encoded)) throw new FormatException("Invalid assertion encoding.");
            string padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            byte[] bytes = Convert.FromBase64String(padded);
            if (Encode(bytes) != encoded) throw new FormatException("Noncanonical assertion encoding.");
            return bytes;
        }
    }
}
