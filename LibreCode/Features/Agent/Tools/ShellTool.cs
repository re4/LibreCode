using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LibreCode.Features.Agent.Tools;

/// <summary>
/// Agent tool for executing shell commands within the project directory.
/// Commands are validated against a blocklist, constrained to the project root,
/// and time-limited to prevent runaway processes.
/// </summary>
public static partial class ShellTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly int MaxOutputLength = 5000;

    private static readonly string[] BlockedCommands =
    [
        "format", "diskpart", "reg ", "regedit", "shutdown", "restart",
        "taskkill", "net user", "net localgroup", "netsh", "icacls",
        "takeown", "bcdedit", "cipher /w", "sfc", "dism",
        "powershell -enc", "powershell -e ", "powershell -encodedcommand",
        "invoke-webrequest", "invoke-restmethod", "wget", "curl",
        "certutil -urlcache", "bitsadmin",
        "rm -rf /", "rm -rf ~", "mkfs", "dd if=", ":(){ :|:& };:",
        "chmod 777 /", "chown -r", "> /dev/sda",
        "del /s /q c:\\", "rd /s /q c:\\", "del /f /s /q",
    ];

    private static readonly string[] BlockedPatterns =
    [
        @">\s*/dev/sd",
        @"rm\s+(-\w+\s+)*?/\s*$",
        @"\|\s*sh\b",
        @"\|\s*bash\b",
        @"\|\s*cmd\b",
        @"\|\s*powershell\b",
        @"powershell.*-e[nc]",
    ];

    /// <summary>
    /// Executes a shell command after security validation.
    /// Rejects dangerous commands and constrains execution to the project directory.
    /// </summary>
    public static async Task<string> ExecuteAsync(
        string command, string? workingDirectory, string? projectRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: No command provided.";

        var validation = ValidateCommand(command);
        if (validation is not null)
            return validation;

        var safeWorkingDir = ResolveWorkingDirectory(workingDirectory, projectRoot);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = safeWorkingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return $"Command timed out after {Timeout.TotalSeconds}s.\nPartial output:\n{stdout}";
        }

        var result = new StringBuilder();
        result.AppendLine($"Exit code: {process.ExitCode}");

        if (stdout.Length > 0)
            result.AppendLine($"Output:\n{stdout}");

        if (stderr.Length > 0)
            result.AppendLine($"Errors:\n{stderr}");

        var text = result.ToString();
        if (text.Length > MaxOutputLength)
            text = text[..MaxOutputLength] + "\n... (output truncated)";

        return text;
    }

    /// <summary>
    /// Validates a command against the blocklist. Returns an error message if blocked, null if safe.
    /// </summary>
    private static string? ValidateCommand(string command)
    {
        var lower = command.ToLowerInvariant().Trim();

        foreach (var blocked in BlockedCommands)
        {
            if (lower.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                return $"Error: Command blocked for safety — contains restricted operation '{blocked}'.";
        }

        foreach (var pattern in BlockedPatterns)
        {
            if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                return "Error: Command blocked for safety — matches a dangerous pattern.";
        }

        return null;
    }

    /// <summary>
    /// Resolves the working directory, ensuring it falls within the project root.
    /// Falls back to the project root if the requested directory is outside bounds.
    /// </summary>
    private static string ResolveWorkingDirectory(string? requested, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(requested))
            return projectRoot;

        var resolved = Path.GetFullPath(requested);
        if (resolved.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            return resolved;

        return projectRoot;
    }
}
