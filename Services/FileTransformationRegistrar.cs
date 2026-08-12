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
    public class FileTransformationRegistrar : IHostedService
    {
        private static readonly Guid TransformationId = Guid.Parse("38e6e634-af18-454d-aad1-bbe3d9475735");

        private readonly ILogger<FileTransformationRegistrar> _logger;

        public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RegisterWithFileTransformation();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private void RegisterWithFileTransformation()
        {
            try
            {
                var ftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

                if (ftAssembly == null)
                {
                    _logger.LogWarning("[ThemeStore] File Transformation plugin not found.");
                    return;
                }

                var pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[ThemeStore] Could not find PluginInterface in File Transformation assembly.");
                    return;
                }

                var newtonsoftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json"
                                      && x != typeof(FileTransformationRegistrar).Assembly);

                if (newtonsoftAssembly == null)
                {
                    newtonsoftAssembly = AssemblyLoadContext.All
                        .SelectMany(x => x.Assemblies)
                        .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");
                }

                if (newtonsoftAssembly == null)
                {
                    _logger.LogWarning("[ThemeStore] Could not find Newtonsoft.Json assembly.");
                    return;
                }

                var jobjectType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JObject");
                var payload = Activator.CreateInstance(jobjectType);

                var jtokenType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JToken");
                var fromObject = jtokenType.GetMethod("FromObject", new[] { typeof(object) });

                var indexerSetter = jobjectType.GetProperty("Item", new[] { typeof(string) })
                                               ?.GetSetMethod();

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

                pluginInterfaceType.GetMethod("RegisterTransformation")
                    ?.Invoke(null, new[] { payload });

                _logger.LogInformation("[ThemeStore] Successfully registered frontend injection with File Transformation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ThemeStore] Failed to register with File Transformation.");
            }
        }
    }
}
