using System.Text.Json.Serialization;

namespace PgHook.TestApi
{
    [JsonSerializable(typeof(List<string>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    public class Program
    {
        
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            var app = builder.Build();

            var secret = app.Configuration.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

            var verification = new WebhookVerification(secret);
            var verificationStd = new WebhookVerificationStd(secret, timestampToleranceInSec: 5);

            var webHooks = app.MapGroup("/webhooks");

            webHooks.MapPost("/", async (HttpRequest req) =>
            {
                using var reader = new StreamReader(req.Body);

                foreach (var (key, val) in req.Headers)
                {
                    Console.WriteLine($"{key}: {val}");
                }

                var body = await reader.ReadToEndAsync();

                if (req.Headers.TryGetValue("webhook-signature", out var value))
                {
                    var signature = value.ToString();
                    var msgId = req.Headers["webhook-id"].ToString();
                    var timestamp = req.Headers["webhook-timestamp"].ToString();

                    var verified = verificationStd.Verify(body, msgId, timestamp, signature, out var verificationError);
                    Console.WriteLine("Verification Std: " + (verified ? "OK" : verificationError));
                }

                if (req.Headers.TryGetValue("X-Hub-Signature-256", out value))
                {
                    var signature = value.ToString();

                    var verified = verification.Verify(body, signature);
                    Console.WriteLine("Verification: " + (verified ? "OK" : "ERR"));
                }

                Console.Write("Content: ");
                Console.WriteLine(body);
                Console.WriteLine();

                return Results.Ok(body);
            });


            app.Run();
        }
    }
}
