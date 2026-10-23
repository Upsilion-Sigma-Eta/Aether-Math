using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class GroupRowSpacingCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("GRS-CORE", "현재 그룹 간격 변경의 범위·유효성·변경 없음 원자성", CorePatchContract);
        yield return new("GRS-DRAFT-STALE", "행 간격 입력·지원 범위·대상 이동 뒤 오래된 초안 보존", DraftAndStaleBoundaries);
        yield return new("GRS-LEGACY-PRECISION", "기존 큰 값·왕복 정밀도·스타일만 편집", LegacyValuesAndPrecision);
        yield return new("GRS-GEOMETRY", "그룹 내부 행 간격과 그룹 사이 옵션 간격의 분리", SpacingGeometry);
        yield return new("GRS-ATOMIC-HISTORY", "스타일·고정·간격 단일 적용 및 Undo/Redo/초기화", AtomicHistoryAndReset);
        yield return new("GRS-IO-CLONE", "복제·실제 파일·복구의 행 간격 보존", CloneAndPersistenceRoundTrip);
        yield return new("GRS-LATEX-DIAGNOSTICS", "다중 행 그룹별 LaTeX 간격 진단과 비차단 상태", LatexLayoutDiagnostics);
        yield return new("GRS-PREVIEW-SVG", "출력 모드 고정 레이아웃과 실제 SVG의 간격 소비", PreviewAndSvgSpacing);
        yield return new("SESSION-ACTIVE-TARGET-NOTIFY", "속성 알림 전에 활성 행·속성 대상 동기화", ActiveTargetNotificationsAreSynchronous);
    }

    private static void ActiveTargetNotificationsAreSynchronous()
    {
        var firstGroup = Group(24, "첫 그룹 첫 행", "첫 그룹 둘째 행");
        var secondGroup = Group(68, "둘째 그룹 첫 행", "둘째 그룹 둘째 행");
        var session = new EditingSession(Workspace(firstGroup, secondGroup));
        session.MarkSaved(session.CaptureSnapshot());
        var panel = new MathPropertiesPanelViewModel(session, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        panel.DraftGroupRowSpacingText = "77.25";
        Qa.Check(panel.CanApply, "활성 행 알림 검증용 간격 초안을 만들 수 없음");

        var observedSessionProperties = new HashSet<string>(StringComparer.Ordinal);
        var panelSpacingNotifications = 0;
        var stalePanelSpacingNotifications = 0;
        panel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MathPropertiesPanelViewModel.CurrentGroupRowSpacing))
                return;

            panelSpacingNotifications++;
            var active = FindActiveRowIndependently(session, "속성 패널 간격 알림");
            Qa.Equal(active.Group.RowSpacing, panel.CurrentGroupRowSpacing,
                "현재 그룹 간격 알림에서 새 활성 그룹 값을 읽을 수 없음");
            if (panel.IsDraftStale)
            {
                stalePanelSpacingNotifications++;
                Qa.Equal("77.25", panel.DraftGroupRowSpacingText,
                    "활성 그룹이 바뀌며 오래된 간격 초안이 변경됨");
                Qa.Check(!panel.CanApply, "오래된 간격 초안이 적용 가능한 상태로 알림됨");
            }
        };

        var observedActiveProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(EditingSession.Workspace),
            nameof(EditingSession.Caret),
            nameof(EditingSession.Selection),
            nameof(EditingSession.SelectedNodeId),
            nameof(EditingSession.IsNodeSelected),
            nameof(EditingSession.ActiveGroupNumber),
            nameof(EditingSession.ActiveRowNumber),
            nameof(EditingSession.ActiveRowCount)
        };
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null || !observedActiveProperties.Contains(args.PropertyName))
                return;

            observedSessionProperties.Add(args.PropertyName);
            AssertActiveTargetIsCurrent(session, args.PropertyName);
        };

        var originalDocument = CaptureState(session);
        var secondGroupFirstText = (TextNode)secondGroup.Rows[0].Root;
        AssertNavigation(session, originalDocument,
            () => session.TrySetCaret(new MathCaret(secondGroupFirstText.Id, 2)),
            "다른 그룹·행으로 caret 이동");

        var firstGroupSecondText = (TextNode)firstGroup.Rows[1].Root;
        AssertNavigation(session, originalDocument,
            () => session.TrySetSelection(new MathSelection(
                new MathCaret(firstGroupSecondText.Id, 0), new MathCaret(firstGroupSecondText.Id, 1))),
            "다른 그룹·행의 부분 텍스트 선택");

        var secondRowSecondText = (TextNode)secondGroup.Rows[1].Root;
        AssertNavigation(session, originalDocument,
            () => session.TrySelectNode(secondRowSecondText.Id),
            "다른 그룹·행의 전체 노드 선택");
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == secondRowSecondText.Id,
            "전체 노드 선택의 활성 대상이 선택 노드가 아님");

        var beforeAddRow = CaptureState(session);
        Qa.Check(session.AddRowAfterCurrent(), "현재 그룹에 행 구조 편집 실패");
        AssertActiveLocation(session, 2, 3, 3, 68, "행 추가 Commit 직후");
        AssertStaleSpacingDraft(panel, "행 추가 Commit 뒤");
        Qa.Check(session.CaptureSnapshot().StateId != beforeAddRow.StateId && session.Revision == beforeAddRow.Revision + 1,
            "행 추가가 단일 구조 Commit으로 알리지 않음");

        session.Undo();
        AssertActiveLocation(session, 2, 2, 2, 68, "행 추가 Undo 직후");
        AssertStaleSpacingDraft(panel, "행 추가 Undo 뒤");
        session.Redo();
        AssertActiveLocation(session, 2, 3, 3, 68, "행 추가 Redo 직후");
        AssertStaleSpacingDraft(panel, "행 추가 Redo 뒤");

        var beforeAddGroup = CaptureState(session);
        Qa.Check(session.AddGroupAfterCurrent(), "그룹 구조 편집 실패");
        AssertActiveLocation(session, 3, 1, 1, 68, "그룹 추가 Commit 직후");
        AssertStaleSpacingDraft(panel, "그룹 추가 Commit 뒤");
        Qa.Check(session.CaptureSnapshot().StateId != beforeAddGroup.StateId && session.Revision == beforeAddGroup.Revision + 1,
            "그룹 추가가 단일 구조 Commit으로 알리지 않음");

        session.Undo();
        AssertActiveLocation(session, 2, 3, 3, 68, "그룹 추가 Undo 직후");
        AssertStaleSpacingDraft(panel, "그룹 추가 Undo 뒤");
        session.Redo();
        AssertActiveLocation(session, 3, 1, 1, 68, "그룹 추가 Redo 직후");
        AssertStaleSpacingDraft(panel, "그룹 추가 Redo 뒤");

        foreach (var propertyName in observedActiveProperties)
            Qa.Check(observedSessionProperties.Contains(propertyName), $"속성 알림을 거치지 않음: {propertyName}");
        Qa.Check(panelSpacingNotifications > 0 && stalePanelSpacingNotifications > 0,
            "현재 그룹 간격 또는 오래된 초안 상태의 패널 알림을 관찰하지 못함");
    }

    private static void AssertNavigation(EditingSession session, SessionState original,
        Func<bool> navigate, string label)
    {
        Qa.Check(navigate(), label + " 실패");
        var after = CaptureState(session);
        Qa.Equal(original.Fingerprint, after.Fingerprint, label + "이 작업 내용을 변경");
        Qa.Equal(original.Revision, after.Revision, label + "이 Revision을 변경");
        Qa.Equal(original.StateId, after.StateId, label + "이 StateId를 변경");
        Qa.Equal(original.IsDirty, after.IsDirty, label + "이 저장 상태를 변경");
        Qa.Equal(original.CanUndo, after.CanUndo, label + "이 Undo 이력을 변경");
        Qa.Equal(original.CanRedo, after.CanRedo, label + "이 Redo 이력을 변경");
        Qa.Equal(string.Join(',', original.RowIds), string.Join(',', after.RowIds), label + "이 행 ID를 변경");
        Qa.Equal(string.Join(',', original.NodeIds), string.Join(',', after.NodeIds), label + "이 노드 ID를 변경");
    }

    private static void AssertActiveTargetIsCurrent(EditingSession session, string propertyName)
    {
        var active = FindActiveRowIndependently(session, propertyName);
        Qa.Equal(active.GroupIndex + 1, session.ActiveGroupNumber,
            $"{propertyName} 알림 시 활성 그룹 번호가 오래됨");
        Qa.Equal(active.RowIndex + 1, session.ActiveRowNumber,
            $"{propertyName} 알림 시 활성 행 번호가 오래됨");
        Qa.Equal(active.Group.Rows.Length, session.ActiveRowCount,
            $"{propertyName} 알림 시 활성 그룹 행 수가 오래됨");

        Qa.Check(session.TryCaptureMathPropertyTarget(MathPropertyScope.CurrentGroup, out var groupTarget) &&
                 groupTarget is not null && groupTarget.GroupId == active.Group.Id && groupTarget.RowId is null,
            $"{propertyName} 알림 시 CurrentGroup 대상 ID가 오래됨");
        Qa.Check(session.TryCaptureMathPropertyTarget(MathPropertyScope.CurrentRow, out var rowTarget) &&
                 rowTarget is not null && rowTarget.GroupId == active.Group.Id && rowTarget.RowId == active.Row.Id,
            $"{propertyName} 알림 시 CurrentRow 대상 ID가 오래됨");
    }

    private static (int GroupIndex, int RowIndex, EquationGroup Group, EquationRow Row)
        FindActiveRowIndependently(EditingSession session, string label)
    {
        var activeNodeId = session.IsNodeSelected ? session.SelectedNodeId : session.Caret.NodeId;
        if (activeNodeId is not Guid nodeId)
            throw new InvalidOperationException(label + "의 활성 노드 ID가 없음");
        if (!MathTree.TryFindContainingRow(session.Workspace, nodeId,
                out var groupIndex, out var rowIndex, out var group, out var row) || group is null || row is null)
            throw new InvalidOperationException(label + "에서 실제 AST 포함 행을 찾지 못함");
        return (groupIndex, rowIndex, group!, row!);
    }

    private static void AssertActiveLocation(EditingSession session, int groupNumber, int rowNumber,
        int rowCount, double rowSpacing, string label)
    {
        AssertActiveTargetIsCurrent(session, label);
        var active = FindActiveRowIndependently(session, label);
        Qa.Equal(groupNumber, active.GroupIndex + 1, label + " AST 그룹 위치");
        Qa.Equal(rowNumber, active.RowIndex + 1, label + " AST 행 위치");
        Qa.Equal(rowCount, active.Group.Rows.Length, label + " AST 그룹 행 수");
        Qa.Equal(rowSpacing, active.Group.RowSpacing, label + " AST 그룹 간격");
    }

    private static void AssertStaleSpacingDraft(MathPropertiesPanelViewModel panel, string label)
    {
        Qa.Equal(68d, panel.CurrentGroupRowSpacing, label + "의 현재 그룹 간격 표시");
        Qa.Equal("77.25", panel.DraftGroupRowSpacingText, label + "의 미적용 초안 보존");
        Qa.Check(panel.IsDraftStale && !panel.CanApply, label + "의 오래된 초안 차단 상태");
    }

    private static void CorePatchContract()
    {
        var group = Group(24, "a", "b");
        var session = new EditingSession(Workspace(group));
        session.MarkSaved(session.CaptureSnapshot());

        var zeroTarget = Capture(session, MathPropertyScope.CurrentGroup);
        var beforeZero = CaptureState(session);
        var appliedZero = session.ApplyMathProperties(zeroTarget,
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 0));
        Qa.Equal(MathPropertyChangeStatus.Applied, appliedZero.Status, "0px 경계값 적용 상태");
        Qa.Equal(beforeZero.Revision + 1, session.Revision, "간격 적용은 한 번만 커밋");
        Qa.Equal(0d, session.Workspace.Groups[0].RowSpacing, "0px 간격 값");
        Qa.Check(session.IsDirty && session.CanUndo, "간격 적용의 미저장·편집 이력 상태");

        var zeroRevision = session.Revision;
        var zeroStateId = session.CaptureSnapshot().StateId;
        var sameZero = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 0d));
        Qa.Equal(MathPropertyChangeStatus.NoChange, sameZero.Status, "동일 수치 간격의 변경 없음 상태");
        Qa.Equal(zeroRevision, session.Revision, "변경 없음이 Revision을 증가");
        Qa.Equal(zeroStateId, session.CaptureSnapshot().StateId, "변경 없음이 StateId를 바꿈");

        foreach (var invalid in new[] { -0.001, 512.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var before = CaptureState(session);
            var result = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentGroup),
                new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: invalid));
            Qa.Equal(MathPropertyChangeStatus.InvalidValue, result.Status, $"잘못된 새 간격 거절 {invalid:R}");
            AssertUnchanged(before, session, $"잘못된 간격 원자 거절 {invalid:R}");
        }

        var maxValue = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 512));
        Qa.Equal(MathPropertyChangeStatus.Applied, maxValue.Status, "512px 경계값 수용");
        Qa.Equal(512d, session.Workspace.Groups[0].RowSpacing, "512px 저장값");

        foreach (var unsupportedScope in new[]
                 {
                     MathPropertyScope.SelectionNode,
                     MathPropertyScope.CurrentRow,
                     MathPropertyScope.WorkspaceDefault
                 })
        {
            var beforeUnsupported = CaptureState(session);
            var unsupported = session.ApplyMathProperties(Capture(session, unsupportedScope),
                new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 36));
            Qa.Equal(MathPropertyChangeStatus.UnsupportedScope, unsupported.Status,
                $"{unsupportedScope} 간격 patch 거절");
            AssertUnchanged(beforeUnsupported, session, $"지원하지 않는 {unsupportedScope} 범위의 간격 patch");
        }

        var combinedBefore = CaptureState(session);
        var unsupportedCombined = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentRow),
            new MathPropertiesPatch(new MathStylePatch(FontSize: 40), GroupRowSpacing: 36));
        Qa.Equal(MathPropertyChangeStatus.UnsupportedScope, unsupportedCombined.Status,
            "CurrentRow에서 스타일+간격 patch를 원자적으로 거절");
        AssertUnchanged(combinedBefore, session, "지원하지 않는 범위에서 스타일만 부분 적용");

        var invalidCombinedBefore = CaptureState(session);
        var invalidCombined = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(FontSize: double.NaN), GroupRowSpacing: 36));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, invalidCombined.Status,
            "무효 글꼴 크기와 유효 간격을 원자적으로 거절");
        AssertUnchanged(invalidCombinedBefore, session, "무효 스타일이 유효 간격을 부분 적용");

        var revisionTarget = Capture(session, MathPropertyScope.CurrentGroup);
        var revisionLeaf = (TextNode)session.Workspace.Groups[0].Rows[0].Root;
        Qa.Caret(session, revisionLeaf.Id, 0);
        var revisionState = CaptureState(session);
        Qa.Check(session.InsertText("x"), "오래된 Revision 대상 검증 데이터 입력");
        var afterEditSpacing = session.Workspace.Groups[0].RowSpacing;
        var beforeStaleRevisionAttempt = CaptureState(session);
        var staleRevision = session.ApplyMathProperties(revisionTarget,
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 88));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, staleRevision.Status, "revision 변경 뒤 오래된 그룹 대상");
        Qa.Equal(afterEditSpacing, session.Workspace.Groups[0].RowSpacing, "오래된 revision에서 간격 무변경");
        Qa.Check(session.Revision > revisionState.Revision, "오래된 대상 작성용 편집이 커밋되지 않음");
        AssertUnchanged(beforeStaleRevisionAttempt, session, "오래된 revision 요청이 현재 편집 상태를 변경");

        var selectionTarget = Capture(session, MathPropertyScope.CurrentGroup);
        var secondRowText = (TextNode)session.Workspace.Groups[0].Rows[1].Root;
        Qa.Check(session.TrySelectNode(secondRowText.Id), "오래된 선택 검증용 노드 선택");
        var selectedState = CaptureState(session);
        var staleSelection = session.ApplyMathProperties(selectionTarget,
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 89));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, staleSelection.Status, "선택 이동 뒤 오래된 그룹 대상");
        Qa.Equal(512d, session.Workspace.Groups[0].RowSpacing, "오래된 선택에서 간격 무변경");
        Qa.Equal(selectedState.SelectedNodeId, session.SelectedNodeId, "오래된 선택 적용이 현재 선택을 변경");
        AssertUnchanged(selectedState, session, "오래된 선택 patch가 현재 편집 상태를 변경");

        var foreignTarget = Capture(session, MathPropertyScope.CurrentGroup);
        var otherSession = new EditingSession(session.Workspace);
        var foreignBefore = CaptureState(otherSession);
        var foreignResult = otherSession.ApplyMathProperties(foreignTarget,
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 90));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, foreignResult.Status, "다른 세션의 그룹 대상 재사용 거절");
        AssertUnchanged(foreignBefore, otherSession, "세션 token이 다른 대상 거절");
    }

    private static void DraftAndStaleBoundaries()
    {
        var first = Group(18, "first", "second");
        var otherGroup = Group(31, "other-one", "other-two");
        var session = new EditingSession(Workspace(first, otherGroup));
        var properties = new MathPropertiesPanelViewModel(session, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        Qa.Check(properties.CanEditGroupRowSpacing, "현재 그룹 행 간격 편집기 비활성");
        Qa.Equal(18d, properties.CurrentGroupRowSpacing, "현재 그룹 저장 간격");
        Qa.Equal(18d.ToString("R", CultureInfo.InvariantCulture), properties.DraftGroupRowSpacingText,
            "초안 간격 왕복 정밀도 표시");
        Qa.Check(properties.GroupRowSpacingNotice.Contains("그룹 사이", StringComparison.Ordinal),
            "행 간격과 그룹 사이 간격 구분 안내");

        foreach (var invalid in new[] { "n/a", "-1", "513", "NaN", "Infinity", "1e309" })
        {
            properties.DraftGroupRowSpacingText = invalid;
            Qa.Check(!string.IsNullOrWhiteSpace(properties.GroupRowSpacingValidationError),
                $"잘못된 초안 진단 누락: {invalid}");
            Qa.Check(!properties.CanApply, $"잘못된 간격 초안 Apply 허용: {invalid}");
        }
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(string.IsNullOrWhiteSpace(properties.GroupRowSpacingValidationError), "ResetDraft 뒤 간격 오류 잔존");
        Qa.Check(!properties.CanApply, "ResetDraft 뒤 untouched 간격 Apply 가능");

        properties.DraftGroupRowSpacingText = "44.5";
        properties.DraftFontFamily = "Spacing Draft Face";
        Qa.Check(properties.CanApply, "유효한 CurrentGroup 간격 및 스타일 초안 적용 불가");
        var draftBeforeMove = properties.DraftGroupRowSpacingText;
        var currentOtherText = (TextNode)otherGroup.Rows[0].Root;
        Qa.Check(session.TrySelectNode(currentOtherText.Id), "다른 그룹 선택 실패");
        Qa.Check(properties.IsDraftStale && !properties.CanApply, "그룹 이동 뒤 간격 초안이 오래된 상태로 차단되지 않음");
        Qa.Equal(draftBeforeMove, properties.DraftGroupRowSpacingText, "그룹 이동이 변경된 간격 초안을 지움");
        Qa.Equal("Spacing Draft Face", properties.DraftFontFamily, "그룹 이동이 다른 스타일 초안을 지움");
        var staleFingerprint = Qa.Fingerprint(session.Workspace);
        var staleRevision = session.Revision;
        properties.ApplyCommand.Execute(null);
        Qa.Equal(staleFingerprint, Qa.Fingerprint(session.Workspace), "오래된 간격·스타일 초안 일부 적용");
        Qa.Equal(staleRevision, session.Revision, "오래된 간격·스타일 초안이 revision을 변경");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && !properties.CanApply, "ResetDraft 뒤 새 그룹 기준값 복구");
        Qa.Equal(31d.ToString("R", CultureInfo.InvariantCulture), properties.DraftGroupRowSpacingText,
            "새 그룹 기준 간격 재포착");

        properties.SelectedScope = MathPropertyScope.CurrentGroup;
        properties.DraftGroupRowSpacingText = "46";
        var beforeScopeMove = properties.DraftGroupRowSpacingText;
        properties.SelectedScope = MathPropertyScope.CurrentRow;
        Qa.Check(properties.IsDraftStale && !properties.CanApply,
            "간격 초안 뒤 범위 이동이 오래된 상태로 차단되지 않음");
        Qa.Equal(beforeScopeMove, properties.DraftGroupRowSpacingText,
            "범위 이동이 변경된 간격 초안을 지움");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && !properties.CanApply,
            "범위 이동 후 ResetDraft가 현재 기준을 복구하지 않음");

        // 새로 진입한 비지원 범위에서는 간격 초안을 버리고 다른 스타일만 부분 적용해서는 안 된다.
        var freshSession = new EditingSession(Workspace(Group(21, "row one", "row two")));
        var freshPanel = new MathPropertiesPanelViewModel(freshSession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentRow
        };
        Qa.Check(!freshPanel.CanEditGroupRowSpacing, "현재 행 범위에서 간격 편집 가능 상태");
        freshPanel.DraftGroupRowSpacingText = "39";
        freshPanel.DraftFontFamily = "Must Not Partially Apply";
        Qa.Check(!freshPanel.CanApply, "새 비그룹 간격 의도가 전체 적용을 막지 않음");
        Qa.Check(!string.IsNullOrWhiteSpace(freshPanel.GroupRowSpacingDisabledReason),
            "CurrentGroup 이외 범위의 간격 편집 불가 사유 누락");
        var freshBefore = CaptureState(freshSession);
        freshPanel.ApplyCommand.Execute(null);
        AssertUnchanged(freshBefore, freshSession, "지원하지 않는 범위의 간격·스타일 초안 부분 적용");

        var invalidStyleSession = new EditingSession(Workspace(Group(17, "invalid style", "valid spacing")));
        var invalidStylePanel = new MathPropertiesPanelViewModel(invalidStyleSession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        invalidStylePanel.DraftGroupRowSpacingText = "33";
        invalidStylePanel.DraftForeground = "not-a-color";
        Qa.Check(!invalidStylePanel.CanApply, "무효 색상과 유효 간격 초안 적용 허용");
        var invalidStyleBefore = CaptureState(invalidStyleSession);
        invalidStylePanel.ApplyCommand.Execute(null);
        AssertUnchanged(invalidStyleBefore, invalidStyleSession, "무효 색상과 유효 간격의 부분 적용");

        // 실제 편집 또는 Session 교체로 대상이 오래되어도 사용자가 초안을 초기화할 때까지 입력값을 보존한다.
        var editSession = new EditingSession(Workspace(Group(20, "edit target", "edit row")));
        var editPanel = new MathPropertiesPanelViewModel(editSession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        editPanel.DraftGroupRowSpacingText = "41";
        var activeText = (TextNode)editSession.Workspace.Groups[0].Rows[0].Root;
        Qa.Caret(editSession, activeText.Id, 4);
        Qa.Check(editPanel.IsDraftStale && !editPanel.CanApply, "caret 이동 뒤 간격 초안 적용 차단");
        Qa.Equal("41", editPanel.DraftGroupRowSpacingText, "caret 이동이 미적용 간격 초안을 지움");
        var beforeRevisionEdit = editSession.Revision;
        Qa.Caret(editSession, activeText.Id, 4);
        Qa.Check(editSession.InsertText("!"), "Revision 변경 검증용 텍스트 삽입 실패");
        Qa.Check(editSession.Revision > beforeRevisionEdit, "텍스트 삽입이 실제 Revision을 증가시키지 않음");
        Qa.Check(editPanel.IsDraftStale && !editPanel.CanApply, "실제 Revision 변경 뒤 간격 초안 적용 가능");
        Qa.Equal("41", editPanel.DraftGroupRowSpacingText, "실제 편집이 미적용 간격 초안을 지움");

        var replacementSession = new EditingSession(Workspace(Group(10, "replacement", "row")));
        editPanel.SetSession(replacementSession);
        Qa.Check(editPanel.IsDraftStale && !editPanel.CanApply, "Session 교체 뒤 간격 초안 적용 차단");
        Qa.Equal("41", editPanel.DraftGroupRowSpacingText, "Session 교체가 대기 중인 간격 초안을 지움");
        editPanel.ResetDraftCommand.Execute(null);
        Qa.Check(!editPanel.IsDraftStale && !editPanel.CanApply, "Session 교체 뒤 초안 초기화로 기준 복구");
        Qa.Equal(10d.ToString("R", CultureInfo.InvariantCulture), editPanel.DraftGroupRowSpacingText,
            "교체된 Session의 간격 기준 재포착");
    }

    private static async Task LegacyValuesAndPrecision()
    {
        using var directory = new TempDirectory();
        var legacyGroup = Group(600, "legacy first", "legacy second");
        var legacySession = new EditingSession(Workspace(legacyGroup));
        var legacyPanel = new MathPropertiesPanelViewModel(legacySession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        Qa.Equal(600d.ToString("R", CultureInfo.InvariantCulture), legacyPanel.DraftGroupRowSpacingText,
            "기존 600 간격 입력을 그대로 표시");
        Qa.Check(legacyPanel.GroupRowSpacingValidationError.Length == 0,
            "변경하지 않은 기존 600을 새 잘못된 입력으로 처리");
        legacyPanel.DraftFontSizeText = "36";
        Qa.Check(legacyPanel.CanApply, "기존 600이 무관한 스타일 단독 적용을 막음");
        legacyPanel.ApplyCommand.Execute(null);
        Qa.Equal(600d, legacySession.Workspace.Groups[0].RowSpacing,
            "스타일 단독 적용이 기존 600을 조용히 제한");
        var legacyPath = directory.File("legacy-spacing.aethermath");
        var fileService = new WorkspaceFileService();
        await fileService.SaveAsync(legacyPath, legacySession.CaptureSnapshot());
        var legacyReopened = await fileService.OpenAsync(legacyPath);
        Qa.Equal(600d, legacyReopened.Groups[0].RowSpacing, "기존 600 간격의 실제 파일 왕복");

        const double precise = 28.1234567890123;
        var preciseSession = new EditingSession(Workspace(Group(precise, "precise first", "precise second")));
        var precisePanel = new MathPropertiesPanelViewModel(preciseSession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        var canonical = precise.ToString("R", CultureInfo.InvariantCulture);
        Qa.Equal(canonical, precisePanel.DraftGroupRowSpacingText, "소수 간격을 R 형식으로 표시");
        var initialState = CaptureState(preciseSession);
        var alternateSameValue = precise.ToString("0.000000000000000", CultureInfo.InvariantCulture);
        precisePanel.DraftGroupRowSpacingText = alternateSameValue;
        Qa.Check(!precisePanel.CanApply, "표기만 다른 동일 수치가 이력 변경으로 처리됨");
        precisePanel.ApplyCommand.Execute(null);
        AssertUnchanged(initialState, preciseSession, "같은 숫자의 다른 표기가 변경 없음으로 처리되지 않음");
        precisePanel.ResetDraftCommand.Execute(null);
        Qa.Equal(canonical, precisePanel.DraftGroupRowSpacingText, "ResetDraft가 R 정밀도를 잃음");

        precisePanel.DraftFontFamily = "Precision Face";
        Qa.Check(precisePanel.CanApply, "정밀도 기준에서 스타일만 적용할 수 없음");
        precisePanel.ApplyCommand.Execute(null);
        Qa.Equal(precise, preciseSession.Workspace.Groups[0].RowSpacing,
            "스타일 단독 적용이 기존 간격 double을 반올림");
        var precisePath = directory.File("precision-spacing.aethermath");
        await fileService.SaveAsync(precisePath, preciseSession.CaptureSnapshot());
        var preciseReopened = await fileService.OpenAsync(precisePath);
        Qa.Equal(precise, preciseReopened.Groups[0].RowSpacing, "28.1234567890123 JSON 실제 왕복");

        // A new explicit value over the UI/API limit is distinct from reading the
        // already-persisted legacy value above.
        var rejectNewLarge = new EditingSession(Workspace(Group(12, "new first", "new second")));
        var result = rejectNewLarge.ApplyMathProperties(Capture(rejectNewLarge, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 600));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, result.Status, "새 명시 간격 600 거절");
        Qa.Equal(12d, rejectNewLarge.Workspace.Groups[0].RowSpacing, "새 explicit 600 거절 뒤 기존 값 보존");
    }

    private static async Task SpacingGeometry()
    {
        var first = Group(12, "a1", "a2");
        var target = Group(37, "b1", "b2", "b3");
        var last = Group(19, "c1", "c2");
        var workspace = Workspace(first, target, last);
        var session = new EditingSession(workspace);
        var targetLeaf = (TextNode)target.Rows[0].Root;
        Qa.Caret(session, targetLeaf.Id, 1);
        session.MarkSaved(session.CaptureSnapshot());
        var beforeState = CaptureState(session);
        var options = new MathLayoutOptions(RowSpacing: 41);
        var metrics = new FixedMetrics();
        var beforeLayout = MathLayoutEngine.Layout(session.Workspace, metrics, options);

        AssertRowGap(beforeLayout, first.Rows[0], first.Rows[1], 12, "첫 그룹 내부 행 간격");
        AssertRowGap(beforeLayout, target.Rows[0], target.Rows[1], 37, "대상 그룹 첫 간격");
        AssertRowGap(beforeLayout, target.Rows[1], target.Rows[2], 37, "대상 그룹 두 번째 간격");
        AssertRowGap(beforeLayout, last.Rows[0], last.Rows[1], 19, "마지막 그룹 내부 행 간격");
        AssertGroupBoundaryGap(beforeLayout, first, target, options.RowSpacing, "그룹 간 옵션 간격 1");
        AssertGroupBoundaryGap(beforeLayout, target, last, options.RowSpacing, "그룹 간 옵션 간격 2");

        var result = session.ApplyMathProperties(Capture(session, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 53));
        Qa.Equal(MathPropertyChangeStatus.Applied, result.Status, "간격만 적용한 기하 변경");
        var afterLayout = MathLayoutEngine.Layout(session.Workspace, metrics, options);
        AssertRowGap(afterLayout, target.Rows[0], target.Rows[1], 53, "변경된 대상 그룹 행 간격");
        AssertRowGap(afterLayout, target.Rows[1], target.Rows[2], 53, "변경된 대상 그룹 두 번째 간격");
        AssertRowGap(afterLayout, first.Rows[0], first.Rows[1], 12, "비대상 그룹 간격 불변");
        AssertRowGap(afterLayout, last.Rows[0], last.Rows[1], 19, "뒤 그룹 간격 불변");
        AssertGroupBoundaryGap(afterLayout, first, target, options.RowSpacing, "첫 그룹 사이 옵션 간격 보존");
        AssertGroupBoundaryGap(afterLayout, target, last, options.RowSpacing, "두 번째 그룹 사이 옵션 간격 보존");
        Qa.Equal(beforeState.Caret, session.Caret, "간격 기하 변경 뒤 caret 보존");
        Qa.Equal(beforeState.Selection, session.Selection, "간격 기하 변경 뒤 selection 보존");
        Qa.Check(beforeState.StateId != session.CaptureSnapshot().StateId,
            "간격 기하 적용이 새 StateId를 만들지 않음");
        Qa.Equal(beforeState.Revision + 1, session.Revision, "간격 기하 적용이 단일 Revision이 아님");
        Qa.Equal(string.Join(',', beforeState.NodeIds), string.Join(',', AllNodeIds(session.Workspace)),
            "간격 기하 적용이 노드 ID를 변경");

        using var directory = new TempDirectory();
        var singleRowGroup = Group(13, "single-row only");
        var singleSession = new EditingSession(Workspace(singleRowGroup));
        var singlePanel = new MathPropertiesPanelViewModel(singleSession, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        Qa.Check(singlePanel.CanEditGroupRowSpacing &&
                 singlePanel.GroupRowSpacingNotice.Contains("화면상 변화가 없습니다", StringComparison.Ordinal) &&
                 string.IsNullOrWhiteSpace(singlePanel.GroupRowSpacingLatexDiagnostic),
            "단일 행 간격 안내 및 LaTeX 진단 경계가 맞지 않음");
        var singleBeforeLayout = MathLayoutEngine.Layout(singleSession.Workspace, metrics, options);
        var singleBeforeGeometry = GlyphGeometry(singleBeforeLayout);
        var singleBeforeState = CaptureState(singleSession);
        singlePanel.DraftGroupRowSpacingText = "27.5";
        Qa.Check(singlePanel.CanApply, "단일 행 그룹 간격 편집이 불가");
        singlePanel.ApplyCommand.Execute(null);
        Qa.Equal(27.5d, singleSession.Workspace.Groups[0].RowSpacing, "단일 행 그룹 새 간격 저장");
        Qa.Equal(singleBeforeState.Revision + 1, singleSession.Revision, "단일 행 간격 적용이 단일 commit이 아님");
        Qa.Check(singleSession.IsDirty && singleSession.CanUndo, "단일 행 간격 변경의 미저장·편집 이력 누락");
        var singleAfterLayout = MathLayoutEngine.Layout(singleSession.Workspace, metrics, options);
        Qa.Equal(singleBeforeLayout.Width, singleAfterLayout.Width, "단일 행 간격이 layout 너비를 변경");
        Qa.Equal(singleBeforeLayout.Height, singleAfterLayout.Height, "단일 행 간격이 layout 높이를 변경");
        Qa.Equal(singleBeforeGeometry, GlyphGeometry(singleAfterLayout), "단일 행 간격이 glyph 기하를 변경");
        var singlePath = directory.File("single-row-spacing.aethermath");
        await new WorkspaceFileService().SaveAsync(singlePath, singleSession.CaptureSnapshot());
        var reopenedSingle = await new WorkspaceFileService().OpenAsync(singlePath);
        Qa.Equal(27.5d, reopenedSingle.Groups.Single().RowSpacing, "단일 행 그룹 적용 간격의 실제 저장 왕복");
        var reopenedSingleLayout = MathLayoutEngine.Layout(reopenedSingle, metrics, options);
        Qa.Equal(singleBeforeLayout.Width, reopenedSingleLayout.Width, "단일 행 간격 재열기 뒤 layout 너비 변경");
        Qa.Equal(singleBeforeLayout.Height, reopenedSingleLayout.Height, "단일 행 간격 재열기 뒤 layout 높이 변경");
        Qa.Equal(singleBeforeGeometry, GlyphGeometry(reopenedSingleLayout),
            "단일 행 간격 재열기 뒤 glyph 기하 변경");
    }

    private static void AtomicHistoryAndReset()
    {
        var roleText = Qa.Text("5") with { Role = MathTextRole.Number };
        var group = Group(20, roleText, Qa.Text("next")) with
        {
            StyleOverride = new MathStyleOverride(FontFamily: "Original Group Face", Foreground: "#112233")
        };
        var session = new EditingSession(Workspace(group));
        session.MarkSaved(session.CaptureSnapshot());
        var before = CaptureState(session);
        var panel = new MathPropertiesPanelViewModel(session, new FixedMetrics())
        {
            SelectedScope = MathPropertyScope.CurrentGroup
        };
        panel.DraftFontSizeText = "36";
        panel.DraftForeground = "#AABBCC";
        panel.DraftTypesettingStyle = MathTypesettingStyle.Script;
        panel.DraftPinTypesettingStyle = true;
        panel.DraftGroupRowSpacingText = "44";
        Qa.Check(panel.CanApply, "스타일·조판 preset 고정·간격 통합 초안을 적용할 수 없음");
        panel.ApplyCommand.Execute(null);

        var appliedGroup = session.Workspace.Groups[0];
        Qa.Equal(44d, appliedGroup.RowSpacing, "통합 적용 간격");
        Qa.Equal(36d, appliedGroup.StyleOverride?.FontSize, "통합 적용 글자 크기");
        Qa.Equal("Original Group Face", appliedGroup.StyleOverride?.FontFamily,
            "통합 적용이 변경하지 않은 기존 글꼴을 지움");
        Qa.Equal("#AABBCC", appliedGroup.StyleOverride?.Foreground, "통합 적용 전경색");
        Qa.Equal(MathTypesettingStyle.Script, appliedGroup.StyleOverride?.TypesettingStyle,
            "통합 적용 조판 preset 고정");
        Qa.Equal(MathTextRole.Number, Qa.Leaf(session, roleText.Id).Role, "통합 적용이 TextRole을 변경");
        Qa.Equal(before.Revision + 1, session.Revision, "스타일·고정·간격 적용이 한 번만 커밋");
        Qa.Equal(before.Caret, session.Caret, "통합 적용 뒤 caret 보존");
        Qa.Equal(before.Selection, session.Selection, "통합 적용 뒤 selection 보존");
        Qa.Check(session.IsDirty && session.CanUndo, "통합 적용의 미저장·편집 이력 상태");
        var appliedFingerprint = Qa.Fingerprint(session.Workspace);
        var appliedStateId = session.CaptureSnapshot().StateId;

        session.Undo();
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), "통합 적용 Undo에서 workspace 복원");
        Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, "통합 적용 Undo에서 저장 StateId 복원");
        Qa.Check(!session.IsDirty && session.CanRedo, "통합 적용 Undo 뒤 dirty/history 상태");
        session.Redo();
        Qa.Equal(appliedFingerprint, Qa.Fingerprint(session.Workspace), "통합 적용 Redo에서 workspace 복원");
        Qa.Equal(appliedStateId, session.CaptureSnapshot().StateId, "통합 적용 Redo에서 StateId 복원");
        Qa.Check(session.IsDirty && session.CanUndo, "통합 적용 Redo 뒤 dirty/history 상태");

        var reset = session.ResetMathStyleOverride(Capture(session, MathPropertyScope.CurrentGroup));
        Qa.Equal(MathPropertyChangeStatus.Applied, reset.Status, "4개 스타일 그룹 override 초기화");
        Qa.Equal(44d, session.Workspace.Groups[0].RowSpacing, "스타일 초기화가 그룹 간격을 제거");
        Qa.Equal<MathStyleOverride?>(null, session.Workspace.Groups[0].StyleOverride, "4개 스타일 override 초기화 범위");
        Qa.Equal(MathTextRole.Number, Qa.Leaf(session, roleText.Id).Role, "그룹 스타일 초기화가 하위 역할을 제거");
        session.Undo();
        Qa.Equal(appliedFingerprint, Qa.Fingerprint(session.Workspace), "스타일 초기화 Undo에서 간격·스타일·Role 복원");
    }

    private static async Task CloneAndPersistenceRoundTrip()
    {
        using var directory = new TempDirectory();
        var roleNode = Qa.Text("unit") with { Role = MathTextRole.Unit };
        var source = Group(28.1234567890123, roleNode, Qa.Text("second")) with
        {
            StyleOverride = new MathStyleOverride(FontFamily: "Group Face", Foreground: "#80FF0000")
        };
        var single = Group(9.75, "only row");
        var session = new EditingSession(Workspace(source, single));
        var original = session.Workspace;
        Qa.Check(session.DuplicateCurrentGroup(), "spacing 그룹 복제 실패");
        var clone = session.Workspace.Groups[1];
        Qa.Check(clone.Id != source.Id, "복제 그룹이 원본 ID 재사용");
        Qa.Equal(source.RowSpacing, clone.RowSpacing, "복제 group 간격 정밀도");
        Qa.Equal(source.StyleOverride, clone.StyleOverride, "복제 그룹의 스타일 override 보존");
        Qa.Equal(source.Rows.Length, clone.Rows.Length, "복제 group 행 개수");
        for (var index = 0; index < source.Rows.Length; index++)
        {
            Qa.Check(source.Rows[index].Id != clone.Rows[index].Id, "복제 행이 원본 ID 재사용");
            Qa.Check(source.Rows[index].Root.Id != clone.Rows[index].Root.Id, "복제 root 노드가 원본 ID 재사용");
        }
        Qa.Equal(MathTextRole.Unit, ((TextNode)clone.Rows[0].Root).Role, "clone 후 TextRole 보존");
        Qa.Equal(9.75d, session.Workspace.Groups[2].RowSpacing, "복제가 이웃 단일 행 그룹 간격을 변경");

        var fileService = new WorkspaceFileService();
        var filePath = directory.File("group-spacing.aethermath");
        await fileService.SaveAsync(filePath, session.CaptureSnapshot());
        var reopened = await fileService.OpenAsync(filePath);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "실제 workspace 파일 간격/ID/style 왕복");
        Qa.Equal(single.RowSpacing, reopened.Groups[2].RowSpacing, "단일 행 그룹의 실제 저장 간격");
        Qa.Check(original.Groups.All(group => reopened.Groups.Any(saved => saved.Id == group.Id)),
            "복제·저장 중 원본 그룹 ID 손실");

        var stateService = new WorkspaceStateService(directory.File("state"));
        var recoveryId = Guid.NewGuid();
        var snapshot = session.CaptureSnapshot();
        var recovery = new WorkspaceRecovery(recoveryId, snapshot, DateTimeOffset.UtcNow, filePath);
        Qa.Equal(RecoveryWriteStatus.Written, (await stateService.SaveRecoveryAsync(recovery)).Status,
            "그룹 간격 recovery 저장");
        var read = await stateService.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, read.Status, "그룹 간격 recovery 읽기");
        Qa.Equal(Qa.Fingerprint(snapshot.Workspace), Qa.Fingerprint(read.Recovery!.Snapshot.Workspace),
            "실제 recovery 왕복에서 간격·복제 ID·Role 보존");
    }

    private static void LatexLayoutDiagnostics()
    {
        var zero = Group(0, "zero first", "zero second");
        var precise = Group(28.1234567890123, "precise first", "precise second");
        var single = Group(600, "one row");
        var workspace = Workspace(zero, precise, single);
        var diagnostics = LatexSerializer.GetLayoutDiagnostics(workspace);
        Qa.Equal(2, diagnostics.Length, "다중 행 그룹 LaTeX 간격 안내 개수");
        Qa.Equal(zero.Id, diagnostics[0].GroupId, "0px 다중 행 진단의 그룹 ID");
        Qa.Equal(0d, diagnostics[0].RowSpacing, "0px 다중 행 진단 값");
        Qa.Equal(precise.Id, diagnostics[1].GroupId, "정밀 간격 다중 행 진단의 그룹 ID");
        Qa.Equal(precise.RowSpacing, diagnostics[1].RowSpacing, "정밀 간격 다중 행 진단 값");
        Qa.Check(diagnostics.All(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Message)),
            "LaTeX 레이아웃 안내 문구가 비어 있음");
        Qa.Check(diagnostics.All(diagnostic => diagnostic.GroupId != single.Id),
            "단일 행 그룹에 불필요한 LaTeX 레이아웃 진단 생성");

        var viewModel = new MainWindowViewModel();
        Qa.Check(!viewModel.LatexLayoutDiagnostics.IsEmpty,
            "기본 두 행 그룹의 MainVM 간격 안내가 비어 있음");
        Qa.Check(viewModel.CanExportLatex,
            "완성된 수식에서 간격 advisory만으로 LaTeX 재사용을 차단");
        Qa.Check(viewModel.LatexCodeDiagnostic.Contains("행 간격", StringComparison.Ordinal),
            "MainWindowViewModel LaTeX 진단에 차단하지 않는 간격 안내 없음");
        var originalLatex = LatexSerializer.ToLatex(workspace);
        Qa.Check(originalLatex.Length > 0,
            "간격 안내가 LaTeX 소스 생성 자체를 중단");

        var spacingSession = new EditingSession(workspace);
        var changed = spacingSession.ApplyMathProperties(Capture(spacingSession, MathPropertyScope.CurrentGroup),
            new MathPropertiesPatch(new MathStylePatch(), GroupRowSpacing: 47.25));
        Qa.Equal(MathPropertyChangeStatus.Applied, changed.Status, "LaTeX 비교용 간격만 적용");
        Qa.Equal(originalLatex, LatexSerializer.ToLatex(spacingSession.Workspace),
            "간격만 변경한 뒤 LaTeX 수식 문자열이 달라짐");
    }

    private static async Task PreviewAndSvgSpacing()
    {
        using var directory = new TempDirectory();
        var first = Group(17.25, "a", "b");
        var second = Group(39.5, "c", "d", "e");
        var workspace = Workspace(first, second);
        var session = new EditingSession(workspace);
        session.MarkSaved(session.CaptureSnapshot());
        var before = CaptureState(session);
        var exporter = new RecordingExporter();
        var preview = new MathOutputPreviewViewModel(session, new FixedMetrics(), exporter);

        foreach (var mode in new[] { MathOutputMode.WorkspaceStyle, MathOutputMode.Display, MathOutputMode.Inline })
        {
            preview.SelectedMode = mode;
            Qa.Check(preview.RefreshPreview(), $"{mode} 간격 미리보기 갱신 실패");
            var snapshot = preview.PreviewSnapshot ?? throw new InvalidOperationException("고정 간격 미리보기 없음");
            Qa.Equal(mode, snapshot.Mode, $"{mode} 고정 출력 모드");
            AssertRowGap(snapshot.Layout, first.Rows[0], first.Rows[1], 17.25,
                $"{mode} 첫 그룹 spacing 투영");
            AssertRowGap(snapshot.Layout, second.Rows[0], second.Rows[1], 39.5,
                $"{mode} 두 번째 그룹 첫 spacing 투영");
            AssertRowGap(snapshot.Layout, second.Rows[1], second.Rows[2], 39.5,
                $"{mode} 두 번째 그룹 다음 spacing 투영");
            AssertGroupBoundaryGap(snapshot.Layout, first, second, new MathLayoutOptions().RowSpacing,
                $"{mode} 그룹 내부 간격과 options.RowSpacing 분리");

            var path = directory.File($"{mode}.svg");
            var exported = await preview.ExportSvgToPathAsync(path);
            Qa.Equal(SvgPreviewExportStatus.Saved, exported.Status, $"{mode} 고정 간격 SVG 저장");
            Qa.Check(ReferenceEquals(snapshot.Layout, exporter.LastLayout),
                $"{mode} 내보내기가 다른 LayoutResult 인스턴스를 사용");
            var expectedSvg = new SvgExportService().ToSvg(snapshot.Layout);
            var actualSvg = await File.ReadAllTextAsync(path);
            Qa.Equal(expectedSvg, actualSvg, $"{mode} SVG 파일이 고정 LayoutResult와 다름");
            AssertSvgGeometry(path, snapshot.Layout, $"{mode} SVG 기하 정보");
        }

        AssertUnchanged(before, session, "Display/Inline 간격 미리보기와 SVG 저장 뒤 원본 세션");
    }

    private static void AssertSvgGeometry(string path, LayoutResult layout, string label)
    {
        var document = XDocument.Load(path);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = document.Root ?? throw new InvalidOperationException(label + ": SVG 루트 누락");
        Qa.Equal("svg", root.Name.LocalName, label + " 루트 이름");
        Qa.Near(layout.Width, Parse(root.Attribute("width")?.Value, label + " 너비"), label + " 너비");
        Qa.Near(layout.Height, Parse(root.Attribute("height")?.Value, label + " 높이"), label + " 높이");
        var viewBox = (root.Attribute("viewBox")?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Parse(value, label + " viewBox"))
            .ToArray();
        Qa.Equal(4, viewBox.Length, label + " viewBox 구성 요소 개수");
        Qa.Near(0, viewBox[0], label + " viewBox x 좌표");
        Qa.Near(0, viewBox[1], label + " viewBox y 좌표");
        Qa.Near(layout.Width, viewBox[2], label + " viewBox 너비");
        Qa.Near(layout.Height, viewBox[3], label + " viewBox 높이");

        var actualGlyphs = root.Descendants(svg + "text").ToArray();
        var expectedGlyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        Qa.Equal(expectedGlyphs.Length, actualGlyphs.Length, label + " glyph 요소 개수");
        for (var index = 0; index < expectedGlyphs.Length; index++)
        {
            var expected = expectedGlyphs[index];
            var actual = actualGlyphs[index];
            Qa.Equal(expected.Text, actual.Value, $"{label} glyph 순서/내용 {index}");
            Qa.Near(expected.BaselineOrigin.X,
                Parse(actual.Attribute("x")?.Value, $"{label} glyph x 좌표 {index}"),
                $"{label} glyph baseline x 좌표 {index}", tolerance: 0.00051);
            Qa.Near(expected.BaselineOrigin.Y,
                Parse(actual.Attribute("y")?.Value, $"{label} glyph y 좌표 {index}"),
                $"{label} glyph baseline y 좌표 {index}", tolerance: 0.00051);
        }
    }

    private static string GlyphGeometry(LayoutResult layout) => string.Join("|",
        layout.Primitives.OfType<GlyphPrimitive>().Select(glyph =>
            $"{glyph.NodeId:N}:{glyph.Text}:{glyph.BaselineOrigin.X:R}:{glyph.BaselineOrigin.Y:R}:{glyph.FontSize:R}"));

    private static double Parse(string? value, string label)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
            throw new InvalidOperationException(label + ": 숫자 형식이 잘못되었습니다.");
        return parsed;
    }

    private static void AssertRowGap(LayoutResult layout, EquationRow upper, EquationRow lower,
        double expectedSpacing, string label)
    {
        var upperLayout = layout.LogicalRows.Single(row => row.RowId == upper.Id);
        var lowerLayout = layout.LogicalRows.Single(row => row.RowId == lower.Id);
        Qa.Near(expectedSpacing, lowerLayout.Bounds.Y - upperLayout.Bounds.Bottom, label);
    }

    private static void AssertGroupBoundaryGap(LayoutResult layout, EquationGroup upper, EquationGroup lower,
        double expectedSpacing, string label)
    {
        var lastUpper = layout.LogicalRows.Single(row => row.RowId == upper.Rows[^1].Id);
        var firstLower = layout.LogicalRows.Single(row => row.RowId == lower.Rows[0].Id);
        Qa.Near(expectedSpacing, firstLower.Bounds.Y - lastUpper.Bounds.Bottom, label);
    }

    private static MathPropertyTargetSnapshot Capture(EditingSession session, MathPropertyScope scope)
    {
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            "수학 속성 대상 캡처 실패: " + scope);
        return target!;
    }

    private static SessionState CaptureState(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace),
        session.Workspace.Groups.SelectMany(group => group.Rows).Select(row => row.Id).ToImmutableArray(),
        AllNodeIds(session.Workspace).ToImmutableArray(),
        session.Revision,
        session.CaptureSnapshot().StateId,
        session.IsDirty,
        session.CanUndo,
        session.CanRedo,
        session.Caret,
        session.Selection,
        session.SelectedNodeId,
        session.IsNodeSelected);

    private static void AssertUnchanged(SessionState before, EditingSession session, string label)
    {
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), label + " workspace");
        Qa.Equal(string.Join(',', before.RowIds), string.Join(',', session.Workspace.Groups.SelectMany(group => group.Rows).Select(row => row.Id)), label + " 행 ID");
        Qa.Equal(string.Join(',', before.NodeIds), string.Join(',', AllNodeIds(session.Workspace)), label + " 노드 ID");
        Qa.Equal(before.Revision, session.Revision, label + " revision");
        Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, label + " StateId");
        Qa.Equal(before.IsDirty, session.IsDirty, label + " dirty");
        Qa.Equal(before.CanUndo, session.CanUndo, label + " Undo 상태");
        Qa.Equal(before.CanRedo, session.CanRedo, label + " Redo 상태");
        Qa.Equal(before.Caret, session.Caret, label + " caret");
        Qa.Equal(before.Selection, session.Selection, label + " selection");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, label + " 선택된 노드 ID");
        Qa.Equal(before.IsNodeSelected, session.IsNodeSelected, label + " 전체 노드 선택 상태");
    }

    private static IEnumerable<Guid> AllNodeIds(EquationWorkspace workspace) =>
        workspace.Groups.SelectMany(group => group.Rows).SelectMany(row => Qa.Walk(row.Root)).Select(node => node.Id);

    private static EquationWorkspace Workspace(params EquationGroup[] groups) => new(
        Guid.NewGuid(), 1, "그룹 행 간격 QA", [.. groups], new MathStyle(30, "Deterministic", "#203040"));

    private static EquationGroup Group(double spacing, params string[] rowTexts) =>
        Group(spacing, rowTexts.Select(text => (MathNode)Qa.Text(text)).ToArray());

    private static EquationGroup Group(double spacing, params MathNode[] roots) => new(
        Guid.NewGuid(), [.. roots.Select(root => new EquationRow(Guid.NewGuid(), root))], AlignmentMode.None, spacing);

    private sealed record SessionState(
        string Fingerprint,
        ImmutableArray<Guid> RowIds,
        ImmutableArray<Guid> NodeIds,
        long Revision,
        Guid StateId,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected);

    private sealed class FixedMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.53 : 0.50);
            var caret = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                caret.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.23, caret.MoveToImmutable());
        }
    }

    private sealed class RecordingExporter : ISvgExportService
    {
        private readonly SvgExportService _inner = new();
        public LayoutResult? LastLayout { get; private set; }

        public Task ExportAsync(string path, LayoutResult layout, CancellationToken cancellationToken = default)
        {
            LastLayout = layout;
            return _inner.ExportAsync(path, layout, cancellationToken);
        }
    }
}
