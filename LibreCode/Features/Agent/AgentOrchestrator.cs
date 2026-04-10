using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreCode.Features.Agent.Tools;
using LibreCode.Services.FileSystem;
using LibreCode.Services.Ollama;
using LibreCode.Services.Ollama.Models;

namespace LibreCode.Features.Agent;

/// <summary>
/// Autonomous coding agent that processes user instructions by iteratively calling tools
/// (file read/write, shell commands, search) guided by the LLM. Streams LLM responses
/// token-by-token so the UI stays responsive during generation.
/// </summary>
public sealed partial class AgentOrchestrator
{
    private readonly OllamaClient _ollama;
    private readonly FileSystemService _fileSystem;
    private readonly List<AgentStep> _steps = [];
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    /// <summary>Fires when the agent's step list changes.</summary>
    public event Action? OnStepsChanged;

    /// <summary>Current execution steps.</summary>
    public IReadOnlyList<AgentStep> Steps => _steps;

    /// <summary>Whether the agent is currently executing.</summary>
    public bool IsRunning => _isRunning;

    public AgentOrchestrator(OllamaClient ollama, FileSystemService fileSystem)
    {
        _ollama = ollama;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Adds a visible error step without starting a full execution.
    /// Used by the UI to surface validation errors.
    /// </summary>
    public void AddError(string message)
    {
        _steps.Clear();
        AddStep("Error", message, AgentStepStatus.Failed);
    }

    /// <summary>
    /// Executes a multi-step agent task. The LLM decides which tools to call
    /// to accomplish the user's goal. Limits to 15 iterations to prevent runaway loops.
    /// Streams each LLM response token-by-token for live UI feedback.
    /// </summary>
    public async Task ExecuteAsync(string instruction, CancellationToken ct = default)
    {
        if (_isRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isRunning = true;
        _steps.Clear();

        AddStep("Starting", $"Task: {instruction}", AgentStepStatus.Completed);

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = GetSystemPrompt()
            },
            new()
            {
                Role = "user",
                Content = instruction
            }
        };

        try
        {
            for (var i = 0; i < 15; i++)
            {
                if (_cts.Token.IsCancellationRequested) break;

                var thinkingStep = AddStep("Thinking", "", AgentStepStatus.Running);

                var response = await StreamChatAsync(messages, thinkingStep, _cts.Token);

                thinkingStep.Status = AgentStepStatus.Completed;
                thinkingStep.Detail = TruncateDetail(response);
                NotifyChanged();

                var toolCall = ParseToolCall(response);
                if (toolCall is null)
                {
                    AddStep("Complete", response, AgentStepStatus.Completed);
                    break;
                }

                var toolStep = AddStep($"Tool: {toolCall.Name}", toolCall.ArgumentsSummary, AgentStepStatus.Running);

                string toolResult;
                try
                {
                    toolResult = await ExecuteToolAsync(toolCall, _cts.Token);
                    toolStep.Status = AgentStepStatus.Completed;
                    toolStep.Detail = TruncateDetail(toolResult);
                }
                catch (Exception ex)
                {
                    toolResult = $"Error: {ex.Message}";
                    toolStep.Status = AgentStepStatus.Failed;
                    toolStep.Detail = toolResult;
                }

                NotifyChanged();

                messages.Add(new ChatMessage { Role = "assistant", Content = response });
                messages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = $"Tool result:\n{toolResult}\n\nContinue with the task. If done, respond normally without any tool call XML tags."
                });
            }
        }
        catch (OperationCanceledException)
        {
            AddStep("Cancelled", "Agent execution was cancelled.", AgentStepStatus.Failed);
        }
        catch (HttpRequestException ex)
        {
            AddStep("Connection Error", $"Could not reach Ollama: {ex.Message}\nMake sure Ollama is running.", AgentStepStatus.Failed);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No models installed"))
        {
            AddStep("No Model", "No models installed. Go to the Models tab and install one first.", AgentStepStatus.Failed);
        }
        catch (Exception ex)
        {
            AddStep("Error", ex.Message, AgentStepStatus.Failed);
        }
        finally
        {
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;
            NotifyChanged();
        }
    }

    /// <summary>
    /// Streams the LLM chat response token-by-token, updating the thinking step's
    /// detail text in real time so the user sees generation progress.
    /// </summary>
    private async Task<string> StreamChatAsync(
        List<ChatMessage> messages, AgentStep thinkingStep, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var lastNotify = DateTime.UtcNow;

        await foreach (var chunk in _ollama.StreamChatAsync(messages, ct: ct))
        {
            if (chunk.Message?.Content is not null)
            {
                sb.Append(chunk.Message.Content);

                var now = DateTime.UtcNow;
                if ((now - lastNotify).TotalMilliseconds > 100)
                {
                    thinkingStep.Detail = TruncateDetail(sb.ToString());
                    NotifyChanged();
                    lastNotify = now;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>Stops the currently running agent task.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    private AgentStep AddStep(string title, string detail, AgentStepStatus status)
    {
        var step = new AgentStep
        {
            Title = title,
            Detail = detail,
            Status = status,
            Timestamp = DateTime.UtcNow
        };
        _steps.Add(step);
        NotifyChanged();
        return step;
    }

    private async Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
    {
        return toolCall.Name.ToLowerInvariant() switch
        {
            "read_file" => await FileReadTool.ExecuteAsync(_fileSystem, toolCall.GetArg("path"), ct),
            "write_file" => await FileWriteTool.ExecuteAsync(_fileSystem, toolCall.GetArg("path"), toolCall.GetArg("content"), ct),
            "search_files" => SearchTool.Execute(_fileSystem, toolCall.GetArg("pattern")),
            "run_command" => await ShellTool.ExecuteAsync(toolCall.GetArg("command"), _fileSystem.ProjectRoot, _fileSystem.ProjectRoot, ct),
            "list_files" => SearchTool.ListDirectory(_fileSystem, toolCall.GetArg("path")),
            _ => $"Unknown tool: {toolCall.Name}"
        };
    }

    /// <summary>
    /// Parses a tool call from the LLM response. Supports both the XML format
    /// and handles multiline JSON args containing code with special characters.
    /// Uses JsonNode for flexible parsing since "content" values are often multiline code.
    /// </summary>
    private static ToolCall? ParseToolCall(string response)
    {
        var match = ToolCallPattern().Match(response);
        if (!match.Success) return null;

        try
        {
            var name = match.Groups[1].Value.Trim();
            var argsJson = match.Groups[2].Value.Trim();

            var node = JsonNode.Parse(argsJson);
            if (node is null) return null;

            var args = new Dictionary<string, string>();
            foreach (var prop in node.AsObject())
            {
                args[prop.Key] = prop.Value?.ToString() ?? string.Empty;
            }

            return new ToolCall(name, args);
        }
        catch
        {
            return null;
        }
    }

    private static string GetSystemPrompt() => """
        You are an autonomous coding agent working within the user's project directory.
        You MUST use the following tools to accomplish file and command tasks.

        Available tools:

        1. read_file — Read a file
           <tool>read_file</tool><args>{"path": "relative/path"}</args>

        2. write_file — Create or overwrite a file
           <tool>write_file</tool><args>{"path": "relative/path", "content": "full file content"}</args>

        3. search_files — Glob search for files
           <tool>search_files</tool><args>{"pattern": "*.py"}</args>

        4. run_command — Run a shell command
           <tool>run_command</tool><args>{"command": "python hello.py"}</args>

        5. list_files — List directory contents
           <tool>list_files</tool><args>{"path": "."}</args>

        FORMAT RULES:
        - Use EXACTLY the <tool>name</tool><args>{...}</args> XML format shown above.
        - The JSON in <args> MUST be valid JSON with properly escaped strings.
        - For write_file, put the COMPLETE file content in the "content" field with newlines as \n.
        - ONE tool call per response. No extra text before the <tool> tag if you are calling a tool.
        - When the task is DONE, respond normally WITHOUT any <tool> or <args> tags.

        SECURITY:
        - Use relative paths only. Never use absolute paths.
        - Never run destructive system commands.
        - Never write executable files (.exe, .bat, .cmd, .dll, .ps1).
        """;

    /// <summary>
    /// Greedy match on args to handle JSON containing special characters and nested braces.
    /// </summary>
    [GeneratedRegex(@"<tool>\s*(.*?)\s*</tool>\s*<args>\s*([\s\S]*?)\s*</args>")]
    private static partial Regex ToolCallPattern();

    private static string TruncateDetail(string text) =>
        text.Length > 500 ? text[..500] + "..." : text;

    private void NotifyChanged() => OnStepsChanged?.Invoke();
}

/// <summary>Represents a single step in agent execution.</summary>
public sealed class AgentStep
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public AgentStepStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>Status of an agent step.</summary>
public enum AgentStepStatus { Running, Completed, Failed }

/// <summary>Parsed tool call from the LLM response.</summary>
public sealed class ToolCall
{
    public string Name { get; }
    public Dictionary<string, string> Arguments { get; }

    public ToolCall(string name, Dictionary<string, string> arguments)
    {
        Name = name;
        Arguments = arguments;
    }

    public string GetArg(string key) =>
        Arguments.TryGetValue(key, out var val) ? val : string.Empty;

    public string ArgumentsSummary =>
        string.Join(", ", Arguments.Select(kv => $"{kv.Key}: {(kv.Value.Length > 60 ? kv.Value[..60] + "..." : kv.Value)}"));
}
