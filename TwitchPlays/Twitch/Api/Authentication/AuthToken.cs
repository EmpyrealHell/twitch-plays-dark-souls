using NLog;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;
using System.Net;
using TwitchPlays.Utils;

namespace TwitchPlays.Twitch.Api.Authentication
{
    /// <summary>
    /// This class provides methods for getting and refreshing oauth tokens.
    /// </summary>
    public class AuthToken
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Acquires a user access token using the oauth authorization code flow.
        /// </summary>
        /// <param name="clientId">The client id of your registered twitch app.</param>
        /// <param name="clientSecret">The client secret of your registered twitch app.</param>
        /// <param name="code">The auth code provided when the user logged in.</param>
        /// <param name="redirectUri">The redirect uri of your registered twitch app.</param>
        /// <returns>The API response containing an OAuth token.</returns>
        public static async Task<TokenResponse?> Fetch(string clientId, string clientSecret, string code, string redirectUri)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new RestClient("https://id.twitch.tv", configureSerialization: s => s.UseNewtonsoftJson(SerializerSettings.Default));
            var request = new RestRequest("oauth2/token", Method.Post);
            RestLogger.AddLogging(request, Logger, true);
            request.AddHeader("Accept", "application/json");
            request.AddQueryParameter("client_id", clientId);
            request.AddQueryParameter("client_secret", clientSecret);
            request.AddQueryParameter("code", code);
            request.AddQueryParameter("grant_type", "authorization_code");
            request.AddQueryParameter("redirect_uri", redirectUri);
            var response = await client.ExecuteAsync<TokenResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                if (response.Data != null)
                {
                    response.Data.ExpirationDate = DateTime.Now.AddSeconds(response.Data?.ExpiresIn ?? 0);
                    return response.Data;
                }
            }
            return null;
        }

        /// <summary>
        /// Validates a token to confirm it is still valid and active.
        /// </summary>
        /// <param name="token">The access token to validate.</param>
        /// <returns>The validation response.</returns>
        public static async Task<ValidationResponse?> Validate(string token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new RestClient("https://id.twitch.tv", configureSerialization: s => s.UseNewtonsoftJson(SerializerSettings.Default));
            var request = new RestRequest("oauth2/validate", Method.Get);
            RestLogger.AddLogging(request, Logger, true);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization", $"Bearer {token}");
            var response = await client.ExecuteAsync<ValidationResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }
            return null;
        }

        /// <summary>
        /// Refreshes an expired user access token.
        /// </summary>
        /// <param name="clientId">The client id of your registered twitch app.</param>
        /// <param name="clientSecret">The client secret of your registered twitch app.</param>
        /// <param name="refreshToken">The refresh token to renew.</param>
        /// <returns>The API containing a new OAuth token.</returns>
        public static async Task<RestResponse<TokenResponse>> Refresh(string clientId, string clientSecret, string refreshToken)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new RestClient("https://id.twitch.tv", configureSerialization: s => s.UseNewtonsoftJson(SerializerSettings.Default));
            var request = new RestRequest("oauth2/token", Method.Post);
            RestLogger.AddLogging(request, Logger, true);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("grant_type", "refresh_token");
            request.AddParameter("refresh_token", refreshToken);
            request.AddParameter("client_id", clientId);
            request.AddParameter("client_secret", clientSecret);
            var response = await client.ExecuteAsync<TokenResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                if (response.Data != null)
                {
                    response.Data.ExpirationDate = DateTime.Now.AddSeconds(response.Data.ExpiresIn);
                }
            }
            return response;
        }
    }
}
