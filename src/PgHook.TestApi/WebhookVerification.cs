using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace PgHook.TestApi
{
    public class WebhookVerification
    {
        private static readonly UTF8Encoding _safeUTF8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private byte[] _key;

        public WebhookVerification(string secret)
        {
            _key = _safeUTF8Encoding.GetBytes(secret);
        }

        public bool Verify(string msgPayload, string msgSignature)
        {
            using var hmac = new HMACSHA256(_key);

            var hash = hmac.ComputeHash(_safeUTF8Encoding.GetBytes(msgPayload));
            var signature = "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return SecureCompare(signature, msgSignature);
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static bool SecureCompare(string a, string b)
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
