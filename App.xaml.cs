using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Services;
using NyvexGeneration.ViewModels;
using NyvexGeneration.Views;

namespace NyvexGeneration;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) => services.AddNyvexCore())
                .Build();

            await _host.StartAsync();

            var sp = _host.Services;
            await sp.GetRequiredService<SqliteDatabase>().InitializeAsync(CancellationToken.None);
            await sp.GetRequiredService<MainViewModel>().InitializeAsync();

            sp.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar o Nyvex Generation.\n\n{ex.Message}",
                "Nyvex Generation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _host?.Services.GetService<ILogService>()?.Log(
                LogSeverity.Error, "App", "Exceção não tratada na interface.", e.Exception.Message);
        }
        catch (Exception)
        {
            // Evita falha em cascata durante o tratamento.
        }

        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{e.Exception.Message}\n\nO aplicativo continuará aberto.",
            "Nyvex Generation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
