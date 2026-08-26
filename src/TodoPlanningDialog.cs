using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PaperTodo;

internal static class TodoPlanningDialog
{
    public static bool TryShow(
        Window owner,
        DateOnly? initialPlannedStartDate,
        DateOnly? initialDueDate,
        bool animate,
        out DateOnly? plannedStartDate,
        out DateOnly? dueDate)
    {
        plannedStartDate = null;
        dueDate = null;
        (DateOnly? Start, DateOnly? Due)? result = null;

        var dialog = new Window
        {
            Owner = owner,
            Title = Strings.Get("TodoPlanningTitle"),
            Width = 408,
            SizeToContent = SizeToContent.Height,
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

        var root = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(16, 14, 16, 15),
            Background = Theme.PaperBrush,
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 3,
                Opacity = Theme.IsDark ? 0.34 : 0.2
            }
        };
        var content = new StackPanel();

        var close = TodoDialogControls.Button("×", compact: true);
        close.IsCancel = true;
        close.Click += (_, _) => dialog.DialogResult = false;
        var title = new TextBlock
        {
            Text = Strings.Get("TodoPlanningTitle"),
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
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(title);
        header.Children.Add(close);
        var hint = new TextBlock
        {
            Text = Strings.Get("TodoPlanningHint"),
            Foreground = Theme.WeakTextBrush,
            FontSize = AppTypography.Scale(10.8),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var heading = new StackPanel();
        heading.Children.Add(header);
        heading.Children.Add(hint);
        var dragSurface = new Border
        {
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(-4, -4, -4, 4),
            Padding = new Thickness(4, 4, 4, 8),
            Child = heading
        };
        dragSurface.MouseLeftButtonDown += (_, e) =>
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
                // Windows may release the pointer while native moving starts.
            }
        };
        content.Children.Add(dragSurface);

        var startInput = CreateDateInput(initialPlannedStartDate);
        var startClear = TodoDialogControls.Button(
            Strings.Get("TodoPlanningClear"),
            compact: true);
        startClear.MinWidth = 58;
        startClear.Click += (_, _) => startInput.Text = "";
        content.Children.Add(BuildDateField(
            Strings.Get("TodoPlanningStartDate"),
            startInput,
            startClear));

        var dueInput = CreateDateInput(initialDueDate);
        var dueClear = TodoDialogControls.Button(
            Strings.Get("TodoPlanningClear"),
            compact: true);
        dueClear.MinWidth = 58;
        dueClear.Click += (_, _) => dueInput.Text = "";
        var dueField = BuildDateField(
            Strings.Get("TodoPlanningDueDate"),
            dueInput,
            dueClear);
        dueField.Margin = new Thickness(0, 9, 0, 0);
        content.Children.Add(dueField);

        var validation = new TextBlock
        {
            Foreground = Theme.DangerBrush,
            FontSize = AppTypography.Scale(10.8),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(2, 9, 2, 0)
        };
        content.Children.Add(validation);

        var cancel = TodoDialogControls.Button(
            Strings.Get("CommonCancel"));
        cancel.IsCancel = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var confirm = TodoDialogControls.Button(
            Strings.Get("CommonSave"),
            primary: true);
        confirm.IsDefault = true;
        confirm.Margin = new Thickness(8, 0, 0, 0);
        confirm.Click += (_, _) =>
        {
            if (!TryReadDate(startInput.Text, out var start) ||
                !TryReadDate(dueInput.Text, out var due))
            {
                ShowValidation(Strings.Get("TodoPlanningInvalidDate"));
                return;
            }
            if (!PaperItem.IsPlanningRangeValid(start, due))
            {
                ShowValidation(Strings.Get("TodoPlanningInvalidRange"));
                return;
            }

            result = (start, due);
            dialog.DialogResult = true;
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        content.Children.Add(actions);

        root.Child = content;
        dialog.Content = root;
        if (animate)
        {
            root.Opacity = 0;
            dialog.ContentRendered += (_, _) =>
                AnimationHelper.FadeIn(root, duration: 110);
        }

        startInput.Focus();
        startInput.SelectAll();
        if (dialog.ShowDialog() != true || result is not { } acceptedResult)
        {
            return false;
        }

        plannedStartDate = acceptedResult.Start;
        dueDate = acceptedResult.Due;
        return true;

        void ShowValidation(string message)
        {
            validation.Text = message;
            validation.Visibility = Visibility.Visible;
        }
    }

    private static TextBox CreateDateInput(DateOnly? value)
    {
        var input = new TextBox
        {
            Text = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MaxLength = 10,
            Foreground = Theme.TextBrush,
            Background = Theme.Tint(16),
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CaretBrush = Theme.TextBrush,
            Padding = new Thickness(9, 6, 9, 6),
            FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(12.5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        return input;
    }

    private static Grid BuildDateField(
        string labelText,
        TextBox input,
        Button clear)
    {
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        field.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = labelText,
            Foreground = Theme.WeakTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0)
        };
        AutomationProperties.SetName(input, labelText);
        clear.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(input, 1);
        Grid.SetColumn(clear, 2);
        field.Children.Add(label);
        field.Children.Add(input);
        field.Children.Add(clear);
        return field;
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
}
