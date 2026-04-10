# LibreCode

A free, open-source AI-powered code editor built from the ground up with .NET 10 and Avalonia UI. Runs natively on Windows, Linux, and macOS -- no Electron, no web views, just fast native rendering.

LibreCode pairs a full-featured code editor with a local AI assistant powered by [Ollama](https://ollama.com), a built-in terminal, a model marketplace, and a .NET reverse engineering toolkit inspired by dnSpy/ILSpy.

---

## Why LibreCode?

Most AI-powered editors are either closed-source, browser-based, or tied to cloud APIs. LibreCode is different:

- **100% local AI** -- your code never leaves your machine. Runs against any Ollama model you have installed.
- **Truly cross-platform** -- native Avalonia UI, not a web wrapper. Starts fast, stays fast.
- **Built for hackers** -- ships with a full .NET reversing toolkit (decompiler, IL viewer, hex editor, PE inspector) alongside a modern code editor.
- **No telemetry, no accounts, no subscriptions.**

---

## Features

### Code Editor

- Syntax highlighting for 25+ languages via TextMate grammars (Dark+ theme)
- Keyword autocomplete popup as you type (Python, C#, JS/TS, Go, Rust, Java, C/C++, HTML, CSS, SQL, Shell, and more)
- Smart auto-indentation -- copies previous line indent and adds a level after `{`, `:`, `(`, `[`
- Multi-tab editing with dirty-file indicators
- Line numbers, monospace font rendering, and a welcome screen when no files are open
- Ctrl+S to save

### File Explorer

- Open any project folder and browse the full directory tree
- Create new files and folders, rename, delete
- Right-click context menu with "Run in Terminal" option
- File watcher automatically refreshes the tree when files change on disk

### AI Assistant

Four interaction modes, all powered by your local Ollama instance:

- **Ask** -- get answers to coding questions with context from your codebase
- **Agent** -- autonomous coding agent that can read files, write files, search your project, and run shell commands in a loop until the task is done
- **Plan** -- have the AI plan out an implementation approach before writing code
- **Debug** -- paste errors and get debugging help

The AI automatically pulls in relevant code context using codebase embeddings (RAG). It indexes your project files, chunks them, generates embeddings via Ollama, and uses cosine similarity search to inject the most relevant snippets into every conversation.

Custom rules let you set persistent instructions that the AI always follows (e.g., "always use TypeScript", "prefer functional style").

### Built-in Terminal

- Integrated terminal panel (toggle with Ctrl+`)
- Auto-detects PowerShell (pwsh or powershell.exe) on Windows with PSReadLine tab completion
- Opens in your project directory by default
- Resizable with a drag splitter

### Model Marketplace

- Browse the full Ollama model library without leaving the editor
- Search models by name
- See model descriptions, parameter counts, variant tags, and estimated VRAM requirements
- GPU detection shows your available VRAM so you know what will fit

### .NET Reverse Engineering Toolkit

A full reversing workbench built right into the editor, powered by the same decompilation engine as ILSpy/dnSpy:

- **C# Decompiler** -- load any .dll or .exe and see the full decompiled C# source. Navigate a type tree (namespaces and classes) and click to decompile individual types.
- **IL Disassembler** -- view raw IL bytecode for the entire module or any specific type.
- **PE Inspector** -- examine PE headers (machine type, subsystem, image base, DLL characteristics, entry point), PE sections (.text, .rsrc, etc.), CLR metadata (runtime version, target framework), and all assembly references with versions and public key tokens.
- **String Search** -- scan the user strings heap for hardcoded strings, URLs, keys, and credentials. Filter results in real time.
- **Hex Viewer** -- page through raw file bytes with offset, hex, and ASCII columns. Jump to any offset instantly.
- **AI Analysis** -- send decompiled code to the AI with a reverse-engineering-focused prompt that analyzes for vulnerabilities, obfuscation patterns, anti-debugging techniques, and control flow.

### Session Persistence

LibreCode remembers your state between launches:

- Open tabs and active file
- Project folder
- Right panel selection
- Chat history

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+S | Save current file |
| Ctrl+` | Toggle terminal |
| Ctrl+Shift+P | Command palette |
| Tab / Enter | Accept autocomplete suggestion |
| Escape | Dismiss autocomplete |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.com) installed and running locally (for AI features)

### Build & Run

```bash
git clone https://github.com/your-username/LibreCode.git
cd LibreCode/LibreCode
dotnet run
```

### Install a Model

The AI features need at least one Ollama model. Open a terminal and run:

```bash
ollama pull llama3.2
```

Or browse models from the **Models** tab inside LibreCode.

### Configuration

Edit `appsettings.json` to customize:

```json
{
  "Ollama": {
    "BaseUrl": "http://127.0.0.1:11434",
    "DefaultModel": "llama3.2",
    "EmbeddingModel": "nomic-embed-text",
    "Temperature": 0.7,
    "MaxTokens": 4096,
    "AutocompleteEnabled": true,
    "AutocompleteDebounceMs": 300
  }
}
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | Avalonia UI 11.3 (cross-platform native) |
| Code Editor | AvaloniaEdit + TextMate grammars |
| Terminal | Iciclecreek.Avalonia.Terminal (XTerm.NET + Porta.Pty) |
| AI Backend | Ollama (local LLM inference) |
| Decompiler | ICSharpCode.Decompiler (ILSpy engine) |
| PE Analysis | System.Reflection.Metadata |
| Architecture | MVVM with CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| Target | .NET 10, Windows / Linux / macOS |

---

## Project Structure

```
LibreCode/
  Features/
    AI/              # AI message models and mode definitions
    Agent/           # Autonomous coding agent + tool implementations
      Tools/         # File read/write, search, shell execution
    Autocomplete/    # Editor keyword completion + data models
    Chat/            # Chat service with RAG context injection
    Context/         # Codebase indexer + embedding store
    InlineEdit/      # Inline code editing via AI
    Marketplace/     # Ollama model browser, GPU detection
    Reversing/       # Assembly analysis service + data models
  Services/
    FileSystem/      # Project-scoped file I/O with watcher
    Ollama/          # Ollama HTTP client + request/response models
    SessionPersistenceService.cs
    RulesService.cs
  ViewModels/
    MainViewModel.cs # Central application state
  Views/
    EditorView       # Code editor with tabs
    FileExplorerView # Project tree browser
    AiPanelView      # AI chat interface
    MarketplaceView  # Model browser
    SettingsView     # Rules and config
    TerminalView     # Integrated terminal
    ReversingView    # Reverse engineering panel
    Reversing/       # Decompile, IL, PE, Strings, Hex, AI sub-views
```

---

## Contributing

Contributions are welcome -- open an issue first to discuss what you'd like to change before submitting a PR.

By submitting a pull request, you agree to the [Contributor License Agreement](CLA.md). In short:

- You assign all rights in your contribution to the project owner.
- You confirm the work is original and doesn't infringe on third-party rights.
- The project owner is not obligated to accept any contribution.

Please read the full [CLA.md](CLA.md) before contributing.

---

## License

LibreCode is released under a **Custom License (Contribution-Only, No Resale, No Fork Distribution)**. See [LICENSE.md](LICENSE.md) for the full text.

The key points:

- **You can use it** -- for personal or commercial business purposes.
- **You cannot redistribute it** -- no copying, selling, sublicensing, or making it available to third parties.
- **You cannot fork and distribute** -- forks are only permitted for the purpose of contributing back to the original repo via pull requests.
- **You cannot offer it as a service** -- no SaaS, hosted, or competing offerings built on this software.
- **All contributions become the property of the original author.**

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.


# Showcase

<img width="1393" height="919" alt="Screenshot 2026-04-09 231703" src="https://github.com/user-attachments/assets/6c82c77b-e051-42dc-bb78-a1771c1dc490" />
<img width="1386" height="917" alt="Screenshot 2026-04-09 231648" src="https://github.com/user-attachments/assets/99ff2358-1b9f-4f43-b2e7-f17ffd237085" />
<img width="1392" height="919" alt="Screenshot 2026-04-09 231657" src="https://github.com/user-attachments/assets/8b99dfc6-d844-4b1a-b5e5-88cd73e60c69" />
<img width="1394" height="922" alt="Screenshot 2026-04-09 231638" src="https://github.com/user-attachments/assets/ebd970fe-4a2f-44c6-b509-056c49bff000" />

