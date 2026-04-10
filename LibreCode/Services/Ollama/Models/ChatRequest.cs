using System.Text.Json.Serialization;

namespace LibreCode.Services.Ollama.Models;

/// <summary>
/// Request body for the Ollama POST /api/chat endpoint.
/// </summary>
public sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<ChatMessage> Messages { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;

    [JsonPropertyName("options")]
    public ChatOptions? Options { get; set; }
}

/// <summary>
/// A single message in the chat conversation.
/// </summary>
public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

/// <summary>
/// Model parameter overrides sent with chat requests.
/// </summary>
public sealed class ChatOptions
{
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; set; }
}
