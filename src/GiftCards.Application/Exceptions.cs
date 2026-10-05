namespace GiftCards.Application;

public class NotFoundException(string message) : Exception(message);

/// <summary>Petición mal formada (por ejemplo, falta el encabezado Idempotency-Key). HTTP 400.</summary>
public class RequestValidationException(string message) : Exception(message);

/// <summary>Se reutilizó una Idempotency-Key con una petición distinta. HTTP 422.</summary>
public class IdempotencyConflictException(string message) : Exception(message);

/// <summary>No se pudo completar por exceso de operaciones concurrentes sobre la misma tarjeta. HTTP 409.</summary>
public class ConcurrencyConflictException(string message) : Exception(message);
