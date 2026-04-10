using LibreCode.Services.FileSystem;

namespace LibreCode.Features.Agent.Tools;

/// <summary>
/// Agent tool for searching and listing files within the project.
/// </summary>
public static class SearchTool
{
    /// <summary>Searches for files matching a glob pattern.</summary>
    public static string Execute(FileSystemService fs, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "Error: No search pattern provided.";

        var files = fs.SearchFiles(pattern);

        if (files.Count == 0)
            return $"No files found matching pattern: {pattern}";

        return $"Found {files.Count} file(s):\n" + string.Join("\n", files);
    }

    /// <summary>Lists files and directories at the given path.</summary>
    public static string ListDirectory(FileSystemService fs, string path)
    {
        var tree = fs.GetDirectoryTree(string.IsNullOrWhiteSpace(path) ? null : path);
        return FormatTree(tree, 0);
    }

    private static string FormatTree(FileNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        var icon = node.IsDirectory ? "📁" : "📄";
        var result = $"{indent}{icon} {node.Name}\n";

        if (node.IsDirectory && depth < 3)
        {
            foreach (var child in node.Children.Take(50))
            {
                result += FormatTree(child, depth + 1);
            }
        }

        return result;
    }
}
