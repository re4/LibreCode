using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace LibreCode.Features.Reversing;

/// <summary>
/// Parses WebAssembly binaries, disassembles instructions, inspects sections/imports/exports,
/// and provides a step-through debug interpreter for WASM bytecode.
/// </summary>
public sealed class WasmAnalysisService : IDisposable
{
    private byte[]? _binary;
    private string? _loadedPath;
    private readonly Lock _gate = new();

    private uint _version;
    private readonly List<WasmSectionInfo> _sections = [];
    private readonly List<WasmFuncType> _funcTypes = [];
    private readonly List<WasmImportEntry> _imports = [];
    private readonly List<WasmExportEntry> _exports = [];
    private readonly List<WasmFunction> _functions = [];
    private readonly List<WasmGlobalInfo> _globals = [];
    private readonly List<byte[]> _dataSegments = [];
    private readonly List<long> _dataSegmentOffsets = [];
    private int _memoryCount;
    private int _tableCount;
    private int _elementCount;
    private int _startFunction = -1;
    private int _importFunctionCount;

    private WasmDebugState? _debugState;

    /// <summary>Currently loaded file path, or null.</summary>
    public string? LoadedPath => _loadedPath;

    /// <summary>Whether a WASM binary is currently loaded.</summary>
    public bool IsLoaded => _binary is not null;

