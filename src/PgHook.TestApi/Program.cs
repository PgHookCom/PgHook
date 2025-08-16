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
        
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            var app = builder.Build();

            var secret = app.Configuration.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

            var webhookVerification = new WebhookVerification(secret, timestampToleranceInSec: 5);

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

                var verified = webhookVerification.Verify(body, msgId, timestamp, signature, out var verificationError);

                Console.WriteLine("Verification: " + (verified ? "OK" : verificationError));

                Console.WriteLine(body);

                return Results.Ok(body);
            });


            app.Run();
        }

        
    }
}
