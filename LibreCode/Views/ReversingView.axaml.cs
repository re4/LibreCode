using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views;

/// <summary>
/// Main reversing panel with assembly picker, sub-tab strip, and swappable sub-views.
/// </summary>
public partial class ReversingView : UserControl
{
    public ReversingView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    private async void OnOpenAssemblyClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open .NET Assembly",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Assemblies") { Patterns = ["*.dll", "*.exe"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        await LoadAssemblyAsync(path);
    }

    /// <summary>Loads the assembly and notifies all sub-views.</summary>
    public async Task LoadAssemblyAsync(string path)
    {
        try
        {
            LoadedFileLabel.Text = $"Loading {Path.GetFileName(path)}...";
            await Svc.LoadAssemblyAsync(path);
            LoadedFileLabel.Text = Path.GetFileName(path);

            DecompilePanel.OnAssemblyLoaded();
            ILPanel.OnAssemblyLoaded();
            PEPanel.OnAssemblyLoaded();
            StringsPanel.OnAssemblyLoaded();
            HexPanel.OnAssemblyLoaded();
            AIPanel.OnAssemblyLoaded();
        }
        catch (Exception ex)
        {
            LoadedFileLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void OnSubTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        DecompilePanel.IsVisible = tag == "decompile";
        ILPanel.IsVisible = tag == "il";
        PEPanel.IsVisible = tag == "pe";
        StringsPanel.IsVisible = tag == "strings";
        HexPanel.IsVisible = tag == "hex";
        AIPanel.IsVisible = tag == "ai";
    }
}
