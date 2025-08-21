using System.Diagnostics;
using System.Net;
using System.Text;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Twitch.Api.Client;
using TwitchPlays.Utils;

namespace TwitchPlays.Twitch
{
    public class AuthCallback
    {
        public static readonly IEnumerable<string> Scopes = [.. new string[] { "chat:read" }];
        public static readonly string RedirectUri = "http://localhost:9000/";
        private readonly string ResponseTemplate = "<html><body><h3>{0}</h3><p>{1}</p></body></html>";
        protected readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
        protected DateTime AuthStart = DateTime.Now;
        public string State { get; private set; } = Guid.NewGuid().ToString();

        private string BuildAuthUrl(string? clientId, IEnumerable<string> scopes)
        {
            var builder = new UriBuilder("https", "id.twitch.tv")
            {
                Path = "oauth2/authorize"
            };
            AddQuery(builder, "client_id", clientId ?? string.Empty);
            AddQuery(builder, "redirect_uri", RedirectUri);
            AddQuery(builder, "response_type", "code");
            AddQuery(builder, "scope", string.Join(" ", scopes));
            AddQuery(builder, "force_verify", "true");
            AddQuery(builder, "state", State);
            return builder.Uri.ToString();
        }

        private static void AddQuery(UriBuilder builder, string rawKey, string rawValue)
        {
            var key = Uri.EscapeDataString(rawKey);
            var value = Uri.EscapeDataString(rawValue);
            if (!string.IsNullOrWhiteSpace(builder.Query))
            {
                builder.Query = builder.Query[1..] + "&" + key + "=" + value;
            }
            else
            {
                builder.Query = key + "=" + value;
            }
        }

        private async Task SendResponse(HttpListenerResponse response, string header, string body)
        {
            var outputStream = response.OutputStream;
            var toSend = string.Format(ResponseTemplate, header, body);
            var bytes = Encoding.UTF8.GetBytes(toSend);
            response.ContentLength64 = bytes.Length;
            response.ContentType = "text/html";
            response.StatusCode = (int)HttpStatusCode.OK;
            await outputStream.WriteAsync(bytes);
            await outputStream.FlushAsync();
        }

        private async Task<string> ProcessResponse(HttpListenerResponse response, Dictionary<string, string> query, string expectedState)
        {
            var header = "Error!";
            var body = "Unexpected error.";
            var code = string.Empty;
            if (query.TryGetValue("error", out var error))
            {
                if (query.TryGetValue("error_description", out var errorDescription))
                {
                    body = $"{error}: {errorDescription}. Close this window and try again.";
                }
                else
                {
                    body = $"{error}. Close this window and try again.";
                }
            }
            if (query.TryGetValue("state", out var state))
            {
                if (!state.Equals(expectedState))
                {
                    body = "CSRF attack detected. Check your firewall settings.";
                }
                if (query.TryGetValue("code", out var requestCode))
                {
                    header = "Authentication complete!";
                    body = "You may now close this window.";
                    code = requestCode;
                }
            }
            await SendResponse(response, header, body);
            return code;
        }

        private async Task<string?> GetAuthCode(string url, string state)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            Process.Start(new ProcessStartInfo()
            {
                FileName = url,
                UseShellExecute = true
            });

            var contextTask = listener.GetContextAsync();
            if (contextTask.Wait(TimeSpan.FromMinutes(1)))
            {
                var context = contextTask.Result;
                var queryString = context.Request.QueryString;
                var queryDict = queryString.AllKeys.Select(x => x ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToDictionary(x => x, x => queryString.Get(x) ?? "");
                var code = await ProcessResponse(context.Response, queryDict, state);
                listener.Close();
                return code;
            }
            return null;
        }

        public async Task<TokenResponse?> GetChatAuthCode(ClientData clientData)
        {
            var code = await GetAuthCode(BuildAuthUrl(clientData.ClientId, Scopes), State);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return await AuthToken.Fetch(clientData.ClientId, clientData.ClientSecret, code, RedirectUri);
            }
            return null;
        }

        public static async Task<TokenData?> LoadTokens(ClientData clientData)
        {
            if (FileUtils.HasTokenData())
            {
                var tokenData = FileUtils.ReadTokenData();
                if (await ValidateAndRefresh(clientData, tokenData))
                {
                    return tokenData;
                }
            }
            return new TokenData();
        }

        public static void ClearTokens()
        {
            if (FileUtils.HasTokenData())
            {
                FileUtils.WriteTokenData(new TokenData());
            }
        }

        public static async Task<bool> ValidateAndRefresh(ClientData clientData, TokenData? tokenData)
        {
            if (tokenData != null)
            {
                RestLogger.SetSensitiveData(clientData, tokenData);
                if (tokenData.AuthToken != null)
                {
                    var validationResponse = await AuthToken.Validate(tokenData.AuthToken.AccessToken);
                    if (validationResponse == null)
                    {
                        var response = await AuthToken.Refresh(clientData.ClientId, clientData.ClientSecret, tokenData.AuthToken.RefreshToken);
                        tokenData.AuthToken = response.Data;
                        if (tokenData.AuthToken != null)
                        {
                            validationResponse = await AuthToken.Validate(tokenData.AuthToken.AccessToken);
                            tokenData.UserName = validationResponse?.Login ?? string.Empty;
                            tokenData.UserId = validationResponse?.UserId ?? string.Empty;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(tokenData.UserName) && string.IsNullOrWhiteSpace(tokenData.UserId) && !Scopes.Any(x => !validationResponse.Scopes.Contains(x)))
                    {
                        tokenData.UserName = validationResponse.Login;
                        tokenData.UserId = validationResponse.UserId;
                    }
                    else if (!validationResponse.Login.Equals(tokenData.UserName) || Scopes.Any(x => !validationResponse.Scopes.Contains(x)))
                    {
                        tokenData.AuthToken = null;
                    }
                }
                if (tokenData.AuthToken != null)
                {
                    FileUtils.WriteTokenData(tokenData);
                    return true;
                }
            }
            return false;
        }
    }
}
