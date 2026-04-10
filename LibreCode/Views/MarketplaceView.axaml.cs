using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibreCode.Features.Marketplace;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views;

/// <summary>
/// Model marketplace panel. Searches the Ollama library and displays available models.
/// </summary>
public partial class MarketplaceView : UserControl
{
    public MarketplaceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var gpu = App.Services.GetRequiredService<GpuDetectionService>();
        GpuBadge.Text = gpu.GetGpuInfo().Display;
        await LoadModelsAsync();
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await LoadModelsAsync(SearchBox.Text);
    }

    private async Task LoadModelsAsync(string? query = null)
    {
        try
        {
            var scraper = App.Services.GetRequiredService<OllamaLibraryScraper>();
            var models = string.IsNullOrWhiteSpace(query)
                ? await scraper.FetchCatalogAsync()
                : await scraper.SearchAsync(query);
            ModelList.ItemsSource = models;
        }
        catch { }
    }
}
