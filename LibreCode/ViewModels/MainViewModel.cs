using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreCode.Services;
using LibreCode.Services.FileSystem;
using LibreCode.Services.Ollama;
using LibreCode.Features.Chat;
using LibreCode.Features.Context;
using LibreCode.Features.AI;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.ViewModels;

/// <summary>
/// Main application state. Manages open tabs, panel visibility,
/// project tree, Ollama status, and session persistence.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FileSystemService _fileSystem;
    private readonly OllamaClient _ollama;
    private readonly ChatService _chat;
    private readonly CodebaseIndexer _indexer;
    private readonly SessionPersistenceService _session;
    private readonly RulesService _rules;
    private CancellationTokenSource? _fileTreeRefreshCts;

    public MainViewModel()
    {
        _fileSystem = App.Services.GetRequiredService<FileSystemService>();
        _ollama = App.Services.GetRequiredService<OllamaClient>();
        _chat = App.Services.GetRequiredService<ChatService>();
        _indexer = App.Services.GetRequiredService<CodebaseIndexer>();
        _session = App.Services.GetRequiredService<SessionPersistenceService>();
        _rules = App.Services.GetRequiredService<RulesService>();

        _indexer.OnProgress += (processed, total) =>
            IndexProgress = $"{processed}/{total}";

        _fileSystem.OnFileChanged += OnFileSystemChanged;
    }

    [ObservableProperty] private string _windowTitle = "LibreCode";
    [ObservableProperty] private string? _projectName;
    [ObservableProperty] private FileNode? _fileTree;
    [ObservableProperty] private bool _ollamaOnline;
    [ObservableProperty] private string? _currentModel;
    [ObservableProperty] private string _indexProgress = string.Empty;
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private int _indexedFiles;
    [ObservableProperty] private int _totalChunks;

    [ObservableProperty] private EditorTab? _activeTab;
    [ObservableProperty] private string _currentContent = string.Empty;
    [ObservableProperty] private string _selectedCode = string.Empty;
    [ObservableProperty] private int _cursorLine = 1;
    [ObservableProperty] private int _cursorCol = 1;

    [ObservableProperty] private string _rightPanelTab = "ai";
    [ObservableProperty] private bool _showTerminal = true;
    [ObservableProperty] private bool _showCommandPalette;

    [ObservableProperty] private AiMode _aiMode = AiMode.Ask;

    public ObservableCollection<EditorTab> OpenTabs { get; } = [];
    public ObservableCollection<AiMessage> AiMessages { get; } = [];

    public FileSystemService FileSystem => _fileSystem;
    public OllamaClient Ollama => _ollama;
    public ChatService Chat => _chat;
    public CodebaseIndexer Indexer => _indexer;

    /// <summary>Checks Ollama connectivity and loads session on startup.</summary>
    public async Task InitializeAsync()
    {
        OllamaOnline = await _ollama.IsAvailableAsync();
        if (OllamaOnline)
        {
            var models = await _ollama.ListModelsAsync();
            CurrentModel = models.FirstOrDefault()?.Name;
        }

        await _rules.LoadAsync();
        await RestoreSessionAsync();
    }

    /// <summary>Opens a project folder and populates the file tree.</summary>
    public void OpenProject(string path)
    {
        _fileSystem.OpenProject(path);
        ProjectName = Path.GetFileName(path);
        FileTree = _fileSystem.GetDirectoryTree();
        WindowTitle = $"LibreCode — {ProjectName}";
        _ = Task.Run(() => _indexer.IndexProjectAsync());
    }

    /// <summary>Opens or switches to a file tab.</summary>
    public async Task OpenFileAsync(string relativePath)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Path == relativePath);
        if (existing is not null)
        {
            ActiveTab = existing;
            CurrentContent = existing.Content;
            return;
        }

        var content = await _fileSystem.ReadFileAsync(relativePath);
        var tab = new EditorTab
        {
            Name = Path.GetFileName(relativePath),
            Path = relativePath,
            Content = content,
            OriginalContent = content,
            Language = GetLanguageFromPath(relativePath)
        };

        OpenTabs.Add(tab);
        ActiveTab = tab;
        CurrentContent = tab.Content;
    }

    /// <summary>Closes an editor tab and selects an adjacent one.</summary>
    [RelayCommand]
    public void CloseTab(EditorTab tab)
    {
        var idx = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (ActiveTab == tab)
        {
            if (OpenTabs.Count > 0)
            {
                var newIdx = Math.Min(idx, OpenTabs.Count - 1);
                ActiveTab = OpenTabs[newIdx];
                CurrentContent = ActiveTab.Content;
            }
            else
            {
                ActiveTab = null;
                CurrentContent = string.Empty;
            }
        }
    }

    /// <summary>Saves the currently active file to disk.</summary>
    [RelayCommand]
    public async Task SaveCurrentFileAsync()
    {
        if (ActiveTab is null) return;
        await _fileSystem.WriteFileAsync(ActiveTab.Path, ActiveTab.Content);
        ActiveTab.OriginalContent = ActiveTab.Content;
        ActiveTab.IsDirty = false;
    }

    /// <summary>Refreshes the file tree from disk.</summary>
    public void RefreshFileTree()
    {
        if (_fileSystem.ProjectRoot is not null)
            FileTree = _fileSystem.GetDirectoryTree();
    }

    [RelayCommand]
    public void ToggleTerminal() => ShowTerminal = !ShowTerminal;

    [RelayCommand]
    public void ToggleCommandPalette() => ShowCommandPalette = !ShowCommandPalette;

    /// <summary>Persists session state (open tabs, panel sizes, chat history).</summary>
    public async Task SaveSessionAsync()
    {
        try
        {
            var state = new SessionState
            {
                ProjectPath = _fileSystem.ProjectRoot,
                OpenTabs = OpenTabs.Select(t => t.Path).ToList(),
                ActiveTab = ActiveTab?.Path,
                RightPanelTab = RightPanelTab,
                ChatHistory = _chat.ExportHistory(),
                SavedAt = DateTime.UtcNow
            };
            await _session.SaveAsync(state);
        }
        catch { }
    }

    private async Task RestoreSessionAsync()
    {
        try
        {
            var state = await _session.LoadAsync();
            if (state.ProjectPath is null || !Directory.Exists(state.ProjectPath)) return;

            RightPanelTab = state.RightPanelTab switch
            {
                "chat" or "agent" => "ai",
                "ai" or "models" or "settings" or "reverse" => state.RightPanelTab,
                _ => "ai"
            };

            if (state.ChatHistory.Count > 0)
                _chat.ImportHistory(state.ChatHistory);

            OpenProject(state.ProjectPath);

            foreach (var tabPath in state.OpenTabs)
            {
                try
                {
                    var content = await _fileSystem.ReadFileAsync(tabPath);
                    OpenTabs.Add(new EditorTab
                    {
                        Name = Path.GetFileName(tabPath),
                        Path = tabPath,
                        Content = content,
                        OriginalContent = content,
                        Language = GetLanguageFromPath(tabPath)
                    });
                }
                catch { }
            }

            var restored = state.ActiveTab is not null
                ? OpenTabs.FirstOrDefault(t => t.Path == state.ActiveTab)
                : null;
            restored ??= OpenTabs.FirstOrDefault();

            if (restored is not null)
            {
                ActiveTab = restored;
                CurrentContent = restored.Content;
            }
        }
        catch { }
    }

    private void OnFileSystemChanged(FileChangeEvent e)
    {
        _fileTreeRefreshCts?.Cancel();
        _fileTreeRefreshCts?.Dispose();
        _fileTreeRefreshCts = new CancellationTokenSource();
        var ct = _fileTreeRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, ct);
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFileTree);
            }
            catch (OperationCanceledException) { }
        });
    }

    public static string GetLanguageFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" => "javascript",
        ".py" => "python",
        ".java" => "java",
        ".go" => "go",
        ".rs" => "rust",
        ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "cpp",
        ".c" => "c",
        ".rb" => "ruby",
        ".php" => "php",
        ".swift" => "swift",
        ".kt" => "kotlin",
        ".css" => "css",
        ".scss" => "scss",
        ".html" => "html",
        ".json" => "json",
        ".xml" or ".csproj" or ".sln" or ".axaml" => "xml",
        ".yaml" or ".yml" => "yaml",
        ".md" => "markdown",
        ".sql" => "sql",
        ".sh" => "shell",
        ".ps1" => "powershell",
        ".bat" or ".cmd" => "bat",
        _ => "plaintext"
    };
}

/// <summary>Represents a single open editor tab.</summary>
public class EditorTab : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public string Language { get; set; } = "plaintext";

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }
}
