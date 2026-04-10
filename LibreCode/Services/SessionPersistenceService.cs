using System.Text.Json;
using System.Text.Json.Serialization;
using LibreCode.Services.Ollama.Models;

namespace LibreCode.Services;

/// <summary>
/// Persists and restores application session state (last project, open tabs,
/// chat history, panel layout) to a JSON file in the user's AppData folder.
/// </summary>
public sealed class SessionPersistenceService
{
    private static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibreCode");

    private static readonly string SessionFile = Path.Combine(SessionDir, "session.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Saves the current session state to disk.
    /// </summary>
    public async Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(SessionDir);
            var json = JsonSerializer.Serialize(state, JsonOpts);
            await File.WriteAllTextAsync(SessionFile, json, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session save failed: {ex.Message}");
        }
    }

    private static readonly long MaxSessionFileBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Loads the last saved session state, or returns a default empty state.
    /// Rejects files exceeding 10 MB to prevent JSON bomb attacks.
    /// </summary>
    public async Task<SessionState> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(SessionFile))
                return new SessionState();

            var fileInfo = new FileInfo(SessionFile);
            if (fileInfo.Length > MaxSessionFileBytes)
            {
                System.Diagnostics.Debug.WriteLine("Session file exceeds size limit — discarding.");
                return new SessionState();
            }

            var json = await File.ReadAllTextAsync(SessionFile, ct);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOpts)
                   ?? new SessionState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session load failed: {ex.Message}");
            return new SessionState();
        }
    }
}

/// <summary>
/// Serializable snapshot of the application session.
/// </summary>
public sealed class SessionState
{
    /// <summary>Last opened project directory path.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Relative paths of all open tabs in order.</summary>
    public List<string> OpenTabs { get; set; } = [];

    /// <summary>Relative path of the active (focused) tab.</summary>
    public string? ActiveTab { get; set; }

    /// <summary>Which right panel tab was active (chat, agent, models, settings).</summary>
    public string? RightPanelTab { get; set; }

    /// <summary>Chat conversation history (user and assistant messages only, no system).</summary>
    public List<ChatMessageDto> ChatHistory { get; set; } = [];

    /// <summary>Layout dimensions.</summary>
    public int SidebarWidth { get; set; } = 260;
    public int RightPanelWidth { get; set; } = 380;
    public int BottomPanelHeight { get; set; } = 250;

    /// <summary>Timestamp of when this session was saved.</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Lightweight DTO for serializing chat messages without Ollama model coupling.
/// </summary>
public sealed class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
