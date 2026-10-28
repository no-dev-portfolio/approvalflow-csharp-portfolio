using Microsoft.EntityFrameworkCore;
namespace ApprovalFlow;

public sealed class UserAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Requester";
    public string PasswordHash { get; set; } = "";
}
public sealed class Request
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public UserAccount Owner { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Purpose { get; set; } = "";
    public long Amount { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class Audit
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;
    public int ActorId { get; set; }
    public UserAccount Actor { get; set; } = null!;
    public string Action { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}
public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<Audit> Audits => Set<Audit>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<UserAccount>().HasIndex(x => x.Email).IsUnique();
        m.Entity<UserAccount>().Property(x => x.Email).HasMaxLength(200);
        m.Entity<UserAccount>().Property(x => x.Name).HasMaxLength(80);
        m.Entity<UserAccount>().Property(x => x.Role).HasMaxLength(20);
        m.Entity<UserAccount>().Property(x => x.PasswordHash).HasMaxLength(500);
        m.Entity<Request>().Property(x => x.Title).HasMaxLength(100);
        m.Entity<Request>().Property(x => x.Purpose).HasMaxLength(2000);
        m.Entity<Request>().Property(x => x.Status).HasMaxLength(20);
        m.Entity<Request>().Property(x => x.Version).IsConcurrencyToken();
        // SQL Server datetime2 has no DateTimeKind. Existing values are UTC; restore that metadata on reads.
        m.Entity<Request>().Property(x => x.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        m.Entity<Audit>().Property(x => x.At).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        m.Entity<Request>().HasIndex(x => new { x.OwnerId, x.Status, x.Id });
        m.Entity<Request>().HasIndex(x => new { x.Status, x.Id });
        m.Entity<Request>().HasOne(x => x.Owner).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<Request>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Amount", "Amount > 0 AND Amount <= 100000000");
            t.HasCheckConstraint("CK_Status", "Status IN ('Draft','Submitted','Approved','Returned')");
        });
        m.Entity<Audit>().Property(x => x.Action).HasMaxLength(40);
        m.Entity<Audit>().Property(x => x.Comment).HasMaxLength(500);
        m.Entity<Audit>().HasOne(x => x.Actor).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<Audit>().HasIndex(x => new { x.RequestId, x.Id });
    }
}
public record RequestInput(string? Title, string? Purpose, long Amount, Guid Version);
public record DecisionInput(string? Action, string? Comment, Guid Version);
public record LoginInput(string? Email, string? Password);
