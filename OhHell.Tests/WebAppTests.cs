using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace OhHell.Tests;

public class WebAppTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WebAppTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HomePage_ReturnsOk()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HomePage_ContainsBlazorFrameworkScript()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("blazor", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomePage_ContainsGameTitle()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Oh Hell", html);
    }

    [Fact]
    public async Task HomePage_ContainsToastScript()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("ohHellToast", html);
    }

    [Fact]
    public async Task HomePage_ContainsCssLink()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("app.css", html);
    }

    [Fact]
    public async Task CssFile_IsAccessible()
    {
        var response = await _client.GetAsync("/_content/OhHell.Components/app.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SvgCard_IsAccessible()
    {
        var response = await _client.GetAsync("/_content/OhHell.Components/cards/svg-cards-1.3/ace_of_spades.svg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SvgCard_AllFourSuits_AreAccessible()
    {
        var suits = new[] { "spades", "hearts", "diamonds", "clubs" };
        foreach (var suit in suits)
        {
            var response = await _client.GetAsync($"/_content/OhHell.Components/cards/svg-cards-1.3/ace_of_{suit}.svg");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task SvgCard_JackWithSuffix_IsAccessible()
    {
        var response = await _client.GetAsync("/_content/OhHell.Components/cards/svg-cards-1.3/jack_of_spades2.svg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SvgCard_KingWithSuffix_IsAccessible()
    {
        var response = await _client.GetAsync("/_content/OhHell.Components/cards/svg-cards-1.3/king_of_hearts2.svg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SvgCard_QueenWithSuffix_IsAccessible()
    {
        var response = await _client.GetAsync("/_content/OhHell.Components/cards/svg-cards-1.3/queen_of_diamonds2.svg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SvgCard_NumberCards_NoSuffix_AreAccessible()
    {
        for (int rank = 2; rank <= 10; rank++)
        {
            var response = await _client.GetAsync($"/_content/OhHell.Components/cards/svg-cards-1.3/{rank}_of_clubs.svg");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task SvgCard_AceAllRanks_AreAccessible()
    {
        var ranks = new[] { "2", "3", "4", "5", "6", "7", "8", "9", "10", "jack", "queen", "king", "ace" };
        foreach (var rank in ranks)
        {
            var suffix = rank is "jack" or "queen" or "king" ? "2" : "";
            var response = await _client.GetAsync($"/_content/OhHell.Components/cards/svg-cards-1.3/{rank}_of_spades{suffix}.svg");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
