using PgOutput2Json;
using System.Security.Cryptography;
using System.Text;

namespace PgHook
{
    internal class WebHookPublisher : IMessagePublisher
    {
        private static readonly TimeSpan[] _retryDelays = 
        [ 
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
        ];

        private readonly HttpClient _httpClient;
        private readonly string _webHookUrl;

        // used for creating github-like signature
        private readonly string _webHookSecret;
        private readonly ILogger<WebHookPublisher>? _logger;
        private readonly List<string> _changes;

        public WebHookPublisher(HttpClient httpClient, string webHookUrl, string webHookSecret, int batchSize, ILogger<WebHookPublisher>? logger)
        {
            _httpClient = httpClient;
            _webHookUrl = webHookUrl;
            _webHookSecret = webHookSecret;
            _logger = logger;
            _changes = new(batchSize);
        }

        public async Task ConfirmAsync(CancellationToken token)
        {
            if (_changes.Count == 0)
                return;

            // Prepare JSON body
            var body = "[" + string.Join(",", _changes) + "]";


            // Sign the payload
            string signature;

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webHookSecret ?? "")))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
                signature = "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            var attempt = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");

                    content.Headers.Add("X-Hub-Signature-256", signature);

                    var response = await _httpClient.PostAsync(_webHookUrl, content, token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(token);

                        _logger?.LogWarning("[PgHook] Webhook returned {StatusCode}: {Request} {Response}", 
                            response.StatusCode, 
                            body,
                            responseBody);
                    }

                    response.EnsureSuccessStatusCode();

                    // Success — clear and return
                    _changes.Clear();
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt >= _retryDelays.Length) throw;

                    var delay = _retryDelays[attempt];

                    attempt++;

                    _logger?.LogError(ex, "[PgHook] attempt {Attempt} failed. Retrying in {Seconds} seconds", attempt, delay.TotalSeconds);

                    await Task.Delay(delay, token);
                }
            }
        }

        public Task<ulong> GetLastPublishedWalSeqAsync(CancellationToken token)
        {
            return Task.FromResult(0UL); // no de-duplication
        }

        public Task PublishAsync(JsonMessage jsonMessage, CancellationToken token)
        {
            // jsonMessage is reused by the caller so we must copy the values
            _changes.Add(jsonMessage.Json.ToString());

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
