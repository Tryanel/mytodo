using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PaperTodo;

internal sealed record TodoBoardPaperOption(string Id, string Title);

internal static class TodoBoardFilterDialog
{
    public static bool TryShow(
        Window owner,
        TodoBoardFilterState? initial,
        IReadOnlyList<TodoBoardPaperOption> papers,
        bool animate,
        out TodoBoardFilterState filters)
    {
        filters = TodoBoardFilters.Clone(initial);
        TodoBoardFilterState? result = null;
        var dialog = TodoBoardQueryDialogControls.Window(
            owner,
            Strings.Get("TodoBoardFilterTitle"),
            width: 560,
            height: 690);
        var content = new StackPanel();
        content.Children.Add(TodoBoardQueryDialogControls.Header(
            dialog,
            Strings.Get("TodoBoardFilterTitle"),
            Strings.Get("TodoBoardFilterHint")));

        content.Children.Add(TodoBoardQueryDialogControls.SectionTitle(
            Strings.Get("TodoBoardStatus")));
        var statuses = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var pending = TodoBoardQueryDialogControls.CheckBox(
            Strings.Get("TodoBoardPending"),
            filters.Statuses.Contains(TodoBoardFilterStatuses.Pending));
        var done = TodoBoardQueryDialogControls.CheckBox(
            Strings.Get("TodoBoardDone"),
            filters.Statuses.Contains(TodoBoardFilterStatuses.Done));
        done.Margin = new Thickness(20, 0, 0, 0);
        statuses.Children.Add(pending);
        statuses.Children.Add(done);
        content.Children.Add(statuses);

        content.Children.Add(TodoBoardQueryDialogControls.SectionTitle(
            Strings.Get("TodoBoardPaper"),
            top: 17));
        var paperChecks = new List<(string Id, CheckBox CheckBox)>();
        var paperStack = new StackPanel();
        foreach (var paper in papers)
        {
            var check = TodoBoardQueryDialogControls.CheckBox(
                paper.Title,
                filters.PaperIds.Contains(paper.Id, StringComparer.Ordinal));
            check.Margin = new Thickness(0, 0, 0, 7);
            paperChecks.Add((paper.Id, check));
            paperStack.Children.Add(check);
        }
        if (paperChecks.Count == 0)
        {
            paperStack.Children.Add(TodoBoardQueryDialogControls.Hint(
                Strings.Get("TodoBoardFilterNoPapers")));
        }
        content.Children.Add(new Border
        {
            Background = Theme.Tint(12),
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 9, 10, 2),
            Margin = new Thickness(0, 7, 0, 0),
            MaxHeight = 130,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = paperStack
            }
        });

        content.Children.Add(TodoBoardQueryDialogControls.SectionTitle(
            Strings.Get("TodoBoardNote"),
            top: 17));
        var note = TodoBoardQueryDialogControls.ComboBox(
            [
                (TodoBoardNoteFilters.Any, Strings.Get("TodoBoardFilterAny")),
                (TodoBoardNoteFilters.WithNote, Strings.Get("TodoBoardFilterWithNote")),
                (TodoBoardNoteFilters.WithoutNote, Strings.Get("TodoBoardFilterWithoutNote"))
            ],
            TodoBoardNoteFilters.Normalize(filters.Note));
        note.Margin = new Thickness(0, 7, 0, 0);
        content.Children.Add(note);

        content.Children.Add(TodoBoardQueryDialogControls.SectionTitle(
            Strings.Get("TodoBoardFilterDateRanges"),
            top: 17));
        content.Children.Add(TodoBoardQueryDialogControls.Hint(
            Strings.Get("TodoBoardFilterDateHint")));
        var created = TodoBoardQueryDialogControls.DateRange(
            Strings.Get("TodoBoardCreated"),
            filters.CreatedFrom,
            filters.CreatedTo);
        var completed = TodoBoardQueryDialogControls.DateRange(
            Strings.Get("TodoBoardCompleted"),
            filters.CompletedFrom,
            filters.CompletedTo);
        var planned = TodoBoardQueryDialogControls.DateRange(
            Strings.Get("TodoBoardFilterPlannedRange"),
            filters.PlannedFrom,
            filters.PlannedTo);
        content.Children.Add(created.Root);
        content.Children.Add(completed.Root);
        content.Children.Add(planned.Root);

        var validation = TodoBoardQueryDialogControls.Validation();
        content.Children.Add(validation);
        var clear = TodoDialogControls.Button(Strings.Get("TodoBoardFilterClear"));
        clear.Click += (_, _) =>
        {
            pending.IsChecked = false;
            done.IsChecked = false;
            foreach (var option in paperChecks)
            {
                option.CheckBox.IsChecked = false;
            }
            note.SelectedIndex = 0;
            foreach (var input in new[]
                     {
                         created.From, created.To,
                         completed.From, completed.To,
                         planned.From, planned.To
                     })
            {
                input.Text = "";
            }
            validation.Visibility = Visibility.Collapsed;
        };
        var cancel = TodoDialogControls.Button(Strings.Get("CommonCancel"));
        cancel.IsCancel = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var apply = TodoDialogControls.Button(
            Strings.Get("TodoBoardApply"),
            primary: true);
        apply.IsDefault = true;
        apply.Click += (_, _) =>
        {
            if (!TryReadRange(created, out var createdFrom, out var createdTo) ||
                !TryReadRange(completed, out var completedFrom, out var completedTo) ||
                !TryReadRange(planned, out var plannedFrom, out var plannedTo))
            {
                ShowValidation(Strings.Get("TodoBoardFilterInvalidDate"));
                return;
            }
            if (!RangeIsValid(createdFrom, createdTo) ||
                !RangeIsValid(completedFrom, completedTo) ||
                !RangeIsValid(plannedFrom, plannedTo))
            {
                ShowValidation(Strings.Get("TodoBoardFilterInvalidRange"));
                return;
            }

            result = TodoBoardFilters.Normalize(new TodoBoardFilterState
            {
                Statuses =
                [
                    .. pending.IsChecked == true
                        ? [TodoBoardFilterStatuses.Pending]
                        : Array.Empty<string>(),
                    .. done.IsChecked == true
                        ? [TodoBoardFilterStatuses.Done]
                        : Array.Empty<string>()
                ],
                PaperIds = paperChecks
                    .Where(option => option.CheckBox.IsChecked == true)
                    .Select(option => option.Id)
                    .ToList(),
                CreatedFrom = createdFrom,
                CreatedTo = createdTo,
                CompletedFrom = completedFrom,
                CompletedTo = completedTo,
                PlannedFrom = plannedFrom,
                PlannedTo = plannedTo,
                Note = TodoBoardQueryDialogControls.SelectedKey(note)
            });
            dialog.DialogResult = true;
        };
        content.Children.Add(TodoBoardQueryDialogControls.Actions(
            clear,
            cancel,
            apply));

        var root = TodoBoardQueryDialogControls.Root(content);
        dialog.Content = root;
        TodoBoardQueryDialogControls.Animate(dialog, root, animate);
        if (dialog.ShowDialog() != true || result is null)
        {
            return false;
        }
        filters = result;
        return true;

        void ShowValidation(string message)
        {
            validation.Text = message;
            validation.Visibility = Visibility.Visible;
        }
    }

    private static bool TryReadRange(
        TodoBoardQueryDialogControls.DateRangeInputs inputs,
        out DateOnly? from,
        out DateOnly? to)
    {
        var fromValid = TryReadDate(inputs.From.Text, out from);
        var toValid = TryReadDate(inputs.To.Text, out to);
        return fromValid && toValid;
    }

    private static bool TryReadDate(string text, out DateOnly? value)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            value = null;
            return true;
        }
        if (DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }
        value = null;
        return false;
    }

    private static bool RangeIsValid(DateOnly? from, DateOnly? to) =>
        !from.HasValue || !to.HasValue || from.Value <= to.Value;
}

