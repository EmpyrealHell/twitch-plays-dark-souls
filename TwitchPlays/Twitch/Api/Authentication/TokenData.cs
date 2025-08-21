namespace TwitchPlays.Twitch.Api.Authentication
{
    /// <summary>
    /// Contains the pair of tokens required to access streamer data and act as
    /// a bot account in irc.
    /// </summary>
    public class TokenData
    {
        /// <summary>
        /// The user the chat token represents.
        /// </summary>
        public string UserName { get; set; } = string.Empty;
        /// <summary>
        /// The Twitch user id of the chat user.
        /// </summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>
        /// The token to use when connecting to chat.
        /// </summary>
        public TokenResponse? AuthToken { get; set; }
    }
}