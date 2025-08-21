namespace TwitchPlays.Twitch
{
    /// <summary>
    /// IRC client built for Twitch.
    /// </summary>
    public interface ITwitchIrcClient : IDisposable
    {
        /// <summary>
        /// Gets the time elapsed since the last message was received.
        /// </summary>
        TimeSpan IdleTime { get; }
        /// <summary>
        /// Disconnects the inner TCP client and reconnects to the server.
        /// </summary>
        void ForceReconnect();
        /// <summary>
        /// Connects the client to the twitch server, authenticates the chat
        /// user, and joins the channel of the broadcast user.
        /// </summary>
        /// <param name="secure">Whether or not to connect using SSL.</param>
        /// <returns>Whether or not the connection succeeded.</returns>
        Task<bool> Connect(bool secure = true);
        /// <summary>
        /// Sends any available queued messages and processes any incoming messages.
        /// </summary>
        /// <returns>A collection of messages that have been received since the
        /// last time this method was called.</returns>
        Task<IEnumerable<IrcMessage>> Process();
    }
}
