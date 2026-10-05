using System.Security.Cryptography;
using GiftCards.Domain;
using Microsoft.EntityFrameworkCore;

namespace GiftCards.Application;

public class GiftCardService(IAppDbContext db)
{
    private const int MaxAttempts = 50;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<GiftCardResponse> IssueAsync(CancellationToken ct = default)
    {
        var card = GiftCard.Issue(GenerateCode());
        db.GiftCards.Add(card);
        await db.SaveChangesAsync(ct);
        return ToResponse(card);
    }

    public async Task<GiftCardResponse> GetAsync(string code, CancellationToken ct = default)
        => ToResponse(await FindAsync(code, ct));

    public async Task<IReadOnlyList<TransactionResponse>> GetTransactionsAsync(string code, CancellationToken ct = default)
    {
        var card = await FindAsync(code, ct);
        var entries = await db.Ledger.AsNoTracking()
            .Where(e => e.GiftCardId == card.Id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        return entries.Select(e => ToResponse(e, replayed: false)).ToList();
    }

    public Task<TransactionResponse> ActivateAsync(string code, ActivateRequest request, string? idempotencyKey, CancellationToken ct = default)
        => ExecuteAsync(code, idempotencyKey, LedgerEntryType.Activation, request.Amount, null,
            (card, minor, _, key) => card.Activate(minor, key), ct);

    public Task<TransactionResponse> RedeemAsync(string code, RedeemRequest request, string? idempotencyKey, CancellationToken ct = default)
        => ExecuteAsync(code, idempotencyKey, LedgerEntryType.Redemption, request.Amount, request.Reference,
            (card, minor, reference, key) => card.Redeem(minor, reference, key), ct);

    public Task<TransactionResponse> RefundAsync(string code, RefundRequest request, string? idempotencyKey, CancellationToken ct = default)
        => ExecuteAsync(code, idempotencyKey, LedgerEntryType.Refund, request.Amount, request.Reference,
            (card, minor, reference, key) => card.Refund(minor, reference, key), ct);

    /// <summary>
    /// Ejecuta una operación sobre la tarjeta garantizando:
    ///  - Idempotencia: repetir la misma Idempotency-Key devuelve el resultado original sin volver a aplicar el movimiento.
    ///  - Concurrencia: si otra operación modificó la tarjeta, se recarga y se reintenta (concurrencia optimista).
    /// </summary>
    private async Task<TransactionResponse> ExecuteAsync(
        string code, string? idempotencyKey, LedgerEntryType type, decimal amount, string? reference,
        Func<GiftCard, long, string?, string, LedgerEntry> operation, CancellationToken ct)
    {
        var key = ValidateKey(idempotencyKey);
        var minor = Money.ToMinor(amount);
        code = NormalizeCode(code);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var card = await db.GiftCards.FirstOrDefaultAsync(c => c.Code == code, ct)
                       ?? throw new NotFoundException("Tarjeta no encontrada.");

            var existing = await FindEntryAsync(card.Id, key, ct);
            if (existing is not null)
                return Replay(existing, type, minor, reference);

            var entry = operation(card, minor, reference, key);
            db.Ledger.Add(entry);

            try
            {
                await db.SaveChangesAsync(ct);
                return ToResponse(entry, replayed: false);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ResetTracking();
                await Task.Delay(Random.Shared.Next(1, 8), ct);
            }
            catch (DbUpdateException)
            {
                // Posible carrera entre dos peticiones con la misma Idempotency-Key (índice único).
                db.ResetTracking();
                var raced = await FindEntryAsync(card.Id, key, ct);
                if (raced is not null)
                    return Replay(raced, type, minor, reference);
                throw;
            }
        }

        throw new ConcurrencyConflictException("Demasiadas operaciones simultáneas sobre la tarjeta. Reintente.");
    }

    private Task<LedgerEntry?> FindEntryAsync(Guid cardId, string key, CancellationToken ct)
        => db.Ledger.AsNoTracking().FirstOrDefaultAsync(e => e.GiftCardId == cardId && e.IdempotencyKey == key, ct);

    private static TransactionResponse Replay(LedgerEntry existing, LedgerEntryType type, long minor, string? reference)
    {
        if (existing.Type != type || existing.AmountMinor != minor || existing.Reference != reference)
            throw new IdempotencyConflictException("La Idempotency-Key ya fue usada con una petición distinta.");

        return ToResponse(existing, replayed: true);
    }

    private async Task<GiftCard> FindAsync(string code, CancellationToken ct)
        => await db.GiftCards.AsNoTracking().FirstOrDefaultAsync(c => c.Code == NormalizeCode(code), ct)
           ?? throw new NotFoundException("Tarjeta no encontrada.");

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static string ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new RequestValidationException("El encabezado Idempotency-Key es obligatorio.");
        if (key.Length > 100)
            throw new RequestValidationException("Idempotency-Key admite como máximo 100 caracteres.");
        return key.Trim();
    }

    private static string GenerateCode()
    {
        static string Group() => new(Enumerable.Range(0, 4)
            .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());

        return $"GC-{Group()}-{Group()}-{Group()}";
    }

    private static GiftCardResponse ToResponse(GiftCard c)
        => new(c.Code, Money.FromMinor(c.BalanceMinor), c.Currency, c.Status, c.CreatedAt, c.ActivatedAt);

    private static TransactionResponse ToResponse(LedgerEntry e, bool replayed)
        => new(e.Id, e.Type, Money.FromMinor(e.AmountMinor), Money.FromMinor(e.BalanceAfterMinor),
            e.Reference, e.CreatedAt, replayed);
}