    /// <summary>Loads and parses a WebAssembly binary file.</summary>
    public Task LoadWasmAsync(string path)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                _binary = File.ReadAllBytes(path);
                _loadedPath = path;
                ClearParsedData();
                ParseModule(_binary);
            }
        });
    }

    /// <summary>Returns high-level inspection data for the loaded WASM binary.</summary>
    public WasmInspectionResult? GetWasmInfo()
    {
        lock (_gate)
        {
            if (_binary is null || _loadedPath is null) return null;

            return new WasmInspectionResult
            {
                FileName = Path.GetFileName(_loadedPath),
                FileSizeBytes = _binary.Length,
                Version = _version,
                FunctionCount = _functions.Count,
                ImportCount = _imports.Count,
                ExportCount = _exports.Count,
                GlobalCount = _globals.Count,
                TableCount = _tableCount,
                MemoryCount = _memoryCount,
                DataSegmentCount = _dataSegments.Count,
                ElementSegmentCount = _elementCount,
                CustomSectionCount = _sections.Count(s => s.Id == 0),
                TypeCount = _funcTypes.Count,
                StartFunction = _startFunction >= 0 ? $"func[{_startFunction}]" : null,
                Sections = [.. _sections]
            };
        }
    }

    /// <summary>Returns all parsed function type signatures.</summary>
    public List<WasmFuncType> GetFuncTypes()
    {
        lock (_gate) return [.. _funcTypes];
    }

    /// <summary>Returns all import entries.</summary>
    public List<WasmImportEntry> GetImports()
    {
        lock (_gate) return [.. _imports];
    }

    /// <summary>Returns all export entries.</summary>
    public List<WasmExportEntry> GetExports()
    {
        lock (_gate) return [.. _exports];
    }

    /// <summary>Returns all globals.</summary>
    public List<WasmGlobalInfo> GetGlobals()
    {
        lock (_gate) return [.. _globals];
    }

    /// <summary>Returns all parsed functions with disassembled instructions.</summary>
    public List<WasmFunction> GetFunctions()
    {
        lock (_gate) return [.. _functions];
    }

    /// <summary>Returns a specific function by index.</summary>
    public WasmFunction? GetFunction(int index)
    {
        lock (_gate) return index >= 0 && index < _functions.Count ? _functions[index] : null;
    }

    /// <summary>Generates a text disassembly of all functions, similar to wasm2wat output.</summary>
    public Task<string> GetFullDisassemblyAsync()
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_binary is null) return "No WASM binary loaded.";
                return GenerateWatText();
            }
        });
    }

    /// <summary>Generates text disassembly for a single function by index.</summary>
    public Task<string> GetFunctionDisassemblyAsync(int funcIndex)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (funcIndex < 0 || funcIndex >= _functions.Count)
                    return $";; Function index {funcIndex} not found.";
                return FormatFunction(_functions[funcIndex]);
            }
        });
    }

    /// <summary>Searches data segments for printable ASCII/UTF-8 strings.</summary>
    public Task<List<WasmStringResult>> SearchStringsAsync(string? filter, int minLength = 4)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                var results = new List<WasmStringResult>();
                for (var segIdx = 0; segIdx < _dataSegments.Count; segIdx++)
                {
                    var seg = _dataSegments[segIdx];
                    var baseOffset = _dataSegmentOffsets[segIdx];
                    ExtractStringsFromSegment(seg, baseOffset, segIdx, filter, minLength, results);
                }
                return results;
            }
        });
    }

    /// <summary>Reads raw bytes from the loaded file for the hex viewer.</summary>
    public Task<List<HexLine>> ReadBytesAsync(long offset, int lineCount)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_binary is null) return [];

                const int bytesPerLine = 16;
                var lines = new List<HexLine>(lineCount);
                var pos = offset;

                for (var i = 0; i < lineCount && pos < _binary.Length; i++)
                {
                    var remaining = (int)Math.Min(bytesPerLine, _binary.Length - pos);
                    var bytes = new byte[remaining];
                    Array.Copy(_binary, pos, bytes, 0, remaining);
                    lines.Add(new HexLine { Offset = pos, Bytes = bytes });
                    pos += bytesPerLine;
                }

                return lines;
            }
        });
    }

    /// <summary>Returns the total file size in bytes.</summary>
    public long GetFileSizeBytes()
    {
        lock (_gate) return _binary?.Length ?? 0;
    }

    /// <summary>Builds a navigable function tree grouped by import/local and module.</summary>
    public Task<ObservableCollection<WasmFunctionTreeNode>> GetFunctionTreeAsync()
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                var root = new ObservableCollection<WasmFunctionTreeNode>();

                if (_imports.Count > 0)
                {
                    var importNode = new WasmFunctionTreeNode
                    {
                        DisplayName = $"Imports ({_imports.Count})",
                        Kind = WasmFunctionTreeNodeKind.Category
                    };

                    var byModule = _imports
                        .Where(i => i.Kind == WasmExternalKind.Function)
                        .GroupBy(i => i.Module);

                    foreach (var group in byModule)
                    {
                        var modNode = new WasmFunctionTreeNode
                        {
                            DisplayName = group.Key,
                            Kind = WasmFunctionTreeNodeKind.Category
                        };

                        foreach (var imp in group)
                        {
                            modNode.Children.Add(new WasmFunctionTreeNode
                            {
                                DisplayName = $"{imp.Field} {imp.TypeDescription}",
                                FunctionIndex = imp.Index,
                                Kind = WasmFunctionTreeNodeKind.Function
                            });
                        }

                        importNode.Children.Add(modNode);
                    }

                    root.Add(importNode);
                }

                if (_functions.Count > 0)
                {
                    var localNode = new WasmFunctionTreeNode
                    {
                        DisplayName = $"Functions ({_functions.Count})",
                        Kind = WasmFunctionTreeNodeKind.Category
                    };

                    foreach (var func in _functions)
                    {
                        localNode.Children.Add(new WasmFunctionTreeNode
                        {
                            DisplayName = $"{func.DisplayName} {func.Signature}",
                            FunctionIndex = func.Index,
                            Kind = WasmFunctionTreeNodeKind.Function
                        });
                    }

                    root.Add(localNode);
                }

                return root;
            }
        });
    }

    #region Debug Engine

    /// <summary>Initializes a debug session for the specified function.</summary>
    public WasmDebugState StartDebugSession(int functionIndex)
    {
        lock (_gate)
        {
            if (functionIndex < 0 || functionIndex >= _functions.Count)
                throw new ArgumentOutOfRangeException(nameof(functionIndex));

            var func = _functions[functionIndex];
            var locals = new List<WasmLocalVariable>();

            for (var i = 0; i < func.LocalCount; i++)
            {
                locals.Add(new WasmLocalVariable
                {
                    Index = i,
                    Name = $"local_{i}",
                    Type = "i32",
                    Value = "0"
                });
            }

            _debugState = new WasmDebugState
            {
                CurrentFunctionIndex = functionIndex,
                InstructionPointer = 0,
                IsRunning = false,
                IsPaused = true,
                Stack = [],
                Locals = locals,
                Breakpoints = _debugState?.Breakpoints ?? [],
                MemoryPages = [new WasmMemoryRegion { Address = 0, Data = new byte[256] }],
                Output = []
            };

            return _debugState;
        }
    }

    /// <summary>Executes the next instruction and returns updated debug state.</summary>
    public WasmDebugState? StepInstruction()
    {
        lock (_gate)
        {
            if (_debugState is null || !_debugState.IsPaused) return _debugState;

            var func = _functions.ElementAtOrDefault(_debugState.CurrentFunctionIndex);
            if (func is null) return _debugState;

            if (_debugState.InstructionPointer >= func.Instructions.Count)
            {
                _debugState.IsPaused = false;
                _debugState.IsRunning = false;
                _debugState.Output.Add("[execution completed]");
                return _debugState;
            }

            var instr = func.Instructions[_debugState.InstructionPointer];
            ExecuteInstruction(instr, _debugState);
            _debugState.InstructionPointer++;

            if (_debugState.InstructionPointer >= func.Instructions.Count)
            {
                _debugState.IsRunning = false;
                _debugState.IsPaused = false;
                _debugState.Output.Add("[execution completed]");
            }

            return _debugState;
        }
    }

    /// <summary>Runs until the next breakpoint or end of function.</summary>
    public WasmDebugState? RunToBreakpoint()
    {
        lock (_gate)
        {
            if (_debugState is null) return null;

            var func = _functions.ElementAtOrDefault(_debugState.CurrentFunctionIndex);
            if (func is null) return _debugState;

            _debugState.IsRunning = true;
            _debugState.IsPaused = false;

            const int maxSteps = 100_000;
            for (var i = 0; i < maxSteps; i++)
            {
                if (_debugState.InstructionPointer >= func.Instructions.Count)
                {
                    _debugState.IsRunning = false;
                    _debugState.Output.Add("[execution completed]");
                    break;
                }

                var bp = _debugState.Breakpoints
                    .FirstOrDefault(b => b.Enabled
                        && b.FunctionIndex == _debugState.CurrentFunctionIndex
                        && b.InstructionIndex == _debugState.InstructionPointer);

                if (bp is not null && i > 0)
                {
                    _debugState.IsPaused = true;
                    _debugState.IsRunning = false;
                    _debugState.Output.Add($"[breakpoint hit at {bp.Label}]");
                    break;
                }

                var instr = func.Instructions[_debugState.InstructionPointer];
                ExecuteInstruction(instr, _debugState);
                _debugState.InstructionPointer++;
            }

            if (_debugState.IsRunning)
            {
                _debugState.IsPaused = true;
                _debugState.IsRunning = false;
                _debugState.Output.Add("[paused: step limit reached]");
            }

            return _debugState;
        }
    }

    /// <summary>Adds a breakpoint and returns its id.</summary>
    public int AddBreakpoint(int functionIndex, int instructionIndex)
    {
        lock (_gate)
        {
            _debugState ??= new WasmDebugState();

            var id = _debugState.Breakpoints.Count > 0
                ? _debugState.Breakpoints.Max(b => b.Id) + 1
                : 1;

            _debugState.Breakpoints.Add(new WasmBreakpoint
            {
                Id = id,
                FunctionIndex = functionIndex,
                InstructionIndex = instructionIndex
            });

            return id;
        }
    }

    /// <summary>Removes a breakpoint by id.</summary>
    public void RemoveBreakpoint(int id)
    {
        lock (_gate)
        {
            _debugState?.Breakpoints.RemoveAll(b => b.Id == id);
        }
    }

    /// <summary>Toggles a breakpoint's enabled state.</summary>
    public void ToggleBreakpoint(int id)
    {
        lock (_gate)
        {
            var bp = _debugState?.Breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null) bp.Enabled = !bp.Enabled;
        }
    }

    /// <summary>Returns the current debug state snapshot.</summary>
    public WasmDebugState? GetDebugState()
    {
        lock (_gate) return _debugState;
    }

    /// <summary>Resets the debug session to the start of the current function.</summary>
    public WasmDebugState? ResetDebugSession()
    {
        lock (_gate)
        {
            if (_debugState is null) return null;
            return StartDebugSession(_debugState.CurrentFunctionIndex);
        }
    }

    #endregion

    #region Binary Parser

    private void ClearParsedData()
    {
        _sections.Clear();
        _funcTypes.Clear();
        _imports.Clear();
        _exports.Clear();
        _functions.Clear();
        _globals.Clear();
        _dataSegments.Clear();
        _dataSegmentOffsets.Clear();
        _memoryCount = 0;
        _tableCount = 0;
        _elementCount = 0;
        _startFunction = -1;
        _importFunctionCount = 0;
        _debugState = null;
    }

    private void ParseModule(byte[] data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("File too small to be a WASM binary.");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        if (magic != 0x6D736100)
            throw new InvalidDataException("Invalid WASM magic number.");

        _version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));

        var offset = 8;
        var funcTypeIndices = new List<uint>();

        while (offset < data.Length)
        {
            var sectionId = data[offset++];
            var sectionSize = ReadLeb128U(data, ref offset);
            var sectionStart = offset;

            var sectionName = sectionId switch
            {
                0 => "Custom",
                1 => "Type",
                2 => "Import",
                3 => "Function",
                4 => "Table",
                5 => "Memory",
                6 => "Global",
                7 => "Export",
                8 => "Start",
                9 => "Element",
                10 => "Code",
                11 => "Data",
                12 => "DataCount",
                _ => $"Unknown({sectionId})"
            };

            if (sectionId == 0)
            {
                var nameLen = ReadLeb128U(data, ref offset);
                var customName = Encoding.UTF8.GetString(data, offset, (int)nameLen);
                sectionName = $"Custom ({customName})";
                offset = sectionStart;
            }

            _sections.Add(new WasmSectionInfo
            {
                Id = sectionId,
                Name = sectionName,
                Offset = sectionStart,
                Size = sectionSize
            });

            try
            {
                var sectionOffset = sectionStart;
                switch (sectionId)
                {
                    case 1: ParseTypeSection(data, ref sectionOffset, sectionStart + (int)sectionSize); break;
                    case 2: ParseImportSection(data, ref sectionOffset, sectionStart + (int)sectionSize); break;
                    case 3: funcTypeIndices = ParseFunctionSection(data, ref sectionOffset); break;
                    case 4: ParseTableSection(data, ref sectionOffset); break;
                    case 5: ParseMemorySection(data, ref sectionOffset); break;
                    case 6: ParseGlobalSection(data, ref sectionOffset, sectionStart + (int)sectionSize); break;
                    case 7: ParseExportSection(data, ref sectionOffset); break;
                    case 8: ParseStartSection(data, ref sectionOffset); break;
                    case 9: ParseElementSection(data, ref sectionOffset); break;
                    case 10: ParseCodeSection(data, ref sectionOffset, funcTypeIndices, sectionStart + (int)sectionSize); break;
                    case 11: ParseDataSection(data, ref sectionOffset, sectionStart + (int)sectionSize); break;
                }
            }
            catch
            {
                // Continue parsing remaining sections even if one fails
            }

            offset = sectionStart + (int)sectionSize;
        }

        ResolveExportNames();
    }

    private void ParseTypeSection(byte[] data, ref int offset, int end)
    {
        var count = ReadLeb128U(data, ref offset);
        for (uint i = 0; i < count && offset < end; i++)
        {
            if (data[offset++] != 0x60) continue;

            var paramCount = ReadLeb128U(data, ref offset);
            var pars = new List<string>();
            for (uint p = 0; p < paramCount; p++)
                pars.Add(ValTypeName(data[offset++]));

            var resultCount = ReadLeb128U(data, ref offset);
            var rets = new List<string>();
            for (uint r = 0; r < resultCount; r++)
                rets.Add(ValTypeName(data[offset++]));

            _funcTypes.Add(new WasmFuncType
            {
                Index = (int)i,
                Parameters = string.Join(", ", pars),
                Results = string.Join(", ", rets)
            });
        }
    }

    private void ParseImportSection(byte[] data, ref int offset, int end)
    {
        var count = ReadLeb128U(data, ref offset);
        for (uint i = 0; i < count && offset < end; i++)
        {
            var moduleLen = ReadLeb128U(data, ref offset);
            var module = Encoding.UTF8.GetString(data, offset, (int)moduleLen);
            offset += (int)moduleLen;

            var fieldLen = ReadLeb128U(data, ref offset);
            var field = Encoding.UTF8.GetString(data, offset, (int)fieldLen);
            offset += (int)fieldLen;

            var kind = (WasmExternalKind)data[offset++];
            var typeDesc = string.Empty;

            switch (kind)
            {
                case WasmExternalKind.Function:
                    var typeIdx = ReadLeb128U(data, ref offset);
                    typeDesc = typeIdx < (uint)_funcTypes.Count
                        ? _funcTypes[(int)typeIdx].Signature
                        : $"type[{typeIdx}]";
                    _importFunctionCount++;
                    break;
                case WasmExternalKind.Table:
                    offset++; // reftype
                    ReadLimits(data, ref offset);
                    typeDesc = "table";
                    break;
                case WasmExternalKind.Memory:
                    ReadLimits(data, ref offset);
                    typeDesc = "memory";
                    break;
                case WasmExternalKind.Global:
                    var valType = ValTypeName(data[offset++]);
                    var mutable = data[offset++] != 0;
                    typeDesc = mutable ? $"(mut {valType})" : valType;
                    break;
            }

            _imports.Add(new WasmImportEntry
            {
                Index = (int)i,
                Module = module,
                Field = field,
                Kind = kind,
                TypeDescription = typeDesc
            });
        }
    }

    private static List<uint> ParseFunctionSection(byte[] data, ref int offset)
    {
        var count = ReadLeb128U(data, ref offset);
        var indices = new List<uint>((int)count);
        for (uint i = 0; i < count; i++)
            indices.Add(ReadLeb128U(data, ref offset));
        return indices;
    }

    private void ParseTableSection(byte[] data, ref int offset)
    {
        _tableCount = (int)ReadLeb128U(data, ref offset);
        for (var i = 0; i < _tableCount; i++)
        {
            offset++; // reftype
            ReadLimits(data, ref offset);
        }
    }

    private void ParseMemorySection(byte[] data, ref int offset)
    {
        _memoryCount = (int)ReadLeb128U(data, ref offset);
        for (var i = 0; i < _memoryCount; i++)
            ReadLimits(data, ref offset);
    }

    private void ParseGlobalSection(byte[] data, ref int offset, int end)
    {
        var count = ReadLeb128U(data, ref offset);
        for (uint i = 0; i < count && offset < end; i++)
        {
            var valType = ValTypeName(data[offset++]);
            var mutable = data[offset++] != 0;
            var initExpr = ReadInitExpression(data, ref offset);

            _globals.Add(new WasmGlobalInfo
            {
                Index = (int)i,
                ValueType = valType,
                Mutable = mutable,
                InitExpression = initExpr
            });
        }
    }

    private void ParseExportSection(byte[] data, ref int offset)
    {
        var count = ReadLeb128U(data, ref offset);
        for (uint i = 0; i < count; i++)
        {
            var nameLen = ReadLeb128U(data, ref offset);
            var name = Encoding.UTF8.GetString(data, offset, (int)nameLen);
            offset += (int)nameLen;

            var kind = (WasmExternalKind)data[offset++];
            var idx = ReadLeb128U(data, ref offset);

            _exports.Add(new WasmExportEntry
            {
                Index = (int)i,
                Name = name,
                Kind = kind,
                FunctionIndex = idx
            });
        }
    }

    private void ParseStartSection(byte[] data, ref int offset)
    {
        _startFunction = (int)ReadLeb128U(data, ref offset);
    }

    private void ParseElementSection(byte[] data, ref int offset)
    {
        _elementCount = (int)ReadLeb128U(data, ref offset);
        for (var i = 0; i < _elementCount; i++)
        {
            var flags = ReadLeb128U(data, ref offset);
            if ((flags & 0x01) == 0)
                SkipInitExpression(data, ref offset);
            if ((flags & 0x02) != 0)
            {
                if ((flags & 0x01) != 0)
                    offset++; // elemkind or reftype
                var numElems = ReadLeb128U(data, ref offset);
                for (uint e = 0; e < numElems; e++)
                {
                    if ((flags & 0x04) != 0)
                        SkipInitExpression(data, ref offset);
                    else
                        ReadLeb128U(data, ref offset);
                }
            }
            else
            {
                var numElems = ReadLeb128U(data, ref offset);
                for (uint e = 0; e < numElems; e++)
                {
                    if ((flags & 0x04) != 0)
                        SkipInitExpression(data, ref offset);
                    else
                        ReadLeb128U(data, ref offset);
                }
            }
        }
    }

    private void ParseCodeSection(byte[] data, ref int offset, List<uint> typeIndices, int end)
    {
        var count = ReadLeb128U(data, ref offset);

        for (uint i = 0; i < count && offset < end; i++)
        {
            var bodySize = ReadLeb128U(data, ref offset);
            var bodyStart = offset;
            var funcIdx = (int)i;
            var absoluteFuncIdx = _importFunctionCount + funcIdx;

            var typeIdx = funcIdx < typeIndices.Count ? (int)typeIndices[funcIdx] : -1;
            var sig = typeIdx >= 0 && typeIdx < _funcTypes.Count
                ? _funcTypes[typeIdx].Signature
                : "(?)";

            var exportName = _exports
                .FirstOrDefault(e => e.Kind == WasmExternalKind.Function && e.FunctionIndex == (uint)absoluteFuncIdx)
                ?.Name ?? string.Empty;

            var localCount = 0;
            var localDeclCount = ReadLeb128U(data, ref offset);
            for (uint l = 0; l < localDeclCount; l++)
            {
                var n = ReadLeb128U(data, ref offset);
                localCount += (int)n;
                offset++; // valtype
            }

            var instructions = DisassembleBody(data, offset, bodyStart + (int)bodySize);

            _functions.Add(new WasmFunction
            {
                Index = funcIdx,
                Name = exportName,
                Signature = sig,
                BodyOffset = bodyStart,
                BodySize = bodySize,
                LocalCount = localCount,
                Instructions = instructions
            });

            offset = bodyStart + (int)bodySize;
        }
    }

    private void ParseDataSection(byte[] data, ref int offset, int end)
    {
        var count = ReadLeb128U(data, ref offset);
        for (uint i = 0; i < count && offset < end; i++)
        {
            var flags = ReadLeb128U(data, ref offset);
            long segOffset = 0;

            if ((flags & 0x02) == 0)
            {
                if ((flags & 0x01) == 0)
                    segOffset = EvalI32InitExpression(data, ref offset);
            }
            else if ((flags & 0x01) == 0)
            {
                ReadLeb128U(data, ref offset); // memidx
                segOffset = EvalI32InitExpression(data, ref offset);
            }

            var size = ReadLeb128U(data, ref offset);
            if (offset + (int)size <= data.Length)
            {
                var seg = new byte[size];
                Array.Copy(data, offset, seg, 0, (int)size);
                _dataSegments.Add(seg);
                _dataSegmentOffsets.Add(segOffset);
            }
            offset += (int)size;
        }
    }

    private void ResolveExportNames()
    {
        foreach (var exp in _exports.Where(e => e.Kind == WasmExternalKind.Function))
        {
            var localIdx = (int)exp.FunctionIndex - _importFunctionCount;
            if (localIdx >= 0 && localIdx < _functions.Count && string.IsNullOrEmpty(_functions[localIdx].Name))
            {
                var f = _functions[localIdx];
                _functions[localIdx] = new WasmFunction
                {
                    Index = f.Index,
                    Name = exp.Name,
                    Signature = f.Signature,
                    BodyOffset = f.BodyOffset,
                    BodySize = f.BodySize,
                    LocalCount = f.LocalCount,
                    Instructions = f.Instructions
                };
            }
        }
    }

    #endregion

    #region Disassembler

    private static List<WasmInstruction> DisassembleBody(byte[] data, int start, int end)
    {
        var instructions = new List<WasmInstruction>();
        var offset = start;
        var depth = 0;

        while (offset < end)
        {
            var instrOffset = offset;
            var opcode = data[offset++];

            var (mnemonic, operand, depthDelta) = DecodeInstruction(opcode, data, ref offset, depth);

            if (depthDelta < 0) depth += depthDelta;

            instructions.Add(new WasmInstruction
            {
                Offset = instrOffset,
                Opcode = opcode,
                Mnemonic = mnemonic,
                Operand = operand,
                Depth = Math.Max(0, depth)
            });

            if (depthDelta > 0) depth += depthDelta;
        }

        return instructions;
    }

    private static (string mnemonic, string operand, int depthDelta) DecodeInstruction(
        byte opcode, byte[] data, ref int offset, int currentDepth)
    {
        switch (opcode)
        {
            case 0x00: return ("unreachable", "", 0);
            case 0x01: return ("nop", "", 0);
            case 0x02:
                var blockType2 = ReadBlockType(data, ref offset);
                return ("block", blockType2, 1);
            case 0x03:
                var blockType3 = ReadBlockType(data, ref offset);
                return ("loop", blockType3, 1);
            case 0x04:
                var blockType4 = ReadBlockType(data, ref offset);
                return ("if", blockType4, 1);
            case 0x05: return ("else", "", 0);
            case 0x0B: return ("end", "", -1);
            case 0x0C: return ("br", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x0D: return ("br_if", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x0E:
                var tableLen = ReadLeb128U(data, ref offset);
                var targets = new List<string>();
                for (uint t = 0; t < tableLen; t++)
                    targets.Add(ReadLeb128U(data, ref offset).ToString());
                var def = ReadLeb128U(data, ref offset);
                return ("br_table", $"[{string.Join(' ', targets)}] {def}", 0);
            case 0x0F: return ("return", "", 0);
            case 0x10: return ("call", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x11:
                var typeIdx = ReadLeb128U(data, ref offset);
                var tableIdx = ReadLeb128U(data, ref offset);
                return ("call_indirect", $"type={typeIdx} table={tableIdx}", 0);

            case 0x1A: return ("drop", "", 0);
            case 0x1B: return ("select", "", 0);

            case 0x20: return ("local.get", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x21: return ("local.set", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x22: return ("local.tee", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x23: return ("global.get", ReadLeb128U(data, ref offset).ToString(), 0);
            case 0x24: return ("global.set", ReadLeb128U(data, ref offset).ToString(), 0);

            case 0x28: return MemLoadStore("i32.load", data, ref offset);
            case 0x29: return MemLoadStore("i64.load", data, ref offset);
            case 0x2A: return MemLoadStore("f32.load", data, ref offset);
            case 0x2B: return MemLoadStore("f64.load", data, ref offset);
            case 0x2C: return MemLoadStore("i32.load8_s", data, ref offset);
            case 0x2D: return MemLoadStore("i32.load8_u", data, ref offset);
            case 0x2E: return MemLoadStore("i32.load16_s", data, ref offset);
            case 0x2F: return MemLoadStore("i32.load16_u", data, ref offset);
            case 0x30: return MemLoadStore("i64.load8_s", data, ref offset);
            case 0x31: return MemLoadStore("i64.load8_u", data, ref offset);
            case 0x32: return MemLoadStore("i64.load16_s", data, ref offset);
            case 0x33: return MemLoadStore("i64.load16_u", data, ref offset);
            case 0x34: return MemLoadStore("i64.load32_s", data, ref offset);
            case 0x35: return MemLoadStore("i64.load32_u", data, ref offset);
            case 0x36: return MemLoadStore("i32.store", data, ref offset);
            case 0x37: return MemLoadStore("i64.store", data, ref offset);
            case 0x38: return MemLoadStore("f32.store", data, ref offset);
            case 0x39: return MemLoadStore("f64.store", data, ref offset);
            case 0x3A: return MemLoadStore("i32.store8", data, ref offset);
            case 0x3B: return MemLoadStore("i32.store16", data, ref offset);
            case 0x3C: return MemLoadStore("i64.store8", data, ref offset);
            case 0x3D: return MemLoadStore("i64.store16", data, ref offset);
            case 0x3E: return MemLoadStore("i64.store32", data, ref offset);

            case 0x3F: offset++; return ("memory.size", "", 0);
            case 0x40: offset++; return ("memory.grow", "", 0);

            case 0x41: return ("i32.const", ReadLeb128S(data, ref offset).ToString(), 0);
            case 0x42: return ("i64.const", ReadLeb128S64(data, ref offset).ToString(), 0);
            case 0x43:
                var f32 = BitConverter.ToSingle(data, offset);
                offset += 4;
                return ("f32.const", f32.ToString("G"), 0);
            case 0x44:
                var f64 = BitConverter.ToDouble(data, offset);
                offset += 8;
                return ("f64.const", f64.ToString("G"), 0);

            case 0x45: return ("i32.eqz", "", 0);
            case 0x46: return ("i32.eq", "", 0);
            case 0x47: return ("i32.ne", "", 0);
            case 0x48: return ("i32.lt_s", "", 0);
            case 0x49: return ("i32.lt_u", "", 0);
            case 0x4A: return ("i32.gt_s", "", 0);
            case 0x4B: return ("i32.gt_u", "", 0);
            case 0x4C: return ("i32.le_s", "", 0);
            case 0x4D: return ("i32.le_u", "", 0);
            case 0x4E: return ("i32.ge_s", "", 0);
            case 0x4F: return ("i32.ge_u", "", 0);

            case 0x50: return ("i64.eqz", "", 0);
            case 0x51: return ("i64.eq", "", 0);
            case 0x52: return ("i64.ne", "", 0);
            case 0x53: return ("i64.lt_s", "", 0);
            case 0x54: return ("i64.lt_u", "", 0);
            case 0x55: return ("i64.gt_s", "", 0);
            case 0x56: return ("i64.gt_u", "", 0);
            case 0x57: return ("i64.le_s", "", 0);
            case 0x58: return ("i64.le_u", "", 0);
            case 0x59: return ("i64.ge_s", "", 0);
            case 0x5A: return ("i64.ge_u", "", 0);

            case 0x5B: return ("f32.eq", "", 0);
            case 0x5C: return ("f32.ne", "", 0);
            case 0x5D: return ("f32.lt", "", 0);
            case 0x5E: return ("f32.gt", "", 0);
            case 0x5F: return ("f32.le", "", 0);
            case 0x60: return ("f32.ge", "", 0);

            case 0x61: return ("f64.eq", "", 0);
            case 0x62: return ("f64.ne", "", 0);
            case 0x63: return ("f64.lt", "", 0);
            case 0x64: return ("f64.gt", "", 0);
            case 0x65: return ("f64.le", "", 0);
            case 0x66: return ("f64.ge", "", 0);

            case 0x67: return ("i32.clz", "", 0);
            case 0x68: return ("i32.ctz", "", 0);
            case 0x69: return ("i32.popcnt", "", 0);
            case 0x6A: return ("i32.add", "", 0);
            case 0x6B: return ("i32.sub", "", 0);
            case 0x6C: return ("i32.mul", "", 0);
            case 0x6D: return ("i32.div_s", "", 0);
            case 0x6E: return ("i32.div_u", "", 0);
            case 0x6F: return ("i32.rem_s", "", 0);
            case 0x70: return ("i32.rem_u", "", 0);
            case 0x71: return ("i32.and", "", 0);
            case 0x72: return ("i32.or", "", 0);
            case 0x73: return ("i32.xor", "", 0);
            case 0x74: return ("i32.shl", "", 0);
            case 0x75: return ("i32.shr_s", "", 0);
            case 0x76: return ("i32.shr_u", "", 0);
            case 0x77: return ("i32.rotl", "", 0);
            case 0x78: return ("i32.rotr", "", 0);

            case 0x79: return ("i64.clz", "", 0);
            case 0x7A: return ("i64.ctz", "", 0);
            case 0x7B: return ("i64.popcnt", "", 0);
            case 0x7C: return ("i64.add", "", 0);
            case 0x7D: return ("i64.sub", "", 0);
            case 0x7E: return ("i64.mul", "", 0);
            case 0x7F: return ("i64.div_s", "", 0);
            case 0x80: return ("i64.div_u", "", 0);
            case 0x81: return ("i64.rem_s", "", 0);
            case 0x82: return ("i64.rem_u", "", 0);
            case 0x83: return ("i64.and", "", 0);
            case 0x84: return ("i64.or", "", 0);
            case 0x85: return ("i64.xor", "", 0);
            case 0x86: return ("i64.shl", "", 0);
            case 0x87: return ("i64.shr_s", "", 0);
            case 0x88: return ("i64.shr_u", "", 0);
            case 0x89: return ("i64.rotl", "", 0);
            case 0x8A: return ("i64.rotr", "", 0);

            case 0x8B: return ("f32.abs", "", 0);
            case 0x8C: return ("f32.neg", "", 0);
            case 0x8D: return ("f32.ceil", "", 0);
            case 0x8E: return ("f32.floor", "", 0);
            case 0x8F: return ("f32.trunc", "", 0);
            case 0x90: return ("f32.nearest", "", 0);
            case 0x91: return ("f32.sqrt", "", 0);
            case 0x92: return ("f32.add", "", 0);
            case 0x93: return ("f32.sub", "", 0);
            case 0x94: return ("f32.mul", "", 0);
            case 0x95: return ("f32.div", "", 0);
            case 0x96: return ("f32.min", "", 0);
            case 0x97: return ("f32.max", "", 0);
            case 0x98: return ("f32.copysign", "", 0);

            case 0x99: return ("f64.abs", "", 0);
            case 0x9A: return ("f64.neg", "", 0);
            case 0x9B: return ("f64.ceil", "", 0);
            case 0x9C: return ("f64.floor", "", 0);
            case 0x9D: return ("f64.trunc", "", 0);
            case 0x9E: return ("f64.nearest", "", 0);
            case 0x9F: return ("f64.sqrt", "", 0);
            case 0xA0: return ("f64.add", "", 0);
            case 0xA1: return ("f64.sub", "", 0);
            case 0xA2: return ("f64.mul", "", 0);
            case 0xA3: return ("f64.div", "", 0);
            case 0xA4: return ("f64.min", "", 0);
            case 0xA5: return ("f64.max", "", 0);
            case 0xA6: return ("f64.copysign", "", 0);

            case 0xA7: return ("i32.wrap_i64", "", 0);
            case 0xA8: return ("i32.trunc_f32_s", "", 0);
            case 0xA9: return ("i32.trunc_f32_u", "", 0);
            case 0xAA: return ("i32.trunc_f64_s", "", 0);
            case 0xAB: return ("i32.trunc_f64_u", "", 0);
            case 0xAC: return ("i64.extend_i32_s", "", 0);
            case 0xAD: return ("i64.extend_i32_u", "", 0);
            case 0xAE: return ("i64.trunc_f32_s", "", 0);
            case 0xAF: return ("i64.trunc_f32_u", "", 0);
            case 0xB0: return ("i64.trunc_f64_s", "", 0);
            case 0xB1: return ("i64.trunc_f64_u", "", 0);
            case 0xB2: return ("f32.convert_i32_s", "", 0);
            case 0xB3: return ("f32.convert_i32_u", "", 0);
            case 0xB4: return ("f32.convert_i64_s", "", 0);
            case 0xB5: return ("f32.convert_i64_u", "", 0);
            case 0xB6: return ("f32.demote_f64", "", 0);
            case 0xB7: return ("f64.convert_i32_s", "", 0);
            case 0xB8: return ("f64.convert_i32_u", "", 0);
            case 0xB9: return ("f64.convert_i64_s", "", 0);
            case 0xBA: return ("f64.convert_i64_u", "", 0);
            case 0xBB: return ("f64.promote_f32", "", 0);
            case 0xBC: return ("i32.reinterpret_f32", "", 0);
            case 0xBD: return ("i64.reinterpret_f64", "", 0);
            case 0xBE: return ("f32.reinterpret_i32", "", 0);
            case 0xBF: return ("f64.reinterpret_i64", "", 0);

            case 0xC0: return ("i32.extend8_s", "", 0);
            case 0xC1: return ("i32.extend16_s", "", 0);
            case 0xC2: return ("i64.extend8_s", "", 0);
            case 0xC3: return ("i64.extend16_s", "", 0);
            case 0xC4: return ("i64.extend32_s", "", 0);

            default: return ($"<unknown 0x{opcode:X2}>", "", 0);
        }
    }

    private static (string mnemonic, string operand, int depthDelta) MemLoadStore(
        string name, byte[] data, ref int offset)
    {
        var align = ReadLeb128U(data, ref offset);
        var memOffset = ReadLeb128U(data, ref offset);
        return (name, $"align={1u << (int)align} offset={memOffset}", 0);
    }

    private static string ReadBlockType(byte[] data, ref int offset)
    {
        var b = data[offset];
        if (b == 0x40) { offset++; return ""; }
        if (b >= 0x7C && b <= 0x7F) { offset++; return ValTypeName(b); }
        var idx = ReadLeb128S(data, ref offset);
        return $"type={idx}";
    }

    #endregion

    #region WAT Text Generation

    private string GenerateWatText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("(module");

        foreach (var ft in _funcTypes)
            sb.AppendLine($"  (type (;{ft.Index};) (func {ft.Signature}))");

        foreach (var imp in _imports)
            sb.AppendLine($"  (import \"{imp.Module}\" \"{imp.Field}\" ({imp.KindName.ToLowerInvariant()} {imp.TypeDescription}))");

        foreach (var func in _functions)
        {
            sb.AppendLine($"  (func (;{_importFunctionCount + func.Index};) {func.DisplayName} {func.Signature}");
            if (func.LocalCount > 0)
                sb.AppendLine($"    (local {func.LocalCount} vars)");
            foreach (var instr in func.Instructions)
                sb.AppendLine($"    {instr.Indent}{instr.Display}");
            sb.AppendLine("  )");
        }

        foreach (var exp in _exports)
            sb.AppendLine($"  (export \"{exp.Name}\" ({exp.KindName.ToLowerInvariant()} {exp.FunctionIndex}))");

        if (_memoryCount > 0)
            sb.AppendLine($"  (memory {_memoryCount})");

        for (var i = 0; i < _globals.Count; i++)
        {
            var g = _globals[i];
            var mutStr = g.Mutable ? $"(mut {g.ValueType})" : g.ValueType;
            sb.AppendLine($"  (global (;{i};) {mutStr} {g.InitExpression})");
        }

        if (_startFunction >= 0)
            sb.AppendLine($"  (start {_startFunction})");

        sb.AppendLine(")");
        return sb.ToString();
    }

    private static string FormatFunction(WasmFunction func)
    {
        var sb = new StringBuilder();
        sb.AppendLine($";; Function {func.DisplayName}");
        sb.AppendLine($";; Signature: {func.Signature}");
        sb.AppendLine($";; Body offset: 0x{func.BodyOffset:X}, size: {func.BodySize} bytes");
        sb.AppendLine($";; Locals: {func.LocalCount}");
        sb.AppendLine();

        foreach (var instr in func.Instructions)
        {
            sb.Append($"  {instr.OffsetHex}  ");
            sb.Append(instr.Indent);
            sb.AppendLine(instr.Display);
        }

        return sb.ToString();
    }

    #endregion

    #region String Extraction

    private static void ExtractStringsFromSegment(byte[] data, long baseOffset, int segIdx,
        string? filter, int minLength, List<WasmStringResult> results)
    {
        var current = new StringBuilder();
        var startOffset = 0L;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (b >= 0x20 && b <= 0x7E)
            {
                if (current.Length == 0) startOffset = baseOffset + i;
                current.Append((char)b);
            }
            else
            {
                if (current.Length >= minLength)
                {
                    var val = current.ToString();
                    if (string.IsNullOrEmpty(filter) ||
                        val.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new WasmStringResult
                        {
                            Offset = startOffset,
                            DataSegmentIndex = segIdx,
                            Value = val
                        });
                    }
                }
                current.Clear();
            }
        }

        if (current.Length >= minLength)
        {
            var val = current.ToString();
            if (string.IsNullOrEmpty(filter) ||
                val.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new WasmStringResult
                {
                    Offset = startOffset,
                    DataSegmentIndex = segIdx,
                    Value = val
                });
            }
        }
    }

    #endregion

    #region Debug Execution Engine

    private void ExecuteInstruction(WasmInstruction instr, WasmDebugState state)
    {
        try
        {
            switch (instr.Mnemonic)
            {
                case "i32.const":
                    if (int.TryParse(instr.Operand, out var i32Val))
                        PushStack(state, "i32", i32Val.ToString());
                    break;

                case "i64.const":
                    if (long.TryParse(instr.Operand, out var i64Val))
                        PushStack(state, "i64", i64Val.ToString());
                    break;

                case "f32.const":
                    PushStack(state, "f32", instr.Operand);
                    break;

                case "f64.const":
                    PushStack(state, "f64", instr.Operand);
                    break;

                case "local.get":
                    if (int.TryParse(instr.Operand, out var lgIdx) && lgIdx < state.Locals.Count)
                        PushStack(state, state.Locals[lgIdx].Type, state.Locals[lgIdx].Value);
                    break;

                case "local.set":
                    if (int.TryParse(instr.Operand, out var lsIdx) && lsIdx < state.Locals.Count && state.Stack.Count > 0)
                    {
                        var val = PopStack(state);
                        state.Locals[lsIdx].Value = val.Value;
                    }
                    break;

                case "local.tee":
                    if (int.TryParse(instr.Operand, out var ltIdx) && ltIdx < state.Locals.Count && state.Stack.Count > 0)
                    {
                        var top = state.Stack[^1];
                        state.Locals[ltIdx].Value = top.Value;
                    }
                    break;

                case "i32.add":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) + int.Parse(b)).ToString());
                    break;

                case "i32.sub":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) - int.Parse(b)).ToString());
                    break;

                case "i32.mul":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) * int.Parse(b)).ToString());
                    break;

                case "i32.div_s":
                    BinaryOp(state, "i32", (a, b) =>
                    {
                        var divisor = int.Parse(b);
                        return divisor == 0 ? "trap:div_by_zero" : (int.Parse(a) / divisor).ToString();
                    });
                    break;

                case "i32.rem_s":
                    BinaryOp(state, "i32", (a, b) =>
                    {
                        var divisor = int.Parse(b);
                        return divisor == 0 ? "trap:div_by_zero" : (int.Parse(a) % divisor).ToString();
                    });
                    break;

                case "i32.and":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) & int.Parse(b)).ToString());
                    break;

                case "i32.or":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) | int.Parse(b)).ToString());
                    break;

                case "i32.xor":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) ^ int.Parse(b)).ToString());
                    break;

                case "i32.shl":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) << (int.Parse(b) & 31)).ToString());
                    break;

                case "i32.shr_s":
                    BinaryOp(state, "i32", (a, b) => (int.Parse(a) >> (int.Parse(b) & 31)).ToString());
                    break;

                case "i32.shr_u":
                    BinaryOp(state, "i32", (a, b) => ((uint)int.Parse(a) >> (int.Parse(b) & 31)).ToString());
                    break;

                case "i32.eq":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) == int.Parse(b));
                    break;

                case "i32.ne":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) != int.Parse(b));
                    break;

                case "i32.lt_s":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) < int.Parse(b));
                    break;

                case "i32.gt_s":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) > int.Parse(b));
                    break;

                case "i32.le_s":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) <= int.Parse(b));
                    break;

                case "i32.ge_s":
                    CompareOp(state, "i32", (a, b) => int.Parse(a) >= int.Parse(b));
                    break;

                case "i32.eqz":
                    if (state.Stack.Count > 0)
                    {
                        var v = PopStack(state);
                        PushStack(state, "i32", (int.Parse(v.Value) == 0 ? 1 : 0).ToString());
                    }
                    break;

                case "drop":
                    if (state.Stack.Count > 0) PopStack(state);
                    break;

                case "select":
                    if (state.Stack.Count >= 3)
                    {
                        var cond = PopStack(state);
                        var val2 = PopStack(state);
                        var val1 = PopStack(state);
                        PushStack(state, val1.Type, int.Parse(cond.Value) != 0 ? val1.Value : val2.Value);
                    }
                    break;

                case "nop":
                case "block":
                case "loop":
                case "if":
                case "else":
                case "end":
                    break;

                default:
                    state.Output.Add($"[skipped: {instr.Display}]");
                    break;
            }
        }
        catch (Exception ex)
        {
            state.Error = $"Runtime error at {instr.OffsetHex}: {ex.Message}";
            state.Output.Add($"[error: {ex.Message}]");
        }
    }

    private static void PushStack(WasmDebugState state, string type, string value)
    {
        state.Stack.Add(new WasmStackValue
        {
            Index = state.Stack.Count,
            Type = type,
            Value = value
        });
    }

    private static WasmStackValue PopStack(WasmDebugState state)
    {
        var val = state.Stack[^1];
        state.Stack.RemoveAt(state.Stack.Count - 1);
        return val;
    }

    private static void BinaryOp(WasmDebugState state, string type, Func<string, string, string> op)
    {
        if (state.Stack.Count < 2) return;
        var b = PopStack(state);
        var a = PopStack(state);
        PushStack(state, type, op(a.Value, b.Value));
    }

    private static void CompareOp(WasmDebugState state, string type, Func<string, string, bool> pred)
    {
        if (state.Stack.Count < 2) return;
        var b = PopStack(state);
        var a = PopStack(state);
        PushStack(state, "i32", pred(a.Value, b.Value) ? "1" : "0");
    }

    #endregion

    #region LEB128 & Utilities

    private static uint ReadLeb128U(byte[] data, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (offset < data.Length)
        {
            var b = data[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    private static int ReadLeb128S(byte[] data, ref int offset)
    {
        int result = 0;
        var shift = 0;
        byte b;
        do
        {
            b = data[offset++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        if (shift < 32 && (b & 0x40) != 0)
            result |= ~0 << shift;

        return result;
    }

    private static long ReadLeb128S64(byte[] data, ref int offset)
    {
        long result = 0;
        var shift = 0;
        byte b;
        do
        {
            b = data[offset++];
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        if (shift < 64 && (b & 0x40) != 0)
            result |= ~0L << shift;

        return result;
    }

    private static void ReadLimits(byte[] data, ref int offset)
    {
        var flags = data[offset++];
        ReadLeb128U(data, ref offset);
        if ((flags & 0x01) != 0)
            ReadLeb128U(data, ref offset);
    }

    private static string ReadInitExpression(byte[] data, ref int offset)
    {
        var sb = new StringBuilder("(");
        while (offset < data.Length)
        {
            var op = data[offset++];
            if (op == 0x0B) break;
            switch (op)
            {
                case 0x41:
                    sb.Append($"i32.const {ReadLeb128S(data, ref offset)}");
                    break;
                case 0x42:
                    sb.Append($"i64.const {ReadLeb128S64(data, ref offset)}");
                    break;
                case 0x43:
                    sb.Append($"f32.const {BitConverter.ToSingle(data, offset):G}");
                    offset += 4;
                    break;
                case 0x44:
                    sb.Append($"f64.const {BitConverter.ToDouble(data, offset):G}");
                    offset += 8;
                    break;
                case 0x23:
                    sb.Append($"global.get {ReadLeb128U(data, ref offset)}");
                    break;
                default:
                    sb.Append($"op(0x{op:X2})");
                    break;
            }
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static long EvalI32InitExpression(byte[] data, ref int offset)
    {
        long val = 0;
        while (offset < data.Length)
        {
            var op = data[offset++];
            if (op == 0x0B) break;
            if (op == 0x41) val = ReadLeb128S(data, ref offset);
            else if (op == 0x23) ReadLeb128U(data, ref offset);
        }
        return val;
    }

    private static void SkipInitExpression(byte[] data, ref int offset)
    {
        while (offset < data.Length && data[offset++] != 0x0B) { }
    }

    private static string ValTypeName(byte b) => b switch
    {
        0x7F => "i32",
        0x7E => "i64",
        0x7D => "f32",
        0x7C => "f64",
        0x70 => "funcref",
        0x6F => "externref",
        _ => $"0x{b:X2}"
    };

    #endregion

    public void Dispose()
    {
        lock (_gate)
        {
            _binary = null;
            _loadedPath = null;
            ClearParsedData();
        }
    }
}
