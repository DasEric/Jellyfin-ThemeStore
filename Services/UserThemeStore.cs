using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThemeStore.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public sealed class UserThemeStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly ILogger<UserThemeStore> _logger;
        private Dictionary<string, UserThemePreference> _preferences;

        public UserThemeStore(ILogger<UserThemeStore> logger)
        {
            _logger = logger;
        }

        public async Task<UserThemePreference> GetAsync(Guid userId, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadWithoutLockAsync(cancellationToken).ConfigureAwait(false);
                string key = userId.ToString("N");
                return _preferences.TryGetValue(key, out UserThemePreference preference)
                    ? Clone(preference)
                    : new UserThemePreference();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(Guid userId, UserThemePreference preference, CancellationToken cancellationToken)
        {
            if (preference == null)
                throw new ArgumentNullException(nameof(preference));

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadWithoutLockAsync(cancellationToken).ConfigureAwait(false);
                string key = userId.ToString("N");
                if (string.IsNullOrWhiteSpace(preference.ThemeId))
                    _preferences.Remove(key);
                else
                    _preferences[key] = Clone(preference);

                await WriteWithoutLockAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task LoadWithoutLockAsync(CancellationToken cancellationToken)
        {
            if (_preferences != null)
                return;

            string path = GetPath();
            if (!File.Exists(path))
            {
                _preferences = new Dictionary<string, UserThemePreference>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                await using FileStream stream = File.OpenRead(path);
                _preferences = await JsonSerializer.DeserializeAsync<Dictionary<string, UserThemePreference>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? new Dictionary<string, UserThemePreference>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogError(ex, "[ThemeStore] Could not load user theme preferences from {Path}.", path);
                _preferences = new Dictionary<string, UserThemePreference>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task WriteWithoutLockAsync(CancellationToken cancellationToken)
        {
            string path = GetPath();
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";

            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, _preferences, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }

        private static string GetPath()
        {
            string dataFolder = Plugin.Instance?.DataFolderPath
                ?? throw new InvalidOperationException("Theme Store plugin is not initialized.");
            return Path.Combine(dataFolder, "user-themes.json");
        }

        private static UserThemePreference Clone(UserThemePreference source)
            => new()
            {
                ThemeId = source.ThemeId ?? string.Empty,
                Variables = source.Variables != null
                    ? new Dictionary<string, string>(source.Variables, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
    }
}
