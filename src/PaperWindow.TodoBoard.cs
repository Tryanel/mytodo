using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private const double TodoBoardTableMinimumWidth = 850;
    private const int TodoBoardCalendarVisibleItemsPerDay = 3;

    private Border? _todoBoardBody;
    private Grid? _todoBoardContentHost;
    private Button? _todoBoardTableButton;
    private Button? _todoBoardCalendarButton;
    private TextBlock? _todoBoardCountText;
    private bool _todoBoardRefreshScheduled;
    private DateTime _todoBoardCalendarMonth = new(
        DateTime.Today.Year,
        DateTime.Today.Month,
        1);

    private UIElement BuildTodoBoardBody()
    {
        var layout = new Grid
        {
            Background = PaperBrush,
            Margin = new Thickness(0)
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        var toolbar = new Grid
        {
            Margin = new Thickness(14, 10, 14, 8)
        };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var views = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        _todoBoardTableButton = CreateTodoBoardToolbarButton(
            "▤",
            Strings.Get("TodoBoardTableView"),
            () => SetTodoBoardView(TodoBoardViews.Table));
        _todoBoardCalendarButton = CreateTodoBoardToolbarButton(
            "□",
            Strings.Get("TodoBoardCalendarView"),
            () => SetTodoBoardView(TodoBoardViews.Calendar));
        _todoBoardCalendarButton.Margin = new Thickness(4, 0, 0, 0);
        views.Children.Add(_todoBoardTableButton);
        views.Children.Add(_todoBoardCalendarButton);

        var summary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _todoBoardCountText = new TextBlock
        {
            Foreground = WeakTextBrush,
            FontSize = AppTypography.Scale(10.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var refresh = CreateTodoBoardIconButton(
            "↻",
            Strings.Get("TodoBoardRefresh"),
            RefreshTodoBoardBody);
        summary.Children.Add(_todoBoardCountText);
        summary.Children.Add(refresh);
        Grid.SetColumn(summary, 1);
        toolbar.Children.Add(views);
        toolbar.Children.Add(summary);

        var divider = new Border
        {
            Height = 1,
            Background = TitleBarDividerBrush,
            Opacity = 0.72
        };
        Grid.SetRow(divider, 1);

        _todoBoardContentHost = new Grid
        {
            Margin = new Thickness(14, 8, 14, 14)
        };
        Grid.SetRow(_todoBoardContentHost, 2);
        layout.Children.Add(toolbar);
        layout.Children.Add(divider);
        layout.Children.Add(_todoBoardContentHost);

        _todoBoardBody = new Border
        {
            Background = PaperBrush,
            Child = layout
        };
        RefreshTodoBoardBody();
        return _todoBoardBody;
    }

    internal void ScheduleTodoBoardRefresh()
    {
        if (_paper.Type != PaperTypes.Board)
        {
            return;
        }

        InvalidateEdgeCapsulePreviewContent();
        if (
            !_isShellBuilt ||
            !_paper.IsVisible ||
            _paper.IsCollapsed ||
            _todoBoardRefreshScheduled)
        {
            return;
        }

        _todoBoardRefreshScheduled = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _todoBoardRefreshScheduled = false;
            RefreshTodoBoardBody();
        }, DispatcherPriority.Background);
    }

    internal void RefreshTodoBoardForExternalChange()
    {
        if (_paper.Type == PaperTypes.Board && _isShellBuilt)
        {
            RefreshTodoBoardBody();
        }
    }

    private void RebuildTodoBoardPresentation()
    {
        if (_paper.Type != PaperTypes.Board ||
            _todoBoardBody?.Parent is not Panel parent)
        {
            RefreshTodoBoardBody();
            return;
        }

        var index = parent.Children.IndexOf(_todoBoardBody);
        parent.Children.RemoveAt(index);
        var replacement = BuildTodoBoardBody();
        Grid.SetRow(replacement, 1);
        parent.Children.Insert(index, replacement);
    }

    private void SetTodoBoardView(string view)
    {
        var normalized = TodoBoardViews.Normalize(view);
        if (string.Equals(_paper.BoardView, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _paper.BoardView = normalized;
        RefreshTodoBoardBody();
        _controller.MarkDirty();
    }

    private void RefreshTodoBoardBody()
    {
        if (_paper.Type != PaperTypes.Board || _todoBoardContentHost == null)
        {
            return;
        }

        _todoBoardBody!.Background = PaperBrush;
        var view = TodoBoardViews.Normalize(_paper.BoardView);
        _paper.BoardView = view;
        UpdateTodoBoardViewButton(_todoBoardTableButton, view == TodoBoardViews.Table);
        UpdateTodoBoardViewButton(_todoBoardCalendarButton, view == TodoBoardViews.Calendar);

        var entries = CollectTodoBoardEntries();
        if (_todoBoardCountText != null)
        {
            _todoBoardCountText.Foreground = WeakTextBrush;
            _todoBoardCountText.Text = Strings.Format(
                "TodoBoardItemCount",
                entries.Count);
        }

        _todoBoardContentHost.Children.Clear();
        _todoBoardContentHost.Children.Add(entries.Count == 0
            ? BuildTodoBoardEmptyState()
            : view == TodoBoardViews.Calendar
                ? BuildTodoBoardCalendar(entries)
                : BuildTodoBoardTable(entries));
    }

    private List<TodoBoardEntry> CollectTodoBoardEntries() =>
        _controller.State.Papers
            .Where(paper => paper.Type == PaperTypes.Todo)
            .SelectMany(paper => paper.Items
                .Where(item => !TodoRules.IsPlaceholder(item))
                .Select(item => new TodoBoardEntry(
                    paper.Id,
                    item.Id,
                    _controller.PaperDisplayTitle(paper),
                    item.Text,
                    item.Note,
                    item.Done,
                    item.CreatedAt,
                    item.CompletedAt,
                    item.Order)))
            .OrderBy(entry => entry.Done)
            .ThenByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.PaperTitle, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.Order)
            .ToList();

    private UIElement BuildTodoBoardEmptyState()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 360
        };
        stack.Children.Add(new TextBlock
        {
            Text = "▤",
            Foreground = WeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(26),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.72
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("TodoBoardEmpty"),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(15),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("TodoBoardEmptyHint"),
            Foreground = WeakTextBrush,
            FontSize = AppTypography.Scale(11.5),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        });
        return stack;
    }

    private UIElement BuildTodoBoardTable(IReadOnlyList<TodoBoardEntry> entries)
    {
        var rows = new StackPanel
        {
            MinWidth = TodoBoardTableMinimumWidth,
            Background = PaperBrush
        };
        rows.Children.Add(BuildTodoBoardTableHeader());
        foreach (var entry in entries)
        {
            rows.Children.Add(BuildTodoBoardTableRow(entry));
        }

        var scroll = new ScrollViewer
        {
            Content = rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        return new Border
        {
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(RadiusSmall),
            ClipToBounds = true,
            Background = PaperBrush,
            Child = scroll
        };
    }

    private Grid BuildTodoBoardTableHeader()
    {
        var row = CreateTodoBoardTableGrid();
        row.Height = 34;
        row.Background = Theme.Tint((byte)(Theme.IsDark ? 16 : 9));
        AddTodoBoardTableHeaderCell(row, 0, "Aa", Strings.Get("TodoBoardTask"));
        AddTodoBoardTableHeaderCell(row, 1, "◉", Strings.Get("TodoBoardStatus"));
        AddTodoBoardTableHeaderCell(row, 2, "□", Strings.Get("TodoBoardPaper"));
        AddTodoBoardTableHeaderCell(row, 3, "◷", Strings.Get("TodoBoardCreated"));
        AddTodoBoardTableHeaderCell(row, 4, "✓", Strings.Get("TodoBoardCompleted"));
        AddTodoBoardTableHeaderCell(row, 5, "≡", Strings.Get("TodoBoardNote"), last: true);
        return row;
    }

    private Border BuildTodoBoardTableRow(TodoBoardEntry entry)
    {
        var row = CreateTodoBoardTableGrid();
        row.MinHeight = 42;
        row.Background = Brushes.Transparent;
        AddTodoBoardTextCell(
            row,
            0,
            CompactTodoBoardText(entry.Text, 120),
            entry.Done ? WeakTextBrush : TextBrush,
            fontWeight: FontWeights.Medium);
        AddTodoBoardStatusCell(row, 1, entry.Done);
        AddTodoBoardTextCell(row, 2, entry.PaperTitle, WeakTextBrush);
        AddTodoBoardTextCell(row, 3, FormatTodoBoardTimestamp(entry.CreatedAt), WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            4,
            entry.CompletedAt.HasValue
                ? FormatTodoBoardTimestamp(entry.CompletedAt.Value)
                : "—",
            WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            5,
            string.IsNullOrWhiteSpace(entry.Note)
                ? ""
                : CompactTodoBoardText(entry.Note, 140),
            WeakTextBrush,
            last: true);

        var host = new Border
        {
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = row,
            ToolTip = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note
        };
        AutomationProperties.SetName(
            host,
            $"{entry.Text}, {entry.PaperTitle}");
        host.MouseEnter += (_, _) => row.Background = HoverBrush;
        host.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
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

    private Grid CreateTodoBoardTableGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 158
        });
        return grid;
    }

    private void AddTodoBoardTableHeaderCell(
        Grid row,
        int column,
        string glyph,
        string text,
        bool last = false)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            Foreground = WeakTextBrush,
            FontSize = AppTypography.Scale(9.5),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.82
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = WeakTextBrush,
            FontSize = AppTypography.Scale(10.5),
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var cell = new Border
        {
            Padding = new Thickness(10, 0, 8, 0),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(0, 0, last ? 0 : 1, 1),
            Child = content
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private void AddTodoBoardTextCell(
        Grid row,
        int column,
        string text,
        Brush foreground,
        FontWeight? fontWeight = null,
        bool last = false)
    {
        var value = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = AppTypography.Scale(11),
            FontWeight = fontWeight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cell = new Border
        {
            Padding = new Thickness(10, 7, 8, 7),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(0, 0, last ? 0 : 1, 0),
            Child = value
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private void AddTodoBoardStatusCell(Grid row, int column, bool done)
    {
        var label = new TextBlock
        {
            Text = Strings.Get(done ? "TodoBoardDone" : "TodoBoardPending"),
            Foreground = done ? WeakTextBrush : TextBrush,
            FontSize = AppTypography.Scale(9.8),
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        var pill = new Border
        {
            Background = done
                ? Theme.Tint((byte)(Theme.IsDark ? 24 : 14))
                : Theme.Tint((byte)(Theme.IsDark ? 48 : 28)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };
        var cell = new Border
        {
            Padding = new Thickness(9, 7, 8, 7),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = pill
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private UIElement BuildTodoBoardCalendar(IReadOnlyList<TodoBoardEntry> entries)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        var navigation = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previous = CreateTodoBoardIconButton(
            "‹",
            Strings.Get("TodoBoardPreviousMonth"),
            () => ChangeTodoBoardCalendarMonth(-1));
        var next = CreateTodoBoardIconButton(
            "›",
            Strings.Get("TodoBoardNextMonth"),
            () => ChangeTodoBoardCalendarMonth(1));
        var month = new TextBlock
        {
            Text = _todoBoardCalendarMonth.ToString(
                "Y",
                UiLanguages.EffectiveCulture),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(13),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(previous);
        left.Children.Add(next);
        left.Children.Add(month);
        var today = CreateTodoBoardTextButton(
            Strings.Get("TodoBoardToday"),
            () =>
            {
                _todoBoardCalendarMonth = new DateTime(
                    DateTime.Today.Year,
                    DateTime.Today.Month,
                    1);
                RefreshTodoBoardBody();
            });
        Grid.SetColumn(today, 2);
        navigation.Children.Add(left);
        navigation.Children.Add(today);

        var calendar = BuildTodoBoardMonthGrid(entries);
        var scroll = new ScrollViewer
        {
            Content = calendar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(navigation);
        layout.Children.Add(scroll);
        return layout;
    }

    private Grid BuildTodoBoardMonthGrid(IReadOnlyList<TodoBoardEntry> entries)
    {
        var culture = UiLanguages.EffectiveCulture;
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)_todoBoardCalendarMonth.DayOfWeek -
            (int)firstDayOfWeek + 7) % 7;
        var firstVisibleDate = _todoBoardCalendarMonth.AddDays(-offset);

        var calendar = new Grid
        {
            MinWidth = 770,
            Background = PaperBrush
        };
        for (var column = 0; column < 7; column++)
        {
            calendar.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 108
            });
        }
        calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (var row = 0; row < 6; row++)
        {
            calendar.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(96)
            });
        }

        for (var column = 0; column < 7; column++)
        {
            var day = firstVisibleDate.AddDays(column);
            var header = new Border
            {
                BorderBrush = PaperBorderBrush,
                BorderThickness = new Thickness(
                    column == 0 ? 1 : 0,
                    1,
                    1,
                    1),
                Background = Theme.Tint((byte)(Theme.IsDark ? 16 : 9)),
                Child = new TextBlock
                {
                    Text = culture.DateTimeFormat.GetShortestDayName(day.DayOfWeek),
                    Foreground = WeakTextBrush,
                    FontSize = AppTypography.Scale(10),
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(header, column);
            calendar.Children.Add(header);
        }

        for (var index = 0; index < 42; index++)
        {
            var date = firstVisibleDate.AddDays(index);
            var row = index / 7 + 1;
            var column = index % 7;
            var cell = BuildTodoBoardCalendarDay(date, entries);
            cell.BorderThickness = new Thickness(
                column == 0 ? 1 : 0,
                0,
                1,
                1);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            calendar.Children.Add(cell);
        }
        return calendar;
    }

    private Border BuildTodoBoardCalendarDay(
        DateTime date,
        IReadOnlyList<TodoBoardEntry> entries)
    {
        var inCurrentMonth = date.Month == _todoBoardCalendarMonth.Month &&
            date.Year == _todoBoardCalendarMonth.Year;
        var isToday = date == DateTime.Today;
        var tasks = entries
            .Where(entry => TodoBoardEntrySpansDate(entry, date))
            .OrderBy(entry => entry.Done)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToList();

        var content = new Grid { Margin = new Thickness(5, 4, 5, 4) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        var dayNumber = new Border
        {
            Width = 22,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            BorderBrush = isToday ? Theme.ActiveBrush : Brushes.Transparent,
            BorderThickness = new Thickness(isToday ? 1 : 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                Foreground = inCurrentMonth ? TextBrush : WeakTextBrush,
                FontSize = AppTypography.Scale(9.8),
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = inCurrentMonth ? 1 : 0.55
            }
        };
        var items = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var entry in tasks.Take(TodoBoardCalendarVisibleItemsPerDay))
        {
            items.Children.Add(BuildTodoBoardCalendarItem(entry));
        }
        if (tasks.Count > TodoBoardCalendarVisibleItemsPerDay)
        {
            items.Children.Add(new TextBlock
            {
                Text = Strings.Format(
                    "TodoBoardMoreItems",
                    tasks.Count - TodoBoardCalendarVisibleItemsPerDay),
                Foreground = WeakTextBrush,
                FontSize = AppTypography.Scale(9.2),
                Margin = new Thickness(5, 2, 0, 0)
            });
        }
        Grid.SetRow(items, 1);
        content.Children.Add(dayNumber);
        content.Children.Add(items);
        return new Border
        {
            BorderBrush = PaperBorderBrush,
            Background = inCurrentMonth
                ? PaperBrush
                : Theme.Tint((byte)(Theme.IsDark ? 10 : 6)),
            Child = content
        };
    }

    private Border BuildTodoBoardCalendarItem(TodoBoardEntry entry)
    {
        var item = new Border
        {
            Height = 18,
            Margin = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(5, 0, 4, 0),
            CornerRadius = new CornerRadius(3),
            Background = entry.Done
                ? Theme.Tint((byte)(Theme.IsDark ? 22 : 13))
                : Theme.Tint((byte)(Theme.IsDark ? 48 : 28)),
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = TodoBoardCalendarToolTip(entry),
            Child = new TextBlock
            {
                Text = $"{(entry.Done ? "✓" : "○")} {CompactTodoBoardText(entry.Text, 34)}",
                Foreground = entry.Done ? WeakTextBrush : TextBrush,
                FontSize = AppTypography.Scale(9.3),
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(item, entry.Text);
        item.MouseLeftButtonUp += (_, e) =>
        {
            _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId);
            e.Handled = true;
        };
        item.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId);
                e.Handled = true;
            }
        };
        return item;
    }

    private Button CreateTodoBoardToolbarButton(
        string glyph,
        string text,
        Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(11),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = AppTypography.Scale(10.8),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var button = CreateTodoBoardButton(content, action);
        button.Padding = new Thickness(9, 5, 9, 5);
        return button;
    }

    private Button CreateTodoBoardIconButton(
        string glyph,
        string toolTip,
        Action action)
    {
        var button = CreateTodoBoardButton(new TextBlock
        {
            Text = glyph,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(13),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, action);
        button.Width = 28;
        button.Height = 26;
        button.Padding = new Thickness(0);
        button.ToolTip = toolTip;
        AutomationProperties.SetName(button, toolTip);
        return button;
    }

    private Button CreateTodoBoardTextButton(string text, Action action)
    {
        var button = CreateTodoBoardButton(new TextBlock
        {
            Text = text,
            FontSize = AppTypography.Scale(10.5),
            VerticalAlignment = VerticalAlignment.Center
        }, action);
        button.Padding = new Thickness(9, 4, 9, 4);
        return button;
    }

    private Button CreateTodoBoardButton(UIElement content, Action action)
    {
        var button = new Button
        {
            Content = content,
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = true
        };
        button.Template = BuildTodoBoardButtonTemplate();
        button.Click += (_, _) => action();
        return button;
    }

    private static ControlTemplate BuildTodoBoardButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Root";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Theme.HoverBrush));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.ActiveBrush));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        template.Triggers.Add(focus);
        return template;
    }

    private static void UpdateTodoBoardViewButton(Button? button, bool selected)
    {
        if (button == null)
        {
            return;
        }
        button.Foreground = selected ? Theme.TextBrush : Theme.WeakTextBrush;
        button.Background = selected
            ? Theme.Tint((byte)(Theme.IsDark ? 42 : 24))
            : Brushes.Transparent;
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void ChangeTodoBoardCalendarMonth(int months)
    {
        _todoBoardCalendarMonth = _todoBoardCalendarMonth.AddMonths(months);
        RefreshTodoBoardBody();
    }

    private static bool TodoBoardEntrySpansDate(TodoBoardEntry entry, DateTime date)
    {
        var start = entry.CreatedAt.LocalDateTime.Date;
        var end = entry.CompletedAt?.LocalDateTime.Date ?? DateTime.Today;
        if (end < start)
        {
            end = start;
        }
        return date >= start && date <= end;
    }

    private static string TodoBoardCalendarToolTip(TodoBoardEntry entry)
    {
        var end = entry.CompletedAt.HasValue
            ? FormatTodoBoardTimestamp(entry.CompletedAt.Value)
            : Strings.Get("TodoBoardToday");
        var note = string.IsNullOrWhiteSpace(entry.Note)
            ? ""
            : $"\n{Strings.Get("TodoBoardNote")}: {CompactTodoBoardText(entry.Note, 160)}";
        return $"{entry.Text}\n{entry.PaperTitle}\n{FormatTodoBoardTimestamp(entry.CreatedAt)} → {end}{note}";
    }

    private static string FormatTodoBoardTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", UiLanguages.EffectiveCulture);

    private static string CompactTodoBoardText(string? value, int maxLength)
    {
        var compact = string.Join(
            " ",
            (value ?? "").Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
        compact = compact.Trim();
        if (compact.Length == 0)
        {
            return Strings.Get("TodoBoardTask");
        }
        return compact.Length <= maxLength
            ? compact
            : compact[..(maxLength - 1)] + "…";
    }

    private sealed record TodoBoardEntry(
        string PaperId,
        string ItemId,
        string PaperTitle,
        string Text,
        string Note,
        bool Done,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        int Order);
}
