using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PgHook.TestApi
{
    [JsonSerializable(typeof(List<string>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    public class Program
    {
        private static readonly UTF8Encoding _safeUTF8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private const string _standardKeyPrefix = "whsec_";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            var app = builder.Build();

            var secret = app.Configuration.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

            var key = GetKeyFromSecret(secret);

            var webHooks = app.MapGroup("/webhooks");

            webHooks.MapPost("/", async (HttpRequest req) =>
            {
                using var reader = new StreamReader(req.Body);

                var body = await reader.ReadToEndAsync();

                var msgId = req.Headers["webhook-id"].ToString();
                var timestamp = req.Headers["webhook-timestamp"].ToString();
                var signature = req.Headers["webhook-signature"].ToString();

                if (!string.IsNullOrEmpty(msgId)) Console.WriteLine($"webhook-id: {msgId}");
                if (!string.IsNullOrEmpty(timestamp)) Console.WriteLine($"webhook-timestamp: {timestamp}");
                if (!string.IsNullOrEmpty(signature)) Console.WriteLine($"webhook-signature: {signature}");

                var verified = Verify(body, msgId, timestamp, signature, key);

                Console.WriteLine("Verification: " + (verified ? "OK" : "ERR"));

                Console.WriteLine(body);

                return Results.Ok(body);
            });


            app.Run();
        }

        private static byte[] GetKeyFromSecret(string secret)
        {
            if (secret.StartsWith(_standardKeyPrefix))
            {
                return Convert.FromBase64String(secret[_standardKeyPrefix.Length..]);
            }
            
            return _safeUTF8Encoding.GetBytes(secret);
        }

        public static bool Verify(string payload, string msgId, string msgTimestamp, string msgSignature, byte[] key)
        {
            if (!VerifyTimestamp(msgTimestamp, 5, out var timestamp)) return false;

            var signature = Sign(msgId, timestamp, payload, key);
            var expectedSignature = signature.Split(',')[1];

            var passedSignatures = msgSignature.Split(' ');

            foreach (string versionedSignature in passedSignatures)
            {
                var parts = versionedSignature.Split(',');
                if (parts.Length < 2)
                {
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
                    return true;
                }
            }

            return false;
        }

        private static bool VerifyTimestamp(string timestampHeader, int tolearanceInSeconds, out DateTimeOffset timestamp)
        {
            var now = DateTimeOffset.UtcNow;

            var timestampInt = long.Parse(timestampHeader);
            timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampInt);

            if (timestamp < now.AddSeconds(-1 * tolearanceInSeconds))
            {
                return false;
            }

            if (timestamp > now.AddSeconds(tolearanceInSeconds))
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
