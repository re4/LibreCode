using System.Collections.ObjectModel;

namespace LibreCode.Features.Reversing;

/// <summary>PE header and CLR metadata inspection result.</summary>
public sealed class PEInspectionResult
{
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string Machine { get; init; } = string.Empty;
    public string Subsystem { get; init; } = string.Empty;
    public ulong ImageBase { get; init; }
    public int FileAlignment { get; init; }
    public string DllCharacteristics { get; init; } = string.Empty;
    public int EntryPointToken { get; init; }
    public bool IsManaged { get; init; }
    public string? ClrVersion { get; init; }
    public string? TargetFramework { get; init; }
    public string? AssemblyName { get; init; }
    public string? AssemblyVersion { get; init; }
    public List<PESectionInfo> Sections { get; init; } = [];
    public List<AssemblyReferenceInfo> References { get; init; } = [];
}

/// <summary>A single PE section (e.g. .text, .rsrc).</summary>
public sealed class PESectionInfo
{
    public string Name { get; init; } = string.Empty;
    public int VirtualAddress { get; init; }
    public int VirtualSize { get; init; }
    public int RawDataSize { get; init; }
}

/// <summary>A referenced assembly.</summary>
public sealed class AssemblyReferenceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? PublicKeyToken { get; init; }
}

/// <summary>Node in the type tree for the decompiler/IL navigation.</summary>
public sealed class TypeTreeNode
{
    public string DisplayName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public TypeTreeNodeKind Kind { get; init; }
    public ObservableCollection<TypeTreeNode> Children { get; } = [];
}

/// <summary>Kind of type tree node.</summary>
public enum TypeTreeNodeKind
{
    Namespace,
    Type,
    Method
}

/// <summary>A single line in the hex viewer.</summary>
public sealed class HexLine
{
    public long Offset { get; init; }
    public byte[] Bytes { get; init; } = [];
    public string OffsetHex => $"{Offset:X8}";

    public string HexPart
    {
        get
        {
            var parts = new string[Bytes.Length];
            for (var i = 0; i < Bytes.Length; i++)
                parts[i] = Bytes[i].ToString("X2");
            return string.Join(' ', parts);
        }
    }

    public string AsciiPart
    {
        get
        {
            var chars = new char[Bytes.Length];
            for (var i = 0; i < Bytes.Length; i++)
            {
                var b = Bytes[i];
                chars[i] = b is >= 0x20 and <= 0x7E ? (char)b : '.';
            }
            return new string(chars);
        }
    }
}

/// <summary>A string found in the assembly metadata.</summary>
public sealed class StringSearchResult
{
    public int Token { get; init; }
    public int Offset { get; init; }
    public string Value { get; init; } = string.Empty;
    public string TokenHex => $"0x{Token:X8}";
    public string OffsetHex => $"0x{Offset:X4}";
}

/// <summary>A .NET method with its decoded IL instructions for the debug view.</summary>
public sealed class ILMethod
{
    public int Index { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string MethodName { get; init; } = string.Empty;
    public int MetadataToken { get; init; }
    public int LocalCount { get; init; }
    public int MaxStack { get; init; }
    public List<ILInstruction> Instructions { get; init; } = [];
    public List<ILLocalInfo> Locals { get; init; } = [];
    public string DisplayName => $"{TypeName}::{MethodName}";
}

/// <summary>A single decoded CIL instruction.</summary>
public sealed class ILInstruction
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public ushort OpCodeValue { get; init; }
    public string Mnemonic { get; init; } = string.Empty;
    public string Operand { get; init; } = string.Empty;
    public string OffsetHex => $"IL_{Offset:X4}";
    public string Display => string.IsNullOrEmpty(Operand) ? Mnemonic : $"{Mnemonic} {Operand}";
}

/// <summary>Metadata about a single IL local variable.</summary>
public sealed class ILLocalInfo
{
    public int Index { get; init; }
    public string TypeName { get; init; } = string.Empty;
}

/// <summary>State of the .NET IL debug session.</summary>
public sealed class ILDebugState
{
    public int CurrentMethodIndex { get; set; } = -1;
    public int InstructionPointer { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public List<ILStackValue> Stack { get; set; } = [];
    public List<ILLocalVariable> Locals { get; set; } = [];
    public List<ILBreakpoint> Breakpoints { get; set; } = [];
    public List<string> Output { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>A value on the .NET evaluation stack during debug.</summary>
public sealed class ILStackValue
{
    public int Index { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>A local variable in the current .NET method frame.</summary>
public sealed class ILLocalVariable
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>A breakpoint set in a .NET method.</summary>
public sealed class ILBreakpoint
{
    public int Id { get; init; }
    public int MethodIndex { get; init; }
    public int InstructionIndex { get; init; }
    public bool Enabled { get; set; } = true;
    public string Label => $"IL @ {InstructionIndex}";
}

/// <summary>Row in the .NET IL debug instructions grid.</summary>
public sealed class ILDebugInstructionRow
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public string Mnemonic { get; init; } = string.Empty;
    public string Operand { get; init; } = string.Empty;
    public bool IsCurrentIP { get; init; }
    public bool HasBreakpoint { get; init; }
    public string OffsetHex => $"IL_{Offset:X4}";
    public string Display => string.IsNullOrEmpty(Operand) ? Mnemonic : $"{Mnemonic} {Operand}";
    public string Marker => HasBreakpoint ? (IsCurrentIP ? ">>>" : " * ") : (IsCurrentIP ? " > " : "");
}
