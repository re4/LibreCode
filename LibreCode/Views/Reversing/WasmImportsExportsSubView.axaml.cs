using Avalonia.Controls;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Split view showing all WASM imports in the top grid and exports in the bottom grid.
/// </summary>
public partial class WasmImportsExportsSubView : UserControl
{
    public WasmImportsExportsSubView()
    {
        InitializeComponent();
    }

    private WasmAnalysisService Svc => App.Services.GetRequiredService<WasmAnalysisService>();

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public void OnWasmLoaded()
    {
        ImportsGrid.ItemsSource = Svc.GetImports();
        ExportsGrid.ItemsSource = Svc.GetExports();
    }
}
