using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ShortLink.Api.Data;

namespace ShortLink.Api.Links;

public record CreateLinkRequest(
    [property: Required, MaxLength(2048)] string Url,
    [property: RegularExpression("^[A-Za-z0-9_-]{3,32}$", ErrorMessage = "Use 3-32 letters, digits, '-' or '_'.")] string? Alias,
    DateTime? ExpiresAt);

public record CreateLinkResponse(string Code, string ShortUrl, string TargetUrl, DateTime CreatedAt, DateTime? ExpiresAt, string OwnerKey);

public record LinkResponse(string Code, string ShortUrl, string TargetUrl, long ClickCount, DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LastClickedAt);

public static class LinkEndpoints
{
    public static void MapLinks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/links").WithTags("Links");
        g.MapPost("/", CreateAsync).RequireRateLimiting("create").AddEndpointFilter<ApiKeyFilter>();
        g.MapGet("/{code}", GetAsync);
        g.MapGet("/{code}/qr", QrAsync);
        g.MapDelete("/{code}", DeleteAsync);

        app.MapGet("/r/{code}", RedirectAsync).WithTags("Redirect");
    }

    private static async Task<Results<Created<CreateLinkResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateLinkRequest req, LinkService links, IOptions<ShortLinkOptions> options, HttpContext http, CancellationToken ct)
    {
        var (link, ownerKey, error) = await links.CreateAsync(req.Url, req.Alias, req.ExpiresAt, options.Value, http.Request.Host.Value ?? "", ct);
        return error switch
        {
            CreateError.InvalidUrl => Field("url", "Enter a full http(s) address, like https://example.com/page."),
            CreateError.HttpNotAllowed => Field("url", "Only https addresses are allowed."),
            CreateError.SelfReference => Field("url", "That address is already a short link."),
            CreateError.ExpiryInPast => Field("expiresAt", "Pick an expiry date in the future."),
            CreateError.AliasReserved => Field("alias", "That alias is reserved. Pick another one."),
            CreateError.AliasTaken => TypedResults.Problem("Pick another alias.", statusCode: 409, title: "Alias already in use"),
            _ => TypedResults.Created($"/api/links/{link!.Code}",
                new CreateLinkResponse(link.Code, ShortUrl(http, options.Value, link.Code), link.TargetUrl, link.CreatedAt, link.ExpiresAt, ownerKey!))
        };
    }

    private static async Task<Results<Ok<LinkResponse>, NotFound>> GetAsync(
        string code, LinkService links, IOptions<ShortLinkOptions> options, HttpContext http, CancellationToken ct)
    {
        var link = await links.FindAsync(code, ct);
        return link is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(link, ShortUrl(http, options.Value, code)));
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> QrAsync(
        string code, LinkService links, IOptions<ShortLinkOptions> options, HttpContext http, CancellationToken ct)
    {
        if (await links.FindAsync(code, ct) is null) return TypedResults.NotFound();
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return TypedResults.File(LinkService.QrPng(ShortUrl(http, options.Value, code)), "image/png", $"{code}.png");
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        string code, HttpContext http, LinkService links, CancellationToken ct)
    {
        var owner = http.Request.Headers["X-Owner-Key"].FirstOrDefault();
        return await links.DeleteAsync(code, owner, ct) switch
        {
            null => TypedResults.NotFound(),
            false => TypedResults.Problem("Send the owner key you got when the link was created in the X-Owner-Key header.", statusCode: 403, title: "Wrong owner key"),
            true => TypedResults.NoContent()
        };
    }

    private static async Task<IResult> RedirectAsync(string code, LinkService links, CancellationToken ct)
    {
        var result = await links.ResolveAsync(code, ct);
        if (result.IsExpired)
            return TypedResults.Problem("This short link has expired.", statusCode: 410, title: "Link expired");
        return result.TargetUrl is null
            ? TypedResults.Problem("No short link uses this code.", statusCode: 404, title: "Link not found")
            : TypedResults.Redirect(result.TargetUrl, permanent: false, preserveMethod: false);
    }

    private static ValidationProblem Field(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string ShortUrl(HttpContext http, ShortLinkOptions options, string code)
    {
        var origin = string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? $"{http.Request.Scheme}://{http.Request.Host}" : options.PublicBaseUrl.TrimEnd('/');
        return $"{origin}/r/{code}";
    }

    private static LinkResponse ToResponse(Link l, string shortUrl) =>
        new(l.Code, shortUrl, l.TargetUrl, l.ClickCount, l.CreatedAt, l.ExpiresAt, l.LastClickedAt);
}

/// <summary>When Api:Key is set, creating links needs a matching X-Api-Key header. Handy for private deployments.</summary>
public sealed class ApiKeyFilter(IConfiguration config) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = config["Api:Key"];
        if (string.IsNullOrEmpty(expected))
            return await next(ctx);

        var sent = ctx.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "";
        var ok = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(sent), System.Text.Encoding.UTF8.GetBytes(expected));
        return ok ? await next(ctx) : TypedResults.Problem("Send a valid X-Api-Key header.", statusCode: 401, title: "API key required");
    }
}
