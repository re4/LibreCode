using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Searches data segments in a loaded WASM binary for printable ASCII strings,
/// with optional filter and configurable minimum length.
/// </summary>
public partial class WasmStringSearchSubView : UserControl
{
    public WasmStringSearchSubView()
    {
        InitializeComponent();
    }

    private WasmAnalysisService Svc => App.Services.GetRequiredService<WasmAnalysisService>();

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public async void OnWasmLoaded()
    {
        try
        {
            ResultsGrid.ItemsSource = await Svc.SearchStringsAsync(null);
        }
        catch { }
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        var filter = FilterBox.Text?.Trim();
        if (!int.TryParse(MinLenBox.Text?.Trim(), out var minLen))
            minLen = 4;

        try
        {
            ResultsGrid.ItemsSource = await Svc.SearchStringsAsync(
                string.IsNullOrEmpty(filter) ? null : filter, minLen);
        }
        catch { }
    }
}
