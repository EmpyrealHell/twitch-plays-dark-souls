using NLog;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using TwitchPlays.Twitch.Api.Authentication;
using TwitchPlays.Utils;

namespace TwitchPlays.Twitch
{
    /// <summary>
    /// IRC client built for Twitch.
    /// </summary>
    public class TwitchIrcClient : ITwitchIrcClient
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string Url = "irc.chat.twitch.tv";
        private static readonly int SslPort = 6697;
        private static readonly int NonSslPort = 6667;
        private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ResponseLimit = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ReconnectTimerBase = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ReconnectTimerMax = TimeSpan.FromSeconds(2048);

        private TcpClient Client;
        private StreamReader InputStream = StreamReader.Null;
        private StreamWriter OutputStream = StreamWriter.Null;
        private readonly TokenData TokenData;
        private readonly ITwitchClient TwitchClient;
        private bool IsSecure;
        private readonly List<IrcMessage> InjectionMessages = [];
        private readonly SemaphoreSlim InjectSemaphore = new(1, 1);

        private DateTime? LastReconnect;
        private TimeSpan ReconnectTimer = ReconnectTimerBase;

        public DateTime LastMessage { get; private set; }
        private bool PingSent;

        private readonly Queue<string> MessageQueue = [];
        private readonly RollingTimer Timer = new(TimeSpan.FromSeconds(30), 100);

        public TimeSpan IdleTime { get { return DateTime.Now - LastMessage; } }

        /// <summary>
        /// Creates a new IRC client.
        /// </summary>
        /// <param name="tokenData">The token data for the authenticated users.</param>
        /// <param name="twitchClient">The twitch client to use when refreshing
        /// the auth token.</param>
        public TwitchIrcClient(TokenData tokenData, ITwitchClient twitchClient)
        {
            Client = new TcpClient();
            TokenData = tokenData;
            TwitchClient = twitchClient;
        }

        /// <summary>
        /// Disposes and recreates the inner tcp client to allow for proper reconnects.
        /// </summary>
        public void Restart()
        {
            Dispose();
            Client = new TcpClient();
        }

