using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private TodoNoteDialog? _todoNoteEditor;

    private void EditTodoNote(PaperItem item)
    {
        if (_todoNoteEditor != null)
        {
            _todoNoteEditor.RequestTarget(TodoNoteTarget(item));
            return;
        }

        var editor = TodoNoteDialog.Create(
            this,
            TodoNoteTarget(item),
            SaveTodoNote);
        _todoNoteEditor = editor;
        editor.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_todoNoteEditor, editor))
            {
                return;
            }

            _todoNoteEditor = null;
            RefreshExperimentalFocusPresentation();
        };

        // Show modelessly so the master collapse control and every edge capsule remain enabled.
        // Register the editor first because Show() synchronously deactivates the owner paper.
        CancelExperimentalAutoCollapse();
        CancelStrictAutoCollapse();
        RefreshExperimentalFocusPresentation(animate: false);
        editor.ShowAndActivate();
    }

    private bool SaveTodoNote(string itemId, string note)
    {
        var item = _paper.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
        if (item == null)
        {
            return false;
        }
        if (string.Equals(item.Note, note, StringComparison.Ordinal))
        {
            return true;
        }

        PushUndoSnapshot();
        item.Note = note;
        TodoTaskLifecycle.MaterializeIfNeeded(
            item,
            DateTimeOffset.Now);
        _controller.MarkDirty();
        ReconcileTodoRows(
            new[] { item.Id },
            focusItemId: null);
        return true;
    }

    private bool HasOpenTodoNoteEditor() => _todoNoteEditor != null;

    private bool HasTodoNoteEditorForDifferentItem(string itemId) =>
        _todoNoteEditor != null &&
        !string.Equals(
            _todoNoteEditor.ItemId,
            itemId,
            StringComparison.Ordinal);

    internal string? TodoNoteEditorItemId => _todoNoteEditor?.ItemId;
    internal TodoNoteDialog? TodoNoteEditorWindow => _todoNoteEditor;

    private void CloseTodoNoteEditor()
    {
        var editor = _todoNoteEditor;
        _todoNoteEditor = null;
        editor?.ForceClose();
    }

    private void RefreshTodoNoteEditorTheme() =>
        _todoNoteEditor?.RefreshTheme();

    private void RefreshTodoNoteEditorTopmost(bool topmost) =>
        _todoNoteEditor?.RefreshTopmost(topmost);

    private static TodoNoteEditorTarget TodoNoteTarget(PaperItem item) =>
        new(item.Id, item.Text, item.Note);

    private Border BuildTodoNoteIndicator(
        PaperItem item,
        TodoVisualMetrics metrics)
    {
        var glyph = new TextBlock
        {
            Text = "▤",
            Foreground = WeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = Math.Max(
                AppTypography.Scale(11),
                metrics.TextFontSize - AppTypography.Scale(1)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.68
        };
        var host = new Border
        {
            Width = Math.Max(
                AppTypography.Scale(16),
                metrics.CheckColumnWidth - AppTypography.Scale(7)),
            MinHeight = metrics.RowMinHeight,
            CornerRadius = new CornerRadius(RadiusSmall),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = glyph,
            ToolTip = Strings.Format(
                "TodoNoteToolTip",
                CompactTodoNote(item.Note))
        };

        host.MouseEnter += (_, _) =>
        {
            host.Background = HoverBrush;
            glyph.Opacity = 1;
        };
        host.MouseLeave += (_, _) =>
        {
            host.Background = Brushes.Transparent;
            glyph.Opacity = 0.68;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            EditTodoNote(item);
            e.Handled = true;
        };
        return host;
    }

    private static string CompactTodoNote(string note)
    {
        var compact = string.Join(
            " ",
            (note ?? "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
        return compact.Length <= 120 ? compact : compact[..119] + "…";
    }

    private static string FormatTodoTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", UiLanguages.EffectiveCulture);

    internal void FocusTodoFromBoard(string itemId)
    {
        if (_paper.Type != PaperTypes.Todo)
        {
            return;
        }

        RebuildTodoRows(itemId, TodoFocusPlacement.End);
        Dispatcher.BeginInvoke(() => FocusTodoItem(
            itemId,
            TodoFocusPlacement.End));
    }
}
