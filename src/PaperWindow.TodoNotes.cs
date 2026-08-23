using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private void EditTodoNote(PaperItem item)
    {
        if (!TodoNoteDialog.TryEdit(this, item.Note, out var note) ||
            string.Equals(item.Note, note, StringComparison.Ordinal))
        {
            return;
        }

        PushUndoSnapshot();
        item.Note = note;
        _controller.MarkDirty();
        ReconcileTodoRows(
            new[] { item.Id },
            focusItemId: item.Id);
    }

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
