using PgOutput2Json;

namespace PgHook
{
    public class Worker : BackgroundService
    {
        private readonly IConfiguration _cfg;
        private readonly ILoggerFactory _loggerFactory;

        public Worker(IConfiguration cfg, ILoggerFactory loggerFactory, ILogger<Worker> logger)
        {
            _cfg = cfg;
            _loggerFactory = loggerFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var webhookUrl = _cfg.GetValue<string>("PGH_WEBHOOK_URL") ?? "";
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                throw new Exception("PGH_WEBHOOK_URL is not set");
            }

            var connectionString = _cfg.GetValue<string>("PGH_POSTGRES_CONN") ?? "";
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new Exception("PGH_POSTGRES_CONN is not set");
            }

            var publicationNamesCfg = _cfg.GetValue<string>("PGH_PUBLICATION_NAMES") ?? "";
            if (string.IsNullOrWhiteSpace(publicationNamesCfg))
            {
                throw new Exception("PGH_PUBLICATION_NAMES is not set");
            }

            string[] publicationNames = [.. publicationNamesCfg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())];

            var usePermanentSlot = _cfg.GetValue<bool>("PGH_USE_PERMANENT_SLOT");

            var replicationSlot = _cfg.GetValue<string>("PGH_REPLICATION_SLOT") ?? "";
            if (string.IsNullOrWhiteSpace(replicationSlot))
            {
                if (usePermanentSlot)
                {
                    throw new Exception("PGH_REPLICATION_SLOT is not set");
                }
                else
                {
                    replicationSlot = $"pghook_{Guid.NewGuid().ToString().Replace("-", "")}";
                }
            }

            var batchSize = _cfg.GetValue<int>("PGH_BATCH_SIZE");
            if (batchSize < 1)
            {
                batchSize = 100;
            }

            var useCompactJson = _cfg.GetValue<bool>("PGH_JSON_COMPACT");

            using var pgOutput2Json = PgOutput2JsonBuilder.Create()
                .WithLoggerFactory(_loggerFactory)
                .WithPgConnectionString(connectionString)
                .WithPgPublications(publicationNames)
                .WithPgReplicationSlot(replicationSlot, useTemporarySlot: !usePermanentSlot)
                .WithBatchSize(batchSize)
                .WithJsonOptions(jsonOptions =>
                {
                    jsonOptions.WriteTableNames = true;
                    jsonOptions.WriteTimestamps = true;
                    jsonOptions.TimestampFormat = TimestampFormat.UnixTimeMilliseconds;
                    jsonOptions.WriteMode = useCompactJson ? JsonWriteMode.Compact : JsonWriteMode.Default;
                })
                .UseWebhook(webhookUrl, options =>
                {
                    options.WebhookSecret = _cfg.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

                    options.RequestTimeout = GetTimeSpan(_cfg, "PGH_WEBHOOK_TIMEOUT_SEC", 30);
                    options.ConnectTimeout = GetTimeSpan(_cfg, "PGH_WEBHOOK_CONNECT_TIMEOUT_SEC", 10);
                    options.KeepAliveDelay = GetTimeSpan(_cfg, "PGH_WEBHOOK_KEEPALIVE_DELAY_SEC", 60);
                    options.KeepAliveTimeout = GetTimeSpan(_cfg, "PGH_WEBHOOK_KEEPALIVE_TIMEOUT_SEC", 10);
                    options.PooledConnectionLifetime = GetTimeSpan(_cfg, "PGH_WEBHOOK_POOLED_CONNECTION_LIFETIME_SEC", 10 * 60);
                    options.PooledConnectionIdleTimeout = GetTimeSpan(_cfg, "PGH_WEBHOOK_POOLED_CONNECTION_IDLE_TIMEOUT_SEC", 2 * 60);    
                })
                .Build();

            await pgOutput2Json.StartAsync(stoppingToken);
        }

        private static TimeSpan GetTimeSpan(IConfiguration cfg, string name, int defaultSec)
        {
            var sec = cfg.GetValue<int>(name);
            return TimeSpan.FromSeconds(sec >= 1 ? sec : defaultSec);
        }
    }
}
