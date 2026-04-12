using System.Collections.ObjectModel;

namespace LibreCode.Features.Reversing;

/// <summary>Top-level inspection result for a loaded WASM binary.</summary>
public sealed class WasmInspectionResult
{
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public uint Version { get; init; }
    public int FunctionCount { get; init; }
    public int ImportCount { get; init; }
    public int ExportCount { get; init; }
    public int GlobalCount { get; init; }
    public int TableCount { get; init; }
    public int MemoryCount { get; init; }
    public int DataSegmentCount { get; init; }
    public int ElementSegmentCount { get; init; }
    public int CustomSectionCount { get; init; }
    public int TypeCount { get; init; }
    public string? StartFunction { get; init; }
    public List<WasmSectionInfo> Sections { get; init; } = [];
}

/// <summary>Describes a single WASM binary section.</summary>
public sealed class WasmSectionInfo
{
    public byte Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long Offset { get; init; }
    public uint Size { get; init; }
}

/// <summary>A WASM function type signature.</summary>
public sealed class WasmFuncType
{
    public int Index { get; init; }
    public string Parameters { get; init; } = string.Empty;
    public string Results { get; init; } = string.Empty;
    public string Signature => $"({Parameters}) -> ({Results})";
}

/// <summary>A WASM import entry.</summary>
public sealed class WasmImportEntry
{
    public int Index { get; init; }
    public string Module { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public WasmExternalKind Kind { get; init; }
    public string TypeDescription { get; init; } = string.Empty;
    public string KindName => Kind.ToString();
}

/// <summary>A WASM export entry.</summary>
public sealed class WasmExportEntry
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public WasmExternalKind Kind { get; init; }
    public uint FunctionIndex { get; init; }
    public string KindName => Kind.ToString();
}

/// <summary>External kind discriminator for imports/exports.</summary>
public enum WasmExternalKind : byte
{
    Function = 0,
    Table = 1,
    Memory = 2,
    Global = 3
}

/// <summary>Represents a decoded WASM function with disassembled instructions.</summary>
public sealed class WasmFunction
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public long BodyOffset { get; init; }
    public uint BodySize { get; init; }
    public int LocalCount { get; init; }
    public List<WasmInstruction> Instructions { get; init; } = [];
    public string DisplayName => string.IsNullOrEmpty(Name) ? $"func[{Index}]" : Name;
}

/// <summary>A single disassembled WASM instruction.</summary>
public sealed class WasmInstruction
{
    public long Offset { get; init; }
    public byte Opcode { get; init; }
    public string Mnemonic { get; init; } = string.Empty;
    public string Operand { get; init; } = string.Empty;
    public int Depth { get; init; }
    public string OffsetHex => $"0x{Offset:X6}";
    public string Indent => new(' ', Depth * 2);
    public string Display => string.IsNullOrEmpty(Operand) ? Mnemonic : $"{Mnemonic} {Operand}";
}

/// <summary>A string found in the WASM data sections.</summary>
public sealed class WasmStringResult
{
    public long Offset { get; init; }
    public int DataSegmentIndex { get; init; }
    public string Value { get; init; } = string.Empty;
    public string OffsetHex => $"0x{Offset:X6}";
}

/// <summary>WASM global variable info.</summary>
public sealed class WasmGlobalInfo
{
    public int Index { get; init; }
    public string ValueType { get; init; } = string.Empty;
    public bool Mutable { get; init; }
    public string InitExpression { get; init; } = string.Empty;
}

/// <summary>Node in the function tree for WASM navigation.</summary>
public sealed class WasmFunctionTreeNode
{
    public string DisplayName { get; init; } = string.Empty;
    public int FunctionIndex { get; init; } = -1;
    public WasmFunctionTreeNodeKind Kind { get; init; }
    public ObservableCollection<WasmFunctionTreeNode> Children { get; } = [];
}

/// <summary>Kind of WASM function tree node.</summary>
public enum WasmFunctionTreeNodeKind
{
    Category,
    Function
}

/// <summary>Represents the state of the WASM debug session.</summary>
public sealed class WasmDebugState
{
    public int CurrentFunctionIndex { get; set; } = -1;
    public int InstructionPointer { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public List<WasmStackValue> Stack { get; set; } = [];
    public List<WasmLocalVariable> Locals { get; set; } = [];
    public List<WasmBreakpoint> Breakpoints { get; set; } = [];
    public List<WasmMemoryRegion> MemoryPages { get; set; } = [];
    public List<string> Output { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>A value on the WASM operand stack during debug.</summary>
public sealed class WasmStackValue
{
    public int Index { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>A local variable in the current WASM frame.</summary>
public sealed class WasmLocalVariable
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>A breakpoint set in a WASM function.</summary>
public sealed class WasmBreakpoint
{
    public int Id { get; init; }
    public int FunctionIndex { get; init; }
    public int InstructionIndex { get; init; }
    public bool Enabled { get; set; } = true;
    public string Label => $"func[{FunctionIndex}] @ {InstructionIndex}";
}

/// <summary>A region of linear memory for the WASM debug inspector.</summary>
public sealed class WasmMemoryRegion
{
    public long Address { get; init; }
    public byte[] Data { get; init; } = [];
    public string AddressHex => $"0x{Address:X8}";
}

/// <summary>Row in the debug instructions grid, annotated with IP marker and breakpoint state.</summary>
public sealed class DebugInstructionRow
{
    public int Index { get; init; }
    public long Offset { get; init; }
    public string Mnemonic { get; init; } = string.Empty;
    public string Operand { get; init; } = string.Empty;
    public int Depth { get; init; }
    public bool IsCurrentIP { get; init; }
    public bool HasBreakpoint { get; init; }
    public string OffsetHex => $"0x{Offset:X6}";
    public string Indent => new(' ', Depth * 2);
    public string Display => string.IsNullOrEmpty(Operand) ? $"{Indent}{Mnemonic}" : $"{Indent}{Mnemonic} {Operand}";
    public string Marker => HasBreakpoint ? (IsCurrentIP ? ">>>" : " * ") : (IsCurrentIP ? " > " : "");
}
