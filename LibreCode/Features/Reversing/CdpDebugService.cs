using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibreCode.Features.Reversing;

/// <summary>
/// Chrome DevTools Protocol client for live WASM debugging. Connects over WebSocket
/// to a browser instance, enables Debugger and Runtime domains, and provides
/// pause/resume/step/breakpoint/evaluate/memory-inspect APIs.
/// </summary>
public sealed class CdpDebugService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private int _nextId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly Lock _gate = new();

    private CdpConnectionState _state = CdpConnectionState.Disconnected;
    private string _pauseReason = string.Empty;
    private readonly List<CdpCallFrame> _callStack = [];
    private readonly List<CdpBreakpoint> _breakpoints = [];
    private readonly List<CdpConsoleMessage> _console = [];
    private readonly List<CdpScript> _scripts = [];
    private int _nextBpLocalId;

    /// <summary>Raised when the debugger state changes (paused, resumed, script parsed, console, etc.).</summary>
    public event Action? StateChanged;

    /// <summary>Current connection state.</summary>
    public CdpConnectionState ConnectionState
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Discovers debug targets from a browser's /json endpoint.</summary>
    public static async Task<List<CdpTarget>> DiscoverTargetsAsync(string host = "127.0.0.1", int port = 9222)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var json = await http.GetStringAsync($"http://{host}:{port}/json");
            var arr = JsonSerializer.Deserialize<JsonArray>(json);
            if (arr is null) return [];

            var targets = new List<CdpTarget>();
            foreach (var item in arr)
            {
                if (item is null) continue;
                targets.Add(new CdpTarget
                {
                    Id = item["id"]?.GetValue<string>() ?? "",
                    Title = item["title"]?.GetValue<string>() ?? "",
                    Type = item["type"]?.GetValue<string>() ?? "",
                    Url = item["url"]?.GetValue<string>() ?? "",
                    WebSocketDebuggerUrl = item["webSocketDebuggerUrl"]?.GetValue<string>() ?? ""
                });
            }
            return targets;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Connects to a CDP target via WebSocket and enables Debugger + Runtime domains.</summary>
    public async Task ConnectAsync(string webSocketUrl)
    {
        lock (_gate)
        {
            _state = CdpConnectionState.Connecting;
            _callStack.Clear();
            _console.Clear();
            _scripts.Clear();
            _pauseReason = string.Empty;
        }
        StateChanged?.Invoke();

        _cts?.Cancel();
        if (_ws is not null)
        {
            try { _ws.Dispose(); } catch { }
        }

        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        try
        {
            await _ws.ConnectAsync(new Uri(webSocketUrl), _cts.Token);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            await SendCommandAsync("Runtime.enable");
            await SendCommandAsync("Debugger.enable", new JsonObject
            {
                ["maxScriptsCacheSize"] = 100_000_000
            });
            await SendCommandAsync("Debugger.setAsyncCallStackDepth", new JsonObject
            {
                ["maxDepth"] = 32
            });

            lock (_gate) _state = CdpConnectionState.Connected;
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _state = CdpConnectionState.Error;
                _pauseReason = ex.Message;
            }
            StateChanged?.Invoke();
        }
    }

    /// <summary>Disconnects and cleans up the WebSocket.</summary>
    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
            catch { }
        }

        try { _ws?.Dispose(); } catch { }
        _ws = null;

        if (_receiveTask is not null)
        {
            try { await _receiveTask; } catch { }
            _receiveTask = null;
        }

        lock (_gate)
        {
            _state = CdpConnectionState.Disconnected;
            _callStack.Clear();
            _pauseReason = string.Empty;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Resumes execution (Debugger.resume).</summary>
    public async Task ResumeAsync()
    {
        await SendCommandAsync("Debugger.resume");
        lock (_gate)
        {
            _state = CdpConnectionState.Connected;
            _callStack.Clear();
            _pauseReason = string.Empty;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Pauses execution (Debugger.pause).</summary>
    public Task PauseAsync() => SendCommandAsync("Debugger.pause");

    /// <summary>Steps over the current statement (Debugger.stepOver).</summary>
    public Task StepOverAsync() => SendCommandAsync("Debugger.stepOver");

    /// <summary>Steps into the current call (Debugger.stepInto).</summary>
    public Task StepIntoAsync() => SendCommandAsync("Debugger.stepInto");

    /// <summary>Steps out of the current call (Debugger.stepOut).</summary>
    public Task StepOutAsync() => SendCommandAsync("Debugger.stepOut");

    /// <summary>Sets a breakpoint by URL and line number.</summary>
    public async Task<CdpBreakpoint?> SetBreakpointAsync(string url, int lineNumber, int columnNumber = 0, string? condition = null)
    {
        var parms = new JsonObject
        {
            ["url"] = url,
            ["lineNumber"] = lineNumber,
            ["columnNumber"] = columnNumber
        };
        if (!string.IsNullOrEmpty(condition))
            parms["condition"] = condition;

        var result = await SendCommandAsync("Debugger.setBreakpointByUrl", parms);
        var bpId = result?["breakpointId"]?.GetValue<string>();
        if (bpId is null) return null;

        var bp = new CdpBreakpoint
        {
            LocalId = Interlocked.Increment(ref _nextBpLocalId),
            BreakpointId = bpId,
            Url = url,
            LineNumber = lineNumber,
            ColumnNumber = columnNumber,
            Condition = condition
        };

        lock (_gate) _breakpoints.Add(bp);
        StateChanged?.Invoke();
        return bp;
    }

    /// <summary>Removes a breakpoint by its CDP breakpoint id.</summary>
    public async Task RemoveBreakpointAsync(string breakpointId)
    {
        await SendCommandAsync("Debugger.removeBreakpoint", new JsonObject
        {
            ["breakpointId"] = breakpointId
        });

        lock (_gate) _breakpoints.RemoveAll(b => b.BreakpointId == breakpointId);
        StateChanged?.Invoke();
    }

    /// <summary>Evaluates an expression in the current call frame's context.</summary>
    public async Task<CdpProperty> EvaluateAsync(string expression, string? callFrameId = null)
    {
        JsonNode? result;
        if (callFrameId is not null)
        {
            result = await SendCommandAsync("Debugger.evaluateOnCallFrame", new JsonObject
            {
                ["callFrameId"] = callFrameId,
                ["expression"] = expression,
                ["returnByValue"] = true
            });
        }
        else
        {
            result = await SendCommandAsync("Runtime.evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["returnByValue"] = true
            });
        }

        return ParseRemoteObject(result?["result"]);
    }

    /// <summary>Gets properties of a remote object by its object id.</summary>
    public async Task<List<CdpProperty>> GetPropertiesAsync(string objectId)
    {
        var result = await SendCommandAsync("Runtime.getProperties", new JsonObject
        {
            ["objectId"] = objectId,
            ["ownProperties"] = true
        });

        var props = new List<CdpProperty>();
        if (result?["result"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is null) continue;
                var name = item["name"]?.GetValue<string>() ?? "";
                var valNode = item["value"];
                props.Add(new CdpProperty
                {
                    Name = name,
                    Type = valNode?["type"]?.GetValue<string>() ?? "undefined",
                    Value = valNode?["description"]?.GetValue<string>()
                            ?? valNode?["value"]?.ToString()
                            ?? "undefined",
                    Subtype = valNode?["subtype"]?.GetValue<string>()
                });
            }
        }
        return props;
    }

    /// <summary>Gets the scope variables for a specific call frame.</summary>
    public async Task<List<CdpScope>> GetScopeChainAsync(CdpCallFrame frame)
    {
        foreach (var scope in frame.ScopeChain)
        {
            if (!string.IsNullOrEmpty(scope.ObjectId))
                scope.Properties = await GetPropertiesAsync(scope.ObjectId);
        }
        return frame.ScopeChain;
    }

    /// <summary>Reads WASM linear memory via Runtime.evaluate with a typed array view.</summary>
    public async Task<byte[]> ReadWasmMemoryAsync(int offset, int length, string? instanceExpression = null)
    {
        var expr = instanceExpression ?? "new Uint8Array(wasmMemory.buffer)";
        var sliceExpr = $"Array.from({expr}.slice({offset}, {offset + length}))";

        var result = await SendCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = sliceExpr,
            ["returnByValue"] = true
        });

        var arr = result?["result"]?["value"];
        if (arr is JsonArray jsonArr)
            return jsonArr.Select(v => (byte)(v?.GetValue<int>() ?? 0)).ToArray();

        return [];
    }

    /// <summary>Gets the source content for a specific script id.</summary>
    public async Task<string> GetScriptSourceAsync(string scriptId)
    {
        var result = await SendCommandAsync("Debugger.getScriptSource", new JsonObject
        {
            ["scriptId"] = scriptId
        });
        return result?["scriptSource"]?.GetValue<string>() ?? "";
    }

    /// <summary>Returns an aggregated snapshot of the current debug state.</summary>
    public CdpDebugSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new CdpDebugSnapshot
            {
                State = _state,
                PauseReason = _pauseReason,
                CallStack = [.. _callStack],
                Breakpoints = [.. _breakpoints],
                ConsoleLog = [.. _console],
                Scripts = [.. _scripts]
            };
        }
    }

    /// <summary>Returns only WASM scripts from the parsed script list.</summary>
    public List<CdpScript> GetWasmScripts()
    {
        lock (_gate) return _scripts.Where(s => s.IsWasm).ToList();
    }

    #region WebSocket Transport

    private async Task<JsonNode?> SendCommandAsync(string method, JsonObject? parms = null)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            return null;

        var id = Interlocked.Increment(ref _nextId);
        var msg = new JsonObject { ["id"] = id, ["method"] = method };
        if (parms is not null) msg["params"] = parms;

        var tcs = new TaskCompletionSource<JsonNode?>();
        _pending[id] = tcs;

        var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token, _cts?.Token ?? CancellationToken.None);

        try
        {
            return await tcs.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            return null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64];
        var sb = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;

                try
                {
                    var json = JsonNode.Parse(sb.ToString());
                    if (json is null) continue;

                    if (json["id"] is not null)
                    {
                        var id = json["id"]!.GetValue<int>();
                        if (_pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(json["result"]);
                    }
                    else if (json["method"] is not null)
                    {
                        HandleEvent(json["method"]!.GetValue<string>(), json["params"]);
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            lock (_gate)
            {
                if (_state != CdpConnectionState.Disconnected)
                    _state = CdpConnectionState.Disconnected;
            }
            StateChanged?.Invoke();
        }
    }

    private void HandleEvent(string method, JsonNode? parms)
    {
        switch (method)
        {
            case "Debugger.paused":
                HandlePaused(parms);
                break;
            case "Debugger.resumed":
                lock (_gate)
                {
                    _state = CdpConnectionState.Connected;
                    _callStack.Clear();
                    _pauseReason = string.Empty;
                }
                StateChanged?.Invoke();
                break;
            case "Debugger.scriptParsed":
                HandleScriptParsed(parms);
                break;
            case "Runtime.consoleAPICalled":
                HandleConsoleApi(parms);
                break;
            case "Runtime.exceptionThrown":
                HandleException(parms);
                break;
        }
    }

    private void HandlePaused(JsonNode? parms)
    {
        if (parms is null) return;

        var reason = parms["reason"]?.GetValue<string>() ?? "other";
        var frames = new List<CdpCallFrame>();

        if (parms["callFrames"] is JsonArray cfArr)
        {
            for (var i = 0; i < cfArr.Count; i++)
            {
                var cf = cfArr[i];
                if (cf is null) continue;

                var scopes = new List<CdpScope>();
                if (cf["scopeChain"] is JsonArray scArr)
                {
                    foreach (var sc in scArr)
                    {
                        if (sc is null) continue;
                        scopes.Add(new CdpScope
                        {
                            Type = sc["type"]?.GetValue<string>() ?? "",
                            ObjectId = sc["object"]?["objectId"]?.GetValue<string>() ?? "",
                            Name = sc["name"]?.GetValue<string>() ?? sc["type"]?.GetValue<string>() ?? ""
                        });
                    }
                }

                var loc = cf["location"];
                frames.Add(new CdpCallFrame
                {
                    Index = i,
                    CallFrameId = cf["callFrameId"]?.GetValue<string>() ?? "",
                    FunctionName = cf["functionName"]?.GetValue<string>() ?? "",
                    ScriptId = loc?["scriptId"]?.GetValue<string>() ?? "",
                    Url = cf["url"]?.GetValue<string>() ?? "",
                    LineNumber = loc?["lineNumber"]?.GetValue<int>() ?? 0,
                    ColumnNumber = loc?["columnNumber"]?.GetValue<int>() ?? 0,
                    ScopeChain = scopes
                });
            }
        }

        lock (_gate)
        {
            _state = CdpConnectionState.Paused;
            _pauseReason = reason;
            _callStack.Clear();
            _callStack.AddRange(frames);
        }
        StateChanged?.Invoke();
    }

    private void HandleScriptParsed(JsonNode? parms)
    {
        if (parms is null) return;

        var url = parms["url"]?.GetValue<string>() ?? "";
        var scriptId = parms["scriptId"]?.GetValue<string>() ?? "";
        var sourceMapUrl = parms["sourceMapURL"]?.GetValue<string>() ?? "";
        var isWasm = url.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)
                     || parms["scriptLanguage"]?.GetValue<string>() == "WebAssembly";

        var script = new CdpScript
        {
            ScriptId = scriptId,
            Url = url,
            IsWasm = isWasm,
            StartLine = parms["startLine"]?.GetValue<int>() ?? 0,
            EndLine = parms["endLine"]?.GetValue<int>() ?? 0,
            SourceMapUrl = sourceMapUrl
        };

        lock (_gate) _scripts.Add(script);
        StateChanged?.Invoke();
    }

    private void HandleConsoleApi(JsonNode? parms)
    {
        if (parms is null) return;

        var type = parms["type"]?.GetValue<string>() ?? "log";
        var textParts = new List<string>();
        if (parms["args"] is JsonArray args)
        {
            foreach (var arg in args)
            {
                if (arg is null) continue;
                textParts.Add(
                    arg["description"]?.GetValue<string>()
                    ?? arg["value"]?.ToString()
                    ?? arg["type"]?.GetValue<string>()
                    ?? "");
            }
        }

        lock (_gate)
        {
            _console.Add(new CdpConsoleMessage
            {
                Type = type,
                Text = string.Join(' ', textParts)
            });
        }
        StateChanged?.Invoke();
    }

    private void HandleException(JsonNode? parms)
    {
        var detail = parms?["exceptionDetails"];
        var text = detail?["text"]?.GetValue<string>()
                   ?? detail?["exception"]?["description"]?.GetValue<string>()
                   ?? "Unknown exception";

        lock (_gate)
        {
            _console.Add(new CdpConsoleMessage
            {
                Type = "error",
                Text = text
            });
        }
        StateChanged?.Invoke();
    }

    private static CdpProperty ParseRemoteObject(JsonNode? obj)
    {
        if (obj is null)
            return new CdpProperty { Name = "result", Type = "undefined", Value = "undefined" };

        return new CdpProperty
        {
            Name = "result",
            Type = obj["type"]?.GetValue<string>() ?? "undefined",
            Value = obj["description"]?.GetValue<string>()
                    ?? obj["value"]?.ToString()
                    ?? "undefined",
            Subtype = obj["subtype"]?.GetValue<string>()
        };
    }

    #endregion

    public void Dispose()
    {
        _cts?.Cancel();
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        _pending.Clear();
    }
}
