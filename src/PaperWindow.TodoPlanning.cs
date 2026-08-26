using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private void EditTodoPlanning(PaperItem item)
    {
        if (TodoRules.IsPlaceholder(item) ||
            !TodoPlanningDialog.TryShow(
                this,
                item.PlannedStartDate,
                item.DueDate,
                _controller.State.EnableAnimations,
                out var plannedStartDate,
                out var dueDate) ||
            !PaperItem.IsPlanningRangeValid(plannedStartDate, dueDate) ||
            item.PlannedStartDate == plannedStartDate &&
            item.DueDate == dueDate)
        {
            return;
        }

        PushUndoSnapshot();
        var result = item.SetPlanningDates(plannedStartDate, dueDate);
        if (result != TodoPlanningUpdateResult.Updated)
        {
            return;
        }

        _controller.MarkDirty();
        ReconcileTodoRows([item.Id], item.Id);
    }

    private static string FormatTodoPlanningDate(DateOnly value) =>
        value.ToString("d", UiLanguages.EffectiveCulture);

    private Border BuildTodoPlanningIndicator(
        PaperItem item,
        TodoVisualMetrics metrics)
    {
        var glyph = new TextBlock
        {
            Text = "▦",
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
            ToolTip = TodoPlanningToolTip(item)
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
            EditTodoPlanning(item);
            e.Handled = true;
        };
        return host;
    }

    private static string TodoPlanningToolTip(PaperItem item)
    {
        var start = item.PlannedStartDate.HasValue
            ? FormatTodoPlanningDate(item.PlannedStartDate.Value)
            : "—";
        var due = item.DueDate.HasValue
            ? FormatTodoPlanningDate(item.DueDate.Value)
            : "—";
        return Strings.Format("TodoPlanningRangeToolTip", start, due);
    }
}
