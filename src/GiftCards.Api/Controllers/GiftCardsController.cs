using GiftCards.Application;
using Microsoft.AspNetCore.Mvc;

namespace GiftCards.Api.Controllers;

[ApiController]
[Route("api/giftcards")]
public class GiftCardsController(GiftCardService service) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Emite una tarjeta nueva (sin saldo, pendiente de activación).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GiftCardResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Issue(CancellationToken ct)
    {
        var card = await service.IssueAsync(ct);
        return CreatedAtAction(nameof(Get), new { code = card.Code }, card);
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<GiftCardResponse>> Get(string code, CancellationToken ct)
        => Ok(await service.GetAsync(code, ct));

    [HttpGet("{code}/transactions")]
    public async Task<ActionResult<IReadOnlyList<TransactionResponse>>> Transactions(string code, CancellationToken ct)
        => Ok(await service.GetTransactionsAsync(code, ct));

    /// <summary>Activa la tarjeta con un saldo inicial. Requiere Idempotency-Key.</summary>
    [HttpPost("{code}/activate")]
    public async Task<ActionResult<TransactionResponse>> Activate(
        string code, ActivateRequest request, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, CancellationToken ct)
        => Respond(await service.ActivateAsync(code, request, idempotencyKey, ct));

    /// <summary>Redime (cobra) un monto de la tarjeta. Requiere Idempotency-Key para poder reintentar sin cobrar dos veces.</summary>
    [HttpPost("{code}/redeem")]
    public async Task<ActionResult<TransactionResponse>> Redeem(
        string code, RedeemRequest request, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, CancellationToken ct)
        => Respond(await service.RedeemAsync(code, request, idempotencyKey, ct));

    /// <summary>Reembolsa parte de lo ya redimido. Requiere Idempotency-Key.</summary>
    [HttpPost("{code}/refund")]
    public async Task<ActionResult<TransactionResponse>> Refund(
        string code, RefundRequest request, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, CancellationToken ct)
        => Respond(await service.RefundAsync(code, request, idempotencyKey, ct));

    private ActionResult<TransactionResponse> Respond(TransactionResponse result)
    {
        if (result.Replayed)
            Response.Headers["Idempotent-Replayed"] = "true";

        return Ok(result);
    }
}
