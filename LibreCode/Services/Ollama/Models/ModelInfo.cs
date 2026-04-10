using System.Text.Json.Serialization;

namespace LibreCode.Services.Ollama.Models;

/// <summary>
/// Response from the Ollama GET /api/tags endpoint.
/// </summary>
public sealed class ModelListResponse
{
    [JsonPropertyName("models")]
    public List<ModelInfo> Models { get; set; } = [];
}

/// <summary>
/// Metadata for a single locally available Ollama model.
/// </summary>
public sealed class ModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("modified_at")]
    public string ModifiedAt { get; set; } = string.Empty;

    /// <summary>Returns human-readable file size.</summary>
    public string SizeFormatted => Size switch
    {
        < 1024L * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F1} GB"
    };
}
