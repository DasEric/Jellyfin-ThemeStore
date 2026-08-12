using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public class PatchRequestPayload
    {
        [JsonPropertyName("contents")]
        public string Contents { get; set; }
    }
}