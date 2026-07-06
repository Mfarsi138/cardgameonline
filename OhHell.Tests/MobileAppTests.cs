using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace OhHell.Tests;

public class MobileAppTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void MauiProject_CardAssetsExist_InComponentsWwwroot()
    {
        var dir = Path.Combine(Root, "OhHell.Components", "wwwroot", "cards", "svg-cards-1.3");
        Assert.True(Directory.Exists(dir), $"Cards directory not found at: {dir}");
        Assert.Equal(52, Directory.GetFiles(dir, "*.svg").Length);
    }

    [Fact]
    public void SvgCardAssetPath_GeneratesCorrectPath()
    {
        var card = new OhHell.Core.Card(OhHell.Core.Suit.Spades, 14);
        var method = typeof(OhHell.Components.Components.Pages.Home)
            .GetMethod("SvgCardAssetPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var path = (string)method.Invoke(null, new object[] { card })!;
        Assert.Equal("_content/OhHell.Components/cards/svg-cards-1.3/ace_of_spades.svg", path);
    }

    [Fact]
    public void SvgCardAssetPath_Jack_GeneratesSuffix()
    {
        var card = new OhHell.Core.Card(OhHell.Core.Suit.Hearts, 11);
        var method = typeof(OhHell.Components.Components.Pages.Home)
            .GetMethod("SvgCardAssetPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var path = (string)method!.Invoke(null, new object[] { card })!;
        Assert.Contains("jack_of_hearts2.svg", path);
    }

    [Fact]
    public void SvgCardAssetPath_NumberCard_NoSuffix()
    {
        var card = new OhHell.Core.Card(OhHell.Core.Suit.Clubs, 7);
        var method = typeof(OhHell.Components.Components.Pages.Home)
            .GetMethod("SvgCardAssetPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var path = (string)method!.Invoke(null, new object[] { card })!;
        Assert.Contains("7_of_clubs.svg", path);
        Assert.DoesNotContain("2.svg", path);
    }

    [Fact]
    public void SvgCardAssetPath_AllRanks_GenerateValidPaths()
    {
        var method = typeof(OhHell.Components.Components.Pages.Home)
            .GetMethod("SvgCardAssetPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var suits = new[] { OhHell.Core.Suit.Spades, OhHell.Core.Suit.Hearts, OhHell.Core.Suit.Diamonds, OhHell.Core.Suit.Clubs };
        var expectedSuffixes = new Dictionary<int, string>
        {
            { 2, "" }, { 3, "" }, { 4, "" }, { 5, "" }, { 6, "" },
            { 7, "" }, { 8, "" }, { 9, "" }, { 10, "" },
            { 11, "2" }, { 12, "2" }, { 13, "2" }, { 14, "" }
        };

        foreach (var suit in suits)
        {
            foreach (var rank in Enumerable.Range(2, 13))
            {
                var card = new OhHell.Core.Card(suit, rank);
                var path = (string)method!.Invoke(null, new object[] { card })!;
                Assert.StartsWith("_content/OhHell.Components/cards/svg-cards-1.3/", path);
                Assert.EndsWith(".svg", path);
                Assert.Contains(expectedSuffixes[rank], path);
            }
        }
    }

    [Fact]
    public async Task MobileApp_CardAssets_AllSuitsAccessibleViaHttp()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var suits = new[] { "spades", "hearts", "diamonds", "clubs" };

        foreach (var suit in suits)
        {
            for (int rank = 2; rank <= 14; rank++)
            {
                var rankName = rank switch
                {
                    <= 10 => rank.ToString(),
                    11 => "jack",
                    12 => "queen",
                    13 => "king",
                    14 => "ace",
                    _ => rank.ToString()
                };
                var suffix = rank is 11 or 12 or 13 ? "2" : "";
                var url = $"/_content/OhHell.Components/cards/svg-cards-1.3/{rankName}_of_{suit}{suffix}.svg";
                var response = await client.GetAsync(url);
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"Card asset not found: {url} returned {response.StatusCode}");
            }
        }
    }

    [Fact]
    public void MobileApp_IndexHtml_HasBlazorWebViewScript()
    {
        var html = File.ReadAllText(Path.Combine(Root, "OhHell.Maui", "wwwroot", "index.html"));
        Assert.Contains("blazor.webview.js", html);
        Assert.Contains("_content/OhHell.Components/app.css", html);
    }

    [Fact]
    public void MobileApp_IndexHtml_ViewportFitCover()
    {
        var html = File.ReadAllText(Path.Combine(Root, "OhHell.Maui", "wwwroot", "index.html"));
        Assert.Contains("viewport-fit=cover", html);
    }

    [Fact]
    public void MobileApp_RoutesRazor_RendersHomeComponent()
    {
        var content = File.ReadAllText(Path.Combine(Root, "OhHell.Maui", "Components", "Routes.razor"));
        Assert.Contains("<Home />", content);
        Assert.Contains("LayoutView", content);
        Assert.Contains("MainLayout", content);
    }

    [Fact]
    public void MobileApp_MauiProgram_RegistersAllServices()
    {
        var content = File.ReadAllText(Path.Combine(Root, "OhHell.Maui", "MauiProgram.cs"));
        Assert.Contains("AddMauiBlazorWebView", content);
        Assert.Contains("IPlatformStorage", content);
        Assert.Contains("MauiStorage", content);
        Assert.Contains("MultiplayerGameService", content);
        Assert.Contains("GameSession", content);
    }

    [Fact]
    public void MobileApp_MainActivity_UsesEdgeToEdge()
    {
        var content = File.ReadAllText(Path.Combine(Root, "OhHell.Maui", "Platforms", "Android", "MainActivity.cs"));
        Assert.Contains("LayoutNoLimits", content);
    }

    [Fact]
    public void MobileApp_StatusBarSafeArea_CssPresent()
    {
        var css = File.ReadAllText(Path.Combine(Root, "OhHell.Components", "wwwroot", "app.css"));
        Assert.Contains("safe-area-inset-top", css);
    }

    [Fact]
    public void MobileApp_CardOverlapCss_FixedSize()
    {
        var css = File.ReadAllText(Path.Combine(Root, "OhHell.Components", "wwwroot", "app.css"));
        Assert.DoesNotContain("clamp(42px", css);
        Assert.DoesNotContain("clamp(38px", css);
        Assert.Contains("-81px", css);
    }

    [Fact]
    public void MobileApp_Animations_UseCubicBezier()
    {
        var css = File.ReadAllText(Path.Combine(Root, "OhHell.Components", "wwwroot", "app.css"));
        Assert.Contains("cubic-bezier(0.34, 1.56, 0.64, 1)", css);
    }
}
