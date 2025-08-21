namespace TwitchPlays.Twitch
{
    /// <summary>
    /// Client that provide access to common Twitch API endpoints.
    /// </summary>
    public interface ITwitchClient
    {
        /// <summary>
        /// Initialization code for the IRC client. This requires an open
        /// database connection.
        /// </summary>
        void Initialize();
        /// <summary>
        /// Attempts to refresh the chat and broadcast tokens.
        /// </summary>
        /// <exception cref="Exception">Exception is thrown if the tokens fail to refresh.</exception>
        Task RefreshTokens();
    }
}
