﻿namespace TwitchPlays.Twitch.Api.Authentication
{
    /// <summary>
    /// The response from a token validation request.
    /// </summary>
    public class ValidationResponse
    {
        /// <summary>
        /// The client id the token was registered with.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;
        /// <summary>
        /// The user name of the user the token was obtained by.
        /// </summary>
        public string Login { get; set; } = string.Empty;
        /// <summary>
        /// The user id of the user the token was obtained by.
        /// </summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>
        /// A collection of scope names that the token has access to.
        /// </summary>
        public IEnumerable<string> Scopes { get; set; } = [];
    }
}
