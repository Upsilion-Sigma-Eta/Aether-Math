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

internal static class ThinMathSpaceCases
{
    private const double SvgTolerance = 0.00051;

    public static IEnumerable<QaCase> All()
    {
        yield return new("THIN-JSON-VALIDATOR", "얇은 수학 공백 JSON 왕복과 필수 콘텐츠 검증", JsonAndRequiredContent);
        yield return new("THIN-INSERT-SEMANTICS", "텍스트 caret·정역 선택 삽입과 독립 편집 이력", InsertSemantics);
        yield return new("THIN-ANCHOR-SELECTION", "빈 슬롯·전체 치환·끝 caret anchor 보존", AnchorAndSelectionBoundaries);
        yield return new("THIN-DELETE-NAVIGATION", "spacer 단일 삭제와 인접 텍스트 편집 경계", DeleteAndCaretBoundaries);
        yield return new("THIN-CLEAR-SEMANTIC-EMPTY", "의미상 빈 spacer의 네 Clear 범위 처리", ClearSemanticEmptySpace);
        yield return new("THIN-LAYOUT-GAPS", "명시 간격 폭과 인접 자동 간격의 분리", LayoutWidthAndSequenceGaps);
        yield return new("THIN-LATEX-OUTPUT", "명시 공백 LaTeX와 U+2009 원문 분리", LatexOutputAndRawUnicode);
        yield return new("THIN-PREVIEW-SVG", "세 출력 모드 frozen layout과 실제 SVG 소비", PreviewAndSvgConsumer);
        yield return new("THIN-CLONE-IO-RECOVERY", "다른 행 caret의 복제·파일·복구 왕복", CloneAndPersistenceRoundTrip);
    }

    private static async Task JsonAndRequiredContent()
    {
        using var directory = new TempDirectory();
        var files = new WorkspaceFileService();
        var anchor = Qa.Text(string.Empty, true);
        var spacer = Space();
        var raw = Qa.Text("앞\u2009뒤");
        var workspace = Qa.Workspace(Qa.Sequence(anchor, spacer, raw));
        var path = directory.File("thin-space.aethermath");
        var session = new EditingSession(workspace);
        await files.SaveAsync(path, session.CaptureSnapshot());
        var jsonText = await File.ReadAllTextAsync(path);
        var json = JsonNode.Parse(jsonText) ?? throw new InvalidOperationException("저장 JSON이 비어 있음");
        var jsonSpace = FindSpaceJson(json);
        Qa.Equal("mathSpace", jsonSpace["$kind"]?.GetValue<string>(), "새 node discriminator");
        Qa.Equal((int)spacer.Kind, jsonSpace["kind"]!.GetValue<int>(), "Thin enum JSON 값");

        var reopened = await files.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(reopened), "명시 spacer와 원문 U+2009 파일 왕복");
        var savedNodes = Qa.Nodes(reopened).ToArray();
        Qa.Equal(spacer.Id, savedNodes.OfType<MathSpaceNode>().Single().Id, "spacer ID 파일 보존");
        Qa.Equal("앞\u2009뒤", savedNodes.OfType<TextNode>().Single(node => node.Id == raw.Id).Text,
            "원문 U+2009 텍스트 보존");

        await Reject("invalid-discriminator", Mutate(jsonText, node => FindSpaceJson(node)["$kind"] = "unrecognized-space"));
        await Reject("invalid-kind", Mutate(jsonText, node => FindSpaceJson(node)["kind"] = 99));
        await Reject("unknown-field", Mutate(jsonText, node => FindSpaceJson(node)["future"] = true));
        await Reject("duplicate-id", Mutate(jsonText, node =>
        {
            var spaceJson = FindSpaceJson(node);
            spaceJson["id"] = anchor.Id.ToString();
        }));

        var bareSpace = JsonNode.Parse(jsonText)!;
        var bareRow = FindRowJson(bareSpace);
        bareRow["root"] = FindSpaceJson(bareSpace).DeepClone();
        await Reject("bare-required-root", bareSpace.ToJsonString());

        var onlySpacer = JsonNode.Parse(jsonText)!;
        var onlyRow = FindRowJson(onlySpacer);
        var onlySpaceJson = FindSpaceJson(onlySpacer).DeepClone();
        onlyRow["root"] = new JsonObject
        {
            ["$kind"] = "sequence",
            ["id"] = Guid.NewGuid().ToString(),
            ["children"] = new JsonArray(onlySpaceJson)
        };
        await Reject("spacer-only-required-sequence", onlySpacer.ToJsonString());

        var bareFractionSlot = JsonNode.Parse(jsonText)!;
        var bareFractionSpace = FindSpaceJson(bareFractionSlot).DeepClone();
        var bareFractionText = FindTextJson(bareFractionSlot, raw.Id).DeepClone();
        FindRowJson(bareFractionSlot)["root"] = FractionJson(bareFractionSpace, bareFractionText);
        await Reject("bare-required-fraction-numerator", bareFractionSlot.ToJsonString());

        var spacerOnlyFractionSlot = JsonNode.Parse(jsonText)!;
        var spacerOnlyFractionSpace = FindSpaceJson(spacerOnlyFractionSlot).DeepClone();
        var spacerOnlyFractionText = FindTextJson(spacerOnlyFractionSlot, raw.Id).DeepClone();
        FindRowJson(spacerOnlyFractionSlot)["root"] = FractionJson(
            new JsonObject
            {
                ["$kind"] = "sequence",
                ["id"] = Guid.NewGuid().ToString(),
                ["children"] = new JsonArray(spacerOnlyFractionSpace)
            }, spacerOnlyFractionText);
        await Reject("spacer-only-required-fraction-numerator", spacerOnlyFractionSlot.ToJsonString());

