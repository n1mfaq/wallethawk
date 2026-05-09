using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure.Tron;

namespace WalletHawk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWalletHawkInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TronGridOptions>(config.GetSection(TronGridOptions.SectionName));

        services.AddHttpClient<ITronExplorerClient, TronGridClient>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));

        return services;
    }
}