internal static class TodoBoardSortDialog
{
    private static readonly string[] Fields =
    [
        TodoBoardSortFields.Task,
        TodoBoardSortFields.Status,
        TodoBoardSortFields.Paper,
        TodoBoardSortFields.Created,
        TodoBoardSortFields.Completed,
        TodoBoardSortFields.PlannedStart,
        TodoBoardSortFields.Due,
        TodoBoardSortFields.Note
    ];

    public static bool TryShow(
        Window owner,
        IReadOnlyList<TodoBoardSortRule>? initial,
        bool animate,
        out List<TodoBoardSortRule> rules)
    {
        rules = TodoBoardSortRules.Normalize(initial);
        List<TodoBoardSortRule>? result = null;
        var working = TodoBoardSortRules.Normalize(initial);
        var dialog = TodoBoardQueryDialogControls.Window(
            owner,
            Strings.Get("TodoBoardSortTitle"),
            width: 560,
            height: 520);
        var content = new StackPanel();
        content.Children.Add(TodoBoardQueryDialogControls.Header(
            dialog,
            Strings.Get("TodoBoardSortTitle"),
            Strings.Get("TodoBoardSortHint")));
        var rows = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        content.Children.Add(rows);

        var add = TodoDialogControls.Button(Strings.Get("TodoBoardSortAdd"));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        add.Click += (_, _) =>
        {
            var field = Fields.FirstOrDefault(candidate =>
                working.All(rule => rule.Field != candidate));
            if (field is null)
            {
                return;
            }
            working.Add(new TodoBoardSortRule(field, false));
            RenderRows();
        };
        content.Children.Add(add);

        var reset = TodoDialogControls.Button(Strings.Get("TodoBoardSortDefault"));
        reset.Click += (_, _) =>
        {
            working.Clear();
            RenderRows();
        };
        var cancel = TodoDialogControls.Button(Strings.Get("CommonCancel"));
        cancel.IsCancel = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var apply = TodoDialogControls.Button(
            Strings.Get("TodoBoardApply"),
            primary: true);
        apply.IsDefault = true;
        apply.Click += (_, _) =>
        {
            result = TodoBoardSortRules.Normalize(working);
            dialog.DialogResult = true;
        };
        content.Children.Add(TodoBoardQueryDialogControls.Actions(
            reset,
            cancel,
            apply));

        var root = TodoBoardQueryDialogControls.Root(content);
        dialog.Content = root;
        TodoBoardQueryDialogControls.Animate(dialog, root, animate);
        RenderRows();
        if (dialog.ShowDialog() != true || result is null)
        {
            return false;
        }
        rules = result;
        return true;

        void RenderRows()
        {
            rows.Children.Clear();
            if (working.Count == 0)
            {
                rows.Children.Add(TodoBoardQueryDialogControls.Hint(
                    Strings.Get("TodoBoardSortDefault")));
            }
            for (var index = 0; index < working.Count; index++)
            {
                var rowIndex = index;
                var rule = working[index];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var priority = new TextBlock
                {
                    Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = Theme.WeakTextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var field = TodoBoardQueryDialogControls.ComboBox(
                    Fields.Select(value => (value, FieldLabel(value))).ToArray(),
                    rule.Field);
                field.Margin = new Thickness(0, 0, 8, 0);
                field.SelectionChanged += (_, _) =>
                {
                    var selected = TodoBoardQueryDialogControls.SelectedKey(field);
                    var duplicate = working.FindIndex(candidate =>
                        candidate.Field == selected);
                    if (duplicate >= 0 && duplicate != rowIndex)
                    {
                        working[duplicate] = working[duplicate] with { Field = rule.Field };
                    }
                    working[rowIndex] = working[rowIndex] with { Field = selected };
                    RenderRows();
                };
                var direction = TodoBoardQueryDialogControls.ComboBox(
                    [
                        ("asc", Strings.Get("TodoBoardSortAscending")),
                        ("desc", Strings.Get("TodoBoardSortDescending"))
                    ],
                    rule.Descending ? "desc" : "asc");
                direction.Margin = new Thickness(0, 0, 8, 0);
                direction.SelectionChanged += (_, _) =>
                {
                    working[rowIndex] = working[rowIndex] with
                    {
                        Descending = TodoBoardQueryDialogControls.SelectedKey(direction) == "desc"
                    };
                };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(SmallButton("↑", "TodoBoardSortMoveUp", () =>
                {
                    if (rowIndex <= 0)
                    {
                        return;
                    }
                    (working[rowIndex - 1], working[rowIndex]) =
                        (working[rowIndex], working[rowIndex - 1]);
                    RenderRows();
                }));
                actions.Children.Add(SmallButton("↓", "TodoBoardSortMoveDown", () =>
                {
                    if (rowIndex >= working.Count - 1)
                    {
                        return;
                    }
                    (working[rowIndex + 1], working[rowIndex]) =
                        (working[rowIndex], working[rowIndex + 1]);
                    RenderRows();
                }));
                actions.Children.Add(SmallButton("×", "TodoBoardSortRemove", () =>
                {
                    working.RemoveAt(rowIndex);
                    RenderRows();
                }));
                Grid.SetColumn(field, 1);
                Grid.SetColumn(direction, 2);
                Grid.SetColumn(actions, 3);
                row.Children.Add(priority);
                row.Children.Add(field);
                row.Children.Add(direction);
                row.Children.Add(actions);
                rows.Children.Add(row);
            }
            add.IsEnabled = working.Count < Fields.Length;
        }

        Button SmallButton(string text, string toolTipKey, Action action)
        {
            var button = TodoDialogControls.Button(text, compact: true);
            button.ToolTip = Strings.Get(toolTipKey);
            button.Margin = new Thickness(3, 0, 0, 0);
            button.Click += (_, _) => action();
            return button;
        }
    }

    internal static string FieldLabel(string field) => field switch
    {
        TodoBoardSortFields.Task => Strings.Get("TodoBoardTask"),
        TodoBoardSortFields.Status => Strings.Get("TodoBoardStatus"),
        TodoBoardSortFields.Paper => Strings.Get("TodoBoardPaper"),
        TodoBoardSortFields.Created => Strings.Get("TodoBoardCreated"),
        TodoBoardSortFields.Completed => Strings.Get("TodoBoardCompleted"),
        TodoBoardSortFields.PlannedStart => Strings.Get("TodoPlanningStartDate"),
        TodoBoardSortFields.Due => Strings.Get("TodoPlanningDueDate"),
        TodoBoardSortFields.Note => Strings.Get("TodoBoardNote"),
        _ => field
    };
}

internal static class TodoBoardQueryDialogControls
{
    internal sealed record DateRangeInputs(Grid Root, TextBox From, TextBox To);

