using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Searchable list of user strings found in the assembly metadata.
/// </summary>
public partial class StringSearchSubView : UserControl
{
    public StringSearchSubView()
    {
        InitializeComponent();
        FilterBox.KeyDown += OnFilterKeyDown;
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public async void OnAssemblyLoaded()
    {
        await SearchAsync(null);
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        await SearchAsync(FilterBox.Text?.Trim());
    }

    private async void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync(FilterBox.Text?.Trim());
        }
    }

    private async Task SearchAsync(string? filter)
    {
        try
        {
            var results = await Svc.SearchStringsAsync(filter);
            ResultsGrid.ItemsSource = results;
        }
        catch { }
    }
}
