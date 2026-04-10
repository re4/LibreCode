using System.Text;
using LibreCode.Services.Ollama;
using Microsoft.Extensions.Options;

namespace LibreCode.Features.Autocomplete;

/// <summary>
/// Provides AI-powered tab autocomplete by sending fill-in-the-middle prompts to Ollama.
/// Uses debouncing to avoid overwhelming the model with requests on every keystroke.
/// </summary>
public sealed class AutocompleteService : IDisposable
{
    private readonly OllamaClient _ollama;
    private readonly OllamaOptions _options;
    private CancellationTokenSource? _cts;

    public AutocompleteService(OllamaClient ollama, IOptions<OllamaOptions> options)
    {
        _ollama = ollama;
        _options = options.Value;
    }

    /// <summary>
    /// Requests an autocomplete suggestion for the given cursor context.
    /// Automatically debounces and cancels previous pending requests.
    /// </summary>
    public async Task<string?> GetCompletionAsync(
        string prefix,
        string suffix,
        string language,
        string? fileName = null)
    {
        if (!_options.AutocompleteEnabled) return null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            await Task.Delay(_options.AutocompleteDebounceMs, ct);

            var prompt = BuildFimPrompt(prefix, suffix, language, fileName);
            var result = new StringBuilder();

            await foreach (var chunk in _ollama.StreamGenerateAsync(
                prompt,
                suffix: suffix,
                maxTokens: 128,
                stop: ["\n\n", "```"],
                ct: ct))
            {
                result.Append(chunk.Response);

                if (result.Length > 300) break;
            }

            var completion = result.ToString().TrimEnd();

            if (string.IsNullOrWhiteSpace(completion) || completion.Length < 2)
                return null;

            return completion;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string BuildFimPrompt(string prefix, string suffix, string language, string? fileName)
    {
        var fileHint = fileName is not null ? $"// File: {fileName}\n" : "";
        return $"{fileHint}// Language: {language}\n{prefix}";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
