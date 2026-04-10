namespace LibreCode.Features.Autocomplete;

/// <summary>
/// Provides static keyword dictionaries per language for the editor autocomplete popup.
/// </summary>
public static class KeywordProvider
{
    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] =
        [
            "False", "None", "True", "and", "as", "assert", "async", "await",
            "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is",
            "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
            "while", "with", "yield",
            "print", "range", "len", "str", "int", "float", "list", "dict",
            "tuple", "set", "bool", "input", "open", "type", "isinstance",
            "enumerate", "zip", "map", "filter", "sorted", "reversed",
            "super", "property", "staticmethod", "classmethod", "abstractmethod",
            "self", "__init__", "__str__", "__repr__", "__name__", "__main__"
        ],
        ["csharp"] =
        [
            "abstract", "as", "async", "await", "base", "bool", "break", "byte",
            "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum",
            "event", "explicit", "extern", "false", "finally", "fixed", "float",
            "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object",
            "operator", "out", "override", "params", "partial", "private", "protected",
            "public", "readonly", "record", "ref", "required", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
            "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
            "volatile", "when", "where", "while", "yield",
            "Console", "WriteLine", "ReadLine", "ToString", "GetType", "Equals",
            "Task", "List", "Dictionary", "IEnumerable", "StringBuilder",
            "Exception", "ArgumentException", "InvalidOperationException",
            "nameof", "global", "init", "get", "set", "value", "file"
        ],
        ["javascript"] =
        [
            "async", "await", "break", "case", "catch", "class", "const", "continue",
            "debugger", "default", "delete", "do", "else", "export", "extends",
            "false", "finally", "for", "from", "function", "if", "import", "in",
            "instanceof", "let", "new", "null", "of", "return", "static", "super",
            "switch", "this", "throw", "true", "try", "typeof", "undefined", "var",
            "void", "while", "with", "yield",
            "console", "document", "window", "require", "module", "exports",
            "Promise", "Array", "Object", "String", "Number", "Boolean", "Map",
            "Set", "JSON", "Math", "Date", "RegExp", "Error", "setTimeout",
            "setInterval", "clearTimeout", "clearInterval", "fetch",
            "addEventListener", "removeEventListener", "querySelector",
            "getElementById", "createElement", "appendChild", "innerHTML",
            "prototype", "constructor", "length", "push", "pop", "shift",
            "forEach", "filter", "reduce", "includes", "indexOf", "splice"
        ],
        ["typescript"] =
        [
            "abstract", "any", "as", "async", "await", "bigint", "boolean", "break",
            "case", "catch", "class", "const", "constructor", "continue", "debugger",
            "declare", "default", "delete", "do", "else", "enum", "export", "extends",
            "false", "finally", "for", "from", "function", "get", "if", "implements",
            "import", "in", "infer", "instanceof", "interface", "is", "keyof", "let",
            "module", "namespace", "never", "new", "null", "number", "object", "of",
            "override", "package", "private", "protected", "public", "readonly",
            "return", "satisfies", "set", "static", "string", "super", "switch",
            "symbol", "this", "throw", "true", "try", "type", "typeof", "undefined",
            "unique", "unknown", "var", "void", "while", "with", "yield",
            "console", "Promise", "Array", "Record", "Partial", "Required",
            "Readonly", "Pick", "Omit", "Exclude", "Extract", "ReturnType"
        ],
        ["go"] =
        [
            "break", "case", "chan", "const", "continue", "default", "defer", "else",
            "fallthrough", "for", "func", "go", "goto", "if", "import", "interface",
            "map", "package", "range", "return", "select", "struct", "switch", "type",
            "var", "append", "cap", "close", "complex", "copy", "delete", "imag",
            "len", "make", "new", "panic", "print", "println", "real", "recover",
            "bool", "byte", "complex64", "complex128", "error", "float32", "float64",
            "int", "int8", "int16", "int32", "int64", "rune", "string", "uint",
            "uint8", "uint16", "uint32", "uint64", "uintptr", "true", "false", "nil",
            "fmt", "Println", "Printf", "Sprintf", "Errorf"
        ],
        ["rust"] =
        [
            "as", "async", "await", "break", "const", "continue", "crate", "dyn",
            "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in",
            "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return",
            "self", "Self", "static", "struct", "super", "trait", "true", "type",
            "union", "unsafe", "use", "where", "while", "yield",
            "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64",
            "u128", "usize", "f32", "f64", "bool", "char", "str", "String",
            "Vec", "Box", "Rc", "Arc", "Option", "Result", "Some", "None", "Ok",
            "Err", "HashMap", "HashSet", "BTreeMap", "println", "eprintln",
            "format", "panic", "unwrap", "expect", "clone", "iter", "into_iter",
            "collect", "map", "filter", "fold"
        ],
        ["java"] =
        [
            "abstract", "assert", "boolean", "break", "byte", "case", "catch",
            "char", "class", "const", "continue", "default", "do", "double", "else",
            "enum", "extends", "final", "finally", "float", "for", "goto", "if",
            "implements", "import", "instanceof", "int", "interface", "long",
            "native", "new", "null", "package", "private", "protected", "public",
            "record", "return", "sealed", "short", "static", "strictfp", "super",
            "switch", "synchronized", "this", "throw", "throws", "transient",
            "true", "false", "try", "var", "void", "volatile", "while", "yield",
            "System", "String", "Integer", "Double", "Boolean", "ArrayList",
            "HashMap", "List", "Map", "Set", "Optional", "Stream",
            "println", "printf", "equals", "toString", "hashCode", "compareTo"
        ],
        ["cpp"] =
        [
            "alignas", "alignof", "and", "asm", "auto", "bool", "break", "case",
            "catch", "char", "class", "concept", "const", "consteval", "constexpr",
            "constinit", "continue", "co_await", "co_return", "co_yield", "decltype",
            "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
            "explicit", "export", "extern", "false", "float", "for", "friend",
            "goto", "if", "inline", "int", "long", "mutable", "namespace", "new",
            "noexcept", "not", "nullptr", "operator", "or", "private", "protected",
            "public", "register", "reinterpret_cast", "requires", "return", "short",
            "signed", "sizeof", "static", "static_assert", "static_cast", "struct",
            "switch", "template", "this", "throw", "true", "try", "typedef",
            "typeid", "typename", "union", "unsigned", "using", "virtual", "void",
            "volatile", "while",
            "std", "cout", "cin", "endl", "string", "vector", "map", "set",
            "unique_ptr", "shared_ptr", "make_unique", "make_shared",
            "size_t", "uint8_t", "int32_t", "int64_t", "printf", "scanf",
            "include", "define", "ifdef", "ifndef", "endif", "pragma"
        ],
        ["c"] =
        [
            "auto", "break", "case", "char", "const", "continue", "default", "do",
            "double", "else", "enum", "extern", "float", "for", "goto", "if",
            "inline", "int", "long", "register", "restrict", "return", "short",
            "signed", "sizeof", "static", "struct", "switch", "typedef", "union",
            "unsigned", "void", "volatile", "while",
            "NULL", "true", "false", "size_t", "uint8_t", "int32_t", "int64_t",
            "printf", "scanf", "malloc", "calloc", "realloc", "free",
            "strlen", "strcpy", "strcat", "strcmp", "memcpy", "memset",
            "fopen", "fclose", "fread", "fwrite", "fprintf", "fscanf",
            "include", "define", "ifdef", "ifndef", "endif", "pragma"
        ],
        ["html"] =
        [
            "html", "head", "body", "div", "span", "section", "article", "header",
            "footer", "nav", "main", "aside", "form", "input", "button", "select",
            "option", "textarea", "label", "table", "thead", "tbody", "tr", "th",
            "td", "ul", "ol", "li", "a", "img", "video", "audio", "canvas",
            "script", "style", "link", "meta", "title", "class", "id", "href",
            "src", "alt", "type", "value", "name", "placeholder", "required",
            "disabled", "checked", "readonly", "hidden", "width", "height"
        ],
        ["css"] =
        [
            "display", "position", "top", "right", "bottom", "left", "float",
            "clear", "margin", "padding", "border", "width", "height", "max-width",
            "min-width", "max-height", "min-height", "overflow", "z-index",
            "background", "color", "font-size", "font-weight", "font-family",
            "text-align", "text-decoration", "line-height", "letter-spacing",
            "flex", "flex-direction", "justify-content", "align-items", "gap",
            "grid", "grid-template-columns", "grid-template-rows",
            "transition", "transform", "animation", "opacity", "visibility",
            "cursor", "pointer-events", "box-shadow", "border-radius",
            "none", "block", "inline", "inline-block", "flex", "grid",
            "absolute", "relative", "fixed", "sticky", "static",
            "inherit", "initial", "unset", "auto", "important"
        ],
        ["sql"] =
        [
            "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET",
            "DELETE", "CREATE", "TABLE", "ALTER", "DROP", "INDEX", "VIEW",
            "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "ON",
            "GROUP", "BY", "ORDER", "ASC", "DESC", "HAVING", "LIMIT", "OFFSET",
            "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "IS", "NULL",
            "AS", "DISTINCT", "COUNT", "SUM", "AVG", "MIN", "MAX",
            "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT",
            "INT", "VARCHAR", "TEXT", "BOOLEAN", "DATE", "TIMESTAMP",
            "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "CASCADE"
        ],
        ["shell"] =
        [
            "if", "then", "else", "elif", "fi", "for", "while", "do", "done",
            "case", "esac", "function", "return", "exit", "break", "continue",
            "echo", "printf", "read", "export", "source", "alias", "unalias",
            "cd", "ls", "pwd", "mkdir", "rmdir", "rm", "cp", "mv", "cat",
            "grep", "find", "sed", "awk", "sort", "uniq", "wc", "head", "tail",
            "chmod", "chown", "curl", "wget", "tar", "zip", "unzip",
            "git", "docker", "npm", "pip", "sudo", "apt", "yum", "brew"
        ]
    };

    /// <summary>
    /// Returns keywords matching the given prefix for the specified language.
    /// </summary>
    public static IReadOnlyList<string> GetCompletions(string language, string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || prefix.Length < 2)
            return [];

        if (!Keywords.TryGetValue(language, out var words))
            return [];

        var results = new List<string>();
        foreach (var word in words)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !word.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(word);
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }
}
