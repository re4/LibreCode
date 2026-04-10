using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Type tree on the left, IL disassembly on the right.
/// Uses ICSharpCode.Decompiler's ReflectionDisassembler for IL output.
/// </summary>
public partial class ILSubView : UserControl
{
    public ILSubView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public async void OnAssemblyLoaded()
    {
        try
        {
            TypeTree.ItemsSource = await Svc.GetTypeTreeAsync();
            ILEditor.Text = await Svc.GetILDisassemblyAsync();
        }
        catch (Exception ex)
        {
            ILEditor.Text = $"// Error: {ex.Message}";
        }
    }

    private async void OnTypeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TypeTree.SelectedItem is not TypeTreeNode node) return;
        if (node.Kind == TypeTreeNodeKind.Namespace) return;

        try
        {
            ILEditor.Text = await Svc.GetILForTypeAsync(node.FullName);
        }
        catch (Exception ex)
        {
            ILEditor.Text = $"// Error: {ex.Message}";
        }
    }
}
