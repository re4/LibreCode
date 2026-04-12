using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Live WASM debugger using Chrome DevTools Protocol. Connects to a browser via CDP WebSocket,
/// provides pause/resume/step controls, breakpoint management, call stack inspection,
/// scope variable viewing, expression evaluation, and console output.
/// </summary>
public partial class WasmCdpDebugSubView : UserControl
{
    private static readonly IBrush DisconnectedColor = new SolidColorBrush(Color.Parse("#666666"));
    private static readonly IBrush ConnectedColor = new SolidColorBrush(Color.Parse("#4ec9b0"));
    private static readonly IBrush PausedColor = new SolidColorBrush(Color.Parse("#dcdcaa"));
    private static readonly IBrush ErrorColor = new SolidColorBrush(Color.Parse("#f44747"));

    private List<CdpTarget> _targets = [];

    public WasmCdpDebugSubView()
    {
        InitializeComponent();
    }

    private CdpDebugService Svc => App.Services.GetRequiredService<CdpDebugService>();

    /// <summary>Called by parent view when the WASM tab becomes active.</summary>
    public void OnWasmLoaded()
    {
        Svc.StateChanged -= OnStateChanged;
        Svc.StateChanged += OnStateChanged;
    }

    private void OnStateChanged()
    {
        Dispatcher.UIThread.InvokeAsync(RefreshUI);
    }

    private async void OnDiscoverClick(object? sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim() ?? "127.0.0.1";
        if (!int.TryParse(PortBox.Text?.Trim(), out var port)) port = 9222;

        StatusLabel.Text = "Discovering targets...";
        _targets = await CdpDebugService.DiscoverTargetsAsync(host, port);

        if (_targets.Count == 0)
        {
            StatusLabel.Text = "No targets found. Ensure browser is running with --remote-debugging-port=9222";
            TargetSelector.ItemsSource = null;
            return;
        }

        TargetSelector.ItemsSource = _targets.Select(t => t.Display).ToList();
        TargetSelector.SelectedIndex = 0;
        StatusLabel.Text = $"Found {_targets.Count} target(s). Select one and click Connect.";
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        var idx = TargetSelector.SelectedIndex;
        if (idx < 0 || idx >= _targets.Count) return;

        var target = _targets[idx];
        if (string.IsNullOrEmpty(target.WebSocketDebuggerUrl))
        {
            StatusLabel.Text = "Selected target has no WebSocket debugger URL.";
            return;
        }

        ConnectBtn.IsEnabled = false;
        StatusLabel.Text = $"Connecting to {target.Title}...";

        await Svc.ConnectAsync(target.WebSocketDebuggerUrl);
        RefreshUI();
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        await Svc.DisconnectAsync();
        RefreshUI();
    }

    private async void OnPauseClick(object? sender, RoutedEventArgs e) => await Svc.PauseAsync();
    private async void OnResumeClick(object? sender, RoutedEventArgs e) => await Svc.ResumeAsync();
    private async void OnStepOverClick(object? sender, RoutedEventArgs e) => await Svc.StepOverAsync();
    private async void OnStepIntoClick(object? sender, RoutedEventArgs e) => await Svc.StepIntoAsync();
    private async void OnStepOutClick(object? sender, RoutedEventArgs e) => await Svc.StepOutAsync();

    private async void OnEvalClick(object? sender, RoutedEventArgs e)
    {
        var expr = EvalBox.Text?.Trim();
        if (string.IsNullOrEmpty(expr)) return;

        var snap = Svc.GetSnapshot();
        var frameId = snap.CallStack.Count > 0 ? snap.CallStack[0].CallFrameId : null;

        var result = await Svc.EvaluateAsync(expr, frameId);
        var snap2 = Svc.GetSnapshot();

        lock (snap2)
        {
            snap2.ConsoleLog.Add(new CdpConsoleMessage
            {
                Type = "eval",
                Text = $"> {expr} => {result.Display}"
            });
        }

        RefreshUI();
    }

    private async void OnSetBreakpointClick(object? sender, RoutedEventArgs e)
    {
        var scriptIdx = ScriptSelector.SelectedIndex;
        var wasmScripts = Svc.GetWasmScripts();
        if (scriptIdx < 0 || scriptIdx >= wasmScripts.Count) return;

        if (!int.TryParse(BpLineBox.Text?.Trim(), out var line)) line = 0;
        if (!int.TryParse(BpColBox.Text?.Trim(), out var col)) col = 0;

        var script = wasmScripts[scriptIdx];
        await Svc.SetBreakpointAsync(script.Url, line, col);
        RefreshUI();
    }

    private async void OnClearBreakpointsClick(object? sender, RoutedEventArgs e)
    {
        var snap = Svc.GetSnapshot();
        foreach (var bp in snap.Breakpoints)
            await Svc.RemoveBreakpointAsync(bp.BreakpointId);
        RefreshUI();
    }

    private async void OnCallFrameSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (CallStackGrid.SelectedItem is not CdpCallFrame frame) return;

        try
        {
            var scopes = await Svc.GetScopeChainAsync(frame);
            var allProps = scopes.SelectMany(s => s.Properties).ToList();
            ScopeGrid.ItemsSource = allProps;
        }
        catch
        {
            ScopeGrid.ItemsSource = null;
        }
    }

    private void RefreshUI()
    {
        var snap = Svc.GetSnapshot();
        var connected = snap.State is CdpConnectionState.Connected or CdpConnectionState.Paused;
        var paused = snap.State == CdpConnectionState.Paused;

        ConnectBtn.IsEnabled = !connected;
        DisconnectBtn.IsEnabled = connected;
        DiscoverBtn.IsEnabled = !connected;
        PauseBtn.IsEnabled = connected && !paused;
        ResumeBtn.IsEnabled = paused;
        StepOverBtn.IsEnabled = paused;
        StepIntoBtn.IsEnabled = paused;
        StepOutBtn.IsEnabled = paused;

        StateDot.Background = snap.State switch
        {
            CdpConnectionState.Connected => ConnectedColor,
            CdpConnectionState.Paused => PausedColor,
            CdpConnectionState.Error => ErrorColor,
            _ => DisconnectedColor
        };

        StatusLabel.Text = snap.State switch
        {
            CdpConnectionState.Disconnected => "Disconnected.",
            CdpConnectionState.Connecting => "Connecting...",
            CdpConnectionState.Connected => $"Connected. {snap.Scripts.Count(s => s.IsWasm)} WASM script(s) loaded.",
            CdpConnectionState.Paused => $"Paused: {snap.PauseReason}. {snap.CallStack.Count} frame(s).",
            CdpConnectionState.Error => $"Error: {snap.PauseReason}",
            _ => ""
        };

        CallStackGrid.ItemsSource = snap.CallStack;

        if (!paused)
            ScopeGrid.ItemsSource = null;

        BreakpointsList.ItemsSource = null;
        BreakpointsList.ItemsSource = snap.Breakpoints;

        var wasmScripts = snap.Scripts.Where(s => s.IsWasm).ToList();
        ScriptsList.ItemsSource = null;
        ScriptsList.ItemsSource = wasmScripts;

        ScriptSelector.ItemsSource = wasmScripts.Select(s => s.Display).ToList();
        if (wasmScripts.Count > 0 && ScriptSelector.SelectedIndex < 0)
            ScriptSelector.SelectedIndex = 0;

        ConsoleText.Text = string.Join('\n', snap.ConsoleLog.TakeLast(200).Select(c => c.Display));
        ConsoleScroll.ScrollToEnd();
    }
}
