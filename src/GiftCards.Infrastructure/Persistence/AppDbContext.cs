using GiftCards.Application;
using GiftCards.Domain;
using Microsoft.EntityFrameworkCore;

namespace GiftCards.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    public void ResetTracking() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GiftCard>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(20).IsRequired();
            e.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            // Concurrencia optimista: el UPDATE incluye "WHERE Version = <valor leído>".
            e.Property(c => c.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<LedgerEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Reference).HasMaxLength(100);
            e.Property(l => l.IdempotencyKey).HasMaxLength(100);
            e.HasOne<GiftCard>().WithMany().HasForeignKey(l => l.GiftCardId).OnDelete(DeleteBehavior.Restrict);

            // Garantiza a nivel de base de datos que una misma Idempotency-Key no se aplique dos veces por tarjeta.
            e.HasIndex(l => new { l.GiftCardId, l.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });
    }
}
