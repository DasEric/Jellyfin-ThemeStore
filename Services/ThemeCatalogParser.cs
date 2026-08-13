using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ThemeStore.Models;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public static class ThemeCatalogParser
    {
        private static readonly Regex ImportRegex = new(
            "^\\s*@import\\s+(?:url\\(\\s*)?['\\\"]?(?<url>[^'\\\")\\s;]+)['\\\"]?\\s*\\)?\\s*;?\\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ThemeCatalogResult Parse(string content, Uri sourceUri)
        {
            if (sourceUri == null)
                throw new ArgumentNullException(nameof(sourceUri));

            var result = new ThemeCatalogResult { SourceUrl = sourceUri.ToString() };
            if (string.IsNullOrWhiteSpace(content))
            {
                result.Warnings.Add("The theme catalog is empty.");
                return result;
            }

            string trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                ParseJson(trimmed, sourceUri, result);
            else
                ParseSimple(content, sourceUri, result);

            EnsureUniqueIds(result);
            return result;
        }

        private static void ParseSimple(string content, Uri sourceUri, ThemeCatalogResult result)
        {
            string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (!line.StartsWith("#", StringComparison.Ordinal))
                {
                    result.Warnings.Add($"Line {index + 1}: expected a theme header beginning with '#'.");
                    continue;
                }

                int headerLine = index + 1;
                List<string> fields = SplitCsv(line.Substring(1));
                string name = fields.Count > 0 ? fields[0].Trim() : string.Empty;
                if (name.Length == 0)
                {
                    result.Warnings.Add($"Line {headerLine}: theme name is missing.");
                    continue;
                }

                var cssUrls = new List<string>();
                while (++index < lines.Length)
                {
                    string candidate = lines[index].Trim();
                    if (candidate.Length == 0 || candidate.StartsWith("//", StringComparison.Ordinal))
                        continue;

                    if (candidate.StartsWith("#", StringComparison.Ordinal))
                    {
                        index--;
                        break;
                    }

                    Match match = ImportRegex.Match(candidate);
                    if (!match.Success)
                    {
                        result.Warnings.Add($"Line {index + 1}: theme '{name}' needs valid @import URLs.");
                        continue;
                    }

                    string resolved = ResolveWebUrl(sourceUri, match.Groups["url"].Value, result.Warnings, index + 1, "CSS");
                    if (resolved.Length > 0 && !cssUrls.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                        cssUrls.Add(resolved);
                }

                if (cssUrls.Count == 0)
                {
                    result.Warnings.Add($"Line {headerLine}: theme '{name}' needs at least one valid @import URL.");
                    continue;
                }

                var theme = new ThemeDefinition
                {
                    Name = name,
                    CssUrl = cssUrls[0],
                    CssUrls = cssUrls,
                    Version = "1"
                };

                foreach (string preview in fields.Skip(1))
                {
                    string previewUrl = ResolveWebUrl(sourceUri, preview.Trim(), result.Warnings, headerLine, "preview");
                    if (previewUrl.Length > 0)
                        theme.PreviewUrls.Add(previewUrl);
                }

                theme.Id = CreateStableId(theme.Name, string.Empty);
                result.Themes.Add(theme);
            }
        }

        private static void ParseJson(string content, Uri sourceUri, ThemeCatalogResult result)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    result.Warnings.Add("The JSON catalog root must be an array.");
                    return;
                }

                int itemNumber = 0;
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    itemNumber++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        result.Warnings.Add($"JSON item {itemNumber}: expected an object.");
                        continue;
                    }

                    string name = GetString(item, "name");
                    string rawCssUrl = GetString(item, "cssUrl");
                    bool hasCssUrlArray = item.TryGetProperty("cssUrls", out JsonElement cssUrlArray)
                        && cssUrlArray.ValueKind == JsonValueKind.Array
                        && cssUrlArray.GetArrayLength() > 0;
                    bool isDefault = string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(rawCssUrl)
                        && !hasCssUrlArray;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        result.Warnings.Add($"JSON item {itemNumber}: theme name is missing.");
                        continue;
                    }

                    var theme = new ThemeDefinition
                    {
                        Name = name.Trim(),
                        Author = GetString(item, "author"),
                        Description = GetString(item, "description"),
                        Version = GetString(item, "version", "1"),
                        Jellyfin = GetString(item, "jellyfin"),
                        SourceUrl = ResolveOptionalWebUrl(sourceUri, GetString(item, "sourceUrl")),
                        License = GetString(item, "license"),
                        CssUrl = string.IsNullOrWhiteSpace(rawCssUrl)
                            ? string.Empty
                            : ResolveWebUrl(sourceUri, rawCssUrl, result.Warnings, itemNumber, "CSS")
                    };

                    AddResolvedUrls(item, "cssUrls", sourceUri, theme.CssUrls);
                    int primaryIndex = theme.CssUrls.FindIndex(url => string.Equals(url, theme.CssUrl, StringComparison.OrdinalIgnoreCase));
                    if (theme.CssUrl.Length > 0 && primaryIndex < 0)
                    {
                        theme.CssUrls.Insert(0, theme.CssUrl);
                    }
                    else if (primaryIndex > 0)
                    {
                        theme.CssUrls.RemoveAt(primaryIndex);
                        theme.CssUrls.Insert(0, theme.CssUrl);
                    }
                    else if (theme.CssUrl.Length == 0 && theme.CssUrls.Count > 0)
                    {
                        theme.CssUrl = theme.CssUrls[0];
                    }

                    if (!isDefault && theme.CssUrls.Count == 0)
                        continue;

                    AddResolvedUrls(item, "previewUrls", sourceUri, theme.PreviewUrls);
                    AddResolvedUrls(item, "previews", sourceUri, theme.PreviewUrls);
                    string singlePreview = ResolveOptionalWebUrl(sourceUri, GetString(item, "previewUrl"));
                    if (singlePreview.Length > 0 && !theme.PreviewUrls.Contains(singlePreview, StringComparer.OrdinalIgnoreCase))
                        theme.PreviewUrls.Insert(0, singlePreview);

                    AddStrings(item, "tags", theme.Tags);
                    AddResolvedUrls(item, "preconnect", sourceUri, theme.Preconnect);
                    ParseVariables(item, theme.Vars);

                    theme.Id = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(theme.Id))
                        theme.Id = isDefault ? "jellyfin-default" : CreateStableId(theme.Name, theme.CssUrl);

                    result.Themes.Add(theme);
                }
            }
            catch (JsonException ex)
            {
                result.Warnings.Add($"Invalid JSON catalog: {ex.Message}");
            }
        }

        private static void ParseVariables(JsonElement item, List<ThemeVariableDefinition> destination)
        {
            if (!item.TryGetProperty("vars", out JsonElement vars) || vars.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement variable in vars.EnumerateArray())
            {
                if (variable.ValueKind != JsonValueKind.Object)
                    continue;

                string key = GetString(variable, "key");
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var definition = new ThemeVariableDefinition
                {
                    Key = key,
                    Name = GetString(variable, "name", key),
                    Description = GetString(variable, "description"),
                    Type = GetString(variable, "type", "text"),
                    Default = GetString(variable, "default"),
                    AllowGradient = !variable.TryGetProperty("allowGradient", out JsonElement allowGradient) || allowGradient.ValueKind != JsonValueKind.False
                };

                if (variable.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement option in options.EnumerateArray())
                    {
                        if (option.ValueKind == JsonValueKind.Object)
                        {
                            definition.Options.Add(new ThemeVariableOption
                            {
                                Name = GetString(option, "name"),
                                Value = GetString(option, "value")
                            });
                        }
                    }
                }

                destination.Add(definition);
            }
        }

        private static void AddStrings(JsonElement item, string propertyName, List<string> destination)
        {
            if (!item.TryGetProperty(propertyName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    destination.Add(value.GetString().Trim());
            }
        }

        private static void AddResolvedUrls(JsonElement item, string propertyName, Uri sourceUri, List<string> destination)
        {
            if (!item.TryGetProperty(propertyName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                    continue;

                string resolved = ResolveOptionalWebUrl(sourceUri, value.GetString());
                if (resolved.Length > 0 && !destination.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                    destination.Add(resolved);
            }
        }

        private static string GetString(JsonElement item, string propertyName, string fallback = "")
        {
            if (!item.TryGetProperty(propertyName, out JsonElement value))
                return fallback;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => fallback
            };
        }

        private static string ResolveOptionalWebUrl(Uri sourceUri, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (!Uri.TryCreate(sourceUri, value.Trim(), out Uri resolved))
                return string.Empty;

            return resolved.Scheme == Uri.UriSchemeHttps || resolved.Scheme == Uri.UriSchemeHttp
                ? resolved.AbsoluteUri
                : string.Empty;
        }

        private static string ResolveWebUrl(Uri sourceUri, string value, List<string> warnings, int line, string kind)
        {
            string resolved = ResolveOptionalWebUrl(sourceUri, value);
            if (resolved.Length == 0)
                warnings.Add($"Line/item {line}: invalid {kind} URL '{value}'. Only HTTP and HTTPS are supported.");

            return resolved;
        }

        private static List<string> SplitCsv(string input)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';

            foreach (char ch in input)
            {
                if ((ch == '\'' || ch == '\"') && (quote == '\0' || quote == ch))
                {
                    quote = quote == '\0' ? ch : '\0';
                    continue;
                }

                if (ch == ',' && quote == '\0')
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            result.Add(current.ToString().Trim());
            return result;
        }

        private static string CreateStableId(string name, string cssUrl)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim() + "\n" + cssUrl.Trim()));
            return "theme-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLower(CultureInfo.InvariantCulture);
        }

        private static void EnsureUniqueIds(ThemeCatalogResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = result.Themes.Count - 1; i >= 0; i--)
            {
                ThemeDefinition theme = result.Themes[i];
                if (!seen.Add(theme.Id))
                {
                    result.Warnings.Add($"Duplicate theme id '{theme.Id}' for '{theme.Name}'. The duplicate entry was ignored.");
                    result.Themes.RemoveAt(i);
                }
            }
        }
    }
}
