using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibreCode.Features.AI;
using LibreCode.Features.Chat;
using LibreCode.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views;

/// <summary>
/// Unified AI panel with Agent, Plan, Debug, and Ask modes.
/// Sends prompts to the ChatService and displays streamed responses.
/// </summary>
public partial class AiPanelView : UserControl
{
    public AiPanelView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnSendClick(object? sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (Vm is null) return;
        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputBox.Text = "";

        Vm.AiMessages.Add(new AiMessage
        {
            Type = AiMessageType.User,
            Title = "You",
            Content = text
        });

        var chat = App.Services.GetRequiredService<ChatService>();
        var response = new AiMessage
        {
            Type = AiMessageType.Assistant,
            Title = Vm.AiMode.ToString(),
            Content = ""
        };
        Vm.AiMessages.Add(response);

        try
        {
            await foreach (var chunk in chat.StreamResponseAsync(text))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    response.Content += chunk;
                });
            }
        }
        catch (Exception ex)
        {
            response.Content = $"Error: {ex.Message}";
        }

        MessageScroll.ScrollToEnd();
    }

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || ModeSelector.SelectedItem is not ComboBoxItem item) return;

        Vm.AiMode = item.Content?.ToString() switch
        {
            "Agent" => AiMode.Agent,
            "Plan" => AiMode.Plan,
            "Debug" => AiMode.Debug,
            _ => AiMode.Ask
        };
    }
}
