using NLog;
using TwitchPlays.Twitch;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Twitch.Api.Client;
using TwitchPlays.Utils;

namespace TwitchPlays
{
    internal class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);
        private static readonly int RetryLimit = 10;

        private static void ConfigureLogger()
        {
            LogManager.Setup().LoadConfiguration(builder =>
            {
                builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole(layout: "${time}|${level:uppercase=true}|${message:withexception=true}");
                builder.ForLogger().FilterMinLevel(LogLevel.Debug)
                    .WriteToFile(fileName: "output.log",
                    archiveAboveSize: 1024 * 1024 * 8,
                    maxArchiveFiles: 10);
            });
        }

        private static string MaskedReadLine()
        {
            var left = Console.CursorLeft;
            var top = Console.CursorTop;
            var value = Console.ReadLine() ?? string.Empty;
            Console.SetCursorPosition(left, top);
            Console.WriteLine(new string('*', value.Length));
            return value;
        }

        private static ClientData LoadClientData()
        {
            var clientData = FileUtils.HasClientData() ? FileUtils.ReadClientData() ?? new() : new();
            if (string.IsNullOrWhiteSpace(clientData.ClientId))
            {
                do
                {
                    Logger.Error("Client id missing or invalid. Enter client id:");
                    clientData.ClientId = MaskedReadLine();
                }
                while (string.IsNullOrWhiteSpace(clientData.ClientId));
                FileUtils.WriteClientData(clientData);
            }
            if (string.IsNullOrWhiteSpace(clientData.ClientSecret))
            {
                do
                {
                    Logger.Error("Client secret missing or invalid. Enter client secret:");
                    clientData.ClientSecret = MaskedReadLine();
                }
                while (string.IsNullOrWhiteSpace(clientData.ClientSecret));
                FileUtils.WriteClientData(clientData);
            }
            return clientData;
        }

        private static async Task<TokenData?> Authenticate(ClientData clientData)
        {
            var authCallback = new AuthCallback();
            if (!clientData.RedirectUri.Equals(AuthCallback.RedirectUri))
            {
                Logger.Warn("The redirect URI saved in your client data is outdated. Please make sure your registered Twitch application has the new redirect URI (http://localhost:9000/) listed as one of its OAuth Redirect URLs before continuing...");
                clientData.RedirectUri = AuthCallback.RedirectUri;
                FileUtils.WriteClientData(clientData);
                AuthCallback.ClearTokens();
            }
            var tokenData = await AuthCallback.LoadTokens(clientData);
            if (tokenData?.AuthToken == null)
            {
                Logger.Warn("User token not found. Launching Twitch login.");
                tokenData ??= new TokenData();
                tokenData.AuthToken = await authCallback.GetChatAuthCode(clientData);
            }
            if (await AuthCallback.ValidateAndRefresh(clientData, tokenData))
            {
                return tokenData;
            }
            else
            {
                Console.WriteLine("Something went wrong authenticating, application terminating...");
            }
            return null;
        }

        private static async Task RunBot(ClientData clientData, TokenData tokenData, string target)
        {
            var client = new TwitchClient(clientData, tokenData);
            var irc = new TwitchIrcClient(tokenData, client);
            Logger.Info("Connecting to Twitch IRC...");
            var delay = Retry;
            var count = 0;
            var connected = await irc.Connect();
            while (!connected && count < RetryLimit)
            {
                count++;
                Logger.Error($"Connection failed, retrying in {delay.TotalSeconds} seconds...");
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
            if (connected)
            {
                var controller = new ChatController(irc, new CancellationTokenSource());
                await controller.Play(target);
            }
            Logger.Error("Unable to connect to Twitch, please verify your connection and try again.");
        }

        static async Task Main(string[] args)
        {
            ConfigureLogger();
            if (args.Any(x => x.Equals("-help") || x.Equals("?") || x.Equals("/?")))
            {
                Logger.Info("Twitch Plays DarkSouls - listens for chat messages in the authenticated user's channel and converts them into inputs for Dark Souls");
                Logger.Info("Arguments: -client,-c - resets client data");
                Logger.Info("Arguments: -auth,-a - resets credentials and forces authentication");
                Logger.Info("Arguments: -target,-t - the name of the process to send commands to (default is DARKSOULS)");
                return;
            }
            if (args.Any(x => x.Equals("-client") || x.Equals("-c")))
            {
                Logger.Info("Clearing client data");
                FileUtils.WriteClientData(new ClientData());
            }
            if (args.Any(x => x.Equals("-auth") || x.Equals("-a")))
            {
                Logger.Info("Clearing auth credentials");
                FileUtils.WriteTokenData(new TokenData());
            }
            var target = "DARKSOULS";
            var newTarget = args.FirstOrDefault("-target") ?? args.FirstOrDefault("-t");
            if (newTarget != null && newTarget.Contains('='))
            {
                target = newTarget.Split('=')[1];
            }
            Logger.Info("Loading client data...");
            var clientData = LoadClientData();
            Logger.Info("Authenticating...");
            var tokenData = await Authenticate(clientData);
            if (tokenData != null)
            {
                await RunBot(clientData, tokenData, target);
            }
            Logger.Info("Application terminated, press any key to close.");
            Console.ReadKey();
        }
    }
}
