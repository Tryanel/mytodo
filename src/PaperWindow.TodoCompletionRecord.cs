using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private const int TodoCompletionRecordPromptDurationMilliseconds = 6500;
    private readonly TodoCompletionRecordPromptSession _todoCompletionRecordPrompt = new();
    private Border? _todoCompletionRecordPromptHost;
    private DispatcherTimer? _todoCompletionRecordPromptTimer;
    private int _todoCompletionRecordPromptGeneration;

    internal string? TodoCompletionRecordPromptItemId =>
        _todoCompletionRecordPrompt.ItemId;

    private void ShowTodoCompletionRecordPrompt(
        PaperItem item,
        bool wasDone,
        TodoCompletionRecordOrigin origin)
    {
        DismissTodoCompletionRecordPrompt(animate: false);
        var itemRemainsAvailable =
            _windowLifecycle == PaperWindowLifecycleState.Alive &&
            _paper.IsVisible &&
            !_paper.IsCollapsed &&
            IsVisible &&
            !HasTodoNoteEditorForDifferentItem(item.Id) &&
            _paper.Items.Any(candidate =>
                string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
        if (!_todoCompletionRecordPrompt.TryOffer(
                item.Id,
                wasDone,
                item.Done,
                itemRemainsAvailable,
                origin))
        {
            return;
        }

        var row = _todoRows.FirstOrDefault(candidate =>
            candidate.Tag is string itemId &&
            string.Equals(itemId, item.Id, StringComparison.Ordinal));
        if (row?.Child is not Grid rowGrid)
        {
            _todoCompletionRecordPrompt.Dismiss();
            return;
        }

        var label = new TextBlock
        {
            Text = Strings.Get("TodoCompletionRecordAction"),
            Foreground = TextBrush,
            FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(11),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = AppTypography.Scale(150)
        };
        var glyph = new TextBlock
        {
            Text = "✎",
            Foreground = WeakTextBrush,
            FontFamily = AppTypography.SymbolFontFamily,
            FontSize = AppTypography.Scale(11),
            Margin = new Thickness(0, 0, AppTypography.Scale(4), 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(glyph);
        content.Children.Add(label);

        var host = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(RadiusControl),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.PaperBorderBrush,
            Background = Theme.Tint(38),
            Cursor = Cursors.Hand,
            Focusable = false,
            ToolTip = Strings.Get("TodoCompletionRecordToolTip"),
            Child = content
        };
        AutomationProperties.SetName(
            host,
            Strings.Get("TodoCompletionRecordAction"));
        ToolTipService.SetIsEnabled(host, _controller.State.EnableToolTips);
        Panel.SetZIndex(host, 20);
        Grid.SetColumn(host, 1);

        host.MouseEnter += (_, _) =>
        {
            host.Background = Theme.Tint(52);
            glyph.Foreground = TextBrush;
        };
        host.MouseLeave += (_, _) =>
        {
            host.Background = Theme.Tint(38);
            glyph.Foreground = WeakTextBrush;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            if (_todoCompletionRecordPrompt.DismissIfInvalid(itemId =>
                    _paper.Items.Any(candidate =>
                        candidate.Done &&
                        string.Equals(
                            candidate.Id,
                            itemId,
                            StringComparison.Ordinal))))
            {
                DismissTodoCompletionRecordPrompt(animate: false);
                e.Handled = true;
                return;
            }

            var itemId = _todoCompletionRecordPrompt.ItemId;
            DismissTodoCompletionRecordPrompt(animate: false);
            var currentItem = _paper.Items.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
            if (currentItem?.Done == true)
            {
                EditTodoNote(currentItem);
            }
            e.Handled = true;
        };
        host.Unloaded += (_, _) =>
        {
            if (ReferenceEquals(_todoCompletionRecordPromptHost, host))
            {
                DismissTodoCompletionRecordPrompt(animate: false);
            }
        };

        _todoCompletionRecordPromptHost = host;
        rowGrid.Children.Add(host);
        var generation = ++_todoCompletionRecordPromptGeneration;
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(
                TodoCompletionRecordPromptDurationMilliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation == _todoCompletionRecordPromptGeneration)
            {
                DismissTodoCompletionRecordPrompt(
                    animate: _controller.State.EnableAnimations);
            }
        };
        _todoCompletionRecordPromptTimer = timer;
        timer.Start();

        if (_controller.State.EnableAnimations)
        {
            host.Opacity = 0;
            AnimationHelper.GetTranslateTransform(host).Y = AppTypography.Scale(3);
            AnimationHelper.FadeIn(host, 140);
            AnimationHelper.TranslateTo(
                host,
                0,
                0,
                160,
                AnimationHelper.QuickEase);
        }
    }

    private void DismissTodoCompletionRecordPromptForItem(
        string itemId,
        bool animate = false)
    {
        if (_todoCompletionRecordPrompt.IsCurrent(itemId))
        {
            DismissTodoCompletionRecordPrompt(animate);
        }
    }

    private void DismissTodoCompletionRecordPrompt(bool animate = false)
    {
        _todoCompletionRecordPromptTimer?.Stop();
        _todoCompletionRecordPromptTimer = null;
        _todoCompletionRecordPrompt.Dismiss();
        _todoCompletionRecordPromptGeneration++;

        var host = _todoCompletionRecordPromptHost;
        _todoCompletionRecordPromptHost = null;
        if (host == null)
        {
            return;
        }

        void RemoveHost()
        {
            host.BeginAnimation(OpacityProperty, null);
            if (host.Parent is Panel parent)
            {
                parent.Children.Remove(host);
            }
        }

        if (animate && host.IsLoaded)
        {
            AnimationHelper.FadeOut(
                host,
                120,
                (_, _) => RemoveHost());
        }
        else
        {
            RemoveHost();
        }
    }
}
