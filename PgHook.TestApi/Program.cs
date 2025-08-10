using System.Text.Json.Serialization;

namespace PgHook.TestApi
{
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

            var webHooks = app.MapGroup("/webhooks");

            webHooks.MapPost("/", async (HttpRequest req) =>
            {
                using var reader = new StreamReader(req.Body);

                var body = await reader.ReadToEndAsync();

                Console.WriteLine(body);

                return Results.Ok(body);
            });


            app.Run();
        }
    }

    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    [JsonSerializable(typeof(Todo[]))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
