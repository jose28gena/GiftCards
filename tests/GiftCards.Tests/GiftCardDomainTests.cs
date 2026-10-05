using GiftCards.Domain;

namespace GiftCards.Tests;

/// <summary>Pruebas unitarias de las reglas de negocio, sin base de datos ni HTTP.</summary>
public class GiftCardDomainTests
{
    private static GiftCard ActiveCard(long balanceMinor)
    {
        var card = GiftCard.Issue("GC-TEST-TEST-TEST");
        card.Activate(balanceMinor, "act");
        return card;
    }

    [Fact]
    public void Redeem_cannot_exceed_balance()
    {
        var card = ActiveCard(1000);

        Assert.Throws<InsufficientFundsException>(() => card.Redeem(1001, null, "k"));
        Assert.Equal(1000, card.BalanceMinor);
    }

    [Fact]
    public void Redeem_entry_records_balance_after_and_bumps_version()
    {
        var card = ActiveCard(1000);
        var versionBefore = card.Version;

        var entry = card.Redeem(250, "t1", "k");

        Assert.Equal(750, entry.BalanceAfterMinor);
        Assert.Equal(750, card.BalanceMinor);
        Assert.Equal(versionBefore + 1, card.Version);
    }

    [Fact]
    public void Refund_is_limited_to_what_was_redeemed()
    {
        var card = ActiveCard(1000);
        card.Redeem(300, null, "r");
        card.Refund(200, null, "f1");

        Assert.Throws<RuleViolationException>(() => card.Refund(101, null, "f2"));
        card.Refund(100, null, "f3");

        // 1000 - 300 redimido + 200 + 100 reembolsado = saldo original
        Assert.Equal(1000, card.BalanceMinor);
        Assert.Throws<RuleViolationException>(() => card.Refund(1, null, "f4"));
    }

    [Fact]
    public void Issued_card_cannot_be_redeemed()
    {
        var card = GiftCard.Issue("GC-TEST-TEST-TEST");

        Assert.Throws<InvalidStateException>(() => card.Redeem(10, null, "k"));
    }

    [Fact]
    public void Activate_twice_is_rejected()
    {
        var card = ActiveCard(500);

        Assert.Throws<InvalidStateException>(() => card.Activate(500, "again"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_amounts_are_rejected(long amount)
    {
        var card = ActiveCard(500);

        Assert.Throws<InvalidAmountException>(() => card.Redeem(amount, null, "k"));
    }
}
