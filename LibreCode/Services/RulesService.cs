using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreCode.Services;

/// <summary>
/// Manages user-defined rules that are injected into every chat context.
/// Rules are persisted to a JSON file in AppData.
/// </summary>
public sealed class RulesService
{
    private static readonly string RulesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibreCode");

    private static readonly string RulesFile = Path.Combine(RulesDir, "rules.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly long MaxRulesFileBytes = 1 * 1024 * 1024; // 1 MB
    private static readonly int MaxRuleCount = 100;
    private static readonly int MaxRuleLength = 2000;

    private List<UserRule> _rules = [];
    private bool _loaded;

    /// <summary>All user-defined rules.</summary>
    public IReadOnlyList<UserRule> Rules => _rules;

    /// <summary>
    /// Loads rules from disk. Safe to call multiple times — only loads once.
    /// Rejects files exceeding 1 MB or containing excessive rules.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;

        try
        {
            if (File.Exists(RulesFile))
            {
                var fileInfo = new FileInfo(RulesFile);
                if (fileInfo.Length > MaxRulesFileBytes)
                {
                    System.Diagnostics.Debug.WriteLine("Rules file exceeds size limit — discarding.");
                    _loaded = true;
                    return;
                }

                var json = await File.ReadAllTextAsync(RulesFile, ct);
                var loaded = JsonSerializer.Deserialize<List<UserRule>>(json, JsonOpts) ?? [];
                _rules = loaded.Take(MaxRuleCount).ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rules load failed: {ex.Message}");
        }

        _loaded = true;
    }

    /// <summary>
    /// Persists the current rules list to disk.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(RulesDir);
            var json = JsonSerializer.Serialize(_rules, JsonOpts);
            await File.WriteAllTextAsync(RulesFile, json, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rules save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a new rule and persists.
    /// </summary>
    /// <summary>
    /// Adds a new rule and persists. Enforces maximum rule count and content length.
    /// </summary>
    public async Task AddRuleAsync(string content, bool enabled = true)
    {
        if (_rules.Count >= MaxRuleCount)
            throw new InvalidOperationException($"Maximum of {MaxRuleCount} rules allowed.");

        var trimmed = content.Trim();
        if (trimmed.Length > MaxRuleLength)
            trimmed = trimmed[..MaxRuleLength];

        _rules.Add(new UserRule
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Content = trimmed,
            Enabled = enabled
        });
        await SaveAsync();
    }

    /// <summary>
    /// Updates an existing rule's content and persists. Enforces content length limit.
    /// </summary>
    public async Task UpdateRuleAsync(string id, string newContent)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule is not null)
        {
            var trimmed = newContent.Trim();
            if (trimmed.Length > MaxRuleLength)
                trimmed = trimmed[..MaxRuleLength];

            rule.Content = trimmed;
            await SaveAsync();
        }
    }

    /// <summary>
    /// Toggles a rule's enabled state and persists.
    /// </summary>
    public async Task ToggleRuleAsync(string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule is not null)
        {
            rule.Enabled = !rule.Enabled;
            await SaveAsync();
        }
    }

    /// <summary>
    /// Removes a rule by ID and persists.
    /// </summary>
    public async Task RemoveRuleAsync(string id)
    {
        _rules.RemoveAll(r => r.Id == id);
        await SaveAsync();
    }

    /// <summary>
    /// Moves a rule up in the list and persists.
    /// </summary>
    public async Task MoveUpAsync(string id)
    {
        var idx = _rules.FindIndex(r => r.Id == id);
        if (idx > 0)
        {
            (_rules[idx], _rules[idx - 1]) = (_rules[idx - 1], _rules[idx]);
            await SaveAsync();
        }
    }

    /// <summary>
    /// Moves a rule down in the list and persists.
    /// </summary>
    public async Task MoveDownAsync(string id)
    {
        var idx = _rules.FindIndex(r => r.Id == id);
        if (idx >= 0 && idx < _rules.Count - 1)
        {
            (_rules[idx], _rules[idx + 1]) = (_rules[idx + 1], _rules[idx]);
            await SaveAsync();
        }
    }

    /// <summary>
    /// Builds the combined rules text for injection into the system prompt.
    /// Returns null if no rules are enabled.
    /// </summary>
    public string? BuildRulesPrompt()
    {
        var enabled = _rules.Where(r => r.Enabled).ToList();
        if (enabled.Count == 0) return null;

        var lines = enabled.Select((r, i) => $"{i + 1}. {r.Content}");
        return $"\n\nUSER RULES (always follow these):\n{string.Join("\n", lines)}";
    }
}

/// <summary>
/// A single user-defined rule applied to every chat context.
/// </summary>
public sealed class UserRule
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
