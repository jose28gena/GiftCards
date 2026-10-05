using Microsoft.Extensions.DependencyInjection;

namespace GiftCards.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
        => services.AddScoped<GiftCardService>();
}
