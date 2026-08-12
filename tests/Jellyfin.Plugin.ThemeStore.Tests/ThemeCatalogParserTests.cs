using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.ThemeStore;
using Jellyfin.Plugin.ThemeStore.Services;
using Jellyfin.Plugin.ThemeStore.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.ThemeStore.Tests;

public sealed class ThemeCatalogParserTests
{
    [Fact]
    public void EmbedsAdminAndUserFrontendResources()
    {
        string[] resources = typeof(global::Jellyfin.Plugin.ThemeStore.Plugin).Assembly.GetManifestResourceNames();
        Assert.Contains("Jellyfin.Plugin.ThemeStore.Configuration.configPage.html", resources);
        Assert.Contains("Jellyfin.Plugin.ThemeStore.Configuration.injection.js", resources);
        Assert.Contains("Jellyfin.Plugin.ThemeStore.Configuration.userThemePage.html", resources);
        Assert.Contains("Jellyfin.Plugin.ThemeStore.Configuration.userThemePage.js", resources);
    }

    [Fact]
    public void RegistersOnlyAdminSettingsAsJellyfinPluginPage()
    {
        var page = Assert.Single(global::Jellyfin.Plugin.ThemeStore.Plugin.CreatePages());
        Assert.Equal("ThemeStoreSettings", page.Name);
        Assert.Equal("Theme Store Settings", page.DisplayName);
        Assert.True(page.EnableInMainMenu);
        Assert.Equal("server", page.MenuSection);
    }

    [Fact]
    public void UserDrawerInjectionTargetsJellyfinMainDrawer()
    {
        string script = ReadEmbeddedText("Jellyfin.Plugin.ThemeStore.Configuration.injection.js");
        Assert.Contains(".mainDrawer-scrollContainer", script);
        Assert.Contains(".customMenuOptions", script);
        Assert.Contains("ThemeStore/Page", script);
        Assert.DoesNotContain(".lnkHomePreferences", script);
    }

    [Fact]
    public void InjectsExternalBootstrapExactlyOnce()
    {
        string first = SkinInjector.InjectTheme(new PatchRequestPayload
        {
            Contents = "<html><head><title>Jellyfin</title></head><body></body></html>"
        });
        string second = SkinInjector.InjectTheme(new PatchRequestPayload { Contents = first });

        Assert.Contains("../ThemeStore/InjectionScript", second);
        Assert.Equal(1, second.Split("../ThemeStore/InjectionScript", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, second.Split("<!-- ThemeStore-Start -->", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ParsesSimpleCatalogWithMultipleAndRelativePreviews()
    {
        const string input = """
            #Ocean, preview/home.png, https://images.example/details.jpg
            @import url('./css/ocean.css');

            #Minimal
            @import 'https://cdn.example/minimal.css';
            """;
        var result = ThemeCatalogParser.Parse(input, new Uri("https://themes.example/catalog/catalog.txt"));
        Assert.Equal(2, result.Themes.Count);
        Assert.Equal("Ocean", result.Themes[0].Name);
        Assert.Equal("https://themes.example/catalog/css/ocean.css", result.Themes[0].CssUrl);
        Assert.Equal(2, result.Themes[0].PreviewUrls.Count);
        Assert.Equal("https://themes.example/catalog/preview/home.png", result.Themes[0].PreviewUrls[0]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ParsesLegacyJsonAndPreviewArrays()
    {
        const string input = """
            [{"name":"Night","author":"Tester","version":"2.0","cssUrl":"themes/night.css","previewUrl":"one.png","previewUrls":["two.webp"]}]
            """;
        var result = ThemeCatalogParser.Parse(input, new Uri("https://example.org/store/catalog.json"));
        var theme = Assert.Single(result.Themes);
        Assert.Equal("https://example.org/store/themes/night.css", theme.CssUrl);
        Assert.Equal(new[] { "https://example.org/store/one.png", "https://example.org/store/two.webp" }, theme.PreviewUrls);
    }

    [Fact]
    public void ReportsMalformedEntriesWithoutDiscardingValidThemes()
    {
        const string input = """
            no header
            #Broken
            not-an-import
            #Working
            @import url('https://example.org/working.css');
            """;
        var result = ThemeCatalogParser.Parse(input, new Uri("https://example.org/catalog.txt"));
        Assert.Single(result.Themes);
        Assert.Equal("Working", result.Themes[0].Name);
        Assert.True(result.Warnings.Count >= 2);
    }

    [Theory]
    [InlineData("http://127.0.0.1/theme.css")]
    [InlineData("http://localhost/theme.css")]
    [InlineData("http://192.168.1.2/theme.css")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.org/theme.css")]
    public void RejectsUnsafeServerSideUrls(string input)
        => Assert.False(ThemeCatalogService.TryValidatePublicHttpUrl(input, out _, out _));

    [Theory]
    [InlineData("https://raw.githubusercontent.com/example/theme/main/theme.css")]
    [InlineData("http://example.org/theme.css")]
    public void AcceptsPublicHttpUrls(string input)
        => Assert.True(ThemeCatalogService.TryValidatePublicHttpUrl(input, out _, out _));

    [Fact]
    public void RewritesRelativeCssResourcesForBlobDelivery()
    {
        const string css = "body{background:url('../images/bg.webp')} @import './parts/cards.css'; .icon{mask:url(data:image/png;base64,abc)}";
        string result = CssUrlRewriter.Rewrite(css, new Uri("https://themes.example/css/main.css"));
        Assert.Contains("https://themes.example/images/bg.webp", result);
        Assert.Contains("https://themes.example/css/parts/cards.css", result);
        Assert.Contains("data:image/png;base64,abc", result);
    }

    [Fact]
    public void ProtectsAllDataAndMutationEndpoints()
    {
        var controller = typeof(ThemeStoreController);
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetCatalog))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetAdminCatalog))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.SavePreference))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.DeletePreference))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetThemeCss))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.ClearCache))!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetInjectionScript))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetPage))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(controller.GetMethod(nameof(ThemeStoreController.GetPageScript))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));

        Assert.DoesNotContain(
            controller.GetMethod(nameof(ThemeStoreController.GetCatalog))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == MediaBrowser.Common.Api.Policies.RequiresElevation);
        Assert.DoesNotContain(
            controller.GetMethod(nameof(ThemeStoreController.SavePreference))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == MediaBrowser.Common.Api.Policies.RequiresElevation);
        Assert.DoesNotContain(
            controller.GetMethod(nameof(ThemeStoreController.DeletePreference))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == MediaBrowser.Common.Api.Policies.RequiresElevation);
    }

    [Fact]
    public void ResolvesStandardNameIdentifierForRegularUsers()
    {
        Guid expected = Guid.NewGuid();
        var controller = new ThemeStoreController(null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, expected.ToString()) },
                        "test"))
                }
            }
        };
        MethodInfo method = typeof(ThemeStoreController).GetMethod("GetUserId", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(expected, (Guid)method.Invoke(controller, null)!);
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(global::Jellyfin.Plugin.ThemeStore.Plugin).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new System.IO.StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
