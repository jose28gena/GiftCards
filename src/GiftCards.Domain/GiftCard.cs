namespace GiftCards.Domain;

public enum GiftCardStatus
{
    Issued,
    Active,
    Blocked
}

public enum LedgerEntryType
{
    Activation,
    Redemption,
    Refund
}

/// <summary>
/// Tarjeta de regalo. Los montos se guardan en unidades menores (centavos) para evitar errores de redondeo.
/// Todas las reglas de negocio viven aquí; los servicios solo orquestan.
/// </summary>
public class GiftCard
{
    private GiftCard() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "MXN";
    public GiftCardStatus Status { get; private set; } = GiftCardStatus.Issued;
    public long BalanceMinor { get; private set; }
    public long RedeemedMinor { get; private set; }
    public long RefundedMinor { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ActivatedAt { get; private set; }

    /// <summary>Token de concurrencia optimista: dos operaciones simultáneas sobre la misma tarjeta no pueden pisarse.</summary>
    public int Version { get; private set; }

    public static GiftCard Issue(string code, string currency = "MXN")
        => new() { Code = code, Currency = currency };

    public LedgerEntry Activate(long amountMinor, string? idempotencyKey)
    {
        EnsurePositive(amountMinor);

        if (Status != GiftCardStatus.Issued)
            throw new InvalidStateException("La tarjeta ya fue activada o está bloqueada.");

        BalanceMinor = amountMinor;
        Status = GiftCardStatus.Active;
        ActivatedAt = DateTime.UtcNow;
        return Record(LedgerEntryType.Activation, amountMinor, null, idempotencyKey);
    }

    public LedgerEntry Redeem(long amountMinor, string? reference, string? idempotencyKey)
    {
        EnsurePositive(amountMinor);
        EnsureActive();

        if (amountMinor > BalanceMinor)
            throw new InsufficientFundsException("Saldo insuficiente.");

        BalanceMinor -= amountMinor;
        RedeemedMinor += amountMinor;
        return Record(LedgerEntryType.Redemption, amountMinor, reference, idempotencyKey);
    }

    public LedgerEntry Refund(long amountMinor, string? reference, string? idempotencyKey)
    {
        EnsurePositive(amountMinor);
        EnsureActive();

        if (amountMinor > RedeemedMinor - RefundedMinor)
            throw new RuleViolationException("El reembolso excede el monto redimido pendiente de reembolsar.");

        BalanceMinor += amountMinor;
        RefundedMinor += amountMinor;
        return Record(LedgerEntryType.Refund, amountMinor, reference, idempotencyKey);
    }

    private void EnsureActive()
    {
        if (Status != GiftCardStatus.Active)
            throw new InvalidStateException("La tarjeta no está activa.");
    }

    private static void EnsurePositive(long amountMinor)
    {
        if (amountMinor <= 0)
            throw new InvalidAmountException("El monto debe ser mayor a cero.");
    }

    private LedgerEntry Record(LedgerEntryType type, long amountMinor, string? reference, string? idempotencyKey)
    {
        Version++;
        return new LedgerEntry
        {
            GiftCardId = Id,
            Type = type,
            AmountMinor = amountMinor,
            BalanceAfterMinor = BalanceMinor,
            Reference = reference,
            IdempotencyKey = idempotencyKey
        };
    }
}
