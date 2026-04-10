using System.Collections.Concurrent;
using System.Numerics;

namespace LibreCode.Features.Context;

/// <summary>
/// In-memory vector store for code chunk embeddings. Supports cosine similarity search
/// for retrieving the most relevant code context for AI queries.
/// </summary>
public sealed class EmbeddingStore
{
    private readonly ConcurrentDictionary<string, List<EmbeddedChunk>> _chunks = new();

    /// <summary>Total number of indexed chunks across all files.</summary>
    public int TotalChunks => _chunks.Values.Sum(list => list.Count);

    /// <summary>Number of indexed files.</summary>
    public int TotalFiles => _chunks.Count;

    /// <summary>Stores embedding vectors for chunks of a specific file.</summary>
    public void Store(string filePath, List<EmbeddedChunk> chunks)
    {
        _chunks[filePath] = chunks;
    }

    /// <summary>Removes all chunks for a specific file.</summary>
    public void Remove(string filePath)
    {
        _chunks.TryRemove(filePath, out _);
    }

    /// <summary>Clears the entire store.</summary>
    public void Clear()
    {
        _chunks.Clear();
    }

    /// <summary>
    /// Searches for the top-K most similar chunks to the given query embedding.
    /// Uses SIMD-accelerated cosine similarity.
    /// </summary>
    public List<EmbeddedChunk> Search(float[] queryEmbedding, int topK = 5)
    {
        var results = new List<(EmbeddedChunk Chunk, float Score)>();

        foreach (var (_, chunks) in _chunks)
        {
            foreach (var chunk in chunks)
            {
                var score = CosineSimilarity(queryEmbedding, chunk.Embedding);
                results.Add((chunk, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Where(r => r.Score > 0.3f)
            .Select(r => r.Chunk)
            .ToList();
    }

    /// <summary>Computes cosine similarity between two vectors using SIMD when available.</summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        var dotProduct = 0f;
        var normA = 0f;
        var normB = 0f;

        var simdLength = Vector<float>.Count;
        var i = 0;

        if (Vector.IsHardwareAccelerated && a.Length >= simdLength)
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            for (; i <= a.Length - simdLength; i += simdLength)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                vDot += va * vb;
                vNormA += va * va;
                vNormB += vb * vb;
            }

            dotProduct = Vector.Dot(vDot, Vector<float>.One);
            normA = Vector.Dot(vNormA, Vector<float>.One);
            normB = Vector.Dot(vNormB, Vector<float>.One);
        }

        for (; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0f ? 0f : dotProduct / denominator;
    }
}

/// <summary>A chunk of source code with its embedding vector and source metadata.</summary>
public sealed class EmbeddedChunk
{
    /// <summary>The source file path relative to the project root.</summary>
    public required string FilePath { get; init; }

    /// <summary>Starting line number in the source file.</summary>
    public int StartLine { get; init; }

    /// <summary>Ending line number in the source file.</summary>
    public int EndLine { get; init; }

    /// <summary>The raw text of this chunk.</summary>
    public required string Text { get; init; }

    /// <summary>The embedding vector for this chunk.</summary>
    public required float[] Embedding { get; init; }
}
