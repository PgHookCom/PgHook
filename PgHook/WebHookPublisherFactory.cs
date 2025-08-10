using PgOutput2Json;

namespace PgHook
{
    internal class WebHookPublisherFactory : IMessagePublisherFactory
    {
        private readonly HttpClient _client;
        private readonly string _webHookUrl;
        private readonly string _webHookSecret;

        public WebHookPublisherFactory(HttpClient client, string webHookUrl, string webHookSecret)
        {
            _client = client;
            _webHookUrl = webHookUrl;
            _webHookSecret = webHookSecret;
        }

        public IMessagePublisher CreateMessagePublisher(ReplicationListenerOptions listenerOptions, ILoggerFactory? loggerFactory)
        {
            var logger = loggerFactory?.CreateLogger<WebHookPublisher>();

            return new WebHookPublisher(_client, _webHookUrl, _webHookSecret, listenerOptions.BatchSize, logger);
        }
    }
}
