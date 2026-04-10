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
/// decompilation, IL disassembly, PE inspection, string search, and hex read APIs.
/// </summary>
public sealed class AssemblyAnalysisService : IDisposable
{
    private PEFile? _peFile;
    private CSharpDecompiler? _decompiler;
    private string? _loadedPath;
    private readonly Lock _gate = new();

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

    public void Dispose()
    {
        lock (_gate)
        {
            _peFile?.Dispose();
            _peFile = null;
            _decompiler = null;
            _loadedPath = null;
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
