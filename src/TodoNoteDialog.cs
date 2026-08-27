using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PaperTodo;

/// <summary>
/// A modeless, owner-independent surface for one todo paper's note editing session. It is not a
/// WPF owned window because owned windows are hidden together with their paper; PaperWindow still
/// owns the session lifetime and supplies stable-id persistence.
/// </summary>
internal sealed class TodoNoteDialog : Window
{
    private readonly TodoNoteEditorSession _session;
    private readonly Func<string, string, bool> _saveNote;
    private readonly Border _root;
    private readonly TextBlock _heading;
    private readonly TextBlock _dirtyStatus;
    private readonly TextBlock _taskIdentity;
    private readonly TextBox _editor;
    private readonly Border _decisionHost;
    private readonly TextBlock _decisionMessage;
    private readonly TextBlock _errorMessage;
    private readonly Button _decisionSave;
    private readonly Button _decisionDiscard;
    private readonly Button _decisionCancel;
    private readonly Button _decisionClose;
    private readonly Button _clear;
    private readonly Button _cancel;
    private readonly Button _save;
    private readonly Dictionary<Button, bool> _buttons = new();
    private bool _allowClose;
    private bool _applyingTarget;
    private bool _invalidated;
    private bool _stageDestructiveDecision;
    private Action<TodoNoteDraftResolution>? _destructiveDecisionCallback;
    private int _preservedSelectionStart;
    private int _preservedSelectionLength;

