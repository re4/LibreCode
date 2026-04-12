using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibreCode.Features.Chat;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Sends WASM disassembly to the AI with a WebAssembly-specific reverse engineering system prompt,
/// streaming the analysis response in real time.
/// </summary>
public partial class WasmAIAnalysisSubView : UserControl
{
    private const string WasmREPrompt =
        """
        You are a WebAssembly reverse engineering analyst. The user has loaded a WASM binary and is showing you a function's disassembly.
        Analyze it thoroughly:
        1. Summarize what the function does at a high level.
        2. Identify any security concerns (unsafe memory access patterns, buffer overflows, integer overflows, etc.).
        3. Detect obfuscation patterns (control flow flattening, dead code, opaque predicates).
        4. Explain the control flow, stack manipulation, and key algorithms.
        5. Identify the likely source language (C, C++, Rust, Go, AssemblyScript, etc.) based on code patterns.
        6. Note any suspicious patterns that warrant deeper investigation (crypto routines, string obfuscation, anti-analysis).
        7. If imports/exports are visible, explain the module's external interface.
        Be specific, cite instruction offsets, and provide actionable findings.
        """;

    public WasmAIAnalysisSubView()
    {
        InitializeComponent();
    }

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public void OnWasmLoaded()
    {
        StatusLabel.Text = "WASM loaded. Select a function in the Disassembly tab, then click Analyze.";
    }

    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        var parent = this.FindAncestorOfType<ReversingView>();
        var disasmView = parent?.FindControl<WasmDisassemblySubView>("WasmDisasmPanel");
        var source = disasmView?.GetCurrentSource() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source))
        {
            ResponseText.Text = "No disassembly available. Load a WASM binary and select a function first.";
            return;
        }

        AnalyzeBtn.IsEnabled = false;
        StatusLabel.Text = "Analyzing...";
        ResponseText.Text = "";

        var customInstructions = CustomPrompt.Text?.Trim();
        var fullPrompt = $"{WasmREPrompt}\n\n";
        if (!string.IsNullOrEmpty(customInstructions))
            fullPrompt += $"Additional instructions: {customInstructions}\n\n";
        fullPrompt += $"Here is the WASM disassembly:\n```wasm\n{source}\n```";

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
