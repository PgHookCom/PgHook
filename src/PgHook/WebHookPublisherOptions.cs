namespace PgHook
{
    internal class WebHookPublisherOptions
    {
        public string WebHookUrl { get; private set; }
        public string WebHookSecret { get; private set; }

        public TimeSpan WebHookTimeout { get; private set; }
        public TimeSpan WebHookConnectTimeout { get; private set; }
        public TimeSpan WebHookKeepAliveDelay { get; private set; }
        public TimeSpan WebHookKeepAliveTimeout { get; private set; }
        public TimeSpan WebHookPooledConnectionLifetime { get; private set; }
        public TimeSpan WebHookPooledConnectionIdleTimeout { get; private set; }

        public string ConnectionString { get; private set; }
        public string[] PublicationNames { get; private set; }

        public string ReplicationSlot { get; private set; }
        public bool UsePermanentSlot { get; private set; }

        public int BatchSize { get; set; }
        public bool UseCompactJson { get; set; }

        public WebHookPublisherOptions(IConfiguration cfg)
        {
            WebHookUrl = cfg.GetValue<string>("PGH_WEBHOOK_URL") ?? "";
            if (string.IsNullOrWhiteSpace(WebHookUrl))
            {
                throw new Exception("PGH_WEBHOOK_URL is not set");
            }

            WebHookSecret = cfg.GetValue<string>("PGH_WEBHOOK_SECRET") ?? "";

            WebHookTimeout = GetTimeSpan(cfg, "PGH_WEBHOOK_TIMEOUT_SEC", 30);
            WebHookConnectTimeout = GetTimeSpan(cfg, "PGH_WEBHOOK_CONNECT_TIMEOUT_SEC", 10);

            WebHookKeepAliveDelay = GetTimeSpan(cfg, "PGH_WEBHOOK_KEEPALIVE_DELAY_SEC", 60);
            WebHookKeepAliveTimeout = GetTimeSpan(cfg, "PGH_WEBHOOK_KEEPALIVE_TIMEOUT_SEC", 10);

            WebHookPooledConnectionLifetime = GetTimeSpan(cfg, "PGH_WEBHOOK_POOLED_CONNECTION_LIFETIME_SEC", 10 * 60);
            WebHookPooledConnectionIdleTimeout = GetTimeSpan(cfg, "PGH_WEBHOOK_POOLED_CONNECTION_IDLE_TIMEOUT_SEC", 2 * 60);

            ConnectionString = cfg.GetValue<string>("PGH_POSTGRES_CONN") ?? "";
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new Exception("PGH_POSTGRES_CONN is not set");
            }
            
            var publicationNames = cfg.GetValue<string>("PGH_PUBLICATION_NAMES") ?? "";
            if (string.IsNullOrWhiteSpace(publicationNames))
            {
                throw new Exception("PGH_PUBLICATION_NAMES is not set");
            }

            PublicationNames = [.. publicationNames.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())];

            UsePermanentSlot = cfg.GetValue<bool>("PGH_USE_PERMANENT_SLOT");

            ReplicationSlot = cfg.GetValue<string>("PGH_REPLICATION_SLOT") ?? "";
            if (string.IsNullOrWhiteSpace(ReplicationSlot))
            {
                if (UsePermanentSlot)
                {
                    throw new Exception("PGH_REPLICATION_SLOT is not set");
                }
                else
                {
                    ReplicationSlot = $"pghook_{Guid.NewGuid().ToString().Replace("-", "")}";
                }
            }

            BatchSize = cfg.GetValue<int>("PGH_BATCH_SIZE");
            if (BatchSize < 1)
            {
                BatchSize = 100;
            }

            UseCompactJson = cfg.GetValue<bool>("PGH_JSON_COMPACT");
        }

        private static TimeSpan GetTimeSpan(IConfiguration cfg, string name, int defaultSec)
        {
            var sec = cfg.GetValue<int>(name);
            return TimeSpan.FromSeconds(sec >= 1 ? sec : defaultSec);
        }
    }
}
