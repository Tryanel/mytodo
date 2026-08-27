using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private bool _exportMarkdownInProgress;
    private CancellationTokenSource? _markdownExportCancellation;

    private async void ExportPaperAsMarkdown()
    {
        if (_exportMarkdownInProgress)
        {
            return;
        }
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

        await ExportPaperAsMarkdownToPathAsync(dialog.FileName, title);
    }

    internal async Task ExportPaperAsMarkdownToPathAsync(
        string fileName,
        string title)
    {
        if (_exportMarkdownInProgress ||
            _windowLifecycle != PaperWindowLifecycleState.Alive)
        {
            return;
        }

        _exportMarkdownInProgress = true;
        var cancellation = new CancellationTokenSource();
        _markdownExportCancellation = cancellation;
        try
        {
            string markdown;
            if (_paper.Type == PaperTypes.Note && !IsCurrentBodyProviderMarkdown)
            {
                try
                {
                    markdown = await _paperBodyHost.GetFullMarkdownAsync(
                        cancellation.Token);
                }
                catch (OperationCanceledException)
                    when (!CanCompleteMarkdownExport(cancellation))
                {
                    return;
                }
                catch
                {
                    if (!CanCompleteMarkdownExport(cancellation))
                    {
                        return;
                    }
                    throw new InvalidOperationException(
                        Strings.Get("ExportMarkdownPluginFailed"));
                }
            }
            else
            {
                markdown = _paper.Type == PaperTypes.Board
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
            }

            if (!CanCompleteMarkdownExport(cancellation))
            {
                return;
            }
            File.WriteAllText(
                fileName,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!CanCompleteMarkdownExport(cancellation))
            {
                return;
            }
            PaperNoticeDialog.Show(
                this,
                Strings.Get("ExportMarkdownSuccessTitle"),
                Strings.Format("ExportMarkdownSuccess", fileName));
        }
        catch (OperationCanceledException)
            when (!CanCompleteMarkdownExport(cancellation))
        {
            // Closing the owning paper invalidates the live plugin session and its export.
        }
        catch (Exception ex)
        {
            if (!CanCompleteMarkdownExport(cancellation))
            {
                return;
            }
            PaperNoticeDialog.Show(
                this,
                Strings.Get("ExportMarkdownFailedTitle"),
                Strings.Format("ExportMarkdownFailed", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_markdownExportCancellation, cancellation))
            {
                _markdownExportCancellation = null;
            }
            cancellation.Dispose();
            _exportMarkdownInProgress = false;
        }
    }

    private bool CanCompleteMarkdownExport(CancellationTokenSource cancellation) =>
        ReferenceEquals(_markdownExportCancellation, cancellation) &&
        !cancellation.IsCancellationRequested &&
        _windowLifecycle == PaperWindowLifecycleState.Alive;

    private void CancelMarkdownExportForWindowClose()
    {
        _markdownExportCancellation?.Cancel();
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
