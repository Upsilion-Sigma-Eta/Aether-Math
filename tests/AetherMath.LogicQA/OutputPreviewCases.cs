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

internal static class OutputPreviewCases
{
    private const double SvgCoordinateTolerance = 0.001;

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    public static IEnumerable<QaCase> All()
    {
        yield return new("OUT-PREVIEW-MODE", "Workspace/Display/Inline 투영과 그룹·행·노드 설정 보존", ModeProjection);
        yield return new("OUT-PREVIEW-PURITY", "미리보기와 Main VM 연결이 편집 상태를 변경하지 않음", PreviewPurity);
        yield return new("OUT-PREVIEW-REFRESH", "탐색·편집·모드·Session별 stale 및 명시 새로고침", RefreshAndStale);
        yield return new("OUT-PREVIEW-INCOMPLETE", "필수 슬롯 미완성 미리보기의 SVG 저장 차단", IncompletePreview);
        yield return new("OUT-SVG-EXACT", "동일 frozen LayoutResult의 SVG 실제 파일 내보내기", ExactSvgExport);
        yield return new("OUT-SVG-RACE", "busy 재진입과 오래된 내보내기 완료의 현재 상태 비침범", SvgExportRace);
        yield return new("OUT-SVG-CANCEL-FAIL", "취소·실패 파일 보존과 취소 무시 실제 성공 구분", SvgCancelFailureAndIgnoredCancel);
        yield return new("OUT-SVG-STALE-START", "stale preview와 picker 대기 중 낡은 snapshot의 I/O 차단", StaleExportStart);
    }

    private static void ModeProjection()
    {
        var plain = Qa.Text("plain");
        var groupText = Qa.Text("group-text");
        var rowScript = Qa.Text("row-script");
        var nodeScriptScript = Qa.Text("node-script-script") with
        {
            StyleOverride = Typesetting(MathTypesettingStyle.ScriptScript)
        };
        var sum = new SummationNode(Guid.NewGuid(), Qa.Text("f(n)"), Qa.Text("n=0"), Qa.Text("N"));
        var groups = ImmutableArray.Create(
            new EquationGroup(Guid.NewGuid(), [new EquationRow(Guid.NewGuid(), plain)], AlignmentMode.None, 24),
            new EquationGroup(Guid.NewGuid(), [new EquationRow(Guid.NewGuid(), groupText)], AlignmentMode.None, 24)
            {
                StyleOverride = Typesetting(MathTypesettingStyle.Text)
            },
            new EquationGroup(Guid.NewGuid(),
                [new EquationRow(Guid.NewGuid(), rowScript) { StyleOverride = Typesetting(MathTypesettingStyle.Script) }],
                AlignmentMode.None, 24)
            {
                StyleOverride = Typesetting(MathTypesettingStyle.Text)
            },
            new EquationGroup(Guid.NewGuid(),
                [new EquationRow(Guid.NewGuid(), nodeScriptScript) { StyleOverride = Typesetting(MathTypesettingStyle.Script) }],
                AlignmentMode.None, 24)
            {
                StyleOverride = Typesetting(MathTypesettingStyle.Text)
            },
            new EquationGroup(Guid.NewGuid(), [new EquationRow(Guid.NewGuid(), sum)], AlignmentMode.None, 24));
        var workspace = new EquationWorkspace(Guid.NewGuid(), 1, "Preview modes", groups,
            new MathStyle(32, "Deterministic", "#203040") { TypesettingStyle = MathTypesettingStyle.Text });
        var session = new EditingSession(workspace);
        var metrics = new DeterministicMetrics();
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, metrics, exporter);
        var before = Qa.Fingerprint(session.Workspace);
        var revision = session.Revision;
        var stateId = session.CaptureSnapshot().StateId;
        var signatures = new Dictionary<MathOutputMode, string>();
        var layouts = new Dictionary<MathOutputMode, LayoutResult>();

        foreach (var (mode, projectedStyle) in new[]
                 {
                     (MathOutputMode.WorkspaceStyle, MathTypesettingStyle.Text),
                     (MathOutputMode.Display, MathTypesettingStyle.Display),
                     (MathOutputMode.Inline, MathTypesettingStyle.Text)
                 })
        {
            preview.SelectedMode = mode;
            Qa.Check(preview.RefreshPreview(), mode + " preview refresh");
            var snapshot = preview.PreviewSnapshot ?? throw new InvalidOperationException("preview snapshot 없음");
            Qa.Equal(MathOutputPreviewState.Ready, preview.State, mode + " 상태");
            Qa.Equal(mode, snapshot.Mode, mode + " snapshot 모드");
            Qa.Equal(workspace.Id, snapshot.WorkspaceId, mode + " WorkspaceId");
            Qa.Equal(revision, snapshot.Revision, mode + " revision");
            Qa.Equal(stateId, snapshot.StateId, mode + " StateId");

            var expectedWorkspace = workspace with
            {
                Style = workspace.Style with { TypesettingStyle = projectedStyle }
            };
            var expected = MathLayoutEngine.Layout(expectedWorkspace, metrics,
                new MathLayoutOptions(FontSize: expectedWorkspace.Style.FontSize));
            var signature = LayoutSignature(snapshot.Layout);
            Qa.Equal(LayoutSignature(expected), signature, mode + " projected layout");
            signatures.Add(mode, signature);
            layouts.Add(mode, snapshot.Layout);
            AssertGlyphSize(snapshot.Layout, plain.Id, 32, mode + " root preset");
            AssertGlyphSize(snapshot.Layout, groupText.Id, 32, mode + " group Text override");
            AssertGlyphSize(snapshot.Layout, rowScript.Id, 22.72, mode + " row Script override");
            AssertGlyphSize(snapshot.Layout, nodeScriptScript.Id, 16.1312, mode + " node ScriptScript override");
        }

