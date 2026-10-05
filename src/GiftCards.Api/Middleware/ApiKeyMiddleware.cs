using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GiftCards.Api.Middleware;

public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Clave que deben enviar las terminales (POS) en el encabezado X-Api-Key. Se inyecta por entorno.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class ApiKeyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public const string HeaderName = "X-Api-Key";

    private readonly byte[] _expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey));

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

        if (!CryptographicOperations.FixedTimeEquals(providedHash, _expectedHash))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autorizado",
                Detail = $"Falta o es inválido el encabezado {HeaderName}."
            });
            return;
        }

        await next(context);
    }
}
