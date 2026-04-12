using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Interactive .NET IL bytecode debugger with step execution, breakpoints,
/// evaluation stack inspection, local variable watch, and execution output log.
/// </summary>
public partial class ILDebugSubView : UserControl
{
    private List<ILMethod> _methods = [];
    private ILMethod? _activeMethod;

    public ILDebugSubView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public void OnAssemblyLoaded()
    {
        _methods = Svc.GetMethods();
        var items = _methods.Select(m => $"[{m.Index}] {m.DisplayName}").ToList();
        MethodSelector.ItemsSource = items;
        if (items.Count > 0) MethodSelector.SelectedIndex = 0;

        StepBtn.IsEnabled = false;
        RunBtn.IsEnabled = false;
        ResetBtn.IsEnabled = false;
        StatusLabel.Text = $"Loaded {_methods.Count} methods. Select one and click Start.";
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        var idx = MethodSelector.SelectedIndex;
        if (idx < 0 || idx >= _methods.Count) return;

        _activeMethod = _methods[idx];
        var state = Svc.StartILDebugSession(idx);

        StepBtn.IsEnabled = true;
        RunBtn.IsEnabled = true;
        ResetBtn.IsEnabled = true;
        StatusLabel.Text = $"Debug session started for {_activeMethod.DisplayName}. IP=0 | MaxStack={_activeMethod.MaxStack}";

        RefreshDebugView(state);
    }

    private void OnStepClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.StepILInstruction();
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
        var state = Svc.RunILToBreakpoint();
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
        var state = Svc.ResetILDebugSession();
        if (state is null) return;

        StepBtn.IsEnabled = true;
        RunBtn.IsEnabled = true;
        StatusLabel.Text = "Debug session reset. IP=0";

        RefreshDebugView(state);
    }

    private void OnAddBreakpointClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BpInstrBox.Text?.Trim(), out var instrIdx)) return;

        var methodIdx = MethodSelector.SelectedIndex;
        if (methodIdx < 0) return;

        Svc.AddILBreakpoint(methodIdx, instrIdx);
        BpInstrBox.Text = "";

        var state = Svc.GetILDebugState();
        if (state is not null) RefreshDebugView(state);
    }

    private void OnClearBreakpointsClick(object? sender, RoutedEventArgs e)
    {
        var state = Svc.GetILDebugState();
        if (state is null) return;

        foreach (var bp in state.Breakpoints.ToList())
            Svc.RemoveILBreakpoint(bp.Id);

        RefreshDebugView(state);
    }

    private void RefreshDebugView(ILDebugState state)
    {
        if (_activeMethod is not null)
        {
            var breakpointIndices = new HashSet<int>(
                state.Breakpoints
                    .Where(b => b.Enabled && b.MethodIndex == state.CurrentMethodIndex)
                    .Select(b => b.InstructionIndex));

            var rows = _activeMethod.Instructions.Select((instr, i) => new ILDebugInstructionRow
            {
                Index = i,
                Offset = instr.Offset,
                Mnemonic = instr.Mnemonic,
                Operand = instr.Operand,
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
