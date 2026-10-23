using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class DfracPinCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("PIN-APPLY", "분수 scope pin 및 부분 텍스트 역할·스타일 통합 적용·되돌리기·초기화", PinApplyAcrossScopes);
        yield return new("PIN-DRAFT", "pin 초안의 no-op·무효·stale 보존과 분수 문맥 분리", PinDraftAndStaleBoundaries);
        yield return new("DFRAC-LATEX", "분수 자체의 명시 Display만 dfrac로 직렬화", DfracLatexScope);
        yield return new("DFRAC-IO", "분수 pin·역할·식별자·복제본의 파일 및 recovery 왕복", DfracPersistenceAndCloneRoundTrip);
        yield return new("DFRAC-LAYOUT-PREVIEW", "pin geometry·Inline 투영·동일 frozen Layout SVG 소비", DfracLayoutAndPreview);
    }

    private static void PinApplyAcrossScopes()
    {
        var numerator = Qa.Text("12") with { Role = MathTextRole.Number, StyleOverride = Override(FontSize: 17) };
        var denominator = Qa.Text("n") with { Role = MathTextRole.Identifier };
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator)
        {
            StyleOverride = Override(FontSize: 31)
        };
        var row = new EquationRow(Guid.NewGuid(), fraction)
        {
            StyleOverride = Override(Foreground: "#112233")
        };
        var group = new EquationGroup(Guid.NewGuid(), [row], AlignmentMode.None, 22)
        {
            StyleOverride = Override(FontFamily: "Group Face")
        };
        var workspace = Workspace([group]);
        var session = new EditingSession(workspace);
        Qa.Check(session.TrySelectNode(fraction.Id), "Fraction 선택 실패");
        session.MarkSaved(session.CaptureSnapshot());

        var properties = new MathPropertiesPanelViewModel(session);
        Qa.Check(properties.CanPinTypesettingStyle && !properties.DraftPinTypesettingStyle &&
                 properties.DraftTypesettingStyle == MathTypesettingStyle.Display,
            "상속 Display 분수의 pin 초안 기준값이 잘못되었습니다.");

        var beforeFingerprint = Qa.Fingerprint(session.Workspace);
        var beforeStateId = session.CaptureSnapshot().StateId;
        var beforeRevision = session.Revision;
        var beforeCaret = session.Caret;
        var beforeSelection = session.Selection;
        var beforeSelectedNode = session.SelectedNodeId;
        properties.DraftForeground = "#80FF0000";
        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply, "색상 변경과 Display pin을 합친 초안을 적용할 수 없습니다.");
        properties.ApplyCommand.Execute(null);

        Qa.Equal(beforeRevision + 1, session.Revision, "node pin + 색상의 단일 commit");
        var pinned = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("node pin 뒤 Fraction ID가 사라졌습니다.");
        Qa.Equal(new MathStyleOverride(31, null, "#80FF0000")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        }, pinned.StyleOverride, "pin이 기존 node size와 결합 스타일을 보존하지 않음");
        Qa.Equal(MathTextRole.Number, Qa.Leaf(session, numerator.Id).Role, "pin이 numerator 역할을 변경");
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, denominator.Id).Role, "pin이 denominator 역할을 변경");
        Qa.Equal(17d, Qa.Leaf(session, numerator.Id).StyleOverride?.FontSize, "pin이 자식 명시 크기를 변경");
        Qa.Equal(beforeCaret, session.Caret, "node pin caret 보존");
        Qa.Equal(beforeSelection, session.Selection, "node pin selection 보존");
        Qa.Equal(beforeSelectedNode, session.SelectedNodeId, "node pin selected ID 보존");
        Qa.Check(session.IsDirty && session.CanUndo, "node pin이 saved marker/history를 갱신하지 않음");
        var pinnedFingerprint = Qa.Fingerprint(session.Workspace);

        session.Undo();
        Qa.Equal(beforeFingerprint, Qa.Fingerprint(session.Workspace), "node pin Undo 원본 styles/roles");
        Qa.Equal(beforeStateId, session.CaptureSnapshot().StateId, "node pin Undo saved StateId");
        Qa.Equal(beforeCaret, session.Caret, "node pin Undo caret");
        Qa.Equal(beforeSelection, session.Selection, "node pin Undo selection");
        Qa.Check(!session.IsDirty && session.CanRedo, "node pin Undo dirty/redo 상태");
        session.Redo();
        Qa.Equal(pinnedFingerprint, Qa.Fingerprint(session.Workspace), "node pin Redo 통합 변경 복원");

        var redoRevision = session.Revision;
        properties.ResetOverrideCommand.Execute(null);
        Qa.Equal(redoRevision + 1, session.Revision, "node pin 4-field reset 단일 추가 commit");
        var resetFraction = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("node style reset 뒤 Fraction ID가 사라졌습니다.");
        Qa.Equal<MathStyleOverride?>(null, resetFraction.StyleOverride, "pin reset이 네 스타일 override를 제거하지 않음");
        Qa.Equal(MathTextRole.Number, Qa.Leaf(session, numerator.Id).Role, "style reset이 TextRole을 제거");
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, denominator.Id).Role, "style reset이 sibling TextRole을 제거");
        session.Undo();
        Qa.Equal(pinnedFingerprint, Qa.Fingerprint(session.Workspace), "pin reset Undo 복원");

        properties.SelectedScope = MathPropertyScope.CurrentRow;
        Qa.Check(properties.CanPinTypesettingStyle, "CurrentRow pin을 허용하지 않음");
        var rowBefore = Capture(session);
        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply, "CurrentRow pin 초안을 적용할 수 없음");
        properties.ApplyCommand.Execute(null);
        var rowApplied = Capture(session);
        Qa.Equal(rowBefore.Revision + 1, rowApplied.Revision, "CurrentRow pin 단일 commit");
        Qa.Check(rowApplied.StateId != rowBefore.StateId, "CurrentRow pin이 새 StateId를 만들지 않음");
        Qa.Check(rowApplied.IsDirty && rowApplied.CanUndo, "CurrentRow pin의 dirty/history 상태");
        Qa.Equal(MathTypesettingStyle.Display, session.Workspace.Groups[0].Rows[0].StyleOverride?.TypesettingStyle,
            "CurrentRow pin이 Row override에 기록되지 않음");
        Qa.Equal("#112233", session.Workspace.Groups[0].Rows[0].StyleOverride?.Foreground,
            "CurrentRow pin이 기존 Row foreground를 덮음");
        Qa.Equal<string?>(null, session.Workspace.Groups[0].Rows[0].StyleOverride?.FontFamily,
            "CurrentRow pin이 기존 Row font family를 변경");
        var rowAppliedFingerprint = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(rowBefore.Workspace, Qa.Fingerprint(session.Workspace), "CurrentRow pin Undo 문서/foreground 복원");
        Qa.Equal(rowBefore.StateId, session.CaptureSnapshot().StateId, "CurrentRow pin Undo StateId 복원");
        Qa.Equal(rowBefore.IsDirty, session.IsDirty, "CurrentRow pin Undo dirty 복원");
        Qa.Check(session.CanRedo, "CurrentRow pin Undo 뒤 Redo 없음");
        Qa.Equal("#112233", session.Workspace.Groups[0].Rows[0].StyleOverride?.Foreground,
            "CurrentRow pin Undo가 원본 foreground를 잃음");
        Qa.Equal<string?>(null, session.Workspace.Groups[0].Rows[0].StyleOverride?.FontFamily,
            "CurrentRow pin Undo가 원본 font family를 잃음");
        session.Redo();
        Qa.Equal(rowAppliedFingerprint, Qa.Fingerprint(session.Workspace), "CurrentRow pin Redo 문서/override 복원");
        Qa.Equal(rowApplied.StateId, session.CaptureSnapshot().StateId, "CurrentRow pin Redo 적용 StateId 복원");
        Qa.Equal(rowApplied.IsDirty, session.IsDirty, "CurrentRow pin Redo dirty 복원");

        properties.SelectedScope = MathPropertyScope.CurrentGroup;
        Qa.Check(properties.CanPinTypesettingStyle, "CurrentGroup pin을 허용하지 않음");
        var groupBefore = Capture(session);
        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply, "CurrentGroup pin 초안을 적용할 수 없음");
        properties.ApplyCommand.Execute(null);
        var groupApplied = Capture(session);
        Qa.Equal(groupBefore.Revision + 1, groupApplied.Revision, "CurrentGroup pin 단일 commit");
        Qa.Check(groupApplied.StateId != groupBefore.StateId, "CurrentGroup pin이 새 StateId를 만들지 않음");
        Qa.Check(groupApplied.IsDirty && groupApplied.CanUndo, "CurrentGroup pin의 dirty/history 상태");
        Qa.Equal(MathTypesettingStyle.Display, session.Workspace.Groups[0].StyleOverride?.TypesettingStyle,
            "CurrentGroup pin이 Group override에 기록되지 않음");
        Qa.Equal("Group Face", session.Workspace.Groups[0].StyleOverride?.FontFamily,
            "CurrentGroup pin이 기존 Group family를 덮음");
        var groupAppliedFingerprint = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(groupBefore.Workspace, Qa.Fingerprint(session.Workspace), "CurrentGroup pin Undo 문서/family 복원");
        Qa.Equal(groupBefore.StateId, session.CaptureSnapshot().StateId, "CurrentGroup pin Undo StateId 복원");
        Qa.Equal(groupBefore.IsDirty, session.IsDirty, "CurrentGroup pin Undo dirty 복원");
        Qa.Check(session.CanRedo, "CurrentGroup pin Undo 뒤 Redo 없음");
        Qa.Equal<MathTypesettingStyle?>(null, session.Workspace.Groups[0].StyleOverride?.TypesettingStyle,
            "CurrentGroup pin Undo 후 원본 inherited preset 보존");
        Qa.Equal("Group Face", session.Workspace.Groups[0].StyleOverride?.FontFamily,
            "CurrentGroup pin Undo가 원본 family를 잃음");
        session.Redo();
        Qa.Equal(groupAppliedFingerprint, Qa.Fingerprint(session.Workspace), "CurrentGroup pin Redo 문서/override 복원");
        Qa.Equal(groupApplied.StateId, session.CaptureSnapshot().StateId, "CurrentGroup pin Redo 적용 StateId 복원");
        Qa.Equal(groupApplied.IsDirty, session.IsDirty, "CurrentGroup pin Redo dirty 복원");

        PartialTextSelectionCanPinAndApplyRoleAndStyleAtomically();
    }

    private static void PinDraftAndStaleBoundaries()
    {
        var firstFraction = new FractionNode(Guid.NewGuid(), Qa.Text("a"), Qa.Text("b"));
        var explicitFraction = new FractionNode(Guid.NewGuid(), Qa.Text("c"), Qa.Text("d"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        var otherText = Qa.Text(" + tail");
        var session = new EditingSession(Workspace([firstFraction, explicitFraction, otherText]));
        Qa.Check(session.TrySelectNode(firstFraction.Id), "상속 분수 선택");
        var properties = new MathPropertiesPanelViewModel(session);
        var initial = Capture(session);
        Qa.Check(!properties.DraftPinTypesettingStyle && !properties.CanApply,
            "현재 effective Display choice가 명시 적용 초안으로 잘못 인식되었습니다.");
        properties.DraftTypesettingStyle = MathTypesettingStyle.Display;
        Qa.Check(!properties.CanApply && !properties.DraftPinTypesettingStyle,
            "같은 effective Display 선택이 pin 없이 patch를 만들었습니다.");
        AssertSessionUnchanged(initial, session, "같은 preset 선택");

        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply && properties.DraftPinTypesettingStyle,
            "상속 분수의 explicit pin 의도가 dirty draft로 등록되지 않았습니다.");
        Qa.Check(properties.ExplicitTypesettingStyle is null &&
                 properties.FractionDisplayNotice.Contains("적용 후", StringComparison.Ordinal) &&
                 properties.FractionDisplayNotice.Contains("\\dfrac", StringComparison.Ordinal) &&
                 properties.FractionDisplayNotice.Contains("amsmath", StringComparison.Ordinal),
            "explicit 값이 없는 상속 분수의 pending pin 안내가 적용 후 dfrac/amsmath 의미를 표시하지 않습니다.");

        Qa.Check(session.TrySelectNode(explicitFraction.Id), "이미 explicit Display인 다른 분수 선택");
        Qa.Check(properties.IsDraftStale && properties.DraftPinTypesettingStyle && !properties.CanApply,
            "다른 explicit Display 분수로 이동한 뒤 pending pin이 보존·차단되지 않았습니다.");
        Qa.Check(!properties.IsFractionSelection && properties.FractionDisplayNotice.Length == 0,
            "stale pin 안내가 이전 분수와 새 대상의 문맥을 섞었습니다.");
        Qa.Check(session.TrySelectNode(otherText.Id), "stale pin 대상 TextNode로 이동");
        Qa.Check(properties.IsDraftStale && properties.DraftPinTypesettingStyle && !properties.CanApply,
            "Fraction에서 Text로 이동하며 pin intent가 사라지거나 적용 가능해졌습니다.");
        Qa.Check(!properties.IsFractionSelection && properties.FractionDisplayNotice.Length == 0,
            "stale Fraction→Text에서 이전 분수 전용 안내가 남았습니다.");

        properties.SelectedScope = MathPropertyScope.WorkspaceDefault;
        Qa.Check(properties.IsDraftStale && properties.DraftPinTypesettingStyle && !properties.CanApply &&
                 !properties.CanPinTypesettingStyle,
            "WorkspaceDefault scope 이동이 pending pin을 보존한 채 적용 차단하지 않았습니다.");
        var replacement = new EditingSession(Workspace(new FractionNode(Guid.NewGuid(), Qa.Text("new"), Qa.Text("den"))));
        properties.SetSession(replacement);
        Qa.Check(properties.IsDraftStale && properties.DraftPinTypesettingStyle && !properties.CanApply,
            "Session 교체가 pending pin draft를 stale 상태로 보존하지 않았습니다.");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && !properties.DraftPinTypesettingStyle && !properties.CanApply,
            "ResetDraft가 새 Session baseline을 복원하지 않았습니다.");

        var nullChoiceFraction = new FractionNode(Guid.NewGuid(), Qa.Text("x"), Qa.Text("y"));
        var nullChoiceSession = new EditingSession(Workspace(nullChoiceFraction));
        Qa.Check(nullChoiceSession.TrySelectNode(nullChoiceFraction.Id), "null-choice 분수 선택");
        var nullChoiceProperties = new MathPropertiesPanelViewModel(nullChoiceSession)
        {
            DraftTypesettingStyle = null
        };
        nullChoiceProperties.DraftPinTypesettingStyle = true;
        Qa.Check(nullChoiceProperties.CanApply, "변경 안 함 + pin이 baseline effective style 적용을 허용하지 않음");
        var nullChoiceRevision = nullChoiceSession.Revision;
        nullChoiceProperties.ApplyCommand.Execute(null);
        Qa.Equal(nullChoiceRevision + 1, nullChoiceSession.Revision, "null-choice pin 단일 commit");
        Qa.Equal(MathTypesettingStyle.Display,
            (Qa.Node(nullChoiceSession, nullChoiceFraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "null choice pin이 baseline effective Display를 명시하지 않음");

        var alreadyExplicitSession = new EditingSession(Workspace(explicitFraction));
        Qa.Check(alreadyExplicitSession.TrySelectNode(explicitFraction.Id), "이미 explicit 분수 선택");
        var alreadyExplicit = new MathPropertiesPanelViewModel(alreadyExplicitSession);
        var explicitBaseline = Capture(alreadyExplicitSession);
        Qa.Check(!alreadyExplicit.CanPinTypesettingStyle, "이미 명시된 Fraction preset을 다시 pin할 수 있습니다.");
        alreadyExplicit.DraftPinTypesettingStyle = true;
        Qa.Check(!alreadyExplicit.DraftPinTypesettingStyle && !alreadyExplicit.CanApply,
            "이미 명시된 local preset의 중복 pin intent가 거절되지 않았습니다.");
        AssertSessionUnchanged(explicitBaseline, alreadyExplicitSession, "already explicit pin no-op");

        var invalidSession = new EditingSession(Workspace(new FractionNode(Guid.NewGuid(), Qa.Text("i"), Qa.Text("j"))));
        var invalidFraction = (FractionNode)invalidSession.Workspace.Groups[0].Rows[0].Root;
        Qa.Check(invalidSession.TrySelectNode(invalidFraction.Id), "invalid draft 분수 선택");
        var invalidProperties = new MathPropertiesPanelViewModel(invalidSession);
        var invalidBaseline = Capture(invalidSession);
        invalidProperties.DraftFontSizeText = "513";
        invalidProperties.DraftPinTypesettingStyle = true;
        Qa.Check(!invalidProperties.CanApply && invalidProperties.DraftPinTypesettingStyle,
            "유효하지 않은 다른 필드가 pin과 함께 원자적으로 차단되지 않았습니다.");
        invalidProperties.ApplyCommand.Execute(null);
        AssertSessionUnchanged(invalidBaseline, invalidSession, "invalid style + pin atomic rejection");

        var workspaceSession = new EditingSession(Workspace(Qa.Text("workspace")));
        var workspaceProperties = new MathPropertiesPanelViewModel(workspaceSession)
        {
            SelectedScope = MathPropertyScope.WorkspaceDefault
        };
        var workspaceBaseline = Capture(workspaceSession);
        Qa.Check(!workspaceProperties.CanPinTypesettingStyle &&
                 !string.IsNullOrWhiteSpace(workspaceProperties.TypesettingPinDisabledReason),
            "WorkspaceDefault에서 pin 비활성 사유가 제공되지 않습니다.");
        workspaceProperties.DraftPinTypesettingStyle = true;
        Qa.Check(!workspaceProperties.DraftPinTypesettingStyle && !workspaceProperties.CanApply,
            "WorkspaceDefault의 거절된 pin 요청이 draft에 반영됐습니다.");
        AssertSessionUnchanged(workspaceBaseline, workspaceSession, "WorkspaceDefault pin 거절");
    }

    private static void DfracLatexScope()
    {
        var inherited = new FractionNode(Guid.NewGuid(), Qa.Text("n"), Qa.Text("d"));
        Qa.Equal("{\\displaystyle \\frac{{\\textstyle n}}{{\\textstyle d}}}",
            LatexSerializer.ToLatex(Workspace(inherited)), "workspace Display만 상속한 분수는 frac");

        var localDisplay = new FractionNode(Guid.NewGuid(), Qa.Text("a"), Qa.Text("b"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        Qa.Equal("{\\displaystyle \\dfrac{{\\textstyle a}}{{\\textstyle b}}}",
            LatexSerializer.ToLatex(Workspace(localDisplay)), "Fraction-local Display만 dfrac");

        var groupDisplayFraction = new FractionNode(Guid.NewGuid(), Qa.Text("g"), Qa.Text("h"));
        Qa.Equal("{\\displaystyle \\frac{{\\textstyle g}}{{\\textstyle h}}}",
            LatexSerializer.ToLatex(Workspace(groupDisplayFraction, groupStyle: Override(Typesetting: MathTypesettingStyle.Display))),
            "Group에서 상속한 Display는 dfrac로 오인되지 않음");

        var rowDisplayFraction = new FractionNode(Guid.NewGuid(), Qa.Text("r"), Qa.Text("s"));
        Qa.Equal("{\\displaystyle \\frac{{\\textstyle r}}{{\\textstyle s}}}",
            LatexSerializer.ToLatex(Workspace(rowDisplayFraction, rowStyle: Override(Typesetting: MathTypesettingStyle.Display))),
            "Row에서 상속한 Display는 dfrac로 오인되지 않음");

        var localText = new FractionNode(Guid.NewGuid(), Qa.Text("t"), Qa.Text("u"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Text)
        };
        Qa.Equal("{\\textstyle \\frac{{\\scriptstyle t}}{{\\scriptstyle u}}}",
            LatexSerializer.ToLatex(Workspace(localText)), "Fraction-local Text는 frac");
        var localScript = new FractionNode(Guid.NewGuid(), Qa.Text("p"), Qa.Text("q"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Script)
        };
        Qa.Equal("{\\scriptstyle \\frac{{\\scriptscriptstyle p}}{{\\scriptscriptstyle q}}}",
            LatexSerializer.ToLatex(Workspace(localScript)), "Fraction-local Script는 frac");

        var deepChild = Qa.Text("deep-num") with
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.ScriptScript)
        };
        var nestedDisplay = new FractionNode(Guid.NewGuid(), deepChild, Qa.Text("deep-den"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        var outerDisplay = new FractionNode(Guid.NewGuid(), nestedDisplay, Qa.Text("outer-den"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        var inheritedSibling = new FractionNode(Guid.NewGuid(), Qa.Text("m"), Qa.Text("k"));
        var sequence = Qa.Sequence(outerDisplay, Qa.Text(" + "), inheritedSibling);
        Qa.Equal("{\\displaystyle \\dfrac{{\\displaystyle \\dfrac{{\\scriptscriptstyle deep-num}}{{\\textstyle deep-den}}}}" +
                 "{{\\textstyle outer-den}}}{\\displaystyle  + }" +
                 "{\\displaystyle \\frac{{\\textstyle m}}{{\\textstyle k}}}",
            LatexSerializer.ToLatex(Workspace(sequence)),
            "중첩 명시 Display 분수와 ScriptScript 자식, 뒤 sibling frac의 exact scope 격리");

        var emptyNumerator = Qa.Text(string.Empty, isEmptySlot: true);
        var incompleteDisplay = new FractionNode(Guid.NewGuid(), emptyNumerator, Qa.Text("z"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        var incompleteWorkspace = Workspace(incompleteDisplay);
        Qa.Equal("{\\displaystyle \\dfrac{{\\textstyle }}{{\\textstyle z}}}",
            LatexSerializer.ToLatex(incompleteWorkspace), "필수 빈 슬롯도 표시용 dfrac 소스는 구조를 유지");
        Qa.Equal(emptyNumerator.Id, LatexSerializer.GetIncompleteSlotNodeIds(incompleteWorkspace).Single(),
            "미완성 Display 분수 필수 슬롯 진단 ID");
    }

    private static async Task DfracPersistenceAndCloneRoundTrip()
    {
        using var directory = new TempDirectory();
        var numerator = Qa.Text("12") with
        {
            Role = MathTextRole.Number,
            StyleOverride = Override(FontSize: 21, Foreground: "#80FF0000")
        };
        var denominator = Qa.Text("x") with { Role = MathTextRole.Identifier };
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator)
        {
            StyleOverride = Override(FontFamily: "Fraction Face", Foreground: "#AABBCC", Typesetting: MathTypesettingStyle.Display)
        };
        var row = new EquationRow(Guid.NewGuid(), fraction)
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Text)
        };
        var group = new EquationGroup(Guid.NewGuid(), [row], AlignmentMode.None, 24)
        {
            StyleOverride = Override(FontSize: 34)
        };
        var session = new EditingSession(Workspace([group]));
        Qa.Check(session.TrySelectNode(fraction.Id), "clone source Fraction 선택");
        var originalWorkspace = session.Workspace;
        Qa.Check(session.DuplicateCurrentRow(), "pin Fraction row 복제 실패");
        var clone = session.Workspace.Groups[0].Rows[1];
        Qa.Check(clone.Id != row.Id, "복제 row가 원본 ID를 재사용");
        Qa.Equal(row.StyleOverride, clone.StyleOverride, "복제 row 조판 스타일 보존");
        var clonedFraction = clone.Root as FractionNode
            ?? throw new InvalidOperationException("복제 root가 FractionNode가 아닙니다.");
        Qa.Check(clonedFraction.Id != fraction.Id, "복제 Fraction이 원본 ID를 재사용");
        Qa.Equal(fraction.StyleOverride, clonedFraction.StyleOverride, "복제 Fraction Display pin override 보존");
        Qa.Check(clonedFraction.Numerator.Id != numerator.Id && clonedFraction.Denominator.Id != denominator.Id,
            "복제 분수 자식이 원본 ID를 재사용");
        Qa.Equal(numerator.Role, ((TextNode)clonedFraction.Numerator).Role, "복제 numerator Role 보존");
        Qa.Equal(denominator.Role, ((TextNode)clonedFraction.Denominator).Role, "복제 denominator Role 보존");
        Qa.Equal(numerator.StyleOverride, clonedFraction.Numerator.StyleOverride, "복제 numerator style 보존");
        var originalIds = Qa.Nodes(originalWorkspace).Select(node => node.Id).ToHashSet();
        var cloneIds = Qa.Walk(clone.Root).Select(node => node.Id).ToHashSet();
        var currentIds = Qa.Nodes(session.Workspace).Select(node => node.Id).ToHashSet();
        Qa.Check(originalIds.IsSubsetOf(currentIds), "row 복제가 기존 원본 row의 노드 ID를 교체");
        Qa.Check(!originalIds.Overlaps(cloneIds), "복제 row의 subtree가 원본 node ID를 재사용");

        var fileService = new WorkspaceFileService();
        var filePath = directory.File("explicit-display-pin.aethermath");
        await fileService.SaveAsync(filePath, session.CaptureSnapshot());
        var reopened = await fileService.OpenAsync(filePath);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "Display pin/Role/ID의 실제 파일 왕복");
        Qa.Equal(1, reopened.SchemaVersion, "pin 저장 schema version");

        var stateRoot = directory.File("workspace-state");
        var stateService = new WorkspaceStateService(stateRoot);
        var recoveryId = Guid.NewGuid();
        var recovery = new WorkspaceRecovery(recoveryId, session.CaptureSnapshot(), DateTimeOffset.UtcNow, filePath);
        Qa.Equal(RecoveryWriteStatus.Written, (await stateService.SaveRecoveryAsync(recovery)).Status,
            "Display pin workspace recovery 저장");
        var recovered = await stateService.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, recovered.Status, "Display pin recovery read");
        Qa.Equal(Qa.Fingerprint(session.Workspace),
            Qa.Fingerprint(recovered.Recovery!.Snapshot.Workspace),
            "recovery가 Fraction-local Display/Role/ID를 보존하지 않음");
    }

    private static async Task DfracLayoutAndPreview()
    {
        var pinnedFraction = new FractionNode(Guid.NewGuid(), Qa.Text("a+b"), Qa.Text("n"));
        var nestedFraction = new FractionNode(Guid.NewGuid(), Qa.Text("u"), Qa.Text("v"))
        {
            StyleOverride = Override(Typesetting: MathTypesettingStyle.Display)
        };
        var nestedScript = new ScriptNode(Guid.NewGuid(), Qa.Text("x"), nestedFraction, null);
        var root = Qa.Sequence(pinnedFraction, Qa.Text(" + "), nestedScript);
        var workspace = Workspace(root, style: new MathStyle(30, "Deterministic", "#203040")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });
        var session = new EditingSession(workspace);
        Qa.Check(session.TrySelectNode(pinnedFraction.Id), "geometry pin Fraction 선택");
        session.MarkSaved(session.CaptureSnapshot());
        var metrics = new DeterministicMetrics();
        var prePinLayout = MathLayoutEngine.Layout(session.Workspace, metrics);
        var prePinSignature = LayoutSignature(prePinLayout);
        var properties = new MathPropertiesPanelViewModel(session);
        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply, "geometry pin 초안을 적용할 수 없음");
        properties.ApplyCommand.Execute(null);
        var postPinWorkspace = session.Workspace;
        var postPinLayout = MathLayoutEngine.Layout(postPinWorkspace, metrics);
        Qa.Equal(prePinSignature, LayoutSignature(postPinLayout),
            "이미 inherited Display였던 분수의 명시 pin이 기본 Display geometry를 변경");
        Qa.Equal(MathTypesettingStyle.Display, (Qa.Node(session, pinnedFraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "geometry pin이 Fraction-local Display를 기록하지 않음");
        Qa.Equal(MathTypesettingStyle.Display, (Qa.Node(session, nestedFraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "중첩 분수 명시 Display override가 보존되지 않음");
        var latex = LatexSerializer.ToLatex(postPinWorkspace);
        Qa.Equal(2, Count(latex, "\\dfrac"), "루트 및 중첩 explicit Display 분수의 LaTeX 출력 수");
        Qa.Check(postPinLayout.Nodes.Single(node => node.NodeId == nestedFraction.Id).Bounds.Width > 0,
            "중첩 explicit Display 분수의 layout geometry가 비어 있음");

        var beforePreview = Capture(session);
        var exporter = new RecordingSvgExportService();
        var preview = new MathOutputPreviewViewModel(session, metrics, exporter)
        {
            SelectedMode = MathOutputMode.Inline
        };
        Qa.Check(preview.RefreshPreview(), "Inline output preview 생성 실패");
        var snapshot = preview.PreviewSnapshot ?? throw new InvalidOperationException("Inline frozen snapshot 없음");
        Qa.Equal(MathOutputPreviewState.Ready, preview.State, "Inline preview Ready 상태");
        var projectedWorkspace = postPinWorkspace with
        {
            Style = postPinWorkspace.Style with { TypesettingStyle = MathTypesettingStyle.Text }
        };
        var expectedInline = MathLayoutEngine.Layout(projectedWorkspace, metrics,
            new MathLayoutOptions(FontSize: projectedWorkspace.Style.FontSize));
        Qa.Equal(LayoutSignature(expectedInline), LayoutSignature(snapshot.Layout),
            "Inline 투영에서 Fraction-local Display pin을 유지한 동일 수학 layout");
        Qa.Check(snapshot.Layout.Nodes.Single(node => node.NodeId == pinnedFraction.Id).Bounds.Width > 0 &&
                 snapshot.Layout.Nodes.Single(node => node.NodeId == nestedFraction.Id).Bounds.Width > 0,
            "Inline preview가 local Display fraction geometry를 누락");

        using var directory = new TempDirectory();
        var path = directory.File("pinned-inline.svg");
        var result = await preview.ExportSvgToPathAsync(path);
        Qa.Equal(SvgPreviewExportStatus.Saved, result.Status, "frozen Inline Layout SVG 저장 결과");
        Qa.Check(ReferenceEquals(snapshot.Layout, exporter.LastLayout),
            "SVG exporter가 preview snapshot과 다른 LayoutResult 인스턴스를 받음");
        Qa.Equal(new SvgExportService().ToSvg(snapshot.Layout), File.ReadAllText(path),
            "실제 SVG 파일이 frozen LayoutResult의 serializer 출력과 다름");
        var svg = XDocument.Load(path);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var rootSvg = svg.Root ?? throw new InvalidOperationException("SVG root 없음");
        Qa.Equal("svg", rootSvg.Name.LocalName, "frozen SVG root");
        Qa.Near(snapshot.Layout.Width, Parse((string?)rootSvg.Attribute("width"), "width"),
            "SVG width가 frozen layout과 다름", 0.0005);
        Qa.Near(snapshot.Layout.Height, Parse((string?)rootSvg.Attribute("height"), "height"),
            "SVG height가 frozen layout과 다름", 0.0005);
        var svgText = string.Join("|", rootSvg.Descendants(ns + "text").Select(element => element.Value));
        var layoutText = string.Join("|", snapshot.Layout.Primitives.OfType<GlyphPrimitive>().Select(glyph => glyph.Text));
        Qa.Equal(layoutText, svgText, "SVG glyph가 동일 frozen Layout primitive를 소비하지 않음");
        AssertSessionUnchanged(beforePreview, session, "Inline refresh/export의 원본 Session 순수성");
    }

    private static void PartialTextSelectionCanPinAndApplyRoleAndStyleAtomically()
    {
        var source = Qa.Text("alpha-beta") with
        {
            Role = MathTextRole.PlainText,
            StyleOverride = Override(Foreground: "#112233")
        };
        var session = new EditingSession(Workspace(source));
        Qa.Select(session, source.Id, 2, 7);
        session.MarkSaved(session.CaptureSnapshot());
        var beforeFingerprint = Qa.Fingerprint(session.Workspace);
        var beforeStateId = session.CaptureSnapshot().StateId;
        var beforeRevision = session.Revision;
        var beforeCaret = session.Caret;
        var beforeSelection = session.Selection;

        var properties = new MathPropertiesPanelViewModel(session);
        Qa.Check(properties.CanEditTextRole && !session.IsNodeSelected && session.Selection is not null,
            "부분 텍스트 선택이 TextNode 역할 편집 대상으로 인식되지 않았습니다.");
        properties.DraftTextRole = MathTextRole.Identifier;
        properties.DraftFontSizeText = "19";
        properties.DraftFontFamily = "Pinned Face";
        properties.DraftForeground = "#334455";
        properties.DraftTypesettingStyle = MathTypesettingStyle.Display;
        properties.DraftPinTypesettingStyle = true;
        Qa.Check(properties.CanApply, "부분 선택의 역할·4개 스타일 통합 초안을 적용할 수 없습니다.");

        properties.ApplyCommand.Execute(null);
        Qa.Equal(beforeRevision + 1, session.Revision, "부분 선택 역할+pin 통합 단일 commit");
        var applied = Qa.Leaf(session, source.Id);
        Qa.Equal("alpha-beta", applied.Text, "부분 선택 스타일 적용이 원문을 바꿈");
        Qa.Equal(MathTextRole.Identifier, applied.Role, "부분 선택 통합 적용에서 TextRole 누락");
        Qa.Equal(new MathStyleOverride(19, "Pinned Face", "#334455")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        }, applied.StyleOverride, "부분 선택 통합 적용의 기존 style merge/pin 결과");
        Qa.Equal(beforeCaret, session.Caret, "부분 선택 통합 적용 caret 보존");
        Qa.Equal(beforeSelection, session.Selection, "부분 선택 통합 적용 selection 보존");
        Qa.Check(session.IsDirty && session.CanUndo, "부분 선택 통합 적용 history/saved marker");

        session.Undo();
        Qa.Equal(beforeFingerprint, Qa.Fingerprint(session.Workspace), "부분 선택 통합 Undo 역할·스타일 복원");
        Qa.Equal(beforeStateId, session.CaptureSnapshot().StateId, "부분 선택 통합 Undo saved StateId");
        Qa.Equal(beforeCaret, session.Caret, "부분 선택 통합 Undo caret 복원");
        Qa.Equal(beforeSelection, session.Selection, "부분 선택 통합 Undo selection 복원");
        Qa.Check(!session.IsDirty && session.CanRedo, "부분 선택 통합 Undo dirty/redo 상태");
    }

    private static EquationWorkspace Workspace(MathNode root, MathStyle? style = null,
        MathStyleOverride? groupStyle = null, MathStyleOverride? rowStyle = null)
    {
        var row = new EquationRow(Guid.NewGuid(), root) { StyleOverride = rowStyle };
        var group = new EquationGroup(Guid.NewGuid(), [row], AlignmentMode.None, 22)
        {
            StyleOverride = groupStyle
        };
        return new EquationWorkspace(Guid.NewGuid(), 1, "Dfrac pin QA", [group],
            style ?? new MathStyle(30, "Deterministic", "#203040")
            {
                TypesettingStyle = MathTypesettingStyle.Display
            });
    }

    private static EquationWorkspace Workspace(EquationGroup[] groups) =>
        new(Guid.NewGuid(), 1, "Dfrac pin QA", [.. groups], new MathStyle(30, "Deterministic", "#203040")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });

    private static EquationWorkspace Workspace(params MathNode[] roots)
    {
        var rows = roots.Select(root => new EquationRow(Guid.NewGuid(), root)).ToImmutableArray();
        return Workspace([new EquationGroup(Guid.NewGuid(), rows, AlignmentMode.None, 22)]);
    }

    private static MathStyleOverride Override(double? FontSize = null, string? FontFamily = null,
        string? Foreground = null, MathTypesettingStyle? Typesetting = null)
    {
        var result = new MathStyleOverride(FontSize, FontFamily, Foreground);
        return Typesetting is null ? result : result with { TypesettingStyle = Typesetting };
    }

    private static SessionState Capture(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), snapshot.StateId, snapshot.Revision,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertSessionUnchanged(SessionState expected, EditingSession actual, string label)
    {
        var current = Capture(actual);
        Qa.Equal(expected.Workspace, current.Workspace, label + " workspace");
        Qa.Equal(expected.StateId, current.StateId, label + " StateId");
        Qa.Equal(expected.Revision, current.Revision, label + " revision");
        Qa.Equal(expected.Caret, current.Caret, label + " caret");
        Qa.Equal(expected.Selection, current.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, current.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, current.IsNodeSelected, label + " node selection");
        Qa.Equal(expected.IsDirty, current.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, current.CanUndo, label + " undo");
        Qa.Equal(expected.CanRedo, current.CanRedo, label + " redo");
    }

    private static string LayoutSignature(LayoutResult layout)
    {
        var primitives = layout.Primitives.Select(primitive => primitive switch
        {
            GlyphPrimitive glyph => new PrimitiveShape("glyph", glyph.NodeId, glyph.Text,
                glyph.BaselineOrigin.X, glyph.BaselineOrigin.Y, glyph.FontSize, glyph.FontFamily, glyph.Fill,
                glyph.Italic, null, null, null, null, null, null),
            LinePrimitive line => new PrimitiveShape("line", null, null,
                line.Start.X, line.Start.Y, null, null, null,
                null, line.End.X, line.End.Y, line.StrokeWidth, line.Stroke, null, null),
            PolylinePrimitive polyline => new PrimitiveShape("polyline", null, null,
                null, null, null, null, null,
                null, null, null, polyline.StrokeWidth, polyline.Stroke,
                string.Join(";", polyline.Points.Select(point => FormattableString.Invariant($"{point.X:R},{point.Y:R}"))),
                polyline.Closed),
            _ => throw new InvalidOperationException("미검증 render primitive " + primitive.GetType().Name)
        }).ToArray();
        var nodes = layout.Nodes.Select(node => new
        {
            node.NodeId,
            node.Kind,
            node.AncestorIds,
            node.Bounds,
            node.Baseline,
            node.CaretStops
        }).ToArray();
        return JsonSerializer.Serialize(new { layout.Width, layout.Height, Primitives = primitives, Nodes = nodes });
    }

    private static int Count(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static double Parse(string? value, string attribute) =>
        double.Parse(value ?? throw new InvalidOperationException("SVG " + attribute + " 속성 없음"), CultureInfo.InvariantCulture);

    private sealed record SessionState(string Workspace, Guid StateId, long Revision, MathCaret Caret,
        MathSelection? Selection, Guid? SelectedNodeId, bool IsNodeSelected, bool IsDirty, bool CanUndo, bool CanRedo);

    private sealed record PrimitiveShape(string Kind, Guid? NodeId, string? Text,
        double? X1, double? Y1, double? FontSize, string? FontFamily, string? Fill, bool? Italic,
        double? X2, double? Y2, double? StrokeWidth, string? Stroke, string? Points, bool? Closed);

    private sealed class DeterministicMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.53 : 0.50);
            var caretX = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                caretX.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.23, caretX.MoveToImmutable());
        }
    }

    private sealed class RecordingSvgExportService : ISvgExportService
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
