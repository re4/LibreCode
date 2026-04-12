using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Interactive WASM bytecode debugger with step execution, breakpoints,
/// operand stack inspection, local variable watch, and execution output log.
/// </summary>
public partial class WasmDebugSubView : UserControl
{
    private List<WasmFunction> _functions = [];
    private WasmFunction? _activeFunction;

    public WasmDebugSubView()
    {
        InitializeComponent();
    }

    private WasmAnalysisService Svc => App.Services.GetRequiredService<WasmAnalysisService>();

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public void OnWasmLoaded()
    {
        _functions = Svc.GetFunctions();
        var items = _functions.Select(f => $"[{f.Index}] {f.DisplayName} {f.Signature}").ToList();
        FuncSelector.ItemsSource = items;
        if (items.Count > 0) FuncSelector.SelectedIndex = 0;

        StepBtn.IsEnabled = false;
        RunBtn.IsEnabled = false;
        ResetBtn.IsEnabled = false;
        StatusLabel.Text = $"Loaded {_functions.Count} functions. Select one and click Start.";
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        var idx = FuncSelector.SelectedIndex;
        if (idx < 0 || idx >= _functions.Count) return;

        _activeFunction = _functions[idx];
        var state = Svc.StartDebugSession(idx);

        StepBtn.IsEnabled = true;
        RunBtn.IsEnabled = true;
        ResetBtn.IsEnabled = true;
        StatusLabel.Text = $"Debug session started for {_activeFunction.DisplayName}. IP=0";

        RefreshDebugView(state);
    }

    private void OnStepClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.StepInstruction();
        if (state is null) return;

        RefreshDebugView(state);

        if (!state.IsPaused && !state.IsRunning)
        {
            StepBtn.IsEnabled = false;
            RunBtn.IsEnabled = false;
            StatusLabel.Text = "Execution completed.";
        }
        else
        {
            StatusLabel.Text = $"IP={state.InstructionPointer} | Stack depth={state.Stack.Count}";
        }
    }

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.RunToBreakpoint();
        if (state is null) return;

        RefreshDebugView(state);

        if (!state.IsPaused && !state.IsRunning)
        {
            StepBtn.IsEnabled = false;
            RunBtn.IsEnabled = false;
            StatusLabel.Text = "Execution completed.";
        }
        else
        {
            StatusLabel.Text = $"Paused at IP={state.InstructionPointer} | Stack depth={state.Stack.Count}";
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.ResetDebugSession();
        if (state is null) return;

        StepBtn.IsEnabled = true;
        RunBtn.IsEnabled = true;
        StatusLabel.Text = $"Debug session reset. IP=0";

        RefreshDebugView(state);
    }

    private void OnAddBreakpointClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BpInstrBox.Text?.Trim(), out var instrIdx)) return;

        var funcIdx = FuncSelector.SelectedIndex;
        if (funcIdx < 0) return;

        Svc.AddBreakpoint(funcIdx, instrIdx);
        BpInstrBox.Text = "";

        var state = Svc.GetDebugState();
        if (state is not null) RefreshDebugView(state);
    }

    private void OnClearBreakpointsClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.GetDebugState();
        if (state is null) return;

        foreach (var bp in state.Breakpoints.ToList())
            Svc.RemoveBreakpoint(bp.Id);

        RefreshDebugView(state);
    }

    private void RefreshDebugView(WasmDebugState state)
    {
        if (_activeFunction is not null)
        {
            var breakpointIndices = new HashSet<int>(
                state.Breakpoints
                    .Where(b => b.Enabled && b.FunctionIndex == state.CurrentFunctionIndex)
                    .Select(b => b.InstructionIndex));

            var rows = _activeFunction.Instructions.Select((instr, i) => new DebugInstructionRow
            {
                Index = i,
                Offset = instr.Offset,
                Mnemonic = instr.Mnemonic,
                Operand = instr.Operand,
                Depth = instr.Depth,
                IsCurrentIP = i == state.InstructionPointer,
                HasBreakpoint = breakpointIndices.Contains(i)
            }).ToList();

            InstructionsGrid.ItemsSource = rows;

            if (state.InstructionPointer < rows.Count)
                InstructionsGrid.ScrollIntoView(rows[state.InstructionPointer], null);
        }

        StackGrid.ItemsSource = null;
        StackGrid.ItemsSource = state.Stack;

        LocalsGrid.ItemsSource = null;
        LocalsGrid.ItemsSource = state.Locals;

        BreakpointsList.ItemsSource = null;
        BreakpointsList.ItemsSource = state.Breakpoints;

        OutputText.Text = string.Join('\n', state.Output);
        OutputScroll.ScrollToEnd();
    }
}
