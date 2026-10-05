using System.ComponentModel.DataAnnotations;
using GiftCards.Domain;

namespace GiftCards.Application;

public record ActivateRequest([Required] decimal Amount);

public record RedeemRequest([Required] decimal Amount, [StringLength(100)] string? Reference);

public record RefundRequest([Required] decimal Amount, [StringLength(100)] string? Reference);

public record GiftCardResponse(
    string Code,
    decimal Balance,
    string Currency,
    GiftCardStatus Status,
    DateTime CreatedAt,
    DateTime? ActivatedAt);

public record TransactionResponse(
    Guid Id,
    LedgerEntryType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Reference,
    DateTime CreatedAt,
    bool Replayed);
