namespace LibreCode.Services.Ollama;

/// <summary>
/// Configuration options for the Ollama API connection and model defaults.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>Base URL of the Ollama REST API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Default model used for chat and generation requests.</summary>
    public string DefaultModel { get; set; } = "llama3.2";

    /// <summary>Model used for generating text embeddings.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Sampling temperature for generation.</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Maximum tokens to generate per request.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Whether AI-powered tab autocomplete is enabled.</summary>
    public bool AutocompleteEnabled { get; set; } = true;

    /// <summary>Debounce interval in milliseconds before triggering autocomplete.</summary>
    public int AutocompleteDebounceMs { get; set; } = 300;
}
