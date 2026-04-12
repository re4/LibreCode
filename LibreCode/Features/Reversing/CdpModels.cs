namespace LibreCode.Features.Reversing;

/// <summary>Connection state for a CDP debug session.</summary>
public enum CdpConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Paused,
    Error
}

/// <summary>Represents a discoverable CDP debug target from the browser's /json endpoint.</summary>
public sealed class CdpTarget
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string WebSocketDebuggerUrl { get; init; } = string.Empty;
    public string Display => $"[{Type}] {Title}";
}

/// <summary>A JavaScript/WASM call frame from the Debugger.paused event.</summary>
public sealed class CdpCallFrame
{
    public int Index { get; init; }
    public string CallFrameId { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string ScriptId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public int ColumnNumber { get; init; }
    public List<CdpScope> ScopeChain { get; init; } = [];
    public string Display => string.IsNullOrEmpty(FunctionName)
        ? $"(anonymous) @ {LineNumber}:{ColumnNumber}"
        : $"{FunctionName} @ {LineNumber}:{ColumnNumber}";
}

/// <summary>A scope in a call frame's scope chain.</summary>
public sealed class CdpScope
{
    public string Type { get; init; } = string.Empty;
    public string ObjectId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<CdpProperty> Properties { get; set; } = [];
}

/// <summary>A property/variable returned from Runtime.getProperties.</summary>
public sealed class CdpProperty
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Subtype { get; init; }
    public string Display => $"{Type}: {Value}";
}

/// <summary>A breakpoint set via CDP Debugger.setBreakpointByUrl.</summary>
public sealed class CdpBreakpoint
{
    public int LocalId { get; init; }
    public string BreakpointId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public int ColumnNumber { get; init; }
    public string? Condition { get; init; }
    public bool Enabled { get; set; } = true;
    public string Label => $"{Url}:{LineNumber}:{ColumnNumber}";
}

/// <summary>A console message from Runtime.consoleAPICalled.</summary>
public sealed class CdpConsoleMessage
{
    public string Type { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Display => $"[{Type}] {Text}";
}

/// <summary>A script loaded in the target, from Debugger.scriptParsed.</summary>
public sealed class CdpScript
{
    public string ScriptId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool IsWasm { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string SourceMapUrl { get; init; } = string.Empty;
    public string Display => string.IsNullOrEmpty(Url) ? $"script:{ScriptId}" : Path.GetFileName(Url);
}

/// <summary>Aggregated state snapshot for the CDP debug UI.</summary>
public sealed class CdpDebugSnapshot
{
    public CdpConnectionState State { get; init; }
    public string PauseReason { get; init; } = string.Empty;
    public List<CdpCallFrame> CallStack { get; init; } = [];
    public List<CdpBreakpoint> Breakpoints { get; init; } = [];
    public List<CdpConsoleMessage> ConsoleLog { get; init; } = [];
    public List<CdpScript> Scripts { get; init; } = [];
    public string? Error { get; init; }
}
