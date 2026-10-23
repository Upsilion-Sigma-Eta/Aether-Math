using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly SemaphoreSlim _closeApprovalGate = new(1, 1);
    private EditingSession _observedSession;
    private TabItem _lastNonOutputInspectorTab;
    private Guid? _closeApprovalToken;
    private bool _closeApprovalRejected;

    internal Func<string, Task>? LatexClipboardWriterOverride { get; set; }

    public MainWindow() : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _observedSession = viewModel.Session;
        _lastNonOutputInspectorTab = PropertiesTab;
        DataContext = viewModel;
        viewModel.AttachWindow(this);
        _observedSession.PropertyChanged += OnObservedSessionPropertyChanged;
        viewModel.EditorFocusRequested += OnEditorFocusRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Properties.PropertyChanged += OnPropertiesPanelPropertyChanged;
        viewModel.OutputPreview.PropertyChanged += OnOutputPreviewPropertyChanged;
        viewModel.SymbolPalette.PropertyChanged += OnSymbolPalettePropertyChanged;
        viewModel.StructurePanel.PropertyChanged += OnStructurePanelPropertyChanged;
        viewModel.StructurePanel.RevealNodeRequested += OnStructureNodeRevealRequested;
        // Handle Enter on the tunnel before TreeViewItem's default toggle so the focused node is explicitly selected.
        StructureTree.AddHandler(InputElement.KeyDownEvent, OnStructureTreeKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        viewModel.CloseRequested += OnCloseRequested;
        ToolModeTabs.SelectionChanged += OnToolModeSelectionChanged;
        EquationInspector.SelectionChanged += OnEquationInspectorSelectionChanged;
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
        Opened += OnWindowOpened;
        UpdateWorkspaceStateVisuals();
        UpdatePaletteCoverageVisuals();
        UpdateUnicodePageIndicator();
        UpdateStructureClearVisuals();
        UpdateStructureSelectionMode();
        UpdateOutputPreviewVisuals();
        UpdateTextRoleVisuals();
        UpdateTypesettingPinVisuals();
        UpdateGroupRowSpacingVisuals();
        UpdateBracketKindVisuals();
        UpdateThinMathSpaceVisuals();
        UpdateLatexCodeDiagnosticVisuals();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.EditorFocusRequested -= OnEditorFocusRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Properties.PropertyChanged -= OnPropertiesPanelPropertyChanged;
        _viewModel.OutputPreview.PropertyChanged -= OnOutputPreviewPropertyChanged;
        _viewModel.SymbolPalette.PropertyChanged -= OnSymbolPalettePropertyChanged;
        _viewModel.StructurePanel.PropertyChanged -= OnStructurePanelPropertyChanged;
        _viewModel.StructurePanel.RevealNodeRequested -= OnStructureNodeRevealRequested;
        StructureTree.RemoveHandler(InputElement.KeyDownEvent, OnStructureTreeKeyDown);
        _viewModel.CloseRequested -= OnCloseRequested;
        ToolModeTabs.SelectionChanged -= OnToolModeSelectionChanged;
        EquationInspector.SelectionChanged -= OnEquationInspectorSelectionChanged;
        Closing -= OnWindowClosing;
        KeyDown -= OnWindowKeyDown;
        Opened -= OnWindowOpened;
        _viewModel.DetachWindow(this);
        _observedSession.PropertyChanged -= OnObservedSessionPropertyChanged;
        base.OnClosed(e);
        _ = StopRecoveryAfterCloseAsync();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApprovalToken is Guid approvalToken)
        {
            _closeApprovalToken = null;
            if (_viewModel.TryConsumeCloseApproval(approvalToken))
                return;

            _closeApprovalRejected = true;
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _viewModel.RequestClose();
    }

    private void OnCloseRequested(object? sender, CloseRequestedEventArgs e)
    {
        _ = StopAndCloseAfterApprovalAsync(e.ApprovalToken);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Session) &&
            !ReferenceEquals(_observedSession, _viewModel.Session))
        {
            _observedSession.PropertyChanged -= OnObservedSessionPropertyChanged;
            _observedSession = _viewModel.Session;
            _observedSession.PropertyChanged += OnObservedSessionPropertyChanged;
            UpdateStructureSelectionMode();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsOutputPreviewVisible))
            UpdateOutputPreviewVisuals();

        if (e.PropertyName is nameof(MainWindowViewModel.CanExportLatex) or
            nameof(MainWindowViewModel.LatexCodeDiagnostic) or
            nameof(MainWindowViewModel.LatexLayoutDiagnostics) or
            nameof(MainWindowViewModel.CanCopyLatex) or
            nameof(MainWindowViewModel.IsCopyingLatex) or
            nameof(MainWindowViewModel.LatexCopyDisabledReason) or
            nameof(MainWindowViewModel.LatexCopyMessage))
            UpdateLatexCodeDiagnosticVisuals();

        if (e.PropertyName is nameof(MainWindowViewModel.CanClearActiveScope) or
            nameof(MainWindowViewModel.ClearScopeDisabledReason))
            UpdateStructureClearVisuals();

        if (e.PropertyName is nameof(MainWindowViewModel.CanInsertThinSpace) or
            nameof(MainWindowViewModel.ThinSpaceDisabledReason) or
            nameof(MainWindowViewModel.ThinSpaceNotice))
            UpdateThinMathSpaceVisuals();

        if (e.PropertyName is nameof(MainWindowViewModel.RecentWorkspaces) or
            nameof(MainWindowViewModel.SelectedRecentWorkspace) or
            nameof(MainWindowViewModel.RecentWorkspaceDiagnostic) or
            nameof(MainWindowViewModel.RecentWorkspaceCanWrite) or
            nameof(MainWindowViewModel.RecoveryCandidates) or
            nameof(MainWindowViewModel.RecoveryDiagnostics) or
            nameof(MainWindowViewModel.IsWorkspaceStateReady) or
            nameof(MainWindowViewModel.IsWorkspaceStateLoading) or
            nameof(MainWindowViewModel.WorkspaceStateError))
            UpdateWorkspaceStateVisuals();

        if (e.PropertyName != nameof(MainWindowViewModel.IsPendingWorkspaceActionVisible) ||
            !_viewModel.IsPendingWorkspaceActionVisible)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_viewModel.IsPendingWorkspaceActionVisible || !PendingWorkspaceActionOverlay.IsVisible)
                return;

            var initialFocus = SavePendingWorkspaceActionButton.IsEffectivelyEnabled
                ? SavePendingWorkspaceActionButton
                : CancelPendingWorkspaceActionButton;
            initialFocus.Focus();
        });
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.InitializeWorkspaceStateAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Workspace state initialization failed: {0}", exception);
        }
        finally
        {
            UpdateWorkspaceStateVisuals();
        }
    }

    private void UpdateWorkspaceStateVisuals()
    {
        var ready = _viewModel.IsWorkspaceStateReady;
        RecentWorkspaceEmptyState.IsVisible = ready && _viewModel.RecentWorkspaces.IsEmpty;
        RecentWorkspaceDiagnosticText.IsVisible = ready && _viewModel.RecentWorkspaceDiagnostic is not null;
        RecentWorkspaceReadOnlyNotice.IsVisible = ready && !_viewModel.RecentWorkspaceCanWrite &&
                                                  _viewModel.RecentWorkspaceDiagnostic is not null;
        RecentWorkspacePreview.IsVisible = _viewModel.SelectedRecentWorkspace is not null;
        RecentWorkspacePreviewEmpty.IsVisible = _viewModel.SelectedRecentWorkspace is null;
        WorkspaceStateError.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.WorkspaceStateError);
        RecoveryEmptyState.IsVisible = ready && _viewModel.RecoveryCandidates.IsEmpty &&
                                       _viewModel.RecoveryDiagnostics.IsEmpty;
    }

    private async Task StopAndCloseAfterApprovalAsync(Guid approvalToken)
    {
        await _closeApprovalGate.WaitAsync();
        try
        {
            if (!IsVisible)
                return;

            await _viewModel.StopRecoveryAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsVisible)
                    return;

                _closeApprovalRejected = false;
                _closeApprovalToken = approvalToken;
                Close();
            });

            if (IsVisible && _closeApprovalRejected)
            {
                _closeApprovalRejected = false;
                await _viewModel.InitializeWorkspaceStateAsync();
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Workspace state drain before close failed: {0}", exception);
            if (IsVisible)
            {
                try
                {
                    await _viewModel.InitializeWorkspaceStateAsync();
                }
                catch (Exception restartException)
                {
                    Trace.TraceError("Workspace state restart after close failure failed: {0}", restartException);
                }
            }
        }
        finally
        {
            _closeApprovalGate.Release();
        }
    }

    private async Task StopRecoveryAfterCloseAsync()
    {
        try
        {
            await _viewModel.StopRecoveryAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Workspace state stop after window close failed: {0}", exception);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_viewModel.IsPendingWorkspaceActionVisible)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Z or Key.Y)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_viewModel.CancelPendingWorkspaceActionCommand.CanExecute(null))
                _viewModel.CancelPendingWorkspaceActionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab)
            return;

        var focusTargets = new[]
            {
                SavePendingWorkspaceActionButton,
                DiscardPendingWorkspaceActionButton,
                CancelPendingWorkspaceActionButton
            }
            .Where(button => button.IsEffectivelyEnabled)
            .ToArray();
        if (focusTargets.Length == 0)
        {
            e.Handled = true;
            return;
        }

        var currentIndex = Array.FindIndex(focusTargets, button => button.IsFocused);
        var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + focusTargets.Length) % focusTargets.Length;
        focusTargets[nextIndex].Focus();
        e.Handled = true;
    }

    private void OnEditorFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsOutputPreviewVisible || !EditorWorkspaceSurface.IsVisible)
                return;

            MathEditorSurface.Focus();
            MathEditorSurface.BringCaretIntoView();
        });
    }

    private void OnOutputPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MathOutputPreviewViewModel.State) or
            nameof(MathOutputPreviewViewModel.Diagnostic) or
            nameof(MathOutputPreviewViewModel.CanExportSvg) or
            nameof(MathOutputPreviewViewModel.ExportDisabledReason))
            UpdateOutputPreviewVisuals();
    }

    private void OnPropertiesPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MathPropertiesPanelViewModel.CanEditTextRole) or
            nameof(MathPropertiesPanelViewModel.TextRoleDisabledReason) or
            nameof(MathPropertiesPanelViewModel.TextRoleScopeNotice))
            UpdateTextRoleVisuals();

        if (e.PropertyName is nameof(MathPropertiesPanelViewModel.CanPinTypesettingStyle) or
            nameof(MathPropertiesPanelViewModel.TypesettingPinDisabledReason) or
            nameof(MathPropertiesPanelViewModel.IsFractionSelection) or
            nameof(MathPropertiesPanelViewModel.FractionDisplayNotice))
            UpdateTypesettingPinVisuals();

        if (e.PropertyName is nameof(MathPropertiesPanelViewModel.CanEditGroupRowSpacing) or
            nameof(MathPropertiesPanelViewModel.CurrentGroupRowSpacing) or
            nameof(MathPropertiesPanelViewModel.GroupRowSpacingValidationError) or
            nameof(MathPropertiesPanelViewModel.GroupRowSpacingNotice) or
            nameof(MathPropertiesPanelViewModel.GroupRowSpacingDisabledReason) or
            nameof(MathPropertiesPanelViewModel.GroupRowSpacingLatexDiagnostic) or
            nameof(MathPropertiesPanelViewModel.IsDraftStale))
            UpdateGroupRowSpacingVisuals();

        if (e.PropertyName is nameof(MathPropertiesPanelViewModel.CanEditBracketKind) or
            nameof(MathPropertiesPanelViewModel.CurrentBracketKind) or
            nameof(MathPropertiesPanelViewModel.CurrentBracketKindLabel) or
            nameof(MathPropertiesPanelViewModel.BracketKindDisabledReason) or
            nameof(MathPropertiesPanelViewModel.BracketKindNotice) or
            nameof(MathPropertiesPanelViewModel.IsDraftStale))
            UpdateBracketKindVisuals();
    }

    private void UpdateTextRoleVisuals()
    {
        var properties = _viewModel.Properties;
        var disabledReason = properties.TextRoleDisabledReason;
        TextRoleCurrentValue.IsVisible = properties.CanEditTextRole;
        TextRoleDisabledReason.IsVisible = !properties.CanEditTextRole &&
                                           !string.IsNullOrWhiteSpace(disabledReason);
        TextRoleScopeNotice.IsVisible = properties.CanEditTextRole &&
                                        !string.IsNullOrWhiteSpace(properties.TextRoleScopeNotice);
    }

    private void UpdateTypesettingPinVisuals()
    {
        var properties = _viewModel.Properties;
        TypesettingPinDisabledReason.IsVisible = !properties.CanPinTypesettingStyle &&
                                                 !string.IsNullOrWhiteSpace(properties.TypesettingPinDisabledReason);
        FractionDisplayNotice.IsVisible = properties.IsFractionSelection &&
                                          !string.IsNullOrWhiteSpace(properties.FractionDisplayNotice);
    }

    private void UpdateGroupRowSpacingVisuals()
    {
        var properties = _viewModel.Properties;
        var canEdit = properties.CanEditGroupRowSpacing;
        GroupRowSpacingCurrentValue.IsVisible = canEdit && properties.CurrentGroupRowSpacing is not null;
        GroupRowSpacingValidationError.IsVisible = canEdit &&
            !string.IsNullOrWhiteSpace(properties.GroupRowSpacingValidationError);
        GroupRowSpacingDisabledReason.IsVisible = !canEdit &&
            !string.IsNullOrWhiteSpace(properties.GroupRowSpacingDisabledReason);
        GroupRowSpacingNotice.IsVisible = canEdit &&
            !string.IsNullOrWhiteSpace(properties.GroupRowSpacingNotice);
        GroupRowSpacingLatexDiagnostic.IsVisible = canEdit &&
            !string.IsNullOrWhiteSpace(properties.GroupRowSpacingLatexDiagnostic);
    }

    private void UpdateBracketKindVisuals()
    {
        var properties = _viewModel.Properties;
        var canEdit = properties.CanEditBracketKind;
        CurrentBracketKindValue.IsVisible = canEdit && properties.CurrentBracketKind is not null;
        BracketKindDisabledReason.IsVisible = !canEdit &&
            !string.IsNullOrWhiteSpace(properties.BracketKindDisabledReason);
        BracketKindNotice.IsVisible = canEdit &&
            !string.IsNullOrWhiteSpace(properties.BracketKindNotice);
    }

    private void UpdateThinMathSpaceVisuals()
    {
        ThinSpaceNotice.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.ThinSpaceNotice);
        ThinSpaceDisabledReason.IsVisible = !_viewModel.CanInsertThinSpace &&
            !string.IsNullOrWhiteSpace(_viewModel.ThinSpaceDisabledReason);
    }

    private void UpdateLatexCodeDiagnosticVisuals()
    {
        var hasBlockingDiagnostic = !_viewModel.CanExportLatex &&
                                    !string.IsNullOrWhiteSpace(_viewModel.LatexCodeDiagnostic);
        var hasLayoutDiagnostic = !_viewModel.LatexLayoutDiagnostics.IsDefaultOrEmpty;
        var hasCopyMessage = !string.IsNullOrWhiteSpace(_viewModel.LatexCopyMessage);
        CopyLatexButtonLabel.Text = _viewModel.IsCopyingLatex ? "복사 중…" : "전체 코드 복사";
        LatexCopyStatusBanner.IsVisible = hasCopyMessage;
        LatexCodeDiagnosticBanner.IsVisible = hasBlockingDiagnostic;
        LatexLayoutDiagnosticBanner.IsVisible = hasLayoutDiagnostic;
        LatexDiagnosticsScrollViewer.IsVisible = hasCopyMessage || hasBlockingDiagnostic || hasLayoutDiagnostic;
    }

    private async void OnCopyLatexClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Func<string, Task>? writer = LatexClipboardWriterOverride;
        if (writer is null)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                writer = text => clipboard.SetTextAsync(text);
        }

        await _viewModel.CopyLatexAsync(writer);
    }

    private void OnLatexHighlightToggleClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateOutputPreviewVisuals()
    {
        var isVisible = _viewModel.IsOutputPreviewVisible;
        TemplateToolsPanel.IsVisible = !isVisible;
        OutputPreviewToolsPanel.IsVisible = isVisible;
        EditorWorkspaceHeader.IsVisible = !isVisible;
        OutputPreviewHeader.IsVisible = isVisible;
        EditorWorkspaceSurface.IsVisible = !isVisible;
        OutputPreviewSurface.IsVisible = isVisible;
        LatexCodeExpander.IsVisible = !isVisible;
        OutputPreviewFooter.IsVisible = isVisible;
        InspectorSubtitle.Text = isVisible
            ? "SVG 전체 작업의 상태와 저장 설정"
            : "현재 편집 중인 수식의 정보";

        var state = _viewModel.OutputPreview.State;
        var diagnostic = _viewModel.OutputPreview.Diagnostic;
        OutputPreviewStaleReason.IsVisible = isVisible && state == MathOutputPreviewState.Stale &&
                                               !string.IsNullOrWhiteSpace(diagnostic);
        OutputPreviewDiagnostic.IsVisible = isVisible &&
            (state is MathOutputPreviewState.Incomplete or MathOutputPreviewState.Failed) &&
            !string.IsNullOrWhiteSpace(diagnostic);
        var hasPrimaryDiagnostic = OutputPreviewStaleReason.IsVisible || OutputPreviewDiagnostic.IsVisible;
        OutputPreviewExportDisabledReason.IsVisible = isVisible &&
            !hasPrimaryDiagnostic &&
            !_viewModel.OutputPreview.CanExportSvg &&
            !string.IsNullOrWhiteSpace(_viewModel.OutputPreview.ExportDisabledReason);

        if (isVisible)
        {
            if (EquationInspector.SelectedItem is TabItem currentTab && !ReferenceEquals(currentTab, OutputPreviewTab))
                _lastNonOutputInspectorTab = currentTab;
            if (!ReferenceEquals(EquationInspector.SelectedItem, OutputPreviewTab))
                EquationInspector.SelectedItem = OutputPreviewTab;
            return;
        }

        if (ReferenceEquals(EquationInspector.SelectedItem, OutputPreviewTab))
            EquationInspector.SelectedItem = _lastNonOutputInspectorTab ?? PropertiesTab;
    }

    private void OnOutputPreviewReturnToEditorClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsOutputPreviewVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsOutputPreviewVisible || !EditorWorkspaceSurface.IsVisible)
                return;
            MathEditorSurface.Focus();
            MathEditorSurface.BringCaretIntoView();
        }, DispatcherPriority.Render);
    }

    private void OnStructurePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StructurePanelViewModel.SelectedItem) or
            nameof(StructurePanelViewModel.Items))
            Dispatcher.UIThread.Post(RevealSelectedStructureNode, DispatcherPriority.Render);
    }

    private void OnEquationInspectorSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, EquationInspector))
            return;

        if (e.Source is Control source &&
            !ReferenceEquals(source, EquationInspector) &&
            !ReferenceEquals(source, EquationInspector.SelectedItem))
            return;

        if (ReferenceEquals(EquationInspector.SelectedItem, OutputPreviewTab))
        {
            if (!_viewModel.IsOutputPreviewVisible)
                _viewModel.ShowOutputPreviewCommand.Execute(null);
            else if (_viewModel.OutputPreview.State == MathOutputPreviewState.Empty)
                _viewModel.ShowOutputPreviewCommand.Execute(null);
            return;
        }

        if (EquationInspector.SelectedItem is TabItem selectedTab)
        {
            _lastNonOutputInspectorTab = selectedTab;
            if (_viewModel.IsOutputPreviewVisible)
                _viewModel.IsOutputPreviewVisible = false;
        }

        if (!StructureInspectorTab.IsSelected)
            return;

        Dispatcher.UIThread.Post(RevealSelectedStructureNode, DispatcherPriority.Render);
    }

    private void OnObservedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditingSession.IsNodeSelected) or
            nameof(EditingSession.Selection) or nameof(EditingSession.Caret) or
            nameof(EditingSession.SelectedNodeId))
            UpdateStructureSelectionMode();
    }

    private void UpdateStructureSelectionMode() =>
        StructureSelectionModeText.Text = _viewModel.Session.IsNodeSelected
            ? "전체 구조 선택"
            : _viewModel.Session.Selection is not null
                ? "표면 부분 선택"
                : "표면 입력 위치";

    private void UpdateStructureClearVisuals()
    {
        StructureClearDisabledReason.IsVisible = !_viewModel.CanClearActiveScope &&
                                                  !string.IsNullOrWhiteSpace(_viewModel.ClearScopeDisabledReason);
        ToolTip.SetTip(ClearActiveScopeButton, _viewModel.CanClearActiveScope
            ? _viewModel.ClearScopeDescription
            : _viewModel.ClearScopeDisabledReason);
    }

    private void OnStructureTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, StructureTree) ||
            StructureTree.SelectedItem is not StructurePanelItem item)
            return;

        // TreeView.SelectedItem is also updated by the surface-to-tree projection. The
        // StructurePanel selection identifies that echo; only a distinct user choice
        // becomes a whole-node selection in the Session.
        if (_viewModel.StructurePanel.SelectedItem?.MathNodeId != item.MathNodeId)
            _viewModel.StructurePanel.SelectNode(item.MathNodeId);
    }

    private void OnStructureNodeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: StructurePanelItem item })
            return;

        _viewModel.StructurePanel.SelectNode(item.MathNodeId);
        e.Handled = true;
    }

    private void OnStructureTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || StructureTree.SelectedItem is not StructurePanelItem item)
            return;

        _viewModel.StructurePanel.SelectNode(item.MathNodeId);
        e.Handled = true;
    }

    private void OnStructureNodeRevealRequested(object? sender, StructureNodeRevealRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (StructureInspectorTab.IsSelected &&
                _viewModel.StructurePanel.SelectedNodeId == e.NodeId &&
                _viewModel.Session.SelectedNodeId == e.NodeId)
            {
                MathEditorSurface.BringCaretIntoView();
                BringStructureNodeIntoView(e.NodeId);
            }
        });
    }

    private void RevealSelectedStructureNode()
    {
        if (StructureInspectorTab.IsSelected && _viewModel.StructurePanel.SelectedItem is StructurePanelItem item)
            BringStructureNodeIntoView(item.MathNodeId);
    }

    private void BringStructureNodeIntoView(Guid nodeId)
    {
        if (!StructureInspectorTab.IsSelected || _viewModel.StructurePanel.SelectedNodeId != nodeId)
            return;

        var session = _viewModel.Session;
        var path = new List<Guid>();
        if (!TryBuildStructurePath(_viewModel.StructurePanel.Items, nodeId, path))
            return;

        ExpandStructurePath(path, 0, nodeId, session, 0);
    }

    private void ExpandStructurePath(IReadOnlyList<Guid> path, int ancestorIndex, Guid nodeId, EditingSession session, int retries)
    {
        if (!StructureInspectorTab.IsSelected ||
            !ReferenceEquals(_viewModel.Session, session) ||
            _viewModel.StructurePanel.SelectedNodeId != nodeId)
            return;

        var currentPath = new List<Guid>();
        if (!TryBuildStructurePath(_viewModel.StructurePanel.Items, nodeId, currentPath))
            return;
        if (!path.SequenceEqual(currentPath))
        {
            ExpandStructurePath(currentPath, 0, nodeId, session, 0);
            return;
        }

        if (ancestorIndex >= path.Count - 1)
        {
            if (FindStructureHeader(nodeId) is { } header)
            {
                header.BringIntoView();
                Dispatcher.UIThread.Post(() => EnsureStructureHeaderVisible(nodeId, session, 0),
                    DispatcherPriority.Render);
                return;
            }

            if (retries < 3)
                Dispatcher.UIThread.Post(() => ExpandStructurePath(path, ancestorIndex, nodeId, session, retries + 1),
                    DispatcherPriority.Render);
            return;
        }

        var ancestor = FindStructureContainer(path[ancestorIndex]);
        if (ancestor is null)
        {
            if (retries < 3)
                Dispatcher.UIThread.Post(() => ExpandStructurePath(path, ancestorIndex, nodeId, session, retries + 1),
                    DispatcherPriority.Render);
            return;
        }

        if (!ancestor.IsExpanded)
            ancestor.IsExpanded = true;

        Dispatcher.UIThread.Post(() => ExpandStructurePath(path, ancestorIndex + 1, nodeId, session, 0),
            DispatcherPriority.Render);
    }

    private void EnsureStructureHeaderVisible(Guid nodeId, EditingSession session, int retries)
    {
        if (!StructureInspectorTab.IsSelected ||
            !ReferenceEquals(_viewModel.Session, session) ||
            _viewModel.StructurePanel.SelectedNodeId != nodeId)
            return;

        var header = FindStructureHeader(nodeId);
        var scrollViewer = StructureTree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (header is null || scrollViewer is null || scrollViewer.Viewport.Width <= 0 || scrollViewer.Viewport.Height <= 0)
        {
            if (retries < 4)
                Dispatcher.UIThread.Post(() => EnsureStructureHeaderVisible(nodeId, session, retries + 1),
                    DispatcherPriority.Render);
            return;
        }

        var headerOrigin = header.TranslatePoint(new Avalonia.Point(0, 0), scrollViewer);
        if (headerOrigin is not Avalonia.Point origin)
        {
            if (retries < 4)
                Dispatcher.UIThread.Post(() => EnsureStructureHeaderVisible(nodeId, session, retries + 1),
                    DispatcherPriority.Render);
            return;
        }

        var headerBounds = new Avalonia.Rect(origin, header.Bounds.Size);
        var offset = scrollViewer.Offset;
        var viewport = scrollViewer.Viewport;
        var extent = scrollViewer.Extent;
        var desiredX = offset.X;
        var desiredY = offset.Y;

        if (headerBounds.Left < 0)
            desiredX += headerBounds.Left;
        else if (headerBounds.Right > viewport.Width)
            desiredX += headerBounds.Right - viewport.Width;

        if (headerBounds.Top < 0)
            desiredY += headerBounds.Top;
        else if (headerBounds.Bottom > viewport.Height)
            desiredY += headerBounds.Bottom - viewport.Height;

        desiredX = Math.Clamp(desiredX, 0, Math.Max(0, extent.Width - viewport.Width));
        desiredY = Math.Clamp(desiredY, 0, Math.Max(0, extent.Height - viewport.Height));
        var needsOffset = Math.Abs(desiredX - offset.X) > 0.5 || Math.Abs(desiredY - offset.Y) > 0.5;
        if (needsOffset)
            scrollViewer.Offset = new Avalonia.Vector(desiredX, desiredY);

        var headerFullyVisible = headerBounds.Left >= 0 && headerBounds.Top >= 0 &&
                                 headerBounds.Right <= viewport.Width && headerBounds.Bottom <= viewport.Height;
        if ((needsOffset || !headerFullyVisible || retries == 0) && retries < 4)
        {
            Dispatcher.UIThread.Post(() => EnsureStructureHeaderVisible(nodeId, session, retries + 1),
                DispatcherPriority.Render);
        }
    }

    private TreeViewItem? FindStructureContainer(Guid nodeId) => StructureTree.GetVisualDescendants()
        .OfType<TreeViewItem>()
        .FirstOrDefault(candidate => candidate.DataContext is StructurePanelItem item && item.MathNodeId == nodeId);

    private Border? FindStructureHeader(Guid nodeId) => StructureTree.GetVisualDescendants()
        .OfType<Border>()
        .FirstOrDefault(candidate => candidate.Classes.Contains("structure-tree-entry") &&
                                     candidate.DataContext is StructurePanelItem item &&
                                     item.MathNodeId == nodeId);

    private static bool TryBuildStructurePath(IEnumerable<StructurePanelItem> items, Guid nodeId, List<Guid> path)
    {
        foreach (var item in items)
        {
            path.Add(item.MathNodeId);
            if (item.MathNodeId == nodeId || TryBuildStructurePath(item.Children, nodeId, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private void OnToolModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, ToolModeTabs))
            return;

        if (e.Source is Control source &&
            !ReferenceEquals(source, ToolModeTabs) &&
            !ReferenceEquals(source, ToolModeTabs.SelectedItem))
            return;

        if (ToolModeTabs.SelectedIndex == 1)
        {
            SetPaletteMode(SymbolPaletteMode.Symbols);
            return;
        }

        if (ToolModeTabs.SelectedIndex == 2)
            SetPaletteMode(EmojiModeButton.IsChecked == true
                ? SymbolPaletteMode.Emoji
                : SymbolPaletteMode.Unicode);
    }

    private void OnUnicodeModeClick(object? sender, RoutedEventArgs e) =>
        SetPaletteMode(SymbolPaletteMode.Unicode);

    private void OnEmojiModeClick(object? sender, RoutedEventArgs e) =>
        SetPaletteMode(SymbolPaletteMode.Emoji);

    private void OnSymbolPalettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SymbolPaletteViewModel.SelectedPaletteEntry) or
            nameof(SymbolPaletteViewModel.SelectedUnicodeEntry) or
            nameof(SymbolPaletteViewModel.SelectedEmojiEntry) or
            nameof(SymbolPaletteViewModel.SelectedPaletteGlyphCoverage) or
            nameof(SymbolPaletteViewModel.SelectedUnicodeGlyphCoverage) or
            nameof(SymbolPaletteViewModel.SelectedEmojiGlyphCoverage))
            UpdatePaletteCoverageVisuals();

        if (e.PropertyName is nameof(SymbolPaletteViewModel.UnicodePageIndex) or
            nameof(SymbolPaletteViewModel.UnicodePageCount))
            UpdateUnicodePageIndicator();
    }

    private void UpdatePaletteCoverageVisuals()
    {
        var palette = _viewModel.SymbolPalette;
        SelectedSymbolDetails.IsVisible = palette.SelectedPaletteEntry is not null;
        SelectedUnicodeDetails.IsVisible = palette.SelectedUnicodeEntry is not null;
        SelectedEmojiDetails.IsVisible = palette.SelectedEmojiEntry is not null;

        SelectedSymbolGlyphStatus.Text = DescribeGlyphCoverage(
            palette.SelectedPaletteGlyphCoverage);
        SelectedUnicodeGlyphStatus.Text = DescribeGlyphCoverage(
            palette.SelectedUnicodeGlyphCoverage);
        SelectedEmojiGlyphStatus.Text = DescribeGlyphCoverage(
            palette.SelectedEmojiGlyphCoverage);

        SelectedSymbolGlyphStatus.IsVisible = palette.SelectedPaletteGlyphCoverage is not null;
        SelectedUnicodeGlyphStatus.IsVisible = palette.SelectedUnicodeGlyphCoverage is not null;
        SelectedEmojiGlyphStatus.IsVisible = palette.SelectedEmojiGlyphCoverage is not null;
    }

    private void UpdateUnicodePageIndicator()
    {
        var current = _viewModel.SymbolPalette.UnicodePageIndex + 1;
        var total = _viewModel.SymbolPalette.UnicodePageCount;
        UnicodePageIndicator.Text = $"{current.ToString("N0", CultureInfo.CurrentCulture)} / {total.ToString("N0", CultureInfo.CurrentCulture)}";
    }

    private static string DescribeGlyphCoverage(MathGlyphCoverageResult? result)
    {
        if (result is null)
            return "항목을 선택하면 기본 글꼴의 문자표를 확인합니다.";

        var status = result.Status switch
        {
            MathGlyphCoverageStatus.Present => "있음",
            MathGlyphCoverageStatus.Missing => "없음",
            _ => "확인할 수 없음"
        };
        return $"기본 글꼴 문자표: {status}";
    }

    private void SetPaletteMode(SymbolPaletteMode mode)
    {
        if (_viewModel.SymbolPalette.Mode != mode)
            _viewModel.SymbolPalette.Mode = mode;

        if (mode == SymbolPaletteMode.Symbols)
            return;

        UnicodeBrowserPanel.IsVisible = mode == SymbolPaletteMode.Unicode;
        EmojiBrowserPanel.IsVisible = mode == SymbolPaletteMode.Emoji;
        UnicodeModeButton.IsChecked = mode == SymbolPaletteMode.Unicode;
        EmojiModeButton.IsChecked = mode == SymbolPaletteMode.Emoji;
    }
}

