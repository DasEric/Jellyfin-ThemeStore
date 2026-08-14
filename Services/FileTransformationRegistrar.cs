using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public class FileTransformationRegistrar : BackgroundService
    {
        private static readonly Guid TransformationId = Guid.Parse("38e6e634-af18-454d-aad1-bbe3d9475735");
        private const int RegistrationAttempts = 30;

        private readonly ILogger<FileTransformationRegistrar> _logger;

        public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int attempt = 1; attempt <= RegistrationAttempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    if (TryRegisterWithFileTransformation())
                    {
                        _logger.LogInformation("[ThemeStore] Successfully registered frontend injection with File Transformation.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < RegistrationAttempts)
                        _logger.LogDebug(ex, "[ThemeStore] File Transformation is not ready yet (attempt {Attempt}/{Attempts}).", attempt, RegistrationAttempts);
                    else
                    {
                        _logger.LogWarning(ex, "[ThemeStore] File Transformation registration failed after {Attempts} attempts.", RegistrationAttempts);
                        return;
                    }
                }

                if (attempt < RegistrationAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }

            if (!stoppingToken.IsCancellationRequested)
                _logger.LogWarning("[ThemeStore] File Transformation was not available after {Attempts} registration attempts. Install or enable it, then restart Jellyfin.", RegistrationAttempts);
        }

        private static bool TryRegisterWithFileTransformation()
        {
            var ftAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);
            var pluginInterfaceType = ftAssembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var registerMethod = pluginInterfaceType?.GetMethod("RegisterTransformation");
            if (registerMethod == null)
                return false;

            var newtonsoftAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json"
                                  && x != typeof(FileTransformationRegistrar).Assembly)
                ?? AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");
            var jobjectType = newtonsoftAssembly?.GetType("Newtonsoft.Json.Linq.JObject");
            var jtokenType = newtonsoftAssembly?.GetType("Newtonsoft.Json.Linq.JToken");
            var fromObject = jtokenType?.GetMethod("FromObject", new[] { typeof(object) });
            var indexerSetter = jobjectType?.GetProperty("Item", new[] { typeof(string) })?.GetSetMethod();
            if (jobjectType == null || fromObject == null || indexerSetter == null)
                return false;

            var payload = Activator.CreateInstance(jobjectType);
            void Set(string key, object value)
            {
                var token = fromObject.Invoke(null, new[] { value });
                indexerSetter.Invoke(payload, new[] { key, token });
            }

            Set("id", TransformationId.ToString());
            Set("fileNamePattern", "index.html");
            Set("callbackAssembly", typeof(SkinInjector).Assembly.FullName);
            Set("callbackClass", typeof(SkinInjector).FullName);
            Set("callbackMethod", nameof(SkinInjector.InjectTheme));
            registerMethod.Invoke(null, new[] { payload });
            return true;
        }
    }
}
