using System.Collections.Generic;

namespace Jellyfin.Plugin.ThemeStore.Models
{
    public sealed class UserThemePreference
    {
        public string ThemeId { get; set; } = string.Empty;

        public Dictionary<string, string> Variables { get; set; } = new();
    }
}
