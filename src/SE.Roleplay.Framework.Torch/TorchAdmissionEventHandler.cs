using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Torch.API.Event;
using Torch.Server.Managers;
using VRage.Network;

namespace SE.Roleplay.Framework.Torch
{
    /// <summary>
    /// Applies the directory's authoritative allowlist decision during Torch's
    /// dedicated-server authentication callback, before the connection is accepted.
    /// </summary>
    internal sealed class TorchAdmissionEventHandler : IEventHandler, IDisposable
    {
        private readonly HttpClient? _httpClient;
        private readonly Uri? _decisionUri;
        private readonly string? _apiKey;
        private readonly IFrameworkLogger _logger;
        private readonly bool _rejectAll;

        public TorchAdmissionEventHandler(
            Uri directoryBaseUri,
            string serverId,
            string apiKey,
            IFrameworkLogger logger)
        {
            if (directoryBaseUri == null) throw new ArgumentNullException(nameof(directoryBaseUri));
            if (directoryBaseUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("The directory endpoint must use HTTPS.", nameof(directoryBaseUri));
            if (string.IsNullOrWhiteSpace(serverId)) throw new ArgumentException("A server ID is required.", nameof(serverId));
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("A server API key is required.", nameof(apiKey));

            _decisionUri = new Uri(directoryBaseUri,
                "api/servers/" + Uri.EscapeDataString(serverId) + "/admission/");
            _apiKey = apiKey;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        /// <summary>Creates a fail-closed handler for invalid or incomplete server configuration.</summary>
        public TorchAdmissionEventHandler(IFrameworkLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rejectAll = true;
        }

        [EventHandler]
        public void OnValidateAuthTicket(ref ValidateAuthTicketEvent info)
        {
            if (_rejectAll)
            {
                info.FutureVerdict = Task.FromResult(JoinResult.TicketCanceled);
                return;
            }

            var previousVerdict = info.FutureVerdict;
            info.FutureVerdict = EvaluateAsync(previousVerdict, info.SteamID);
        }

        private async Task<JoinResult> EvaluateAsync(Task<JoinResult> previousVerdict, ulong steamId)
        {
            try
            {
                if (previousVerdict == null)
                    return JoinResult.TicketCanceled;

                var defaultVerdict = await previousVerdict.ConfigureAwait(false);
                if (defaultVerdict != JoinResult.OK)
                    return defaultVerdict;

                var uri = new Uri(_decisionUri!, steamId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    request.Headers.TryAddWithoutValidation("X-Server-Api-Key", _apiKey!);
                    using (var response = await _httpClient!.SendAsync(request).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn("Directory admission check failed with HTTP " + (int)response.StatusCode +
                                "; rejecting the pending connection because enforcement is enabled.");
                            return JoinResult.TicketCanceled;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var decision = JsonConvert.DeserializeObject<AdmissionDecisionResponse>(json);
                        if (decision == null)
                        {
                            _logger.Warn("Directory admission response was empty or invalid; rejecting the pending connection.");
                            return JoinResult.TicketCanceled;
                        }

                        if (!decision.ShouldAdmit)
                        {
                            _logger.Info("Directory allowlist denied Steam account " + steamId + ".");
                            return JoinResult.NotInGroup;
                        }

                        return JoinResult.OK;
                    }
                }
            }
            catch (Exception exception)
            {
                // Do not log request headers, credentials, or exception objects that may contain request details.
                _logger.Warn("Directory admission check could not be completed (" +
                    exception.GetType().Name + "); rejecting the pending connection because enforcement is enabled.");
                return JoinResult.TicketCanceled;
            }
        }

        public void Dispose() => _httpClient?.Dispose();

        private sealed class AdmissionDecisionResponse
        {
            [JsonProperty("shouldAdmit")]
            public bool ShouldAdmit { get; set; }
        }
    }
}