public sealed class UnicodePreviewFontSizeConverter : IValueConverter
{
    public static UnicodePreviewFontSizeConverter Instance { get; } = new();

    private UnicodePreviewFontSizeConverter()
    {
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 10d : 21d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MathTypesettingStyleDisplayConverter : IValueConverter
{
    public static MathTypesettingStyleDisplayConverter Instance { get; } = new();

    private MathTypesettingStyleDisplayConverter()
    {
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MathTypesettingStyle.Display => "독립 수식 (Display)",
        MathTypesettingStyle.Text => "텍스트 크기 (Text)",
        MathTypesettingStyle.Script => "첨자 (Script)",
        MathTypesettingStyle.ScriptScript => "작은 첨자 (ScriptScript)",
        null => "상속",
        _ => value.ToString() ?? "상속"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MathTextRoleDisplayConverter : IValueConverter
{
    public static MathTextRoleDisplayConverter Instance { get; } = new();

    private MathTextRoleDisplayConverter()
    {
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MathTextRole.Automatic => "자동 (기존 수식 표시)",
        MathTextRole.Identifier => "식별자 (기울임)",
        MathTextRole.MathSymbol => "수학 기호 (직립)",
        MathTextRole.Number => "숫자 (직립)",
        MathTextRole.PlainText => "일반 텍스트 (직립)",
        MathTextRole.Unit => "단위 (직립)",
        MathTextRole.Condition => "조건 (직립)",
        null => "선택된 수식 텍스트 없음",
        _ => value.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class GroupRowSpacingDisplayConverter : IValueConverter
{
    public static GroupRowSpacingDisplayConverter Instance { get; } = new();

    private GroupRowSpacingDisplayConverter()
    {
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double spacing when double.IsFinite(spacing) => spacing.ToString("R", CultureInfo.InvariantCulture),
        null => "현재 그룹 값을 확인할 수 없음",
        _ => value.ToString() ?? "현재 그룹 값을 확인할 수 없음"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
