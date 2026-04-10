using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using LibreCode.Features.Autocomplete;
using LibreCode.ViewModels;

namespace LibreCode.Views;

/// <summary>
/// Code editor panel using AvaloniaEdit with TextMate syntax highlighting,
/// keyword autocomplete via CompletionWindow, and smart auto-indentation.
/// </summary>
public partial class EditorView : UserControl
{
    private RegistryOptions? _registryOptions;
    private TextMate.Installation? _textMateInstallation;
    private CompletionWindow? _completionWindow;
    private bool _suppressTextChange;

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SetupTextMate();
        SetupCompletion();
        SetupIndentation();
    }

    private void SetupTextMate()
    {
        _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);
        Editor.TextChanged += OnEditorTextChanged;
    }

    private void SetupCompletion()
    {
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.TextEntering += OnTextEntering;
    }

    private void SetupIndentation()
    {
        Editor.TextArea.IndentationStrategy = new SmartIndentStrategy();
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        Vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveTab))
                LoadActiveTab();
        };
        LoadActiveTab();
    }

    private void LoadActiveTab()
    {
        var hasTab = Vm?.ActiveTab is not null;
        Editor.IsVisible = hasTab;
        WelcomePanel.IsVisible = !hasTab;

        if (!hasTab || _registryOptions is null || _textMateInstallation is null)
            return;

        _suppressTextChange = true;
        Editor.Text = Vm!.ActiveTab!.Content;
        _suppressTextChange = false;

        var ext = Path.GetExtension(Vm.ActiveTab.Path);
        try
        {
            var lang = _registryOptions.GetLanguageByExtension(ext);
            if (lang is not null)
            {
                var scope = _registryOptions.GetScopeByLanguageId(lang.Id);
                _textMateInstallation.SetGrammar(scope);
            }
        }
        catch
        {
            _textMateInstallation.SetGrammar(null);
        }

        Vm.CursorLine = 1;
        Vm.CursorCol = 1;
    }

    #region Autocomplete

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (Vm?.ActiveTab is null || string.IsNullOrEmpty(e.Text)) return;
        if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_') return;

        var prefix = GetCurrentWordPrefix();
        if (prefix.Length < 2) return;

        var language = Vm.ActiveTab.Language;
        var completions = KeywordProvider.GetCompletions(language, prefix);
        if (completions.Count == 0)
        {
            _completionWindow?.Close();
            return;
        }

        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            MinWidth = 180
        };
        _completionWindow.StartOffset -= prefix.Length;

        var data = _completionWindow.CompletionList.CompletionData;
        foreach (var item in completions)
            data.Add(new CompletionData(item));

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow is null || string.IsNullOrEmpty(e.Text)) return;

        if (e.Text.Length > 0 && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
            _completionWindow.CompletionList.RequestInsertion(e);
    }

    private string GetCurrentWordPrefix()
    {
        var doc = Editor.Document;
        var offset = Editor.TextArea.Caret.Offset;
        var start = offset;

        while (start > 0)
        {
            var c = doc.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(c) || c == '_')
                start--;
            else
                break;
        }

        return start < offset ? doc.GetText(start, offset - start) : string.Empty;
    }

    #endregion

    #region Text change sync

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChange || Vm?.ActiveTab is null) return;

        var content = Editor.Text;
        Vm.ActiveTab.Content = content;
        Vm.ActiveTab.IsDirty = content != Vm.ActiveTab.OriginalContent;
        Vm.CurrentContent = content;

        var caret = Editor.TextArea.Caret;
        Vm.CursorLine = caret.Line;
        Vm.CursorCol = caret.Column;
    }

    #endregion

    #region Tab handling

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: EditorTab tab } && Vm is not null)
        {
            Vm.ActiveTab = tab;
            Vm.CurrentContent = tab.Content;
        }
    }

    private void OnTabClosePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { DataContext: EditorTab tab } && Vm is not null)
        {
            e.Handled = true;
            Vm.CloseTab(tab);
        }
    }

    #endregion
}

/// <summary>
/// Smart indentation strategy: copies previous line whitespace and adds
/// an extra indent level after block openers ({, :, (, [).
/// </summary>
internal sealed class SmartIndentStrategy : IIndentationStrategy
{
    private const string Indent = "    ";

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (line.LineNumber <= 1) return;

        var prevLine = document.GetLineByNumber(line.LineNumber - 1);
        var prevText = document.GetText(prevLine.Offset, prevLine.Length);

        var leadingWhitespace = GetLeadingWhitespace(prevText);
        var trimmed = prevText.TrimEnd();

        if (trimmed.Length > 0 && IsBlockOpener(trimmed[^1]))
            leadingWhitespace += Indent;

        var currentIndent = document.GetText(line.Offset, line.Length);
        if (string.IsNullOrWhiteSpace(currentIndent))
            document.Replace(line.Offset, line.Length, leadingWhitespace);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine) { }

    private static string GetLeadingWhitespace(string text)
    {
        var i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            i++;
        return text[..i];
    }

    private static bool IsBlockOpener(char c) => c is '{' or ':' or '(' or '[';
}
