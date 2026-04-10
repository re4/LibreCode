using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibreCode.ViewModels;

namespace LibreCode;

/// <summary>Main IDE window.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
        KeyDown += OnGlobalKeyDown;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await _vm.InitializeAsync();
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (StatusText is not null)
            StatusText.Text = _vm.OllamaOnline ? "Connected" : "Offline";
    }

    private async void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.S:
                    await _vm.SaveCurrentFileAsync();
                    e.Handled = true;
                    break;
                case Key.OemTilde:
                    _vm.ToggleTerminal();
                    e.Handled = true;
                    break;
            }
        }

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.P)
        {
            _vm.ToggleCommandPalette();
            e.Handled = true;
        }
    }

    private void OnAiTabClick(object? sender, RoutedEventArgs e)
    {
        _vm.RightPanelTab = "ai";
        SetRightPanel("ai");
    }

    private void OnModelsTabClick(object? sender, RoutedEventArgs e)
    {
        _vm.RightPanelTab = "models";
        SetRightPanel("models");
    }

    private void OnSettingsTabClick(object? sender, RoutedEventArgs e)
    {
        _vm.RightPanelTab = "settings";
        SetRightPanel("settings");
    }

    private void OnReverseTabClick(object? sender, RoutedEventArgs e)
    {
        _vm.RightPanelTab = "reverse";
        SetRightPanel("reverse");
    }

    private void SetRightPanel(string tab)
    {
        AiPanel.IsVisible = tab == "ai";
        MarketplacePanel.IsVisible = tab == "models";
        SettingsPanel.IsVisible = tab == "settings";
        ReversingPanel.IsVisible = tab == "reverse";
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        await _vm.SaveSessionAsync();
        base.OnClosing(e);
    }
}
