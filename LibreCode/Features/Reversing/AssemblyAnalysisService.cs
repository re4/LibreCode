using System.Collections.ObjectModel;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace LibreCode.Features.Reversing;

/// <summary>
/// Wraps ICSharpCode.Decompiler and System.Reflection.Metadata to provide
/// decompilation, IL disassembly, PE inspection, string search, hex read,
/// and IL step-through debug APIs.
/// </summary>
public sealed class AssemblyAnalysisService : IDisposable
{
    private PEFile? _peFile;
    private CSharpDecompiler? _decompiler;
    private string? _loadedPath;
    private readonly Lock _gate = new();

    private readonly List<ILMethod> _methods = [];
    private ILDebugState? _debugState;

    /// <summary>Currently loaded assembly path, or null.</summary>
    public string? LoadedPath => _loadedPath;

    /// <summary>Whether an assembly is currently loaded.</summary>
    public bool IsLoaded => _peFile is not null;

    /// <summary>Loads a .NET assembly for analysis.</summary>
    public Task LoadAssemblyAsync(string path)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                _peFile?.Dispose();
                _peFile = new PEFile(path);
                _decompiler = new CSharpDecompiler(path, new DecompilerSettings(LanguageVersion.CSharp1)
                {
                    ThrowOnAssemblyResolveErrors = false
                });
                _loadedPath = path;
                _methods.Clear();
                _debugState = null;
                ExtractMethods();
            }
        });
    }

    /// <summary>Decompiles the entire module to C# source.</summary>
    public Task<string> DecompileWholeModuleAsync()
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_decompiler is null) return "No assembly loaded.";
                return _decompiler.DecompileWholeModuleAsString();
            }
        });
    }

    /// <summary>Decompiles a single type by full name.</summary>
    public Task<string> DecompileTypeAsync(string fullTypeName)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_decompiler is null) return "No assembly loaded.";
                try
                {
                    var name = new FullTypeName(fullTypeName);
                    return _decompiler.DecompileTypeAsString(name);
                }
                catch (Exception ex)
                {
                    return $"// Decompilation error: {ex.Message}";
                }
            }
        });
    }

    /// <summary>Returns full IL disassembly for the loaded module.</summary>
    public Task<string> GetILDisassemblyAsync()
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_peFile is null) return "No assembly loaded.";
                return DisassembleModule(_peFile);
            }
        });
    }

    /// <summary>Returns IL disassembly for a single type.</summary>
    public Task<string> GetILForTypeAsync(string fullTypeName)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_peFile is null || _decompiler is null) return "No assembly loaded.";
                try
                {
                    var name = new FullTypeName(fullTypeName);
                    var type = _decompiler.TypeSystem.FindType(name).GetDefinition();
                    if (type is null) return $"// Type not found: {fullTypeName}";

                    using var writer = new StringWriter();
                    var output = new PlainTextOutput(writer);
                    var disassembler = new ReflectionDisassembler(output, CancellationToken.None);
                    var token = (TypeDefinitionHandle)type.MetadataToken;
                    disassembler.DisassembleType(_peFile, token);
                    return writer.ToString();
                }
                catch (Exception ex)
                {
                    return $"// IL disassembly error: {ex.Message}";
                }
            }
        });
    }

    /// <summary>Inspects PE headers, sections, CLR metadata, and references.</summary>
    public PEInspectionResult? GetPEInfo()
    {
        lock (_gate)
        {
            if (_peFile is null || _loadedPath is null) return null;

            var reader = _peFile.Metadata;
            var headers = _peFile.Reader.PEHeaders;
            var peHeader = headers.PEHeader;

            string? targetFramework = null;
            try
            {
                var asmDef = reader.GetAssemblyDefinition();
                foreach (var caHandle in asmDef.GetCustomAttributes())
                {
                    var ca = reader.GetCustomAttribute(caHandle);
                    var ctorHandle = ca.Constructor;
                    if (ctorHandle.Kind == HandleKind.MemberReference)
                    {
                        var mr = reader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                        var parentRef = mr.Parent;
                        if (parentRef.Kind == HandleKind.TypeReference)
                        {
                            var tr = reader.GetTypeReference((TypeReferenceHandle)parentRef);
                            var attrName = reader.GetString(tr.Name);
                            if (attrName == "TargetFrameworkAttribute")
                            {
                                var blob = reader.GetBlobBytes(ca.Value);
                                if (blob.Length > 4)
                                {
                                    var strLen = blob[2];
                                    targetFramework = System.Text.Encoding.UTF8.GetString(blob, 3, strLen);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var sections = new List<PESectionInfo>();
            foreach (var sh in headers.SectionHeaders)
            {
                sections.Add(new PESectionInfo
                {
                    Name = sh.Name,
                    VirtualAddress = sh.VirtualAddress,
                    VirtualSize = sh.VirtualSize,
                    RawDataSize = sh.SizeOfRawData
                });
            }

            var refs = new List<AssemblyReferenceInfo>();
            foreach (var arHandle in reader.AssemblyReferences)
            {
                var ar = reader.GetAssemblyReference(arHandle);
                string? pktStr = null;
                if (!ar.PublicKeyOrToken.IsNil)
                {
                    var bytes = reader.GetBlobBytes(ar.PublicKeyOrToken);
                    pktStr = Convert.ToHexString(bytes).ToLowerInvariant();
                }

                refs.Add(new AssemblyReferenceInfo
                {
                    Name = reader.GetString(ar.Name),
                    Version = ar.Version.ToString(),
                    PublicKeyToken = pktStr
                });
            }

            string? asmName = null;
            string? asmVersion = null;
            try
            {
                var ad = reader.GetAssemblyDefinition();
                asmName = reader.GetString(ad.Name);
                asmVersion = ad.Version.ToString();
            }
            catch { }

            return new PEInspectionResult
            {
                FileName = Path.GetFileName(_loadedPath),
                FileSizeBytes = new FileInfo(_loadedPath).Length,
                Machine = headers.CoffHeader.Machine.ToString(),
                Subsystem = peHeader?.Subsystem.ToString() ?? "Unknown",
                ImageBase = peHeader?.ImageBase ?? 0,
                FileAlignment = peHeader?.FileAlignment ?? 0,
                DllCharacteristics = peHeader?.DllCharacteristics.ToString() ?? "None",
                EntryPointToken = headers.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0,
                IsManaged = headers.CorHeader is not null,
                ClrVersion = headers.MetadataStartOffset > 0 ? reader.MetadataVersion : null,
                TargetFramework = targetFramework,
                AssemblyName = asmName,
                AssemblyVersion = asmVersion,
                Sections = sections,
                References = refs
            };
        }
    }

    /// <summary>Searches user strings and string literals in the metadata.</summary>
    public Task<List<StringSearchResult>> SearchStringsAsync(string? filter)
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_peFile is null) return [];

                var results = new List<StringSearchResult>();
                var reader = _peFile.Metadata;
                var heapSize = reader.GetHeapSize(HeapIndex.UserString);
                if (heapSize == 0) return results;

                var offset = 1;
                while (offset < heapSize)
                {
                    try
                    {
                        var handle = MetadataTokens.UserStringHandle(offset);
                        var value = reader.GetUserString(handle);
                        var byteLen = value.Length * 2 + 1;
                        var recordSize = GetCompressedIntSize(byteLen) + byteLen;

                        if (!string.IsNullOrEmpty(value))
                        {
                            var token = MetadataTokens.GetToken(handle);
                            if (string.IsNullOrEmpty(filter) ||
                                value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            {
                                results.Add(new StringSearchResult
                                {
                                    Token = token,
                                    Offset = offset,
                                    Value = value
                                });
                            }
                        }

                        offset += Math.Max(1, recordSize);
                    }
                    catch
                    {
                        offset++;
                    }
                }

                return results;
            }
        });
    }

    private static int GetCompressedIntSize(int value) => value switch
    {
        <= 0x7F => 1,
        <= 0x3FFF => 2,
        _ => 4
    };

    /// <summary>Builds a namespace/type hierarchy tree for navigation.</summary>
    public Task<ObservableCollection<TypeTreeNode>> GetTypeTreeAsync()
    {
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_decompiler is null) return [];

                var nsMap = new SortedDictionary<string, TypeTreeNode>(StringComparer.Ordinal);

                foreach (var type in _decompiler.TypeSystem.MainModule.TypeDefinitions)
                {
                    if (type.Name.StartsWith('<')) continue;

                    var ns = type.Namespace ?? "(global)";
                    if (!nsMap.TryGetValue(ns, out var nsNode))
                    {
                        nsNode = new TypeTreeNode
                        {
                            DisplayName = ns,
                            FullName = ns,
                            Kind = TypeTreeNodeKind.Namespace
                        };
                        nsMap[ns] = nsNode;
                    }

                    var typeNode = new TypeTreeNode
                    {
                        DisplayName = type.Name,
                        FullName = type.FullName,
                        Kind = TypeTreeNodeKind.Type
                    };

                    nsNode.Children.Add(typeNode);
                }

                return new ObservableCollection<TypeTreeNode>(nsMap.Values);
            }
        });
    }

    /// <summary>Reads raw bytes from the loaded file for the hex viewer.</summary>
    public Task<List<HexLine>> ReadBytesAsync(long offset, int lineCount)
    {
        return Task.Run(() =>
        {
            if (_loadedPath is null) return [];

            const int bytesPerLine = 16;
            var lines = new List<HexLine>(lineCount);

            using var fs = new FileStream(_loadedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[bytesPerLine];
            for (var i = 0; i < lineCount; i++)
            {
                var read = fs.Read(buffer, 0, bytesPerLine);
                if (read == 0) break;

                var bytes = new byte[read];
                Array.Copy(buffer, bytes, read);
                lines.Add(new HexLine
                {
                    Offset = offset + (long)i * bytesPerLine,
                    Bytes = bytes
                });
            }

            return lines;
        });
    }

    /// <summary>Returns the total file size in bytes.</summary>
    public long GetFileSizeBytes()
    {
        if (_loadedPath is null) return 0;
        return new FileInfo(_loadedPath).Length;
    }

    #region IL Debug Engine

    /// <summary>Returns all extracted methods with decoded IL instructions.</summary>
    public List<ILMethod> GetMethods()
    {
        lock (_gate) return [.. _methods];
    }

    /// <summary>Returns a specific method by index.</summary>
    public ILMethod? GetMethod(int index)
    {
        lock (_gate) return index >= 0 && index < _methods.Count ? _methods[index] : null;
    }

    /// <summary>Initializes a debug session for the specified method.</summary>
    public ILDebugState StartILDebugSession(int methodIndex)
    {
        lock (_gate)
        {
            if (methodIndex < 0 || methodIndex >= _methods.Count)
                throw new ArgumentOutOfRangeException(nameof(methodIndex));

            var method = _methods[methodIndex];
            var locals = method.Locals.Select(l => new ILLocalVariable
            {
                Index = l.Index,
                Name = $"V_{l.Index}",
                Type = l.TypeName,
                Value = DefaultValueForType(l.TypeName)
            }).ToList();

            _debugState = new ILDebugState
            {
                CurrentMethodIndex = methodIndex,
                InstructionPointer = 0,
                IsRunning = false,
                IsPaused = true,
                Stack = [],
                Locals = locals,
                Breakpoints = _debugState?.Breakpoints ?? [],
                Output = []
            };

            return _debugState;
        }
    }

    /// <summary>Executes the next IL instruction and returns updated debug state.</summary>
    public ILDebugState? StepILInstruction()
    {
        lock (_gate)
        {
            if (_debugState is null || !_debugState.IsPaused) return _debugState;

            var method = _methods.ElementAtOrDefault(_debugState.CurrentMethodIndex);
            if (method is null) return _debugState;

            if (_debugState.InstructionPointer >= method.Instructions.Count)
            {
                _debugState.IsPaused = false;
                _debugState.IsRunning = false;
                _debugState.Output.Add("[execution completed]");
                return _debugState;
            }

            var instr = method.Instructions[_debugState.InstructionPointer];
            ExecuteILInstruction(instr, _debugState);
            _debugState.InstructionPointer++;

            if (_debugState.InstructionPointer >= method.Instructions.Count)
            {
                _debugState.IsRunning = false;
                _debugState.IsPaused = false;
                _debugState.Output.Add("[execution completed]");
            }

            return _debugState;
        }
    }

    /// <summary>Runs until the next breakpoint or end of method.</summary>
    public ILDebugState? RunILToBreakpoint()
    {
        lock (_gate)
        {
            if (_debugState is null) return null;

            var method = _methods.ElementAtOrDefault(_debugState.CurrentMethodIndex);
            if (method is null) return _debugState;

            _debugState.IsRunning = true;
            _debugState.IsPaused = false;

            const int maxSteps = 100_000;
            for (var i = 0; i < maxSteps; i++)
            {
                if (_debugState.InstructionPointer >= method.Instructions.Count)
                {
                    _debugState.IsRunning = false;
                    _debugState.Output.Add("[execution completed]");
                    break;
                }

                var bp = _debugState.Breakpoints
                    .FirstOrDefault(b => b.Enabled
                        && b.MethodIndex == _debugState.CurrentMethodIndex
                        && b.InstructionIndex == _debugState.InstructionPointer);

                if (bp is not null && i > 0)
                {
                    _debugState.IsPaused = true;
                    _debugState.IsRunning = false;
                    _debugState.Output.Add($"[breakpoint hit at {bp.Label}]");
                    break;
                }

                var instr = method.Instructions[_debugState.InstructionPointer];
                ExecuteILInstruction(instr, _debugState);
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
    public int AddILBreakpoint(int methodIndex, int instructionIndex)
    {
        lock (_gate)
        {
            _debugState ??= new ILDebugState();

            var id = _debugState.Breakpoints.Count > 0
                ? _debugState.Breakpoints.Max(b => b.Id) + 1
                : 1;

            _debugState.Breakpoints.Add(new ILBreakpoint
            {
                Id = id,
                MethodIndex = methodIndex,
                InstructionIndex = instructionIndex
            });

            return id;
        }
    }

    /// <summary>Removes a breakpoint by id.</summary>
    public void RemoveILBreakpoint(int id)
    {
        lock (_gate)
        {
            _debugState?.Breakpoints.RemoveAll(b => b.Id == id);
        }
    }

    /// <summary>Returns the current IL debug state snapshot.</summary>
    public ILDebugState? GetILDebugState()
    {
        lock (_gate) return _debugState;
    }

    /// <summary>Resets the IL debug session to the start of the current method.</summary>
    public ILDebugState? ResetILDebugSession()
    {
        lock (_gate)
        {
            if (_debugState is null) return null;
            return StartILDebugSession(_debugState.CurrentMethodIndex);
        }
    }

    private void ExtractMethods()
    {
        if (_peFile is null || _decompiler is null) return;

        var reader = _peFile.Metadata;
        var methodIndex = 0;

        foreach (var typeDef in _decompiler.TypeSystem.MainModule.TypeDefinitions)
        {
            if (typeDef.Name.StartsWith('<')) continue;

            foreach (var method in typeDef.Methods)
            {
                try
                {
                    var handle = (MethodDefinitionHandle)method.MetadataToken;
                    var methodDef = reader.GetMethodDefinition(handle);
                    var rva = methodDef.RelativeVirtualAddress;
                    if (rva == 0) continue;

                    var body = _peFile.Reader.GetMethodBody(rva);
                    var ilBytes = body.GetILBytes();
                    if (ilBytes is null || ilBytes.Length == 0) continue;

                    var locals = new List<ILLocalInfo>();
                    if (!body.LocalSignature.IsNil)
                    {
                        try
                        {
                            var sigReader = reader.GetBlobReader(
                                reader.GetStandaloneSignature(body.LocalSignature).Signature);
                            if (sigReader.ReadSignatureHeader().Kind == SignatureKind.LocalVariables)
                            {
                                var count = sigReader.ReadCompressedInteger();
                                for (var i = 0; i < count; i++)
                                {
                                    locals.Add(new ILLocalInfo
                                    {
                                        Index = i,
                                        TypeName = ReadTypeNameFromSig(ref sigReader)
                                    });
                                }
                            }
                        }
                        catch
                        {
                            for (var i = 0; i < body.LocalVariablesInitialized.GetHashCode(); i++)
                                locals.Add(new ILLocalInfo { Index = i, TypeName = "object" });
                        }
                    }

                    var instructions = DecodeILBody(ilBytes, reader);

                    _methods.Add(new ILMethod
                    {
                        Index = methodIndex++,
                        FullName = method.FullName,
                        TypeName = typeDef.Name,
                        MethodName = method.Name,
                        MetadataToken = MetadataTokens.GetToken(handle),
                        LocalCount = locals.Count,
                        MaxStack = body.MaxStack,
                        Instructions = instructions,
                        Locals = locals
                    });
                }
                catch { }
            }
        }
    }

    private static string ReadTypeNameFromSig(ref BlobReader sigReader)
    {
        try
        {
            var b = sigReader.ReadByte();
            return b switch
            {
                0x02 => "bool",
                0x03 => "char",
                0x04 => "sbyte",
                0x05 => "byte",
                0x06 => "short",
                0x07 => "ushort",
                0x08 => "int",
                0x09 => "uint",
                0x0A => "long",
                0x0B => "ulong",
                0x0C => "float",
                0x0D => "double",
                0x0E => "string",
                0x1C => "object",
                0x0F => "nint",
                0x10 => "nuint",
                _ => "object"
            };
        }
        catch { return "object"; }
    }

    private static List<ILInstruction> DecodeILBody(byte[] il, MetadataReader reader)
    {
        var instructions = new List<ILInstruction>();
        var offset = 0;
        var instrIndex = 0;

        while (offset < il.Length)
        {
            var instrOffset = offset;
            var b = il[offset++];

            string mnemonic;
            string operand;

            if (b == 0xFE)
            {
                if (offset >= il.Length) break;
                var b2 = il[offset++];
                (mnemonic, operand) = DecodeTwoByte(b2, il, ref offset, reader);
            }
            else
            {
                (mnemonic, operand) = DecodeOneByte(b, il, ref offset, reader);
            }

            instructions.Add(new ILInstruction
            {
                Index = instrIndex++,
                Offset = instrOffset,
                OpCodeValue = b == 0xFE ? (ushort)(0xFE00 | il[instrOffset + 1]) : b,
                Mnemonic = mnemonic,
                Operand = operand
            });
        }

        return instructions;
    }

    private static (string mnemonic, string operand) DecodeOneByte(
        byte op, byte[] il, ref int offset, MetadataReader reader)
    {
        switch (op)
        {
            case 0x00: return ("nop", "");
            case 0x01: return ("break", "");
            case 0x02: return ("ldarg.0", "");
            case 0x03: return ("ldarg.1", "");
            case 0x04: return ("ldarg.2", "");
            case 0x05: return ("ldarg.3", "");
            case 0x06: return ("ldloc.0", "");
            case 0x07: return ("ldloc.1", "");
            case 0x08: return ("ldloc.2", "");
            case 0x09: return ("ldloc.3", "");
            case 0x0A: return ("stloc.0", "");
            case 0x0B: return ("stloc.1", "");
            case 0x0C: return ("stloc.2", "");
            case 0x0D: return ("stloc.3", "");
            case 0x0E: return ("ldarg.s", ReadU8(il, ref offset).ToString());
            case 0x0F: return ("ldarga.s", ReadU8(il, ref offset).ToString());
            case 0x10: return ("starg.s", ReadU8(il, ref offset).ToString());
            case 0x11: return ("ldloc.s", ReadU8(il, ref offset).ToString());
            case 0x12: return ("ldloca.s", ReadU8(il, ref offset).ToString());
            case 0x13: return ("stloc.s", ReadU8(il, ref offset).ToString());
            case 0x14: return ("ldnull", "");
            case 0x15: return ("ldc.i4.m1", "");
            case 0x16: return ("ldc.i4.0", "");
            case 0x17: return ("ldc.i4.1", "");
            case 0x18: return ("ldc.i4.2", "");
            case 0x19: return ("ldc.i4.3", "");
            case 0x1A: return ("ldc.i4.4", "");
            case 0x1B: return ("ldc.i4.5", "");
            case 0x1C: return ("ldc.i4.6", "");
            case 0x1D: return ("ldc.i4.7", "");
            case 0x1E: return ("ldc.i4.8", "");
            case 0x1F: return ("ldc.i4.s", ReadI8(il, ref offset).ToString());
            case 0x20: return ("ldc.i4", ReadI32(il, ref offset).ToString());
            case 0x21: return ("ldc.i8", ReadI64(il, ref offset).ToString());
            case 0x22: return ("ldc.r4", ReadF32(il, ref offset).ToString("G"));
            case 0x23: return ("ldc.r8", ReadF64(il, ref offset).ToString("G"));
            case 0x25: return ("dup", "");
            case 0x26: return ("pop", "");
            case 0x27: return ("jmp", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x28: return ("call", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x29: return ("calli", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x2A: return ("ret", "");
            case 0x2B: return ("br.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x2C: return ("brfalse.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x2D: return ("brtrue.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x2E: return ("beq.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x2F: return ("bge.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x30: return ("bgt.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x31: return ("ble.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x32: return ("blt.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x33: return ("bne.un.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x34: return ("bge.un.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x35: return ("bgt.un.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x36: return ("ble.un.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x37: return ("blt.un.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0x38: return ("br", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x39: return ("brfalse", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3A: return ("brtrue", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3B: return ("beq", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3C: return ("bge", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3D: return ("bgt", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3E: return ("ble", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x3F: return ("blt", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x40: return ("bne.un", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x41: return ("bge.un", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x42: return ("bgt.un", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x43: return ("ble.un", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x44: return ("blt.un", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0x45:
                var n = ReadU32(il, ref offset);
                var targets = new string[n];
                for (uint i = 0; i < n; i++)
                    targets[i] = ReadI32(il, ref offset).ToString();
                return ("switch", $"({string.Join(", ", targets)})");
            case 0x46: return ("ldind.i1", "");
            case 0x47: return ("ldind.u1", "");
            case 0x48: return ("ldind.i2", "");
            case 0x49: return ("ldind.u2", "");
            case 0x4A: return ("ldind.i4", "");
            case 0x4B: return ("ldind.u4", "");
            case 0x4C: return ("ldind.i8", "");
            case 0x4D: return ("ldind.i", "");
            case 0x4E: return ("ldind.r4", "");
            case 0x4F: return ("ldind.r8", "");
            case 0x50: return ("ldind.ref", "");
            case 0x51: return ("stind.ref", "");
            case 0x52: return ("stind.i1", "");
            case 0x53: return ("stind.i2", "");
            case 0x54: return ("stind.i4", "");
            case 0x55: return ("stind.i8", "");
            case 0x56: return ("stind.r4", "");
            case 0x57: return ("stind.r8", "");
            case 0x58: return ("add", "");
            case 0x59: return ("sub", "");
            case 0x5A: return ("mul", "");
            case 0x5B: return ("div", "");
            case 0x5C: return ("div.un", "");
            case 0x5D: return ("rem", "");
            case 0x5E: return ("rem.un", "");
            case 0x5F: return ("and", "");
            case 0x60: return ("or", "");
            case 0x61: return ("xor", "");
            case 0x62: return ("shl", "");
            case 0x63: return ("shr", "");
            case 0x64: return ("shr.un", "");
            case 0x65: return ("neg", "");
            case 0x66: return ("not", "");
            case 0x67: return ("conv.i1", "");
            case 0x68: return ("conv.i2", "");
            case 0x69: return ("conv.i4", "");
            case 0x6A: return ("conv.i8", "");
            case 0x6B: return ("conv.r4", "");
            case 0x6C: return ("conv.r8", "");
            case 0x6D: return ("conv.u4", "");
            case 0x6E: return ("conv.u8", "");
            case 0x6F: return ("callvirt", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x70: return ("cpobj", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x71: return ("ldobj", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x72: return ("ldstr", ResolveStringToken(ReadI32(il, ref offset), reader));
            case 0x73: return ("newobj", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x74: return ("castclass", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x75: return ("isinst", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x76: return ("conv.r.un", "");
            case 0x79: return ("unbox", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x7A: return ("throw", "");
            case 0x7B: return ("ldfld", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x7C: return ("ldflda", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x7D: return ("stfld", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x7E: return ("ldsfld", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x7F: return ("ldsflda", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x80: return ("stsfld", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x81: return ("stobj", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x82: return ("conv.ovf.i1.un", "");
            case 0x83: return ("conv.ovf.i2.un", "");
            case 0x84: return ("conv.ovf.i4.un", "");
            case 0x85: return ("conv.ovf.i8.un", "");
            case 0x86: return ("conv.ovf.u1.un", "");
            case 0x87: return ("conv.ovf.u2.un", "");
            case 0x88: return ("conv.ovf.u4.un", "");
            case 0x89: return ("conv.ovf.u8.un", "");
            case 0x8A: return ("conv.ovf.i.un", "");
            case 0x8B: return ("conv.ovf.u.un", "");
            case 0x8C: return ("box", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x8D: return ("newarr", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x8E: return ("ldlen", "");
            case 0x8F: return ("ldelema", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x90: return ("ldelem.i1", "");
            case 0x91: return ("ldelem.u1", "");
            case 0x92: return ("ldelem.i2", "");
            case 0x93: return ("ldelem.u2", "");
            case 0x94: return ("ldelem.i4", "");
            case 0x95: return ("ldelem.u4", "");
            case 0x96: return ("ldelem.i8", "");
            case 0x97: return ("ldelem.i", "");
            case 0x98: return ("ldelem.r4", "");
            case 0x99: return ("ldelem.r8", "");
            case 0x9A: return ("ldelem.ref", "");
            case 0x9B: return ("stelem.i", "");
            case 0x9C: return ("stelem.i1", "");
            case 0x9D: return ("stelem.i2", "");
            case 0x9E: return ("stelem.i4", "");
            case 0x9F: return ("stelem.i8", "");
            case 0xA0: return ("stelem.r4", "");
            case 0xA1: return ("stelem.r8", "");
            case 0xA2: return ("stelem.ref", "");
            case 0xA3: return ("ldelem", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xA4: return ("stelem", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xA5: return ("unbox.any", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xB3: return ("conv.ovf.i1", "");
            case 0xB4: return ("conv.ovf.u1", "");
            case 0xB5: return ("conv.ovf.i2", "");
            case 0xB6: return ("conv.ovf.u2", "");
            case 0xB7: return ("conv.ovf.i4", "");
            case 0xB8: return ("conv.ovf.u4", "");
            case 0xB9: return ("conv.ovf.i8", "");
            case 0xBA: return ("conv.ovf.u8", "");
            case 0xC2: return ("refanyval", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xC3: return ("ckfinite", "");
            case 0xC6: return ("mkrefany", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xD0: return ("ldtoken", ResolveToken(ReadI32(il, ref offset), reader));
            case 0xD1: return ("conv.u2", "");
            case 0xD2: return ("conv.u1", "");
            case 0xD3: return ("conv.i", "");
            case 0xD4: return ("conv.ovf.i", "");
            case 0xD5: return ("conv.ovf.u", "");
            case 0xD6: return ("add.ovf", "");
            case 0xD7: return ("add.ovf.un", "");
            case 0xD8: return ("mul.ovf", "");
            case 0xD9: return ("mul.ovf.un", "");
            case 0xDA: return ("sub.ovf", "");
            case 0xDB: return ("sub.ovf.un", "");
            case 0xDC: return ("endfinally", "");
            case 0xDD: return ("leave", FormatBrTarget(offset + 4 + ReadI32(il, ref offset)));
            case 0xDE: return ("leave.s", FormatBrTarget(offset + ReadI8(il, ref offset)));
            case 0xDF: return ("stind.i", "");
            case 0xE0: return ("conv.u", "");
            default: return ($"<unknown 0x{op:X2}>", "");
        }
    }

    private static (string mnemonic, string operand) DecodeTwoByte(
        byte op2, byte[] il, ref int offset, MetadataReader reader)
    {
        switch (op2)
        {
            case 0x00: return ("arglist", "");
            case 0x01: return ("ceq", "");
            case 0x02: return ("cgt", "");
            case 0x03: return ("cgt.un", "");
            case 0x04: return ("clt", "");
            case 0x05: return ("clt.un", "");
            case 0x06: return ("ldftn", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x07: return ("ldvirtftn", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x09: return ("ldarg", ReadU16(il, ref offset).ToString());
            case 0x0A: return ("ldarga", ReadU16(il, ref offset).ToString());
            case 0x0B: return ("starg", ReadU16(il, ref offset).ToString());
            case 0x0C: return ("ldloc", ReadU16(il, ref offset).ToString());
            case 0x0D: return ("ldloca", ReadU16(il, ref offset).ToString());
            case 0x0E: return ("stloc", ReadU16(il, ref offset).ToString());
            case 0x0F: return ("localloc", "");
            case 0x11: return ("endfilter", "");
            case 0x12: return ("unaligned.", ReadU8(il, ref offset).ToString());
            case 0x13: return ("volatile.", "");
            case 0x14: return ("tail.", "");
            case 0x15: return ("initobj", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x16: return ("constrained.", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x17: return ("cpblk", "");
            case 0x18: return ("initblk", "");
            case 0x1A: return ("rethrow", "");
            case 0x1C: return ("sizeof", ResolveToken(ReadI32(il, ref offset), reader));
            case 0x1D: return ("refanytype", "");
            case 0x1E: return ("readonly.", "");
            default: return ($"<unknown 0xFE{op2:X2}>", "");
        }
    }

    private static string ResolveToken(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => reader.GetString(
                    reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                HandleKind.MemberReference => reader.GetString(
                    reader.GetMemberReference((MemberReferenceHandle)handle).Name),
                HandleKind.TypeDefinition => reader.GetString(
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                HandleKind.TypeReference => reader.GetString(
                    reader.GetTypeReference((TypeReferenceHandle)handle).Name),
                HandleKind.FieldDefinition => reader.GetString(
                    reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
                HandleKind.StandaloneSignature => $"sig(0x{token:X8})",
                _ => $"0x{token:X8}"
            };
        }
        catch { return $"0x{token:X8}"; }
    }

    private static string ResolveStringToken(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.UserStringHandle(token & 0x00FFFFFF);
            var value = reader.GetUserString(handle);
            if (value.Length > 60) value = value[..60] + "...";
            return $"\"{value}\"";
        }
        catch { return $"0x{token:X8}"; }
    }

    private static string FormatBrTarget(int target) => $"IL_{target:X4}";

    private static byte ReadU8(byte[] il, ref int offset) => il[offset++];
    private static sbyte ReadI8(byte[] il, ref int offset) => (sbyte)il[offset++];
    private static ushort ReadU16(byte[] il, ref int offset)
    {
        var v = BitConverter.ToUInt16(il, offset); offset += 2; return v;
    }
    private static int ReadI32(byte[] il, ref int offset)
    {
        var v = BitConverter.ToInt32(il, offset); offset += 4; return v;
    }
    private static uint ReadU32(byte[] il, ref int offset)
    {
        var v = BitConverter.ToUInt32(il, offset); offset += 4; return v;
    }
    private static long ReadI64(byte[] il, ref int offset)
    {
        var v = BitConverter.ToInt64(il, offset); offset += 8; return v;
    }
    private static float ReadF32(byte[] il, ref int offset)
    {
        var v = BitConverter.ToSingle(il, offset); offset += 4; return v;
    }
    private static double ReadF64(byte[] il, ref int offset)
    {
        var v = BitConverter.ToDouble(il, offset); offset += 8; return v;
    }

    private static void ExecuteILInstruction(ILInstruction instr, ILDebugState state)
    {
        try
        {
            switch (instr.Mnemonic)
            {
                case "nop":
                case "break":
                    break;

                case "ldc.i4.m1": ILPush(state, "int32", "-1"); break;
                case "ldc.i4.0": ILPush(state, "int32", "0"); break;
                case "ldc.i4.1": ILPush(state, "int32", "1"); break;
                case "ldc.i4.2": ILPush(state, "int32", "2"); break;
                case "ldc.i4.3": ILPush(state, "int32", "3"); break;
                case "ldc.i4.4": ILPush(state, "int32", "4"); break;
                case "ldc.i4.5": ILPush(state, "int32", "5"); break;
                case "ldc.i4.6": ILPush(state, "int32", "6"); break;
                case "ldc.i4.7": ILPush(state, "int32", "7"); break;
                case "ldc.i4.8": ILPush(state, "int32", "8"); break;
                case "ldc.i4.s":
                case "ldc.i4":
                    ILPush(state, "int32", instr.Operand); break;
                case "ldc.i8":
                    ILPush(state, "int64", instr.Operand); break;
                case "ldc.r4":
                    ILPush(state, "float32", instr.Operand); break;
                case "ldc.r8":
                    ILPush(state, "float64", instr.Operand); break;

                case "ldnull":
                    ILPush(state, "object", "null"); break;

                case "ldloc.0": ILLoadLocal(state, 0); break;
                case "ldloc.1": ILLoadLocal(state, 1); break;
                case "ldloc.2": ILLoadLocal(state, 2); break;
                case "ldloc.3": ILLoadLocal(state, 3); break;
                case "ldloc.s":
                case "ldloc":
                    if (int.TryParse(instr.Operand, out var llIdx))
                        ILLoadLocal(state, llIdx);
                    break;

                case "stloc.0": ILStoreLocal(state, 0); break;
                case "stloc.1": ILStoreLocal(state, 1); break;
                case "stloc.2": ILStoreLocal(state, 2); break;
                case "stloc.3": ILStoreLocal(state, 3); break;
                case "stloc.s":
                case "stloc":
                    if (int.TryParse(instr.Operand, out var slIdx))
                        ILStoreLocal(state, slIdx);
                    break;

                case "ldarg.0": ILPush(state, "object", "arg[0]"); break;
                case "ldarg.1": ILPush(state, "object", "arg[1]"); break;
                case "ldarg.2": ILPush(state, "object", "arg[2]"); break;
                case "ldarg.3": ILPush(state, "object", "arg[3]"); break;
                case "ldarg.s":
                case "ldarg":
                    ILPush(state, "object", $"arg[{instr.Operand}]"); break;

                case "dup":
                    if (state.Stack.Count > 0)
                    {
                        var top = state.Stack[^1];
                        ILPush(state, top.Type, top.Value);
                    }
                    break;

                case "pop":
                    if (state.Stack.Count > 0) ILPop(state);
                    break;

                case "add": ILBinaryOp(state, (a, b) => (a + b).ToString()); break;
                case "sub": ILBinaryOp(state, (a, b) => (a - b).ToString()); break;
                case "mul": ILBinaryOp(state, (a, b) => (a * b).ToString()); break;
                case "div":
                    ILBinaryOp(state, (a, b) => b == 0 ? "trap:div_by_zero" : (a / b).ToString());
                    break;
                case "rem":
                    ILBinaryOp(state, (a, b) => b == 0 ? "trap:div_by_zero" : (a % b).ToString());
                    break;
                case "and": ILBinaryOp(state, (a, b) => (a & b).ToString()); break;
                case "or": ILBinaryOp(state, (a, b) => (a | b).ToString()); break;
                case "xor": ILBinaryOp(state, (a, b) => (a ^ b).ToString()); break;
                case "shl": ILBinaryOp(state, (a, b) => (a << (int)(b & 31)).ToString()); break;
                case "shr": ILBinaryOp(state, (a, b) => (a >> (int)(b & 31)).ToString()); break;
                case "neg":
                    if (state.Stack.Count > 0)
                    {
                        var v = ILPop(state);
                        if (long.TryParse(v.Value, out var nv))
                            ILPush(state, v.Type, (-nv).ToString());
                        else
                            ILPush(state, v.Type, $"-({v.Value})");
                    }
                    break;
                case "not":
                    if (state.Stack.Count > 0)
                    {
                        var v = ILPop(state);
                        if (long.TryParse(v.Value, out var notv))
                            ILPush(state, v.Type, (~notv).ToString());
                        else
                            ILPush(state, v.Type, $"~({v.Value})");
                    }
                    break;

                case "ceq": ILCompareOp(state, (a, b) => a == b); break;
                case "cgt": ILCompareOp(state, (a, b) => a > b); break;
                case "clt": ILCompareOp(state, (a, b) => a < b); break;

                case "conv.i1": ILConvert(state, "int8"); break;
                case "conv.i2": ILConvert(state, "int16"); break;
                case "conv.i4": ILConvert(state, "int32"); break;
                case "conv.i8": ILConvert(state, "int64"); break;
                case "conv.r4": ILConvert(state, "float32"); break;
                case "conv.r8": ILConvert(state, "float64"); break;
                case "conv.u1": ILConvert(state, "uint8"); break;
                case "conv.u2": ILConvert(state, "uint16"); break;
                case "conv.u4": ILConvert(state, "uint32"); break;
                case "conv.u8": ILConvert(state, "uint64"); break;

                case "ldstr":
                    ILPush(state, "string", instr.Operand); break;

                case "ret":
                    if (state.Stack.Count > 0)
                    {
                        var rv = ILPop(state);
                        state.Output.Add($"[return: {rv.Type} = {rv.Value}]");
                    }
                    else
                    {
                        state.Output.Add("[return: void]");
                    }
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

    private static void ILPush(ILDebugState state, string type, string value)
    {
        state.Stack.Add(new ILStackValue
        {
            Index = state.Stack.Count,
            Type = type,
            Value = value
        });
    }

    private static ILStackValue ILPop(ILDebugState state)
    {
        var val = state.Stack[^1];
        state.Stack.RemoveAt(state.Stack.Count - 1);
        return val;
    }

    private static void ILLoadLocal(ILDebugState state, int index)
    {
        if (index < state.Locals.Count)
            ILPush(state, state.Locals[index].Type, state.Locals[index].Value);
    }

    private static void ILStoreLocal(ILDebugState state, int index)
    {
        if (state.Stack.Count > 0 && index < state.Locals.Count)
        {
            var val = ILPop(state);
            state.Locals[index].Value = val.Value;
        }
    }

    private static void ILBinaryOp(ILDebugState state, Func<long, long, string> op)
    {
        if (state.Stack.Count < 2) return;
        var b = ILPop(state);
        var a = ILPop(state);
        if (long.TryParse(a.Value, out var va) && long.TryParse(b.Value, out var vb))
            ILPush(state, a.Type, op(va, vb));
        else
            ILPush(state, a.Type, $"({a.Value} op {b.Value})");
    }

    private static void ILCompareOp(ILDebugState state, Func<long, long, bool> pred)
    {
        if (state.Stack.Count < 2) return;
        var b = ILPop(state);
        var a = ILPop(state);
        if (long.TryParse(a.Value, out var va) && long.TryParse(b.Value, out var vb))
            ILPush(state, "int32", pred(va, vb) ? "1" : "0");
        else
            ILPush(state, "int32", "?");
    }

    private static void ILConvert(ILDebugState state, string targetType)
    {
        if (state.Stack.Count > 0)
        {
            var v = ILPop(state);
            ILPush(state, targetType, v.Value);
        }
    }

    private static string DefaultValueForType(string typeName) => typeName switch
    {
        "bool" => "false",
        "float" or "double" => "0.0",
        "string" => "null",
        "object" => "null",
        _ => "0"
    };

    #endregion

    public void Dispose()
    {
        lock (_gate)
        {
            _peFile?.Dispose();
            _peFile = null;
            _decompiler = null;
            _loadedPath = null;
            _methods.Clear();
            _debugState = null;
        }
    }

    private static string DisassembleModule(PEFile peFile)
    {
        using var writer = new StringWriter();
        var output = new PlainTextOutput(writer);
        var disassembler = new ReflectionDisassembler(output, CancellationToken.None);
        disassembler.WriteModuleContents(peFile);
        return writer.ToString();
    }
}
