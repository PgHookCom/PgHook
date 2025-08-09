using PgOutput2Json;

namespace PgHook
{
    internal class WebHookPublisherFactory : IMessagePublisherFactory
    {
        private readonly string _webHookUrl;
        private readonly string _webHookSecret;

        public WebHookPublisherFactory(string webHookUrl, string webHookSecret)
        {
            _webHookUrl = webHookUrl;
            _webHookSecret = webHookSecret;
        }

        public IMessagePublisher CreateMessagePublisher(ReplicationListenerOptions listenerOptions, ILoggerFactory? loggerFactory)
        {
            var logger = loggerFactory?.CreateLogger<WebHookPublisher>();

            return new WebHookPublisher(_webHookUrl, _webHookSecret, listenerOptions.BatchSize, logger);
        }
    }
}
