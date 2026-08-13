using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.IO;
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
    public void IncludedCatalogContainsAllCuratedThemesAndVariants()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "themes", "catalog.json");
        string content = File.ReadAllText(path);
        var result = ThemeCatalogParser.Parse(content, new Uri(global::Jellyfin.Plugin.ThemeStore.Plugin.DefaultCatalogUrl));

        Assert.Empty(result.Warnings);
        Assert.Equal(67, result.Themes.Count);
        Assert.Equal(67, result.Themes.Select(theme => theme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(result.Themes, theme => theme.Id == "catppuccin-mocha" && theme.CssUrls.Count == 2);
        Assert.Contains(result.Themes, theme => theme.Id == "scyfin-oled" && theme.CssUrls.Count == 2);
        Assert.All(result.Themes, theme => Assert.StartsWith("https://", theme.SourceUrl));
        Assert.All(result.Themes, theme => Assert.NotEmpty(theme.PreviewUrls));
        Assert.All(result.Themes, theme => Assert.NotEmpty(theme.License));
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
        Assert.Contains("dataType: 'text'", script);
        Assert.Contains("dataType: 'json'", script);
        Assert.Contains("typeof value.text === 'function'", script);
        Assert.Contains("function ensureThemeLast()", script);
        Assert.Contains("priorityObserver", script);
        Assert.Contains("theme.License", ReadEmbeddedText("Jellyfin.Plugin.ThemeStore.Configuration.userThemePage.js"));
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
    public void ParsesOrderedBaseAndAddonImportsAsOneVariant()
    {
        const string input = """
            #Scyfin - OLED, preview.png
            @import url('https://cdn.example/scyfin.css');
            @import url('https://cdn.example/oled.css');
            """;
        var theme = Assert.Single(ThemeCatalogParser.Parse(input, new Uri("https://example.org/catalog.txt")).Themes);

        Assert.Equal("https://cdn.example/scyfin.css", theme.CssUrl);
        Assert.Equal(new[] { "https://cdn.example/scyfin.css", "https://cdn.example/oled.css" }, theme.CssUrls);
    }

    [Fact]
    public void ParsesJsonCssUrlArraysForCompleteVariants()
    {
        const string input = """
            [{"id":"catppuccin-mocha","name":"Catppuccin - Mocha","cssUrls":["base.css","mocha.css"]}]
            """;
        var theme = Assert.Single(ThemeCatalogParser.Parse(input, new Uri("https://example.org/themes/catalog.json")).Themes);

        Assert.Equal("https://example.org/themes/base.css", theme.CssUrl);
        Assert.Equal(new[] { "https://example.org/themes/base.css", "https://example.org/themes/mocha.css" }, theme.CssUrls);
    }

    [Fact]
    public void ExplicitCssUrlIsAlwaysFirstInJsonVariantOrder()
    {
        const string input = """
            [{"name":"Variant","cssUrl":"base.css","cssUrls":["addon.css","base.css"]}]
            """;
        var theme = Assert.Single(ThemeCatalogParser.Parse(input, new Uri("https://example.org/catalog.json")).Themes);

        Assert.Equal(new[] { "https://example.org/base.css", "https://example.org/addon.css" }, theme.CssUrls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/refs/heads/main/skins.json")]
    [InlineData("https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/main/skins.json")]
    public void MigratesOnlyTheLegacyDefaultCatalog(string value)
        => Assert.Equal(global::Jellyfin.Plugin.ThemeStore.Plugin.DefaultCatalogUrl, global::Jellyfin.Plugin.ThemeStore.Plugin.MigrateCatalogUrl(value));

    [Fact]
    public void PreservesAdministratorCatalogDuringMigration()
        => Assert.Equal("https://example.org/custom.json", global::Jellyfin.Plugin.ThemeStore.Plugin.MigrateCatalogUrl(" https://example.org/custom.json "));

    [Fact]
    public void ParsesLegacyJsonAndPreviewArrays()
    {
        const string input = """
            [{"name":"Night","author":"Tester","version":"2.0","license":"MIT","cssUrl":"themes/night.css","previewUrl":"one.png","previewUrls":["two.webp"]}]
            """;
        var result = ThemeCatalogParser.Parse(input, new Uri("https://example.org/store/catalog.json"));
        var theme = Assert.Single(result.Themes);
        Assert.Equal("https://example.org/store/themes/night.css", theme.CssUrl);
        Assert.Equal("MIT", theme.License);
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
