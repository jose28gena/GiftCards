using GiftCards.Application;
using GiftCards.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GiftCards.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, title) = ex switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
                RequestValidationException or InvalidAmountException => (StatusCodes.Status400BadRequest, "Petición inválida"),
                InvalidStateException or ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Conflicto"),
                RuleViolationException or IdempotencyConflictException => (StatusCodes.Status422UnprocessableEntity, "Regla de negocio incumplida"),
                _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor")
            };

            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Excepción no controlada");

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError ? null : ex.Message
            });
        }
    }
}
