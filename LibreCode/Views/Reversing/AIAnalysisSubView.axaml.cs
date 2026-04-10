using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibreCode.Features.Chat;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Sends decompiled code to the AI with a reverse-engineering-specific system prompt,
/// streaming the analysis response in real time.
/// </summary>
public partial class AIAnalysisSubView : UserControl
{
    private const string REPrompt =
        """
        You are a reverse engineering analyst. The user has decompiled a .NET assembly and is showing you a type's source code.
        Analyze it thoroughly:
        1. Summarize what the code does.
        2. Identify any security vulnerabilities (hardcoded credentials, SQL injection, path traversal, insecure crypto, etc.).
        3. Detect obfuscation patterns (string encryption, control flow flattening, proxy delegates, etc.).
        4. Explain the control flow and key algorithms.
        5. Note any anti-debugging or anti-tampering techniques.
        6. Suggest areas that warrant deeper investigation.
        Be specific, cite method names, and provide actionable findings.
        """;

    private string _lastDecompiledSource = string.Empty;

    public AIAnalysisSubView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public void OnAssemblyLoaded()
    {
        StatusLabel.Text = "Assembly loaded. Select a type in the Decompile tab, then click Analyze.";
    }

    /// <summary>Sets the decompiled source to be sent to the AI.</summary>
    public void SetSource(string source)
    {
        _lastDecompiledSource = source;
    }

    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        var parent = this.FindAncestorOfType<ReversingView>();
        var decompileView = parent?.FindControl<DecompileSubView>("DecompilePanel");
        if (decompileView is not null)
            _lastDecompiledSource = decompileView.GetCurrentSource();

        if (string.IsNullOrWhiteSpace(_lastDecompiledSource))
        {
            ResponseText.Text = "No decompiled source available. Load an assembly and select a type first.";
            return;
        }

        AnalyzeBtn.IsEnabled = false;
        StatusLabel.Text = "Analyzing...";
        ResponseText.Text = "";

        var customInstructions = CustomPrompt.Text?.Trim();
        var fullPrompt = $"{REPrompt}\n\n";
        if (!string.IsNullOrEmpty(customInstructions))
            fullPrompt += $"Additional instructions: {customInstructions}\n\n";
        fullPrompt += $"Here is the decompiled source:\n```csharp\n{_lastDecompiledSource}\n```";

        try
        {
            var chat = App.Services.GetRequiredService<ChatService>();
            await foreach (var chunk in chat.StreamResponseAsync(fullPrompt))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ResponseText.Text += chunk;
                });
            }

            StatusLabel.Text = "Analysis complete.";
        }
        catch (Exception ex)
        {
            ResponseText.Text = $"Error: {ex.Message}";
            StatusLabel.Text = "Analysis failed.";
        }
        finally
        {
            AnalyzeBtn.IsEnabled = true;
            ResponseScroll.ScrollToEnd();
        }
    }
}
