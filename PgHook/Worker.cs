using PgOutput2Json;

namespace PgHook
{
    public class Worker : BackgroundService
    {
        private readonly IConfiguration _cfg;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Worker> _logger;

        public Worker(IConfiguration cfg, ILoggerFactory loggerFactory, ILogger<Worker> logger)
        {
            _cfg = cfg;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WebHookPublisherOptions options;
            try
            {
                options = new WebHookPublisherOptions(_cfg);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Invalid PgHook Configuration");
                return;
            }

            var httpClient = CreateHttpClient(options);

            using var pgOutput2Json = PgOutput2JsonBuilder.Create()
                .WithLoggerFactory(_loggerFactory)
                .WithPgConnectionString(options.ConnectionString)
                .WithPgPublications(options.PublicationNames)
                .WithPgReplicationSlot(options.ReplicationSlot, useTemporarySlot: !options.UsePermanentSlot)
                .WithBatchSize(options.BatchSize)
                .WithMessagePublisherFactory(new WebHookPublisherFactory(httpClient, options.WebHookUrl, options.WebHookSecret))
                .WithJsonOptions(jsonOptions =>
                {
                    jsonOptions.WriteTableNames = true;
                    jsonOptions.WriteMode = options.UseCompactJson ? JsonWriteMode.Compact : JsonWriteMode.Default;
                })
                .Build();

            await pgOutput2Json.StartAsync(stoppingToken);

            httpClient.Dispose();
        }

        private static HttpClient CreateHttpClient(WebHookPublisherOptions options)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = options.WebHookPooledConnectionLifetime,
                PooledConnectionIdleTimeout = options.WebHookPooledConnectionIdleTimeout,

                ConnectTimeout = options.WebHookConnectTimeout,

                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                KeepAlivePingDelay = options.WebHookKeepAliveDelay,
                KeepAlivePingTimeout = options.WebHookKeepAliveTimeout
            };

            return new HttpClient(handler)
            {
                Timeout = options.WebHookTimeout
            };
        }
    }
}