        var markedFractionSlot = JsonNode.Parse(jsonText)!;
        var markedFractionSpace = FindSpaceJson(markedFractionSlot).DeepClone();
        var markedFractionAnchor = FindTextJson(markedFractionSlot, anchor.Id).DeepClone();
        var markedFractionText = FindTextJson(markedFractionSlot, raw.Id).DeepClone();
        FindRowJson(markedFractionSlot)["root"] = FractionJson(
            new JsonObject
            {
                ["$kind"] = "sequence",
                ["id"] = Guid.NewGuid().ToString(),
                ["children"] = new JsonArray(markedFractionAnchor, markedFractionSpace)
            }, markedFractionText);
        var markedFractionPath = directory.File("marked-empty-fraction-space.aethermath");
        await File.WriteAllTextAsync(markedFractionPath, markedFractionSlot.ToJsonString());
        var markedFractionWorkspace = await files.OpenAsync(markedFractionPath);
        var markedFractionSession = new EditingSession(markedFractionWorkspace);
        var markedFractionLayout = MathLayoutEngine.Layout(markedFractionWorkspace, new FixedMetrics());
        Qa.Check(markedFractionLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == anchor.Id),
            "분자 marker+space에서 empty-slot diagnostic 누락");
        Qa.Check(LatexSerializer.GetIncompleteSlotNodeIds(markedFractionWorkspace).Contains(anchor.Id),
            "분자 marker+space에서 LaTeX incomplete node 누락");
        var markedFractionPreview = new MathOutputPreviewViewModel(
            markedFractionSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(markedFractionPreview.RefreshPreview(), "분자 marker+space preview 진단 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, markedFractionPreview.State,
            "분자 marker+space preview incomplete 상태 누락");
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(markedFractionLayout),
            "분자 marker+space의 실제 SVG 저장이 거절되지 않음");
        var latexVm = new MainWindowViewModel(files, new SvgExportService());
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await latexVm.OpenFromPathAsync(markedFractionPath), "분자 marker+space 작업공간을 root VM으로 열기");
        Qa.Check(!latexVm.CanExportLatex, "분자 marker+space가 root LaTeX 재사용 차단을 해제함");

        var incompleteLayout = MathLayoutEngine.Layout(workspace, new FixedMetrics());
        Qa.Check(incompleteLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == anchor.Id),
            "marked-empty anchor와 spacer가 empty-slot 진단을 유지하지 않음");
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incompleteLayout),
            "marked-empty anchor가 포함된 SVG 저장 거절");

        async Task Reject(string name, string contents)
        {
            var invalidPath = directory.File(name + ".aethermath");
            await File.WriteAllTextAsync(invalidPath, contents);
            var before = await File.ReadAllBytesAsync(invalidPath);
            await Qa.FailsAsync(async () => await files.OpenAsync(invalidPath),
                name + " 입력 거절", typeof(JsonException), typeof(InvalidDataException));
            var afterBytes = await File.ReadAllBytesAsync(invalidPath);
            Qa.Check(before.SequenceEqual(afterBytes), name + " 거절 뒤 원본 바이트 보존");
        }

        static JsonObject FractionJson(JsonNode numerator, JsonNode denominator) => new()
        {
            ["$kind"] = "fraction",
            ["id"] = Guid.NewGuid().ToString(),
            ["numerator"] = numerator,
            ["denominator"] = denominator
        };
    }

    private static void InsertSemantics()
    {
        var source = Qa.Text("A👩‍🔬e\u0301Z") with
        {
            Role = MathTextRole.Identifier,
            StyleOverride = new MathStyleOverride(FontSize: 23, Foreground: "#80FF0000")
        };
        var tail = Qa.Text("tail");
        var root = Qa.Sequence(source, tail);
        var session = new EditingSession(Qa.Workspace(root));
        Qa.Check(session.TrySetSelection(new MathSelection(
            new MathCaret(source.Id, 3), new MathCaret(source.Id, 1))), "역방향 Unicode grapheme 선택");
        session.MarkSaved(session.CaptureSnapshot());
        var before = CaptureState(session);

        var insertedId = session.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(insertedId.HasValue, "역방향 선택 구간에 spacer 삽입");
        var afterNodes = ((SequenceNode)session.Workspace.Groups[0].Rows[0].Root).Children;
        Qa.Equal(4, afterNodes.Length, "prefix·spacer·suffix·이웃 구조 수");
        var prefix = (TextNode)afterNodes[0];
        var inserted = (MathSpaceNode)afterNodes[1];
        var suffix = (TextNode)afterNodes[2];
        Qa.Equal(source.Id, prefix.Id, "prefix가 원본 Text ID를 우선 보존");
        Qa.Equal("A", prefix.Text, "역방향 grapheme 선택 앞 텍스트");
        Qa.Equal(insertedId, inserted.Id, "반환된 ID가 삽입 spacer ID");
        Qa.Equal("Z", suffix.Text, "역방향 grapheme 선택 뒤 텍스트");
        Qa.Check(suffix.Id != source.Id, "prefix와 suffix가 원본 ID를 중복 사용");
        Qa.Equal(source.Role, prefix.Role, "prefix Role 보존");
        Qa.Equal(source.Role, suffix.Role, "suffix Role 보존");
        Qa.Equal(source.StyleOverride, prefix.StyleOverride, "prefix 명시 스타일 보존");
        Qa.Equal(source.StyleOverride, suffix.StyleOverride, "suffix 명시 스타일 보존");
        Qa.Equal(source.StyleOverride, inserted.StyleOverride, "spacer의 원본 명시 스타일 보존");
        Qa.Equal(tail.Id, afterNodes[3].Id, "이웃 노드 ID 보존");
        Qa.Equal(before.Revision + 1, session.Revision, "삽입 요청 하나가 단일 commit");
        Qa.Check(session.CanUndo && !session.CanRedo && session.IsDirty, "삽입 뒤 이력 상태");
        Qa.Equal(suffix.Id, session.Caret.NodeId, "삽입 caret이 유효한 suffix Text anchor");
        Qa.Equal(0, session.Caret.TextElementOffset, "suffix anchor caret 위치");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "삽입 뒤 선택 해제");

        var insertedFingerprint = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), "한 번 Undo가 첫 요청만 완전히 되돌림");
        Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, "삽입 Undo가 저장 StateId 복원");
        Qa.Equal(before.Caret, session.Caret, "삽입 Undo caret 복원");
        Qa.Equal(before.Selection, session.Selection, "삽입 Undo 선택 복원");
        session.Redo();
        Qa.Equal(insertedFingerprint, Qa.Fingerprint(session.Workspace), "삽입 Redo 구조 복원");

        var afterOneRequest = CaptureState(session);
        var secondSpaceId = session.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(secondSpaceId.HasValue && secondSpaceId != insertedId, "반복 요청이 별도 spacer ID 생성");
        Qa.Equal(afterOneRequest.Revision + 1, session.Revision, "두 번째 요청이 독립 commit");
        var twoRequestFingerprint = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(insertedFingerprint, Qa.Fingerprint(session.Workspace), "Undo 한 번이 두 번째 요청만 되돌림");
        session.Redo();
        Qa.Equal(twoRequestFingerprint, Qa.Fingerprint(session.Workspace), "두 번째 요청 Redo");

        var invalidBefore = CaptureState(session);
        Qa.Throws<ArgumentOutOfRangeException>(() => session.InsertMathSpace((MathSpaceKind)int.MaxValue),
            "정의되지 않은 spacer 종류 거절");
        AssertUnchanged(invalidBefore, session, "잘못된 spacer kind의 원자 거절");

        var fullTextSelection = new EditingSession(Qa.Workspace(Qa.Text("text")));
        var fullText = (TextNode)fullTextSelection.Workspace.Groups[0].Rows[0].Root;
        Qa.Check(fullTextSelection.TrySelectNode(fullText.Id), "TextNode 전체 선택 준비");
        var fullTextBefore = CaptureState(fullTextSelection);
        Qa.Check(!fullTextSelection.CanInsertMathSpace, "전체 TextNode 구조 선택이 삽입 가능으로 표시됨");
        Qa.Equal<Guid?>(null, fullTextSelection.InsertMathSpace(MathSpaceKind.Thin), "전체 TextNode 구조 선택의 삽입 거절");
        AssertUnchanged(fullTextBefore, fullTextSelection, "전체 TextNode 구조 선택의 무변경");

        var structuralSelection = new EditingSession(Qa.Workspace(new FractionNode(Guid.NewGuid(), Qa.Text("n"), Qa.Text("d"))));
        var structural = structuralSelection.Workspace.Groups[0].Rows[0].Root;
        Qa.Check(structuralSelection.TrySelectNode(structural.Id), "구조 전체 선택 준비");
        var structuralBefore = CaptureState(structuralSelection);
        Qa.Check(!structuralSelection.CanInsertMathSpace, "구조 선택이 삽입 가능으로 표시됨");
        Qa.Equal<Guid?>(null, structuralSelection.InsertMathSpace(MathSpaceKind.Thin), "구조 선택 삽입 거절");
        AssertUnchanged(structuralBefore, structuralSelection, "구조 선택의 spacer 삽입 무변경");
    }

    private static async Task AnchorAndSelectionBoundaries()
    {
        using var directory = new TempDirectory();
        var files = new WorkspaceFileService();
        var empty = Qa.Text(string.Empty, true) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 26, FontFamily: "Anchor Face")
        };
        var emptySession = new EditingSession(Qa.Workspace(empty));
        Qa.Caret(emptySession, empty.Id, 0);
        var emptySpaceId = emptySession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(emptySpaceId.HasValue, "필수 빈 슬롯에 spacer 삽입");
        var emptyChildren = ((SequenceNode)emptySession.Workspace.Groups[0].Rows[0].Root).Children;
        Qa.Equal(2, emptyChildren.Length, "빈 슬롯 spacer와 anchor 수");
        var retainedEmpty = (TextNode)emptyChildren.Single(node => node is TextNode);
        Qa.Equal(empty.Id, retainedEmpty.Id, "빈 슬롯 원래 Text ID 보존");
        Qa.Check(retainedEmpty.IsEmptySlot && retainedEmpty.Text.Length == 0, "빈 슬롯 marked-empty anchor 보존");
        Qa.Equal(MathTextRole.Condition, retainedEmpty.Role, "빈 슬롯 역할 보존");
        Qa.Equal(empty.StyleOverride, retainedEmpty.StyleOverride, "빈 슬롯 스타일 보존");
        Qa.Equal(retainedEmpty.Id, emptySession.Caret.NodeId, "빈 슬롯 삽입 후 caret anchor");
        var repeatedRequiredSpaceId = emptySession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(repeatedRequiredSpaceId.HasValue && repeatedRequiredSpaceId != emptySpaceId,
            "필수 빈 슬롯의 반복 삽입이 별도 spacer를 생성");
        Qa.Equal(2, Qa.Nodes(emptySession.Workspace).OfType<MathSpaceNode>().Count(),
            "필수 빈 슬롯의 반복 spacer 수");
        Qa.Check(Qa.Leaf(emptySession, retainedEmpty.Id).IsEmptySlot,
            "필수 빈 슬롯 반복 삽입이 marked-empty 상태를 해제함");
        Qa.Check(MathLayoutEngine.Layout(emptySession.Workspace, new FixedMetrics()).Diagnostics
                .Any(diagnostic => diagnostic.NodeId == retainedEmpty.Id),
            "필수 빈 슬롯 반복 삽입 뒤 empty 진단이 사라짐");
        var repeatedRequiredPreview = new MathOutputPreviewViewModel(
            emptySession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(repeatedRequiredPreview.RefreshPreview(), "필수 빈 슬롯 반복 spacer preview 진단 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, repeatedRequiredPreview.State,
            "필수 빈 슬롯 반복 spacer가 preview incomplete 상태를 잃음");
        Qa.Check(!repeatedRequiredPreview.CanExportSvg,
            "필수 빈 슬롯 반복 spacer에서 SVG 내보내기가 가능해짐");
        Qa.Check(emptySession.InsertText("x"), "빈 슬롯 spacer 뒤 문자 입력");
        var typed = Qa.Leaf(emptySession, retainedEmpty.Id);
        Qa.Equal("x", typed.Text, "빈 슬롯 입력 원문");
        Qa.Equal(MathTextRole.Condition, typed.Role, "입력 후 역할 보존");
        Qa.Equal(empty.StyleOverride, typed.StyleOverride, "입력 후 명시 스타일 보존");
        Qa.Check(emptySession.Backspace(), "삽입 문자 삭제");
        Qa.Check(Qa.Leaf(emptySession, retainedEmpty.Id).IsEmptySlot, "삭제 뒤 anchor가 marked-empty 상태로 복귀");
        var emptyPath = directory.File("empty-anchor.aethermath");
        await files.SaveAsync(emptyPath, emptySession.CaptureSnapshot());
        Qa.Equal(Qa.Fingerprint(emptySession.Workspace), Qa.Fingerprint(await files.OpenAsync(emptyPath)),
            "빈 슬롯 spacer anchor 실제 파일 왕복");
        var emptyLayout = MathLayoutEngine.Layout(emptySession.Workspace, new FixedMetrics());
        Qa.Check(emptyLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == retainedEmpty.Id),
            "반복 입력/삭제 뒤 required empty 진단 유지");

        foreach (var offset in new[] { 0, 1, 2 })
        {
            var source = Qa.Text("ab") with { Role = MathTextRole.Unit };
            var session = new EditingSession(Qa.Workspace(source));
            Qa.Caret(session, source.Id, offset);
            var id = session.InsertMathSpace(MathSpaceKind.Thin);
            Qa.Check(id.HasValue, $"위치 {offset}의 spacer 삽입");
            var sequence = (SequenceNode)session.Workspace.Groups[0].Rows[0].Root;
            var anchorNodes = sequence.Children.OfType<TextNode>().Where(node => node.Text.Length == 0).ToArray();
            if (offset == 0)
            {
                Qa.Equal(source.Id, sequence.Children.OfType<TextNode>().Single(node => node.Text == "ab").Id,
                    "선두 삽입에서 기존 suffix ID 보존");
            }
            if (offset == 1)
            {
                Qa.Equal(source.Id, sequence.Children.OfType<TextNode>().Single(node => node.Text == "a").Id,
                    "중간 삽입에서 기존 prefix ID 보존");
                Qa.Equal("b", sequence.Children.OfType<TextNode>().Single(node => node.Text == "b").Text,
                    "중간 삽입 suffix 원문 보존");
            }
            if (offset == 2)
            {
                Qa.Equal(source.Id, sequence.Children.OfType<TextNode>().Single(node => node.Text == "ab").Id,
                    "끝 삽입에서 기존 prefix ID 보존");
                Qa.Equal(1, anchorNodes.Length, "끝 삽입은 단일 caret anchor 생성");
                Qa.Check(!anchorNodes[0].IsEmptySlot, "완성 식 끝의 생성 caret anchor가 marked-empty");
                var completeLayout = MathLayoutEngine.Layout(session.Workspace, new FixedMetrics());
                Qa.Check(!completeLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == anchorNodes[0].Id),
                    "완성 식 끝 caret anchor가 required empty 진단을 만듦");
                var completePreview = new MathOutputPreviewViewModel(session, new FixedMetrics(), new RecordingSvgExporter());
                Qa.Check(completePreview.RefreshPreview(), "완성 식 끝 spacer preview 갱신");
                Qa.Equal(MathOutputPreviewState.Ready, completePreview.State,
                    "완성 식 끝 spacer가 preview를 incomplete로 바꿈");
                Qa.Caret(session, source.Id, 0);
                Qa.Select(session, source.Id, 0, 2);
                Qa.Check(session.DeleteSelection(), "완성 식 본문을 지워 꼬리 anchor 위험 검증");
                var afterDelete = Qa.Fingerprint(session.Workspace);
                await files.SaveAsync(directory.File($"deleted-tail-{offset}.aethermath"), session.CaptureSnapshot());
                Qa.Equal(afterDelete, Qa.Fingerprint(await files.OpenAsync(directory.File($"deleted-tail-{offset}.aethermath"))),
                    "본문 전체 삭제 뒤 끝 anchor와 empty space의 유효한 파일 왕복");
                _ = MathLayoutEngine.Layout(session.Workspace, new FixedMetrics());
                var preview = new MathOutputPreviewViewModel(session, new FixedMetrics(), new RecordingSvgExporter());
                Qa.Check(preview.RefreshPreview(), "삭제 뒤 empty-anchor preview 진단 생성");
                Qa.Equal(MathOutputPreviewState.Incomplete, preview.State, "required empty spacer가 완성 상태로 바뀌지 않음");
            }
            else
            {
                await files.SaveAsync(directory.File($"insert-{offset}.aethermath"), session.CaptureSnapshot());
                _ = await files.OpenAsync(directory.File($"insert-{offset}.aethermath"));
            }
            _ = MathLayoutEngine.Layout(session.Workspace, new FixedMetrics());
        }

        var completedText = Qa.Text("a") with { Role = MathTextRole.Identifier };
        var completedSession = new EditingSession(Qa.Workspace(completedText));
        Qa.Caret(completedSession, completedText.Id, 1);
        var firstTrailingSpaceId = completedSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(firstTrailingSpaceId.HasValue, "완성 문자 끝에 첫 공백 삽입");
        var completedAnchorId = completedSession.Caret.NodeId;
        Qa.Check(!Qa.Leaf(completedSession, completedAnchorId).IsEmptySlot,
            "완성 문자 끝의 자동 caret anchor가 marked-empty");
        var secondTrailingSpaceId = completedSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(secondTrailingSpaceId.HasValue && secondTrailingSpaceId != firstTrailingSpaceId,
            "완성 문자 끝 반복 삽입이 별도 spacer를 생성");
        Qa.Check(!Qa.Leaf(completedSession, completedAnchorId).IsEmptySlot,
            "완성 문자 끝 반복 삽입이 자동 anchor를 marked-empty로 바꿈");
        var completedLayout = MathLayoutEngine.Layout(completedSession.Workspace, new FixedMetrics());
        Qa.Check(!completedLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == completedAnchorId),
            "완성 문자 끝 반복 삽입이 empty-slot 진단을 생성");
        var completedPreview = new MathOutputPreviewViewModel(completedSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(completedPreview.RefreshPreview(), "완성 문자 끝 반복 삽입 preview 갱신");
        Qa.Equal(MathOutputPreviewState.Ready, completedPreview.State,
            "완성 문자 끝 반복 삽입이 preview를 incomplete로 바꿈");
        _ = new SvgExportService().ToSvg(completedPreview.PreviewSnapshot!.Layout);
        var completedPath = directory.File("completed-repeat-spaces.aethermath");
        await files.SaveAsync(completedPath, completedSession.CaptureSnapshot());
        Qa.Equal(Qa.Fingerprint(completedSession.Workspace), Qa.Fingerprint(await files.OpenAsync(completedPath)),
            "완성 문자 끝 반복 spacer의 파일 왕복");

        var deleteSpaceText = Qa.Text("a") with
        {
            Role = MathTextRole.Unit,
            StyleOverride = new MathStyleOverride(FontSize: 22, Foreground: "#80FF0000")
        };
        var deleteSpaceSession = new EditingSession(Qa.Workspace(deleteSpaceText));
        Qa.Caret(deleteSpaceSession, deleteSpaceText.Id, 1);
        var removableSpaceId = deleteSpaceSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(removableSpaceId.HasValue, "spacer 선삭제 회귀용 공백 삽입");
        var removableAnchorId = deleteSpaceSession.Caret.NodeId;
        Qa.Check(!Qa.Leaf(deleteSpaceSession, removableAnchorId).IsEmptySlot,
            "spacer 선삭제 전 후행 caret anchor가 marked-empty");
        Qa.Check(deleteSpaceSession.Backspace(), "앞 문자 삭제 전 spacer를 먼저 삭제");
        Qa.Check(Qa.Nodes(deleteSpaceSession.Workspace).All(node => node.Id != removableSpaceId),
            "선행 spacer 삭제가 해당 spacer를 남김");
        Qa.Check(!Qa.Leaf(deleteSpaceSession, removableAnchorId).IsEmptySlot,
            "spacer 삭제만으로 suffix anchor가 required marker로 바뀜");
        var beforeDeleteLastCharacter = CaptureState(deleteSpaceSession);
        Qa.Select(deleteSpaceSession, deleteSpaceText.Id, 0, 1);
        Qa.Check(deleteSpaceSession.DeleteSelection(), "spacer 선삭제 뒤 남은 앞 문자를 삭제");
        var emptyPrefix = Qa.Leaf(deleteSpaceSession, deleteSpaceText.Id);
        var normalizedSuffix = Qa.Leaf(deleteSpaceSession, removableAnchorId);
        Qa.Check(emptyPrefix.IsEmptySlot && normalizedSuffix.IsEmptySlot,
            "direct Sequence가 비었는데 unmarked empty anchor를 정규화하지 않음");
        Qa.Equal(deleteSpaceText.Role, normalizedSuffix.Role, "empty anchor 정규화 뒤 suffix Role 보존");
        Qa.Equal(deleteSpaceText.StyleOverride, normalizedSuffix.StyleOverride,
            "empty anchor 정규화 뒤 suffix style 보존");
        var normalizedState = CaptureState(deleteSpaceSession);
        deleteSpaceSession.Undo();
        Qa.Equal(beforeDeleteLastCharacter.Fingerprint, Qa.Fingerprint(deleteSpaceSession.Workspace),
            "empty anchor 정규화 Undo가 문서 상태를 복원");
        deleteSpaceSession.Redo();
        Qa.Equal(normalizedState.Fingerprint, Qa.Fingerprint(deleteSpaceSession.Workspace),
            "empty anchor 정규화 Redo가 문서 상태를 복원");
        var normalizedPath = directory.File("spacer-first-then-delete-text.aethermath");
        await files.SaveAsync(normalizedPath, deleteSpaceSession.CaptureSnapshot());
        Qa.Equal(normalizedState.Fingerprint, Qa.Fingerprint(await files.OpenAsync(normalizedPath)),
            "spacer 선삭제 뒤 마지막 문자 삭제 파일 왕복");
        var normalizedLayout = MathLayoutEngine.Layout(deleteSpaceSession.Workspace, new FixedMetrics());
        Qa.Check(normalizedLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == removableAnchorId),
            "정규화된 suffix empty 진단이 유지되지 않음");
        var normalizedPreview = new MathOutputPreviewViewModel(deleteSpaceSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(normalizedPreview.RefreshPreview(), "정규화된 empty anchor preview 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, normalizedPreview.State,
            "정규화된 필수 empty anchor preview가 Incomplete가 아님");

        var wholeSelectedText = Qa.Text("전체 선택") with { Role = MathTextRole.Identifier };
        var wholeSelectedTail = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Unit,
            StyleOverride = new MathStyleOverride(FontSize: 17, Foreground: "#556677")
        };
        var wholeSelectedSpace = Space();
        var wholeSelectedRoot = Qa.Sequence(wholeSelectedText, wholeSelectedSpace, wholeSelectedTail);
        var wholeSelectedSession = new EditingSession(Qa.Workspace(wholeSelectedRoot));
        Qa.Check(wholeSelectedSession.TrySelectNode(wholeSelectedText.Id),
            "Text 전체 선택 삭제 정규화 준비");
        var wholeSelectedBefore = CaptureState(wholeSelectedSession);
        Qa.Check(wholeSelectedSession.DeleteSelection(), "Text whole-node DeleteSelection");
        var wholeSelectedAfterNodes = Qa.Nodes(wholeSelectedSession.Workspace).OfType<TextNode>().ToArray();
        Qa.Check(wholeSelectedAfterNodes.Length >= 2 && wholeSelectedAfterNodes.All(text => text.IsEmptySlot),
            "whole-node 삭제 뒤 direct unmarked empty anchor 정규화 누락");
        var wholeSelectedTailAfter = wholeSelectedAfterNodes.Single(text => text.Id == wholeSelectedTail.Id);
        Qa.Equal(wholeSelectedTail.Role, wholeSelectedTailAfter.Role, "whole-node 정규화 sibling Role 보존");
        Qa.Equal(wholeSelectedTail.StyleOverride, wholeSelectedTailAfter.StyleOverride,
            "whole-node 정규화 sibling style 보존");
        Qa.Check(Qa.Nodes(wholeSelectedSession.Workspace).OfType<MathSpaceNode>()
                .Any(space => space.Id == wholeSelectedSpace.Id),
            "whole-node 텍스트 삭제가 spacer를 임의로 제거");
        var wholeSelectedAfter = CaptureState(wholeSelectedSession);
        wholeSelectedSession.Undo();
        Qa.Equal(wholeSelectedBefore.Fingerprint, Qa.Fingerprint(wholeSelectedSession.Workspace),
            "whole-node empty 정규화 Undo");
        wholeSelectedSession.Redo();
        Qa.Equal(wholeSelectedAfter.Fingerprint, Qa.Fingerprint(wholeSelectedSession.Workspace),
            "whole-node empty 정규화 Redo");
        var wholeSelectedPath = directory.File("whole-node-empty-normalization.aethermath");
        await files.SaveAsync(wholeSelectedPath, wholeSelectedSession.CaptureSnapshot());
        Qa.Equal(wholeSelectedAfter.Fingerprint, Qa.Fingerprint(await files.OpenAsync(wholeSelectedPath)),
            "whole-node empty 정규화 저장/재열기");

        var replacementSource = Qa.Text("a") with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 23, FontFamily: "Replacement Face", Foreground: "#AABBCC")
        };
        var replacementOldSpace = Space() with
        {
            StyleOverride = new MathStyleOverride(FontSize: 18, Foreground: "#223344")
        };
        var replacementTail = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Unit,
            StyleOverride = new MathStyleOverride(FontSize: 17, FontFamily: "Tail Face")
        };
        var replacementRoot = Qa.Sequence(replacementSource, replacementOldSpace, replacementTail);
        var replacementSession = new EditingSession(Qa.Workspace(replacementRoot));
        replacementSession.MarkSaved(replacementSession.CaptureSnapshot());
        Qa.Select(replacementSession, replacementSource.Id, 0, Qa.Elements(replacementSource.Text));
        var beforeReplacement = CaptureState(replacementSession);
        var newReplacementSpaceId = replacementSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(newReplacementSpaceId.HasValue, "마지막 문자 전체 선택을 새 spacer로 치환");
        Qa.Equal(beforeReplacement.Revision + 1, replacementSession.Revision,
            "마지막 문자 spacer 치환이 단일 commit이 아님");
        Qa.Check(replacementSession.IsDirty, "마지막 문자 spacer 치환이 dirty 상태를 만들지 않음");
        Qa.Check(Qa.Nodes(replacementSession.Workspace).Select(node => node.Id).ToHashSet()
                .IsSupersetOf(Qa.Nodes(beforeReplacement.Workspace).Select(node => node.Id)),
            "마지막 문자 치환/정규화가 기존 node ID를 잃음");
        var replacementAfterNodes = Qa.Nodes(replacementSession.Workspace).ToArray();
        Qa.Equal(2, replacementAfterNodes.OfType<MathSpaceNode>().Count(),
            "전체 문자 치환이 새 spacer와 기존 spacer를 모두 보존하지 않음");
        var replacementSourceAfter = Qa.Leaf(replacementSession, replacementSource.Id);
        var replacementTailAfter = Qa.Leaf(replacementSession, replacementTail.Id);
        Qa.Check(replacementSourceAfter.Text.Length == 0 && replacementSourceAfter.IsEmptySlot,
            "전체 문자 치환의 기존 Text ID가 marked-empty anchor가 아님");
        Qa.Check(replacementTailAfter.Text.Length == 0 && replacementTailAfter.IsEmptySlot,
            "전체 문자 치환 뒤 unmarked tail anchor가 정규화되지 않음");
        Qa.Equal(replacementSource.Role, replacementSourceAfter.Role,
            "전체 문자 치환 anchor Role 보존");
        Qa.Equal(replacementSource.StyleOverride, replacementSourceAfter.StyleOverride,
            "전체 문자 치환 anchor style 보존");
        Qa.Equal(replacementTail.Role, replacementTailAfter.Role,
            "전체 문자 치환 뒤 tail Role 보존");
        Qa.Equal(replacementTail.StyleOverride, replacementTailAfter.StyleOverride,
            "전체 문자 치환 뒤 tail style 보존");
        Qa.Equal(replacementOldSpace.StyleOverride,
            ((MathSpaceNode)Qa.Node(replacementSession, replacementOldSpace.Id)).StyleOverride,
            "전체 문자 치환이 기존 spacer style을 바꿈");
        Qa.Equal(replacementSource.Id, replacementSession.Caret.NodeId,
            "전체 문자 치환 뒤 caret이 marked-empty source anchor에 있지 않음");
        var replacementState = CaptureState(replacementSession);
        var replacementLayout = MathLayoutEngine.Layout(replacementSession.Workspace, new FixedMetrics());
        Qa.Check(replacementLayout.Diagnostics.Any(diagnostic => diagnostic.NodeId == replacementSource.Id),
            "전체 문자 치환 뒤 required empty 진단이 사라짐");
        var replacementPreview = new MathOutputPreviewViewModel(
            replacementSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(replacementPreview.RefreshPreview(), "전체 문자 치환 preview 진단 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, replacementPreview.State,
            "전체 문자 spacer 치환이 미완성 preview로 표시되지 않음");
        Qa.Check(!replacementPreview.CanExportSvg, "전체 문자 spacer 치환에서 SVG 저장 가능");
        var replacementPath = directory.File("whole-text-spacer-replacement-normalization.aethermath");
        await files.SaveAsync(replacementPath, replacementSession.CaptureSnapshot());
        Qa.Equal(replacementState.Fingerprint, Qa.Fingerprint(await files.OpenAsync(replacementPath)),
            "전체 문자 spacer 치환의 marked-anchor 파일 왕복");
        replacementSession.Undo();
        Qa.Equal(beforeReplacement.Fingerprint, Qa.Fingerprint(replacementSession.Workspace),
            "전체 문자 spacer 치환 Undo가 원문·기존 spacer를 복구");
        Qa.Equal(beforeReplacement.StateId, replacementSession.CaptureSnapshot().StateId,
            "전체 문자 spacer 치환 Undo StateId 복구");
        Qa.Check(!replacementSession.IsDirty, "전체 문자 spacer 치환 Undo가 saved 상태를 복구하지 않음");
        replacementSession.Redo();
        Qa.Equal(replacementState.Fingerprint, Qa.Fingerprint(replacementSession.Workspace),
            "전체 문자 spacer 치환 Redo가 정규화된 anchor를 복구");

        var spaceReplacementSource = Qa.Text("a") with
        {
            Role = MathTextRole.Number,
            StyleOverride = new MathStyleOverride(FontSize: 20, Foreground: "#445566")
        };
        var spaceReplacementOldSpace = Space() with
        {
            StyleOverride = new MathStyleOverride(FontSize: 16, FontFamily: "Old Space Face")
        };
        var spaceReplacementTail = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 18, Foreground: "#667788")
        };
        var spaceReplacementRoot = Qa.Sequence(spaceReplacementSource, spaceReplacementOldSpace,
            spaceReplacementTail);
        var spaceReplacementSession = new EditingSession(Qa.Workspace(spaceReplacementRoot));
        spaceReplacementSession.MarkSaved(spaceReplacementSession.CaptureSnapshot());
        Qa.Select(spaceReplacementSession, spaceReplacementSource.Id, 0,
            Qa.Elements(spaceReplacementSource.Text));
        var beforeSpaceReplacement = CaptureState(spaceReplacementSession);
        var insertedReplacementSpaceId = spaceReplacementSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(insertedReplacementSpaceId.HasValue,
            "마지막 문자를 전체 선택해 새 spacer로 치환");
        Qa.Equal(beforeSpaceReplacement.Revision + 1, spaceReplacementSession.Revision,
            "전체 문자 spacer 치환의 history commit 수");
        var replacementSourceMarker = Qa.Leaf(spaceReplacementSession, spaceReplacementSource.Id);
        var replacementTailMarker = Qa.Leaf(spaceReplacementSession, spaceReplacementTail.Id);
        Qa.Check(replacementSourceMarker.IsEmptySlot && replacementTailMarker.IsEmptySlot,
            "공백 치환 뒤 기존 unmarked tail이 required marker로 정규화되지 않음");
        Qa.Equal(spaceReplacementSource.Role, replacementSourceMarker.Role,
            "공백 치환 source anchor Role 보존");
        Qa.Equal(spaceReplacementSource.StyleOverride, replacementSourceMarker.StyleOverride,
            "공백 치환 source anchor style 보존");
        Qa.Equal(spaceReplacementTail.Role, replacementTailMarker.Role,
            "공백 치환 tail anchor Role 보존");
        Qa.Equal(spaceReplacementTail.StyleOverride, replacementTailMarker.StyleOverride,
            "공백 치환 tail anchor style 보존");
        var replacedSpaces = Qa.Nodes(spaceReplacementSession.Workspace).OfType<MathSpaceNode>().ToArray();
        Qa.Equal(2, replacedSpaces.Length, "공백 치환이 새/기존 spacer를 모두 보존하지 않음");
        Qa.Check(replacedSpaces.Any(space => space.Id == insertedReplacementSpaceId) &&
                 replacedSpaces.Any(space => space.Id == spaceReplacementOldSpace.Id),
            "공백 치환 spacer ID가 생성/보존되지 않음");
        Qa.Equal(spaceReplacementOldSpace.StyleOverride,
            replacedSpaces.Single(space => space.Id == spaceReplacementOldSpace.Id).StyleOverride,
            "공백 치환이 기존 spacer style을 바꿈");
        var beforeReplacementUndo = CaptureState(spaceReplacementSession);
        var replacementIncompletePreview = new MathOutputPreviewViewModel(
            spaceReplacementSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(replacementIncompletePreview.RefreshPreview(),
            "공백 치환 marker preview 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, replacementIncompletePreview.State,
            "공백 치환 marker가 preview를 미완성으로 표시하지 않음");
        Qa.Check(!replacementIncompletePreview.CanExportSvg,
            "공백 치환 marker가 SVG 내보내기를 차단하지 않음");
        var spaceReplacementPath = directory.File("whole-text-space-replacement-normalization.aethermath");
        await files.SaveAsync(spaceReplacementPath, spaceReplacementSession.CaptureSnapshot());
        Qa.Equal(beforeReplacementUndo.Fingerprint,
            Qa.Fingerprint(await files.OpenAsync(spaceReplacementPath)),
            "공백 치환 marker의 유효 파일 왕복");
        spaceReplacementSession.Undo();
        Qa.Equal(beforeSpaceReplacement.Fingerprint, Qa.Fingerprint(spaceReplacementSession.Workspace),
            "공백 치환 Undo가 기존 text/spacer/tail을 복구");
        Qa.Equal(beforeSpaceReplacement.StateId, spaceReplacementSession.CaptureSnapshot().StateId,
            "공백 치환 Undo StateId 복구");
        Qa.Check(!spaceReplacementSession.IsDirty, "공백 치환 Undo가 saved 상태를 복구하지 않음");
        spaceReplacementSession.Redo();
        Qa.Equal(beforeReplacementUndo.Fingerprint, Qa.Fingerprint(spaceReplacementSession.Workspace),
            "공백 치환 Redo가 정규화된 incomplete 상태를 복구");

        var plainAnchor = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 21, Foreground: "#334455")
        };
        var plainSource = Qa.Text("끝") with
        {
            Role = MathTextRole.Unit,
            StyleOverride = new MathStyleOverride(FontSize: 24, FontFamily: "Anchor Face")
        };
        var plainSequence = Qa.Sequence(plainSource, plainAnchor);
        var plainSession = new EditingSession(Qa.Workspace(plainSequence));
        Qa.Select(plainSession, plainSource.Id, 0, Qa.Elements(plainSource.Text));
        var beforePlainDelete = CaptureState(plainSession);
        Qa.Check(plainSession.DeleteSelection(), "spacer 없는 Sequence의 마지막 Text 삭제");
        var normalizedPlainSource = Qa.Leaf(plainSession, plainSource.Id);
        var normalizedPlainSibling = Qa.Leaf(plainSession, plainAnchor.Id);
        Qa.Check(normalizedPlainSource.IsEmptySlot && normalizedPlainSibling.IsEmptySlot,
            "semantic-empty Sequence의 spacer 없는 빈 sibling anchor 표식 정규화 누락");
        Qa.Equal(plainAnchor.Id, normalizedPlainSibling.Id, "spacer 없는 sibling 정규화 ID 보존");
        Qa.Equal(plainAnchor.Role, normalizedPlainSibling.Role, "spacer 없는 sibling 정규화 Role 보존");
        Qa.Equal(plainAnchor.StyleOverride, normalizedPlainSibling.StyleOverride,
            "spacer 없는 sibling 정규화 style 보존");
        var plainNormalizedState = CaptureState(plainSession);
        plainSession.Undo();
        Qa.Equal(beforePlainDelete.Fingerprint, Qa.Fingerprint(plainSession.Workspace),
            "spacer 없는 sibling 정규화 Undo 문서 복원");
        plainSession.Redo();
        Qa.Equal(plainNormalizedState.Fingerprint, Qa.Fingerprint(plainSession.Workspace),
            "spacer 없는 sibling 정규화 Redo 문서 복원");
        var plainNormalizedPath = directory.File("plain-empty-sibling-normalization.aethermath");
        await files.SaveAsync(plainNormalizedPath, plainSession.CaptureSnapshot());
        Qa.Equal(plainNormalizedState.Fingerprint, Qa.Fingerprint(await files.OpenAsync(plainNormalizedPath)),
            "spacer 없는 sibling 정규화 저장/재열기");

        var nestedText = Qa.Text("중첩") with
        {
            Role = MathTextRole.Identifier,
            StyleOverride = new MathStyleOverride(FontSize: 27, Foreground: "#445566")
        };
        var nestedSpace = Space();
        var innerTail = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 18, FontFamily: "Inner Anchor")
        };
        var outerTail = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Unit,
            StyleOverride = new MathStyleOverride(FontSize: 19, Foreground: "#667788")
        };
        var innerSequence = Qa.Sequence(nestedText, nestedSpace, innerTail);
        var nestedOuterSequence = Qa.Sequence(innerSequence, outerTail);
        var nestedSession = new EditingSession(Qa.Workspace(nestedOuterSequence));
        Qa.Select(nestedSession, nestedText.Id, 0, Qa.Elements(nestedText.Text));
        var nestedBeforeDelete = CaptureState(nestedSession);
        Qa.Check(nestedSession.DeleteSelection(), "연속 semantic-empty Sequence 경로에서 Text 삭제");
        var innerAfterDelete = (SequenceNode)Qa.Node(nestedSession, innerSequence.Id);
        var innerTailAfterDelete = Qa.Leaf(nestedSession, innerTail.Id);
        var outerTailAfterDelete = Qa.Leaf(nestedSession, outerTail.Id);
        Qa.Check(Qa.Leaf(nestedSession, nestedText.Id).IsEmptySlot && innerTailAfterDelete.IsEmptySlot &&
                 outerTailAfterDelete.IsEmptySlot,
            "연속 empty Sequence 조상 경로의 unmarked Text anchor 정규화 누락");
        Qa.Equal(innerTail.Id, innerTailAfterDelete.Id, "inner anchor ID 보존");
        Qa.Equal(innerTail.Role, innerTailAfterDelete.Role, "inner anchor Role 보존");
        Qa.Equal(innerTail.StyleOverride, innerTailAfterDelete.StyleOverride, "inner anchor style 보존");
        Qa.Equal(outerTail.Id, outerTailAfterDelete.Id, "중첩 sibling anchor ID 보존");
        Qa.Equal(outerTail.Role, outerTailAfterDelete.Role, "중첩 sibling anchor Role 보존");
        Qa.Equal(outerTail.StyleOverride, outerTailAfterDelete.StyleOverride, "중첩 sibling anchor style 보존");
        Qa.Equal(innerSequence.Id, innerAfterDelete.Id, "중첩 정규화가 inner Sequence ID를 바꿈");
        var nestedNormalizedState = CaptureState(nestedSession);
        nestedSession.Undo();
        Qa.Equal(nestedBeforeDelete.Fingerprint, Qa.Fingerprint(nestedSession.Workspace),
            "중첩 anchor 정규화 Undo 문서 복원");
        nestedSession.Redo();
        Qa.Equal(nestedNormalizedState.Fingerprint, Qa.Fingerprint(nestedSession.Workspace),
            "중첩 anchor 정규화 Redo 문서 복원");
        var nestedPath = directory.File("nested-empty-sequence-normalization.aethermath");
        await files.SaveAsync(nestedPath, nestedSession.CaptureSnapshot());
        Qa.Equal(nestedNormalizedState.Fingerprint, Qa.Fingerprint(await files.OpenAsync(nestedPath)),
            "중첩 anchor 정규화 저장/재열기");
        var nestedPreview = new MathOutputPreviewViewModel(nestedSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(nestedPreview.RefreshPreview(), "중첩 empty anchor preview 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, nestedPreview.State,
            "중첩 필수 empty anchor preview가 Incomplete가 아님");

        var emptySubtreeSource = Qa.Text("기존");
        var emptySubtree = Qa.Sequence();
        var emptySubtreeRoot = Qa.Sequence(emptySubtreeSource, emptySubtree);
        var emptySubtreeSession = new EditingSession(Qa.Workspace(emptySubtreeRoot));
        Qa.Select(emptySubtreeSession, emptySubtreeSource.Id, 0, Qa.Elements(emptySubtreeSource.Text));
        var emptySubtreeBefore = CaptureState(emptySubtreeSession);
        Qa.Check(emptySubtreeSession.DeleteSelection(), "빈 Sequence sibling을 둔 마지막 Text 삭제");
        var normalizedEmptySubtree = (SequenceNode)Qa.Node(emptySubtreeSession, emptySubtree.Id);
        Qa.Equal(emptySubtree.Id, normalizedEmptySubtree.Id, "빈 Sequence 정규화가 기존 Sequence ID를 바꿈");
        var emptySubtreeMarker = normalizedEmptySubtree.Children.OfType<TextNode>().Single();
        Qa.Check(emptySubtreeMarker.IsEmptySlot && emptySubtreeMarker.Text.Length == 0,
            "빈 Sequence에 marked-empty Text anchor가 추가되지 않음");
        Qa.Equal(MathTextRole.Automatic, emptySubtreeMarker.Role, "빈 Sequence 새 marker의 기본 Role");
        Qa.Equal(1, normalizedEmptySubtree.Children.Length, "빈 Sequence에 marker가 둘 이상 추가됨");
        var emptySubtreeIdsBefore = Qa.Nodes(emptySubtreeBefore.Workspace).Select(node => node.Id).ToHashSet();
        var emptySubtreeIdsAfter = Qa.Nodes(emptySubtreeSession.Workspace).Select(node => node.Id).ToHashSet();
        Qa.Check(emptySubtreeIdsBefore.IsSubsetOf(emptySubtreeIdsAfter),
            "빈 Sequence 정규화가 기존 node ID를 제거");
        Qa.Equal(emptySubtreeIdsBefore.Count + 1, emptySubtreeIdsAfter.Count,
            "빈 Sequence 정규화가 새 marker 외 ID를 추가");
        Qa.Equal(emptySubtreeSource.Id, emptySubtreeSession.Caret.NodeId,
            "빈 Sequence marker 생성이 삭제 Text caret을 이동");
        var emptySubtreeAfter = CaptureState(emptySubtreeSession);
        emptySubtreeSession.Undo();
        Qa.Equal(emptySubtreeBefore.Fingerprint, Qa.Fingerprint(emptySubtreeSession.Workspace),
            "빈 Sequence marker 정규화 Undo");
        emptySubtreeSession.Redo();
        Qa.Equal(emptySubtreeAfter.Fingerprint, Qa.Fingerprint(emptySubtreeSession.Workspace),
            "빈 Sequence marker 정규화 Redo");
        var emptySubtreePath = directory.File("empty-sequence-marker-normalization.aethermath");
        await files.SaveAsync(emptySubtreePath, emptySubtreeSession.CaptureSnapshot());
        Qa.Equal(emptySubtreeAfter.Fingerprint, Qa.Fingerprint(await files.OpenAsync(emptySubtreePath)),
            "빈 Sequence 새 marker 저장/재열기");

        var nestedSiblingSource = Qa.Text("a") with { Role = MathTextRole.Number };
        var nestedSiblingAnchor = Qa.Text(string.Empty) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 16, Foreground: "#223344")
        };
        var nestedSiblingSequence = Qa.Sequence(nestedSiblingAnchor);
        var nestedSiblingRoot = Qa.Sequence(nestedSiblingSource, nestedSiblingSequence);
        var nestedSiblingSession = new EditingSession(Qa.Workspace(nestedSiblingRoot));
        Qa.Select(nestedSiblingSession, nestedSiblingSource.Id, 0, Qa.Elements(nestedSiblingSource.Text));
        var nestedSiblingBefore = CaptureState(nestedSiblingSession);
        Qa.Check(nestedSiblingSession.DeleteSelection(), "nested sibling Sequence의 마지막 Text 삭제");
        var nestedSiblingSequenceAfter = (SequenceNode)Qa.Node(nestedSiblingSession, nestedSiblingSequence.Id);
        var nestedSiblingAnchorAfter = Qa.Leaf(nestedSiblingSession, nestedSiblingAnchor.Id);
        Qa.Check(nestedSiblingAnchorAfter.IsEmptySlot, "nested sibling Sequence 내부 Text marker 승격 누락");
        Qa.Equal(nestedSiblingAnchor.Id, nestedSiblingAnchorAfter.Id, "nested sibling marker 기존 ID 보존");
        Qa.Equal(nestedSiblingAnchor.Role, nestedSiblingAnchorAfter.Role, "nested sibling marker Role 보존");
        Qa.Equal(nestedSiblingAnchor.StyleOverride, nestedSiblingAnchorAfter.StyleOverride,
            "nested sibling marker style 보존");
        Qa.Equal(nestedSiblingSequence.Id, nestedSiblingSequenceAfter.Id,
            "nested sibling marker 승격이 Sequence ID를 바꿈");
        var nestedSiblingAfter = CaptureState(nestedSiblingSession);
        nestedSiblingSession.Undo();
        Qa.Equal(nestedSiblingBefore.Fingerprint, Qa.Fingerprint(nestedSiblingSession.Workspace),
            "nested sibling marker 승격 Undo");
        nestedSiblingSession.Redo();
        Qa.Equal(nestedSiblingAfter.Fingerprint, Qa.Fingerprint(nestedSiblingSession.Workspace),
            "nested sibling marker 승격 Redo");
        var nestedSiblingPath = directory.File("nested-sibling-marker-normalization.aethermath");
        await files.SaveAsync(nestedSiblingPath, nestedSiblingSession.CaptureSnapshot());
        Qa.Equal(nestedSiblingAfter.Fingerprint, Qa.Fingerprint(await files.OpenAsync(nestedSiblingPath)),
            "nested sibling marker 승격 저장/재열기");

        var selected = Qa.Text("replace me") with { Role = MathTextRole.Number };
        var selectedSession = new EditingSession(Qa.Workspace(selected));
        Qa.Select(selectedSession, selected.Id, 0, Qa.Elements(selected.Text));
        var selectedSpaceId = selectedSession.InsertMathSpace(MathSpaceKind.Thin);
        Qa.Check(selectedSpaceId.HasValue, "전체 텍스트 범위 치환 spacer 삽입");
        var selectedSequence = (SequenceNode)selectedSession.Workspace.Groups[0].Rows[0].Root;
        var selectedAnchor = selectedSequence.Children.OfType<TextNode>().Single();
        Qa.Equal(selected.Id, selectedAnchor.Id, "전체 치환에서 원래 Text ID anchor 보존");
        Qa.Check(selectedAnchor.Text.Length == 0 && selectedAnchor.IsEmptySlot,
            "전체 치환에서 marked-empty anchor 유지");
        Qa.Equal(MathTextRole.Number, selectedAnchor.Role, "전체 치환 anchor 역할 보존");
    }

    private static async Task DeleteAndCaretBoundaries()
    {
        using var directory = new TempDirectory();
        var left = Qa.Text("왼쪽") with { Role = MathTextRole.Identifier };
        var firstSpace = Space() with { StyleOverride = new MathStyleOverride(FontSize: 18) };
        var secondSpace = Space();
        var right = Qa.Text("오른쪽") with { Role = MathTextRole.Unit };
        var session = new EditingSession(Qa.Workspace(Qa.Sequence(left, firstSpace, secondSpace, right)));
        session.MarkSaved(session.CaptureSnapshot());

        Qa.Check(session.TrySelectNode(firstSpace.Id), "spacer 전체 선택");
        var beforeSelectionDelete = CaptureState(session);
        Qa.Check(session.DeleteSelection(), "선택 spacer 삭제");
        var afterSelectionDelete = ((SequenceNode)session.Workspace.Groups[0].Rows[0].Root).Children;
        Qa.Check(afterSelectionDelete.All(node => node.Id != firstSpace.Id), "선택 spacer node 제거");
        Qa.Check(afterSelectionDelete.Any(node => node.Id == secondSpace.Id), "선택 spacer 삭제가 이웃 spacer도 제거");
        Qa.Equal(left.Id, afterSelectionDelete[0].Id, "선택 spacer 삭제 뒤 왼쪽 Text ID");
        Qa.Equal(right.Id, afterSelectionDelete[^1].Id, "선택 spacer 삭제 뒤 오른쪽 Text ID");
        Qa.Equal(MathTextRole.Identifier, ((TextNode)afterSelectionDelete[0]).Role, "삭제 뒤 왼쪽 Role");
        Qa.Equal(MathTextRole.Unit, ((TextNode)afterSelectionDelete[^1]).Role, "삭제 뒤 오른쪽 Role");
        Qa.Equal(left.Id, session.Caret.NodeId, "선택 spacer 삭제 caret을 이전 Text로 복원");
        Qa.Equal(Qa.Elements(left.Text), session.Caret.TextElementOffset, "이전 Text 끝 caret");
        session.Undo();
        Qa.Equal(beforeSelectionDelete.Fingerprint, Qa.Fingerprint(session.Workspace), "선택 spacer 삭제 Undo");
        session.Redo();
        Qa.Check(Qa.Nodes(session.Workspace).All(node => node.Id != firstSpace.Id), "선택 spacer 삭제 Redo");

        var prefixSpace = Space();
        var prefixText = Qa.Text("앞") with { Role = MathTextRole.Condition };
        var prefixSession = new EditingSession(Qa.Workspace(Qa.Sequence(prefixText, prefixSpace, Qa.Text("뒤"))));
        Qa.Caret(prefixSession, prefixText.Id, Qa.Elements(prefixText.Text));
        var prefixBefore = CaptureState(prefixSession);
        Qa.Check(prefixSession.DeleteForward(), "Text 끝 Delete가 바로 뒤 spacer 제거");
        Qa.Check(Qa.Nodes(prefixSession.Workspace).All(node => node.Id != prefixSpace.Id), "Delete 뒤 spacer 잔존");
        Qa.Equal(prefixText.Id, prefixSession.Caret.NodeId, "Delete 뒤 기존 caret Text 유지");
        prefixSession.Undo();
        Qa.Equal(prefixBefore.Fingerprint, Qa.Fingerprint(prefixSession.Workspace), "경계 Delete Undo");
        prefixSession.Redo();
        Qa.Check(Qa.Nodes(prefixSession.Workspace).All(node => node.Id != prefixSpace.Id), "경계 Delete Redo");

        var suffixSpace = Space();
        var suffixText = Qa.Text("뒤") with { Role = MathTextRole.Condition };
        var suffixSession = new EditingSession(Qa.Workspace(Qa.Sequence(Qa.Text("앞"), suffixSpace, suffixText)));
        Qa.Caret(suffixSession, suffixText.Id, 0);
        var suffixBefore = CaptureState(suffixSession);
        Qa.Check(suffixSession.Backspace(), "Text 시작 Backspace가 바로 앞 spacer 제거");
        Qa.Check(Qa.Nodes(suffixSession.Workspace).All(node => node.Id != suffixSpace.Id), "Backspace 뒤 spacer 잔존");
        Qa.Equal(suffixText.Id, suffixSession.Caret.NodeId, "Backspace 뒤 기존 caret Text 유지");
        Qa.Equal(0, suffixSession.Caret.TextElementOffset, "Backspace 뒤 Text 시작 위치 유지");
        suffixSession.Undo();
        Qa.Equal(suffixBefore.Fingerprint, Qa.Fingerprint(suffixSession.Workspace), "경계 Backspace Undo");
        suffixSession.Redo();
        Qa.Check(Qa.Nodes(suffixSession.Workspace).All(node => node.Id != suffixSpace.Id), "경계 Backspace Redo");

        var fractionNumerator = Qa.Text("분자");
        var fractionDenominator = Qa.Text("분모") with { Role = MathTextRole.Unit };
        var fraction = new FractionNode(Guid.NewGuid(), fractionNumerator, fractionDenominator);
        var structuralSpace = Space();
        var radicalContent = Qa.Text("근호 내용") with { Role = MathTextRole.Identifier };
        var radical = new RadicalNode(Guid.NewGuid(), radicalContent);
        var structuralRoot = Qa.Sequence(fraction, structuralSpace, radical);
        var structuralSession = new EditingSession(Qa.Workspace(structuralRoot));
        structuralSession.MarkSaved(structuralSession.CaptureSnapshot());
        var structuralBeforeNavigation = CaptureState(structuralSession);
        Qa.Check(structuralSession.TrySelectNode(structuralSpace.Id), "구조 사이 spacer 전체 선택");
        Qa.Check(structuralSession.TryMoveFromSelectedMathSpace(forward: false),
            "구조 사이 spacer 왼쪽 탈출");
        Qa.Equal(fractionDenominator.Id, structuralSession.Caret.NodeId,
            "spacer 왼쪽 탈출이 분수 분모 facing leaf를 선택하지 않음");
        Qa.Equal(Qa.Elements(fractionDenominator.Text), structuralSession.Caret.TextElementOffset,
            "spacer 왼쪽 탈출 caret이 분모 끝이 아님");
        Qa.Check(!structuralSession.IsNodeSelected, "구조 사이 spacer 왼쪽 탈출 뒤 전체 선택이 유지됨");
        AssertDocumentHistoryUnchanged(structuralBeforeNavigation, structuralSession,
            "구조 사이 spacer 왼쪽 탐색");
        Qa.Check(structuralSession.TrySelectNode(structuralSpace.Id), "오른쪽 탈출 전 spacer 재선택");
        Qa.Check(structuralSession.TryMoveFromSelectedMathSpace(forward: true),
            "구조 사이 spacer 오른쪽/Tab 탈출");
        Qa.Equal(radicalContent.Id, structuralSession.Caret.NodeId,
            "spacer 오른쪽 탈출이 근호 내용 facing leaf를 선택하지 않음");
        Qa.Equal(0, structuralSession.Caret.TextElementOffset, "spacer 오른쪽 탈출 caret이 근호 내용 시작이 아님");
        AssertDocumentHistoryUnchanged(structuralBeforeNavigation, structuralSession,
            "구조 사이 spacer 오른쪽 탐색");
        Qa.Check(structuralSession.TrySelectNode(structuralSpace.Id), "구조 spacer 삭제 전 선택");
        Qa.Check(structuralSession.DeleteSelection(), "구조 사이 spacer 단일 삭제");
        var structuralAfterDelete = (SequenceNode)structuralSession.Workspace.Groups[0].Rows[0].Root;
        Qa.Equal(2, structuralAfterDelete.Children.Length, "구조 사이 삭제가 spacer 외 이웃을 제거");
        Qa.Equal(fraction.Id, structuralAfterDelete.Children[0].Id, "삭제 후 분수 ID 보존");
        Qa.Equal(radical.Id, structuralAfterDelete.Children[1].Id, "삭제 후 근호 ID 보존");
        Qa.Check(Qa.Node(structuralSession, structuralSession.Caret.NodeId) is TextNode,
            "구조 사이 spacer 삭제 뒤 caret이 유효한 Text가 아님");
        structuralSession.Undo();
        Qa.Check(Qa.Nodes(structuralSession.Workspace).Any(node => node.Id == structuralSpace.Id),
            "구조 사이 spacer 삭제 Undo");
        structuralSession.Redo();
        Qa.Check(Qa.Nodes(structuralSession.Workspace).All(node => node.Id != structuralSpace.Id),
            "구조 사이 spacer 삭제 Redo");

        var requiredAnchor = Qa.Text(string.Empty, true) with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(FontSize: 24, FontFamily: "Required Face")
        };
        var nestedOnlySpace = Space() with { StyleOverride = new MathStyleOverride(FontSize: 19) };
        var nestedOnlySpaceSequence = Qa.Sequence(nestedOnlySpace) with
        {
            StyleOverride = new MathStyleOverride(FontSize: 28, Foreground: "#334455")
        };
        var nestedOnlySpaceRoot = Qa.Sequence(requiredAnchor, nestedOnlySpaceSequence);
        var nestedOnlySpaceSession = new EditingSession(Qa.Workspace(nestedOnlySpaceRoot));
        nestedOnlySpaceSession.MarkSaved(nestedOnlySpaceSession.CaptureSnapshot());
        Qa.Check(nestedOnlySpaceSession.TrySelectNode(nestedOnlySpace.Id),
            "필수 marker 아래 유일 spacer 구조 선택");
        var beforeNestedOnlyDelete = CaptureState(nestedOnlySpaceSession);
        Qa.Check(nestedOnlySpaceSession.DeleteSelection(), "중첩 Sequence의 유일 spacer 삭제");
        Qa.Equal(beforeNestedOnlyDelete.Revision + 1, nestedOnlySpaceSession.Revision,
            "중첩 유일 spacer 삭제가 단일 commit이 아님");
        var preservedNestedSequence = (SequenceNode)Qa.Node(nestedOnlySpaceSession, nestedOnlySpaceSequence.Id);
        Qa.Equal(nestedOnlySpaceSequence.StyleOverride, preservedNestedSequence.StyleOverride,
            "유일 spacer 삭제가 parent Sequence style을 바꿈");
        Qa.Equal(1, preservedNestedSequence.Children.Length,
            "빈 parent Sequence에 marker 하나가 추가되지 않음");
        var deletionAnchor = preservedNestedSequence.Children.Single() as TextNode;
        Qa.Check(deletionAnchor is { Text.Length: 0, IsEmptySlot: true },
            "유일 spacer 삭제가 parent Sequence 안 유효한 marked-empty anchor를 만들지 않음");
        Qa.Check(deletionAnchor!.Id != nestedOnlySpace.Id && deletionAnchor.Id != requiredAnchor.Id,
            "유일 spacer 삭제 marker가 새 ID를 쓰지 않음");
        Qa.Check(Qa.Nodes(nestedOnlySpaceSession.Workspace).All(node => node.Id != nestedOnlySpace.Id),
            "삭제한 유일 spacer ID가 남음");
        Qa.Equal(nestedOnlySpaceRoot.Id, ((SequenceNode)nestedOnlySpaceSession.Workspace.Groups[0].Rows[0].Root).Id,
            "유일 spacer 삭제가 outer root Sequence ID를 바꿈");
        var retainedRequiredAnchor = Qa.Leaf(nestedOnlySpaceSession, requiredAnchor.Id);
        Qa.Check(retainedRequiredAnchor.IsEmptySlot, "유일 spacer 삭제가 기존 필수 marker를 해제함");
        Qa.Equal(requiredAnchor.Role, retainedRequiredAnchor.Role, "기존 필수 marker Role 보존");
        Qa.Equal(requiredAnchor.StyleOverride, retainedRequiredAnchor.StyleOverride,
            "기존 필수 marker style 보존");
        Qa.Equal(deletionAnchor.Id, nestedOnlySpaceSession.Caret.NodeId,
            "유일 spacer 삭제 뒤 caret이 새 parent marker를 가리키지 않음");
        var nestedOnlyAfter = CaptureState(nestedOnlySpaceSession);
        var nestedOnlyPath = directory.File("nested-only-spacer-delete-marker.aethermath");
        await new WorkspaceFileService().SaveAsync(nestedOnlyPath, nestedOnlySpaceSession.CaptureSnapshot());
        Qa.Equal(nestedOnlyAfter.Fingerprint, Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(nestedOnlyPath)),
            "유일 spacer 삭제 marker의 파일 저장/재열기");
        var nestedOnlyPreview = new MathOutputPreviewViewModel(
            nestedOnlySpaceSession, new FixedMetrics(), new RecordingSvgExporter());
        Qa.Check(nestedOnlyPreview.RefreshPreview(), "유일 spacer 삭제 marker preview 진단 갱신");
        Qa.Equal(MathOutputPreviewState.Incomplete, nestedOnlyPreview.State,
            "유일 spacer 삭제 뒤 필수 marker가 preview incomplete를 잃음");
        Qa.Check(!nestedOnlyPreview.CanExportSvg, "유일 spacer 삭제 marker에서 SVG 저장 가능");
        nestedOnlySpaceSession.Undo();
        Qa.Equal(beforeNestedOnlyDelete.Fingerprint, Qa.Fingerprint(nestedOnlySpaceSession.Workspace),
            "유일 spacer 삭제 Undo가 원 Sequence와 spacer 복구");
        Qa.Equal(beforeNestedOnlyDelete.StateId, nestedOnlySpaceSession.CaptureSnapshot().StateId,
            "유일 spacer 삭제 Undo StateId 복구");
        Qa.Check(!nestedOnlySpaceSession.IsDirty, "유일 spacer 삭제 Undo가 저장 상태를 복구하지 않음");
        nestedOnlySpaceSession.Redo();
        Qa.Equal(nestedOnlyAfter.Fingerprint, Qa.Fingerprint(nestedOnlySpaceSession.Workspace),
            "유일 spacer 삭제 Redo가 marker를 복구");

    }

    private static void ClearSemanticEmptySpace()
    {
        var scopeCases = new[]
        {
            MathClearScope.Selection,
            MathClearScope.CurrentRow,
            MathClearScope.CurrentGroup,
            MathClearScope.Workspace
        };

        foreach (var scope in scopeCases)
        {
            var targetAnchor = Qa.Text(string.Empty, true) with
            {
                StyleOverride = new MathStyleOverride(FontSize: 25),
                Role = MathTextRole.Unit
            };
            var targetSpace = Space() with { StyleOverride = targetAnchor.StyleOverride };
            var targetRoot = Qa.Sequence(targetAnchor, targetSpace);
            var selectedRow = Row(targetRoot) with
            {
                StyleOverride = new MathStyleOverride(FontSize: 27, Foreground: "#AABBCC")
            };
            var otherRow = Row(Qa.Text("비대상 행"));
            var group1 = new EquationGroup(Guid.NewGuid(), [selectedRow, otherRow], AlignmentMode.Equals, 31)
            {
                StyleOverride = new MathStyleOverride(FontFamily: "Group Face")
            };
            var otherGroupRow = Row(Qa.Text("다른 그룹"));
            var group2 = new EquationGroup(Guid.NewGuid(), [otherGroupRow], AlignmentMode.None, 44);
            var workspace = Workspace(group1, group2);
            var session = new EditingSession(workspace);
            Qa.Check(session.TrySelectNode(targetSpace.Id), scope + " spacer 구조 선택");
            Qa.Check(MathTree.IsSemanticallyEmpty(targetSpace), "MathSpace가 semantic empty가 아님");
            var before = CaptureState(session);
            Qa.Check(session.CanClear(scope), scope + " spacer-only 범위를 clear 가능으로 표시하지 않음");
            Qa.Check(session.Clear(scope), scope + " spacer-only 범위 clear");
            Qa.Equal(before.Revision + 1, session.Revision, scope + " clear 단일 commit");
            Qa.Check(session.Caret.NodeId != Guid.Empty && Qa.Node(session, session.Caret.NodeId) is TextNode,
                scope + " clear 뒤 caret이 새 Text root를 가리키지 않음");
            Qa.Check(!session.IsNodeSelected && session.Selection is null, scope + " clear 뒤 선택 해제");

            var rowsAfter = session.Workspace.Groups.SelectMany(group => group.Rows).ToArray();
            var affectedRows = scope switch
            {
                MathClearScope.Selection => new[] { selectedRow.Id },
                MathClearScope.CurrentRow => new[] { selectedRow.Id },
                MathClearScope.CurrentGroup => group1.Rows.Select(row => row.Id).ToArray(),
                MathClearScope.Workspace => workspace.Groups.SelectMany(group => group.Rows).Select(row => row.Id).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
            Qa.Equal(string.Join(',', workspace.Groups.Select(group => group.Id)),
                string.Join(',', session.Workspace.Groups.Select(group => group.Id)), scope + " 그룹 ID 보존");
            Qa.Equal(string.Join(',', workspace.Groups.SelectMany(group => group.Rows).Select(row => row.Id)),
                string.Join(',', rowsAfter.Select(row => row.Id)), scope + " 행 ID·문서 순서 보존");
            foreach (var row in rowsAfter.Where(row => affectedRows.Contains(row.Id)))
                Qa.Check(!Qa.Walk(row.Root).Any(node => node.Id == targetSpace.Id),
                    scope + " 대상 spacer가 남음");

            if (scope == MathClearScope.Selection)
            {
                var selectionRow = rowsAfter.Single(row => row.Id == selectedRow.Id);
                Qa.Equal(targetRoot.Id, selectionRow.Root.Id, "선택 Clear가 행 root ID를 바꿈");
                Qa.Check(selectionRow.Root is SequenceNode, "선택 Clear가 주변 Sequence root를 제거");
                Qa.Check(!Qa.Walk(selectionRow.Root).OfType<MathSpaceNode>().Any(),
                    "선택 Clear가 spacer 하나만 제거하지 않음");
                var survivingAnchor = Qa.Nodes(session.Workspace).OfType<TextNode>().Single(node => node.Id == targetAnchor.Id);
                Qa.Equal(targetAnchor.Text, survivingAnchor.Text, "선택 Clear 주변 anchor 원문 보존");
                Qa.Equal(targetAnchor.StyleOverride, survivingAnchor.StyleOverride, "선택 Clear 주변 anchor 스타일 보존");
                Qa.Equal(targetAnchor.Role, survivingAnchor.Role, "선택 Clear 주변 anchor 역할 보존");
            }
            else
            {
                foreach (var row in rowsAfter.Where(row => affectedRows.Contains(row.Id)))
                {
                    Qa.Check(row.Root is TextNode { IsEmptySlot: true, Text.Length: 0 },
                        scope + " 대상 행 root가 새 marked-empty Text가 아님");
                    Qa.Check(row.Root.Id != before.RootIds.Single(pair => pair.Key == row.Id).Value,
                        scope + " 범위 Clear가 이전 child root ID를 보존했다고 가정함");
                }
                Qa.Equal(group1.Alignment, session.Workspace.Groups[0].Alignment, scope + " 상위 정렬 보존");
                Qa.Equal(group1.RowSpacing, session.Workspace.Groups[0].RowSpacing, scope + " 상위 간격 보존");
                Qa.Equal(group1.StyleOverride, session.Workspace.Groups[0].StyleOverride, scope + " 상위 스타일 보존");
                Qa.Equal(selectedRow.StyleOverride,
                    session.Workspace.Groups[0].Rows.Single(row => row.Id == selectedRow.Id).StyleOverride,
                    scope + " 대상 row style 보존");
                if (scope == MathClearScope.CurrentRow)
                    Qa.Equal(otherRow.Root.Id, rowsAfter.Single(row => row.Id == otherRow.Id).Root.Id,
                        "CurrentRow clear가 비대상 행 root를 바꿈");
                if (scope == MathClearScope.CurrentGroup)
                    Qa.Equal(otherGroupRow.Root.Id, rowsAfter.Single(row => row.Id == otherGroupRow.Id).Root.Id,
                        "CurrentGroup clear가 비대상 그룹 root를 바꿈");
            }

            session.Undo();
            Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), scope + " clear Undo 문서 복원");
            Qa.Equal(before.Caret, session.Caret, scope + " clear Undo caret 복원");
            Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, scope + " clear Undo 선택 ID 복원");
            session.Redo();
            var rowsAfterRedo = session.Workspace.Groups.SelectMany(group => group.Rows).ToArray();
            if (scope == MathClearScope.Selection)
            {
                var redoneSelectionRow = rowsAfterRedo.Single(row => row.Id == selectedRow.Id);
                Qa.Equal(targetRoot.Id, redoneSelectionRow.Root.Id, "선택 Clear Redo가 행 root ID를 바꿈");
                Qa.Check(!Qa.Walk(redoneSelectionRow.Root).OfType<MathSpaceNode>().Any(),
                    "선택 Clear Redo가 spacer를 남김");
            }
            else
                Qa.Check(rowsAfterRedo.Where(row => affectedRows.Contains(row.Id))
                        .All(row => row.Root is TextNode { IsEmptySlot: true }),
                    scope + " clear Redo");

            var clearedState = CaptureState(session);
            Qa.Check(!session.CanClear(scope) && !session.Clear(scope), scope + " 비운 뒤 다시 비우기 허용");
            AssertUnchanged(clearedState, session, scope + " 빈 범위의 clear no-op");
        }
    }

    private static void LayoutWidthAndSequenceGaps()
    {
        foreach (var (fontSize, explicitSize) in new[] { (24d, (double?)null), (36d, 18d) })
        {
            var left = Qa.Text("a");
            var spacer = Space() with
            {
                StyleOverride = explicitSize is null ? null : new MathStyleOverride(FontSize: explicitSize)
            };
            var right = Qa.Text("b");
            var sequence = Qa.Sequence(left, spacer, right);
            var workspace = WorkspaceWithStyle(sequence, fontSize);
            var layout = MathLayoutEngine.Layout(workspace, new FixedMetrics());
            var leftLayout = layout.Nodes.Single(node => node.NodeId == left.Id);
            var spaceLayout = layout.Nodes.Single(node => node.NodeId == spacer.Id);
            var rightLayout = layout.Nodes.Single(node => node.NodeId == right.Id);
            var effectiveSize = explicitSize ?? fontSize;
            Qa.Equal(MathNodeKind.MathSpace, spaceLayout.Kind, "spacer layout kind");
            Qa.Near(effectiveSize / 6, spaceLayout.Bounds.Width, "effective font size의 1/6 폭");
            Qa.Check(spaceLayout.Bounds.Height > 0, "spacer의 line-height bounds가 0 이하");
            Qa.Check(spaceLayout.CaretStops.IsEmpty, "spacer가 caret stop을 소유");
            Qa.Check(layout.Primitives.OfType<GlyphPrimitive>().All(glyph => glyph.NodeId != spacer.Id),
                "spacer가 glyph primitive로 출력됨");
            Qa.Near(leftLayout.Bounds.Right, spaceLayout.Bounds.X, "spacer 앞 자동 gap은 0");
            Qa.Near(spaceLayout.Bounds.Right, rightLayout.Bounds.X, "spacer 뒤 자동 gap은 0");
        }

        var plainFirst = Qa.Text("a");
        var plainSecond = Qa.Text("b");
        var plainLayout = MathLayoutEngine.Layout(WorkspaceWithStyle(Qa.Sequence(plainFirst, plainSecond), 24),
            new FixedMetrics());
        var plainFirstBounds = plainLayout.Nodes.Single(node => node.NodeId == plainFirst.Id).Bounds;
        var plainSecondBounds = plainLayout.Nodes.Single(node => node.NodeId == plainSecond.Id).Bounds;
        Qa.Near(24 * 0.06, plainSecondBounds.X - plainFirstBounds.Right,
            "spacer 없는 Text pair의 기존 자동 간격");

        var structuralText = Qa.Text("a");
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("n"), Qa.Text("d"));
        var structuralLayout = MathLayoutEngine.Layout(
            WorkspaceWithStyle(Qa.Sequence(structuralText, fraction), 24), new FixedMetrics());
        var textBounds = structuralLayout.Nodes.Single(node => node.NodeId == structuralText.Id).Bounds;
        var fractionBounds = structuralLayout.Nodes.Single(node => node.NodeId == fraction.Id).Bounds;
        Qa.Near(24 * 0.10, fractionBounds.X - textBounds.Right, "spacer 없는 구조 pair의 기존 간격");

        var leading1 = Space();
        var leading2 = Space();
        var leadingText = Qa.Text("x");
        var leadingLayout = MathLayoutEngine.Layout(
            WorkspaceWithStyle(Qa.Sequence(leading1, leading2, leadingText), 30), new FixedMetrics());
        var leadingSpace1 = leadingLayout.Nodes.Single(node => node.NodeId == leading1.Id).Bounds;
        var leadingSpace2 = leadingLayout.Nodes.Single(node => node.NodeId == leading2.Id).Bounds;
        var leadingTextBounds = leadingLayout.Nodes.Single(node => node.NodeId == leadingText.Id).Bounds;
        Qa.Near(5, leadingSpace1.Width, "연속 spacer 첫 폭");
        Qa.Near(leadingSpace1.Right, leadingSpace2.X, "연속 spacer 사이 자동 gap 0");
        Qa.Near(leadingSpace2.Right, leadingTextBounds.X, "연속 spacer 뒤 자동 gap 0");

        var scriptSpace = Space();
        var scriptSequence = Qa.Sequence(Qa.Text("a"), scriptSpace, Qa.Text("b"));
        var script = new ScriptNode(Guid.NewGuid(), Qa.Text("x"), scriptSequence, null);
        var scriptLayout = MathLayoutEngine.Layout(WorkspaceWithStyle(script, 30), new FixedMetrics());
        var scriptSpaceBounds = scriptLayout.Nodes.Single(node => node.NodeId == scriptSpace.Id).Bounds;
        Qa.Near(30 * 0.71 / 6, scriptSpaceBounds.Width, "Script 문맥의 effective font size/6");

        var spacerBeforeStructure = Space();
        var structureAfterSpace = new FractionNode(Guid.NewGuid(), Qa.Text("u"), Qa.Text("v"));
        var nested = Qa.Sequence(Qa.Text("p"), spacerBeforeStructure, structureAfterSpace,
            Space(), Qa.Text("q"));
        var nestedLayout = MathLayoutEngine.Layout(WorkspaceWithStyle(nested, 32), new FixedMetrics());
        var beforeStructure = nestedLayout.Nodes.Single(node => node.NodeId == spacerBeforeStructure.Id).Bounds;
        var afterStructure = nestedLayout.Nodes.Single(node => node.NodeId == structureAfterSpace.Id).Bounds;
        var trailingSpace = nested.Children.OfType<MathSpaceNode>().Last();
        var trailingBounds = nestedLayout.Nodes.Single(node => node.NodeId == trailingSpace.Id).Bounds;
        var qBounds = nestedLayout.Nodes.Single(node => node.NodeId == ((TextNode)nested.Children[^1]).Id).Bounds;
        Qa.Near(beforeStructure.Right, afterStructure.X, "spacer가 인접 구조 뒤 자동 gap 0");
        Qa.Near(afterStructure.Right, trailingBounds.X, "구조와 spacer 직접 인접 gap 0");
        Qa.Near(trailingBounds.Right, qBounds.X, "구조 뒤 spacer와 Text gap 0");
    }

    private static void LatexOutputAndRawUnicode()
    {
        var anchor = Qa.Text(string.Empty, true);
        var raw = Qa.Text("A\u2009B");
        var explicitSpace = Space();
        var root = Qa.Sequence(Qa.Text("왼쪽"), explicitSpace, raw);
        var workspace = Workspace(root);
        var latex = LatexSerializer.ToLatex(workspace);
        Qa.Check(latex.Contains("\\,", StringComparison.Ordinal), "명시 Thin node가 LaTeX \\, 명령으로 출력되지 않음");
        Qa.Equal(1, Count(latex, "\\,"), "명시 spacer 출력은 한 개의 thin-space 명령");
        Qa.Check(latex.Contains("A\u2009B", StringComparison.Ordinal), "TextNode U+2009가 원문으로 출력되지 않음");
        Qa.Check(!latex.Contains("A\\,B", StringComparison.Ordinal), "원문 U+2009를 명시 spacer로 변환");

        var styleAnchor = Qa.Text(string.Empty, true);
        var baseStyleWorkspace = Workspace(Qa.Sequence(styleAnchor, explicitSpace));
        Qa.Equal("{\\displaystyle }{\\displaystyle \\,}", LatexSerializer.ToLatex(baseStyleWorkspace),
            "Workspace Display 기본 스타일이 marker와 명시 spacer에 보존되지 않음");
        var workspaceScript = baseStyleWorkspace with
        {
            Style = baseStyleWorkspace.Style with { TypesettingStyle = MathTypesettingStyle.Script }
        };
        Qa.Equal("{\\scriptstyle }{\\scriptstyle \\,}", LatexSerializer.ToLatex(workspaceScript),
            "Workspace Script 상속이 spacer 조판 그룹에 반영되지 않음");
        var groupScript = new EquationGroup(Guid.NewGuid(), [Row(Qa.Sequence(styleAnchor, explicitSpace))], AlignmentMode.None, 0)
        {
            StyleOverride = new MathStyleOverride { TypesettingStyle = MathTypesettingStyle.Script }
        };
        Qa.Equal("{\\scriptstyle }{\\scriptstyle \\,}", LatexSerializer.ToLatex(Workspace(groupScript)),
            "Group Script 상속이 spacer 조판 그룹에 반영되지 않음");
        var rowScript = Row(Qa.Sequence(styleAnchor, explicitSpace)) with
        {
            StyleOverride = new MathStyleOverride { TypesettingStyle = MathTypesettingStyle.Script }
        };
        var rowScriptGroup = new EquationGroup(Guid.NewGuid(), [rowScript], AlignmentMode.None, 0);
        Qa.Equal("{\\scriptstyle }{\\scriptstyle \\,}", LatexSerializer.ToLatex(Workspace(rowScriptGroup)),
            "Row Script 상속이 spacer 조판 그룹에 반영되지 않음");

        var scriptSpace = Space() with
        {
            StyleOverride = new MathStyleOverride { TypesettingStyle = MathTypesettingStyle.Script }
        };
        var localScriptWorkspace = Workspace(Qa.Sequence(styleAnchor, scriptSpace));
        Qa.Equal("{\\displaystyle }{\\scriptstyle \\,}", LatexSerializer.ToLatex(localScriptWorkspace),
            "spacer-local Script override가 marker/공백 조판 그룹에 반영되지 않음");
        var displaySpace = Space() with
        {
            StyleOverride = new MathStyleOverride { TypesettingStyle = MathTypesettingStyle.Display }
        };
        Qa.Equal("{\\scriptstyle }{\\displaystyle \\,}", LatexSerializer.ToLatex(workspaceScript with
            { Groups = [new EquationGroup(Guid.NewGuid(), [Row(Qa.Sequence(styleAnchor, displaySpace))], AlignmentMode.None, 0)] }),
            "spacer-local Display override가 상속 Script보다 우선하지 않음");

        var scriptLayout = MathLayoutEngine.Layout(workspaceScript, new FixedMetrics());
        Qa.Near(30 * 0.71 / 6, scriptLayout.Nodes.Single(node => node.NodeId == explicitSpace.Id).Bounds.Width,
            "Workspace Script의 spacer effective 폭이 layout 문맥과 다름");
        var localDisplayLayout = MathLayoutEngine.Layout(workspaceScript with
        {
            Groups = [new EquationGroup(Guid.NewGuid(),
                [Row(Qa.Sequence(styleAnchor, displaySpace))], AlignmentMode.None, 0)]
        }, new FixedMetrics());
        Qa.Near(30 / 6, localDisplayLayout.Nodes.Single(node => node.NodeId == displaySpace.Id).Bounds.Width,
            "spacer-local Display의 effective 폭이 inherited Script보다 우선하지 않음");

        var incomplete = Workspace(Qa.Sequence(anchor, Space()));
        var generated = LatexSerializer.ToLatex(incomplete);
        Qa.Check(generated.Contains("\\,", StringComparison.Ordinal), "미완성 source 생성 자체가 차단됨");
        Qa.Check(LatexSerializer.GetIncompleteSlotNodeIds(incomplete).Contains(anchor.Id),
            "미완성 spacer와 함께 empty marker 진단 유지");
    }

    private static async Task PreviewAndSvgConsumer()
    {
        using var directory = new TempDirectory();
        var numerator = Qa.Text("a");
        var denominator = Qa.Text("b");
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var spacer = Space();
        var root = Qa.Sequence(Qa.Text("left"), spacer, fraction, Qa.Text("right"));
        var workspace = WorkspaceWithStyle(root, 30);
        var session = new EditingSession(workspace);
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Caret(session, numerator.Id, 1);
        Qa.Select(session, numerator.Id, 0, 1);
        var before = CaptureState(session);
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, new FixedMetrics(), exporter);
        foreach (var mode in new[] { MathOutputMode.WorkspaceStyle, MathOutputMode.Display, MathOutputMode.Inline })
        {
            preview.SelectedMode = mode;
            Qa.Check(preview.RefreshPreview(), mode + " preview 갱신");
            var snapshot = preview.PreviewSnapshot ?? throw new InvalidOperationException(mode + " snapshot 없음");
            Qa.Equal(MathOutputPreviewState.Ready, preview.State, mode + " preview Ready");
            Qa.Check(snapshot.Layout.Nodes.Any(node => node.NodeId == spacer.Id && node.Kind == MathNodeKind.MathSpace),
                mode + " frozen layout의 spacer bounds 누락");
            Qa.Check(snapshot.Layout.Primitives.OfType<GlyphPrimitive>().All(glyph => glyph.NodeId != spacer.Id),
                mode + " preview spacer glyph primitive");
            var path = directory.File(mode + ".svg");
            var result = await preview.ExportSvgToPathAsync(path);
            Qa.Equal(SvgPreviewExportStatus.Saved, result.Status, mode + " SVG 저장 결과");
            Qa.Check(ReferenceEquals(snapshot.Layout, exporter.Layouts[^1]), mode + " export가 frozen LayoutResult 인스턴스를 재사용");
            var xml = XDocument.Load(path);
            AssertSvgMatchesLayout(xml, snapshot.Layout, spacer.Id, mode.ToString());
        }
        AssertUnchanged(before, session, "preview 모드·SVG 내보내기 원본 Session");

        var emptyAnchor = Qa.Text(string.Empty, true);
        var incompleteSession = new EditingSession(Workspace(Qa.Sequence(emptyAnchor, Space())));
        var incompleteExporter = new RecordingSvgExporter();
        var incompletePreview = new MathOutputPreviewViewModel(incompleteSession, new FixedMetrics(), incompleteExporter);
        Qa.Check(incompletePreview.RefreshPreview(), "빈 슬롯 preview 진단 생성");
        Qa.Equal(MathOutputPreviewState.Incomplete, incompletePreview.State, "빈 슬롯과 spacer의 preview 상태");
        Qa.Check(!incompletePreview.CanExportSvg, "미완성 spacer preview SVG 버튼 enabled");
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incompletePreview.PreviewSnapshot!.Layout),
            "미완성 spacer를 포함한 실제 SVG 경계");
        Qa.Equal(0, incompleteExporter.Calls, "미완성 preview에서 exporter를 호출");
    }

    private static async Task CloneAndPersistenceRoundTrip()
    {
        using var directory = new TempDirectory();
        var left = Qa.Text("L") with { Role = MathTextRole.Identifier, StyleOverride = new MathStyleOverride(FontSize: 22) };
        var space = Space() with { StyleOverride = left.StyleOverride };
        var right = Qa.Text("R") with { Role = MathTextRole.Unit, StyleOverride = left.StyleOverride };
        var sequence = Qa.Sequence(left, space, right);
        var spacerRow = Row(sequence);
        var firstGroupRow = Row(Qa.Text("그룹 첫 행"));
        var otherRowText = Qa.Text("다른 행 caret");
        var otherRow = Row(otherRowText);
        var group = new EquationGroup(Guid.NewGuid(), [firstGroupRow, spacerRow, otherRow], AlignmentMode.None, 29)
        {
            StyleOverride = new MathStyleOverride(Foreground: "#80FF0000")
        };
        var secondGroupText = Qa.Text("다른 그룹");
        var secondGroupRow = Row(secondGroupText);
        var secondGroup = new EquationGroup(Guid.NewGuid(), [secondGroupRow], AlignmentMode.Equals, 33);
        var workspace = Workspace(group, secondGroup);

        var rowSession = new EditingSession(workspace);
        Qa.Caret(rowSession, otherRowText.Id, 2);
        Qa.Check(rowSession.TrySelectNode(space.Id), "다른 행 caret에서 spacer tree 선택");
        var rowBefore = CaptureState(rowSession);
        Qa.Check(rowSession.DuplicateCurrentRow(), "다른 행 caret 상태의 spacer 선택 row 복제");
        AssertClonedSelectedSpacer(rowSession, space, spacerRow.Id, group.Id, "행 복제");
        var rowCloneState = CaptureState(rowSession);
        rowSession.Undo();
        Qa.Equal(rowBefore.Fingerprint, Qa.Fingerprint(rowSession.Workspace), "행 복제 Undo 문서");
        Qa.Equal(rowBefore.StateId, rowSession.CaptureSnapshot().StateId, "행 복제 Undo StateId");
        rowSession.Redo();
        Qa.Equal(rowCloneState.Fingerprint, Qa.Fingerprint(rowSession.Workspace), "행 복제 Redo");

        var groupSession = new EditingSession(workspace);
        Qa.Caret(groupSession, secondGroupText.Id, 1);
        Qa.Check(groupSession.TrySelectNode(space.Id), "다른 행 caret에서 spacer tree 선택(그룹)");
        var groupBefore = CaptureState(groupSession);
        Qa.Check(groupSession.DuplicateCurrentGroup(), "다른 행 caret 상태의 spacer 선택 그룹 복제");
        var clonedGroup = groupSession.Workspace.Groups.Single(candidate => candidate.Id != group.Id && candidate.Id != secondGroup.Id);
        var clonedSpace = Qa.Nodes(groupSession.Workspace).OfType<MathSpaceNode>().Single(node => node.Id == groupSession.SelectedNodeId);
        Qa.Check(clonedSpace.Id != space.Id && clonedSpace.Kind == space.Kind, "복제 spacer ID와 Kind");
        Qa.Equal(space.StyleOverride, clonedSpace.StyleOverride, "복제 spacer 명시 스타일");
        Qa.Check(groupSession.IsNodeSelected && groupSession.SelectedNodeId == clonedSpace.Id,
            "그룹 복제 뒤 선택 ID가 복제 spacer를 가리키지 않음");
        Qa.Check(clonedGroup.Rows.SelectMany(row => Qa.Walk(row.Root)).Any(node => node.Id == clonedSpace.Id),
            "복제 spacer가 새 그룹 안에 없음");
        Qa.Check(Qa.Nodes(groupSession.Workspace).Select(node => node.Id).Distinct().Count() ==
                 Qa.Nodes(groupSession.Workspace).Count(), "그룹 복제 뒤 전역 node ID 중복");
        var caretText = Qa.Node(groupSession, groupSession.Caret.NodeId) as TextNode;
        var clonedSpaceRow = clonedGroup.Rows.Single(row => Qa.Walk(row.Root).Any(node => node.Id == clonedSpace.Id));
        Qa.Check(caretText is not null && Qa.Walk(clonedSpaceRow.Root).Any(node => node.Id == caretText.Id),
            "그룹 복제 fallback caret이 선택 spacer가 포함된 복제 행의 Text가 아님");
        var clonedGroupState = CaptureState(groupSession);
        groupSession.Undo();
        Qa.Equal(groupBefore.Fingerprint, Qa.Fingerprint(groupSession.Workspace), "그룹 복제 Undo 문서");
        groupSession.Redo();
        Qa.Equal(clonedGroupState.Fingerprint, Qa.Fingerprint(groupSession.Workspace), "그룹 복제 Redo 문서");

        var persistentRoot = Qa.Sequence(Qa.Text("a"), space, Qa.Text("b\u2009c"));
        var persistentSession = new EditingSession(Workspace(persistentRoot));
        var fileService = new WorkspaceFileService();
        var path = directory.File("thin-space-roundtrip.aethermath");
        var snapshot = persistentSession.CaptureSnapshot();
        await fileService.SaveAsync(path, snapshot);
        var reopened = await fileService.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(snapshot.Workspace), Qa.Fingerprint(reopened), "실제 파일에서 spacer·원문 U+2009 fingerprint");
        Qa.Equal(1, Qa.Nodes(reopened).OfType<MathSpaceNode>().Count(), "파일 왕복 spacer 종류/개수");
        Qa.Check(Qa.Nodes(reopened).OfType<TextNode>().Any(node => node.Text == "b\u2009c"),
            "파일 왕복 Text 내 U+2009 변환");

        var stateService = new WorkspaceStateService(directory.File("state"));
        var recoveryId = Guid.NewGuid();
        var recovery = new WorkspaceRecovery(recoveryId, snapshot, DateTimeOffset.UtcNow, path);
        Qa.Equal(RecoveryWriteStatus.Written, (await stateService.SaveRecoveryAsync(recovery)).Status,
            "실제 recovery 저장");
        var recovered = await stateService.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, recovered.Status, "실제 recovery 읽기 상태");
        Qa.Equal(Qa.Fingerprint(snapshot.Workspace), Qa.Fingerprint(recovered.Recovery!.Snapshot.Workspace),
            "recovery envelope에서 spacer·원문 U+2009 지문 보존");
    }

    private static void AssertClonedSelectedSpacer(EditingSession session, MathSpaceNode originalSpace,
        Guid originalRowId, Guid originalGroupId, string label)
    {
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId is Guid selectedId && selectedId != originalSpace.Id,
            label + " 뒤 복제 spacer가 선택되지 않거나 원래 ID를 재사용");
        var selected = Qa.Node(session, session.SelectedNodeId!.Value) as MathSpaceNode;
        Qa.Check(selected is not null, label + " 선택 ID가 MathSpace가 아님");
        Qa.Equal(originalSpace.Kind, selected!.Kind, label + " spacer 종류 보존");
        Qa.Equal(originalSpace.StyleOverride, selected.StyleOverride, label + " spacer 스타일 보존");
        Qa.Check(session.Workspace.Groups.SelectMany(group => group.Rows)
                .Any(row => row.Id != originalRowId && Qa.Walk(row.Root).Any(node => node.Id == selected.Id)),
            label + " 복제 spacer가 새 row에 존재하지 않음");
        Qa.Check(session.Workspace.Groups.Any(group => group.Id != originalGroupId &&
            group.Rows.Any(row => Qa.Walk(row.Root).Any(node => node.Id == selected.Id))) ||
            session.Workspace.Groups.Where(group => group.Id == originalGroupId)
                .SelectMany(group => group.Rows).Count(row => Qa.Walk(row.Root).Any(node => node.Id == selected.Id)) == 1,
            label + " 복제 spacer 위치가 올바르지 않음");
        Qa.Check(Qa.Node(session, session.Caret.NodeId) is TextNode, label + " 뒤 caret이 Text anchor가 아님");
        Qa.Equal(Qa.Nodes(session.Workspace).Count(), Qa.Nodes(session.Workspace).Select(node => node.Id).Distinct().Count(),
            label + " 뒤 전역 node ID가 중복됨");
    }

    private static void AssertSvgMatchesLayout(XDocument document, LayoutResult layout, Guid spacerId, string label)
    {
        var root = document.Root ?? throw new InvalidOperationException(label + " SVG root 없음");
        Qa.Near(layout.Width, Number(root, "width"), label + " SVG width", SvgTolerance);
        Qa.Near(layout.Height, Number(root, "height"), label + " SVG height", SvgTolerance);
        var viewBox = root.Attribute("viewBox")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Qa.Check(viewBox is { Length: 4 }, label + " SVG viewBox 형식");
        Qa.Near(layout.Width, viewBox![2], label + " viewBox 폭", SvgTolerance);
        Qa.Near(layout.Height, viewBox[3], label + " viewBox 높이", SvgTolerance);
        var elements = root.Descendants().Where(element =>
            element.Name.LocalName is "text" or "line" or "polyline" or "polygon").ToArray();
        Qa.Equal(layout.Primitives.Length, elements.Length, label + " SVG와 primitive 수");
        var layoutGlyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        var svgGlyphs = elements.Where(element => element.Name.LocalName == "text").ToArray();
        Qa.Check(layoutGlyphs.All(glyph => glyph.NodeId != spacerId), label + " Layout가 spacer에 glyph를 연결");
        Qa.Equal(layoutGlyphs.Length, svgGlyphs.Length, label + " SVG text glyph 수");
        for (var index = 0; index < layoutGlyphs.Length; index++)
        {
            var glyph = layoutGlyphs[index];
            var element = svgGlyphs[index];
            Qa.Equal(glyph.Text, element.Value, label + " glyph 원문");
            Qa.Near(glyph.BaselineOrigin.X, Number(element, "x"), label + " glyph x", SvgTolerance);
            Qa.Near(glyph.BaselineOrigin.Y, Number(element, "y"), label + " glyph baseline", SvgTolerance);
        }
    }

    private static double Number(XElement element, string attribute) =>
        double.Parse(element.Attribute(attribute)!.Value, System.Globalization.CultureInfo.InvariantCulture);

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string Mutate(string json, Action<JsonNode> mutate)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("fixture JSON이 비어 있음");
        mutate(node);
        return node.ToJsonString();
    }

    private static JsonObject FindSpaceJson(JsonNode root) => FindJsonObject(root, obj =>
        obj["$kind"]?.GetValue<string>() == "mathSpace");

    private static JsonObject FindTextJson(JsonNode root, Guid id) => FindJsonObject(root, obj =>
        obj["$kind"]?.GetValue<string>() == "text" &&
        Guid.TryParse(obj["id"]?.GetValue<string>(), out var candidateId) && candidateId == id);

    private static JsonObject FindRowJson(JsonNode root) => FindJsonObject(root, obj =>
        obj.ContainsKey("root") && obj["root"] is JsonObject);

    private static JsonObject FindJsonObject(JsonNode node, Func<JsonObject, bool> predicate)
    {
        if (node is JsonObject obj)
        {
            if (predicate(obj))
                return obj;
            foreach (var property in obj)
                if (property.Value is not null && FindJsonObjectOrNull(property.Value, predicate) is { } found)
                    return found;
        }
        if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null && FindJsonObjectOrNull(item, predicate) is { } found)
                    return found;
        throw new InvalidOperationException("JSON fixture에서 대상 객체를 찾지 못함");
    }

    private static JsonObject? FindJsonObjectOrNull(JsonNode node, Func<JsonObject, bool> predicate)
    {
        try { return FindJsonObject(node, predicate); }
        catch (InvalidOperationException) { return null; }
    }

    private static MathSpaceNode Space() => new(Guid.NewGuid(), MathSpaceKind.Thin);

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationWorkspace Workspace(params MathNode[] roots) => Qa.Workspace(roots);

    private static EquationWorkspace Workspace(EquationGroup first, params EquationGroup[] more) => new(
        Guid.NewGuid(), 1, "얇은 공백 QA",
        ImmutableArray.Create(first).AddRange(more),
        new MathStyle(30, "Deterministic", "#25364D"));

    private static EquationWorkspace WorkspaceWithStyle(MathNode root, double fontSize) => new(
        Guid.NewGuid(), 1, "얇은 공백 레이아웃",
        [new EquationGroup(Guid.NewGuid(), [Row(root)], AlignmentMode.None, 28)],
        new MathStyle(fontSize, "Deterministic", "#25364D"));

    private static SessionState CaptureState(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.Workspace, session.Revision, session.CaptureSnapshot().StateId,
        session.IsDirty, session.CanUndo, session.CanRedo, session.Caret, session.Selection,
        session.SelectedNodeId, session.IsNodeSelected,
        session.Workspace.Groups.SelectMany(group => group.Rows).ToImmutableDictionary(row => row.Id, row => row.Root.Id));

    private static void AssertUnchanged(SessionState before, EditingSession session, string label)
    {
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), label + " workspace");
        Qa.Equal(before.Revision, session.Revision, label + " revision");
        Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, label + " StateId");
        Qa.Equal(before.IsDirty, session.IsDirty, label + " dirty");
        Qa.Equal(before.CanUndo, session.CanUndo, label + " CanUndo");
        Qa.Equal(before.CanRedo, session.CanRedo, label + " CanRedo");
        Qa.Equal(before.Caret, session.Caret, label + " caret");
        Qa.Equal(before.Selection, session.Selection, label + " selection");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, label + " selected node");
        Qa.Equal(before.IsNodeSelected, session.IsNodeSelected, label + " whole-node selection");
    }

    private static void AssertDocumentHistoryUnchanged(SessionState before, EditingSession session, string label)
    {
        Qa.Equal(before.Fingerprint, Qa.Fingerprint(session.Workspace), label + " workspace");
        Qa.Equal(before.Revision, session.Revision, label + " revision");
        Qa.Equal(before.StateId, session.CaptureSnapshot().StateId, label + " StateId");
        Qa.Equal(before.IsDirty, session.IsDirty, label + " dirty");
        Qa.Equal(before.CanUndo, session.CanUndo, label + " CanUndo");
        Qa.Equal(before.CanRedo, session.CanRedo, label + " CanRedo");
    }

    private sealed record SessionState(string Fingerprint, EquationWorkspace Workspace, long Revision,
        Guid StateId, bool IsDirty, bool CanUndo, bool CanRedo, MathCaret Caret, MathSelection? Selection,
        Guid? SelectedNodeId, bool IsNodeSelected, ImmutableDictionary<Guid, Guid> RootIds);

    private sealed class FixedMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.56 : 0.5);
            var caretX = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                caretX.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.22, caretX.MoveToImmutable());
        }
    }

    private sealed class RecordingSvgExporter : ISvgExportService
    {
        private readonly SvgExportService _inner = new();
        private readonly List<LayoutResult> _layouts = [];
        public IReadOnlyList<LayoutResult> Layouts => _layouts;
        public int Calls { get; private set; }

        public async Task ExportAsync(string path, LayoutResult layout, CancellationToken cancellationToken = default)
        {
            Calls++;
            _layouts.Add(layout);
            await _inner.ExportAsync(path, layout, cancellationToken);
        }
    }
}
