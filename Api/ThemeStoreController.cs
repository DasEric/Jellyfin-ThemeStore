using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThemeStore.Models;
using Jellyfin.Plugin.ThemeStore.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore.Api
{
    [ApiController]
    [Route("ThemeStore")]
    public sealed class ThemeStoreController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly Dictionary<string, byte[]> ResourceCache = new(StringComparer.Ordinal);
        private static readonly object ResourceLock = new();
        private readonly ThemeCatalogService _catalogService;
        private readonly UserThemeStore _userThemeStore;
        private readonly ILogger<ThemeStoreController> _logger;

        public ThemeStoreController(ThemeCatalogService catalogService, UserThemeStore userThemeStore, ILogger<ThemeStoreController> logger)
        {
            _catalogService = catalogService;
            _userThemeStore = userThemeStore;
            _logger = logger;
        }

        [HttpGet("InjectionScript")]
        [AllowAnonymous]
        [Produces("application/javascript")]
        public IActionResult GetInjectionScript()
            => GetEmbeddedResource("Configuration.injection.js", "application/javascript; charset=utf-8");

        [HttpGet("Page")]
        [AllowAnonymous]
        [Produces("text/html")]
        public IActionResult GetPage()
            => GetEmbeddedResource("Configuration.userThemePage.html", "text/html; charset=utf-8");

        [HttpGet("PageScript")]
        [AllowAnonymous]
        [Produces("application/javascript")]
        public IActionResult GetPageScript()
            => GetEmbeddedResource("Configuration.userThemePage.js", "application/javascript; charset=utf-8");

        [HttpGet("Catalog")]
        [Authorize]
        public async Task<ActionResult<ThemeCatalogResponse>> GetCatalog(CancellationToken cancellationToken)
        {
            ThemeCatalogResult catalog = await _catalogService.GetCatalogAsync(false, cancellationToken).ConfigureAwait(false);
            var config = Plugin.Instance.Configuration;
            UserThemePreference preference = await _userThemeStore.GetAsync(GetUserId(), cancellationToken).ConfigureAwait(false);
            return Ok(new ThemeCatalogResponse
            {
                AllowUserThemes = config.AllowUserThemes,
                DefaultMode = config.DefaultThemeMode,
                DefaultThemeId = config.DefaultThemeId,
                DefaultThemeName = config.DefaultThemeName,
                DefaultVariables = ParseVariables(config.DefaultThemeVariablesJson),
                SelectedThemeId = preference.ThemeId,
                Variables = preference.Variables,
                Themes = catalog.Themes,
                Warnings = catalog.Warnings
            });
        }

        [HttpGet("AdminCatalog")]
        [Authorize(Policy = Policies.RequiresElevation)]
        public async Task<ActionResult<ThemeCatalogResult>> GetAdminCatalog([FromQuery] bool refresh, CancellationToken cancellationToken)
            => Ok(await _catalogService.GetCatalogAsync(refresh, cancellationToken).ConfigureAwait(false));

        [HttpPut("Preference")]
        [Authorize]
        public async Task<IActionResult> SavePreference([FromBody] SaveUserThemeRequest request, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance.Configuration;
            if (!config.AllowUserThemes)
                return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Personal themes are disabled by the administrator." });

            string themeId = request?.ThemeId?.Trim() ?? string.Empty;
            if (themeId.Length > 0 && await _catalogService.FindThemeAsync(themeId, cancellationToken).ConfigureAwait(false) == null)
                return BadRequest(new { Message = "The selected theme does not exist in the configured catalog." });

            var preference = new UserThemePreference
            {
                ThemeId = themeId,
                Variables = SanitizeVariables(request?.Variables)
            };
            await _userThemeStore.SaveAsync(GetUserId(), preference, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }

        [HttpDelete("Preference")]
        [Authorize]
        public async Task<IActionResult> DeletePreference(CancellationToken cancellationToken)
        {
            await _userThemeStore.SaveAsync(GetUserId(), new UserThemePreference(), cancellationToken).ConfigureAwait(false);
            return NoContent();
        }

        [HttpGet("Theme.css")]
        [Authorize]
        [Produces("text/css")]
        public async Task<IActionResult> GetThemeCss([FromQuery] string id, [FromQuery] string v, CancellationToken cancellationToken)
        {
            string css;
            if (string.Equals(id, "custom", StringComparison.OrdinalIgnoreCase))
            {
                css = Plugin.Instance.Configuration.CustomCss ?? string.Empty;
            }
            else
            {
                ThemeDefinition theme = await _catalogService.FindThemeAsync(id, cancellationToken).ConfigureAwait(false);
                if (theme == null || (string.IsNullOrWhiteSpace(theme.CssUrl) && theme.CssUrls.Count == 0))
                    return NotFound();

                var sources = theme.CssUrls.Count > 0
                    ? theme.CssUrls
                    : new System.Collections.Generic.List<string> { theme.CssUrl };
                var parts = new System.Collections.Generic.List<string>(sources.Count);
                foreach (string source in sources)
                {
                    string part = await SkinResourceProxy.GetResourceAsync(source, theme.Version ?? v, _logger, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(part))
                        return StatusCode(StatusCodes.Status502BadGateway, $"Could not load CSS source for theme '{theme.Name}'.");

                    parts.Add(CssUrlRewriter.Rewrite(part, new Uri(source)));
                }

                css = string.Join("\n\n", parts);
            }

            Response.Headers.CacheControl = "private, max-age=300";
            return Content(css ?? string.Empty, "text/css; charset=utf-8", Encoding.UTF8);
        }

        [HttpPost("ClearCache")]
        [Authorize(Policy = Policies.RequiresElevation)]
        public IActionResult ClearCache()
        {
            SkinResourceProxy.ClearCache();
            return NoContent();
        }

        private Guid GetUserId()
        {
            string[] values =
            {
                User.FindFirstValue("Jellyfin-UserId"),
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                User.FindFirstValue("UserId")
            };
            foreach (string value in values)
            {
                if (Guid.TryParse(value, out Guid userId) && userId != Guid.Empty)
                    return userId;
            }

            throw new InvalidOperationException("The authenticated Jellyfin user id is missing.");
        }

        private static Dictionary<string, string> SanitizeVariables(IDictionary<string, string> variables)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (variables == null)
                return result;

            foreach (KeyValuePair<string, string> pair in variables.Take(64))
            {
                string key = pair.Key?.Trim() ?? string.Empty;
                string value = pair.Value?.Trim() ?? string.Empty;
                if (key.Length is > 0 and <= 80 && value.Length <= 1000 && key.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'))
                    result[key] = value;
            }

            return result;
        }

        private static Dictionary<string, string> ParseVariables(string json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        private IActionResult GetEmbeddedResource(string suffix, string mediaType)
        {
            byte[] bytes;
            lock (ResourceLock)
            {
                if (!ResourceCache.TryGetValue(suffix, out bytes))
                {
                    var assembly = typeof(ThemeStoreController).Assembly;
                    string resourceName = $"{typeof(Plugin).Namespace}.{suffix}";
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        return NotFound();

                    bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes, 0, bytes.Length);
                    ResourceCache[suffix] = bytes;
                }
            }

            Response.Headers.CacheControl = "no-cache";
            return File(bytes, mediaType);
        }
    }

    public sealed class SaveUserThemeRequest
    {
        public string ThemeId { get; set; } = string.Empty;

        public Dictionary<string, string> Variables { get; set; } = new();

    }

    public sealed class ThemeCatalogResponse
    {
        [Required]
        public bool AllowUserThemes { get; set; }

        public string DefaultMode { get; set; } = "Jellyfin";

        public string DefaultThemeId { get; set; } = string.Empty;

        public string DefaultThemeName { get; set; } = "Jellyfin Default";

        public string SelectedThemeId { get; set; } = string.Empty;

        public Dictionary<string, string> Variables { get; set; } = new();

        public Dictionary<string, string> DefaultVariables { get; set; } = new();

        public List<ThemeDefinition> Themes { get; set; } = new();

        public List<string> Warnings { get; set; } = new();
    }
}
