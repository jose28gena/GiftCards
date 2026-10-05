using System.Net;
using System.Net.Http.Json;
using GiftCards.Application;
using GiftCards.Domain;
using static GiftCards.Tests.TestApiFactory;

namespace GiftCards.Tests;

public class GiftCardTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private HttpClient Pos => factory.CreatePosClient();

    // ---------- Seguridad ----------

    [Fact]
    public async Task Requests_without_api_key_are_rejected()
    {
        var response = await factory.CreateClient().PostAsync("/api/giftcards", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Emisión y activación ----------

    [Fact]
    public async Task Issue_creates_a_card_without_balance_pending_activation()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client);

        var card = await GetCardAsync(client, code);

        Assert.Matches(@"^GC-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", code);
        Assert.Equal(GiftCardStatus.Issued, card.Status);
        Assert.Equal(0m, card.Balance);
    }

    [Fact]
    public async Task Activate_sets_balance_and_cannot_be_repeated_with_another_key()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, activateWith: 500m);

        var second = await Post(client, $"/api/giftcards/{code}/activate", new ActivateRequest(900m), "otra-clave");
        var card = await GetCardAsync(client, code);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(500m, card.Balance);
        Assert.Equal(GiftCardStatus.Active, card.Status);
    }

    [Fact]
    public async Task Unknown_card_returns_404()
    {
        var response = await Pos.GetAsync("/api/giftcards/GC-NOPE-NOPE-NOPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Redención ----------

    [Fact]
    public async Task Redeem_reduces_balance_and_records_a_ledger_entry()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 200m);

        var response = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(75.50m, "ticket-1"), "k1");
        var tx = await response.Content.ReadFromJsonAsync<TransactionResponse>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(124.50m, tx!.BalanceAfter);
        Assert.Equal(124.50m, (await GetCardAsync(client, code)).Balance);
    }

    [Fact]
    public async Task Redeem_more_than_balance_returns_422_and_keeps_balance()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        var response = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(100.01m, null), "k1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(100m, (await GetCardAsync(client, code)).Balance);
    }

    [Fact]
    public async Task Redeem_on_a_card_that_is_not_active_returns_409()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client);

        var response = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(10m, null), "k1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    [InlineData(1000000.01)]
    public async Task Invalid_amounts_return_400(double amount)
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        var response = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest((decimal)amount, null), "k1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        var response = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(10m, null), idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Idempotencia ----------

    [Fact]
    public async Task Repeating_a_redeem_with_the_same_key_charges_only_once()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);
        var body = new RedeemRequest(30m, "ticket-9");

        var first = await Post(client, $"/api/giftcards/{code}/redeem", body, "retry-key");
        var second = await Post(client, $"/api/giftcards/{code}/redeem", body, "retry-key");

        var firstTx = await first.Content.ReadFromJsonAsync<TransactionResponse>(Json);
        var secondTx = await second.Content.ReadFromJsonAsync<TransactionResponse>(Json);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstTx!.Id, secondTx!.Id);
        Assert.True(secondTx.Replayed);
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(70m, (await GetCardAsync(client, code)).Balance);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_amount_returns_422()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(30m, null), "same-key");
        var conflict = await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(40m, null), "same-key");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        Assert.Equal(70m, (await GetCardAsync(client, code)).Balance);
    }

    [Fact]
    public async Task Parallel_requests_with_the_same_key_charge_only_once()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(10m, null), "double-click")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(90m, (await GetCardAsync(client, code)).Balance);
    }

    // ---------- Concurrencia ----------

    [Fact]
    public async Task Concurrent_redemptions_never_overdraw_the_card()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);

        // 30 cobros distintos de 10.00 contra un saldo de 100.00: solo 10 pueden proceder.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(i => Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(10m, $"t-{i}"), $"key-{i}")));

        var ok = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var transactions = await client.GetFromJsonAsync<List<TransactionResponse>>($"/api/giftcards/{code}/transactions", Json);

        Assert.Equal(10, ok);
        Assert.Equal(20, rejected);
        Assert.Equal(0m, (await GetCardAsync(client, code)).Balance);
        Assert.Equal(11, transactions!.Count); // 1 activación + 10 redenciones
    }

    // ---------- Reembolsos y libro ----------

    [Fact]
    public async Task Refund_returns_money_but_never_more_than_was_redeemed()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);
        await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(40m, "ticket-1"), "r1");

        var ok = await Post(client, $"/api/giftcards/{code}/refund", new RefundRequest(15m, "ticket-1"), "f1");
        var tooMuch = await Post(client, $"/api/giftcards/{code}/refund", new RefundRequest(26m, "ticket-1"), "f2");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        Assert.Equal(75m, (await GetCardAsync(client, code)).Balance);
    }

    [Fact]
    public async Task Ledger_lists_every_movement_in_order_and_balance_can_be_rebuilt()
    {
        var client = Pos;
        var code = await factory.IssueCardAsync(client, 100m);
        await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(30m, null), "a");
        await Post(client, $"/api/giftcards/{code}/redeem", new RedeemRequest(20m, null), "b");
        await Post(client, $"/api/giftcards/{code}/refund", new RefundRequest(10m, null), "c");

        var ledger = await client.GetFromJsonAsync<List<TransactionResponse>>($"/api/giftcards/{code}/transactions", Json);

        Assert.Equal(
            [LedgerEntryType.Activation, LedgerEntryType.Redemption, LedgerEntryType.Redemption, LedgerEntryType.Refund],
            ledger!.Select(l => l.Type));

        var rebuilt = ledger!.Sum(l => l.Type switch
        {
            LedgerEntryType.Redemption => -l.Amount,
            _ => l.Amount
        });
        Assert.Equal((await GetCardAsync(client, code)).Balance, rebuilt);
    }
}
