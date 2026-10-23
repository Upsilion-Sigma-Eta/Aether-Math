using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Layout;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AetherMath.LogicQA")]

namespace AetherMath.Desktop;

internal enum ClipboardPasteResult
{
    Applied,
    Empty,
    NoSession,
    NoClipboard,
    StaleSession,
    StaleTarget,
    Canceled,
    ReadFailed,
    NoChange
}

public sealed class MathEditorControl : Control
{
    public static readonly StyledProperty<EditingSession?> SessionProperty =
        AvaloniaProperty.Register<MathEditorControl, EditingSession?>(nameof(Session));

    private static readonly IBrush TextSelectionFill = new SolidColorBrush(Color.FromArgb(62, 9, 109, 224));
    private static readonly IBrush EmptySlotFill = new SolidColorBrush(Color.FromArgb(20, 9, 109, 224));
    private static readonly Pen WholeNodeSelectionPen = new(new SolidColorBrush(Color.Parse("#096DE0")), 2);
    private static readonly Pen EmptySlotPen = new(new SolidColorBrush(Color.Parse("#87BDF2")), 1);
    private static readonly Pen CaretPen = new(new SolidColorBrush(Color.Parse("#096DE0")), 1.4);

    private readonly AvaloniaMathTextMetrics _metrics = new();
    private EditingSession? _attachedSession;
    private LayoutResult? _layout;
    private LayoutRenderResources? _renderResources;
    private readonly object _clipboardPasteSync = new();
    private Task _clipboardPasteTail = Task.CompletedTask;
    private CancellationTokenSource _attachmentCancellation = new();
    private long _layoutRevision = -1;
    private long _attachmentEpoch;
    private long _clipboardPasteGeneration;
    private int _pendingClipboardPastes;
    private MathCaret? _dragAnchor;
    private IPointer? _capturedPointer;
    private bool _dragging;
    private bool _isApplyingClipboardPaste;
    private bool _isAttached;

    static MathEditorControl()
    {
        AffectsRender<MathEditorControl>(SessionProperty);
    }

    public MathEditorControl()
    {
        Focusable = true;
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public EditingSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    internal void BringCaretIntoView()
    {
        if (Session is not EditingSession session || EnsureLayout() is not LayoutResult layout)
            return;

        Rect target;
        if (session.IsNodeSelected && session.SelectedNodeId is Guid selectedId &&
            MathTree.TryFindNode(session.Workspace, selectedId, out var selectedNode, out _) &&
            selectedNode is not null && layout.GetBounds(selectedId) is MathRect selectedBounds)
        {
            target = new Rect(selectedBounds.X - 3, selectedBounds.Y - 3,
                selectedBounds.Width + 6, selectedBounds.Height + 6);
        }
        else
        {
            var node = FindNodeLayout(layout, session.Caret.NodeId);
            if (node is null || FindCaretStop(node, session.Caret.TextElementOffset) is not Point caret)
                return;

            var isEmptySlot = MathTree.FindTextNode(session.Workspace, session.Caret.NodeId)?.IsEmptySlot == true;
            var width = isEmptySlot ? Math.Max(12, node.Bounds.Width) : 3;
            var x = isEmptySlot ? node.Bounds.X : caret.X - width / 2;
            const double verticalPadding = 2;
            var height = Math.Max(18, node.Bounds.Height);
            target = new Rect(x, node.Bounds.Y - verticalPadding, width, height + verticalPadding * 2);
        }

        this.BringIntoView(target);
    }

    internal Func<CancellationToken, Task<string?>>? ClipboardTextReaderOverride { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SessionProperty)
            return;
        AdvanceAttachmentEpoch();
        ReleasePointerCapture();
        AttachSession(_isAttached ? Session : null);
        ReleaseLayoutResources();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetVisualTreeAttachmentState(attached: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetVisualTreeAttachmentState(attached: false);
        base.OnDetachedFromVisualTree(e);
    }

    internal void SetVisualTreeAttachmentState(bool attached)
    {
        if (_isAttached == attached)
            return;

        _isAttached = attached;
        AdvanceAttachmentEpoch();
        if (attached)
        {
            AttachSession(Session);
        }
        else
        {
            ReleasePointerCapture();
            AttachSession(null);
            ReleaseLayoutResources();
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = EnsureLayout();
        if (layout is null)
            return new Size(1, 360);
        return new Size(Math.Max(1, layout.Width), Math.Max(360, layout.Height));
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var session = Session;
        var layout = EnsureLayout();
        if (session is null || layout is null || _renderResources is null)
            return;

        // 투명 채움은 glyph 없는 spacer 영역에서 포인터 입력을 받기 위한 것이며 수식 출력 primitive가 아니다.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        DrawTextSelection(context, layout, session);
        DrawPreparedPrimitives(context);
        DrawEmptySlots(context, layout);
        DrawWholeNodeSelection(context, layout, session);
        DrawCaret(context, layout, session);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Session is not EditingSession session || EnsureLayout() is not LayoutResult layout)
            return;

        var point = e.GetPosition(this);
        var mathPoint = new MathPoint(point.X, point.Y);
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
            return;

        Focus();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var target = layout.HitTestSelectionTarget(mathPoint);
            if (target is Guid nodeId && session.TrySelectNode(nodeId))
            {
                _dragging = false;
                _dragAnchor = null;
                e.Handled = true;
            }
            return;
        }

        var caret = layout.HitTest(mathPoint);
        if (caret is null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var anchor = session.Selection?.Anchor ?? session.Caret;
            if (anchor.NodeId == caret.Value.NodeId)
                session.TrySetSelection(new MathSelection(anchor, caret.Value));
        }
        else
        {
            session.TrySetCaret(caret.Value);
        }

        _dragAnchor = session.Selection?.Anchor ?? caret.Value;
        _dragging = true;
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _dragAnchor is not MathCaret anchor ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            Session is not EditingSession session || EnsureLayout() is not LayoutResult layout)
            return;

