using LibreCode.Services.FileSystem;
using LibreCode.Services.Ollama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreCode.Features.Context;

/// <summary>
/// Indexes source files in the project by chunking them and generating embeddings
/// via Ollama's embedding API. Watches for file changes to keep the index current.
/// </summary>
public sealed class CodebaseIndexer : IDisposable
{
    private readonly OllamaClient _ollama;
    private readonly FileSystemService _fileSystem;
    private readonly EmbeddingStore _store;
    private readonly OllamaOptions _options;
    private readonly ILogger<CodebaseIndexer> _logger;
    private bool _isIndexing;
    private CancellationTokenSource? _cts;

    /// <summary>Whether the indexer is currently processing files.</summary>
    public bool IsIndexing => _isIndexing;

    /// <summary>Number of files indexed.</summary>
    public int IndexedFiles => _store.TotalFiles;

    /// <summary>Total chunks in the index.</summary>
    public int TotalChunks => _store.TotalChunks;

    /// <summary>Fires when indexing progress changes.</summary>
    public event Action<int, int>? OnProgress;

    public CodebaseIndexer(
        OllamaClient ollama,
        FileSystemService fileSystem,
        EmbeddingStore store,
        IOptions<OllamaOptions> options,
        ILogger<CodebaseIndexer> logger)
    {
        _ollama = ollama;
        _fileSystem = fileSystem;
        _store = store;
        _options = options.Value;
        _logger = logger;

        _fileSystem.OnFileChanged += HandleFileChange;
    }

    /// <summary>
    /// Indexes all source files in the currently open project.
    /// Chunks files and generates embeddings in batches.
    /// </summary>
    public async Task IndexProjectAsync(CancellationToken ct = default)
    {
        if (_isIndexing || _fileSystem.ProjectRoot is null) return;

        if (!await IsEmbeddingModelAvailable(ct))
        {
            _logger.LogInformation("Embedding model not available, skipping indexing");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isIndexing = true;
        _store.Clear();

        try
        {
            var files = _fileSystem.GetAllSourceFiles();
            var total = files.Count;
            var processed = 0;

            foreach (var batch in files.Chunk(5))
            {
                if (_cts.Token.IsCancellationRequested) break;

                foreach (var file in batch)
                {
                    try
                    {
                        await IndexFileAsync(file, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("Indexing cancelled or timed out for: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to index file: {File}", file);
                    }

                    processed++;
                    OnProgress?.Invoke(processed, total);
                }
            }
        }
        finally
        {
            _isIndexing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Searches the index for chunks most relevant to the given query text.
    /// </summary>
    public async Task<List<EmbeddedChunk>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        if (_store.TotalChunks == 0) return [];

        try
        {
            var embeddings = await _ollama.GetEmbeddingsAsync([query], ct: ct);
            if (embeddings.Count == 0) return [];

            return _store.Search(embeddings[0], topK);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Checks if the configured embedding model is installed in Ollama.
    /// </summary>
    private async Task<bool> IsEmbeddingModelAvailable(CancellationToken ct)
    {
        try
        {
            var models = await _ollama.ListModelsAsync(ct);
            var embeddingModel = _options.EmbeddingModel;
            return models.Any(m =>
                m.Name.StartsWith(embeddingModel, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task IndexFileAsync(string relativePath, CancellationToken ct)
    {
        var content = await _fileSystem.ReadFileAsync(relativePath, ct);
        if (string.IsNullOrWhiteSpace(content)) return;

        var chunks = ChunkFile(content, relativePath);
        if (chunks.Count == 0) return;

        var texts = chunks.Select(c => c.Text).ToList();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var embeddings = await _ollama.GetEmbeddingsAsync(texts, ct: timeoutCts.Token);

        if (embeddings.Count != chunks.Count) return;

        var embeddedChunks = chunks.Zip(embeddings, (chunk, embedding) => new EmbeddedChunk
        {
            FilePath = chunk.FilePath,
            StartLine = chunk.StartLine,
            EndLine = chunk.EndLine,
            Text = chunk.Text,
            Embedding = embedding
        }).ToList();

        _store.Store(relativePath, embeddedChunks);
    }

    /// <summary>
    /// Splits a file into overlapping chunks of ~50 lines each for embedding.
    /// </summary>
    private static List<CodeChunk> ChunkFile(string content, string filePath, int chunkSize = 50, int overlap = 10)
    {
        var lines = content.Split('\n');
        var chunks = new List<CodeChunk>();

        for (var i = 0; i < lines.Length; i += chunkSize - overlap)
        {
            var end = Math.Min(i + chunkSize, lines.Length);
            var chunkLines = lines[i..end];
            var text = string.Join('\n', chunkLines).Trim();

            if (text.Length < 20) continue;

            if (text.Length > 2000)
                text = text[..2000];

            chunks.Add(new CodeChunk
            {
                FilePath = filePath,
                StartLine = i + 1,
                EndLine = end,
                Text = text
            });

            if (end >= lines.Length) break;
        }

        return chunks;
    }

    private void HandleFileChange(FileChangeEvent e)
    {
        if (e.ChangeType == FileChangeType.Deleted)
        {
            _store.Remove(e.RelativePath);
        }
        else
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await IndexFileAsync(e.RelativePath, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to re-index changed file: {File}", e.RelativePath);
                }
            });
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private sealed class CodeChunk
    {
        public required string FilePath { get; init; }
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public required string Text { get; init; }
    }
}
