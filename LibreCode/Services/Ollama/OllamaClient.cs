using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LibreCode.Services.Ollama.Models;
using Microsoft.Extensions.Options;

namespace LibreCode.Services.Ollama;

/// <summary>
/// Typed HTTP client for communicating with the Ollama REST API.
/// Supports streaming chat/generate and batch embedding.
/// </summary>
public sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private string? _resolvedDefaultModel;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(HttpClient http, IOptions<OllamaOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>
    /// Resolves the effective model name: uses the configured default if set,
    /// otherwise auto-detects the first locally installed model.
    /// Validates model names to prevent injection via crafted model identifiers.
    /// </summary>
    public async Task<string> ResolveModelAsync(string? requested = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            ValidateModelName(requested);
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(_resolvedDefaultModel)) return _resolvedDefaultModel;
        if (!string.IsNullOrWhiteSpace(_options.DefaultModel))
        {
            ValidateModelName(_options.DefaultModel);
            return _options.DefaultModel;
        }

        var models = await ListModelsAsync(ct);
        _resolvedDefaultModel = models.FirstOrDefault()?.Name
            ?? throw new InvalidOperationException("No models installed. Install a model from the Models tab first.");
        return _resolvedDefaultModel;
    }

    /// <summary>
    /// Validates that a model name contains only safe characters to prevent injection.
    /// </summary>
    private static void ValidateModelName(string name)
    {
        if (name.Length > 256)
            throw new ArgumentException("Model name is too long.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9._:/@-]+$"))
            throw new ArgumentException($"Model name contains invalid characters: '{name}'");
    }

    /// <summary>Clears the cached resolved model so it re-detects on next call.</summary>
    public void ClearModelCache() => _resolvedDefaultModel = null;

    /// <summary>Checks whether the Ollama server is reachable.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/version", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Lists all locally available models.</summary>
    public async Task<List<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<ModelListResponse>("/api/tags", JsonOpts, ct);
        return response?.Models ?? [];
    }

    /// <summary>
    /// Streams a chat completion from Ollama, yielding each token as it arrives.
    /// </summary>
    public async IAsyncEnumerable<ChatResponse> StreamChatAsync(
        List<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var effectiveModel = await ResolveModelAsync(model, ct);
        var request = new ChatRequest
        {
            Model = effectiveModel,
            Messages = messages,
            Stream = true,
            Options = new ChatOptions
            {
                Temperature = temperature ?? _options.Temperature,
                NumPredict = _options.MaxTokens
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = content };

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<ChatResponse>(line, JsonOpts);
            if (chunk is not null)
                yield return chunk;
        }
    }

    /// <summary>
    /// Sends a non-streaming chat request and returns the full response.
    /// </summary>
    public async Task<string> ChatAsync(
        List<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in StreamChatAsync(messages, model, temperature, ct))
        {
            if (chunk.Message?.Content is not null)
                sb.Append(chunk.Message.Content);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Streams a generate completion (fill-in-the-middle or plain prompt).
    /// </summary>
    public async IAsyncEnumerable<GenerateResponse> StreamGenerateAsync(
        string prompt,
        string? suffix = null,
        string? model = null,
        int? maxTokens = null,
        List<string>? stop = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var effectiveModel = await ResolveModelAsync(model, ct);
        var request = new GenerateRequest
        {
            Model = effectiveModel,
            Prompt = prompt,
            Suffix = suffix,
            Stream = true,
            Options = new ChatOptions
            {
                Temperature = 0.2,
                NumPredict = maxTokens ?? 256,
                Stop = stop
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/generate") { Content = content };

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<GenerateResponse>(line, JsonOpts);
            if (chunk is not null)
                yield return chunk;
        }
    }

    /// <summary>
    /// Streams a model pull operation, yielding progress updates as they arrive.
    /// </summary>
    public async IAsyncEnumerable<Features.Marketplace.PullProgress> PullModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateModelName(modelName);
        var payload = JsonSerializer.Serialize(new { name = modelName, stream = true }, JsonOpts);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pull") { Content = content };

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var progress = JsonSerializer.Deserialize<Features.Marketplace.PullProgress>(line, JsonOpts);
            if (progress is not null)
                yield return progress;
        }
    }

    /// <summary>
    /// Deletes a locally installed model.
    /// </summary>
    public async Task<bool> DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        ValidateModelName(modelName);
        var payload = JsonSerializer.Serialize(new { name = modelName }, JsonOpts);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete") { Content = content };

        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Generates embeddings for one or more text inputs.
    /// </summary>
    public async Task<List<float[]>> GetEmbeddingsAsync(
        List<string> inputs,
        string? model = null,
        CancellationToken ct = default)
    {
        var request = new EmbeddingRequest
        {
            Model = model ?? _options.EmbeddingModel,
            Input = inputs
        };

        var response = await _http.PostAsJsonAsync("/api/embed", request, JsonOpts, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts, ct);
        return result?.Embeddings.Select(e => e.ToArray()).ToList() ?? [];
    }
}
