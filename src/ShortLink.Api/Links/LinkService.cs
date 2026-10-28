using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using QRCoder;
using ShortLink.Api.Data;

namespace ShortLink.Api.Links;

public sealed class ShortLinkOptions
{
    public const string Section = "Links";

    /// <summary>Public origin used for short URLs and QR codes, e.g. https://sho.rt. Falls back to the request host.</summary>
    public string? PublicBaseUrl { get; set; }
    public bool AllowHttpTargets { get; set; }
    public int CodeLength { get; set; } = 7;
}

public enum CreateError
{
    None,
    InvalidUrl,
    HttpNotAllowed,
    SelfReference,
    AliasTaken,
    AliasReserved,
    ExpiryInPast
}

public sealed partial class LinkService(ShortLinkDbContext db, IDistributedCache cache, TimeProvider clock)
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "r", "health", "docs", "openapi", "assets", "index", "favicon", "admin", "static"
    };

    private static readonly DistributedCacheEntryOptions CacheEntry = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) };

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    public static partial Regex AliasPattern();

    public async Task<(Link? Link, string? OwnerKey, CreateError Error)> CreateAsync(
        string url, string? alias, DateTime? expiresAt, ShortLinkOptions options, string requestHost, CancellationToken ct)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https") || string.IsNullOrEmpty(target.Host))
            return (null, null, CreateError.InvalidUrl);
        if (target.Scheme == "http" && !options.AllowHttpTargets)
            return (null, null, CreateError.HttpNotAllowed);

        var ownHost = Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBase) ? publicBase.Host : requestHost.Split(':')[0];
        if (string.Equals(target.Host, ownHost, StringComparison.OrdinalIgnoreCase))
            return (null, null, CreateError.SelfReference);

        var now = clock.GetUtcNow().UtcDateTime;
        if (expiresAt is { } exp && exp.ToUniversalTime() <= now)
            return (null, null, CreateError.ExpiryInPast);

        string code;
        if (!string.IsNullOrWhiteSpace(alias))
        {
            code = alias.Trim();
            if (Reserved.Contains(code)) return (null, null, CreateError.AliasReserved);
            if (await db.Links.AnyAsync(l => l.Code == code, ct)) return (null, null, CreateError.AliasTaken);
        }
        else
        {
            do code = RandomCode(options.CodeLength);
            while (await db.Links.AnyAsync(l => l.Code == code, ct));
        }

        var ownerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var link = new Link
        {
            Code = code,
            TargetUrl = target.AbsoluteUri,
            OwnerKeyHash = Hash(ownerKey),
            CreatedAt = now,
            ExpiresAt = expiresAt?.ToUniversalTime()
        };
        db.Links.Add(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race for the same code; the unique index caught it.
            db.Entry(link).State = EntityState.Detached;
            if (await db.Links.AsNoTracking().AnyAsync(l => l.Code == code, ct))
                return (null, null, CreateError.AliasTaken);
            throw;
        }
        return (link, ownerKey, CreateError.None);
    }

    public Task<Link?> FindAsync(string code, CancellationToken ct) =>
        db.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code, ct);

    /// <summary>Resolves a code for a redirect and counts the click. The lookup is cached, the increment is a single UPDATE.</summary>
    public async Task<ResolveResult> ResolveAsync(string code, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var entry = await cache.GetStringAsync(CacheKey(code), ct);
        string target;
        DateTime? expiresAt;

        if (entry is not null)
        {
            var parts = entry.Split('\n', 2);
            expiresAt = parts[0].Length == 0 ? null : DateTime.Parse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind);
            target = parts[1];
        }
        else
        {
            var link = await FindAsync(code, ct);
            if (link is null) return ResolveResult.Missing;
            target = link.TargetUrl;
            expiresAt = link.ExpiresAt;
            await cache.SetStringAsync(CacheKey(code), $"{expiresAt?.ToString("O")}\n{target}", CacheEntry, ct);
        }

        if (expiresAt <= now) return ResolveResult.Expired;

        await db.Links.Where(l => l.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ClickCount, l => l.ClickCount + 1).SetProperty(l => l.LastClickedAt, now), ct);
        return new ResolveResult(target, false);
    }

    public async Task<bool?> DeleteAsync(string code, string? ownerKey, CancellationToken ct)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Code == code, ct);
        if (link is null) return null;
        if (string.IsNullOrEmpty(ownerKey)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(ownerKey)), Encoding.ASCII.GetBytes(link.OwnerKeyHash)))
            return false;

        db.Links.Remove(link);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKey(code), ct);
        return true;
    }

    public static byte[] QrPng(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(10);
    }

    private static string RandomCode(int length) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string CacheKey(string code) => $"link:{code}";
}

public record ResolveResult(string? TargetUrl, bool IsExpired)
{
    public static readonly ResolveResult Missing = new(null, false);
    public static readonly ResolveResult Expired = new(null, true);
}
