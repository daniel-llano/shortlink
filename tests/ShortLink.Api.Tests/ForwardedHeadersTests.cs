using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShortLink.Api.Links;

namespace ShortLink.Api.Tests;

public class ForwardedHeadersTests(ShortLinkFactory factory) : IClassFixture<ShortLinkFactory>
{
    private static readonly IPAddress Peer = IPAddress.Parse("203.0.113.10");

    [Fact]
    public async Task SpoofedForwardedFor_FromUnknownPeer_IsIgnoredByTheRateLimit()
    {
        var client = ClientFromPeer(trustPeer: false);

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 3; i++)
            statuses.Add(await SendAsync(client, $"198.51.100.{i}"));

        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ForwardedFor_FromKnownProxy_IsUsedAsTheClientIp()
    {
        var client = ClientFromPeer(trustPeer: true);

        for (var i = 1; i <= 3; i++)
            (await SendAsync(client, $"198.51.100.{i}")).Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string forwardedFor)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/links") { Content = JsonContent.Create(new CreateLinkRequest("https://example.com/forwarded", null, null)) };
        req.Headers.Add("X-Forwarded-For", forwardedFor);
        using var resp = await client.SendAsync(req);
        return resp.StatusCode;
    }

    private HttpClient ClientFromPeer(bool trustPeer) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("RateLimiting:CreatesPerMinute", "2");
        if (trustPeer) b.UseSetting("ForwardedHeaders:KnownProxies:0", Peer.ToString());
        b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new PeerAddress(Peer)));
    }).CreateClient();

    /// <summary>TestServer has no socket, so give every request the same remote address, as a single proxy would.</summary>
    private sealed class PeerAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.Connection.RemoteIpAddress = address;
                return nxt(ctx);
            });
            next(app);
        };
    }
}
