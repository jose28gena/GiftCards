namespace GiftCards.Domain;

/// <summary>Movimiento inmutable del libro de una tarjeta. El saldo siempre se puede reconstruir sumando movimientos.</summary>
public class LedgerEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid GiftCardId { get; init; }
    public LedgerEntryType Type { get; init; }
    public long AmountMinor { get; init; }
    public long BalanceAfterMinor { get; init; }
    public string? Reference { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
