using System.Text.Json.Serialization;

namespace LibreCode.Services.Ollama.Models;

/// <summary>
/// Request body for the Ollama POST /api/embed endpoint.
/// </summary>
public sealed class EmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("input")]
    public required List<string> Input { get; set; }
}

/// <summary>
/// Response from the Ollama POST /api/embed endpoint.
/// </summary>
public sealed class EmbeddingResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("embeddings")]
    public List<List<float>> Embeddings { get; set; } = [];
}