        var point = e.GetPosition(this);
        var caret = layout.HitTest(new MathPoint(point.X, point.Y));
        if (caret is MathCaret focus && focus.NodeId == anchor.NodeId)
            session.TrySetSelection(new MathSelection(anchor, focus));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(_capturedPointer, e.Pointer))
        {
            ReleasePointerCapture();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!ReferenceEquals(_capturedPointer, e.Pointer))
            return;

        _capturedPointer = null;
        _dragging = false;
        _dragAnchor = null;
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (Session is not EditingSession session || string.IsNullOrEmpty(e.Text))
            return;
        if (session.InsertText(e.Text))
            e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var session = Session;
        if (session is null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            e.Handled = true;
            StartClipboardPasteFromKeyboard();
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var canRedo = session.CanRedo;
                session.Redo();
                if (canRedo) BringCaretIntoView();
            }
            else
            {
                var canUndo = session.CanUndo;
                session.Undo();
                if (canUndo) BringCaretIntoView();
            }
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            var canRedo = session.CanRedo;
            session.Redo();
            if (canRedo) BringCaretIntoView();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (session.TrySelectParentStructure())
                e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Back:
                if (session.Backspace()) e.Handled = true;
                break;
            case Key.Delete:
                if (session.DeleteForward()) e.Handled = true;
                break;
            case Key.Left:
                if (MoveCaret(-1, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
                break;
            case Key.Right:
                if (MoveCaret(1, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
                break;
            case Key.Home:
                if (MoveToTextBoundary(atEnd: false, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
                break;
            case Key.End:
                if (MoveToTextBoundary(atEnd: true, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
                break;
            case Key.Tab:
                if (MoveToAdjacentTextNode(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
                break;
        }
    }
    internal Task<ClipboardPasteResult> PasteFromClipboardAsync()
    {
        var session = Session;
        if (session is null)
            return Task.FromResult(ClipboardPasteResult.NoSession);

        var epoch = _attachmentEpoch;
        var cancellationToken = _attachmentCancellation.Token;
        lock (_clipboardPasteSync)
        {
            var generation = _clipboardPasteGeneration;
            var previousPaste = _clipboardPasteTail;
            var completion = new TaskCompletionSource<ClipboardPasteResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingClipboardPastes++;
            _clipboardPasteTail = ContinueClipboardPasteQueueAsync(completion.Task);
            _ = CompleteQueuedClipboardPasteAsync(
                previousPaste, session, epoch, generation, cancellationToken, completion);
            return completion.Task;
        }
    }

    private async void StartClipboardPasteFromKeyboard()
    {
        try
        {
            await PasteFromClipboardAsync();
        }
        catch (Exception exception)
        {
            // Clipboard read errors are reported as a result. Editing failures remain visible on the UI dispatcher.
            Dispatcher.UIThread.Post(() => ExceptionDispatchInfo.Capture(exception).Throw());
        }
    }

    private async Task<ClipboardPasteResult> PasteFromClipboardAfterAsync(
        Task previousPaste,
        EditingSession session,
        long epoch,
        long generation,
        CancellationToken cancellationToken)
    {
        await previousPaste.ConfigureAwait(true);
        if (!IsCurrentAttachment(session, epoch, cancellationToken))
            return ClipboardPasteResult.StaleSession;
        if (_clipboardPasteGeneration != generation)
            return ClipboardPasteResult.StaleTarget;

        var target = CapturePasteTarget(session);
        ClipboardTextRead clipboardText;
        try
        {
            clipboardText = await ReadClipboardTextAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return ClipboardPasteResult.Canceled;
        }
        catch (Exception)
        {
            return ClipboardPasteResult.ReadFailed;
        }

        if (!IsCurrentAttachment(session, epoch, cancellationToken))
            return ClipboardPasteResult.StaleSession;
        if (_clipboardPasteGeneration != generation || !target.Matches(session))
            return ClipboardPasteResult.StaleTarget;
        if (!clipboardText.IsAvailable)
            return ClipboardPasteResult.NoClipboard;
        if (string.IsNullOrEmpty(clipboardText.Text))
            return ClipboardPasteResult.Empty;

        _isApplyingClipboardPaste = true;
        try
        {
            return session.InsertText(clipboardText.Text)
                ? ClipboardPasteResult.Applied
                : ClipboardPasteResult.NoChange;
        }
        finally
        {
            _isApplyingClipboardPaste = false;
        }
    }

    private async Task CompleteQueuedClipboardPasteAsync(
        Task previousPaste,
        EditingSession session,
        long epoch,
        long generation,
        CancellationToken cancellationToken,
        TaskCompletionSource<ClipboardPasteResult> completion)
    {
        try
        {
            var result = await PasteFromClipboardAfterAsync(
                previousPaste, session, epoch, generation, cancellationToken).ConfigureAwait(true);
            completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task<ClipboardTextRead> ReadClipboardTextAsync(CancellationToken cancellationToken)
    {
        var reader = ClipboardTextReaderOverride;
        if (reader is not null)
        {
            var text = await AwaitClipboardTextAsync(reader(cancellationToken), cancellationToken)
                .ConfigureAwait(true);
            return new ClipboardTextRead(IsAvailable: true, text);
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return new ClipboardTextRead(IsAvailable: false, Text: null);

        var clipboardText = await AwaitClipboardTextAsync(clipboard.TryGetTextAsync(), cancellationToken)
            .ConfigureAwait(true);
        return new ClipboardTextRead(IsAvailable: true, clipboardText);
    }

    private static async Task<string?> AwaitClipboardTextAsync(
        Task<string?> readTask,
        CancellationToken cancellationToken)
    {
        try
        {
            return await readTask.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = readTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private bool IsCurrentAttachment(EditingSession session, long epoch, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _isAttached && _attachmentEpoch == epoch &&
        ReferenceEquals(Session, session) && ReferenceEquals(_attachedSession, session);

    private static PasteTarget CapturePasteTarget(EditingSession session) =>
        new(session.Revision, session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected);

    private async Task ContinueClipboardPasteQueueAsync(Task<ClipboardPasteResult> operation)
    {
        await Task.Yield();
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // Preserve the fault on the request task while allowing later queued pastes to proceed.
        }
        finally
        {
            lock (_clipboardPasteSync)
            {
                _pendingClipboardPastes--;
                if (_pendingClipboardPastes == 0)
                    _clipboardPasteTail = Task.CompletedTask;
            }
        }
    }

    private void InvalidatePendingClipboardPastes(string? propertyName)
    {
        if (_isApplyingClipboardPaste || propertyName is not (
            nameof(EditingSession.Workspace) or
            nameof(EditingSession.Revision) or
            nameof(EditingSession.Caret) or
            nameof(EditingSession.Selection) or
            nameof(EditingSession.SelectedNodeId) or
            nameof(EditingSession.IsNodeSelected)))
            return;

        lock (_clipboardPasteSync)
        {
            if (_pendingClipboardPastes > 0)
                _clipboardPasteGeneration++;
        }
    }

    private void AdvanceAttachmentEpoch()
    {
        var previous = _attachmentCancellation;
        _attachmentCancellation = new CancellationTokenSource();
        _attachmentEpoch++;
        previous.Cancel();
        previous.Dispose();
    }

    private void ReleasePointerCapture()
    {
        var pointer = _capturedPointer;
        _capturedPointer = null;
        _dragging = false;
        _dragAnchor = null;
        if (pointer is not null && ReferenceEquals(pointer.Captured, this))
            pointer.Capture(null);
    }

    private readonly record struct ClipboardTextRead(bool IsAvailable, string? Text);

    private readonly record struct PasteTarget(
        long Revision,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected)
    {
        public bool Matches(EditingSession session) =>
            session.Revision == Revision && session.Caret == Caret && session.Selection == Selection &&
            session.SelectedNodeId == SelectedNodeId && session.IsNodeSelected == IsNodeSelected;
    }

    private LayoutResult? EnsureLayout()
    {
        var session = Session;
        if (session is null)
            return null;
        if (_layout is not null && _layoutRevision == session.Revision && _renderResources is not null)
            return _layout;

        var workspace = session.Workspace;
        var layout = MathLayoutEngine.Layout(workspace, _metrics,
            new MathLayoutOptions(FontSize: workspace.Style.FontSize));
        var resources = new LayoutRenderResources(layout);
        _renderResources?.Dispose();
        _layout = layout;
        _renderResources = resources;
        _layoutRevision = session.Revision;
        return layout;
    }
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidatePendingClipboardPastes(e.PropertyName);
        if (e.PropertyName == nameof(EditingSession.Workspace))
        {
            ReleaseLayoutResources();
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    private void AttachSession(EditingSession? session)
    {
        if (ReferenceEquals(_attachedSession, session))
            return;
        if (_attachedSession is not null)
            _attachedSession.PropertyChanged -= OnSessionPropertyChanged;
        _attachedSession = session;
        if (_attachedSession is not null)
            _attachedSession.PropertyChanged += OnSessionPropertyChanged;
    }

    private void ReleaseLayoutResources()
    {
        _renderResources?.Dispose();
        _renderResources = null;
        _layout = null;
        _layoutRevision = -1;
    }

    private void DrawPreparedPrimitives(DrawingContext context)
    {
        _renderResources?.Draw(context);
    }
    private static void DrawEmptySlots(DrawingContext context, LayoutResult layout)
    {
        foreach (var diagnostic in layout.Diagnostics)
        {
            var node = FindNodeLayout(layout, diagnostic.NodeId);
            if (node is null || node.Kind != MathNodeKind.Text)
                continue;
            var rect = new Rect(node.Bounds.X, node.Bounds.Y,
                Math.Max(12, node.Bounds.Width), Math.Max(18, node.Bounds.Height));
            context.DrawRectangle(EmptySlotFill, EmptySlotPen, rect);
        }
    }


    private static void DrawTextSelection(DrawingContext context, LayoutResult layout, EditingSession session)
    {
        if (session.Selection is not MathSelection selection || selection.Anchor.NodeId != selection.Focus.NodeId)
            return;
        var node = FindNodeLayout(layout, selection.Anchor.NodeId);
        if (node is null || node.CaretStops.IsDefaultOrEmpty)
            return;
        var first = FindCaretStop(node, Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset));
        var last = FindCaretStop(node, Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset));
        if (first is null || last is null)
            return;
        var rect = new Rect(Math.Min(first.Value.X, last.Value.X), node.Bounds.Y,
            Math.Max(1, Math.Abs(last.Value.X - first.Value.X)), node.Bounds.Height);
        context.DrawRectangle(TextSelectionFill, null, rect);
    }

    private static void DrawWholeNodeSelection(DrawingContext context, LayoutResult layout, EditingSession session)
    {
        if (!session.IsNodeSelected || session.SelectedNodeId is not Guid selectedId || layout.GetBounds(selectedId) is not MathRect bounds)
            return;
        context.DrawRectangle(null, WholeNodeSelectionPen,
            new Rect(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6));
    }


    private void DrawCaret(DrawingContext context, LayoutResult layout, EditingSession session)
    {
        if (!IsFocused || session.IsNodeSelected || session.Selection is not null)
            return;
        var node = FindNodeLayout(layout, session.Caret.NodeId);
        if (node is null)
            return;
        var stop = FindCaretStop(node, session.Caret.TextElementOffset);
        if (stop is null)
            return;
        context.DrawLine(CaretPen, new Point(stop.Value.X, node.Bounds.Y), new Point(stop.Value.X, node.Bounds.Bottom));
    }

    private bool MoveCaret(int direction, bool extendSelection)
    {
        if (Session is not EditingSession session)
            return false;

        var current = session.Caret;
        if (session.Selection is MathSelection selection && !extendSelection)
        {
            var selectedOffset = direction < 0
                ? Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset)
                : Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            return session.TrySetCaret(new MathCaret(selection.Anchor.NodeId, selectedOffset));
        }

        if (session.IsNodeSelected && session.SelectedNodeId is Guid selectedSpaceId &&
            MathTree.TryFindNode(session.Workspace, selectedSpaceId, out var selectedSpace, out _) &&
            selectedSpace is MathSpaceNode)
            return session.TryMoveFromSelectedMathSpace(forward: direction > 0);

        if (session.IsNodeSelected && session.SelectedNodeId is Guid selectedId &&
            MathTree.TryFindNode(session.Workspace, selectedId, out var selected, out _))
        {
            var leaves = EnumerateTextNodes(selected!).ToList();
            if (leaves.Count == 0)
                return false;
            var edge = direction < 0 ? leaves[^1] : leaves[0];
            current = new MathCaret(edge.Id, direction < 0 ? TextElementOffsets.Count(edge.Text) : 0);
            return session.TrySetCaret(current);
        }

        var text = MathTree.FindTextNode(session.Workspace, current.NodeId);
        if (text is null)
            return false;
        var offset = current.TextElementOffset + direction;
        if (offset < 0 || offset > TextElementOffsets.Count(text.Text))
        {
            var ordered = EnumerateTextNodes(session.Workspace).ToList();
            var index = ordered.FindIndex(candidate => candidate.Id == current.NodeId);
            var next = index + direction;
            if (next < 0 || next >= ordered.Count)
                return false;
            text = ordered[next];
            offset = direction < 0 ? TextElementOffsets.Count(text.Text) : 0;
        }

        var target = new MathCaret(text.Id, offset);
        if (!extendSelection)
            return session.TrySetCaret(target);
        var anchor = session.Selection?.Anchor ?? session.Caret;
        return anchor.NodeId == target.NodeId && session.TrySetSelection(new MathSelection(anchor, target));
    }

    private bool MoveToTextBoundary(bool atEnd, bool extendSelection)
    {
        if (Session is not EditingSession session)
            return false;
        var node = MathTree.FindTextNode(session.Workspace, session.Caret.NodeId);
        if (node is null)
            return false;
        var target = new MathCaret(node.Id, atEnd ? TextElementOffsets.Count(node.Text) : 0);
        if (!extendSelection)
            return session.TrySetCaret(target);
        var anchor = session.Selection?.Anchor ?? session.Caret;
        return anchor.NodeId == target.NodeId && session.TrySetSelection(new MathSelection(anchor, target));
    }

    private bool MoveToAdjacentTextNode(bool forward)
    {
        if (Session is not EditingSession session)
            return false;
        if (session.IsNodeSelected && session.SelectedNodeId is Guid selectedSpaceId &&
            MathTree.TryFindNode(session.Workspace, selectedSpaceId, out var selectedSpace, out _) &&
            selectedSpace is MathSpaceNode)
            return session.TryMoveFromSelectedMathSpace(forward);

        var ordered = EnumerateTextNodes(session.Workspace).ToList();
        var currentId = session.IsNodeSelected ? session.SelectedNodeId : session.Caret.NodeId;
        var index = ordered.FindIndex(candidate => candidate.Id == currentId);
        var next = index + (forward ? 1 : -1);
        if (next < 0 || next >= ordered.Count)
            return false;
        return session.TrySetCaret(new MathCaret(ordered[next].Id, 0));
    }

    private static IEnumerable<TextNode> EnumerateTextNodes(EquationWorkspace workspace)
    {
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
        foreach (var text in EnumerateTextNodes(row.Root))
            yield return text;
    }

    private static IEnumerable<TextNode> EnumerateTextNodes(MathNode node)
    {
        if (node is TextNode textNode)
        {
            yield return textNode;
            yield break;
        }
        foreach (var child in MathTree.ChildrenOf(node))
        foreach (var descendantText in EnumerateTextNodes(child))
            yield return descendantText;
    }

    private static NodeLayout? FindNodeLayout(LayoutResult layout, Guid nodeId)
    {
        foreach (var node in layout.Nodes)
            if (node.NodeId == nodeId)
                return node;
        return null;
    }
    private static Point? FindCaretStop(NodeLayout node, int offset)
    {
        foreach (var stop in node.CaretStops)
            if (stop.TextElementOffset == offset)
                return new Point(stop.Position.X, stop.Position.Y);
        return null;
    }

}
