using LibreCode.Services.FileSystem;

namespace LibreCode.Features.Agent.Tools;

/// <summary>
/// Agent tool for writing files within the project. Creates directories as needed.
/// </summary>
public static class FileWriteTool
{
    /// <summary>Writes content to the specified file path.</summary>
    public static async Task<string> ExecuteAsync(
        FileSystemService fs, string path, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: No file path provided.";

        if (string.IsNullOrEmpty(content))
            return "Error: No content provided.";

        await fs.WriteFileAsync(path, content, ct);
        return $"Successfully wrote {content.Length} characters to {path}";
    }
}
