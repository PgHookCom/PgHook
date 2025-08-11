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
            var webHookUrl = _cfg.GetValue<string>("PGH_WEBHOOK_URL");
            if (string.IsNullOrWhiteSpace(webHookUrl))
            {
                _logger.LogCritical("PGH_WEBHOOK_URL is not set");
                return;
            }

            var webHookSecret = _cfg.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

            var webHookTimeoutSec = _cfg.GetValue<int>("PGH_WEBHOOK_TIMEOUT_SEC");

            var connString = _cfg.GetValue<string>("PGH_POSTGRES_CONN");
            if (string.IsNullOrWhiteSpace(connString))
            {
                _logger.LogCritical("PGH_POSTGRES_CONN is not set");
                return;
            }
            
            var publicationNames = _cfg.GetValue<string>("PGH_PUBLICATION_NAMES");
            if (string.IsNullOrWhiteSpace(publicationNames))
            {
                _logger.LogCritical("PGH_PUBLICATION_NAMES is not set");
                return;
            }
            
            var usePermanentSlot = _cfg.GetValue<bool>("PGH_USE_PERMANENT_SLOT");

            var replicationSlot = _cfg.GetValue<string>("PGH_REPLICATION_SLOT");
            if (string.IsNullOrWhiteSpace(replicationSlot))
            {
                if (usePermanentSlot)
                {
                    _logger.LogCritical("PGH_REPLICATION_SLOT is not set");
                    return;
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

            var jsonCompact = _cfg.GetValue<bool>("PGH_JSON_COMPACT");

            var publications = publicationNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();

            var httpClient = new HttpClient();

            if (webHookTimeoutSec > 0)
            {
                httpClient.Timeout = TimeSpan.FromSeconds(webHookTimeoutSec);
            }

            using var pgOutput2Json = PgOutput2JsonBuilder.Create()
                .WithLoggerFactory(_loggerFactory)
                .WithPgConnectionString(connString)
                .WithPgPublications(publicationNames)
                .WithPgReplicationSlot(replicationSlot, useTemporarySlot: !usePermanentSlot)
                .WithBatchSize(batchSize)
                .WithMessagePublisherFactory(new WebHookPublisherFactory(httpClient, webHookUrl, webHookSecret))
                .WithJsonOptions(options =>
                {
                    options.WriteTableNames = true;
                    options.WriteMode = jsonCompact ? JsonWriteMode.Compact : JsonWriteMode.Default;
                })
                .Build();

            await pgOutput2Json.StartAsync(stoppingToken);
        }
    }
}
