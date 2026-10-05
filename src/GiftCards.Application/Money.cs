using GiftCards.Domain;

namespace GiftCards.Application;

public static class Money
{
    public const decimal MaxAmount = 1_000_000m;

    public static long ToMinor(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidAmountException("El monto debe ser mayor a cero.");
        if (amount > MaxAmount)
            throw new InvalidAmountException($"El monto máximo por operación es {MaxAmount:N2}.");
        if (decimal.Round(amount, 2) != amount)
            throw new InvalidAmountException("El monto admite como máximo 2 decimales.");

        return (long)(amount * 100m);
    }

    public static decimal FromMinor(long minor) => minor / 100m;
}
