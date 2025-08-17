using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace PgHook.TestApi
{
    public class WebhookVerificationStd
    {
        private static readonly UTF8Encoding _safeUTF8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private const string _standardKeyPrefix = "whsec_";

        private readonly byte[] _key;
        private readonly int _timestampToleranceInSec;

        public WebhookVerificationStd(string secret, int timestampToleranceInSec)
        {
            _key = GetKeyFromSecret(secret);
            _timestampToleranceInSec = timestampToleranceInSec;
        }

        private static byte[] GetKeyFromSecret(string secret)
        {
            if (secret.StartsWith(_standardKeyPrefix))
            {
                return Convert.FromBase64String(secret[_standardKeyPrefix.Length..]);
            }
            
            return _safeUTF8Encoding.GetBytes(secret);
        }

        public bool Verify(string payload, string msgId, string msgTimestamp, string msgSignature, 
            [NotNullWhen(false)] out string? error)
        {
            if (!VerifyTimestamp(msgTimestamp, out var timestamp))
            {
                error = "Timestamp verification failed";
                return false;
            }

            var signature = Sign(msgId, timestamp, payload, _key);
            var expectedSignature = signature.Split(',')[1];

            var passedSignatures = msgSignature.Split(' ');

            foreach (string versionedSignature in passedSignatures)
            {
                var parts = versionedSignature.Split(',');
                if (parts.Length < 2)
                {
                    error = "Invalid signature - missing version";
                    return false;
                }

                var version = parts[0];
                var passedSignature = parts[1];

                if (version != "v1")
                {
                    continue;
                }

                if (SecureCompare(expectedSignature, passedSignature))
                {
                    error = null;
                    return true;
                }
            }

            error = "Signature verification failed";
            return false;
        }

        private bool VerifyTimestamp(string timestampHeader, out DateTimeOffset timestamp)
        {
            var now = DateTimeOffset.UtcNow;

            var timestampInt = long.Parse(timestampHeader);
            timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampInt);

            if (timestamp < now.AddSeconds(-1 * _timestampToleranceInSec))
            {
                return false;
            }

            if (timestamp > now.AddSeconds(_timestampToleranceInSec))
            {
                return false;
            }

            return true;
        }

        public static string Sign(string msgId, DateTimeOffset timestamp, string payload, byte[] key)
        {
            var toSign = $"{msgId}.{timestamp.ToUnixTimeSeconds()}.{payload}";
            var toSignBytes = _safeUTF8Encoding.GetBytes(toSign);

            using var hmac = new HMACSHA256(key);

            var hash = hmac.ComputeHash(toSignBytes);
            var signature = Convert.ToBase64String(hash);

            return $"v1,{signature}";
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        public static bool SecureCompare(string a, string b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            var result = 0;
            for (var i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }

            return result == 0;
        }
    }
}
