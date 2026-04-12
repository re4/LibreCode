using Avalonia.Controls;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Function tree on the left, WAT-style disassembly on the right.
/// Selecting a function shows only that function's disassembled body.
/// </summary>
public partial class WasmDisassemblySubView : UserControl
{
    public WasmDisassemblySubView()
    {
        InitializeComponent();
    }

    private WasmAnalysisService Svc => App.Services.GetRequiredService<WasmAnalysisService>();

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public async void OnWasmLoaded()
    {
        try
        {
            FuncTree.ItemsSource = await Svc.GetFunctionTreeAsync();
            WatEditor.Text = await Svc.GetFullDisassemblyAsync();
        }
        catch (Exception ex)
        {
            WatEditor.Text = $";; Error: {ex.Message}";
        }
    }

    private async void OnFunctionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (FuncTree.SelectedItem is not WasmFunctionTreeNode node) return;
        if (node.Kind == WasmFunctionTreeNodeKind.Category) return;

        try
        {
            WatEditor.Text = await Svc.GetFunctionDisassemblyAsync(node.FunctionIndex);
        }
        catch (Exception ex)
        {
            WatEditor.Text = $";; Error: {ex.Message}";
        }
    }

    /// <summary>Returns the currently displayed disassembly text.</summary>
    public string GetCurrentSource() => WatEditor.Text;
}
