using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LibreCode.Services.Ollama;
using LibreCode.Services.FileSystem;
using LibreCode.Features.Chat;
using LibreCode.Features.InlineEdit;
using LibreCode.Features.Autocomplete;
using LibreCode.Features.Agent;
using LibreCode.Features.Context;
using LibreCode.Features.Marketplace;
using LibreCode.Features.Reversing;
using LibreCode.Services;

namespace LibreCode;

/// <summary>
/// Avalonia application entry. Configures dependency injection and launches the main window.
/// </summary>
public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += OnShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.Configure<OllamaOptions>(
            configuration.GetSection("Ollama"));

        services.AddHttpClient<OllamaClient>(client =>
        {
            var baseUrl = configuration.GetValue<string>("Ollama:BaseUrl")
                          ?? "http://127.0.0.1:11434";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = false
        });

        services.AddSingleton<FileSystemService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<InlineEditService>();
        services.AddSingleton<AutocompleteService>();
        services.AddSingleton<AgentOrchestrator>();
        services.AddSingleton<CodebaseIndexer>();
        services.AddSingleton<EmbeddingStore>();
        services.AddSingleton<GpuDetectionService>();
        services.AddSingleton<OllamaLibraryScraper>();
        services.AddSingleton<SessionPersistenceService>();
        services.AddSingleton<RulesService>();
        services.AddSingleton<AssemblyAnalysisService>();

        Services = services.BuildServiceProvider();
    }

    private void OnShutdown(object? sender, ShutdownRequestedEventArgs e)
    {
        try
        {
            var task = Task.Run(async () =>
            {
                var session = Services.GetRequiredService<SessionPersistenceService>();
                var chat = Services.GetRequiredService<ChatService>();
                var fs = Services.GetRequiredService<FileSystemService>();

                var state = new SessionState
                {
                    ProjectPath = fs.ProjectRoot,
                    ChatHistory = chat.ExportHistory(),
                    SavedAt = DateTime.UtcNow
                };

                await session.SaveAsync(state);
            });

            task.Wait(TimeSpan.FromSeconds(3));
        }
        catch { }
        finally
        {
            TryDispose<CodebaseIndexer>();
            TryDispose<AutocompleteService>();
            TryDispose<AssemblyAnalysisService>();
            TryDispose<FileSystemService>();
        }
    }

    private static void TryDispose<T>() where T : IDisposable
    {
        try { Services.GetService<T>()?.Dispose(); } catch { }
    }
}