    public static Window Window(
        Window owner,
        string title,
        double width,
        double height)
    {
        var ownerWorkArea = WindowWorkAreaHelper.WorkAreaFor(owner);
        var availableWidth = Math.Max(360, ownerWorkArea.Width - 32);
        var availableHeight = Math.Max(360, ownerWorkArea.Height - 32);
        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            Width = Math.Min(width, availableWidth),
            Height = Math.Min(height, availableHeight),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = owner.Topmost,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(12),
            Language = AppTypography.Language,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        AppTypography.ApplyTextRendering(dialog);
        return dialog;
    }

    public static Border Root(UIElement content) => new()
    {
        Margin = new Thickness(10),
        Padding = new Thickness(18, 15, 18, 16),
        Background = Theme.PaperBrush,
        BorderBrush = Theme.PaperBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Effect = new DropShadowEffect
        {
            BlurRadius = 20,
            ShadowDepth = 3,
            Opacity = Theme.IsDark ? 0.34 : 0.2
        },
        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        }
    };

    public static UIElement Header(Window dialog, string title, string hint)
    {
        var close = TodoDialogControls.Button("×", compact: true);
        close.IsCancel = true;
        close.Click += (_, _) => dialog.DialogResult = false;
        var titleText = new TextBlock
        {
            Text = title,
            Foreground = Theme.TextBrush,
            FontSize = AppTypography.Scale(14),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(close, 1);
        header.Children.Add(titleText);
        header.Children.Add(close);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(Hint(hint));
        var drag = new Border
        {
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(-4, -4, -4, 0),
            Padding = new Thickness(4, 4, 4, 8),
            Child = stack
        };
        drag.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || close.IsMouseOver)
            {
                return;
            }
            try
            {
                dialog.DragMove();
            }
            catch (InvalidOperationException)
            {
                // Native moving can release the pointer before WPF observes it.
            }
        };
        return drag;
    }

    public static TextBlock SectionTitle(string text, double top = 8) => new()
    {
        Text = text,
        Foreground = Theme.TextBrush,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, top, 0, 0)
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Theme.WeakTextBrush,
        FontSize = AppTypography.Scale(10.5),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
    };

    public static CheckBox CheckBox(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value,
        Foreground = Theme.TextBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static ComboBox ComboBox(
        IReadOnlyList<(string Key, string Label)> options,
        string selectedKey)
    {
        var combo = new ComboBox
        {
            Foreground = Theme.TextBrush,
            Background = Theme.Tint(16),
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 4, 7, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        foreach (var option in options)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Key,
                Foreground = Theme.TextBrush,
                Background = Theme.PaperBrush,
                Padding = new Thickness(7, 4, 7, 4)
            });
        }
        combo.SelectedIndex = Math.Max(
            0,
            options
                .Select((option, index) => (option.Key, index))
                .FirstOrDefault(pair => pair.Key == selectedKey)
                .index);
        return combo;
    }

    public static string SelectedKey(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string key } ? key : "";

    public static DateRangeInputs DateRange(
        string label,
        DateOnly? from,
        DateOnly? to)
    {
        var root = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        var labelText = new TextBlock
        {
            Text = label,
            Foreground = Theme.WeakTextBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var fromInput = DateInput(from);
        var arrow = new TextBlock
        {
            Text = "→",
            Foreground = Theme.WeakTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var toInput = DateInput(to);
        Grid.SetColumn(fromInput, 1);
        Grid.SetColumn(arrow, 2);
        Grid.SetColumn(toInput, 3);
        root.Children.Add(labelText);
        root.Children.Add(fromInput);
        root.Children.Add(arrow);
        root.Children.Add(toInput);
        return new DateRangeInputs(root, fromInput, toInput);
    }

    public static TextBlock Validation() => new()
    {
        Foreground = Theme.DangerBrush,
        FontSize = AppTypography.Scale(10.8),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 10, 0, 0)
    };

    public static UIElement Actions(Button left, Button cancel, Button apply)
    {
        var root = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        apply.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(cancel);
        right.Children.Add(apply);
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);
        return root;
    }

    public static void Animate(Window dialog, Border root, bool animate)
    {
        if (!animate)
        {
            return;
        }
        root.Opacity = 0;
        dialog.ContentRendered += (_, _) => AnimationHelper.FadeIn(root, duration: 110);
    }

    private static TextBox DateInput(DateOnly? value) => new()
    {
        Text = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        MaxLength = 10,
        Foreground = Theme.TextBrush,
        Background = Theme.Tint(16),
        BorderBrush = Theme.PaperBorderBrush,
        BorderThickness = new Thickness(1),
        CaretBrush = Theme.TextBrush,
        Padding = new Thickness(8, 5, 8, 5),
        VerticalContentAlignment = VerticalAlignment.Center
    };
}
