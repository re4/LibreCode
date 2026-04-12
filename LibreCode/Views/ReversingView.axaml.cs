using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views;

/// <summary>
/// Unified reversing panel with .NET and WASM format sub-tabs.
/// The top-level tab strip switches between .NET assembly analysis and WASM binary analysis,
/// each with their own file picker and feature sub-tabs.
/// </summary>
public partial class ReversingView : UserControl
{
    private static readonly IBrush ActiveFg = new SolidColorBrush(Color.Parse("#4ec9b0"));
    private static readonly IBrush InactiveFg = new SolidColorBrush(Color.Parse("#999999"));

    public ReversingView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService AsmSvc => App.Services.GetRequiredService<AssemblyAnalysisService>();
    private WasmAnalysisService WasmSvc => App.Services.GetRequiredService<WasmAnalysisService>();

    #region Format Tab Switching

    private void OnFormatTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        var isDotNet = tag == "dotnet";
        DotNetPanel.IsVisible = isDotNet;
        WasmPanel.IsVisible = !isDotNet;

        DotNetTabBtn.Foreground = isDotNet ? ActiveFg : InactiveFg;
        DotNetTabBtn.FontWeight = isDotNet ? FontWeight.Bold : FontWeight.Normal;
        WasmTabBtn.Foreground = !isDotNet ? ActiveFg : InactiveFg;
        WasmTabBtn.FontWeight = !isDotNet ? FontWeight.Bold : FontWeight.Normal;
    }

    #endregion

    #region .NET Assembly

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

    /// <summary>Loads the .NET assembly and notifies all .NET sub-views.</summary>
    public async Task LoadAssemblyAsync(string path)
    {
        try
        {
            LoadedFileLabel.Text = $"Loading {Path.GetFileName(path)}...";
            await AsmSvc.LoadAssemblyAsync(path);
            LoadedFileLabel.Text = Path.GetFileName(path);

            DecompilePanel.OnAssemblyLoaded();
            ILPanel.OnAssemblyLoaded();
            PEPanel.OnAssemblyLoaded();
            StringsPanel.OnAssemblyLoaded();
            HexPanel.OnAssemblyLoaded();
            DebugPanel.OnAssemblyLoaded();
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
        DebugPanel.IsVisible = tag == "debug";
        AIPanel.IsVisible = tag == "ai";
    }

    #endregion

    #region WASM

    private async void OnOpenWasmClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open WebAssembly Binary",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WebAssembly") { Patterns = ["*.wasm"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        await LoadWasmAsync(path);
    }

    /// <summary>Loads the WASM binary and notifies all WASM sub-views.</summary>
    public async Task LoadWasmAsync(string path)
    {
        try
        {
            WasmLoadedFileLabel.Text = $"Loading {Path.GetFileName(path)}...";
            await WasmSvc.LoadWasmAsync(path);
            WasmLoadedFileLabel.Text = Path.GetFileName(path);

            WasmDisasmPanel.OnWasmLoaded();
            WasmInfoPanel.OnWasmLoaded();
            WasmImpExpPanel.OnWasmLoaded();
            WasmStringsPanel.OnWasmLoaded();
            WasmHexPanel.OnWasmLoaded();
            WasmDebugPanel.OnWasmLoaded();
            WasmCdpPanel.OnWasmLoaded();
            WasmAIPanel.OnWasmLoaded();
        }
        catch (Exception ex)
        {
            WasmLoadedFileLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void OnWasmSubTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        WasmDisasmPanel.IsVisible = tag == "disasm";
        WasmInfoPanel.IsVisible = tag == "info";
        WasmImpExpPanel.IsVisible = tag == "impexp";
        WasmStringsPanel.IsVisible = tag == "strings";
        WasmHexPanel.IsVisible = tag == "hex";
        WasmDebugPanel.IsVisible = tag == "debug";
        WasmCdpPanel.IsVisible = tag == "cdp";
        WasmAIPanel.IsVisible = tag == "ai";
    }

    #endregion
}
