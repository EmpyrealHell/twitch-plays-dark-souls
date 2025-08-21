using RestSharp;
using System.Net;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Twitch.Api.Client;
using TwitchPlays.Utils;

namespace TwitchPlays.Twitch
{
    /// <summary>
    /// Client that provide access to common Twitch API endpoints.
    /// </summary>
    public class TwitchClient : ITwitchClient
    {
        private readonly ClientData ClientData;
        private readonly TokenData TokenData;

        public TwitchClient(ClientData clientData, TokenData tokenData)
        {
            ClientData = clientData;
            TokenData = tokenData;
        }

        /// <summary>
        /// Initializes properties that require database access.
        /// </summary>
        public void Initialize()
        {
        }

        private async Task<RestResponse<TokenResponse>?> RefreshToken(TokenResponse token)
        {
            if (token.ExpirationDate < DateTime.Now)
            {
                return await AuthToken.Refresh(ClientData.ClientId, ClientData.ClientSecret, token.RefreshToken);
            }
            return null;
        }

        /// <summary>
        /// Attempts to refresh the chat and broadcast tokens.
        /// </summary>
        /// <exception cref="Exception">Exception is thrown if the tokens fail to refresh.</exception>
        public async Task RefreshTokens()
        {
            var updated = false;
            if (TokenData.AuthToken != null)
            {
                var response = await RefreshToken(TokenData.AuthToken);
                if (response != null)
                {
                    if (response.StatusCode != HttpStatusCode.OK || response.Data == null)
                    {
                        if (response.ErrorException != null || response.ErrorMessage != null)
                        {
                            throw new Exception($"Encountered an exception trying to refresh the token for {TokenData.UserName}. {response.ErrorMessage}. {response.ErrorException}");
                        }
                        throw new Exception($"Encountered an unexpected response trying to refresh the token for {TokenData.UserName}. {response.StatusCode}: {response.Content}");
                    }
                    TokenData.AuthToken.CopyFrom(response.Data);
                    updated = true;
                }
                if (updated)
                {
                    FileUtils.WriteTokenData(TokenData);
                }
            }
        }
    }
}
