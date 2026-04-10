using LibreCode.Services.FileSystem;

namespace LibreCode.Features.Agent.Tools;

/// <summary>
/// Agent tool for reading file contents within the project.
/// </summary>
public static class FileReadTool
{
    /// <summary>Reads and returns the contents of the specified file.</summary>
    public static async Task<string> ExecuteAsync(FileSystemService fs, string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: No file path provided.";

        if (!fs.FileExists(path))
            return $"Error: File not found: {path}";

        var content = await fs.ReadFileAsync(path, ct);

        if (content.Length > 10_000)
            return content[..10_000] + $"\n\n... (file truncated, total {content.Length} characters)";

        return content;
    }
}
