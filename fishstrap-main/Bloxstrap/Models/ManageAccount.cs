using System;

namespace Bloxstrap.Models
{
    public class ManagedAccount
    {
        [JsonPropertyName("userId")]
        public long UserId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("encryptedCookie")]
        public string EncryptedCookie { get; set; } = string.Empty;
    }
}