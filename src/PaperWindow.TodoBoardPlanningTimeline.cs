using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private Button? _todoBoardTimelineButton;
    private DateOnly _todoBoardTimelineAnchor = DateOnly.FromDateTime(DateTime.Today);

    private UIElement BuildTodoBoardPlanningTimeline(TodoBoardSnapshot snapshot)
    {
        var culture = UiLanguages.EffectiveCulture;
        var scale = TodoBoardTimelineScales.Normalize(_paper.BoardTimelineScale);
        var layout = TodoBoardPlanningTimelineLayout.Build(
            snapshot,
            _todoBoardTimelineAnchor,
            scale,
            culture.DateTimeFormat.FirstDayOfWeek);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(BuildTodoBoardTimelineNavigation(layout));

        var scheduled = BuildTodoBoardScheduledTimeline(layout);
        Grid.SetRow(scheduled, 1);
        root.Children.Add(scheduled);

        var unscheduled = BuildTodoBoardUnscheduledList(layout.UnscheduledEntries);
        Grid.SetRow(unscheduled, 2);
        root.Children.Add(unscheduled);
        return root;
    }

    private UIElement BuildTodoBoardTimelineNavigation(
        TodoBoardPlanningTimelineLayout layout)
    {
        var navigation = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previous = CreateTodoBoardIconButton(
            "‹",
            Strings.Get("TodoBoardTimelinePreviousWindow"),
            () => ChangeTodoBoardTimelineWindow(-1));
        var next = CreateTodoBoardIconButton(
            "›",
            Strings.Get("TodoBoardTimelineNextWindow"),
            () => ChangeTodoBoardTimelineWindow(1));
        var title = new TextBlock
        {
            Text = TodoBoardTimelineWindowTitle(layout),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(12.5),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(previous);
        left.Children.Add(next);
        left.Children.Add(title);

        var week = CreateTodoBoardTextButton(
            Strings.Get("TodoBoardTimelineWeek"),
            () => SetTodoBoardTimelineScale(TodoBoardTimelineScales.Week));
        var month = CreateTodoBoardTextButton(
            Strings.Get("TodoBoardTimelineMonth"),
            () => SetTodoBoardTimelineScale(TodoBoardTimelineScales.Month));
        week.Padding = new Thickness(8, 4, 8, 4);
        month.Padding = new Thickness(8, 4, 8, 4);
        month.Margin = new Thickness(3, 0, 0, 0);
        UpdateTodoBoardViewButton(
            week,
            layout.Scale == TodoBoardTimelineScales.Week);
        UpdateTodoBoardViewButton(
            month,
            layout.Scale == TodoBoardTimelineScales.Month);
        var today = CreateTodoBoardTextButton(
            Strings.Get("TodoBoardToday"),
            () =>
            {
                _todoBoardTimelineAnchor = DateOnly.FromDateTime(DateTime.Today);
                RefreshTodoBoardBody();
            });
        today.Margin = new Thickness(8, 0, 0, 0);
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        controls.Children.Add(week);
        controls.Children.Add(month);
        controls.Children.Add(today);
        Grid.SetColumn(controls, 2);

        navigation.Children.Add(left);
        navigation.Children.Add(controls);
        return navigation;
    }

    private UIElement BuildTodoBoardScheduledTimeline(
        TodoBoardPlanningTimelineLayout layout)
    {
        if (layout.ScheduledItems.Count == 0)
        {
            var empty = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24)
            };
            empty.Children.Add(new TextBlock
            {
                Text = Strings.Get("TodoBoardTimelineWindowEmpty"),
                Foreground = TextBrush,
                FontSize = AppTypography.Scale(12),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            empty.Children.Add(new TextBlock
            {
                Text = Strings.Get("TodoBoardTimelineWindowEmptyHint"),
                Foreground = WeakTextBrush,
                FontSize = AppTypography.Scale(10),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });
            return empty;
        }

        var timeline = new Grid
        {
            MinWidth = 180 + layout.DayCount *
                (layout.Scale == TodoBoardTimelineScales.Week ? 84 : 34),
            Background = PaperBrush
        };
        timeline.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(180)
        });
        for (var day = 0; day < layout.DayCount; day++)
        {
            timeline.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(
                    layout.Scale == TodoBoardTimelineScales.Week ? 84 : 34)
            });
        }
        timeline.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        timeline.Children.Add(new Border
        {
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Theme.Tint((byte)(Theme.IsDark ? 16 : 9)),
            Padding = new Thickness(10, 0, 8, 0),
            Child = new TextBlock
            {
                Text = Strings.Get("TodoBoardTimelineScheduled"),
                Foreground = WeakTextBrush,
                FontSize = AppTypography.Scale(9.6),
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        for (var day = 0; day < layout.DayCount; day++)
        {
            var date = layout.WindowStart.AddDays(day);
            var header = new Border
            {
                BorderBrush = PaperBorderBrush,
                BorderThickness = new Thickness(0, 1, 1, 1),
                Background = Theme.Tint((byte)(Theme.IsDark ? 16 : 9)),
                Child = new TextBlock
                {
                    Text = layout.Scale == TodoBoardTimelineScales.Week
                        ? $"{date.ToString("ddd", UiLanguages.EffectiveCulture)} {date.Day}"
                        : date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = date == DateOnly.FromDateTime(DateTime.Today)
                        ? Theme.ActiveBrush
                        : WeakTextBrush,
                    FontSize = AppTypography.Scale(9.2),
                    FontWeight = date == DateOnly.FromDateTime(DateTime.Today)
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(header, day + 1);
            timeline.Children.Add(header);
        }

        for (var index = 0; index < layout.ScheduledItems.Count; index++)
        {
            var item = layout.ScheduledItems[index];
            timeline.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            var label = BuildTodoBoardTimelineTaskButton(item.Entry);
            Grid.SetRow(label, index + 1);
            timeline.Children.Add(label);

            var track = new Border
            {
                BorderBrush = PaperBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = index % 2 == 0
                    ? Brushes.Transparent
                    : Theme.Tint((byte)(Theme.IsDark ? 8 : 5))
            };
            Grid.SetRow(track, index + 1);
            Grid.SetColumn(track, 1);
            Grid.SetColumnSpan(track, layout.DayCount);
            timeline.Children.Add(track);

            var planning = BuildTodoBoardPlanningItem(item);
            Grid.SetRow(planning, index + 1);
            Grid.SetColumn(planning, item.StartIndex + 1);
            Grid.SetColumnSpan(planning, item.EndIndex - item.StartIndex + 1);
            Panel.SetZIndex(planning, 2);
            timeline.Children.Add(planning);
        }

        return new ScrollViewer
        {
            Content = timeline,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
    }

    private Border BuildTodoBoardPlanningItem(TodoBoardPlanningItem item)
    {
        var isMarker = item.Kind == TodoBoardPlanningItemKinds.Marker;
        var entry = item.Entry;
        var label = CompactTodoBoardText(entry.Text, 80);
        var host = new Border
        {
            Height = isMarker ? 16 : 20,
            Width = isMarker ? 16 : double.NaN,
            Margin = new Thickness(
                isMarker || item.ContinuesBefore ? 2 : 5,
                0,
                isMarker || item.ContinuesAfter ? 2 : 5,
                0),
            Padding = isMarker ? new Thickness(0) : new Thickness(6, 0, 5, 0),
            CornerRadius = isMarker
                ? new CornerRadius(8)
                : new CornerRadius(
                    item.ContinuesBefore ? 0 : 5,
                    item.ContinuesAfter ? 0 : 5,
                    item.ContinuesAfter ? 0 : 5,
                    item.ContinuesBefore ? 0 : 5),
            Background = entry.Done
                ? Theme.Tint((byte)(Theme.IsDark ? 24 : 15))
                : Theme.Tint((byte)(Theme.IsDark ? 54 : 32)),
            BorderBrush = entry.Done ? PaperBorderBrush : Theme.ActiveBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = isMarker
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = TodoBoardPlanningToolTip(item),
            Child = isMarker
                ? null
                : new TextBlock
                {
                    Text = label,
                    Foreground = entry.Done ? WeakTextBrush : TextBrush,
                    FontSize = AppTypography.Scale(9.4),
                    FontWeight = FontWeights.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
        };
        AutomationProperties.SetName(
            host,
            item.Kind == TodoBoardPlanningItemKinds.Span
                ? Strings.Format(
                    "TodoBoardTimelineSpanAutomation",
                    label,
                    item.PlannedStart.ToString("d", UiLanguages.EffectiveCulture),
                    item.PlannedEnd.ToString("d", UiLanguages.EffectiveCulture),
                    entry.PaperTitle)
                : Strings.Format(
                    "TodoBoardTimelineMarkerAutomation",
                    label,
                    item.PlannedStart.ToString("d", UiLanguages.EffectiveCulture),
                    entry.PaperTitle));
        host.MouseLeftButtonUp += (_, e) =>
        {
            _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId);
            e.Handled = true;
        };
        host.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId);
                e.Handled = true;
            }
        };
        return host;
    }

    private Border BuildTodoBoardTimelineTaskButton(TodoBoardEntry entry)
    {
        var button = CreateTodoBoardButton(
            new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = CompactTodoBoardText(entry.Text, 42),
                        Foreground = entry.Done ? WeakTextBrush : TextBrush,
                        FontSize = AppTypography.Scale(9.7),
                        FontWeight = FontWeights.Medium,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = entry.PaperTitle,
                        Foreground = WeakTextBrush,
                        FontSize = AppTypography.Scale(8.6),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            },
            () => _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId));
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(9, 3, 7, 3);
        AutomationProperties.SetName(
            button,
            TodoBoardPlanningEntryAutomationName(entry));
        return new Border
        {
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1, 0, 1, 1),
            Child = button
        };
    }

    private UIElement BuildTodoBoardUnscheduledList(
        IReadOnlyList<TodoBoardEntry> entries)
    {
        var section = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        section.Children.Add(new TextBlock
        {
            Text = Strings.Format("TodoBoardTimelineUnscheduled", entries.Count),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(10.5),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 5)
        });
        if (entries.Count == 0)
        {
            section.Children.Add(new TextBlock
            {
                Text = Strings.Get("TodoBoardTimelineNoUnscheduled"),
                Foreground = WeakTextBrush,
                FontSize = AppTypography.Scale(9.6),
                Margin = new Thickness(8, 3, 0, 5)
            });
            return section;
        }

        var tasks = new WrapPanel();
        foreach (var entry in entries)
        {
            var button = CreateTodoBoardTextButton(
                $"{(entry.Done ? "✓" : "○")} {CompactTodoBoardText(entry.Text, 54)}",
                () => _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId));
            button.Margin = new Thickness(0, 0, 6, 5);
            button.Background = Theme.Tint((byte)(Theme.IsDark ? 18 : 10));
            button.ToolTip = entry.PaperTitle;
            AutomationProperties.SetName(
                button,
                $"{Strings.Get("TodoBoardTimelineUnscheduledAutomationPrefix")} " +
                TodoBoardPlanningEntryAutomationName(entry));
            tasks.Children.Add(button);
        }
        section.Children.Add(new ScrollViewer
        {
            Content = tasks,
            MaxHeight = 126,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return section;
    }

    private void SetTodoBoardTimelineScale(string scale)
    {
        var normalized = TodoBoardTimelineScales.Normalize(scale);
        if (string.Equals(
                _paper.BoardTimelineScale,
                normalized,
                StringComparison.Ordinal))
        {
            return;
        }
        _paper.BoardTimelineScale = normalized;
        _controller.MarkDirty();
        RefreshTodoBoardBody();
    }

    private void ChangeTodoBoardTimelineWindow(int amount)
    {
        _todoBoardTimelineAnchor = TodoBoardTimelineScales.Normalize(
                _paper.BoardTimelineScale) == TodoBoardTimelineScales.Month
            ? _todoBoardTimelineAnchor.AddMonths(amount)
            : _todoBoardTimelineAnchor.AddDays(amount * 7);
        RefreshTodoBoardBody();
    }

    private static string TodoBoardTimelineWindowTitle(
        TodoBoardPlanningTimelineLayout layout) =>
        layout.Scale == TodoBoardTimelineScales.Month
            ? layout.WindowStart.ToString("Y", UiLanguages.EffectiveCulture)
            : $"{layout.WindowStart.ToString("d", UiLanguages.EffectiveCulture)} – " +
                layout.WindowEnd.ToString("d", UiLanguages.EffectiveCulture);

    private static string TodoBoardPlanningEntryAutomationName(TodoBoardEntry entry) =>
        $"{entry.StatusText}: {CompactTodoBoardText(entry.Text, 80)} — {entry.PaperTitle}";

    private static string TodoBoardPlanningToolTip(TodoBoardPlanningItem item)
    {
        var label = CompactTodoBoardText(item.Entry.Text, 120);
        var dates = item.Kind == TodoBoardPlanningItemKinds.Span
            ? $"{item.PlannedStart.ToString("d", UiLanguages.EffectiveCulture)} → " +
                item.PlannedEnd.ToString("d", UiLanguages.EffectiveCulture)
            : item.PlannedStart.ToString("d", UiLanguages.EffectiveCulture);
        return $"{label}\n{item.Entry.PaperTitle}\n{dates}";
    }
}
