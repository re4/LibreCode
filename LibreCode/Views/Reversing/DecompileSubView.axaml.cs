using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Type tree on the left, decompiled C# source on the right.
/// Clicking a type decompiles only that type for fast navigation.
/// </summary>
public partial class DecompileSubView : UserControl
{
    private TextMate.Installation? _tmInstall;

    public DecompileSubView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var registry = new RegistryOptions(ThemeName.DarkPlus);
        _tmInstall = CodeEditor.InstallTextMate(registry);
        var lang = registry.GetLanguageByExtension(".cs");
        if (lang is not null)
            _tmInstall.SetGrammar(registry.GetScopeByLanguageId(lang.Id));
    }

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public async void OnAssemblyLoaded()
    {
        try
        {
            TypeTree.ItemsSource = await Svc.GetTypeTreeAsync();
            CodeEditor.Text = await Svc.DecompileWholeModuleAsync();
        }
        catch (Exception ex)
        {
            CodeEditor.Text = $"// Error: {ex.Message}";
        }
    }

    private async void OnTypeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TypeTree.SelectedItem is not TypeTreeNode node) return;
        if (node.Kind == TypeTreeNodeKind.Namespace) return;

        try
        {
            CodeEditor.Text = await Svc.DecompileTypeAsync(node.FullName);
        }
        catch (Exception ex)
        {
            CodeEditor.Text = $"// Error: {ex.Message}";
        }
    }

    /// <summary>Returns the currently displayed decompiled source.</summary>
    public string GetCurrentSource() => CodeEditor.Text;
}