        /// <summary>
        /// Connects the client to the twitch server, authenticates the chat
        /// user, and joins the channel of the broadcast user.
        /// </summary>
        /// <param name="secure">Whether or not to connect using SSL.</param>
        /// <returns>Whether or not the connection succeeded.</returns>
        public async Task<bool> Connect(bool secure = true)
        {
            IsSecure = secure;
            try
            {
                await Client.ConnectAsync(Url, secure ? SslPort : NonSslPort);
                if (Client.Connected)
                {
                    Stream stream = Client.GetStream();
                    if (secure)
                    {
                        var sslStream = new SslStream(stream);
                        await sslStream.AuthenticateAsClientAsync(Url);
                        stream = sslStream;
                    }
                    var encoding = new UTF8Encoding(false);
                    InputStream = new StreamReader(stream, encoding);
                    OutputStream = new StreamWriter(stream, encoding)
                    {
                        AutoFlush = false
                    };

                    ExpectResponse();
                    await WriteLines("CAP REQ :twitch.tv/tags twitch.tv/commands",
                        $"PASS oauth:{TokenData.AuthToken?.AccessToken ?? string.Empty}",
                        $"NICK {TokenData.UserName.ToLower()}",
                        $"JOIN #{TokenData.UserName}");
                    Logger.Info("Logged in to Twitch IRC server as {user}", TokenData.UserName);
                    LastReconnect = null;
                    return true;
                }
                else
                {
                    Logger.Error("Connection failed!");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
            LastReconnect = DateTime.Now;
            return false;
        }

        /// <summary>
        /// Disconnects the inner TCP client and reconnects to the server.
        /// </summary>
        public void ForceReconnect()
        {
            PingSent = true;
            LastMessage = DateTime.Now - (IdleLimit + ResponseLimit);
        }

        private async Task Reconnect()
        {
            PingSent = false;
            Restart();
            var connected = await Connect(IsSecure);
            if (!connected)
            {
                ReconnectTimer = TimeSpan.FromSeconds(Math.Min(ReconnectTimer.TotalSeconds * 2, ReconnectTimerMax.TotalSeconds));
                Logger.Error("Connection failed. Retrying in {seconds} seconds.", ReconnectTimer.TotalSeconds);
            }
            else
            {
                await WriteLine("PING");
                ReconnectTimer = TimeSpan.FromSeconds(ReconnectTimerBase.TotalSeconds);
            }
        }

        private void ExpectResponse()
        {
            LastMessage = DateTime.Now - IdleLimit;
        }

        private async Task<bool> WriteLine(string line)
        {
            try
            {
                await OutputStream.WriteLineAsync(line);
                await OutputStream.FlushAsync();
            }
            catch (Exception e)
            {
                Logger.Error(e);
                Logger.Error("Encountered an error sending data to IRC. Reconnecting.");
                Dispose();
                LastReconnect = DateTime.Now;
                return false;
            }
            return true;
        }

        private async Task WriteLines(params string[] lines)
        {
            foreach (var line in lines)
            {
                await OutputStream.WriteLineAsync(line);
            }
            await OutputStream.FlushAsync();
        }

        private async Task<string?> Read()
        {
            if (Client.GetStream().DataAvailable)
            {
                var content = await InputStream.ReadLineAsync();
                return content;
            }
            return null;
        }

        private async Task<IEnumerable<IrcMessage>> GetAndClearInjections()
        {
            List<IrcMessage> output;
            await InjectSemaphore.WaitAsync();
            try
            {
                output = [.. InjectionMessages];
                InjectionMessages.Clear();
            }
            finally
            {
                InjectSemaphore.Release();
            }
            return output;
        }

        /// <summary>
        /// Sends any available queued messages and processes any incoming messages.
        /// </summary>
        /// <returns>A collection of messages that have been received since the
        /// last time this method was called.</returns>
        public async Task<IEnumerable<IrcMessage>> Process()
        {
            var output = new List<IrcMessage>();
            output.AddRange(await GetAndClearInjections());
            if (Client.Connected)
            {
                if (MessageQueue.Count > 0)
                {
                    var toSend = MessageQueue.Peek();
                    if (toSend != null && Timer.AvailableOccurrences() > 0)
                    {
                        var success = await WriteLine($"PRIVMSG #{TokenData.UserName} :{toSend}");
                        if (success)
                        {
                            MessageQueue.Dequeue();
                            Timer.AddOccurrence(DateTime.Now);
                        }
                        else
                        {
                            //If write fails, the client is disconnected
                            //abort the process so it can reconnect
                            return output;
                        }
                    }
                }

                var input = await Read();
                while (input != null)
                {
                    LastMessage = DateTime.Now;
                    var message = IrcMessage.Parse(input);
                    if (message != null)
                    {
                        if (message.IsChat || message.IsWhisper || message.IsUserNotice)
                        {
                            output.Add(message);
                        }
                        else if ("notice".Equals(message.Command, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info("Notice received, likely due to expired OAuth token. Refreshing tokens and reconnecting.");
                            await TwitchClient.RefreshTokens();
                            await Reconnect();
                        }
                        else if ("pong".Equals(message.Command, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info("Pong received, connection confirmed.");
                            PingSent = false;
                        }
                        else if ("ping".Equals(message.Command, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info("Ping from received from Twitch, sending pong.");
                            await WriteLine($"PONG :{message.Message}");
                        }
                        else
                        {
                            Logger.Debug("Received {command} type message from Twitch.", message.Command);
                        }
                    }
                    input = await Read();
                }

                if (DateTime.Now - LastMessage > IdleLimit && !PingSent)
                {
                    Logger.Info("No messages in {seconds} seconds, sending ping to Twitch.", IdleLimit.TotalSeconds);
                    await WriteLine("PING");
                    PingSent = true;
                }
                else if (DateTime.Now - LastMessage > IdleLimit + ResponseLimit && PingSent)
                {
                    Logger.Info("IRC client disconnected. Reconnecting...");
                    await Reconnect();
                }
            }
            else if (LastReconnect.HasValue && DateTime.Now - LastReconnect.Value > ReconnectTimer)
            {
                await Reconnect();
            }
            return output;
        }

        /// <summary>
        /// Queues a message to send out on the IRC client.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public void QueueMessage(string message)
        {
            MessageQueue.Enqueue(message);
        }

        /// <summary>
        /// Injects a message into the incoming message stream. Used to allow
        /// the bot UI to send commands without going through the IRC
        /// connection.
        /// </summary>
        /// <param name="message">The text message to inject.</param>
        /// <param name="username">The Username to send from.</param>
        /// <param name="userid">The Twitch ID of the user to send from.</param>
        public void InjectMessage(string message, string username, string userid)
        {
            InjectSemaphore.Wait();
            try
            {
                InjectionMessages.Add(IrcMessage.Create(message, username, userid));
            }
            finally
            {
                InjectSemaphore.Release();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (InputStream != null)
            {
                try { InputStream.Close(); } catch { }
                try { InputStream.Dispose(); } catch { }
            }

            if (OutputStream != null)
            {
                try { OutputStream.Close(); } catch { }
                try { OutputStream.Dispose(); } catch { }
            }

            if (Client != null)
            {
                try { Client.Close(); } catch { }
                try { Client.Dispose(); } catch { }
            }
        }
    }
}