        Qa.Check(signatures[MathOutputMode.WorkspaceStyle] != signatures[MathOutputMode.Display],
            "WorkspaceStyle이 저장된 Text 조판을 Display로 바꾸거나 Display가 차이를 내지 않음");
        Qa.Equal(signatures[MathOutputMode.WorkspaceStyle], signatures[MathOutputMode.Inline],
            "저장된 Text와 Inline 출력 구조가 다름");
        AssertSummationModeGeometry(layouts[MathOutputMode.Display], sum, "Display");
        AssertSummationModeGeometry(layouts[MathOutputMode.Inline], sum, "Inline");

        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "mode 투영 원본 AST/설정");
        Qa.Equal(revision, session.Revision, "mode 투영 revision");
        Qa.Equal(stateId, session.CaptureSnapshot().StateId, "mode 투영 StateId");
        Qa.Check(!session.IsDirty && !session.CanUndo && !session.CanRedo, "mode 투영 dirty/history");
    }

    private static void PreviewPurity()
    {
        var leaf = Qa.Text("stable preview");
        var session = new EditingSession(Qa.Workspace(leaf));
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Caret(session, leaf.Id, 3);
        Qa.Select(session, leaf.Id, 1, 4);
        var beforeFingerprint = Qa.Fingerprint(session.Workspace);
        var beforeWorkspace = session.Workspace;
        var beforeSnapshot = session.CaptureSnapshot();
        var beforeCaret = session.Caret;
        var beforeSelection = session.Selection;
        var beforeNodeId = session.SelectedNodeId;
        var beforeNodeSelected = session.IsNodeSelected;
        var beforeCanUndo = session.CanUndo;
        var beforeCanRedo = session.CanRedo;
        var beforeDirty = session.IsDirty;
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), exporter);

        Qa.Check(preview.RefreshPreview(), "순수성 preview refresh 실패");
        var frozen = preview.PreviewSnapshot ?? throw new InvalidOperationException("preview snapshot 없음");
        preview.SelectedMode = MathOutputMode.Inline;
        Qa.Equal(MathOutputPreviewState.Stale, preview.State, "모드 변경 stale");
        Qa.Check(ReferenceEquals(frozen, preview.PreviewSnapshot), "모드 변경이 기존 frozen snapshot을 폐기함");
        Qa.Check(preview.RefreshPreview(), "Inline refresh 실패");
        Qa.Equal(beforeFingerprint, Qa.Fingerprint(session.Workspace), "preview 원본 workspace");
        Qa.Check(ReferenceEquals(beforeWorkspace, session.Workspace), "preview가 workspace 인스턴스를 바꿈");
        Qa.Equal(beforeCaret, session.Caret, "preview caret");
        Qa.Equal(beforeSelection, session.Selection, "preview selection");
        Qa.Equal(beforeNodeId, session.SelectedNodeId, "preview selected node");
        Qa.Equal(beforeNodeSelected, session.IsNodeSelected, "preview whole-node selection");
        Qa.Equal(beforeSnapshot.Revision, session.Revision, "preview revision");
        Qa.Equal(beforeSnapshot.StateId, session.CaptureSnapshot().StateId, "preview StateId");
        Qa.Equal(beforeCanUndo, session.CanUndo, "preview CanUndo");
        Qa.Equal(beforeCanRedo, session.CanRedo, "preview CanRedo");
        Qa.Equal(beforeDirty, session.IsDirty, "preview dirty");
        Qa.Equal(0, exporter.Calls, "preview 생성 중 exporter 호출");

        var mainExporter = new RecordingSvgExporter();
        var mainVm = new MainWindowViewModel(new WorkspaceFileService(), mainExporter);
        var stableChild = mainVm.OutputPreview;
        Qa.Check(!ReferenceEquals(mainVm.ShowOutputPreviewCommand, mainVm.ExportSvgCommand),
            "기존 picker SVG 저장 명령이 preview 진입 명령으로 대체됨");
        var mainSession = mainVm.Session;
        var mainBefore = Qa.Fingerprint(mainSession.Workspace);
        var mainSnapshot = mainSession.CaptureSnapshot();
        var mainCaret = mainSession.Caret;
        var mainSelection = mainSession.Selection;
        mainVm.ShowOutputPreviewCommand.Execute(null);
        Qa.Check(mainVm.IsOutputPreviewVisible, "preview 공개 command가 패널을 표시하지 않음");
        Qa.Check(ReferenceEquals(stableChild, mainVm.OutputPreview), "표시 전 child VM identity");
        Qa.Equal(mainBefore, Qa.Fingerprint(mainSession.Workspace), "표시 command AST");
        Qa.Equal(mainSnapshot.Revision, mainSession.Revision, "표시 command revision");
        Qa.Equal(mainSnapshot.StateId, mainSession.CaptureSnapshot().StateId, "표시 command StateId");
        Qa.Equal(mainCaret, mainSession.Caret, "표시 command caret");
        Qa.Equal(mainSelection, mainSession.Selection, "표시 command selection");
        Qa.Check(mainVm.RequestNewWorkspace() == WorkspaceActionRequestResult.Completed, "새 작업 fixture 전환");
        Qa.Check(ReferenceEquals(stableChild, mainVm.OutputPreview), "Session 교체 때 OutputPreview child identity 변경");
        Qa.Equal(MathOutputPreviewState.Stale, stableChild.State, "Session 교체 stale 상태");
        Qa.Check(stableChild.PreviewSnapshot is not null, "Session 교체가 이전 frozen preview를 없앰");
    }

    private static void RefreshAndStale()
    {
        var leaf = Qa.Text("navigation");
        var session = new EditingSession(Qa.Workspace(leaf));
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), new RecordingSvgExporter());
        Qa.Check(preview.RefreshPreview(), "초기 refresh");
        var first = preview.PreviewSnapshot!;
        Qa.Caret(session, leaf.Id, 2);
        Qa.Select(session, leaf.Id, 1, 3);
        Qa.Equal(MathOutputPreviewState.Ready, preview.State, "caret/selection 이동이 preview를 stale 처리");
        Qa.Check(ReferenceEquals(first, preview.PreviewSnapshot), "탐색이 frozen snapshot을 교체");

        Qa.Caret(session, leaf.Id, 3);
        Qa.Check(session.InsertText("!"), "편집 fixture");
        Qa.Equal(MathOutputPreviewState.Stale, preview.State, "편집 뒤 stale");
        Qa.Check(ReferenceEquals(first, preview.PreviewSnapshot), "편집이 마지막 frozen preview를 폐기");
        Qa.Check(preview.RefreshPreview(), "편집 후 명시 refresh");
        var afterEdit = preview.PreviewSnapshot!;
        Qa.Check(!ReferenceEquals(first, afterEdit), "명시 refresh가 snapshot을 갱신하지 않음");
        Qa.Equal(session.Revision, afterEdit.Revision, "refresh revision");
        Qa.Equal(session.CaptureSnapshot().StateId, afterEdit.StateId, "refresh StateId");

        preview.SelectedMode = MathOutputMode.Display;
        Qa.Equal(MathOutputPreviewState.Stale, preview.State, "mode 변경 stale");
        Qa.Check(ReferenceEquals(afterEdit, preview.PreviewSnapshot), "mode 변경이 이전 결과를 덮음");
        Qa.Check(preview.RefreshPreview(), "mode 변경 후 refresh");
        var afterMode = preview.PreviewSnapshot!;
        Qa.Equal(MathOutputMode.Display, afterMode.Mode, "mode refresh 결과");

        var replacementLeaf = Qa.Text("replacement session");
        var replacement = new EditingSession(Qa.Workspace(replacementLeaf));
        preview.SetSession(replacement);
        Qa.Equal(MathOutputPreviewState.Stale, preview.State, "Session 교체 stale");
        Qa.Check(ReferenceEquals(afterMode, preview.PreviewSnapshot), "Session 교체가 이전 frozen 결과를 교체");
        Qa.Check(preview.RefreshPreview(), "새 Session refresh");
        var afterSession = preview.PreviewSnapshot!;
        Qa.Equal(replacement.Workspace.Id, afterSession.WorkspaceId, "새 Session WorkspaceId");
        Qa.Equal(replacement.Revision, afterSession.Revision, "새 Session revision");
        Qa.Check(!ReferenceEquals(afterMode, afterSession), "새 Session refresh snapshot identity");

        var faultLeaf = Qa.Text("metrics failure");
        var faultSession = new EditingSession(Qa.Workspace(faultLeaf));
        var faultMetrics = new FaultInjectingMetrics();
        var faultPreview = new MathOutputPreviewViewModel(faultSession, faultMetrics, new RecordingSvgExporter());
        Qa.Check(faultPreview.RefreshPreview(), "측정 실패 경계 초기 refresh");
        var beforeFailure = faultPreview.PreviewSnapshot!;
        faultMetrics.NextFailure = new IOException("제어 가능한 측정 오류");
        Qa.Check(!faultPreview.RefreshPreview(), "측정 예외 refresh 성공으로 반환");
        Qa.Equal(MathOutputPreviewState.Failed, faultPreview.State, "측정 예외 상태");
        Qa.Check(ReferenceEquals(beforeFailure, faultPreview.PreviewSnapshot), "측정 실패가 마지막 frozen snapshot을 지움");
        Qa.Check(!faultPreview.CanExportSvg, "측정 실패 후 SVG export 가능");
        Qa.Check(!string.IsNullOrWhiteSpace(faultPreview.Diagnostic), "측정 실패 진단 없음");
        Qa.Check(faultPreview.RefreshPreview(), "측정 실패 후 명시 refresh 복구");
        Qa.Equal(MathOutputPreviewState.Ready, faultPreview.State, "측정 실패 복구 상태");
        Qa.Check(!ReferenceEquals(beforeFailure, faultPreview.PreviewSnapshot), "측정 복구 refresh가 snapshot을 갱신하지 않음");

        var reentrantLeaf = Qa.Text("reentrant metrics");
        var reentrantSession = new EditingSession(Qa.Workspace(reentrantLeaf));
        var reentrantMetrics = new FaultInjectingMetrics();
        var reentrantPreview = new MathOutputPreviewViewModel(reentrantSession, reentrantMetrics, new RecordingSvgExporter());
        Qa.Check(reentrantPreview.RefreshPreview(), "재진입 경계 초기 refresh");
        var beforeReentry = reentrantPreview.PreviewSnapshot!;
        reentrantMetrics.OnNextMeasure = () => reentrantPreview.SelectedMode = MathOutputMode.Inline;
        Qa.Check(!reentrantPreview.RefreshPreview(), "측정 중 mode 변경 후보를 publish함");
        Qa.Equal(MathOutputPreviewState.Stale, reentrantPreview.State, "측정 재진입 뒤 상태");
        Qa.Check(ReferenceEquals(beforeReentry, reentrantPreview.PreviewSnapshot), "측정 재진입이 이전 snapshot을 교체");
        Qa.Check(reentrantPreview.RefreshPreview(), "재진입 이후 명시 refresh");
        Qa.Equal(MathOutputMode.Inline, reentrantPreview.PreviewSnapshot!.Mode, "재진입 후 refresh mode");
    }

    private static async Task IncompletePreview()
    {
        using var directory = new TempDirectory();
        var requiredEmpty = Qa.Text("");
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("x"), requiredEmpty);
        var session = new EditingSession(Qa.Workspace(fraction));
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), exporter);

        Qa.Check(preview.RefreshPreview(), "미완성 preview refresh");
        Qa.Equal(MathOutputPreviewState.Incomplete, preview.State, "빈 필수 슬롯 상태");
        Qa.Check(!preview.CanExportSvg, "미완성 preview 저장 허용");
        Qa.Check(preview.PreviewSnapshot!.Layout.Diagnostics.Any(diagnostic =>
            diagnostic.Code == LayoutDiagnosticCode.EmptySlot && diagnostic.NodeId == requiredEmpty.Id), "빈 슬롯 진단");
        var path = directory.File("incomplete.svg");
        var result = await preview.ExportSvgToPathAsync(path);
        Qa.Equal(SvgPreviewExportStatus.Incomplete, result.Status, "미완성 export 상태");
        Qa.Equal(0, exporter.Calls, "미완성 시 exporter 호출");
        Qa.Check(!File.Exists(path), "미완성 output 파일 생성");
        Qa.Equal(0, Directory.GetFileSystemEntries(directory.Path).Length, "미완성 output 임시 산출물");
    }

    private static async Task ExactSvgExport()
    {
        using var directory = new TempDirectory();
        var leaf = Qa.Text("frozen 𝑥 + e\u0301");
        var fraction = new FractionNode(Guid.NewGuid(), leaf, Qa.Text("den"));
        var session = new EditingSession(Qa.Workspace(fraction));
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), exporter);
        Qa.Check(preview.RefreshPreview(), "exact export refresh");
        var snapshot = preview.PreviewSnapshot!;
        var path = directory.File("exact.svg");

        var result = await preview.ExportSvgToPathAsync(path);
        Qa.Equal(SvgPreviewExportStatus.Saved, result.Status, "exact export status");
        Qa.Equal(1, exporter.Calls, "exact export 호출 수");
        Qa.Check(ReferenceEquals(snapshot.Layout, exporter.Layouts.Single()), "exporter에 frozen LayoutResult가 아닌 다른 참조 전달");
        Qa.Equal(Path.GetFullPath(path), Path.GetFullPath(result.Path!), "export 경로");
        var xmlText = await File.ReadAllTextAsync(path);
        var expected = new SvgExportService().ToSvg(snapshot.Layout);
        Qa.Equal(expected, xmlText, "파일 SVG가 frozen LayoutResult의 실제 XML과 다름");
        var root = XDocument.Parse(xmlText).Root ?? throw new InvalidOperationException("SVG root 없음");
        Qa.Equal("svg", root.Name.LocalName, "SVG 루트");
        Qa.Near(snapshot.Layout.Width, ParseSvgNumber(root.Attribute("width")?.Value, "width"), "SVG root width", SvgCoordinateTolerance);
        Qa.Near(snapshot.Layout.Height, ParseSvgNumber(root.Attribute("height")?.Value, "height"), "SVG root height", SvgCoordinateTolerance);
        var viewBox = root.Attribute("viewBox")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray()
            ?? throw new InvalidOperationException("SVG viewBox 없음");
        Qa.Equal(4, viewBox.Length, "SVG viewBox 항목 수");
        Qa.Near(0, viewBox[0], "SVG viewBox x", SvgCoordinateTolerance);
        Qa.Near(0, viewBox[1], "SVG viewBox y", SvgCoordinateTolerance);
        Qa.Near(snapshot.Layout.Width, viewBox[2], "SVG viewBox width", SvgCoordinateTolerance);
        Qa.Near(snapshot.Layout.Height, viewBox[3], "SVG viewBox height", SvgCoordinateTolerance);
        Qa.Equal(string.Concat(snapshot.Layout.Primitives.OfType<GlyphPrimitive>().Select(glyph => glyph.Text)),
            string.Concat(root.Descendants().Where(element => element.Name.LocalName == "text").Select(element => element.Value)),
            "SVG glyph 원문/순서");
        var expectedLines = snapshot.Layout.Primitives.OfType<LinePrimitive>().ToArray();
        var actualLines = root.Descendants().Where(element => element.Name.LocalName == "line").ToArray();
        Qa.Check(expectedLines.Length > 0, "SVG geometry fixture에 비교 가능한 fraction bar line이 없음");
        Qa.Equal(expectedLines.Length, actualLines.Length, "SVG line primitive 수");
        for (var index = 0; index < expectedLines.Length; index++)
        {
            var expectedLine = expectedLines[index];
            var actualLine = actualLines[index];
            Qa.Near(expectedLine.Start.X, ParseSvgNumber(actualLine.Attribute("x1")?.Value, "x1"), "SVG line x1", SvgCoordinateTolerance);
            Qa.Near(expectedLine.Start.Y, ParseSvgNumber(actualLine.Attribute("y1")?.Value, "y1"), "SVG line y1", SvgCoordinateTolerance);
            Qa.Near(expectedLine.End.X, ParseSvgNumber(actualLine.Attribute("x2")?.Value, "x2"), "SVG line x2", SvgCoordinateTolerance);
            Qa.Near(expectedLine.End.Y, ParseSvgNumber(actualLine.Attribute("y2")?.Value, "y2"), "SVG line y2", SvgCoordinateTolerance);
            Qa.Near(expectedLine.StrokeWidth, ParseSvgNumber(actualLine.Attribute("stroke-width")?.Value, "stroke-width"), "SVG line stroke width", SvgCoordinateTolerance);
            Qa.Equal(expectedLine.Stroke, actualLine.Attribute("stroke")?.Value, "SVG line stroke");
        }
        Qa.Check(preview.LastExportSummary.Contains("exact.svg", StringComparison.Ordinal), "성공 저장 요약 경로");
    }

    private static async Task SvgExportRace()
    {
        using var directory = new TempDirectory();
        var leaf = Qa.Text("race");
        var session = new EditingSession(Qa.Workspace(leaf));
        var exporter = new RecordingSvgExporter();
        var firstGate = exporter.GateNext();
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), exporter);
        Qa.Check(preview.RefreshPreview(), "race 최초 refresh");
        var firstSnapshot = preview.PreviewSnapshot!;
        var firstPath = directory.File("old-preview.svg");
        var firstExport = preview.ExportSvgToPathAsync(firstPath);
        await firstGate.WaitUntilStartedAsync();
        MathOutputPreviewSnapshot refreshedSnapshot;
        string summaryBeforeLateCompletion;
        try
        {
            var busy = await preview.ExportSvgToPathAsync(directory.File("busy.svg"));
            Qa.Equal(SvgPreviewExportStatus.Busy, busy.Status, "동시 export 재진입");
            Qa.Equal(1, exporter.Calls, "Busy 재진입이 exporter를 추가 호출");
            Qa.Check(!File.Exists(directory.File("busy.svg")), "Busy 재진입 파일 생성");

            Qa.Caret(session, leaf.Id, 2);
            Qa.Check(session.InsertText("!"), "race 중 편집");
            Qa.Equal(MathOutputPreviewState.Stale, preview.State, "race 중 편집 stale");
            preview.SelectedMode = MathOutputMode.Inline;
            Qa.Check(preview.RefreshPreview(), "race 중 새 snapshot refresh");
            refreshedSnapshot = preview.PreviewSnapshot!;
            summaryBeforeLateCompletion = preview.LastExportSummary;
            Qa.Check(!ReferenceEquals(firstSnapshot, refreshedSnapshot), "race refresh snapshot이 교체되지 않음");
            Qa.Equal(MathOutputPreviewState.Ready, preview.State, "새 refresh 완료 후 상태");
        }
        finally
        {
            firstGate.Release();
            try { await firstExport.WaitAsync(WaitLimit); } catch { }
        }
        var firstResult = await firstExport.WaitAsync(WaitLimit);
        Qa.Equal(SvgPreviewExportStatus.SavedStale, firstResult.Status, "refresh 이후 이전 export 결과");
        Qa.Check(File.Exists(firstPath), "오래된 frozen output은 저장 완료돼야 함");
        Qa.Equal(new SvgExportService().ToSvg(firstSnapshot.Layout), await File.ReadAllTextAsync(firstPath),
            "오래된 export가 새 layout로 파일을 씀");
        Qa.Check(ReferenceEquals(refreshedSnapshot, preview.PreviewSnapshot), "늦은 완료가 현재 snapshot을 덮음");
        Qa.Equal(MathOutputPreviewState.Ready, preview.State, "늦은 완료가 현재 Ready 상태를 바꿈");
        Qa.Equal(summaryBeforeLateCompletion, preview.LastExportSummary, "SavedStale가 현재 export 요약을 덮음");

        var secondGate = exporter.GateNext();
        var replacementPath = directory.File("replaced-session.svg");
        var secondExport = preview.ExportSvgToPathAsync(replacementPath);
        await secondGate.WaitUntilStartedAsync();
        MathOutputPreviewSnapshot? replacementSnapshotBeforeCompletion;
        MathOutputPreviewState replacementStateBeforeCompletion;
        string replacementSummaryBeforeCompletion;
        try
        {
            var replacement = new EditingSession(Qa.Workspace(Qa.Text("new owner")));
            preview.SetSession(replacement);
            replacementSnapshotBeforeCompletion = preview.PreviewSnapshot;
            replacementStateBeforeCompletion = preview.State;
            replacementSummaryBeforeCompletion = preview.LastExportSummary;
        }
        finally
        {
            secondGate.Release();
            try { await secondExport.WaitAsync(WaitLimit); } catch { }
        }
        var secondResult = await secondExport.WaitAsync(WaitLimit);
        Qa.Equal(SvgPreviewExportStatus.SavedStale, secondResult.Status, "Session 교체 중 export 완료");
        Qa.Check(ReferenceEquals(replacementSnapshotBeforeCompletion, preview.PreviewSnapshot), "오래된 export가 Session 교체 후 snapshot 침범");
        Qa.Equal(replacementStateBeforeCompletion, preview.State, "오래된 export가 교체 후 상태 침범");
        Qa.Equal(replacementSummaryBeforeCompletion, preview.LastExportSummary, "오래된 export가 Session 교체 후 요약 침범");
        Qa.Check(File.Exists(replacementPath), "Session 교체 시 이미 시작한 export 결과 보존");
    }

    private static async Task SvgCancelFailureAndIgnoredCancel()
    {
        using var directory = new TempDirectory();
        var session = new EditingSession(Qa.Workspace(Qa.Text("cancel/failure")));
        var realExporter = new SvgExportService();
        var realPreview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), realExporter);
        Qa.Check(realPreview.RefreshPreview(), "real exporter preview refresh");

        var existingPath = directory.File("existing.svg");
        await File.WriteAllTextAsync(existingPath, "prior valid bytes");
        var originalBytes = await File.ReadAllBytesAsync(existingPath);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var canceledResult = await realPreview.ExportSvgToPathAsync(existingPath, canceled.Token);
            Qa.Equal(SvgPreviewExportStatus.Canceled, canceledResult.Status, "실제 SVG service 취소 결과");
        }
        var afterCanceledBytes = await File.ReadAllBytesAsync(existingPath);
        Qa.Check(originalBytes.SequenceEqual(afterCanceledBytes), "취소된 실제 SVG 저장이 이전 파일을 덮음");
        Qa.Equal(1, Directory.GetFileSystemEntries(directory.Path).Length, "취소 후 SVG 임시파일 정리");

        var lockPath = directory.File("locked.svg");
        await File.WriteAllTextAsync(lockPath, "locked prior bytes");
        var lockedBytes = await File.ReadAllBytesAsync(lockPath);
        SvgPreviewExportResult failureResult;
        using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failureResult = await realPreview.ExportSvgToPathAsync(lockPath);
        }
        Qa.Equal(SvgPreviewExportStatus.Failed, failureResult.Status, "실제 atomic replace 실패 결과");
        var afterFailureBytes = await File.ReadAllBytesAsync(lockPath);
        Qa.Check(lockedBytes.SequenceEqual(afterFailureBytes), "실패한 실제 SVG 저장이 이전 파일을 덮음");
        Qa.Equal(2, Directory.GetFileSystemEntries(directory.Path).Length, "실패 후 SVG 임시파일 정리");

        var ignoredExporter = new RecordingSvgExporter();
        var ignoreGate = ignoredExporter.GateNext(ignoreCancellation: true);
        var ignoredPreview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), ignoredExporter);
        Qa.Check(ignoredPreview.RefreshPreview(), "취소 무시 preview refresh");
        using var cancellation = new CancellationTokenSource();
        var successfulPath = directory.File("cancel-ignored-but-saved.svg");
        var ignoredExport = ignoredPreview.ExportSvgToPathAsync(successfulPath, cancellation.Token);
        await ignoreGate.WaitUntilStartedAsync();
        cancellation.Cancel();
        ignoreGate.Release();
        var ignoredResult = await ignoredExport.WaitAsync(WaitLimit);
        Qa.Equal(SvgPreviewExportStatus.Saved, ignoredResult.Status, "실제 성공한 취소 무시 exporter를 Canceled로 잘못 표시");
        Qa.Check(File.Exists(successfulPath), "취소 무시 실제 성공 파일 없음");
        var successfulXml = await File.ReadAllTextAsync(successfulPath);
        Qa.Equal(new SvgExportService().ToSvg(ignoredPreview.PreviewSnapshot!.Layout), successfulXml,
            "취소 무시 성공 파일 내용");
    }

    private static async Task StaleExportStart()
    {
        using var directory = new TempDirectory();
        var leaf = Qa.Text("stale export");
        var session = new EditingSession(Qa.Workspace(leaf));
        var exporter = new RecordingSvgExporter();
        var preview = new MathOutputPreviewViewModel(session, new DeterministicMetrics(), exporter);
        Qa.Check(preview.RefreshPreview(), "stale start 초기 refresh");
        var original = preview.PreviewSnapshot!;
        Qa.Caret(session, leaf.Id, 1);
        Qa.Check(session.InsertText("!"), "stale start 편집");
        var stalePath = directory.File("stale-start.svg");
        var staleResult = await preview.ExportSvgToPathAsync(stalePath);
        Qa.Equal(SvgPreviewExportStatus.Stale, staleResult.Status, "stale 상태 export 시작");
        Qa.Equal(0, exporter.Calls, "stale 상태에서 exporter 호출");
        Qa.Check(!File.Exists(stalePath), "stale 상태에서 output 파일 생성");

        Qa.Check(preview.RefreshPreview(), "stale expected snapshot 새 refresh");
        var current = preview.PreviewSnapshot!;
        var state = preview.State;
        var diagnostic = preview.Diagnostic;
        var summary = preview.LastExportSummary;
        var pickerPath = directory.File("picker-old-snapshot.svg");
        var pickerResult = await preview.ExportSvgToPathAsync(pickerPath, original);
        Qa.Equal(SvgPreviewExportStatus.Stale, pickerResult.Status, "picker 대기 뒤 오래된 expected snapshot");
        Qa.Check(ReferenceEquals(current, preview.PreviewSnapshot), "낡은 picker snapshot이 새 preview를 교체");
        Qa.Equal(state, preview.State, "낡은 picker snapshot이 현재 상태를 오염");
        Qa.Equal(diagnostic, preview.Diagnostic, "낡은 picker snapshot이 현재 진단을 오염");
        Qa.Equal(summary, preview.LastExportSummary, "낡은 picker snapshot이 현재 export 요약을 오염");
        Qa.Equal(0, exporter.Calls, "낡은 picker snapshot에서 exporter 호출");
        Qa.Check(!File.Exists(pickerPath), "낡은 picker snapshot에서 파일 생성");
        Qa.Equal(0, Directory.GetFileSystemEntries(directory.Path).Length, "stale 시작 I/O 산출물");
    }

    private static void AssertGlyphSize(LayoutResult layout, Guid nodeId, double expected, string label)
    {
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>()
            .Where(primitive => primitive.NodeId == nodeId)
            .ToArray();
        Qa.Check(glyphs.Length > 0, label + " glyph 없음");
        for (var index = 0; index < glyphs.Length; index++)
            Qa.Near(expected, glyphs[index].FontSize, $"{label} glyph {index + 1}/{glyphs.Length}");
    }

    private static void AssertSummationModeGeometry(LayoutResult layout, SummationNode sum, string mode)
    {
        var operatorGlyph = layout.Primitives.OfType<GlyphPrimitive>().Single(primitive =>
            primitive.NodeId == sum.Id && primitive.Text == "∑");
        var operatorWidth = operatorGlyph.FontSize * 0.5;
        var lower = layout.GetBounds(sum.LowerLimit!.Id) ?? throw new InvalidOperationException(mode + " lower bounds 없음");
        var upper = layout.GetBounds(sum.UpperLimit!.Id) ?? throw new InvalidOperationException(mode + " upper bounds 없음");
        if (mode == "Display")
        {
            var operatorCenter = operatorGlyph.BaselineOrigin.X + operatorWidth / 2;
            Qa.Near(operatorCenter, lower.X + lower.Width / 2, "Display sum lower center");
            Qa.Near(operatorCenter, upper.X + upper.Width / 2, "Display sum upper center");
            Qa.Check(lower.Y > operatorGlyph.BaselineOrigin.Y, "Display sum lower가 연산자 아래에 배치되지 않음");
            Qa.Check(upper.Bottom < operatorGlyph.BaselineOrigin.Y, "Display sum upper가 연산자 위에 배치되지 않음");
        }
        else
        {
            Qa.Check(lower.X > operatorGlyph.BaselineOrigin.X + operatorWidth, "Inline sum lower가 연산자 옆에 배치되지 않음");
            Qa.Check(upper.X > operatorGlyph.BaselineOrigin.X + operatorWidth, "Inline sum upper가 연산자 옆에 배치되지 않음");
        }
    }

    private static double ParseSvgNumber(string? value, string attribute) =>
        double.Parse(value ?? throw new InvalidOperationException("SVG 숫자 속성 없음: " + attribute), CultureInfo.InvariantCulture);

    private static MathStyleOverride Typesetting(MathTypesettingStyle style) => new() { TypesettingStyle = style };

    private static string LayoutSignature(LayoutResult layout)
    {
        var primitives = layout.Primitives.Select(primitive => primitive switch
        {
            GlyphPrimitive glyph => new PrimitiveShape("glyph", glyph.NodeId, glyph.Text,
                glyph.BaselineOrigin.X, glyph.BaselineOrigin.Y, glyph.FontSize, glyph.FontFamily, glyph.Fill,
                glyph.Italic, null, null, null, null, null, null, null, null),
            LinePrimitive line => new PrimitiveShape("line", null, null,
                line.Start.X, line.Start.Y, null, null, null, null,
                line.End.X, line.End.Y, line.StrokeWidth, line.Stroke, null, null, null, null),
            PolylinePrimitive polyline => new PrimitiveShape("polyline", null, null,
                null, null, null, null, null, null,
                null, null, polyline.StrokeWidth, polyline.Stroke,
                string.Join(";", polyline.Points.Select(point => FormattableString.Invariant($"{point.X:R},{point.Y:R}"))),
                polyline.Closed, null, null),
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
        return System.Text.Json.JsonSerializer.Serialize(new { layout.Width, layout.Height, Primitives = primitives, Nodes = nodes });
    }

    private sealed record PrimitiveShape(string Kind, Guid? NodeId, string? Text,
        double? X1, double? Y1, double? FontSize, string? FontFamily, string? Fill, bool? Italic,
        double? X2, double? Y2, double? StrokeWidth, string? Stroke, string? Points, bool? Closed,
        string? Unused1, string? Unused2);

    private sealed class DeterministicMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.53 : 0.5);
            var carets = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                carets.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.23, carets.MoveToImmutable());
        }
    }

    private sealed class FaultInjectingMetrics : IMathTextMetrics
    {
        private readonly DeterministicMetrics _inner = new();

        public Exception? NextFailure { get; set; }
        public Action? OnNextMeasure { get; set; }

        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var failure = NextFailure;
            NextFailure = null;
            var callback = OnNextMeasure;
            OnNextMeasure = null;
            callback?.Invoke();
            if (failure is not null)
                throw failure;
            return _inner.Measure(text, fontFamily, fontSize, italic);
        }
    }

    private sealed class RecordingSvgExporter : ISvgExportService
    {
        private readonly SvgExportService _inner = new();
        private readonly Queue<ExportGate> _gates = new();
        private readonly List<LayoutResult> _layouts = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public IReadOnlyList<LayoutResult> Layouts => _layouts;

        public ExportGate GateNext(bool ignoreCancellation = false)
        {
            var gate = new ExportGate(ignoreCancellation);
            lock (_gates)
                _gates.Enqueue(gate);
            return gate;
        }

        public async Task ExportAsync(string path, LayoutResult layout, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _layouts.Add(layout);
            ExportGate? gate = null;
            lock (_gates)
                if (_gates.Count > 0)
                    gate = _gates.Dequeue();
            if (gate is not null)
            {
                gate.SignalStarted();
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (gate.IgnoreCancellation)
                    cancellationToken = CancellationToken.None;
            }
            await _inner.ExportAsync(path, layout, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ExportGate(bool ignoreCancellation)
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IgnoreCancellation { get; } = ignoreCancellation;
        public void SignalStarted() => _started.TrySetResult();
        public void Release() => _released.TrySetResult();
        public Task WaitUntilStartedAsync() => _started.Task.WaitAsync(WaitLimit);
        public Task WaitAsync(CancellationToken cancellationToken) =>
            _released.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken);
    }
}
