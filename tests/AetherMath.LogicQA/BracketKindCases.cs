using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class BracketKindCases
{
    private const double SvgTolerance = 0.00051;

    public static IEnumerable<QaCase> All()
    {
        yield return new("BRACKET-KIND-CORE", "괄호 종류 patch의 대상·범위·유효성·무변경 경계", CorePatchContract);
        yield return new("BRACKET-KIND-APPLY", "괄호 종류와 스타일 적용·초기화의 단일 이력 및 내용 보존", ApplyAndHistory);
        yield return new("BRACKET-KIND-VM", "괄호 속성 초안·선택지와 이동 후 stale 보존", DraftAndStaleBoundaries);
        yield return new("BRACKET-KIND-IO", "네 괄호 종류의 복제·실제 파일·복구 왕복과 잘못된 JSON", CloneAndPersistenceRoundTrip);
        yield return new("BRACKET-KIND-LATEX", "네 괄호 종류의 정확한 LaTeX 구분자와 필수 슬롯 진단", LatexKindsAndIncompleteContent);
        yield return new("BRACKET-KIND-LAYOUT-PREVIEW", "네 모양의 layout·Ready preview·실제 SVG 소비", LayoutPreviewAndSvgConsumers);
    }

    private static void CorePatchContract()
    {
        var fixture = CreateBracket(MathBracketKind.Parentheses, "x+y");
        var session = new EditingSession(Workspace(fixture.Bracket));
        Qa.Check(session.TrySelectNode(fixture.Bracket.Id), "괄호 전체 선택 실패");
        session.MarkSaved(session.CaptureSnapshot());

        var initial = CaptureState(session);
        var target = Capture(session, MathPropertyScope.SelectionNode);
        var nullPatch = session.ApplyMathProperties(target, new MathPropertiesPatch(new MathStylePatch()));
        Qa.Equal(MathPropertyChangeStatus.NoChange, nullPatch.Status, "nullable kind 없음은 미변경");
        AssertUnchanged(initial, session, "nullable kind 없음");
        var sameKind = session.ApplyMathProperties(target,
            new MathPropertiesPatch(new MathStylePatch(), BracketKind: MathBracketKind.Parentheses));
        Qa.Equal(MathPropertyChangeStatus.NoChange, sameKind.Status, "현재와 같은 kind");
        AssertUnchanged(initial, session, "같은 kind patch");

        foreach (var kind in Enum.GetValues<MathBracketKind>())
        {
            var before = CaptureState(session);
            var revision = session.Revision;
            var result = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
                new MathPropertiesPatch(new MathStylePatch(), BracketKind: kind));
            if (((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Kind == kind && result.Status == MathPropertyChangeStatus.NoChange)
            {
                AssertUnchanged(before, session, "순회 중 같은 괄호 kind " + kind);
                continue;
            }

            Qa.Equal(MathPropertyChangeStatus.Applied, result.Status, "유효한 괄호 kind 적용 " + kind);
            Qa.Equal(revision + 1, session.Revision, "괄호 kind 단일 commit " + kind);
            Qa.Equal(kind, ((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Kind, "저장된 괄호 kind " + kind);
            session.Undo();
            Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), "괄호 kind undo " + kind);
            Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, "괄호 kind undo StateId " + kind);
            Qa.Equal(before.Caret, session.Caret, "괄호 kind undo caret " + kind);
            Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "괄호 kind undo selected ID " + kind);
            Qa.Check(session.CanRedo, "괄호 kind undo 뒤 redo 없음 " + kind);
            session.Redo();
            Qa.Equal(kind, ((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Kind, "괄호 kind redo " + kind);
            Qa.Equal(before.Caret, session.Caret, "괄호 kind redo caret " + kind);
            Qa.Check(session.CanUndo && !session.CanRedo, "괄호 kind redo history " + kind);
        }

        var invalidBefore = CaptureState(session);
        var invalid = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(FontSize: 41, Foreground: "#8099AABB"),
                MathTypesettingStyle.Script, BracketKind: (MathBracketKind)999));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, invalid.Status, "정의되지 않은 kind 거절");
        AssertUnchanged(invalidBefore, session, "잘못된 kind와 결합한 스타일의 원자 거절");

        AssertUnsupportedTarget(fixture, MathPropertyScope.SelectionNode,
            targetSession => Qa.Check(targetSession.TrySelectNode(fixture.ContentA.Id), "Text 전체 선택 실패"),
            "전체 선택한 Text");
        AssertUnsupportedTarget(fixture, MathPropertyScope.SelectionNode,
            targetSession => Qa.Caret(targetSession, fixture.ContentA.Id, 1),
            "괄호 내용 내부 caret");
        AssertUnsupportedTarget(fixture, MathPropertyScope.SelectionNode,
            targetSession => Qa.Select(targetSession, fixture.ContentA.Id, 0, 1),
            "괄호 내용 일부 선택");
        foreach (var scope in new[]
                 {
                     MathPropertyScope.CurrentRow,
                     MathPropertyScope.CurrentGroup,
                     MathPropertyScope.WorkspaceDefault
                 })
        {
            AssertUnsupportedTarget(fixture, scope,
                targetSession => Qa.Check(targetSession.TrySelectNode(fixture.Bracket.Id), "괄호 선택 실패 " + scope),
                "괄호 선택 후 비지원 scope " + scope);
        }
        AssertUnsupportedTarget(fixture, MathPropertyScope.SelectionNode,
            targetSession => Qa.Check(targetSession.TrySelectNode(fixture.Content.Id), "괄호 content 구조 선택 실패"),
            "괄호 안의 비괄호 구조 선택");

        var staleFixture = CreateBracket(MathBracketKind.Square, "stale");
        var staleSession = new EditingSession(Workspace(staleFixture.Bracket));
        Qa.Check(staleSession.TrySelectNode(staleFixture.Bracket.Id), "stale fixture 괄호 선택");
        var staleTarget = Capture(staleSession, MathPropertyScope.SelectionNode);
        Qa.Caret(staleSession, staleFixture.ContentA.Id, staleFixture.ContentA.Text.Length);
        Qa.Check(staleSession.InsertText("!"), "stale fixture 내용 편집");
        var staleBeforeAttempt = CaptureState(staleSession);
        var staleInvalid = staleSession.ApplyMathProperties(staleTarget,
            new MathPropertiesPatch(new MathStylePatch(FontSize: 44), BracketKind: (MathBracketKind)999));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, staleInvalid.Status, "stale 우선순위가 invalid enum보다 앞섬");
        AssertUnchanged(staleBeforeAttempt, staleSession, "stale 거절 후 현재 상태");
    }

    private static void ApplyAndHistory()
    {
        var fixture = CreateBracket(MathBracketKind.Parentheses, "a+b", new MathStyleOverride(22, "Old Face", "#223344")
        {
            TypesettingStyle = MathTypesettingStyle.Text
        });
        var session = new EditingSession(Workspace(fixture.Bracket));
        Qa.Check(session.TrySelectNode(fixture.Bracket.Id), "Apply fixture 괄호 선택");
        session.MarkSaved(session.CaptureSnapshot());
        var before = CaptureState(session);
        var contentFingerprint = Qa.NodeFingerprint(fixture.Content);
        var result = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(36, "Updated Face", "#80445566"),
                MathTypesettingStyle.Script, BracketKind: MathBracketKind.Angle));
        Qa.Equal(MathPropertyChangeStatus.Applied, result.Status, "kind+style+preset 결합 Apply");
        Qa.Equal(before.Revision + 1, session.Revision, "결합 Apply 단일 commit");
        AssertBracketContentAndSelection(session, fixture, contentFingerprint, MathBracketKind.Angle, "결합 Apply");
        Qa.Equal(new MathStyleOverride(36, "Updated Face", "#80445566")
        {
            TypesettingStyle = MathTypesettingStyle.Script
        }, Qa.Node(session, fixture.Bracket.Id).StyleOverride, "결합 Apply 괄호 override");
        var applied = CaptureState(session);
        AssertUndoRedo(session, before, applied, "kind+style+preset 결합 Apply");

        var styleOnly = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(Foreground: "#AABBCC")));
        Qa.Equal(MathPropertyChangeStatus.Applied, styleOnly.Status, "kind 전에 별도 스타일 적용");
        var beforeKind = CaptureState(session);
        var kindOnly = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(), BracketKind: MathBracketKind.Square));
        Qa.Equal(MathPropertyChangeStatus.Applied, kindOnly.Status, "스타일 적용 후 kind 변경");
        var styledBracket = (BracketsNode)Qa.Node(session, fixture.Bracket.Id);
        Qa.Equal(MathBracketKind.Square, styledBracket.Kind, "스타일 적용 후 선택 kind");
        Qa.Equal(new MathStyleOverride(36, "Updated Face", "#AABBCC")
        {
            TypesettingStyle = MathTypesettingStyle.Script
        }, styledBracket.StyleOverride, "kind 변경이 먼저 저장된 스타일 override를 덮음");
        var kindApplied = CaptureState(session);
        AssertUndoRedo(session, beforeKind, kindApplied, "스타일 뒤 kind 변경");

        var reset = session.ResetMathStyleOverride(Capture(session, MathPropertyScope.SelectionNode));
        Qa.Equal(MathPropertyChangeStatus.Applied, reset.Status, "괄호 스타일 초기화");
        Qa.Equal<MathStyleOverride?>(null, Qa.Node(session, fixture.Bracket.Id).StyleOverride,
            "style reset이 all-null override를 남김");
        Qa.Equal(MathBracketKind.Square, ((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Kind,
            "style reset이 괄호 kind를 초기화");
        Qa.Equal(contentFingerprint, Qa.NodeFingerprint(((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Content),
            "style reset이 content subtree를 바꿈");
        session.Undo();
        Qa.Equal(new MathStyleOverride(36, "Updated Face", "#AABBCC")
        {
            TypesettingStyle = MathTypesettingStyle.Script
        }, Qa.Node(session, fixture.Bracket.Id).StyleOverride, "style reset undo가 명시 override를 복원");
        Qa.Equal(MathBracketKind.Square, ((BracketsNode)Qa.Node(session, fixture.Bracket.Id)).Kind,
            "style reset undo에서 괄호 kind");
    }

    private static void DraftAndStaleBoundaries()
    {
        var first = CreateBracket(MathBracketKind.Parentheses, "first");
        var second = CreateBracket(MathBracketKind.Curly, "second");
        var session = new EditingSession(Workspace(first.Bracket, second.Bracket));
        Qa.Check(session.TrySelectNode(first.Bracket.Id), "VM 첫 괄호 선택");
        var properties = new MathPropertiesPanelViewModel(session);
        Qa.Check(properties.BracketKindChoices.Select(choice => choice.Value).SequenceEqual(new MathBracketKind?[]
            {
                null, MathBracketKind.Parentheses, MathBracketKind.Square,
                MathBracketKind.Curly, MathBracketKind.Angle
            }), "괄호 choices 순서");
        Qa.Check(properties.BracketKindChoices.Select(choice => choice.DisplayName).SequenceEqual(new[]
            {
                "변경 안 함", "소괄호 ( )", "대괄호 [ ]", "중괄호 { }", "꺾쇠 ⟨ ⟩"
            }), "괄호 choices 표시명 순서");
        Qa.Equal(MathBracketKind.Parentheses, properties.CurrentBracketKind, "현재 kind 표시");
        Qa.Equal("소괄호", properties.CurrentBracketKindLabel, "현재 kind 한국어 label");
        Qa.Check(properties.CanEditBracketKind, "whole BracketsNode 선택이 편집 가능으로 표시되지 않음");
        Qa.Equal(string.Empty, properties.BracketKindDisabledReason,
            "편집 가능한 괄호에 비활성 사유가 남아 있음");
        Qa.Equal<MathBracketKind?>(null, properties.DraftBracketKind, "초기 nullable kind draft");

        var sameChoiceBefore = CaptureState(session);
        properties.SelectedBracketKindChoice = properties.BracketKindChoices.Single(choice =>
            choice.Value == MathBracketKind.Parentheses);
        Qa.Check(!properties.CanApply, "현재 kind와 같은 draft를 변경으로 처리");
        AssertUnchanged(sameChoiceBefore, session, "같은 kind 선택은 이력을 바꾸지 않음");

        properties.DraftBracketKind = MathBracketKind.Angle;
        Qa.Check(properties.CanApply, "유효 kind 초안 적용 불가");
        Qa.Check(session.TrySelectNode(second.Bracket.Id), "다른 괄호로 이동");
        Qa.Check(properties.IsDraftStale && !properties.CanApply, "선택 이동 뒤 kind 초안 stale/차단");
        Qa.Equal(MathBracketKind.Angle, properties.DraftBracketKind, "대상 이동 뒤 kind draft 보존");
        Qa.Equal<MathBracketKind?>(null, properties.CurrentBracketKind, "stale 대상의 현재 kind를 유효값처럼 표시");
        Qa.Check(!properties.CanEditBracketKind && properties.BracketKindNotice.Contains("오래", StringComparison.Ordinal),
            "stale kind 진단/편집 차단");
        Qa.Check(properties.BracketKindDisabledReason.Length > 0,
            "stale 괄호 대상에 비활성 사유가 없음");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && !properties.CanApply, "ResetDraft가 새 baseline을 재수집하지 않음");
        Qa.Equal(MathBracketKind.Curly, properties.CurrentBracketKind, "ResetDraft 뒤 새 선택 kind");
        Qa.Equal<MathBracketKind?>(null, properties.DraftBracketKind, "ResetDraft 뒤 kind patch가 남음");

        properties.DraftBracketKind = MathBracketKind.Square;
        properties.SelectedScope = MathPropertyScope.CurrentRow;
        Qa.Check(properties.IsDraftStale && !properties.CanApply, "scope 이동 뒤 dirty kind draft stale");
        Qa.Equal(MathBracketKind.Square, properties.DraftBracketKind, "scope stale 뒤 kind draft 보존");
        Qa.Check(!properties.CanEditBracketKind, "CurrentRow에서 bracket kind 편집 가능 표시");
        Qa.Check(properties.BracketKindDisabledReason.Length > 0,
            "비지원 scope의 괄호 비활성 사유가 없음");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && properties.DraftBracketKind is null,
            "비지원 scope에서 ResetDraft가 stale kind를 재수집하지 않음");

        properties.SelectedScope = MathPropertyScope.SelectionNode;
        Qa.Equal(MathBracketKind.Curly, properties.CurrentBracketKind, "SelectionNode 재진입 current kind");
        properties.DraftBracketKind = MathBracketKind.Parentheses;
        Qa.Caret(session, second.ContentA.Id, second.ContentA.Text.Length);
        Qa.Check(session.InsertText("!"), "괄호 content 변경으로 revision stale 준비");
        Qa.Check(properties.IsDraftStale && !properties.CanApply, "content revision 변경 뒤 kind draft stale");
        Qa.Equal(MathBracketKind.Parentheses, properties.DraftBracketKind, "content revision 뒤 kind draft 보존");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && !properties.CanApply && !properties.CanEditBracketKind,
            "caret 대상의 ResetDraft 상태");

        Qa.Check(session.TrySelectNode(second.Bracket.Id), "session 교체 전 bracket 재선택");
        properties.DraftBracketKind = MathBracketKind.Angle;
        var replacementFixture = CreateBracket(MathBracketKind.Square, "replacement");
        var replacementSession = new EditingSession(Workspace(replacementFixture.Bracket));
        Qa.Check(replacementSession.TrySelectNode(replacementFixture.Bracket.Id), "replacement bracket 선택");
        properties.SetSession(replacementSession);
        Qa.Check(properties.IsDraftStale && !properties.CanApply, "Session 교체 뒤 kind draft stale");
        Qa.Equal(MathBracketKind.Angle, properties.DraftBracketKind, "Session 교체 뒤 기존 kind draft 보존");
        Qa.Equal<MathBracketKind?>(null, properties.CurrentBracketKind, "Session stale 중 current kind 노출");
        properties.ResetDraftCommand.Execute(null);
        Qa.Check(!properties.IsDraftStale && properties.CurrentBracketKind == MathBracketKind.Square &&
                 properties.DraftBracketKind is null, "Session 교체 후 ResetDraft가 새 kind baseline 재수집");
    }

    private static async Task CloneAndPersistenceRoundTrip()
    {
        using var directory = new TempDirectory();
        var kinds = Enum.GetValues<MathBracketKind>();
        var brackets = kinds.Select((kind, index) => CreateBracket(kind, "x" + index,
            new MathStyleOverride(22 + index, "Bracket Face " + index, "#AABBCC")
            {
                TypesettingStyle = index % 2 == 0 ? MathTypesettingStyle.Display : MathTypesettingStyle.Script
            })).ToArray();
        var roots = brackets.Select(fixture => (MathNode)fixture.Bracket).ToArray();
        var initialWorkspace = Workspace(roots);
        var workspace = initialWorkspace with
        {
            Groups = [initialWorkspace.Groups[0] with
            {
                StyleOverride = new MathStyleOverride(FontFamily: "Group Face")
                {
                    TypesettingStyle = MathTypesettingStyle.Text
                }
            }]
        };
        var session = new EditingSession(workspace);
        Qa.Check(session.TrySelectNode(brackets[2].Bracket.Id), "괄호 복제 fixture 선택");
        var originalFingerprint = Qa.Fingerprint(session.Workspace);
        var originalIds = Qa.Nodes(session.Workspace).Select(node => node.Id).ToHashSet();
        Qa.Check(session.DuplicateCurrentGroup(), "네 종류 괄호 group 복제 실패");
        var clone = session.Workspace.Groups[1];
        Qa.Check(clone.Id != workspace.Groups[0].Id, "복제 group이 원본 ID를 재사용");
        Qa.Equal(workspace.Groups[0].StyleOverride, clone.StyleOverride, "복제 group 괄호 fixture preset/style 보존");
        var clonedBrackets = clone.Rows.Select(row => row.Root).OfType<BracketsNode>().ToArray();
        Qa.Equal(kinds.Length, clonedBrackets.Length, "복제 group 괄호 개수");
        for (var index = 0; index < kinds.Length; index++)
        {
            Qa.Equal(kinds[index], clonedBrackets[index].Kind, "복제 괄호 kind " + kinds[index]);
            Qa.Check(clonedBrackets[index].Id != brackets[index].Bracket.Id, "복제 괄호가 원본 ID 재사용 " + kinds[index]);
            Qa.Equal(brackets[index].Bracket.StyleOverride, clonedBrackets[index].StyleOverride,
                "복제 괄호 명시 style " + kinds[index]);
            Qa.Equal(NodeShapeFingerprint(brackets[index].Content), NodeShapeFingerprint(clonedBrackets[index].Content),
                "복제 괄호 content의 구조/Role/style 변경 " + kinds[index]);
        }
        Qa.Check(!originalIds.Overlaps(clone.Rows.SelectMany(row => Qa.Walk(row.Root)).Select(node => node.Id)),
            "복제 group의 node ID가 원본과 겹침");
        Qa.Check(originalIds.IsSubsetOf(Qa.Nodes(session.Workspace).Select(node => node.Id).ToHashSet()),
            "복제 중 원본 node ID가 교체됨");
        Qa.Check(Qa.Fingerprint(session.Workspace) != originalFingerprint, "group 복제가 workspace를 변경하지 않음");

        var fileService = new WorkspaceFileService();
        var path = directory.File("bracket-kinds.aethermath");
        await fileService.SaveAsync(path, session.CaptureSnapshot());
        var reopened = await fileService.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "네 kind·복제 ID·style 실제 파일 왕복");
        Qa.Equal(1, reopened.SchemaVersion, "bracket kind schema version");

        var stateService = new WorkspaceStateService(directory.File("workspace-state"));
        var recoveryId = Guid.NewGuid();
        var recovery = new WorkspaceRecovery(recoveryId, session.CaptureSnapshot(), DateTimeOffset.UtcNow, path);
        Qa.Equal(RecoveryWriteStatus.Written, (await stateService.SaveRecoveryAsync(recovery)).Status,
            "괄호 kind workspace recovery 저장");
        var recovered = await stateService.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, recovered.Status, "괄호 kind recovery 읽기");
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(recovered.Recovery!.Snapshot.Workspace),
            "복구 왕복 괄호 kind/ID/style");

        var json = JsonNode.Parse(await File.ReadAllTextAsync(path)) ??
                   throw new InvalidOperationException("저장된 bracket JSON 없음");
        var badKind = json.DeepClone();
        FindBracketJson(badKind)["kind"] = 999;
        var invalidPath = directory.File("invalid-kind.aethermath");
        await File.WriteAllTextAsync(invalidPath, badKind.ToJsonString());
        await Qa.FailsAsync(() => fileService.OpenAsync(invalidPath), "알 수 없는 괄호 enum 거절",
            typeof(InvalidDataException), typeof(JsonException));

        var extraField = json.DeepClone();
        FindBracketJson(extraField)["unmappedBracketField"] = "preserve strict schema";
        var unknownPath = directory.File("unknown-field.aethermath");
        await File.WriteAllTextAsync(unknownPath, extraField.ToJsonString());
        await Qa.FailsAsync(() => fileService.OpenAsync(unknownPath), "알 수 없는 괄호 필드 거절", typeof(JsonException));
    }

    private static void LatexKindsAndIncompleteContent()
    {
        var expected = new Dictionary<MathBracketKind, string>
        {
            [MathBracketKind.Parentheses] = @"{\displaystyle \left(\mathrm{x}\right)}",
            [MathBracketKind.Square] = @"{\displaystyle \left[\mathrm{x}\right]}",
            [MathBracketKind.Curly] = @"{\displaystyle \left\{\mathrm{x}\right\}}",
            [MathBracketKind.Angle] = @"{\displaystyle \left\langle{\mathrm{x}}\right\rangle{}}"
        };
        var outputs = new List<string>();
        foreach (var pair in expected)
        {
            var content = Qa.Text("x") with { Role = MathTextRole.MathSymbol };
            var bracket = new BracketsNode(Guid.NewGuid(), pair.Key, content);
            var workspace = Workspace(bracket);
            var latex = LatexSerializer.ToLatex(workspace);
            Qa.Equal(pair.Value, latex, "괄호 kind 정확한 LaTeX " + pair.Key);
            Qa.Check(latex.IndexOf("\\mathrm{x}", StringComparison.Ordinal) > latex.IndexOf("\\left", StringComparison.Ordinal) &&
                     latex.IndexOf("\\mathrm{x}", StringComparison.Ordinal) < latex.IndexOf("\\right", StringComparison.Ordinal),
                "괄호 안쪽 content LaTeX 순서 " + pair.Key);
            outputs.Add(latex);
        }
        Qa.Equal(4, outputs.Distinct(StringComparer.Ordinal).Count(), "네 괄호 kind LaTeX 결과가 구분되지 않음");

        var requiredSlot = Qa.Text("");
        var incompleteBracket = new BracketsNode(Guid.NewGuid(), MathBracketKind.Angle, requiredSlot);
        var incompleteWorkspace = Workspace(incompleteBracket);
        Qa.Equal(requiredSlot.Id, LatexSerializer.GetIncompleteSlotNodeIds(incompleteWorkspace).Single(),
            "괄호 내부 필수 빈 content 진단 ID");
        var incompleteLatex = LatexSerializer.ToLatex(incompleteWorkspace);
        Qa.Check(incompleteLatex.Contains("\\left\\langle", StringComparison.Ordinal) &&
                 incompleteLatex.Contains("\\right\\rangle", StringComparison.Ordinal),
            "필수 content가 비어도 생성되는 괄호 source 구분자");
    }

    private static async Task LayoutPreviewAndSvgConsumers()
    {
        var metrics = new DeterministicMetrics();
        var kinds = Enum.GetValues<MathBracketKind>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        MathRect? referenceContentBounds = null;
        var pointCounts = new Dictionary<MathBracketKind, int>
        {
            [MathBracketKind.Parentheses] = 9,
            [MathBracketKind.Square] = 4,
            [MathBracketKind.Curly] = 11,
            [MathBracketKind.Angle] = 3
        };
        foreach (var kind in kinds)
        {
            var fixture = CreateBracket(kind, "xy", contentRole: MathTextRole.MathSymbol);
            var layout = MathLayoutEngine.Layout(Workspace(fixture.Bracket), metrics);
            var delimiterLines = layout.Primitives.OfType<PolylinePrimitive>().ToArray();
            Qa.Equal(2, delimiterLines.Length, "괄호 delimiter primitive 쌍 " + kind);
            Qa.Check(delimiterLines.All(line => !line.Closed), "괄호 delimiter가 닫힌 path로 출력 " + kind);
            Qa.Equal(2 * pointCounts[kind], delimiterLines.Sum(line => line.Points.Length),
                "괄호 delimiter 실제 점 개수 " + kind);
            var signature = string.Join("|", delimiterLines.Select(line =>
                string.Join(";", line.Points.Select(point => $"{point.X:R},{point.Y:R}"))));
            Qa.Check(signatures.Add(signature), "괄호 종류에 따른 실제 delimiter 좌표가 중복됨 " + kind);
            var contentBounds = layout.Nodes.Single(node => node.NodeId == fixture.ContentA.Id).Bounds;
            if (referenceContentBounds is MathRect reference)
            {
                Qa.Near(reference.Width, contentBounds.Width, "괄호 종류별 content 폭 " + kind);
                Qa.Near(reference.Height, contentBounds.Height, "괄호 종류별 content 높이 " + kind);
            }
            else
            {
                referenceContentBounds = contentBounds;
            }
        }

        using var directory = new TempDirectory();
        var fixtures = kinds.Select(kind => CreateBracket(kind, "xy", contentRole: MathTextRole.MathSymbol)).ToArray();
        var roots = fixtures.Select(fixture => (MathNode)fixture.Bracket).ToArray();
        var session = new EditingSession(Workspace(roots));
        session.MarkSaved(session.CaptureSnapshot());
        var before = CaptureState(session);
        var preview = new MathOutputPreviewViewModel(session, metrics, new SvgExportService());
        foreach (var mode in new[]
                 {
                     MathOutputMode.WorkspaceStyle, MathOutputMode.Display, MathOutputMode.Inline
                 })
        {
            preview.SelectedMode = mode;
            Qa.Check(preview.RefreshPreview(), "괄호 preview refresh " + mode);
            var snapshot = preview.PreviewSnapshot ?? throw new InvalidOperationException("괄호 preview snapshot 없음");
            Qa.Equal(MathOutputPreviewState.Ready, preview.State, "populated bracket preview Ready " + mode);
            Qa.Check(preview.CanExportSvg, "완성 괄호 preview의 SVG 저장 비활성 " + mode);
            Qa.Equal(mode, snapshot.Mode, "괄호 preview mode " + mode);
            Qa.Equal(session.Workspace.Id, snapshot.WorkspaceId, "괄호 preview WorkspaceId " + mode);
            Qa.Equal(session.Revision, snapshot.Revision, "괄호 preview revision " + mode);
            Qa.Equal(session.CaptureSnapshot().StateId, snapshot.StateId, "괄호 preview StateId " + mode);
            foreach (var fixture in fixtures)
            {
                Qa.Check(snapshot.Layout.Nodes.Any(node => node.NodeId == fixture.ContentA.Id) &&
                         snapshot.Layout.Nodes.Any(node => node.NodeId == fixture.ContentB.Id),
                    "frozen preview Layout에서 괄호 content ID가 사라짐 " + mode);
            }
            Qa.Equal(8, snapshot.Layout.Primitives.OfType<PolylinePrimitive>().Count(),
                "네 괄호 두 delimiter씩 frozen Layout에 포함 " + mode);

            var path = directory.File($"bracket-{mode}.svg");
            var export = await preview.ExportSvgToPathAsync(path);
            Qa.Equal(SvgPreviewExportStatus.Saved, export.Status, "괄호 frozen SVG 저장 " + mode);
            var xmlText = await File.ReadAllTextAsync(path);
            Qa.Equal(new SvgExportService().ToSvg(snapshot.Layout), xmlText,
                "실제 SVG 파일이 현재 frozen Layout과 다름 " + mode);
            var svg = XDocument.Parse(xmlText).Root ?? throw new InvalidOperationException("괄호 SVG root 없음");
            Qa.Near(snapshot.Layout.Width, SvgNumber(svg.Attribute("width")?.Value, "SVG width"),
                "괄호 SVG width " + mode, SvgTolerance);
            Qa.Near(snapshot.Layout.Height, SvgNumber(svg.Attribute("height")?.Value, "SVG height"),
                "괄호 SVG height " + mode, SvgTolerance);
            var viewBox = (svg.Attribute("viewBox")?.Value ?? throw new InvalidOperationException("괄호 SVG viewBox 없음"))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value,
                    System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Qa.Equal(4, viewBox.Length, "괄호 SVG viewBox 구성 " + mode);
            Qa.Near(snapshot.Layout.Width, viewBox[2], "괄호 SVG viewBox 폭 " + mode, SvgTolerance);
            Qa.Near(snapshot.Layout.Height, viewBox[3], "괄호 SVG viewBox 높이 " + mode, SvgTolerance);
            var expectedGlyphs = snapshot.Layout.Primitives.OfType<GlyphPrimitive>().ToArray();
            var actualGlyphs = svg.Descendants().Where(element => element.Name.LocalName == "text").ToArray();
            Qa.Equal(expectedGlyphs.Length, actualGlyphs.Length, "괄호 SVG glyph 수 " + mode);
            Qa.Equal(string.Concat(expectedGlyphs.Select(glyph => glyph.Text)),
                string.Concat(actualGlyphs.Select(element => element.Value)), "괄호 SVG 원문·순서 " + mode);
            for (var index = 0; index < expectedGlyphs.Length; index++)
            {
                Qa.Near(expectedGlyphs[index].BaselineOrigin.X,
                    SvgNumber(actualGlyphs[index].Attribute("x")?.Value, "text x"), "괄호 SVG glyph x " + index + " " + mode, SvgTolerance);
                Qa.Near(expectedGlyphs[index].BaselineOrigin.Y,
                    SvgNumber(actualGlyphs[index].Attribute("y")?.Value, "text y"), "괄호 SVG glyph y " + index + " " + mode, SvgTolerance);
            }
            Qa.Equal(8, svg.Descendants().Count(element => element.Name.LocalName == "polyline"),
                "실제 SVG delimiter shape 수 " + mode);
        }
        AssertUnchanged(before, session, "괄호 frozen preview/SVG 원본 Session");

        var emptyContent = Qa.Text("");
        var incompleteSession = new EditingSession(Workspace(
            new BracketsNode(Guid.NewGuid(), MathBracketKind.Curly, emptyContent)));
        var incompletePreview = new MathOutputPreviewViewModel(incompleteSession, metrics, new SvgExportService());
        Qa.Check(incompletePreview.RefreshPreview(), "필수 빈 괄호 content preview 계산");
        Qa.Equal(MathOutputPreviewState.Incomplete, incompletePreview.State, "필수 빈 괄호 content preview 상태");
        Qa.Check(!incompletePreview.CanExportSvg, "필수 빈 괄호 content SVG 저장 허용");
        Qa.Check(incompletePreview.PreviewSnapshot!.Layout.Diagnostics.Any(diagnostic =>
                diagnostic.Code == LayoutDiagnosticCode.EmptySlot && diagnostic.NodeId == emptyContent.Id),
            "필수 빈 괄호 content layout diagnostic");
        var rejectedPath = directory.File("incomplete-bracket.svg");
        var rejected = await incompletePreview.ExportSvgToPathAsync(rejectedPath);
        Qa.Equal(SvgPreviewExportStatus.Incomplete, rejected.Status, "필수 빈 괄호 content SVG 거절");
        Qa.Check(!File.Exists(rejectedPath), "미완성 괄호 SVG 파일 생성");
    }

    private static void AssertUnsupportedTarget(BracketFixture fixture, MathPropertyScope scope,
        Action<EditingSession> prepare, string label)
    {
        var session = new EditingSession(Workspace(fixture.Bracket));
        prepare(session);
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            label + " target capture");
        var before = CaptureState(session);
        var result = session.ApplyMathProperties(target!,
            new MathPropertiesPatch(new MathStylePatch(FontSize: 42, FontFamily: "Must Not Apply", Foreground: "#123456"),
                MathTypesettingStyle.Script, BracketKind: MathBracketKind.Square));
        Qa.Equal(MathPropertyChangeStatus.UnsupportedScope, result.Status, label + " kind unsupported");
        AssertUnchanged(before, session, label + " unsupported state");
    }

    private static void AssertBracketContentAndSelection(EditingSession session, BracketFixture fixture,
        string contentFingerprint, MathBracketKind expectedKind, string label)
    {
        var brackets = Qa.Node(session, fixture.Bracket.Id) as BracketsNode
            ?? throw new InvalidOperationException(label + "에서 괄호 node가 사라짐");
        Qa.Equal(expectedKind, brackets.Kind, label + " kind");
        Qa.Equal(contentFingerprint, Qa.NodeFingerprint(brackets.Content), label + " content subtree ID/Role/style");
        Qa.Equal(fixture.Bracket.Id, session.SelectedNodeId, label + " 선택 ID");
        Qa.Check(session.IsNodeSelected, label + " 전체 노드 선택 상태");
    }

    private static BracketFixture CreateBracket(MathBracketKind kind, string text,
        MathStyleOverride? bracketStyle = null, MathTextRole contentRole = MathTextRole.Automatic)
    {
        if (text.Length < 2)
            throw new ArgumentException("괄호 fixture는 비어 있지 않은 두 Text 조각을 만들 수 있어야 합니다.", nameof(text));

        var left = Qa.Text(text[..(text.Length / 2)]) with
        {
            Role = contentRole,
            StyleOverride = new MathStyleOverride(FontSize: 19, FontFamily: "Content Face", Foreground: "#775566")
            {
                TypesettingStyle = MathTypesettingStyle.Script
            }
        };
        var right = Qa.Text(text[(text.Length / 2)..]) with { Role = MathTextRole.Identifier };
        var content = Qa.Sequence(left, right);
        var bracket = new BracketsNode(Guid.NewGuid(), kind, content) { StyleOverride = bracketStyle };
        return new BracketFixture(bracket, content, left, right);
    }

    private static string NodeShapeFingerprint(MathNode node) => node switch
    {
        TextNode text => $"Text|{text.Text}|{text.Role}|{text.IsEmptySlot}|{text.StyleOverride}",
        SequenceNode sequence => $"Sequence|{sequence.StyleOverride}|" +
                                string.Join(";", sequence.Children.Select(NodeShapeFingerprint)),
        BracketsNode brackets => $"Brackets|{brackets.Kind}|{brackets.StyleOverride}|" +
                                 NodeShapeFingerprint(brackets.Content),
        _ => throw new InvalidOperationException("복제 content fixture에 예상하지 못한 node 형식이 있습니다: " + node.GetType().Name)
    };

    private static EquationWorkspace Workspace(params MathNode[] roots)
    {
        var group = new EquationGroup(Guid.NewGuid(),
            [.. roots.Select(root => new EquationRow(Guid.NewGuid(), root))], AlignmentMode.None, 18)
        {
            StyleOverride = new MathStyleOverride(FontFamily: "Group Face")
            {
                TypesettingStyle = MathTypesettingStyle.Display
            }
        };
        return new EquationWorkspace(Guid.NewGuid(), 1, "괄호 종류 QA", [group],
            new MathStyle(30, "Deterministic", "#24364A") { TypesettingStyle = MathTypesettingStyle.Display });
    }

    private static MathPropertyTargetSnapshot Capture(EditingSession session, MathPropertyScope scope)
    {
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            "괄호 속성 대상 캡처 실패: " + scope);
        return target!;
    }

    private static JsonObject FindBracketJson(JsonNode root)
    {
        if (root is JsonObject obj)
        {
            if (obj["$kind"]?.GetValue<string>() == "brackets")
                return obj;
            foreach (var property in obj.ToArray())
                if (property.Value is not null && TryFindBracketJson(property.Value) is { } found)
                    return found;
        }
        else if (root is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null && TryFindBracketJson(item) is { } found)
                    return found;
        }
        throw new InvalidOperationException("저장 JSON에서 괄호 node를 찾지 못함");
    }

    private static JsonObject? TryFindBracketJson(JsonNode node)
    {
        try { return FindBracketJson(node); }
        catch (InvalidOperationException) { return null; }
    }

    private static double SvgNumber(string? value, string label) => double.Parse(
        value ?? throw new InvalidOperationException(label + " 값 누락"),
        System.Globalization.CultureInfo.InvariantCulture);

    private static SessionState CaptureState(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), session.Revision, snapshot.StateId,
            session.IsDirty, session.CanUndo, session.CanRedo, session.Caret, session.Selection,
            session.SelectedNodeId, session.IsNodeSelected);
    }

    private static void AssertUnchanged(SessionState before, EditingSession session, string label)
    {
        var after = CaptureState(session);
        Qa.Equal(before.Fingerprint, after.Fingerprint, label + " workspace");
        Qa.Equal(before.Revision, after.Revision, label + " revision");
        Qa.Equal(before.StateId, after.StateId, label + " StateId");
        Qa.Equal(before.IsDirty, after.IsDirty, label + " dirty");
        Qa.Equal(before.CanUndo, after.CanUndo, label + " CanUndo");
        Qa.Equal(before.CanRedo, after.CanRedo, label + " CanRedo");
        Qa.Equal(before.Caret, after.Caret, label + " caret");
        Qa.Equal(before.Selection, after.Selection, label + " selection");
        Qa.Equal(before.SelectedNodeId, after.SelectedNodeId, label + " selected ID");
        Qa.Equal(before.IsNodeSelected, after.IsNodeSelected, label + " whole-node selection");
    }

    private static void AssertUndoRedo(EditingSession session, SessionState before, SessionState after, string label)
    {
        session.Undo();
        var undone = CaptureState(session);
        Qa.Equal(before.Fingerprint, undone.Fingerprint, label + " undo workspace");
        Qa.Equal(before.StateId, undone.StateId, label + " undo saved StateId");
        Qa.Equal(before.Caret, undone.Caret, label + " undo caret");
        Qa.Equal(before.Selection, undone.Selection, label + " undo selection");
        Qa.Equal(before.SelectedNodeId, undone.SelectedNodeId, label + " undo selected ID");
        Qa.Equal(before.IsNodeSelected, undone.IsNodeSelected, label + " undo whole-node selection");
        Qa.Equal(before.IsDirty, undone.IsDirty, label + " undo dirty");
        Qa.Equal(after.Revision + 1, undone.Revision, label + " undo revision");
        Qa.Check(undone.CanRedo, label + " undo 뒤 redo 없음");
        session.Redo();
        var redone = CaptureState(session);
        Qa.Equal(after.Fingerprint, redone.Fingerprint, label + " redo workspace");
        Qa.Equal(after.StateId, redone.StateId, label + " redo StateId");
        Qa.Equal(after.Caret, redone.Caret, label + " redo caret");
        Qa.Equal(after.Selection, redone.Selection, label + " redo selection");
        Qa.Equal(after.SelectedNodeId, redone.SelectedNodeId, label + " redo selected ID");
        Qa.Equal(after.IsNodeSelected, redone.IsNodeSelected, label + " redo whole-node selection");
        Qa.Equal(after.IsDirty, redone.IsDirty, label + " redo dirty");
        Qa.Equal(after.Revision + 2, redone.Revision, label + " redo revision");
        Qa.Check(redone.CanUndo && !redone.CanRedo, label + " redo history");
    }

    private sealed record BracketFixture(BracketsNode Bracket, SequenceNode Content, TextNode ContentA,
        TextNode ContentB);

    private sealed record SessionState(string Fingerprint, long Revision, Guid StateId, bool IsDirty,
        bool CanUndo, bool CanRedo, MathCaret Caret, MathSelection? Selection, Guid? SelectedNodeId,
        bool IsNodeSelected);

    private sealed class DeterministicMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.53 : 0.5);
            var carets = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                carets.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.23,
                carets.MoveToImmutable());
        }
    }
}
