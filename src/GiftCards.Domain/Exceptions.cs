namespace GiftCards.Domain;

public abstract class DomainException(string message) : Exception(message);

/// <summary>Monto inválido (cero, negativo, con más de 2 decimales). Se traduce a HTTP 400.</summary>
public class InvalidAmountException(string message) : DomainException(message);

/// <summary>La operación no es válida para el estado actual de la tarjeta. Se traduce a HTTP 409.</summary>
public class InvalidStateException(string message) : DomainException(message);

/// <summary>Regla de negocio incumplida (saldo insuficiente, reembolso excesivo). Se traduce a HTTP 422.</summary>
public class RuleViolationException(string message) : DomainException(message);

public class InsufficientFundsException(string message) : RuleViolationException(message);
