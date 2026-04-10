using System.Text.Json.Serialization;

namespace LibreCode.Services.Ollama.Models;

/// <summary>
/// Request body for the Ollama POST /api/generate endpoint.
/// </summary>
public sealed class GenerateRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;

    [JsonPropertyName("options")]
    public ChatOptions? Options { get; set; }
}

/// <summary>
/// Response from the Ollama POST /api/generate endpoint (one chunk when streaming).
/// </summary>
public sealed class GenerateResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }
}
