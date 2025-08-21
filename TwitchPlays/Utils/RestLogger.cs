using NLog;
using RestSharp;
using RestSharp.Interceptors;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Twitch.Api.Client;

namespace TwitchPlays.Utils
{
    /// <summary>
    /// Utility class that writes details of http requests and responses to an
    /// NLog logger.
    /// </summary>
    public class RestLogger
    {
        private static readonly HashSet<string> ReplacementKeys = ["Authorization", "Client-ID"];

        private static ClientData ClientData = new();
        private static TokenData TokenData = new();

        /// <summary>
        /// Sets the client data and auth token data. These values are replaced
        /// with placeholders in the logs to prevent leaking of sensitive data.
        /// </summary>
        /// <param name="clientData">The client data object to mask.</param>
        /// <param name="tokenData">The token data object to mask.</param>
        public static void SetSensitiveData(ClientData clientData, TokenData tokenData)
        {
            ClientData = clientData;
            TokenData = tokenData;
        }

        /// <summary>
        /// Adds logging to a rest request object.
        /// </summary>
        /// <param name="request">The rest request object to configure.</param>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="maskExtra">Whether or not to mask extra security info.</param>
        public static void AddLogging(RestRequest request, Logger logger, bool maskExtra = false)
        {
            request.Interceptors =
            [
                new CompatibilityInterceptor()
                {
                    OnBeforeRequest = x =>
                    {
                        LogRequest(x, logger, maskExtra);
                        return default;
                    },
                    OnAfterRequest = x =>
                    {
                        LogResponse(x, logger, maskExtra);
                        return default;
                    }
                }
            ];
        }

        /// <summary>
        /// Writes the details of an http request to the specified logger.
        /// </summary>
        /// <param name="request">The request to log.</param>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="maskExtra">Whether or not to mask extra security info.</param>
        public static async void LogRequest(HttpRequestMessage request, Logger logger, bool maskExtra)
        {
            var diag = new System.Diagnostics.StackTrace(true);
            var frames = diag.GetFrames().Reverse().Where(x => !string.IsNullOrWhiteSpace(x.GetFileName()));
            logger.Debug("Http request from {trace}", string.Join(" -> ", frames.Select(x => $"{x.GetFileName()}:{x.GetFileLineNumber()}")));
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (maskExtra && uri.Contains('?'))
            {
                uri = string.Concat(uri.AsSpan(0, uri.IndexOf('?')), "{Auth Query Params}");
            }
            logger.Debug("HTTP{version} {method} {uri}", request.Version.ToString(), request.Method, uri);
            foreach (var header in request.Headers)
            {
                var value = header.Value.First();
                if (ReplacementKeys.Contains(header.Key))
                {
                    value = ClientData?.ClientId == null ? value : value.Replace(ClientData.ClientId, "{ClientId}");
                    value = TokenData?.AuthToken?.AccessToken == null ? value : value.Replace(TokenData.AuthToken.AccessToken, "{ChatToken}");
                }
                logger.Debug("{header}: {value}", header.Key, value);
            }
            if (request.Content != null)
            {
                var content = await request.Content.ReadAsStringAsync();
                if (maskExtra)
                {
                    content = ClientData?.ClientId == null ? content : content.Replace(ClientData.ClientId, "{ClientId}");
                    content = ClientData?.ClientSecret == null ? content : content.Replace(ClientData.ClientSecret, "{ClientSecret}");
                    content = TokenData?.AuthToken?.RefreshToken == null ? content : content.Replace(TokenData.AuthToken.RefreshToken, "{ChatRefreshToken}");
                }
                logger.Debug(content);
            }
            else
            {
                logger.Debug("No content.");
            }
        }

        /// <summary>
        /// Writes the details of an http response to the specified logger.
        /// </summary>
        /// <param name="response">The response to log.</param>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="maskExtra">Whether or not to mask extra security info.</param>
        public static async void LogResponse(HttpResponseMessage response, Logger logger, bool maskExtra)
        {
            logger.Debug("{responseCode} ({response}) {uri}", (int)response.StatusCode, response.StatusCode.ToString(), response.RequestMessage?.RequestUri);
            foreach (var header in response.Headers)
            {
                var value = header.Value.First();
                if (ReplacementKeys.Contains(header.Key))
                {
                    value = ClientData?.ClientId == null ? value : value.Replace(ClientData.ClientId, "{ClientId}");
                    value = TokenData?.AuthToken?.AccessToken == null ? value : value.Replace(TokenData.AuthToken.AccessToken, "{ChatToken}");
                }
                logger.Debug("{header}: {value}", header.Key, value);
            }
            if (response.Content != null)
            {
                if (maskExtra)
                {
                    logger.Debug("{Auth Content Masked}");
                }
                else
                {
                    logger.Debug(await response.Content.ReadAsStringAsync());
                }
            }
            else
            {
                logger.Debug("No content.");
            }
        }
    }
}
