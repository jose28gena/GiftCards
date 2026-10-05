using GiftCards.Domain;
using Microsoft.EntityFrameworkCore;

namespace GiftCards.Application;

public interface IAppDbContext
{
    DbSet<GiftCard> GiftCards { get; }
    DbSet<LedgerEntry> Ledger { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarta las entidades en memoria; se usa antes de reintentar tras un conflicto de concurrencia.</summary>
    void ResetTracking();
}
