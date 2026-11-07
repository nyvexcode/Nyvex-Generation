using Microsoft.Extensions.DependencyInjection;
using NyvexGeneration.Data;
using NyvexGeneration.Services;
using NyvexGeneration.ViewModels;
using NyvexGeneration.Views;

namespace NyvexGeneration.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNyvexCore(this IServiceCollection services)
    {
        // Infraestrutura
        services.AddSingleton<AppPaths>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<SettingsState>();
        services.AddSingleton<SessionState>();
        services.AddSingleton<ILogService>(sp => new LogService(sp.GetRequiredService<AppPaths>().LogsDirectory));

        // Dados (SQLite)
        services.AddSingleton(sp => new SqliteDatabase(sp.GetRequiredService<AppPaths>().DatabaseFile));
        services.AddSingleton<IBackupService>(sp => sp.GetRequiredService<SqliteDatabase>());
        services.AddSingleton<ILookupHistoryRepository, SqliteLookupHistoryRepository>();
        services.AddSingleton<IGenerationHistoryRepository, SqliteGenerationHistoryRepository>();
        services.AddSingleton<IFavoritesRepository, SqliteFavoritesRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IStatsRepository, SqliteStatsRepository>();

        // Serviços de domínio
        services.AddSingleton<IUsernameValidator, UsernameValidator>();
        services.AddSingleton<IUsernameGenerator>(sp => new UsernameGenerator(sp.GetRequiredService<IUsernameValidator>()));
        services.AddSingleton<IExportService, ExportService>();

        services.AddHttpClient(RobloxApiService.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://users.roblox.com/");
            client.Timeout = Timeout.InfiniteTimeSpan; // o timeout é controlado por requisição no serviço
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NyvexGeneration/3.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddSingleton<IRobloxApiService>(sp => new RobloxApiService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RobloxApiService.ClientName),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<SettingsState>()));

        services.AddSingleton<ICheckCoordinator, CheckCoordinator>();

        // ViewModels e janela
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<GeneratorViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
