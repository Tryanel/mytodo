using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private const double TodoBoardTableMinimumWidth = 1080;

    private Border? _todoBoardBody;
    private Grid? _todoBoardContentHost;
    private Button? _todoBoardTableButton;
    private Button? _todoBoardCalendarButton;
    private TextBlock? _todoBoardCountText;
    private StackPanel? _todoBoardTableTools;
    private TextBox? _todoBoardSearchBox;
    private TextBlock? _todoBoardSearchPlaceholder;
    private Button? _todoBoardClearSearchButton;
    private Button? _todoBoardFilterButton;
    private Button? _todoBoardSortButton;
    private Popup? _todoBoardCalendarOverflowPopup;
    private string _todoBoardSearchQuery = "";
    private bool _todoBoardRefreshScheduled;
    private DateTime _todoBoardCalendarMonth = new(
        DateTime.Today.Year,
        DateTime.Today.Month,
        1);

    private UIElement BuildTodoBoardBody()
    {
        var layout = new Grid
        {
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
        _todoBoardTimelineButton = CreateTodoBoardToolbarButton(
            "↔",
            Strings.Get("TodoBoardTimelineView"),
            () => SetTodoBoardView(TodoBoardViews.Timeline));
        _todoBoardTimelineButton.Margin = new Thickness(4, 0, 0, 0);
        views.Children.Add(_todoBoardTableButton);
        views.Children.Add(_todoBoardCalendarButton);
        views.Children.Add(_todoBoardTimelineButton);

        var summary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _todoBoardTableTools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _todoBoardTableTools.Children.Add(BuildTodoBoardSearchControl());
        _todoBoardFilterButton = CreateTodoBoardToolbarButton(
            "≡",
            Strings.Get("TodoBoardFilter"),
            OpenTodoBoardFilterDialog);
        _todoBoardFilterButton.Margin = new Thickness(5, 0, 0, 0);
        _todoBoardTableTools.Children.Add(_todoBoardFilterButton);
        _todoBoardSortButton = CreateTodoBoardToolbarButton(
            "↕",
            Strings.Get("TodoBoardSort"),
            OpenTodoBoardSortDialog);
        _todoBoardSortButton.Margin = new Thickness(5, 0, 0, 0);
        _todoBoardTableTools.Children.Add(_todoBoardSortButton);
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
        summary.Children.Add(_todoBoardTableTools);
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
            // The shared paper chrome owns the paper surface and its rounded outline. Keeping
            // the board body transparent prevents this rectangular child from covering the
            // shell's lower corners, matching Todo and Note papers.
            Background = Brushes.Transparent,
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

        CloseTodoBoardCalendarOverflow();

        var view = TodoBoardViews.Normalize(_paper.BoardView);
        _paper.BoardView = view;
        _paper.BoardTimelineScale = TodoBoardTimelineScales.Normalize(
            _paper.BoardTimelineScale);
        _paper.BoardSort = TodoBoardSorts.Normalize(_paper.BoardSort);
        _paper.BoardFilters = TodoBoardFilters.Normalize(_paper.BoardFilters);
        _paper.BoardSortRules = _paper.BoardSortRules is null
            ? TodoBoardSortRules.FromLegacy(_paper.BoardSort)
            : TodoBoardSortRules.Normalize(_paper.BoardSortRules);
        UpdateTodoBoardViewButton(_todoBoardTableButton, view == TodoBoardViews.Table);
        UpdateTodoBoardViewButton(_todoBoardCalendarButton, view == TodoBoardViews.Calendar);
        UpdateTodoBoardViewButton(_todoBoardTimelineButton, view == TodoBoardViews.Timeline);
        if (_todoBoardTableTools != null)
        {
            _todoBoardTableTools.Visibility = Visibility.Visible;
        }
        if (_todoBoardSortButton != null)
        {
            _todoBoardSortButton.Visibility = view == TodoBoardViews.Table
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        UpdateTodoBoardFilterButton();
        UpdateTodoBoardSortButton();

        var snapshot = TodoBoardProjection.Build(
            _controller.State.Papers,
            _controller.PaperDisplayTitle,
            new TodoBoardQueryContext(
                _todoBoardSearchQuery,
                _paper.BoardSort,
                DateOnly.FromDateTime(DateTime.Today),
                CultureInfo.CurrentCulture,
                UiLanguages.EffectiveCulture,
                TimeZoneInfo.Local,
                Strings.Get("TodoBoardPending"),
                Strings.Get("TodoBoardDone"),
                _paper.BoardFilters,
                _paper.BoardSortRules));
        var entries = snapshot.AllEntries;
        var displayedEntries = view == TodoBoardViews.Table
            ? snapshot.TableEntries
            : snapshot.QueryEntries;
        var hasQuery = !string.IsNullOrWhiteSpace(_todoBoardSearchQuery) ||
            TodoBoardFilters.IsActive(_paper.BoardFilters);
        if (_todoBoardCountText != null)
        {
            _todoBoardCountText.Foreground = WeakTextBrush;
            _todoBoardCountText.Text = hasQuery
                ? Strings.Format(
                    "TodoBoardFilteredCount",
                    displayedEntries.Count,
                    entries.Count)
                : Strings.Format("TodoBoardItemCount", entries.Count);
        }

        _todoBoardContentHost.Children.Clear();
        _todoBoardContentHost.Children.Add(entries.Count == 0
            ? BuildTodoBoardEmptyState()
            : displayedEntries.Count == 0
                ? BuildTodoBoardNoResultsState()
            : view == TodoBoardViews.Calendar
                ? BuildTodoBoardCalendar(snapshot)
                : view == TodoBoardViews.Timeline
                    ? BuildTodoBoardPlanningTimeline(snapshot)
                    : BuildTodoBoardTable(displayedEntries));
    }

    private UIElement BuildTodoBoardSearchControl()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = "⌕",
            Foreground = WeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(12),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 5, 0),
            IsHitTestVisible = false
        };

        _todoBoardSearchBox = new TextBox
        {
            Text = _todoBoardSearchQuery,
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = TextBrush,
            FontSize = AppTypography.Scale(10.8),
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = Strings.Get("TodoBoardSearchToolTip")
        };
        AutomationProperties.SetName(
            _todoBoardSearchBox,
            Strings.Get("TodoBoardSearchPlaceholder"));

        _todoBoardSearchPlaceholder = new TextBlock
        {
            Text = Strings.Get("TodoBoardSearchPlaceholder"),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(10.8),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0.68,
            Visibility = string.IsNullOrEmpty(_todoBoardSearchQuery)
                ? Visibility.Visible
                : Visibility.Collapsed
        };

        var textHost = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        textHost.Children.Add(_todoBoardSearchPlaceholder);
        textHost.Children.Add(_todoBoardSearchBox);
        Grid.SetColumn(textHost, 1);

        _todoBoardClearSearchButton = CreateTodoBoardIconButton(
            "×",
            Strings.Get("TodoBoardClearSearch"),
            ClearTodoBoardSearch);
        _todoBoardClearSearchButton.Width = 22;
        _todoBoardClearSearchButton.Height = 24;
        _todoBoardClearSearchButton.Visibility =
            string.IsNullOrEmpty(_todoBoardSearchQuery)
                ? Visibility.Collapsed
                : Visibility.Visible;
        Grid.SetColumn(_todoBoardClearSearchButton, 2);

        layout.Children.Add(icon);
        layout.Children.Add(textHost);
        layout.Children.Add(_todoBoardClearSearchButton);

        var border = new Border
        {
            Width = 210,
            Height = 28,
            Background = Theme.Tint((byte)(Theme.IsDark ? 16 : 8)),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = layout
        };
        _todoBoardSearchBox.GotKeyboardFocus += (_, _) =>
            border.BorderBrush = Theme.ActiveBrush;
        _todoBoardSearchBox.LostKeyboardFocus += (_, _) =>
            border.BorderBrush = PaperBorderBrush;
        _todoBoardSearchBox.TextChanged += (_, _) =>
        {
            _todoBoardSearchQuery = _todoBoardSearchBox.Text;
            var empty = string.IsNullOrEmpty(_todoBoardSearchQuery);
            if (_todoBoardSearchPlaceholder != null)
            {
                _todoBoardSearchPlaceholder.Visibility = empty
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (_todoBoardClearSearchButton != null)
            {
                _todoBoardClearSearchButton.Visibility = empty
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            RefreshTodoBoardBody();
        };
        _todoBoardSearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape &&
                !string.IsNullOrEmpty(_todoBoardSearchQuery))
            {
                ClearTodoBoardSearch();
                e.Handled = true;
            }
        };
        return border;
    }

    private void ClearTodoBoardSearch()
    {
        _todoBoardSearchQuery = "";
        if (_todoBoardSearchBox != null)
        {
            _todoBoardSearchBox.Text = "";
            _todoBoardSearchBox.Focus();
        }
        else
        {
            RefreshTodoBoardBody();
        }
    }

    private bool HandleTodoBoardPreviewKeyDown(KeyEventArgs e)
    {
        if (_paper.Type != PaperTypes.Board)
        {
            return false;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            _ = Dispatcher.BeginInvoke(
                (Action)(() =>
                {
                    _todoBoardSearchBox?.Focus();
                    _todoBoardSearchBox?.SelectAll();
                }),
                DispatcherPriority.Input);
            return true;
        }

        if (e.Key == Key.Escape &&
            Keyboard.Modifiers == ModifierKeys.None &&
            _todoBoardSearchBox?.IsKeyboardFocusWithin == true &&
            !string.IsNullOrEmpty(_todoBoardSearchQuery))
        {
            ClearTodoBoardSearch();
            return true;
        }

        return false;
    }

    private UIElement BuildTodoBoardNoResultsState()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 380
        };
        stack.Children.Add(new TextBlock
        {
            Text = "⌕",
            Foreground = WeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.72
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("TodoBoardNoResults"),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(14.5),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 9, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("TodoBoardNoResultsHint"),
            Foreground = WeakTextBrush,
            FontSize = AppTypography.Scale(11.2),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 10)
        });
        stack.Children.Add(CreateTodoBoardTextButton(
            Strings.Get("TodoBoardQueryClear"),
            ClearTodoBoardQuery));
        return stack;
    }

    private void OpenTodoBoardFilterDialog()
    {
        var papers = _controller.State.Papers
            .Where(paper => paper.Type == PaperTypes.Todo)
            .Select(paper => new TodoBoardPaperOption(
                paper.Id,
                _controller.PaperDisplayTitle(paper)))
            .ToList();
        if (!TodoBoardFilterDialog.TryShow(
                this,
                _paper.BoardFilters,
                papers,
                _controller.State.EnableAnimations,
                out var filters))
        {
            return;
        }

        _paper.BoardFilters = filters;
        RefreshTodoBoardBody();
        _controller.MarkDirty();
    }

    private void ClearTodoBoardQuery()
    {
        _paper.BoardFilters = new TodoBoardFilterState();
        if (_todoBoardSearchBox != null && _todoBoardSearchBox.Text.Length > 0)
        {
            _todoBoardSearchBox.Text = "";
        }
        else
        {
            _todoBoardSearchQuery = "";
            RefreshTodoBoardBody();
        }
        _controller.MarkDirty();
    }

    private void UpdateTodoBoardFilterButton()
    {
        if (_todoBoardFilterButton == null)
        {
            return;
        }
        var active = TodoBoardFilters.IsActive(_paper.BoardFilters);
        _todoBoardFilterButton.Foreground = active ? Theme.ActiveBrush : TextBrush;
        _todoBoardFilterButton.Background = active
            ? Theme.Tint((byte)(Theme.IsDark ? 42 : 24))
            : Brushes.Transparent;
    }

    private void OpenTodoBoardSortDialog()
    {
        if (!TodoBoardSortDialog.TryShow(
                this,
                _paper.BoardSortRules,
                _controller.State.EnableAnimations,
                out var rules))
        {
            return;
        }
        SetTodoBoardSortRules(rules);
    }

    private void SetTodoBoardSortRules(IEnumerable<TodoBoardSortRule> rules)
    {
        _paper.BoardSortRules = TodoBoardSortRules.Normalize(rules);
        _paper.BoardSort = TodoBoardSorts.Default;
        RefreshTodoBoardBody();
        _controller.MarkDirty();
    }

    private void ToggleTodoBoardSort(string field, bool descendingFirst)
    {
        SetTodoBoardSortRules(TodoBoardSortRules.SetPrimary(
            _paper.BoardSortRules,
            field,
            descendingFirst));
    }

    private void UpdateTodoBoardSortButton()
    {
        if (_todoBoardSortButton == null)
        {
            return;
        }

        var rules = TodoBoardSortRules.Normalize(_paper.BoardSortRules);
        var isDefault = rules.Count == 0;
        _todoBoardSortButton.Foreground = isDefault ? TextBrush : Theme.ActiveBrush;
        _todoBoardSortButton.Background = isDefault
            ? Brushes.Transparent
            : Theme.Tint((byte)(Theme.IsDark ? 42 : 24));
        var description = TodoBoardSortDescription(rules);
        _todoBoardSortButton.ToolTip = Strings.Format(
            "TodoBoardSortCurrent",
            description);
        AutomationProperties.SetName(
            _todoBoardSortButton,
            Strings.Format("TodoBoardSortCurrent", description));
    }

    private static string TodoBoardSortDescription(
        IReadOnlyList<TodoBoardSortRule> rules)
    {
        if (rules.Count == 0)
        {
            return Strings.Get("TodoBoardSortDefault");
        }
        return string.Join(
            " → ",
            rules.Select((rule, index) =>
                $"{index + 1}. {TodoBoardSortDialog.FieldLabel(rule.Field)} " +
                (rule.Descending ? "↓" : "↑")));
    }

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
        AddTodoBoardTableHeaderCell(
            row, 0, "Aa", Strings.Get("TodoBoardTask"),
            TodoBoardSortFields.Task);
        AddTodoBoardTableHeaderCell(
            row, 1, "◉", Strings.Get("TodoBoardStatus"),
            TodoBoardSortFields.Status);
        AddTodoBoardTableHeaderCell(
            row, 2, "□", Strings.Get("TodoBoardPaper"),
            TodoBoardSortFields.Paper);
        AddTodoBoardTableHeaderCell(
            row, 3, "◷", Strings.Get("TodoBoardCreated"),
            TodoBoardSortFields.Created,
            descendingFirst: true);
        AddTodoBoardTableHeaderCell(
            row, 4, "✓", Strings.Get("TodoBoardCompleted"),
            TodoBoardSortFields.Completed,
            descendingFirst: true);
        AddTodoBoardTableHeaderCell(
            row, 5, "▶", Strings.Get("TodoPlanningStartDate"),
            TodoBoardSortFields.PlannedStart);
        AddTodoBoardTableHeaderCell(
            row, 6, "◆", Strings.Get("TodoPlanningDueDate"),
            TodoBoardSortFields.Due);
        AddTodoBoardTableHeaderCell(
            row, 7, "≡", Strings.Get("TodoBoardNote"),
            TodoBoardSortFields.Note,
            last: true);
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
        AddTodoBoardStatusCell(row, 1, entry.StatusText, entry.Done);
        AddTodoBoardTextCell(row, 2, entry.PaperTitle, WeakTextBrush);
        AddTodoBoardTextCell(row, 3, entry.CreatedText, WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            4,
            entry.CompletedText ?? "—",
            WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            5,
            entry.PlannedStartText ?? "—",
            WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            6,
            entry.DueText ?? "—",
            WeakTextBrush);
        AddTodoBoardTextCell(
            row,
            7,
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
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
        string field,
        bool descendingFirst = false,
        bool last = false)
    {
        var rules = TodoBoardSortRules.Normalize(_paper.BoardSortRules);
        var priority = rules.FindIndex(rule => rule.Field == field);
        var isActive = priority >= 0;
        var isDescending = isActive && rules[priority].Descending;
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
            Foreground = isActive ? Theme.ActiveBrush : WeakTextBrush,
            FontSize = AppTypography.Scale(10.5),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (isActive)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{(isDescending ? "↓" : "↑")}{priority + 1}",
                Foreground = Theme.ActiveBrush,
                FontSize = AppTypography.Scale(10),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        var cell = new Border
        {
            Padding = new Thickness(10, 0, 8, 0),
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(0, 0, last ? 0 : 1, 1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = Strings.Format("TodoBoardSortBy", text),
            Child = content
        };
        AutomationProperties.SetName(
            cell,
            Strings.Format("TodoBoardSortBy", text));
        cell.MouseEnter += (_, _) => cell.Background = HoverBrush;
        cell.MouseLeave += (_, _) => cell.Background = cell.IsKeyboardFocusWithin
            ? Theme.Tint((byte)(Theme.IsDark ? 36 : 22))
            : Brushes.Transparent;
        cell.GotKeyboardFocus += (_, _) =>
            cell.Background = Theme.Tint((byte)(Theme.IsDark ? 36 : 22));
        cell.LostKeyboardFocus += (_, _) =>
            cell.Background = cell.IsMouseOver ? HoverBrush : Brushes.Transparent;
        cell.MouseLeftButtonUp += (_, e) =>
        {
            ToggleTodoBoardSort(field, descendingFirst);
            e.Handled = true;
        };
        cell.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleTodoBoardSort(field, descendingFirst);
                e.Handled = true;
            }
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

    private void AddTodoBoardStatusCell(
        Grid row,
        int column,
        string statusText,
        bool done)
    {
        var label = new TextBlock
        {
            Text = statusText,
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

    private UIElement BuildTodoBoardCalendar(TodoBoardSnapshot snapshot)
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

        var calendar = BuildTodoBoardMonthGrid(snapshot);
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

    private Grid BuildTodoBoardMonthGrid(TodoBoardSnapshot snapshot)
    {
        var culture = UiLanguages.EffectiveCulture;
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)_todoBoardCalendarMonth.DayOfWeek -
            (int)firstDayOfWeek + 7) % 7;
        var firstVisibleDate = _todoBoardCalendarMonth.AddDays(-offset);
        var firstVisibleCalendarDate = DateOnly.FromDateTime(firstVisibleDate);
        var visibleLaneCount = TodoBoardCalendarVisibleLaneCount();
        var activityLayout = TodoBoardActivityCalendarLayout.Build(
            snapshot,
            firstVisibleCalendarDate,
            weekCount: 6,
            visibleLaneCount);
        var weekRowHeight = Math.Max(88, 56 + visibleLaneCount * 20);

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
                Height = new GridLength(weekRowHeight)
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
            var cell = BuildTodoBoardCalendarDay(date, snapshot.Today);
            cell.BorderThickness = new Thickness(
                column == 0 ? 1 : 0,
                0,
                1,
                1);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            calendar.Children.Add(cell);
        }

        foreach (var segment in activityLayout.Segments.Where(segment => segment.IsVisible))
        {
            var item = BuildTodoBoardCalendarSegment(segment);
            Grid.SetRow(item, segment.WeekIndex + 1);
            Grid.SetColumn(item, segment.StartColumn);
            Grid.SetColumnSpan(item, segment.EndColumn - segment.StartColumn + 1);
            Panel.SetZIndex(item, 2);
            calendar.Children.Add(item);
        }

        foreach (var overflow in activityLayout.OverflowDays)
        {
            var dayOffset = overflow.Date.DayNumber - firstVisibleCalendarDate.DayNumber;
            var entries = activityLayout.EntriesOn(overflow.Date);
            Button? overflowButton = null;
            overflowButton = CreateTodoBoardTextButton(
                Strings.Format("TodoBoardMoreItems", overflow.HiddenEntries.Count),
                () =>
                {
                    if (overflowButton != null)
                    {
                        OpenTodoBoardCalendarOverflow(
                            overflowButton,
                            overflow.Date,
                            entries);
                    }
                });
            overflowButton.Padding = new Thickness(5, 1, 5, 1);
            overflowButton.Margin = new Thickness(4, 0, 4, 4);
            overflowButton.HorizontalAlignment = HorizontalAlignment.Left;
            overflowButton.VerticalAlignment = VerticalAlignment.Bottom;
            overflowButton.Foreground = WeakTextBrush;
            overflowButton.ToolTip = Strings.Format(
                "TodoBoardCalendarOverflowToolTip",
                entries.Count);
            AutomationProperties.SetName(
                overflowButton,
                Strings.Format("TodoBoardCalendarOverflowToolTip", entries.Count));
            Grid.SetRow(overflowButton, dayOffset / 7 + 1);
            Grid.SetColumn(overflowButton, dayOffset % 7);
            Panel.SetZIndex(overflowButton, 3);
            calendar.Children.Add(overflowButton);
        }
        return calendar;
    }

    private Border BuildTodoBoardCalendarDay(
        DateTime date,
        DateOnly today)
    {
        var inCurrentMonth = date.Month == _todoBoardCalendarMonth.Month &&
            date.Year == _todoBoardCalendarMonth.Year;
        var calendarDate = DateOnly.FromDateTime(date);
        var isToday = calendarDate == today;

        var content = new Grid { Margin = new Thickness(5, 4, 5, 4) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
        content.Children.Add(dayNumber);
        return new Border
        {
            BorderBrush = PaperBorderBrush,
            Background = inCurrentMonth
                ? PaperBrush
                : Theme.Tint((byte)(Theme.IsDark ? 10 : 6)),
            Child = content
        };
    }

    private Border BuildTodoBoardCalendarSegment(TodoBoardActivitySegment segment)
    {
        var entry = segment.Entry;
        var item = new Border
        {
            Height = 18,
            Margin = new Thickness(
                segment.ContinuesBefore ? 0 : 4,
                29 + segment.Lane * 20,
                segment.ContinuesAfter ? 0 : 4,
                0),
            Padding = new Thickness(5, 0, 4, 0),
            CornerRadius = new CornerRadius(
                segment.ContinuesBefore ? 0 : 4,
                segment.ContinuesAfter ? 0 : 4,
                segment.ContinuesAfter ? 0 : 4,
                segment.ContinuesBefore ? 0 : 4),
            Background = entry.Done
                ? Theme.Tint((byte)(Theme.IsDark ? 22 : 13))
                : Theme.Tint((byte)(Theme.IsDark ? 48 : 28)),
            BorderBrush = entry.Done
                ? PaperBorderBrush
                : Theme.ActiveBrush,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Focusable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
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
        AutomationProperties.SetName(
            item,
            TodoBoardCalendarAutomationName(entry));
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

    private int TodoBoardCalendarVisibleLaneCount()
    {
        var height = _todoBoardContentHost?.ActualHeight ?? 0;
        if (!double.IsFinite(height) || height <= 0)
        {
            return 3;
        }

        var approximateWeekHeight = Math.Max(0, height - 76) / 6;
        return Math.Clamp(
            (int)Math.Floor((approximateWeekHeight - 56) / 20),
            1,
            6);
    }

    private void OpenTodoBoardCalendarOverflow(
        Button anchor,
        DateOnly date,
        IReadOnlyList<TodoBoardEntry> entries)
    {
        CloseTodoBoardCalendarOverflow();

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Strings.Format(
                "TodoBoardCalendarOverflowTitle",
                date.ToString("D", UiLanguages.EffectiveCulture),
                entries.Count),
            Foreground = TextBrush,
            FontSize = AppTypography.Scale(12),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 10, 12, 8)
        });
        foreach (var entry in entries)
        {
            var row = CreateTodoBoardButton(
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{(entry.Done ? "✓" : "○")} {CompactTodoBoardText(entry.Text, 80)}",
                            Foreground = entry.Done ? WeakTextBrush : TextBrush,
                            FontSize = AppTypography.Scale(10.4),
                            FontWeight = FontWeights.Medium,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = entry.PaperTitle,
                            Foreground = WeakTextBrush,
                            FontSize = AppTypography.Scale(9.3),
                            Margin = new Thickness(18, 2, 0, 0)
                        }
                    }
                },
                () =>
                {
                    CloseTodoBoardCalendarOverflow();
                    _controller.OpenTodoFromBoard(entry.PaperId, entry.ItemId);
                });
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.Padding = new Thickness(10, 7, 10, 7);
            row.ToolTip = TodoBoardCalendarToolTip(entry);
            AutomationProperties.SetName(
                row,
                TodoBoardCalendarAutomationName(entry));
            content.Children.Add(row);
        }

        var workArea = TodoBoardCalendarPopupWorkArea(anchor);
        var popupBody = new Border
        {
            Width = Math.Max(120, Math.Min(340, workArea.Width - 48)),
            MaxHeight = Math.Max(100, workArea.Height - 72),
            Background = PaperBrush,
            BorderBrush = PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Focusable = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 4,
                Opacity = Theme.IsDark ? 0.45 : 0.2
            },
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            }
        };
        popupBody.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseTodoBoardCalendarOverflow();
                anchor.Focus();
                e.Handled = true;
            }
        };

        var popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Bottom,
            PlacementTarget = anchor,
            HorizontalOffset = 0,
            VerticalOffset = 2,
            Child = popupBody
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_todoBoardCalendarOverflowPopup, popup))
            {
                _todoBoardCalendarOverflowPopup = null;
            }
        };
        _todoBoardCalendarOverflowPopup = popup;
        popup.IsOpen = true;
        _ = Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                if (_todoBoardCalendarOverflowPopup?.IsOpen == true)
                {
                    popupBody.Focus();
                }
            }),
            DispatcherPriority.Input);
    }

    private void CloseTodoBoardCalendarOverflow()
    {
        if (_todoBoardCalendarOverflowPopup != null)
        {
            _todoBoardCalendarOverflowPopup.IsOpen = false;
            _todoBoardCalendarOverflowPopup = null;
        }
    }

    private Rect TodoBoardCalendarPopupWorkArea(FrameworkElement anchor)
    {
        try
        {
            var center = anchor.PointToScreen(new Point(
                Math.Max(0, anchor.ActualWidth / 2),
                Math.Max(0, anchor.ActualHeight / 2)));
            return WindowWorkAreaHelper.TryGetMonitorGeometryAtDeviceScreenPoint(
                    DeviceScreenPoint.FromPoint(center),
                    out var geometry)
                ? geometry.LocalWorkAreaDip
                : WindowWorkAreaHelper.WorkAreaFor(this);
        }
        catch (InvalidOperationException)
        {
            return WindowWorkAreaHelper.WorkAreaFor(this);
        }
    }

    private static string TodoBoardCalendarAutomationName(TodoBoardEntry entry) =>
        $"{entry.StatusText}: {CompactTodoBoardText(entry.Text, 80)} — {entry.PaperTitle}";

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

    private static string TodoBoardCalendarToolTip(TodoBoardEntry entry)
    {
        var end = entry.CompletedText ?? Strings.Get("TodoBoardToday");
        var note = string.IsNullOrWhiteSpace(entry.Note)
            ? ""
            : $"\n{Strings.Get("TodoBoardNote")}: {CompactTodoBoardText(entry.Note, 160)}";
        return $"{entry.Text}\n{entry.PaperTitle}\n{entry.CreatedText} → {end}{note}";
    }

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

}
