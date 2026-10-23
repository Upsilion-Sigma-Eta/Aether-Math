using System.Text.Json.Nodes;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class PropertiesCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("PROP-CASCADE", "세 스타일 필드의 상속 출처와 자식 명시 override", ResolveCascade);
        yield return new("PROP-TARGETS", "네 scope의 실제 대상과 구조/부분 텍스트 선택 경계", CaptureTargets);
        yield return new("PROP-APPLY-RESET", "한 번 적용·초기화, 선택/ID 보존과 undo/redo", ApplyResetAndHistory);
        yield return new("PROP-STALE", "캡처 뒤 revision·대상·선택·Session 변경 거절", RejectStaleTargets);
        yield return new("PROP-VALIDATION", "수치·글꼴·색 검증과 명시값 기준 no-op", ValidatePatches);
        yield return new("PROP-EDIT-CLONE-CLEAR", "텍스트 편집·템플릿 감싸기·복제·Clear의 스타일 ID 보존", EditCloneAndClear);
        yield return new("PROP-JSON-RECOVERY", "schema v1 기본값·파일/복구 왕복 및 손상 원본 보존", JsonAndRecoveryRoundTrip);
        yield return new("PROP-OUTPUT", "첨자/분수 축소의 effective style 및 SVG 반투명 색", OutputConsumesResolvedStyle);
    }

    private static void ResolveCascade()
    {
        var plain = Qa.Text("plain");
        var explicitLeaf = Qa.Text("leaf") with
        {
            StyleOverride = Override(FontFamily: "Leaf Face", Foreground: "#A0B0C0")
        };
        var parent = Qa.Sequence(plain, explicitLeaf) with
        {
            StyleOverride = Override(Foreground: "#445566")
        };
        var row = Row(parent) with { StyleOverride = Override(FontFamily: "Row Face") };
        var group = Group([row]) with { StyleOverride = Override(FontSize: 24) };
        var untouched = Qa.Text("default");
        var defaultGroup = Group([Row(untouched)]);
        var workspace = Workspace([group, defaultGroup], new MathStyle(18, "Workspace Face", "#112233"));
        var session = new EditingSession(workspace);

        Qa.Caret(session, plain.Id, 0);
        var inheritedTarget = Capture(session, MathPropertyScope.SelectionNode);
        var inherited = session.ResolveMathStyle(inheritedTarget) ?? throw new InvalidOperationException("inherited style resolve 실패");
        Qa.Near(24, inherited.FontSize.Value, "group 크기 상속");
        Qa.Equal(MathStyleSourceKind.Group, inherited.FontSize.Origin.Kind, "크기 출처 종류");
        Qa.Equal(group.Id, inherited.FontSize.Origin.OwnerId, "크기 출처 ID");
        Qa.Equal<int?>(1, inherited.FontSize.Origin.GroupNumber, "크기 출처 그룹 번호");
        Qa.Equal<int?>(null, inherited.FontSize.Origin.RowNumber, "그룹 출처의 행 번호 없음");
        Qa.Equal("Row Face", inherited.FontFamily.Value, "row 글꼴 상속");
        Qa.Equal(MathStyleSourceKind.Row, inherited.FontFamily.Origin.Kind, "글꼴 출처 종류");
        Qa.Equal(row.Id, inherited.FontFamily.Origin.OwnerId, "글꼴 출처 ID");
        Qa.Equal("#445566", inherited.Foreground.Value, "부모 node 색 상속");
        Qa.Equal(MathStyleSourceKind.Node, inherited.Foreground.Origin.Kind, "부모 node 색 출처 종류");
        Qa.Equal(parent.Id, inherited.Foreground.Origin.OwnerId, "부모 node 색 출처 ID");
        Qa.Equal<int?>(1, inherited.Foreground.Origin.RowNumber, "node 색 출처 행 번호");
        Qa.Equal<MathStyleOverride?>(null, inherited.TargetOverride, "대상 leaf의 빈 explicit override");

        Qa.Caret(session, explicitLeaf.Id, 0);
        var leafTarget = Capture(session, MathPropertyScope.SelectionNode);
        var leafResolved = session.ResolveMathStyle(leafTarget) ?? throw new InvalidOperationException("leaf style resolve 실패");
        Qa.Equal("Leaf Face", leafResolved.FontFamily.Value, "leaf 명시 글꼴 우선");
        Qa.Equal(explicitLeaf.Id, leafResolved.FontFamily.Origin.OwnerId, "leaf 글꼴 출처 ID");
        Qa.Equal("#A0B0C0", leafResolved.Foreground.Value, "leaf 명시 색 우선");
        Qa.Equal(explicitLeaf.StyleOverride, leafResolved.TargetOverride, "대상 explicit override 투영");

        Qa.Caret(session, untouched.Id, 0);
        var workspaceTarget = Capture(session, MathPropertyScope.WorkspaceDefault);
        var defaults = session.ResolveMathStyle(workspaceTarget) ?? throw new InvalidOperationException("workspace default resolve 실패");
        Qa.Equal(18d, defaults.FontSize.Value, "작업 기본 크기");
        Qa.Equal("Workspace Face", defaults.FontFamily.Value, "작업 기본 글꼴");
        Qa.Equal("#112233", defaults.Foreground.Value, "작업 기본 색");
        Qa.Equal(MathStyleSourceKind.WorkspaceDefault, defaults.FontSize.Origin.Kind, "workspace 크기 출처");
        Qa.Equal(workspace.Id, defaults.Foreground.Origin.OwnerId, "workspace 색 출처 ID");
    }

    private static void CaptureTargets()
    {
        var first = Qa.Text("A🙂e\u0301Z");
        var secondRowText = Qa.Text("inside fraction");
        var fraction = new FractionNode(Guid.NewGuid(), secondRowText, Qa.Text("denominator"));
        var row1 = Row(first);
        var row2 = Row(Qa.Sequence(Qa.Text("prefix"), fraction));
        var group1 = Group([row1, row2]);
        var other = Qa.Text("other group");
        var group2 = Group([Row(other)]);
        var session = new EditingSession(Workspace([group1, group2]));

        Qa.Caret(session, first.Id, 2);
        var initialRow = Capture(session, MathPropertyScope.CurrentRow);
        Qa.Equal(group1.Id, initialRow.GroupId, "caret 행 대상 그룹");
        Qa.Equal(row1.Id, initialRow.RowId, "caret 행 대상 ID");
        Qa.Equal<Guid?>(null, initialRow.TargetNodeId, "행 scope가 node 대상을 노출");

        Qa.Check(session.TrySelectNode(fraction.Id), "교차 행 구조 전체 선택");
        var selectedRow = Capture(session, MathPropertyScope.CurrentRow);
        var selectedGroup = Capture(session, MathPropertyScope.CurrentGroup);
        var selectedNode = Capture(session, MathPropertyScope.SelectionNode);
        Qa.Equal(row2.Id, selectedRow.RowId, "whole-node 선택 행이 현재 caret 행보다 우선");
        Qa.Equal(group1.Id, selectedGroup.GroupId, "whole-node 선택 그룹");
        Qa.Equal<Guid?>(null, selectedGroup.RowId, "그룹 scope가 행 ID를 고정");
        Qa.Equal<Guid?>(fraction.Id, selectedNode.TargetNodeId, "SelectionNode가 명시 구조 선택을 대상으로 삼음");
        Qa.Equal(row2.Id, selectedNode.RowId, "구조 선택의 위치 행");

        Qa.Caret(session, first.Id, 1);
        var anchor = new MathCaret(first.Id, 1);
        var focus = new MathCaret(first.Id, 3);
        Qa.Check(session.TrySetSelection(new MathSelection(anchor, focus)), "부분 텍스트 선택 설정");
        var partial = Capture(session, MathPropertyScope.SelectionNode);
        Qa.Equal<Guid?>(first.Id, partial.TargetNodeId, "부분 텍스트 선택은 전체 TextNode를 대상화");
        Qa.Equal(new MathSelection(anchor, focus), partial.Selection, "부분 선택 경계 snapshot 보존");
        Qa.Check(!partial.IsNodeSelected, "부분 텍스트 선택이 구조 전체 선택으로 취급됨");

        var workspaceTarget = Capture(session, MathPropertyScope.WorkspaceDefault);
        Qa.Equal<Guid?>(null, workspaceTarget.GroupId, "WorkspaceDefault가 그룹에 묶임");
        Qa.Equal<Guid?>(null, workspaceTarget.RowId, "WorkspaceDefault가 행에 묶임");
        Qa.Equal<Guid?>(null, workspaceTarget.TargetNodeId, "WorkspaceDefault가 node에 묶임");
        Qa.Check(!session.TryCaptureMathPropertyTarget((MathPropertyScope)999, out var invalid) && invalid is null,
            "정의되지 않은 scope 캡처 허용");
    }

    private static void ApplyResetAndHistory()
    {
        var leaf = Qa.Text("가나다") with { StyleOverride = Override(FontFamily: "Child Face") };
        var group = Group([Row(leaf) with { StyleOverride = Override(FontSize: 26, FontFamily: "Row Face") }])
            with { StyleOverride = Override(Foreground: "#223344") };
        var session = new EditingSession(Workspace([group]));
        Qa.Select(session, leaf.Id, 1, 2);
        session.MarkSaved(session.CaptureSnapshot());
        var beforeWorkspace = session.Workspace;
        var before = Capture(session);

        var target = Capture(session, MathPropertyScope.CurrentRow);
        var applied = session.ApplyMathStyle(target, new MathStylePatch(FontSize: 32, Foreground: "#80FF0000"));
        Qa.Equal(MathPropertyChangeStatus.Applied, applied.Status, "row partial patch 적용");
        Qa.Equal(before.Revision + 1, session.Revision, "Apply가 단일 commit 아님");
        var updatedRow = session.Workspace.Groups[0].Rows[0];
        Qa.Equal(new MathStyleOverride(32, "Row Face", "#80FF0000"), updatedRow.StyleOverride,
            "null patch 필드가 기존 명시값을 덮음");
        Qa.Equal(new MathStyleOverride(null, "Child Face", null), Qa.Node(session, leaf.Id).StyleOverride,
            "부모 Apply가 자식 명시 override를 바꿈");
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(ReplaceStyleOnly(beforeWorkspace, session.Workspace)),
            "Apply가 스타일 외 workspace/ID를 바꿈");
        Qa.Equal(before.Caret, session.Caret, "Apply가 caret를 바꿈");
        Qa.Equal(before.Selection, session.Selection, "Apply가 selection 방향/범위를 바꿈");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "Apply가 selected node를 바꿈");
        Qa.Equal(before.IsNodeSelected, session.IsNodeSelected, "Apply가 whole-node 선택 flag를 바꿈");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, "Apply가 저장/history 상태를 잘못 갱신");
        var appliedState = Capture(session);
        AssertUndoRedo(session, before, appliedState, "Apply row style");

        session.MarkSaved(session.CaptureSnapshot());
        var resetBefore = Capture(session);
        var resetTarget = Capture(session, MathPropertyScope.CurrentRow);
        var reset = session.ResetMathStyleOverride(resetTarget);
        Qa.Equal(MathPropertyChangeStatus.Applied, reset.Status, "row 전체 override reset");
        Qa.Equal<MathStyleOverride?>(null, session.Workspace.Groups[0].Rows[0].StyleOverride,
            "Reset이 scope 세 필드를 남김");
        Qa.Equal(group.StyleOverride, session.Workspace.Groups[0].StyleOverride, "Reset이 부모 group override를 지움");
        Qa.Equal(new MathStyleOverride(null, "Child Face", null), Qa.Node(session, leaf.Id).StyleOverride,
            "Reset이 자식 override를 지움");
        var resetAfter = Capture(session);
        AssertUndoRedo(session, resetBefore, resetAfter, "Reset row style");

        session.MarkSaved(session.CaptureSnapshot());
        var nodeBefore = Capture(session);
        var nodeTarget = Capture(session, MathPropertyScope.SelectionNode);
        var nodeApply = session.ApplyMathStyle(nodeTarget, new MathStylePatch(Foreground: "#445577"));
        Qa.Equal(MathPropertyChangeStatus.Applied, nodeApply.Status, "SelectionNode style 적용");
        Qa.Equal(new MathStyleOverride(null, "Child Face", "#445577"), Qa.Node(session, leaf.Id).StyleOverride,
            "SelectionNode patch가 다른 explicit 필드를 보존하지 않음");
        var nodeAfter = Capture(session);
        AssertUndoRedo(session, nodeBefore, nodeAfter, "Apply SelectionNode style");

        session.MarkSaved(session.CaptureSnapshot());
        var nodeResetBefore = Capture(session);
        var nodeReset = session.ResetMathStyleOverride(Capture(session, MathPropertyScope.SelectionNode));
        Qa.Equal(MathPropertyChangeStatus.Applied, nodeReset.Status, "SelectionNode override reset");
        Qa.Equal<MathStyleOverride?>(null, Qa.Node(session, leaf.Id).StyleOverride,
            "SelectionNode reset이 대상 override 전체를 제거하지 않음");
        Qa.Equal(group.StyleOverride, session.Workspace.Groups[0].StyleOverride,
            "SelectionNode reset이 상위 group override를 제거");
        var nodeResetAfter = Capture(session);
        AssertUndoRedo(session, nodeResetBefore, nodeResetAfter, "Reset SelectionNode style");

        session.MarkSaved(session.CaptureSnapshot());
        var rowRestore = session.ApplyMathStyle(Capture(session, MathPropertyScope.CurrentRow),
            new MathStylePatch(FontFamily: "Row Face"));
        Qa.Equal(MathPropertyChangeStatus.Applied, rowRestore.Status, "group reset fixture용 row override 설정");
        Qa.Equal(new MathStyleOverride(null, "Row Face", null), session.Workspace.Groups[0].Rows[0].StyleOverride,
            "row fixture override 저장");

        session.MarkSaved(session.CaptureSnapshot());
        var groupBefore = Capture(session);
        var groupTarget = Capture(session, MathPropertyScope.CurrentGroup);
        var groupApply = session.ApplyMathStyle(groupTarget, new MathStylePatch(FontFamily: "Explicit Group Face"));
        Qa.Equal(MathPropertyChangeStatus.Applied, groupApply.Status, "CurrentGroup style 적용");
        Qa.Equal(new MathStyleOverride(null, "Explicit Group Face", "#223344"),
            session.Workspace.Groups[0].StyleOverride, "CurrentGroup patch가 다른 explicit 필드를 보존하지 않음");
        var groupAfter = Capture(session);
        AssertUndoRedo(session, groupBefore, groupAfter, "Apply CurrentGroup style");

        session.MarkSaved(session.CaptureSnapshot());
        var groupResetBefore = Capture(session);
        var groupReset = session.ResetMathStyleOverride(Capture(session, MathPropertyScope.CurrentGroup));
        Qa.Equal(MathPropertyChangeStatus.Applied, groupReset.Status, "CurrentGroup override reset");
        Qa.Equal<MathStyleOverride?>(null, session.Workspace.Groups[0].StyleOverride,
            "CurrentGroup reset이 group override 전체를 제거하지 않음");
        Qa.Equal(new MathStyleOverride(null, "Row Face", null), session.Workspace.Groups[0].Rows[0].StyleOverride,
            "CurrentGroup reset이 자식 row override를 제거");
        var groupResetAfter = Capture(session);
        AssertUndoRedo(session, groupResetBefore, groupResetAfter, "Reset CurrentGroup style");

        session.MarkSaved(session.CaptureSnapshot());
        var workspaceBefore = Capture(session);
        var workspaceTarget = Capture(session, MathPropertyScope.WorkspaceDefault);
        var workspaceApply = session.ApplyMathStyle(workspaceTarget, new MathStylePatch(Foreground: "#336699"));
        Qa.Equal(MathPropertyChangeStatus.Applied, workspaceApply.Status, "WorkspaceDefault style 적용");
        Qa.Equal(new MathStyle(24, "Default Face", "#336699"), session.Workspace.Style,
            "WorkspaceDefault patch가 다른 baseline 필드를 바꿈");
        var workspaceAfter = Capture(session);
        AssertUndoRedo(session, workspaceBefore, workspaceAfter, "Apply WorkspaceDefault style");
    }

    private static void RejectStaleTargets()
    {
        var first = Qa.Text("first");
        var second = Qa.Text("second");
        var wrapper = new FractionNode(Guid.NewGuid(), second, Qa.Text("denominator"));
        var workspace = Workspace([Group([Row(Qa.Sequence(first, wrapper))])]);

        var revisionSession = new EditingSession(workspace);
        Qa.Caret(revisionSession, first.Id, 0);
        var revisionTarget = Capture(revisionSession, MathPropertyScope.SelectionNode);
        revisionSession.InsertText("x");
        AssertStaleWithoutFurtherMutation(revisionSession, revisionTarget, "revision stale");

        var caretSession = new EditingSession(workspace);
        Qa.Caret(caretSession, first.Id, 0);
        var caretTarget = Capture(caretSession, MathPropertyScope.SelectionNode);
        Qa.Caret(caretSession, second.Id, 1);
        AssertStaleWithoutFurtherMutation(caretSession, caretTarget, "caret stale");

        var selectionSession = new EditingSession(workspace);
        Qa.Caret(selectionSession, first.Id, 0);
        var selectionTarget = Capture(selectionSession, MathPropertyScope.SelectionNode);
        Qa.Select(selectionSession, first.Id, 0, 2);
        AssertStaleWithoutFurtherMutation(selectionSession, selectionTarget, "partial selection stale");

        var nodeSelectionSession = new EditingSession(workspace);
        Qa.Check(nodeSelectionSession.TrySelectNode(wrapper.Id), "초기 whole-node 선택");
        var nodeSelectionTarget = Capture(nodeSelectionSession, MathPropertyScope.SelectionNode);
        Qa.Check(nodeSelectionSession.TrySelectNode(first.Id), "새 whole-node 선택");
        AssertStaleWithoutFurtherMutation(nodeSelectionSession, nodeSelectionTarget, "selected node stale");

        var originalSession = new EditingSession(workspace);
        var sessionTarget = Capture(originalSession, MathPropertyScope.SelectionNode);
        var replacementSession = new EditingSession(workspace);
        var replacementBefore = Capture(replacementSession);
        var replaced = replacementSession.ApplyMathStyle(sessionTarget, new MathStylePatch(Foreground: "#FEDCBA"));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, replaced.Status, "다른 Session이 snapshot을 받아들임");
        Qa.Equal(replacementBefore, Capture(replacementSession), "Session token stale 거절이 상태를 바꿈");
    }

    private static void ValidatePatches()
    {
        var text = Qa.Text("value");
        var row = Row(text) with { StyleOverride = Override(FontSize: 30, FontFamily: "Explicit Face", Foreground: "#102030") };
        var session = new EditingSession(Workspace([Group([row])]));
        session.MarkSaved(session.CaptureSnapshot());
        var target = Capture(session, MathPropertyScope.CurrentRow);
        var before = Capture(session);
        Qa.Equal(MathPropertyChangeStatus.NoChange,
            session.ApplyMathStyle(target, new MathStylePatch()).Status,
            "완전히 빈 patch가 no-op이 아님");
        Qa.Equal(before, Capture(session), "완전히 빈 patch가 상태를 변경");
        var invalidPatches = new[]
        {
            new MathStylePatch(FontSize: 0.999),
            new MathStylePatch(FontSize: 0),
            new MathStylePatch(FontSize: 512.001),
            new MathStylePatch(FontSize: double.NaN),
            new MathStylePatch(FontSize: double.PositiveInfinity),
            new MathStylePatch(FontFamily: "  "),
            new MathStylePatch(Foreground: "red"),
            new MathStylePatch(Foreground: "#GG0000")
        };
        foreach (var patch in invalidPatches)
        {
            var result = session.ApplyMathStyle(target, patch);
            Qa.Equal(MathPropertyChangeStatus.InvalidValue, result.Status, "잘못된 patch가 거절되지 않음: " + patch);
            Qa.Equal(before, Capture(session), "잘못된 patch가 상태를 변경: " + patch);
        }

        Qa.Equal(MathPropertyChangeStatus.NoChange,
            session.ApplyMathStyle(target, new MathStylePatch(FontSize: 30)).Status,
            "이미 저장된 explicit value와 같은 patch가 no-op이 아님");
        Qa.Equal(before, Capture(session), "같은 explicit value no-op 상태");
        var resetUnsupported = session.ResetMathStyleOverride(Capture(session, MathPropertyScope.WorkspaceDefault));
        Qa.Equal(MathPropertyChangeStatus.UnsupportedScope, resetUnsupported.Status, "WorkspaceDefault Reset 허용");
        Qa.Equal(before, Capture(session), "unsupported reset이 상태 변경");

        var equalEffectiveSession = new EditingSession(Workspace([Group([Row(Qa.Text("x"))])],
            new MathStyle(30, "Default Face", "#102030")));
        var equalEffectiveTarget = Capture(equalEffectiveSession, MathPropertyScope.CurrentRow);
        var explicitOverride = equalEffectiveSession.ApplyMathStyle(equalEffectiveTarget, new MathStylePatch(FontSize: 30));
        Qa.Equal(MathPropertyChangeStatus.Applied, explicitOverride.Status,
            "상위 effective와 같은 새 explicit override를 상속관계 고정으로 적용하지 않음");
        Qa.Equal<double?>(30d, equalEffectiveSession.Workspace.Groups[0].Rows[0].StyleOverride?.FontSize,
            "같은 effective 크기의 명시 override 저장 누락");
        var sameExplicit = equalEffectiveSession.ApplyMathStyle(Capture(equalEffectiveSession, MathPropertyScope.CurrentRow),
            new MathStylePatch(FontSize: 30));
        Qa.Equal(MathPropertyChangeStatus.NoChange, sameExplicit.Status, "같은 explicit override가 반복 commit");
        Qa.Equal(1L, equalEffectiveSession.Revision, "same explicit no-op이 revision 증가");

        var legacyLargeRow = Row(Qa.Text("large baseline")) with
        {
            StyleOverride = Override(FontFamily: "Legacy Row Face")
        };
        var legacyLargeSession = new EditingSession(Workspace([Group([legacyLargeRow])],
            new MathStyle(600, "Legacy Face", "#123456")));
        var legacyTarget = Capture(legacyLargeSession, MathPropertyScope.CurrentRow);
        var colorOnly = legacyLargeSession.ApplyMathStyle(legacyTarget, new MathStylePatch(Foreground: "#654321"));
        Qa.Equal(MathPropertyChangeStatus.Applied, colorOnly.Status,
            "기존 유효 large baseline에서 색만 변경하는 요청이 차단됨");
        Qa.Equal(600d, legacyLargeSession.Workspace.Style.FontSize,
            "색 변경이 기존 baseline 크기를 바꿈");
        Qa.Equal<MathStyleOverride?>(new MathStyleOverride(null, "Legacy Row Face", "#654321"),
            legacyLargeSession.Workspace.Groups[0].Rows[0].StyleOverride,
            "색만 변경한 row override가 새 size 제한으로 거절됨");

        var largePanelMetrics = new RecordingMetrics();
        var unknownProbe = new FixedGlyphProbe(new MathGlyphCoverageResult(
            MathGlyphCoverageStatus.Unknown, "Legacy Face", "문자표 관측을 확정할 수 없습니다."));
        var largePanel = new MathPropertiesPanelViewModel(legacyLargeSession, largePanelMetrics, unknownProbe);
        largePanel.SelectedScope = MathPropertyScope.CurrentRow;
        Qa.Check(!largePanel.CanApply, "미수정 속성 draft가 최초 적용 가능 상태로 열림");
        var beforeLargePanelEdit = Capture(legacyLargeSession);
        largePanel.DraftFontFamily = "   ";
        largePanel.DraftForeground = "#ABCDEF";
        Qa.Check(largePanel.FontMeasurementDiagnostic.Contains("확정할 수 없습니다", StringComparison.Ordinal),
            "coverage Unknown이 advisory 진단으로 표시되지 않음");
        Qa.Check(largePanel.CanApply, "coverage Unknown이 유효한 색 변경을 차단");
        Qa.Equal(600d, legacyLargeSession.Workspace.Style.FontSize,
            "기존 baseline 크기 600을 속성 초안이 유효하지 않은 신규 override로 취급");
        largePanel.ApplyCommand.Execute(null);
        Qa.Equal(beforeLargePanelEdit.Revision + 1, legacyLargeSession.Revision,
            "legacy baseline에서 색 전용 Apply가 단일 commit이 아님");
        Qa.Equal(600d, legacyLargeSession.Workspace.Style.FontSize,
            "색 전용 panel Apply가 legacy baseline 크기를 바꿈");
        Qa.Equal(new MathStyleOverride(null, "Legacy Row Face", "#ABCDEF"),
            legacyLargeSession.Workspace.Groups[0].Rows[0].StyleOverride,
            "blank family가 null patch로 처리되어 기존 explicit 글꼴을 보존하지 않음");

        var failingMetrics = new RecordingMetrics();
        var failingSession = new EditingSession(Workspace([Group([Row(Qa.Text("font candidate"))])]));
        var failingPanel = new MathPropertiesPanelViewModel(failingSession, failingMetrics,
            new FixedGlyphProbe(new MathGlyphCoverageResult(MathGlyphCoverageStatus.Present,
                "Default Face", "표본 글리프가 관측됨.")));
        var beforeFailingCandidate = Capture(failingSession);
        failingMetrics.ThrowOnMeasure = true;
        failingPanel.DraftFontFamily = "Unmeasurable Face";
        Qa.Check(!string.IsNullOrWhiteSpace(failingPanel.FontMeasurementDiagnostic),
            "후보 글꼴 측정 예외 진단이 비어 있음");
        Qa.Check(!failingPanel.CanApply, "치명적인 후보 글꼴 측정 실패에서 Apply가 허용됨");
        var callsBeforeInvalidSize = failingMetrics.Calls;
        failingPanel.DraftFontSizeText = "1e300";
        Qa.Check(!string.IsNullOrWhiteSpace(failingPanel.FontSizeValidationError),
            "1e300 신규 크기에 validation error가 없음");
        Qa.Equal(callsBeforeInvalidSize, failingMetrics.Calls,
            "범위 밖 1e300 크기가 IMathTextMetrics에 전달됨");
        failingPanel.ApplyCommand.Execute(null);
        Qa.Equal(beforeFailingCandidate, Capture(failingSession),
            "측정 실패/범위 밖 크기에서 disabled Apply가 Session을 변경");
    }

    private static void EditCloneAndClear()
    {
        var edited = Qa.Text("ab") with { StyleOverride = Override(31, "Leaf Face", "#123456") };
        var editSession = new EditingSession(Workspace([Group([Row(edited)])]));
        Qa.Caret(editSession, edited.Id, 1);
        editSession.InsertText("🙂");
        Qa.Equal("a🙂b", Qa.Leaf(editSession, edited.Id).Text, "일반 삽입 문자열");
        Qa.Equal(edited.StyleOverride, Qa.Node(editSession, edited.Id).StyleOverride, "일반 삽입이 leaf override를 잃음");
        Qa.Select(editSession, edited.Id, 0, Qa.Elements("a🙂b"));
        editSession.InsertText("e\u0301Z");
        Qa.Equal("e\u0301Z", Qa.Leaf(editSession, edited.Id).Text, "전체 TextNode 교체 문자열");
        Qa.Equal(edited.StyleOverride, Qa.Node(editSession, edited.Id).StyleOverride, "전체 선택 입력이 기존 override를 잃음");

        var wrapped = Qa.Text("A🙂Z") with { StyleOverride = Override(27, "Wrapped Face", "#ABCDEF") };
        var wrapSession = new EditingSession(Workspace([Group([Row(wrapped)])]));
        Qa.Select(wrapSession, wrapped.Id, 1, 2);
        var wrapperId = wrapSession.InsertTemplate(MathTemplateKind.Fraction);
        Qa.Check(wrapperId.HasValue, "부분 텍스트 선택 wrapping 실패");
        var preserved = Qa.Leaf(wrapSession, wrapped.Id);
        Qa.Equal("🙂", preserved.Text, "선택된 부분 텍스트의 기존 node ID 유지");
        Qa.Equal(wrapped.StyleOverride, preserved.StyleOverride, "wrapping이 선택 content override를 잃음");
        Qa.Equal(wrapped.StyleOverride, Qa.Nodes(wrapSession.Workspace).OfType<TextNode>().Single(n => n.Text == "A").StyleOverride,
            "wrapping prefix가 원래 style override를 잃음");
        Qa.Equal(wrapped.StyleOverride, Qa.Nodes(wrapSession.Workspace).OfType<TextNode>().Single(n => n.Text == "Z").StyleOverride,
            "wrapping suffix가 원래 style override를 잃음");

        AssertEmptyTemplateContentStylePreserved();

        var clonedNode = new FractionNode(Guid.NewGuid(),
            Qa.Text("n") with { StyleOverride = Override(FontSize: 33, Foreground: "#FF00AA") },
            Qa.Text("d")) with { StyleOverride = Override(FontFamily: "Fraction Face") };
        var clonedRoot = Qa.Sequence(clonedNode, Qa.Text("tail")) with { StyleOverride = Override(Foreground: "#010203") };
        var sourceRow = Row(clonedRoot) with { StyleOverride = Override(FontSize: 25) };
        var sourceGroup = Group([sourceRow, Row(Qa.Text("neighbor"))]) with
        {
            StyleOverride = Override(FontFamily: "Group Face")
        };
        var cloneSession = new EditingSession(Workspace([sourceGroup]));
        Qa.Caret(cloneSession, Qa.Nodes(cloneSession.Workspace).OfType<TextNode>().Single(n => n.Text == "n").Id, 0);
        Qa.Check(cloneSession.DuplicateCurrentRow(), "style가 있는 row 복제 실패");
        var rows = cloneSession.Workspace.Groups[0].Rows;
        var originalNodes = Qa.Walk(sourceRow.Root).ToArray();
        var cloneNodes = Qa.Walk(rows[1].Root).ToArray();
        Qa.Check(rows[0].Id != rows[1].Id && rows[0].Root.Id != rows[1].Root.Id, "복제가 새 row/root ID를 만들지 않음");
        Qa.Equal(sourceRow.StyleOverride, rows[1].StyleOverride, "row 복제의 style override 손실");
        Qa.Equal(originalNodes.Length, cloneNodes.Length, "복제 node 개수");
        for (var i = 0; i < originalNodes.Length; i++)
        {
            Qa.Check(originalNodes[i].Id != cloneNodes[i].Id, "복제 node가 새 ID를 받지 않음");
            Qa.Equal(originalNodes[i].StyleOverride, cloneNodes[i].StyleOverride, "clone node override 순서 " + i);
        }
        Qa.Equal(sourceGroup.StyleOverride, cloneSession.Workspace.Groups[0].StyleOverride, "row 복제가 부모 group override를 변경");
        Qa.Check(cloneSession.DuplicateCurrentGroup(), "style가 있는 group 복제 실패");
        var copiedGroup = cloneSession.Workspace.Groups[1];
        Qa.Check(copiedGroup.Id != sourceGroup.Id && copiedGroup.Rows[0].Id != sourceRow.Id,
            "group 복제가 새 group/row ID를 만들지 않음");
        Qa.Equal(sourceGroup.StyleOverride, copiedGroup.StyleOverride, "group clone override 손실");
        Qa.Equal(sourceRow.StyleOverride, copiedGroup.Rows[0].StyleOverride, "group clone row override 손실");
        AssertCloneTree(sourceGroup.Rows[0].Root, copiedGroup.Rows[0].Root, "group clone row tree");

        var clearRoot = Qa.Sequence(
            Qa.Text("styled") with { StyleOverride = Override(Foreground: "#AA0000") }, Qa.Text("other"))
            with { StyleOverride = Override(FontSize: 29, FontFamily: "Root Face") };
        var clearRow = Row(clearRoot) with { StyleOverride = Override(Foreground: "#00AA00") };
        var clearGroup = Group([clearRow]) with { StyleOverride = Override(FontSize: 21) };
        var clearSession = new EditingSession(Workspace([clearGroup]));
        Qa.Caret(clearSession, ((TextNode)clearRoot.Children[0]).Id, 0);
        clearSession.MarkSaved(clearSession.CaptureSnapshot());
        var beforeClear = Capture(clearSession);
        Qa.Check(clearSession.Clear(MathClearScope.CurrentRow), "styled root Clear 실패");
        var emptyRoot = clearSession.Workspace.Groups[0].Rows[0].Root;
        Qa.Equal<MathStyleOverride?>(null, emptyRoot.StyleOverride, "Clear가 제거된 node override를 남김");
        Qa.Equal(clearRow.StyleOverride, clearSession.Workspace.Groups[0].Rows[0].StyleOverride, "Clear가 row override를 지움");
        Qa.Equal(clearGroup.StyleOverride, clearSession.Workspace.Groups[0].StyleOverride, "Clear가 group override를 지움");
        var afterClear = Capture(clearSession);
        AssertUndoRedo(clearSession, beforeClear, afterClear, "Clear node override");
    }

    private static void AssertEmptyTemplateContentStylePreserved()
    {
        var kinds = new[]
        {
            MathTemplateKind.Subscript,
            MathTemplateKind.Integral,
            MathTemplateKind.Summation,
            MathTemplateKind.Limit,
            MathTemplateKind.Brackets,
            MathTemplateKind.Cases
        };

        foreach (var kind in kinds)
        {
            var source = Qa.Text(string.Empty, isEmptySlot: true) with
            {
                StyleOverride = Override(23, "Empty Content Face", "#456789")
            };
            var session = new EditingSession(Workspace([Group([Row(source)])]));
            Qa.Caret(session, source.Id, 0);
            session.MarkSaved(session.CaptureSnapshot());
            var before = Capture(session);

            var insertedId = session.InsertTemplate(kind);
            Qa.Check(insertedId.HasValue, kind + "이 빈 스타일 슬롯에 삽입되지 않음");
            var wrapper = Qa.Node(session, insertedId!.Value);
            var content = wrapper switch
            {
                ScriptNode script when kind == MathTemplateKind.Subscript => script.Base,
                IntegralNode integral => integral.Integrand,
                SummationNode summation => summation.Body,
                LimitNode limit => limit.Body,
                BracketsNode brackets => brackets.Content,
                CasesNode cases => cases.Rows[0].Expression,
                _ => throw new InvalidOperationException(kind + "의 의미상 content를 찾지 못했습니다.")
            };
            Qa.Equal(source.Id, content.Id, kind + " 기존 빈 TextNode ID가 의미상 content에 보존되지 않음");
            Qa.Equal(source.StyleOverride, content.StyleOverride,
                kind + " 기존 빈 TextNode StyleOverride가 의미상 content에 보존되지 않음");

            var otherNodes = Qa.Walk(wrapper).Where(node => node.Id != source.Id).ToArray();
            Qa.Check(otherNodes.All(node => node.StyleOverride is null),
                kind + " 새 wrapper/다른 슬롯에 원본 content override가 복사됨");
            var expectedFocus = wrapper switch
            {
                IntegralNode integral => ((TextNode)integral.LowerLimit!).Id,
                SummationNode summation => ((TextNode)summation.LowerLimit!).Id,
                LimitNode limit => ((TextNode)limit.Approach).Id,
                _ => content.Id
            };
            Qa.Equal(new MathCaret(expectedFocus, 0), session.Caret,
                kind + " 빈 슬롯 삽입에서 기존 focus 목적지가 바뀜");
            Qa.Check(session.Selection is null && !session.IsNodeSelected,
                kind + " 삽입 뒤 선택 상태가 남음");
            var after = Capture(session);
            AssertUndoRedo(session, before, after, kind + " 빈 슬롯 style 삽입");
        }
    }

    private static async Task JsonAndRecoveryRoundTrip()
    {
        using var directory = new TempDirectory();
        var fileService = new WorkspaceFileService();
        var leaf = Qa.Text("復元🙂") with { StyleOverride = Override(36, "Recovery Face", "#80FF0000") };
        var root = new FractionNode(Guid.NewGuid(), leaf, Qa.Text("denom")) with
        {
            StyleOverride = Override(Foreground: "#102030")
        };
        var row = Row(root) with { StyleOverride = Override(FontSize: 23) };
        var group = Group([row]) with { StyleOverride = Override(FontFamily: "Group Face") };
        var workspace = Workspace([group], new MathStyle(600, "Legacy Large Face", "#234567"));
        var session = new EditingSession(workspace);
        var path = directory.File("style-roundtrip.aethermath");
        await fileService.SaveAsync(path, session.CaptureSnapshot());
        var reopened = await fileService.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(reopened), "노드/행/그룹 override 파일 왕복");
        Qa.Equal(600d, reopened.Style.FontSize, "기존 v1 root MathStyle의 유효 양수 범위가 좁아짐");

        var recoveryStore = new WorkspaceStateService(directory.File("state"));
        var recovery = new WorkspaceRecovery(Guid.NewGuid(), session.CaptureSnapshot(),
            new DateTimeOffset(2044, 6, 7, 8, 9, 10, TimeSpan.FromHours(9)), path);
        var written = await recoveryStore.SaveRecoveryAsync(recovery);
        Qa.Equal(RecoveryWriteStatus.Written, written.Status, "style snapshot recovery 저장");
        var read = await recoveryStore.ReadRecoveryAsync(recovery.RecoveryId);
        Qa.Equal(RecoveryReadStatus.Found, read.Status, "style recovery 재열기");
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(read.Recovery!.Snapshot.Workspace), "recovery가 style override를 보존하지 않음");

        var legacyPath = directory.File("legacy-v1.aethermath");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        legacy["style"]!["fontSize"] = 600;
        RemovePropertyRecursively(legacy, "styleOverride");
        await File.WriteAllTextAsync(legacyPath, legacy.ToJsonString());
        var legacyLoaded = await fileService.OpenAsync(legacyPath);
        Qa.Equal(600d, legacyLoaded.Style.FontSize, "legacy root style load compatibility");
        Qa.Check(Qa.Nodes(legacyLoaded).All(node => node.StyleOverride is null) &&
                 legacyLoaded.Groups.All(g => g.StyleOverride is null && g.Rows.All(r => r.StyleOverride is null)),
            "필드 누락 legacy schema v1이 잘못된 default override를 가짐");

        var invalidPath = directory.File("invalid-override.aethermath");
        var invalidJson = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var overrideObject = FindFirstObject(invalidJson, "styleOverride") ??
            throw new InvalidOperationException("serialized styleOverride fixture가 없음");
        overrideObject["fontSize"] = 513;
        await File.WriteAllTextAsync(invalidPath, invalidJson.ToJsonString());
        var originalInvalidBytes = await File.ReadAllBytesAsync(invalidPath);
        await Qa.FailsAsync(() => fileService.OpenAsync(invalidPath), "invalid override open",
            typeof(InvalidDataException), typeof(System.Text.Json.JsonException));
        var bytesAfterInvalidRead = await File.ReadAllBytesAsync(invalidPath);
        Qa.Check(originalInvalidBytes.SequenceEqual(bytesAfterInvalidRead), "invalid override open이 파일 bytes를 변경");

        var unknownPath = directory.File("unknown-override.aethermath");
        var unknownJson = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var unknownOverride = FindFirstObject(unknownJson, "styleOverride") ??
            throw new InvalidOperationException("unknown-field fixture의 override가 없음");
        unknownOverride["unknownProperty"] = true;
        await File.WriteAllTextAsync(unknownPath, unknownJson.ToJsonString());
        var unknownOriginalBytes = await File.ReadAllBytesAsync(unknownPath);
        await Qa.FailsAsync(() => fileService.OpenAsync(unknownPath), "unknown override member open",
            typeof(System.Text.Json.JsonException));
        var unknownBytesAfterRead = await File.ReadAllBytesAsync(unknownPath);
        Qa.Check(unknownOriginalBytes.SequenceEqual(unknownBytesAfterRead), "unknown override open이 파일 bytes를 변경");

        var allNull = row with { StyleOverride = new MathStyleOverride() };
        Qa.Equal<MathStyleOverride?>(null, allNull.StyleOverride, "all-null override가 null로 정규화되지 않음");
    }

    private static void OutputConsumesResolvedStyle()
    {
        var baseText = Qa.Text("base");
        var explicitSubscript = Qa.Text("subexplicit") with
        {
            StyleOverride = Override(19, "Script Face", "#80FF0000")
        };
        var explicitScript = new ScriptNode(Guid.NewGuid(), baseText, explicitSubscript, null);
        var autoScript = new ScriptNode(Guid.NewGuid(), Qa.Text("otherbase"), Qa.Text("autosub"), null);
        var fraction = new FractionNode(Guid.NewGuid(), explicitScript, autoScript)
            with
            {
                StyleOverride = Override(FontSize: 34, Foreground: "#80FF0000") with
                {
                    TypesettingStyle = MathTypesettingStyle.Display
                }
            };
        var compactFraction = new FractionNode(Guid.NewGuid(), Qa.Text("compactnum"), Qa.Text("compactden"))
            with
            {
                StyleOverride = Override(FontSize: 34, Foreground: "#80FF0000") with
                {
                    TypesettingStyle = MathTypesettingStyle.Text
                }
            };
        var workspace = Workspace([Group([Row(fraction), Row(compactFraction)])],
            new MathStyle(28, "Base Face", "#112233") { TypesettingStyle = MathTypesettingStyle.Display });
        var before = Qa.Fingerprint(workspace);
        var layout = MathLayoutEngine.Layout(workspace, new AvaloniaMathTextMetrics());
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        var explicitSubGlyphs = glyphs.Where(glyph => glyph.Text == "subexplicit").ToArray();
        var fractionBaseGlyphs = glyphs.Where(glyph => glyph.Text == "base").ToArray();
        var fractionOtherBaseGlyphs = glyphs.Where(glyph => glyph.Text == "otherbase").ToArray();
        var unoverriddenScriptGlyphs = glyphs.Where(glyph => glyph.Text == "autosub").ToArray();
        var compactFractionGlyphs = glyphs.Where(glyph => glyph.Text is "compactnum" or "compactden").ToArray();
        Qa.Check(explicitSubGlyphs.Length > 0, "명시 override 첨자 글리프 primitive 없음");
        Qa.Check(explicitSubGlyphs.All(glyph => glyph.FontFamily == "Script Face" && glyph.Fill == "#80FF0000"),
            "첨자 축소에서 effective 글꼴/ARGB 색 전달 실패");
        Qa.Check(explicitSubGlyphs.All(glyph => glyph.FontSize == 19),
            "명시된 첨자 크기가 상위 수식 축소에 덮임");
        Qa.Check(fractionBaseGlyphs.Length > 0 && fractionOtherBaseGlyphs.Length > 0,
            "분수의 크기 상속 비교용 text glyph 없음");
        Qa.Check(fractionBaseGlyphs.Concat(fractionOtherBaseGlyphs).All(glyph => glyph.FontSize == 34),
            "Display 분수의 Text 자식이 같은 level0 크기를 유지하지 않음");
        var expectedCompactFractionChildSize = 34d * 0.71;
        Qa.Check(compactFractionGlyphs.Length == 2 && compactFractionGlyphs.All(glyph =>
                Math.Abs(glyph.FontSize - expectedCompactFractionChildSize) < 0.000001),
            "Text 분수 자식이 compact level+1 크기로 전이되지 않음");
        Qa.Check(unoverriddenScriptGlyphs.Length > 0 &&
                 unoverriddenScriptGlyphs.All(glyph => Math.Abs(glyph.FontSize - expectedCompactFractionChildSize) < 0.000001),
            "Display 분수 안 Script 자식이 compact level+1 크기로 전이되지 않음");
        var fractionLines = layout.Primitives.OfType<LinePrimitive>().ToArray();
        Qa.Check(fractionLines.Length > 0 && fractionLines.All(line => line.Stroke == "#80FF0000"),
            "분수 bar가 node effective ARGB 색을 사용하지 않음");

        var xml = new SvgExportService().ToSvg(layout);
        var document = XDocument.Parse(xml);
        var redText = document.Descendants().Where(element => element.Name.LocalName == "text" && element.Value == "subexplicit").ToArray();
        Qa.Check(redText.Length > 0, "SVG에서 첨자 glyph를 찾지 못함");
        foreach (var glyph in redText)
        {
            Qa.Equal("#FF0000", (string?)glyph.Attribute("fill"), "SVG alpha 변환 시 RGB 값");
            Qa.Near(128d / 255d,
                double.Parse((string?)glyph.Attribute("fill-opacity") ?? throw new InvalidOperationException("SVG fill-opacity 누락"),
                    System.Globalization.CultureInfo.InvariantCulture), "SVG text alpha");
        }
        var redLines = document.Descendants().Where(element => element.Name.LocalName == "line" &&
            (string?)element.Attribute("stroke") == "#FF0000").ToArray();
        Qa.Check(redLines.Length > 0, "SVG shape에서 alpha RGB stroke가 없음");
        Qa.Near(128d / 255d,
            double.Parse((string?)redLines[0].Attribute("stroke-opacity") ?? throw new InvalidOperationException("SVG stroke-opacity 누락"),
                System.Globalization.CultureInfo.InvariantCulture), "SVG shape alpha");

        var styleDiagnostics = LatexSerializer.GetStyleDiagnostics(workspace);
        Qa.Check(styleDiagnostics.Any(diagnostic => diagnostic.Field == LatexStyleField.FontSize &&
                diagnostic.OwnerId == fraction.Id && diagnostic.Scope == MathPropertyScope.SelectionNode),
            "LaTeX가 분수 명시 크기의 미표현 진단을 제공하지 않음");
        Qa.Check(styleDiagnostics.Any(diagnostic => diagnostic.Field == LatexStyleField.FontFamily &&
                diagnostic.OwnerId == explicitSubscript.Id && diagnostic.Scope == MathPropertyScope.SelectionNode),
            "LaTeX가 첨자 명시 글꼴의 미표현 진단을 제공하지 않음");
        Qa.Check(styleDiagnostics.Any(diagnostic => diagnostic.Field == LatexStyleField.Foreground &&
                diagnostic.OwnerId == explicitSubscript.Id && diagnostic.Scope == MathPropertyScope.SelectionNode),
            "LaTeX가 첨자 명시 색의 미표현 진단을 제공하지 않음");
        Qa.Equal(before, Qa.Fingerprint(workspace), "layout/export가 모델·override를 변경");
    }

    private static MathPropertyTargetSnapshot Capture(EditingSession session, MathPropertyScope scope)
    {
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            "속성 대상 캡처 실패: " + scope);
        return target!;
    }

    private static MathStyleOverride Override(double? FontSize = null, string? FontFamily = null, string? Foreground = null) =>
        new(FontSize, FontFamily, Foreground);

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationGroup Group(EquationRow[] rows) =>
        new(Guid.NewGuid(), [.. rows], AlignmentMode.None, 16);

    private static EquationWorkspace Workspace(EquationGroup[] groups, MathStyle? style = null) =>
        new(Guid.NewGuid(), 1, "속성 QA", [.. groups], style ?? new MathStyle(24, "Default Face", "#102030"));

    private sealed record SessionState(string Fingerprint, MathCaret Caret, MathSelection? Selection,
        Guid? SelectedNodeId, bool IsNodeSelected, long Revision, Guid StateId, bool IsDirty,
        bool CanUndo, bool CanRedo);

    private static SessionState Capture(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), session.Caret, session.Selection,
            session.SelectedNodeId, session.IsNodeSelected, session.Revision, snapshot.StateId,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertUndoRedo(EditingSession session, SessionState before, SessionState after, string label)
    {
        session.Undo();
        var undone = Capture(session);
        Qa.Equal(before.Fingerprint, undone.Fingerprint, label + " undo workspace");
        Qa.Equal(before.Caret, undone.Caret, label + " undo caret");
        Qa.Equal(before.Selection, undone.Selection, label + " undo selection");
        Qa.Equal(before.SelectedNodeId, undone.SelectedNodeId, label + " undo selected node");
        Qa.Equal(before.IsNodeSelected, undone.IsNodeSelected, label + " undo structure-selection state");
        Qa.Equal(before.StateId, undone.StateId, label + " undo state ID");
        Qa.Equal(before.IsDirty, undone.IsDirty, label + " undo dirty");
        Qa.Equal(after.Revision + 1, undone.Revision, label + " undo revision");
        Qa.Check(undone.CanRedo, label + " undo에서 redo 경로가 없음");
        Qa.Equal(before.CanUndo, undone.CanUndo, label + " undo가 선행 history를 바꿈");

        session.Redo();
        var redone = Capture(session);
        Qa.Equal(after.Fingerprint, redone.Fingerprint, label + " redo workspace");
        Qa.Equal(after.Caret, redone.Caret, label + " redo caret");
        Qa.Equal(after.Selection, redone.Selection, label + " redo selection");
        Qa.Equal(after.SelectedNodeId, redone.SelectedNodeId, label + " redo selected node");
        Qa.Equal(after.IsNodeSelected, redone.IsNodeSelected, label + " redo structure-selection state");
        Qa.Equal(after.StateId, redone.StateId, label + " redo state ID");
        Qa.Equal(after.IsDirty, redone.IsDirty, label + " redo dirty");
        Qa.Equal(after.Revision + 2, redone.Revision, label + " redo revision");
        Qa.Check(redone.CanUndo && !redone.CanRedo, label + " redo history boundary");
    }

    private static void AssertStaleWithoutFurtherMutation(EditingSession session,
        MathPropertyTargetSnapshot target, string label)
    {
        var beforeAttempt = Capture(session);
        var result = session.ApplyMathStyle(target, new MathStylePatch(Foreground: "#FEDCBA"));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, result.Status, label + " stale status");
        Qa.Equal(beforeAttempt, Capture(session), label + " 거절 후 추가 상태 변경");
    }

    private static EquationWorkspace ReplaceStyleOnly(EquationWorkspace before, EquationWorkspace after)
    {
        var beforeGroups = before.Groups.ToDictionary(group => group.Id);
        var afterGroups = after.Groups.ToDictionary(group => group.Id);
        return after with
        {
            Style = before.Style,
            Groups = [.. after.Groups.Select(group => group with
            {
                StyleOverride = beforeGroups[group.Id].StyleOverride,
                Rows = [.. group.Rows.Select(row => row with
                {
                    StyleOverride = beforeGroups[group.Id].Rows.Single(old => old.Id == row.Id).StyleOverride,
                    Root = ReplaceNodeStyles(row.Root, beforeGroups[group.Id].Rows.Single(old => old.Id == row.Id).Root)
                })]
            })]
        };
    }

    private static MathNode ReplaceNodeStyles(MathNode current, MathNode before)
    {
        Qa.Equal(current.GetType(), before.GetType(), "Apply 구조 node type");
        Qa.Equal(current.Id, before.Id, "Apply 구조 node ID");
        return current switch
        {
            TextNode node when before is TextNode old => node with { StyleOverride = old.StyleOverride },
            SequenceNode node when before is SequenceNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Children = [.. node.Children.Zip(old.Children, ReplaceNodeStyles)]
            },
            FractionNode node when before is FractionNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Numerator = ReplaceNodeStyles(node.Numerator, old.Numerator),
                Denominator = ReplaceNodeStyles(node.Denominator, old.Denominator)
            },
            RadicalNode node when before is RadicalNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Radicand = ReplaceNodeStyles(node.Radicand, old.Radicand)
            },
            ScriptNode node when before is ScriptNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Base = ReplaceNodeStyles(node.Base, old.Base),
                Subscript = node.Subscript is null ? null : ReplaceNodeStyles(node.Subscript, old.Subscript!),
                Superscript = node.Superscript is null ? null : ReplaceNodeStyles(node.Superscript, old.Superscript!)
            },
            MatrixNode node when before is MatrixNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Cells = [.. node.Cells.Zip(old.Cells, ReplaceNodeStyles)]
            },
            IntegralNode node when before is IntegralNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Integrand = ReplaceNodeStyles(node.Integrand, old.Integrand),
                LowerLimit = node.LowerLimit is null ? null : ReplaceNodeStyles(node.LowerLimit, old.LowerLimit!),
                UpperLimit = node.UpperLimit is null ? null : ReplaceNodeStyles(node.UpperLimit, old.UpperLimit!),
                DifferentialVariable = ReplaceNodeStyles(node.DifferentialVariable, old.DifferentialVariable)
            },
            SummationNode node when before is SummationNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Body = ReplaceNodeStyles(node.Body, old.Body),
                LowerLimit = node.LowerLimit is null ? null : ReplaceNodeStyles(node.LowerLimit, old.LowerLimit!),
                UpperLimit = node.UpperLimit is null ? null : ReplaceNodeStyles(node.UpperLimit, old.UpperLimit!)
            },
            LimitNode node when before is LimitNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Approach = ReplaceNodeStyles(node.Approach, old.Approach),
                Body = ReplaceNodeStyles(node.Body, old.Body)
            },
            BracketsNode node when before is BracketsNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Content = ReplaceNodeStyles(node.Content, old.Content)
            },
            CasesNode node when before is CasesNode old => node with
            {
                StyleOverride = old.StyleOverride,
                Rows = [.. node.Rows.Zip(old.Rows, (currentRow, oldRow) =>
                    new CaseRow(ReplaceNodeStyles(currentRow.Expression, oldRow.Expression),
                        ReplaceNodeStyles(currentRow.Condition, oldRow.Condition)))]
            },
            _ => throw new InvalidOperationException("Apply가 구조를 바꿨거나 node 종류가 다릅니다.")
        };
    }

    private static void AssertCloneTree(MathNode original, MathNode clone, string label)
    {
        var originals = Qa.Walk(original).ToArray();
        var clones = Qa.Walk(clone).ToArray();
        Qa.Equal(originals.Length, clones.Length, label + " node count");
        for (var index = 0; index < originals.Length; index++)
        {
            Qa.Equal(originals[index].GetType(), clones[index].GetType(), label + " node kind " + index);
            Qa.Check(originals[index].Id != clones[index].Id, label + " node ID " + index);
            Qa.Equal(originals[index].StyleOverride, clones[index].StyleOverride, label + " override " + index);
            if (originals[index] is TextNode originalText && clones[index] is TextNode cloneText)
                Qa.Equal(originalText.Text, cloneText.Text, label + " text " + index);
        }
    }

    private static void RemovePropertyRecursively(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(propertyName);
            foreach (var property in obj.ToArray())
                if (property.Value is not null)
                    RemovePropertyRecursively(property.Value, propertyName);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null)
                    RemovePropertyRecursively(child, propertyName);
        }
    }

    private static JsonObject? FindFirstObject(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            if (obj[propertyName] is JsonObject nested)
                return nested;
            foreach (var property in obj)
                if (property.Value is not null && FindFirstObject(property.Value, propertyName) is { } found)
                    return found;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null && FindFirstObject(child, propertyName) is { } found)
                    return found;
        }
        return null;
    }

    private sealed class RecordingMetrics : IMathTextMetrics
    {
        public int Calls { get; private set; }
        public bool ThrowOnMeasure { get; set; }

        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            Calls++;
            if (ThrowOnMeasure)
                throw new InvalidOperationException("candidate face measurement failed");
            return new TextMeasurement(8, 6, 2,
                System.Collections.Immutable.ImmutableArray.Create(0d, 8d));
        }
    }

    private sealed class FixedGlyphProbe(MathGlyphCoverageResult result) : IMathGlyphCoverageProbe
    {
        public MathGlyphCoverageResult Probe(string text, string fontFamily) => result;
    }
}
