using System.Collections.Concurrent;

namespace LibreCode.Services.FileSystem;

/// <summary>
/// Provides file system operations scoped to the currently opened project directory.
/// Validates all paths to prevent directory traversal and symlink escapes.
/// </summary>
public sealed class FileSystemService : IDisposable
{
    private string? _projectRoot;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _recentChanges = new();
    private static readonly long MaxFileReadBytes = 50 * 1024 * 1024; // 50 MB

    private static readonly HashSet<string> BlockedWriteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".pif",
        ".vbs", ".vbe", ".wsf", ".wsh", ".msi", ".msp",
        ".reg", ".inf", ".hta", ".cpl", ".msc"
    };

    /// <summary>Fires when a file in the project is created, changed, renamed, or deleted.</summary>
    public event Action<FileChangeEvent>? OnFileChanged;

    /// <summary>The currently opened project root directory.</summary>
    public string? ProjectRoot => _projectRoot;

    /// <summary>Opens a project directory and starts watching for changes.</summary>
    public void OpenProject(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        _projectRoot = Path.GetFullPath(path);
        StartWatcher();
    }

    /// <summary>Reads a file's contents, validating the path is within the project.</summary>
    public async Task<string> ReadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(relativePath);

        var info = new FileInfo(fullPath);
        if (info.Exists && info.Length > MaxFileReadBytes)
            throw new InvalidOperationException($"File exceeds maximum read size of {MaxFileReadBytes / (1024 * 1024)} MB.");

        return await File.ReadAllTextAsync(fullPath, ct);
    }

    /// <summary>Writes content to a file, creating directories as needed. Blocks dangerous extensions.</summary>
    public async Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default)
    {
        ValidateWritePath(relativePath);
        var fullPath = ResolveSafePath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, content, ct);
    }

    /// <summary>Deletes a file within the project.</summary>
    public void DeleteFile(string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    /// <summary>Deletes a directory and all its contents within the project.</summary>
    public void DeleteDirectory(string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
    }

    /// <summary>Renames or moves a file or directory within the project.</summary>
    public void Rename(string oldRelativePath, string newRelativePath)
    {
        var oldFull = ResolveSafePath(oldRelativePath);
        var newFull = ResolveSafePath(newRelativePath);

        if (File.Exists(oldFull))
            File.Move(oldFull, newFull);
        else if (Directory.Exists(oldFull))
            Directory.Move(oldFull, newFull);
    }

    /// <summary>Copies a file within the project.</summary>
    public void CopyFile(string sourceRelative, string destRelative)
    {
        var srcFull = ResolveSafePath(sourceRelative);
        var dstFull = ResolveSafePath(destRelative);

        var dir = Path.GetDirectoryName(dstFull);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.Copy(srcFull, dstFull, overwrite: true);
    }

    /// <summary>Creates a new empty file within the project.</summary>
    public async Task CreateFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, string.Empty, ct);
    }

    /// <summary>Creates a new directory within the project.</summary>
    public void CreateDirectory(string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        Directory.CreateDirectory(fullPath);
    }

    /// <summary>Gets the full resolved path for a relative project path.</summary>
    public string GetFullPath(string relativePath) => ResolveSafePath(relativePath);

    /// <summary>Checks if a file exists within the project.</summary>
    public bool FileExists(string relativePath)
    {
        try
        {
            var fullPath = ResolveSafePath(relativePath);
            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the recursive directory tree for the project, excluding common non-source directories.
    /// </summary>
    public FileNode GetDirectoryTree(string? subPath = null)
    {
        EnsureProjectOpen();
        var root = subPath is null
            ? _projectRoot!
            : ResolveSafePath(subPath);

        return BuildTree(root);
    }

    /// <summary>
    /// Searches for files matching a pattern within the project.
    /// </summary>
    public List<string> SearchFiles(string pattern)
    {
        EnsureProjectOpen();

        var sanitized = SanitizeSearchPattern(pattern);

        return Directory.GetFiles(_projectRoot!, sanitized, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_projectRoot!, f))
            .Take(200)
            .ToList();
    }

    /// <summary>
    /// Lists all source-code files in the project suitable for indexing.
    /// </summary>
    public List<string> GetAllSourceFiles()
    {
        EnsureProjectOpen();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs",
            ".cpp", ".c", ".h", ".hpp", ".rb", ".php", ".swift", ".kt",
            ".css", ".scss", ".html", ".razor", ".json", ".xml", ".yaml", ".yml",
            ".md", ".txt", ".sql", ".sh", ".ps1", ".bat"
        };

        var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".idea",
            "dist", "build", "__pycache__", ".next", "target", "packages"
        };

        var files = new List<string>();
        CollectFiles(_projectRoot!, extensions, excludeDirs, files);
        return files;
    }

    /// <summary>
    /// Validates that a write path does not target a dangerous executable file type.
    /// </summary>
    private static void ValidateWritePath(string relativePath)
    {
        var ext = Path.GetExtension(relativePath);
        if (BlockedWriteExtensions.Contains(ext))
            throw new UnauthorizedAccessException(
                $"Writing files with extension '{ext}' is blocked for security.");

        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new UnauthorizedAccessException("Invalid file name.");

        if (fileName.StartsWith('.') && fileName.Length > 1)
        {
            var lower = fileName.ToLowerInvariant();
            if (lower is ".env" or ".env.local" or ".env.production")
                throw new UnauthorizedAccessException(
                    $"Writing to '{fileName}' is blocked — environment files may contain secrets.");
        }
    }

    /// <summary>
    /// Sanitizes a search pattern to prevent path traversal via glob.
    /// </summary>
    private static string SanitizeSearchPattern(string pattern)
    {
        if (pattern.Contains("..") || pattern.Contains('/') || pattern.Contains('\\'))
        {
            var fileName = Path.GetFileName(pattern);
            return string.IsNullOrWhiteSpace(fileName) ? "*.txt" : fileName;
        }
        return pattern;
    }

    private void CollectFiles(
        string dir,
        HashSet<string> extensions,
        HashSet<string> excludeDirs,
        List<string> result)
    {
        var dirName = Path.GetFileName(dir);
        if (excludeDirs.Contains(dirName)) return;

        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (extensions.Contains(ext))
                    result.Add(Path.GetRelativePath(_projectRoot!, file));
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                if (IsSymlink(subDir)) continue;
                CollectFiles(subDir, extensions, excludeDirs, result);
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private FileNode BuildTree(string dirPath)
    {
        var name = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(name)) name = dirPath;

        var node = new FileNode
        {
            Name = name,
            Path = Path.GetRelativePath(_projectRoot!, dirPath),
            IsDirectory = true
        };

        var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".idea", "dist", "build"
        };

        try
        {
            foreach (var dir in Directory.GetDirectories(dirPath).OrderBy(d => d))
            {
                var dirName = Path.GetFileName(dir);
                if (!excludeDirs.Contains(dirName) && !IsSymlink(dir))
                    node.Children.Add(BuildTree(dir));
            }

            foreach (var file in Directory.GetFiles(dirPath).OrderBy(f => f))
            {
                node.Children.Add(new FileNode
                {
                    Name = Path.GetFileName(file),
                    Path = Path.GetRelativePath(_projectRoot!, file),
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException) { }

        return node;
    }

    /// <summary>
    /// Resolves a relative path to a full path within the project, preventing traversal
    /// and symlink escapes by resolving to the real filesystem path.
    /// </summary>
    private string ResolveSafePath(string relativePath)
    {
        EnsureProjectOpen();

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new UnauthorizedAccessException("Path cannot be empty.");

        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException("Absolute paths are not allowed — use relative paths.");

        var normalized = relativePath.Replace('/', '\\').Replace("\0", "");

        if (normalized.Contains(".."))
            throw new UnauthorizedAccessException("Path traversal detected — '..' segments are not allowed.");

        var combined = Path.Combine(_projectRoot!, normalized);
        var full = Path.GetFullPath(combined);

        if (!full.StartsWith(_projectRoot!, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected — access denied.");

        if (File.Exists(full) || Directory.Exists(full))
        {
            var realPath = GetRealPath(full);
            if (!realPath.StartsWith(_projectRoot!, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Symlink escape detected — access denied.");
        }

        return full;
    }

    /// <summary>
    /// Resolves the real filesystem path, following symlinks/junctions.
    /// </summary>
    private static string GetRealPath(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget is not null)
                return Path.GetFullPath(fileInfo.LinkTarget);

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.LinkTarget is not null)
                return Path.GetFullPath(dirInfo.LinkTarget);
        }
        catch { }

        return path;
    }

    /// <summary>
    /// Checks whether a directory path is a symlink or junction.
    /// </summary>
    private static bool IsSymlink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget is not null ||
                   info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureProjectOpen()
    {
        if (_projectRoot is null)
            throw new InvalidOperationException("No project is currently open.");
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(_projectRoot!)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, e) => RaiseChange(e.FullPath, FileChangeType.Changed);
        _watcher.Created += (_, e) => RaiseChange(e.FullPath, FileChangeType.Created);
        _watcher.Deleted += (_, e) => RaiseChange(e.FullPath, FileChangeType.Deleted);
        _watcher.Renamed += (_, e) => RaiseChange(e.FullPath, FileChangeType.Renamed);
    }

    private void RaiseChange(string fullPath, FileChangeType changeType)
    {
        var now = DateTime.UtcNow;
        var key = $"{fullPath}:{changeType}";
        if (_recentChanges.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < 200)
            return;

        _recentChanges[key] = now;
        var relativePath = Path.GetRelativePath(_projectRoot!, fullPath);
        OnFileChanged?.Invoke(new FileChangeEvent(relativePath, changeType));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}

/// <summary>Represents a node in the file explorer tree.</summary>
public sealed class FileNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public List<FileNode> Children { get; set; } = [];
}

/// <summary>Describes a file change event within the project.</summary>
public sealed record FileChangeEvent(string RelativePath, FileChangeType ChangeType);

/// <summary>Type of file system change.</summary>
public enum FileChangeType { Created, Changed, Deleted, Renamed }
