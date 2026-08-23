using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PaperTodo;

internal static class TodoNoteDialog
{
    public static bool TryEdit(
        Window owner,
        string currentNote,
        out string note)
    {
        var accepted = false;
        var result = currentNote ?? "";
        var dialog = new Window
        {
            Owner = owner,
            Title = Strings.Get("TodoNoteTitle"),
            Width = 440,
            Height = 340,
            MinWidth = 360,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
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
            Background = Theme.PaperBrush,
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.22
            }
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(2, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = Strings.Get("TodoNoteTitle"),
            Foreground = Theme.TextBrush,
            FontSize = AppTypography.Scale(16),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = CreateButton("×", subtle: true);
        close.MinWidth = 34;
        close.IsCancel = true;
        close.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetColumn(close, 1);
        header.Children.Add(title);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || close.IsMouseOver)
            {
                return;
            }
            try { dialog.DragMove(); } catch (InvalidOperationException) { }
        };

        var editor = new TextBox
        {
            Text = currentNote ?? "",
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxLength = PaperWindow.TodoNoteMaxLength,
            Padding = new Thickness(10),
            BorderBrush = Theme.PaperBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Theme.Tint(12),
            Foreground = Theme.TextBrush,
            CaretBrush = Theme.TextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(13)
        };

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var clear = CreateButton(Strings.Get("TodoNoteClear"), subtle: true);
        clear.IsEnabled = !string.IsNullOrWhiteSpace(currentNote);
        clear.Click += (_, _) =>
        {
            accepted = true;
            result = "";
            dialog.DialogResult = true;
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = CreateButton(Strings.Get("CommonCancel"), subtle: true);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = CreateButton(Strings.Get("CommonSave"));
        save.Margin = new Thickness(8, 0, 0, 0);
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            accepted = true;
            result = editor.Text;
            dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetColumn(actions, 1);
        buttons.Children.Add(clear);
        buttons.Children.Add(actions);

        Grid.SetRow(editor, 1);
        Grid.SetRow(buttons, 2);
        layout.Children.Add(header);
        layout.Children.Add(editor);
        layout.Children.Add(buttons);
        root.Child = layout;
        dialog.Content = root;
        dialog.ContentRendered += (_, _) =>
        {
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        };

        _ = dialog.ShowDialog();
        note = result;
        return accepted;
    }

    private static Button CreateButton(string text, bool subtle = false)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, subtle ? Brushes.Transparent : Theme.Tint(34)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.TextBrush));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 72.0));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Tint(50)));
        template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return new Button { Content = text, Style = style };
    }
}
