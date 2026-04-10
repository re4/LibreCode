using System.Text.RegularExpressions;
using LibreCode.Services;
using LibreCode.Services.Ollama;
using LibreCode.Services.Ollama.Models;
using LibreCode.Features.Context;
using Microsoft.Extensions.Options;

namespace LibreCode.Features.Chat;

/// <summary>
/// Manages chat conversations and context injection for the AI chat panel.
/// </summary>
public sealed partial class ChatService
{
    private readonly OllamaClient _ollama;
    private readonly CodebaseIndexer _indexer;
    private readonly OllamaOptions _options;
    private readonly RulesService _rules;
    private readonly List<ChatMessage> _history = [];

    public ChatService(OllamaClient ollama, CodebaseIndexer indexer, IOptions<OllamaOptions> options, RulesService rules)
    {
        _ollama = ollama;
        _indexer = indexer;
        _options = options.Value;
        _rules = rules;
    }

    /// <summary>The full conversation history.</summary>
    public IReadOnlyList<ChatMessage> History => _history;

    /// <summary>
    /// Streams a chat response while injecting relevant codebase context.
    /// </summary>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string userMessage,
        string? activeFileContent = null,
        string? selectedCode = null,
        string? activeFilePath = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(selectedCode))
            contextParts.Add($"Currently selected code:\n```\n{selectedCode}\n```");

        if (!string.IsNullOrWhiteSpace(activeFilePath) && !string.IsNullOrWhiteSpace(activeFileContent))
            contextParts.Add($"Current file ({activeFilePath}):\n```\n{TruncateForContext(activeFileContent, 3000)}\n```");

        var relevantChunks = await _indexer.SearchAsync(userMessage, topK: 3, ct: ct);
        if (relevantChunks.Count > 0)
        {
            var chunksText = string.Join("\n\n", relevantChunks.Select(c =>
                $"File: {c.FilePath} (lines {c.StartLine}-{c.EndLine}):\n```\n{c.Text}\n```"));
            contextParts.Add($"Relevant code from the codebase:\n{chunksText}");
        }

        if (_history.Count == 0)
        {
            _history.Add(new ChatMessage
            {
                Role = "system",
                Content = BuildFullSystemPrompt()
            });
        }

        var fullMessage = contextParts.Count > 0
            ? $"{string.Join("\n\n", contextParts)}\n\nUser question: {userMessage}"
            : userMessage;

        _history.Add(new ChatMessage { Role = "user", Content = fullMessage });

        var assistantContent = string.Empty;

        await foreach (var chunk in _ollama.StreamChatAsync(_history, ct: ct))
        {
            if (chunk.Message?.Content is not null)
            {
                assistantContent += chunk.Message.Content;
                yield return chunk.Message.Content;
            }
        }

        _history.Add(new ChatMessage { Role = "assistant", Content = assistantContent });
    }

    /// <summary>Clears the conversation history.</summary>
    public void ClearHistory()
    {
        _history.Clear();
    }

    /// <summary>Exports the conversation history for session persistence.</summary>
    public List<ChatMessageDto> ExportHistory()
    {
        return _history
            .Where(m => m.Role != "system")
            .Select(m => new ChatMessageDto { Role = m.Role, Content = m.Content })
            .ToList();
    }

    /// <summary>Restores conversation history from a previous session.</summary>
    public void ImportHistory(List<ChatMessageDto> messages)
    {
        _history.Clear();
        if (messages.Count == 0) return;

        _history.Add(new ChatMessage
        {
            Role = "system",
            Content = BuildFullSystemPrompt()
        });

        foreach (var msg in messages)
            _history.Add(new ChatMessage { Role = msg.Role, Content = msg.Content });
    }

    /// <summary>
    /// Rebuilds the system prompt with current user rules appended.
    /// Called when rules change to update the active system prompt.
    /// </summary>
    public void RefreshSystemPrompt()
    {
        var systemMsg = _history.FirstOrDefault(m => m.Role == "system");
        if (systemMsg is not null)
            systemMsg.Content = BuildFullSystemPrompt();
    }

    private string BuildFullSystemPrompt()
    {
        var prompt = SystemPrompt;
        var rulesBlock = _rules.BuildRulesPrompt();
        if (rulesBlock is not null)
            prompt += rulesBlock;
        return prompt;
    }

    /// <summary>
    /// Parses code blocks from assistant responses, extracting filename annotations.
    /// Format: ```language:filename\ncode\n``` or ```language\ncode\n```
    /// </summary>
    public static List<CodeBlock> ParseCodeBlocks(string content)
    {
        var blocks = new List<CodeBlock>();
        var matches = CodeBlockRegex().Matches(content);

        foreach (Match match in matches)
        {
            var header = match.Groups[1].Value.Trim();
            var code = match.Groups[2].Value;

            string language = string.Empty;
            string? fileName = null;

            if (header.Contains(':'))
            {
                var parts = header.Split(':', 2);
                language = parts[0].Trim();
                fileName = parts[1].Trim();
            }
            else
            {
                language = header;
            }

            if (string.IsNullOrEmpty(fileName))
                fileName = InferFileName(language, code);

            blocks.Add(new CodeBlock
            {
                Language = language,
                FileName = fileName,
                Code = code.TrimEnd(),
                RawMatch = match.Value
            });
        }

        return blocks;
    }

    /// <summary>
    /// Infers a reasonable filename from the language and code content.
    /// </summary>
    private static string? InferFileName(string language, string code)
    {
        var ext = language.ToLowerInvariant() switch
        {
            "python" or "py" => ".py",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "csharp" or "cs" or "c#" => ".cs",
            "java" => ".java",
            "go" => ".go",
            "rust" or "rs" => ".rs",
            "cpp" or "c++" => ".cpp",
            "c" => ".c",
            "ruby" or "rb" => ".rb",
            "php" => ".php",
            "swift" => ".swift",
            "kotlin" or "kt" => ".kt",
            "html" => ".html",
            "css" => ".css",
            "sql" => ".sql",
            "shell" or "bash" or "sh" => ".sh",
            "powershell" or "ps1" => ".ps1",
            "yaml" or "yml" => ".yaml",
            "json" => ".json",
            "xml" => ".xml",
            "markdown" or "md" => ".md",
            _ => null
        };

        if (ext is null) return null;

        var classMatch = ClassNameRegex().Match(code);
        if (classMatch.Success)
            return classMatch.Groups[1].Value + ext;

        var defMatch = FunctionNameRegex().Match(code);
        if (defMatch.Success && ext == ".py")
            return defMatch.Groups[1].Value + ext;

        return "main" + ext;
    }

    private static string TruncateForContext(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + "\n... (truncated)";
    }

    private const string SystemPrompt =
        """
        You are LibreCode, an AI coding assistant embedded in a desktop code editor. You help users write, edit, and create code.

        CRITICAL RULES:
        1. When the user asks you to create, write, or code something, ALWAYS output the full file content in a fenced code block.
        2. Use the format ```language:filename to specify the target file. Examples:
           - ```python:hello.py
           - ```javascript:index.js
           - ```csharp:Program.cs
        3. The user can then click "Apply" to create the file in their project.
        4. If editing an existing file, use the same filename format with the existing path.
        5. Be concise in explanations. Let the code speak for itself.
        6. For multi-file projects, output each file in its own code block with the appropriate path.
        7. Always write complete, runnable code — never use placeholders or "..." unless explaining a concept.
        """;

    [GeneratedRegex(@"```(\w+(?::[^\n]+)?)\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"(?:class|struct|interface|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex(@"def\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex FunctionNameRegex();
}

/// <summary>
/// Represents a parsed code block from an assistant response.
/// </summary>
public sealed class CodeBlock
{
    public string Language { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string RawMatch { get; set; } = string.Empty;
}
