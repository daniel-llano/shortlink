using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortLink.Api.Links;

namespace ShortLink.Api.Tests;

public sealed class ShortLinkFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"shortlink-{Guid.NewGuid():N}.db");

    public string? ApiKey { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:ShortLink", $"Data Source={_db};Pooling=False");
        builder.UseSetting("Links:PublicBaseUrl", "https://sho.rt");
        builder.UseSetting("RateLimiting:CreatesPerMinute", "10000");
        if (ApiKey is not null) builder.UseSetting("Api:Key", ApiKey);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_db); } catch (IOException) { }
    }
}

public class LinkTests(ShortLinkFactory factory) : IClassFixture<ShortLinkFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<CreateLinkResponse> CreateAsync(string url = "https://example.com/some/long/path?q=1", string? alias = null, DateTime? expiresAt = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest(url, alias, expiresAt));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<CreateLinkResponse>())!;
    }

    [Fact]
    public async Task Create_ReturnsShortUrlAndOwnerKey()
    {
        var link = await CreateAsync();

        link.Code.Should().MatchRegex("^[A-Za-z0-9]{7}$");
        link.ShortUrl.Should().Be($"https://sho.rt/r/{link.Code}");
        link.OwnerKey.Should().HaveLength(32);
    }

    [Fact]
    public async Task Redirect_SendsToTargetAndCountsEveryClick()
    {
        var link = await CreateAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _client.GetAsync($"/r/{link.Code}")));
        var redirect = await _client.GetAsync($"/r/{link.Code}");

        redirect.StatusCode.Should().Be(HttpStatusCode.Found);
        redirect.Headers.Location!.ToString().Should().Be("https://example.com/some/long/path?q=1");
        var details = await _client.GetFromJsonAsync<LinkResponse>($"/api/links/{link.Code}");
        details!.ClickCount.Should().Be(21);
        details.LastClickedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://example.com/plain")]
    [InlineData("https://sho.rt/r/abc")]
    public async Task Create_RejectsBadTargets(string url)
    {
        var resp = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest(url, null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("url");
    }

    [Fact]
    public async Task Alias_IsUsedAndMustBeUnique()
    {
        var alias = "sale-" + Guid.NewGuid().ToString("N")[..6];
        var link = await CreateAsync(alias: alias);
        link.Code.Should().Be(alias);

        var again = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest("https://example.org", alias, null));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("ab")]
    [InlineData("has space")]
    public async Task Alias_RejectsReservedOrMalformed(string alias)
    {
        var resp = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest("https://example.org", alias, null));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExpiredLinks_Return410()
    {
        var link = await CreateAsync(expiresAt: DateTime.UtcNow.AddSeconds(2));
        await Task.Delay(2500);

        (await _client.GetAsync($"/r/{link.Code}")).StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Expiry_InThePast_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest("https://example.org", null, DateTime.UtcNow.AddMinutes(-1)));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_NeedsTheOwnerKey()
    {
        var link = await CreateAsync();

        var wrong = new HttpRequestMessage(HttpMethod.Delete, $"/api/links/{link.Code}") { Headers = { { "X-Owner-Key", "nope" } } };
        (await _client.SendAsync(wrong)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.DeleteAsync($"/api/links/{link.Code}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var right = new HttpRequestMessage(HttpMethod.Delete, $"/api/links/{link.Code}") { Headers = { { "X-Owner-Key", link.OwnerKey } } };
        (await _client.SendAsync(right)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/r/{link.Code}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Qr_ReturnsPng()
    {
        var link = await CreateAsync();

        var resp = await _client.GetAsync($"/api/links/{link.Code}/qr");

        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await resp.Content.ReadAsByteArrayAsync())[..4].Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    [Fact]
    public async Task UnknownCode_Returns404()
    {
        (await _client.GetAsync("/r/doesnotexist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync("/api/links/doesnotexist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HomePage_IsServed()
    {
        var html = await _client.GetStringAsync("/");
        html.Should().Contain("<title>ShortLink</title>");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Returns200(string path)
    {
        (await _client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public class ApiKeyTests
{
    [Fact]
    public async Task Create_RequiresKeyWhenConfigured()
    {
        await using var factory = new ShortLinkFactory { ApiKey = "s3cret" };
        var client = factory.CreateClient();
        var body = new CreateLinkRequest("https://example.com", null, null);

        (await client.PostAsJsonAsync("/api/links", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Add("X-Api-Key", "s3cret");
        (await client.PostAsJsonAsync("/api/links", body)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
