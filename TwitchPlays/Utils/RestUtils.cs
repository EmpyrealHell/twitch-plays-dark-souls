using NLog;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;
using System.Net;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Twitch.Api.Client;

namespace TwitchPlays.Utils
{
    public class RestUtils
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Executes a rest request. If the response indicates the access token
        /// has expired, a refresh is attempted. If the refresh is successful,
        /// the request is executed again and the response is returned. If the
        /// refresh fails, the original request failure is returned.
        /// </summary>
        /// <typeparam name="T">The expected response type.</typeparam>
        /// <param name="tokenResponse">The token data used in the request.</param>
        /// <param name="clientData">The client information used in the request.</param>
        /// <param name="client">The rest client to use when executing the request.</param>
        /// <param name="request">The request object to execute against the client.</param>
        /// <returns>The response of the rest request. The response content
        /// will be deserialized into type T.</returns>
        public static async Task<RestResponse<T>> ExecuteWithRefresh<T>(TokenResponse tokenResponse, ClientData clientData, RestClient client, RestRequest request) where T : class
        {
            var response = await client.ExecuteAsync<T>(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Warn($"Unauthorized response calling Twitch API. Refreshing token.");
                var authResponse = await AuthToken.Refresh(clientData.ClientId, clientData.ClientSecret, tokenResponse.RefreshToken);
                if (authResponse == null || authResponse.StatusCode != HttpStatusCode.OK)
                {
                    Logger.Error("Token refresh failed. Something may be wrong with the access token, please delete token.json and relaunch the application.");
                    return response;
                }
                if (authResponse.Data != null)
                {
                    tokenResponse.CopyFrom(authResponse.Data);
                }
                request.AddOrUpdateHeader("Authorization", $"Bearer {tokenResponse.AccessToken}");
                response = await client.ExecuteAsync<T>(request);
            }
            return response;
        }

        /// <summary>
        /// Executes a rest request. If the response indicates the access token
        /// has expired, a refresh is attempted. If the refresh is successful,
        /// the request is executed again and the response is returned. If the
        /// refresh fails, the original request failure is returned.
        /// </summary>
        /// <typeparam name="T">The expected response type.</typeparam>
        /// <param name="tokenResponse">The token data used in the request.</param>
        /// <param name="clientData">The client information used in the request.</param>
        /// <param name="client">The rest client to use when executing the request.</param>
        /// <param name="request">The request object to execute against the client.</param>
        /// <returns>The response of the rest request. The response content
        /// will not be deserialized. Use this if no response body is expected.</returns>
        public static async Task<RestResponse> ExecuteWithRefresh(TokenResponse tokenResponse, ClientData clientData, RestClient client, RestRequest request)
        {
            var response = await client.ExecuteAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Warn($"Unauthorized response calling Twitch API. Refreshing token.");
                var authResponse = await AuthToken.Refresh(clientData.ClientId, clientData.ClientSecret, tokenResponse.RefreshToken);
                if (authResponse == null || authResponse.StatusCode != HttpStatusCode.OK)
                {
                    Logger.Error("Token refresh failed. Something may be wrong with the access token, please delete token.json and relaunch the application.");
                    return response;
                }
                if (authResponse.Data != null)
                {
                    tokenResponse.CopyFrom(authResponse.Data);
                }
                request.AddOrUpdateHeader("Authorization", $"Bearer {tokenResponse.AccessToken}");
                response = await client.ExecuteAsync(request);
            }
            return response;
        }

        /// <summary>
        /// Creates a standard rest client for use when making rest requests to
        /// the Twitch API.
        /// </summary>
        /// <returns>A rest client configured for the Twitch API.</returns>
        public static RestClient CreateStandardClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new RestClient("https://api.twitch.tv", configureSerialization: s => s.UseNewtonsoftJson(SerializerSettings.Default));
            return client;
        }

        /// <summary>
        /// Creates a standard rest request for use when making requests to the
        /// Twitch API.
        /// </summary>
        /// <param name="endpoint">The url endpoint to call.</param>
        /// <param name="method">The method of the request.</param>
        /// <param name="token">An OAuth token to authenticate the request.</param>
        /// <param name="clientId">The client id of the app making the call.</param>
        /// <param name="logger">An NLog instance to write log data to.</param>
        /// <returns>A rest request configured for the Twitch API.</returns>
        public static RestRequest CreateStandardRequest(string endpoint, Method method, string token, string clientId, Logger logger)
        {
            var request = new RestRequest(endpoint, method);
            RestLogger.AddLogging(request, logger);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Client-ID", clientId);
            return request;
        }
    }
}
