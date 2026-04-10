using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views;

/// <summary>Settings panel for managing AI rules and Ollama configuration.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private RulesService Rules => App.Services.GetRequiredService<RulesService>();

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await Rules.LoadAsync();
        RefreshRulesList();
    }

    private void RefreshRulesList() => RulesList.ItemsSource = Rules.Rules.ToList();

    private async void OnAddRuleClick(object? sender, RoutedEventArgs e)
    {
        var text = NewRuleBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        await Rules.AddRuleAsync(text);
        NewRuleBox.Text = "";
        RefreshRulesList();
    }

    private async void OnRemoveRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UserRule rule })
        {
            await Rules.RemoveRuleAsync(rule.Id);
            RefreshRulesList();
        }
    }
}
