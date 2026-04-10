using LibreCode.Services.Ollama;
using LibreCode.Services.Ollama.Models;

namespace LibreCode.Features.InlineEdit;

/// <summary>
/// Handles inline code editing requests — takes selected code and an instruction,
/// returns the modified code via the Ollama API.
/// </summary>
public sealed class InlineEditService
{
    private readonly OllamaClient _ollama;

    public InlineEditService(OllamaClient ollama)
    {
        _ollama = ollama;
    }

    /// <summary>
    /// Generates modified code based on the user's instruction.
    /// </summary>
    public async Task<string> EditCodeAsync(
        string selectedCode,
        string instruction,
        string? fileContext = null,
        string? language = null,
        CancellationToken ct = default)
    {
        var systemPrompt = "You are a precise code editor. Given code and an instruction, return ONLY the modified code. " +
                          "Do not include explanations, markdown fences, or anything besides the code itself. " +
                          "Preserve indentation and style.";

        var userPrompt = $"Language: {language ?? "unknown"}\n";

        if (!string.IsNullOrWhiteSpace(fileContext))
            userPrompt += $"\nFile context (for reference only):\n{fileContext[..Math.Min(fileContext.Length, 2000)]}\n";

        userPrompt += $"\nCode to edit:\n{selectedCode}\n\nInstruction: {instruction}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = userPrompt }
        };

        return await _ollama.ChatAsync(messages, temperature: 0.1, ct: ct);
    }

    /// <summary>
    /// Streams the modified code token by token for real-time preview.
    /// </summary>
    public IAsyncEnumerable<string> StreamEditAsync(
        string selectedCode,
        string instruction,
        string? language = null,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = "You are a precise code editor. Return ONLY the modified code, no explanations."
            },
            new()
            {
                Role = "user",
                Content = $"Language: {language ?? "unknown"}\nCode:\n{selectedCode}\n\nInstruction: {instruction}"
            }
        };

        return StreamTokensAsync(messages, ct);
    }

    private async IAsyncEnumerable<string> StreamTokensAsync(
        List<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in _ollama.StreamChatAsync(messages, temperature: 0.1, ct: ct))
        {
            if (chunk.Message?.Content is not null)
                yield return chunk.Message.Content;
        }
    }
}
