using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private void ExportPaperAsMarkdown()
    {
        if (_paper.Type == PaperTypes.Todo)
        {
            CommitFocusedTextIfNeeded();
        }

        var title = _controller.PaperDisplayTitle(_paper);
        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("ExportMarkdownDialogTitle"),
            Filter = Strings.Get("ExportMarkdownFilter"),
            DefaultExt = ".md",
            AddExtension = true,
            FileName = SafeMarkdownFileName(title)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var markdown = _paper.Type == PaperTypes.Board
                ? PaperMarkdownExporter.BuildBoard(
                    title,
                    _controller.State.Papers
                        .Where(paper => paper.Type == PaperTypes.Todo)
                        .Select(paper => (
                            Paper: paper,
                            Title: _controller.PaperDisplayTitle(paper))))
                : PaperMarkdownExporter.Build(
                    _paper,
                    title,
                    _paper.Type == PaperTypes.Note
                        ? _noteBox?.PersistentText ?? _paper.Content
                        : null);
            File.WriteAllText(
                dialog.FileName,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            PaperNoticeDialog.Show(
                this,
                Strings.Get("ExportMarkdownSuccessTitle"),
                Strings.Format("ExportMarkdownSuccess", dialog.FileName));
        }
        catch (Exception ex)
        {
            PaperNoticeDialog.Show(
                this,
                Strings.Get("ExportMarkdownFailedTitle"),
                Strings.Format("ExportMarkdownFailed", ex.Message));
        }
    }

    private static string SafeMarkdownFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((title ?? "")
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "PaperTodo.md" : safe + ".md";
    }
}
