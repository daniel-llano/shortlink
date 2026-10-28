using Microsoft.EntityFrameworkCore;

namespace ShortLink.Api.Data;

public class Link
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string TargetUrl { get; set; } = "";

    // SHA-256 of the owner key. The key itself is shown once, when the link is created.
    public string OwnerKeyHash { get; set; } = "";
    public long ClickCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastClickedAt { get; set; }
}

public class ShortLinkDbContext(DbContextOptions<ShortLinkDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Link>(e =>
        {
            e.Property(l => l.Code).HasMaxLength(32).IsRequired();
            e.HasIndex(l => l.Code).IsUnique();
            e.Property(l => l.TargetUrl).HasMaxLength(2048).IsRequired();
            e.Property(l => l.OwnerKeyHash).HasMaxLength(64).IsRequired();
        });
    }
}