    private TodoNoteDialog(
        Window placementOwner,
        TodoNoteEditorTarget target,
        Func<string, string, bool> saveNote)
    {
        _session = new TodoNoteEditorSession(target);
        _saveNote = saveNote;

        Width = 460;
        Height = 390;
        MinWidth = 380;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = placementOwner.Topmost;
        FontFamily = AppTypography.UiFontFamily;
        FontSize = AppTypography.Scale(12);
        Language = AppTypography.Language;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        AppTypography.ApplyTextRendering(this);
        PlaceNear(placementOwner);

        _root = new Border
        {
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
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(2, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _heading = new TextBlock
        {
            FontSize = AppTypography.Scale(16),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _dirtyStatus = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var close = CreateButton("×", subtle: true);
        close.MinWidth = 34;
        close.Click += (_, _) => Close();
        Grid.SetColumn(_dirtyStatus, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(_heading);
        header.Children.Add(_dirtyStatus);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || close.IsMouseOver)
            {
                return;
            }
            try { DragMove(); } catch (InvalidOperationException) { }
        };

        _taskIdentity = new TextBlock
        {
            Margin = new Thickness(2, 0, 2, 10),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxLength = PaperWindow.TodoNoteMaxLength,
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(13)
        };
        AutomationProperties.SetName(_editor, Strings.Get("TodoNoteTitle"));

        _decisionMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _errorMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var decisionActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _decisionSave = CreateButton(Strings.Get("CommonSave"));
        _decisionDiscard = CreateButton("", subtle: true);
        _decisionDiscard.Margin = new Thickness(8, 0, 0, 0);
        _decisionCancel = CreateButton("", subtle: true);
        _decisionCancel.Margin = new Thickness(8, 0, 0, 0);
        _decisionClose = CreateButton(Strings.Get("CommonClose"));
        _decisionClose.Margin = new Thickness(8, 0, 0, 0);
        _decisionClose.Visibility = Visibility.Collapsed;
        _decisionSave.Click += (_, _) => ResolvePending(TodoNoteDraftResolution.Save);
        _decisionDiscard.Click += (_, _) => ResolvePending(TodoNoteDraftResolution.Discard);
        _decisionCancel.Click += (_, _) => ResolvePending(TodoNoteDraftResolution.Cancel);
        _decisionClose.Click += (_, _) => ForceClose();
        decisionActions.Children.Add(_decisionSave);
        decisionActions.Children.Add(_decisionDiscard);
        decisionActions.Children.Add(_decisionCancel);
        decisionActions.Children.Add(_decisionClose);
        var decisionLayout = new StackPanel();
        decisionLayout.Children.Add(_decisionMessage);
        decisionLayout.Children.Add(decisionActions);
        decisionLayout.Children.Add(_errorMessage);
        _decisionHost = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = decisionLayout,
            Visibility = Visibility.Collapsed
        };

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _clear = CreateButton(Strings.Get("TodoNoteClear"), subtle: true);
        _clear.Click += (_, _) =>
        {
            _editor.Text = "";
            _editor.Focus();
        };
        var regularActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _cancel = CreateButton(Strings.Get("CommonCancel"), subtle: true);
        _cancel.Click += (_, _) => Close();
        _save = CreateButton(Strings.Get("CommonSave"));
        _save.Margin = new Thickness(8, 0, 0, 0);
        _save.IsDefault = true;
        _save.Click += (_, _) => SaveAndClose();
        regularActions.Children.Add(_cancel);
        regularActions.Children.Add(_save);
        Grid.SetColumn(regularActions, 1);
        buttons.Children.Add(_clear);
        buttons.Children.Add(regularActions);

        Grid.SetRow(_taskIdentity, 1);
        Grid.SetRow(_editor, 2);
        Grid.SetRow(_decisionHost, 3);
        Grid.SetRow(buttons, 4);
        layout.Children.Add(header);
        layout.Children.Add(_taskIdentity);
        layout.Children.Add(_editor);
        layout.Children.Add(_decisionHost);
        layout.Children.Add(buttons);
        _root.Child = layout;
        Content = _root;

        _editor.TextChanged += (_, _) =>
        {
            if (_applyingTarget)
            {
                return;
            }
            _session.SetDraft(_editor.Text);
            RefreshIdentityText();
            _clear.IsEnabled = _editor.Text.Length > 0;
            HideError();
        };
        ApplyTargetToSurface();
        RefreshTheme();
        ContentRendered += (_, _) =>
        {
            _editor.Focus();
            _editor.CaretIndex = _editor.Text.Length;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnDialogClosing;
    }

    public static TodoNoteDialog Create(
        Window placementOwner,
        TodoNoteEditorTarget target,
        Func<string, string, bool> saveNote) =>
        new(placementOwner, target, saveNote);

    public string ItemId => _session.Target.ItemId;
    public bool IsDirty => _session.IsDirty;
    public TodoNoteDraftIntent PendingIntent => _session.PendingIntent;
    internal TextBox Editor => _editor;

    public void RequestTarget(TodoNoteEditorTarget target)
    {
        if (_invalidated || _destructiveDecisionCallback != null)
        {
            ShowAndActivate();
            return;
        }
        _session.SetDraft(_editor.Text);
        var transition = _session.RequestSwitch(target);
        ApplyTransition(transition);
        ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }
        Activate();
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    public void RefreshTopmost(bool topmost)
    {
        Topmost = topmost;
    }

    public void RefreshTheme()
    {
        _root.Background = Theme.PaperBrush;
        _root.BorderBrush = Theme.PaperBorderBrush;
        _heading.Foreground = Theme.TextBrush;
        _dirtyStatus.Foreground = Theme.ActiveBrush;
        _taskIdentity.Foreground = Theme.WeakTextBrush;
        _editor.BorderBrush = Theme.PaperBorderBrush;
        _editor.Background = Theme.Tint(12);
        _editor.Foreground = Theme.TextBrush;
        _editor.CaretBrush = Theme.TextBrush;
        _decisionHost.Background = Theme.Tint(18);
        _decisionHost.BorderBrush = Theme.PaperBorderBrush;
        _decisionMessage.Foreground = Theme.TextBrush;
        _errorMessage.Foreground = Theme.DangerBrush;
        foreach (var (button, subtle) in _buttons)
        {
            button.Style = CreateButtonStyle(subtle);
        }
    }

    public void RequestDestructiveAction(
        TodoNoteDraftIntent intent,
        bool stageDecision,
        Action<TodoNoteDraftResolution> resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (_invalidated)
        {
            resolved(TodoNoteDraftResolution.Cancel);
            return;
        }
        if (_destructiveDecisionCallback != null)
        {
            ShowAndActivate();
            resolved(TodoNoteDraftResolution.Cancel);
            return;
        }

        _session.SetDraft(_editor.Text);
        var transition = _session.RequestDestructive(intent);
        if (transition == TodoNoteSessionTransition.DestructiveActionApproved)
        {
            ForceClose();
            resolved(TodoNoteDraftResolution.Discard);
            return;
        }

        _stageDestructiveDecision = stageDecision;
        _destructiveDecisionCallback = resolved;
        ApplyTransition(transition);
        ShowAndActivate();
    }

    public bool CommitStagedDestructiveDecision(
        TodoNoteDraftResolution resolution)
    {
        if (_destructiveDecisionCallback == null || !_stageDestructiveDecision)
        {
            return false;
        }

        _session.ResolvePending(resolution);
        _destructiveDecisionCallback = null;
        _stageDestructiveDecision = false;
        ForceClose();
        return true;
    }

    public bool PrepareStagedDestructiveDecision(
        TodoNoteDraftResolution resolution)
    {
        return _destructiveDecisionCallback != null &&
            _stageDestructiveDecision &&
            _session.PendingIntent == TodoNoteDraftIntent.Exit &&
            resolution != TodoNoteDraftResolution.Cancel;
    }

    public bool TryGetStagedSave(out string itemId, out string draft)
    {
        itemId = "";
        draft = "";
        if (_destructiveDecisionCallback == null ||
            !_stageDestructiveDecision ||
            _session.PendingIntent != TodoNoteDraftIntent.Exit)
        {
            return false;
        }

        itemId = _session.Target.ItemId;
        draft = _session.Draft;
        return true;
    }

    public void CancelStagedDestructiveDecision()
    {
        if (_destructiveDecisionCallback == null || !_stageDestructiveDecision)
        {
            return;
        }
        _session.ResolvePending(TodoNoteDraftResolution.Cancel);
        _destructiveDecisionCallback = null;
        _stageDestructiveDecision = false;
        HideDecision(restoreEditorFocus: true);
    }

    public void Invalidate(TodoNoteInvalidationReason reason)
    {
        if (_invalidated)
        {
            return;
        }

        var callback = _destructiveDecisionCallback;
        _destructiveDecisionCallback = null;
        _stageDestructiveDecision = false;
        if (_session.PendingIntent != TodoNoteDraftIntent.None)
        {
            _session.ResolvePending(TodoNoteDraftResolution.Cancel);
        }

        _invalidated = true;
        _editor.IsReadOnly = true;
        _clear.IsEnabled = false;
        _cancel.IsEnabled = false;
        _save.IsEnabled = false;
        HideError();
        _decisionMessage.Text = Strings.Get(
            reason == TodoNoteInvalidationReason.PaperDeleted
                ? "TodoNotePaperInvalidated"
                : "TodoNoteTaskInvalidated");
        _decisionMessage.Visibility = Visibility.Visible;
        _decisionSave.Visibility = Visibility.Collapsed;
        _decisionDiscard.Visibility = Visibility.Collapsed;
        _decisionCancel.Visibility = Visibility.Collapsed;
        _decisionClose.Visibility = Visibility.Visible;
        _decisionHost.Visibility = Visibility.Visible;
        _dirtyStatus.Text = Strings.Get("TodoNoteInvalidatedStatus");
        _dirtyStatus.Visibility = Visibility.Visible;
        Title = Strings.Get("TodoNoteInvalidatedWindowTitle");
        ShowAndActivate();
        callback?.Invoke(TodoNoteDraftResolution.Cancel);
    }

    private void SaveAndClose()
    {
        _session.SetDraft(_editor.Text);
        if (!TrySaveCurrent())
        {
            return;
        }
        _allowClose = true;
        Close();
    }

    private bool TrySaveCurrent()
    {
        if (!_session.IsDirty)
        {
            return true;
        }
        if (!_saveNote(_session.Target.ItemId, _session.Draft))
        {
            ShowError();
            return false;
        }
        _session.MarkSaved();
        RefreshIdentityText();
        return true;
    }

    private void ResolvePending(TodoNoteDraftResolution resolution)
    {
        if (_destructiveDecisionCallback != null)
        {
            ResolveDestructiveDecision(resolution);
            return;
        }
        if (resolution == TodoNoteDraftResolution.Save && !TrySaveCurrent())
        {
            return;
        }
        ApplyTransition(_session.ResolvePending(resolution));
    }

    private void ResolveDestructiveDecision(TodoNoteDraftResolution resolution)
    {
        var callback = _destructiveDecisionCallback;
        if (callback == null)
        {
            return;
        }

        if (resolution == TodoNoteDraftResolution.Cancel)
        {
            _session.ResolvePending(resolution);
            _destructiveDecisionCallback = null;
            _stageDestructiveDecision = false;
            HideDecision(restoreEditorFocus: true);
            callback(resolution);
            return;
        }

        if (_stageDestructiveDecision)
        {
            _decisionMessage.Text = Strings.Get("TodoNoteExitDecisionStaged");
            _decisionSave.Visibility = Visibility.Collapsed;
            _decisionDiscard.Visibility = Visibility.Collapsed;
            _decisionCancel.Visibility = Visibility.Collapsed;
            callback(resolution);
            return;
        }

        if (resolution == TodoNoteDraftResolution.Save && !TrySaveCurrent())
        {
            return;
        }
        _session.ResolvePending(resolution);
        _destructiveDecisionCallback = null;
        ForceClose();
        callback(resolution);
    }

    private void ApplyTransition(TodoNoteSessionTransition transition)
    {
        switch (transition)
        {
            case TodoNoteSessionTransition.TargetChanged:
                HideDecision(restoreEditorFocus: false);
                ApplyTargetToSurface();
                _editor.Focus();
                _editor.CaretIndex = _editor.Text.Length;
                break;
            case TodoNoteSessionTransition.DecisionRequired:
                ShowDecision();
                break;
            case TodoNoteSessionTransition.Close:
                _allowClose = true;
                Close();
                break;
            case TodoNoteSessionTransition.Reactivate:
                HideDecision(restoreEditorFocus: true);
                break;
            default:
                HideDecision(restoreEditorFocus: true);
                break;
        }
    }

    private void ApplyTargetToSurface()
    {
        _applyingTarget = true;
        try
        {
            _editor.Text = _session.Draft;
            _editor.IsReadOnly = false;
            _clear.IsEnabled = _editor.Text.Length > 0;
        }
        finally
        {
            _applyingTarget = false;
        }
        RefreshIdentityText();
        HideError();
    }

    private void RefreshIdentityText()
    {
        var task = DisplayTaskText(_session.Target.TaskText);
        _heading.Text = Strings.Get("TodoNoteTitle");
        _taskIdentity.Text = Strings.Format("TodoNoteTaskIdentity", task);
        _dirtyStatus.Text = Strings.Get("TodoNoteDirtyStatus");
        _dirtyStatus.Visibility = _session.IsDirty
            ? Visibility.Visible
            : Visibility.Collapsed;
        Title = Strings.Format(
            _session.IsDirty ? "TodoNoteDirtyWindowTitle" : "TodoNoteWindowTitle",
            task);
    }

    private void ShowDecision()
    {
        _preservedSelectionStart = _editor.SelectionStart;
        _preservedSelectionLength = _editor.SelectionLength;
        _editor.IsReadOnly = true;
        _clear.IsEnabled = false;
        _cancel.IsEnabled = false;
        _save.IsEnabled = false;
        ConfigureDecisionText();
        _decisionSave.Visibility = Visibility.Visible;
        _decisionDiscard.Visibility = Visibility.Visible;
        _decisionCancel.Visibility = Visibility.Visible;
        _decisionClose.Visibility = Visibility.Collapsed;
        _decisionHost.Visibility = Visibility.Visible;
        HideError();
        _decisionSave.Focus();
    }

    private void ConfigureDecisionText()
    {
        var task = DisplayTaskText(_session.Target.TaskText);
        switch (_session.PendingIntent)
        {
            case TodoNoteDraftIntent.SwitchTarget:
                _decisionMessage.Text = Strings.Format(
                    "TodoNoteSwitchPrompt",
                    task,
                    DisplayTaskText(_session.PendingTarget?.TaskText ?? ""));
                _decisionDiscard.Content = Strings.Get("TodoNoteDiscardAndSwitch");
                _decisionCancel.Content = Strings.Get("TodoNoteCancelSwitch");
                break;
            case TodoNoteDraftIntent.DeleteTask:
                _decisionMessage.Text = Strings.Format("TodoNoteDeleteTaskPrompt", task);
                _decisionDiscard.Content = Strings.Get("TodoNoteDiscardAndDelete");
                _decisionCancel.Content = Strings.Get("TodoNoteCancelDelete");
                break;
            case TodoNoteDraftIntent.DeletePaper:
                _decisionMessage.Text = Strings.Get("TodoNoteDeletePaperPrompt");
                _decisionDiscard.Content = Strings.Get("TodoNoteDiscardAndDelete");
                _decisionCancel.Content = Strings.Get("TodoNoteCancelDelete");
                break;
            case TodoNoteDraftIntent.Exit:
                _decisionMessage.Text = Strings.Format("TodoNoteExitPrompt", task);
                _decisionDiscard.Content = Strings.Get("TodoNoteDiscardAndExit");
                _decisionCancel.Content = Strings.Get("TodoNoteCancelExit");
                break;
            default:
                _decisionMessage.Text = Strings.Format("TodoNoteClosePrompt", task);
                _decisionDiscard.Content = Strings.Get("TodoNoteDiscardAndClose");
                _decisionCancel.Content = Strings.Get("TodoNoteContinueEditing");
                break;
        }
    }

    private void HideDecision(bool restoreEditorFocus)
    {
        _decisionHost.Visibility = Visibility.Collapsed;
        _editor.IsReadOnly = false;
        _clear.IsEnabled = _editor.Text.Length > 0;
        _cancel.IsEnabled = true;
        _save.IsEnabled = true;
        HideError();
        if (!restoreEditorFocus)
        {
            return;
        }
        _editor.Focus();
        var start = Math.Min(_preservedSelectionStart, _editor.Text.Length);
        _editor.Select(
            start,
            Math.Min(
                _preservedSelectionLength,
                Math.Max(0, _editor.Text.Length - start)));
    }

    private void ShowError()
    {
        var hasPendingDecision = _session.PendingIntent != TodoNoteDraftIntent.None;
        _decisionMessage.Visibility = hasPendingDecision
            ? Visibility.Visible
            : Visibility.Collapsed;
        _decisionSave.Visibility = hasPendingDecision
            ? Visibility.Visible
            : Visibility.Collapsed;
        _decisionDiscard.Visibility = hasPendingDecision
            ? Visibility.Visible
            : Visibility.Collapsed;
        _decisionCancel.Visibility = hasPendingDecision
            ? Visibility.Visible
            : Visibility.Collapsed;
        _errorMessage.Text = Strings.Get("TodoNoteSaveFailed");
        _errorMessage.Visibility = Visibility.Visible;
        _decisionHost.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        _errorMessage.Visibility = Visibility.Collapsed;
        _errorMessage.Text = "";
        _decisionMessage.Visibility = Visibility.Visible;
        _decisionSave.Visibility = Visibility.Visible;
        _decisionDiscard.Visibility = Visibility.Visible;
        _decisionCancel.Visibility = Visibility.Visible;
        _decisionClose.Visibility = Visibility.Collapsed;
        if (_session.PendingIntent == TodoNoteDraftIntent.None)
        {
            _decisionHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDialogClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        if (_invalidated)
        {
            _allowClose = true;
            return;
        }
        if (_destructiveDecisionCallback != null)
        {
            e.Cancel = true;
            ResolveDestructiveDecision(TodoNoteDraftResolution.Cancel);
            return;
        }
        _session.SetDraft(_editor.Text);
        var transition = _session.RequestClose();
        if (transition == TodoNoteSessionTransition.Close)
        {
            _allowClose = true;
            e.Cancel = false;
            return;
        }
        e.Cancel = true;
        ApplyTransition(transition);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }
        if (_session.PendingIntent != TodoNoteDraftIntent.None)
        {
            ResolvePending(TodoNoteDraftResolution.Cancel);
        }
        else
        {
            Close();
        }
        e.Handled = true;
    }

    private Button CreateButton(string text, bool subtle = false)
    {
        var button = new Button
        {
            Content = text,
            Style = CreateButtonStyle(subtle)
        };
        _buttons[button] = subtle;
        return button;
    }

    private static Style CreateButtonStyle(bool subtle)
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
        return style;
    }

    private void PlaceNear(Window placementOwner)
    {
        var ownerWidth = placementOwner.ActualWidth > 0
            ? placementOwner.ActualWidth
            : placementOwner.Width;
        var ownerHeight = placementOwner.ActualHeight > 0
            ? placementOwner.ActualHeight
            : placementOwner.Height;
        var placement = TodoNoteDialogPlacement.Calculate(
            new Rect(
                placementOwner.Left,
                placementOwner.Top,
                ownerWidth,
                ownerHeight),
            new Size(Width, Height),
            WindowWorkAreaHelper.WorkAreaFor(placementOwner));
        Left = placement.X;
        Top = placement.Y;
    }

    private static string DisplayTaskText(string text)
    {
        var compact = string.Join(
            " ",
            (text ?? "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
        if (compact.Length == 0)
        {
            return Strings.Get("TodoNoteUntitledTask");
        }
        return compact.Length <= 80 ? compact : compact[..79] + "…";
    }
}

internal static class TodoNoteDialogPlacement
{
    public static Point Calculate(
        Rect ownerBounds,
        Size dialogSize,
        Rect workArea)
    {
        var left = ownerBounds.Left + Math.Max(0, (ownerBounds.Width - dialogSize.Width) / 2);
        var top = ownerBounds.Top + Math.Max(0, (ownerBounds.Height - dialogSize.Height) / 2);
        return new Point(
            Math.Clamp(
                left,
                workArea.Left,
                Math.Max(workArea.Left, workArea.Right - dialogSize.Width)),
            Math.Clamp(
                top,
                workArea.Top,
                Math.Max(workArea.Top, workArea.Bottom - dialogSize.Height)));
    }
}
