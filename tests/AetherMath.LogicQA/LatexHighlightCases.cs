using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class LatexHighlightCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("LATEX-HIGHLIGHT-COVERAGE", "UTF-16 토큰 범위가 원문을 완전히 보존", CoverageAndUnicode);
        yield return new("LATEX-HIGHLIGHT-CONTEXT", "제어어·기호·주석·수식 기호를 분류", ContextClassification);
        yield return new("LATEX-HIGHLIGHT-REFRESH", "코드 변경 시 원문·토큰 스냅샷을 함께 갱신", RefreshPublication);
        yield return new("LATEX-HIGHLIGHT-PURITY", "강조 설정은 수식·진단·복사 판정을 바꾸지 않음", TogglePurity);
    }

    private static void CoverageAndUnicode()
    {
        var empty = LatexSyntaxTokenizer.Tokenize(string.Empty);
        Qa.Check(empty.IsEmpty, "빈 원문에 토큰이 생성됨");

        var sources = new[]
        {
            "",
            "x\\alpha 12.5\r\n",
            "𝑥 e\u0301 한글",
            "유효 surrogate: \U0001D465; 단독 상위: \uD800; 단독 하위: \uDC00; 끝",
            "\\% raw % comment\r\n다음 줄"
        };

        foreach (var source in sources)
        {
            var first = LatexSyntaxTokenizer.Tokenize(source);
            var second = LatexSyntaxTokenizer.Tokenize(source);
            Qa.Check(first.SequenceEqual(second), "같은 원문에서 토큰 범위가 결정적이지 않음");
            AssertCoverage(source, first, "UTF-16 범위 전체 포함");
        }

        var supplementary = "𝑥";
        var supplementaryRanges = LatexSyntaxTokenizer.Tokenize(supplementary);
        Qa.Equal(1, supplementaryRanges.Length, "보조평면 문자 토큰 수");
        Qa.Equal(2, supplementaryRanges[0].Length, "보조평면 문자를 쪼개지 않는 UTF-16 길이");
    }

    private static void ContextClassification()
    {
        var source = @"\alpha\beta\\\%\{x\} {r}&a_2^10 12.34 %raw" + "\r\nz";
        var tokens = LatexSyntaxTokenizer.Tokenize(source);
        AssertCoverage(source, tokens, "분류 fixture 범위 전체 포함");

        var commands = tokens.Where(token => token.Kind == LatexTokenKind.ControlWord)
            .Select(token => Slice(source, token)).ToArray();
        Qa.Check(commands.SequenceEqual(new[] { @"\alpha", @"\beta" }), "붙어 있는 제어어가 별도 범위로 나뉘지 않음");

        AssertSingleToken(source, tokens, @"\alpha", LatexTokenKind.ControlWord);
        AssertSingleToken(source, tokens, @"\beta", LatexTokenKind.ControlWord);
        AssertSingleToken(source, tokens, @"\%", LatexTokenKind.ControlSymbol);
        AssertSingleToken(source, tokens, @"\{", LatexTokenKind.ControlSymbol);
        AssertSingleToken(source, tokens, @"\}", LatexTokenKind.ControlSymbol);
        AssertSingleToken(source, tokens, "{", LatexTokenKind.Brace);
        AssertSingleToken(source, tokens, "}", LatexTokenKind.Brace);
        AssertSingleToken(source, tokens, "&", LatexTokenKind.Alignment);
        AssertSingleToken(source, tokens, "_", LatexTokenKind.Script);
        AssertSingleToken(source, tokens, "^", LatexTokenKind.Script);
        AssertSingleToken(source, tokens, "12", LatexTokenKind.Number);
        AssertSingleToken(source, tokens, "34", LatexTokenKind.Number);
        AssertSingleToken(source, tokens, "%raw", LatexTokenKind.Comment);
        Qa.Check(!tokens.Any(token => token.Kind == LatexTokenKind.Comment && Slice(source, token).Contains("\\%", StringComparison.Ordinal)),
            "escape된 퍼센트가 주석으로 분류됨");

        var comment = tokens.Single(token => token.Kind == LatexTokenKind.Comment);
        Qa.Equal("%raw".Length, comment.Length, "CRLF를 포함하지 않는 주석 범위");
        var crlf = tokens.Single(token => Slice(source, token) == "\r\n");
        Qa.Equal(LatexTokenKind.Whitespace, crlf.Kind, "주석 뒤 CRLF 공백 분류");
        AssertSingleToken(source, tokens, @"\\", LatexTokenKind.ControlSymbol);

        var unicodeControl = "\\" + "\U0001D465";
        var unicodeControlTokens = LatexSyntaxTokenizer.Tokenize(unicodeControl);
        Qa.Equal(1, unicodeControlTokens.Length, "유니코드 제어 기호 범위 수");
        Qa.Equal(LatexTokenKind.ControlSymbol, unicodeControlTokens[0].Kind, "유니코드 제어 기호 분류");
        Qa.Equal(3, unicodeControlTokens[0].Length, "제어 기호가 유효 surrogate pair를 보존");

        var trailingSlash = "x\\";
        var trailingTokens = LatexSyntaxTokenizer.Tokenize(trailingSlash);
        Qa.Equal(LatexTokenKind.ControlSymbol, trailingTokens[^1].Kind, "끝의 단독 역슬래시 분류");
        Qa.Equal(1, trailingTokens[^1].Length, "끝의 단독 역슬래시 범위 길이");
    }

    private static void RefreshPublication()
    {
        var vm = new MainWindowViewModel();
        Qa.Check(vm.IsLatexHighlightingEnabled, "강조 표시 기본값이 켜져 있지 않음");
        Qa.Equal(vm.LatexCode, vm.LatexSyntax.Source, "초기 스냅샷 원문 불일치");
        AssertCoverage(vm.LatexSyntax.Source, vm.LatexSyntax.Tokens, "초기 스냅샷 coverage");

        var oldSnapshot = vm.LatexSyntax;
        var oldSource = vm.LatexCode;
        var relevantNotifications = 0;
        var sawCodeNotification = false;
        var sawSyntaxNotification = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(MainWindowViewModel.LatexCode)
                or nameof(MainWindowViewModel.LatexSyntax)
                or nameof(MainWindowViewModel.LatexTextDiagnostics)
                or nameof(MainWindowViewModel.LatexLayoutDiagnostics)
                or nameof(MainWindowViewModel.LatexIncompleteSlotNodeIds)
                or nameof(MainWindowViewModel.CanExportLatex)
                or nameof(MainWindowViewModel.LatexCodeDiagnostic)))
                return;

            relevantNotifications++;
            if (args.PropertyName == nameof(MainWindowViewModel.LatexCode)) sawCodeNotification = true;
            if (args.PropertyName == nameof(MainWindowViewModel.LatexSyntax)) sawSyntaxNotification = true;
            AssertPublishedState(vm, args.PropertyName ?? "(속성 이름 없음)");
        };

        var text = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>()
            .First(node => !node.IsEmptySlot && node.Text.Length > 0);
        Qa.Caret(vm.Session, text.Id, text.Text.Length);
        Qa.Check(vm.Session.InsertText("𝑄"), "원문 변경 fixture 입력 실패");

        Qa.Check(!string.Equals(oldSource, vm.LatexCode, StringComparison.Ordinal), "편집 후 serializer 원문이 바뀌지 않음");
        Qa.Check(!ReferenceEquals(oldSnapshot, vm.LatexSyntax), "원문 변경 뒤 새 syntax 스냅샷이 발행되지 않음");
        Qa.Equal(vm.LatexCode, vm.LatexSyntax.Source, "갱신된 스냅샷 원문 불일치");
        AssertCoverage(vm.LatexSyntax.Source, vm.LatexSyntax.Tokens, "갱신된 스냅샷 coverage");
        Qa.Equal(oldSource, oldSnapshot.Source, "이전 immutable 스냅샷 원문이 바뀜");
        Qa.Check(LatexSyntaxTokenizer.Tokenize(oldSource).SequenceEqual(oldSnapshot.Tokens),
            "이전 immutable 스냅샷 토큰이 바뀜");
        Qa.Check(relevantNotifications > 0 && sawCodeNotification && sawSyntaxNotification,
            "원문 편집에서 코드·토큰 범위 알림이 함께 발행되지 않음");
    }

    private static async Task TogglePurity()
    {
        var vm = new MainWindowViewModel();
        var before = Capture(vm);
        var syntax = vm.LatexSyntax;
        var highlightingNotifications = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsLatexHighlightingEnabled))
                highlightingNotifications++;
        };
        Qa.Check(vm.CanExportLatex && vm.CanCopyLatex, "정상 복사 순수성 fixture가 허용 상태가 아님");
        Qa.Check(!vm.LatexLayoutDiagnostics.IsEmpty, "기본 fixture에 비차단 행 간격 안내가 없음");

        vm.IsLatexHighlightingEnabled = false;
        Qa.Check(!vm.IsLatexHighlightingEnabled, "강조 표시 끄기 상태가 반영되지 않음");
        Qa.Check(ReferenceEquals(syntax, vm.LatexSyntax), "표시 설정 전환이 토큰 스냅샷을 다시 생성함");
        vm.IsLatexHighlightingEnabled = true;
        Qa.Check(vm.IsLatexHighlightingEnabled, "강조 표시 켜기 상태가 반영되지 않음");
        Qa.Check(ReferenceEquals(syntax, vm.LatexSyntax), "표시 설정 복원이 원문/토큰 스냅샷을 바꿈");
        Qa.Equal(2, highlightingNotifications, "강조 표시 전환 속성 알림 횟수");
        AssertUnchanged(before, Capture(vm), "강조 전환 뒤 수식 상태");

        var calls = 0;
        string? copied = null;
        var result = await vm.CopyLatexAsync(source =>
        {
            calls++;
            copied = source;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Copied, result.Status, "강조 전환 뒤 테스트 작성기로 정상 복사");
        Qa.Equal(1, calls, "강조 전환 뒤 복사 호출 횟수");
        Qa.Equal(before.LatexCode, copied, "강조 전환 뒤 전체 원문 복사");
        AssertUnchanged(before, Capture(vm), "fake writer 복사 뒤 수식 상태");

        var blockedVm = new MainWindowViewModel();
        var controlledText = Qa.Nodes(blockedVm.Session.Workspace).OfType<TextNode>()
            .First(node => !node.IsEmptySlot && node.Text.Length > 0);
        Qa.Caret(blockedVm.Session, controlledText.Id, controlledText.Text.Length);
        Qa.Check(blockedVm.Session.InsertText("\u0001"), "차단 gate fixture 제어 문자 입력 실패");
        var wasExportable = blockedVm.CanExportLatex;
        var wasCopyable = blockedVm.CanCopyLatex;
        var blockedCode = blockedVm.LatexCode;
        var blockedDiagnostic = blockedVm.LatexCodeDiagnostic;
        var blockedSyntax = blockedVm.LatexSyntax;
        var blockedBeforeToggle = Capture(blockedVm);
        blockedVm.IsLatexHighlightingEnabled = false;
        Qa.Equal(wasExportable, blockedVm.CanExportLatex, "강조 전환이 export 차단 상태를 바꿈");
        Qa.Equal(wasCopyable, blockedVm.CanCopyLatex, "강조 전환이 copy 차단 상태를 바꿈");
        Qa.Equal(blockedCode, blockedVm.LatexCode, "강조 전환이 진단 원문을 바꿈");
        Qa.Equal(blockedDiagnostic, blockedVm.LatexCodeDiagnostic, "강조 전환이 진단 문구를 바꿈");
        Qa.Check(ReferenceEquals(blockedSyntax, blockedVm.LatexSyntax), "강조 전환이 차단 상태 토큰을 바꿈");
        AssertUnchanged(blockedBeforeToggle, Capture(blockedVm), "차단 gate에서 강조 전환 뒤 수식 상태");
        var blockedCalls = 0;
        var blocked = await blockedVm.CopyLatexAsync(_ =>
        {
            blockedCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Blocked, blocked.Status, "강조 전환 뒤 기존 진단 차단 유지");
        Qa.Equal(0, blockedCalls, "차단 상태에서 테스트 작성기 호출");
        AssertUnchanged(blockedBeforeToggle, Capture(blockedVm), "차단 복사 뒤 강조 설정 수식 상태");

        var incompleteVm = new MainWindowViewModel();
        Qa.Equal(WorkspaceActionRequestResult.Completed, incompleteVm.RequestNewWorkspace(), "필수 빈 슬롯 fixture 전환");
        Qa.Check(!incompleteVm.CanExportLatex && !incompleteVm.CanCopyLatex
            && !incompleteVm.LatexIncompleteSlotNodeIds.IsEmpty, "필수 빈 슬롯 차단 상태가 설정되지 않음");
        var incompleteBeforeToggle = Capture(incompleteVm);
        var incompleteSyntax = incompleteVm.LatexSyntax;
        incompleteVm.IsLatexHighlightingEnabled = false;
        Qa.Check(ReferenceEquals(incompleteSyntax, incompleteVm.LatexSyntax), "빈 슬롯 상태의 강조 전환이 스냅샷을 바꿈");
        AssertUnchanged(incompleteBeforeToggle, Capture(incompleteVm), "필수 빈 슬롯 상태의 강조 전환");
        var incompleteCalls = 0;
        var incompleteResult = await incompleteVm.CopyLatexAsync(_ =>
        {
            incompleteCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Blocked, incompleteResult.Status, "강조 전환 뒤 필수 빈 슬롯 차단 유지");
        Qa.Equal(0, incompleteCalls, "필수 빈 슬롯에서 테스트 작성기 호출");
        AssertUnchanged(incompleteBeforeToggle, Capture(incompleteVm), "필수 빈 슬롯 차단 복사 뒤 상태");
    }

    private static void AssertCoverage(string source, IReadOnlyList<LatexTokenRange> tokens, string label)
    {
        var position = 0;
        foreach (var token in tokens)
        {
            Qa.Check(token.Length > 0, label + ": 길이 0 토큰");
            Qa.Equal(position, token.Start, label + ": 범위 빈틈 또는 중첩");
            Qa.Check(token.Start >= 0 && token.Start + token.Length <= source.Length, label + ": 원문 범위를 벗어남");
            var end = token.Start + token.Length;
            Qa.Check(!SplitsValidSurrogatePair(source, token.Start) && !SplitsValidSurrogatePair(source, end),
                label + ": 유효 surrogate pair 내부 경계를 생성함");
            position = end;
        }

        Qa.Equal(source.Length, position, label + ": 원문 끝까지 덮지 않음");
        var restored = string.Concat(tokens.Select(token => source.Substring(token.Start, token.Length)));
        Qa.Equal(source, restored, label + ": 토큰 조각 연결이 원문 UTF-16 단위를 보존하지 않음");
    }

    private static bool SplitsValidSurrogatePair(string source, int boundary) =>
        boundary > 0 && boundary < source.Length
        && char.IsHighSurrogate(source[boundary - 1])
        && char.IsLowSurrogate(source[boundary]);

    private static void AssertSingleToken(string source, IReadOnlyList<LatexTokenRange> tokens,
        string slice, LatexTokenKind expectedKind)
    {
        var matches = tokens.Where(token => Slice(source, token) == slice).ToArray();
        Qa.Equal(1, matches.Length, "토큰 조각 개수: " + slice);
        Qa.Equal(expectedKind, matches[0].Kind, "토큰 분류: " + slice);
    }

    private static string Slice(string source, LatexTokenRange token) =>
        source.Substring(token.Start, token.Length);

    private static void AssertPublishedState(MainWindowViewModel vm, string label)
    {
        Qa.Equal(vm.LatexCode, vm.LatexSyntax.Source, label + " 알림 중 원문·토큰 스냅샷 일치");
        Qa.Check(LatexSyntaxTokenizer.Tokenize(vm.LatexCode).SequenceEqual(vm.LatexSyntax.Tokens),
            label + " 알림 중 최신 토큰 범위 일치");
        var workspace = vm.Session.Workspace;
        Qa.Check(LatexSerializer.GetTextDiagnostics(workspace).SequenceEqual(vm.LatexTextDiagnostics),
            label + " 알림 중 텍스트 진단 일치");
        Qa.Check(LatexSerializer.GetLayoutDiagnostics(workspace).SequenceEqual(vm.LatexLayoutDiagnostics),
            label + " 알림 중 배치 진단 일치");
        Qa.Check(LatexSerializer.GetIncompleteSlotNodeIds(workspace).SequenceEqual(vm.LatexIncompleteSlotNodeIds),
            label + " 알림 중 필수 슬롯 진단 일치");
        var canExport = vm.LatexTextDiagnostics.IsEmpty && vm.LatexIncompleteSlotNodeIds.IsEmpty;
        Qa.Equal(canExport, vm.CanExportLatex, label + " 알림 중 내보내기 허용 상태 일치");
        Qa.Equal(canExport, vm.CanCopyLatex, label + " 알림 중 복사 허용 상태 일치");
        Qa.Equal(LatexSerializer.ToLatex(workspace), vm.LatexCode, label + " 알림 중 serializer 원문 일치");
    }

    private static SessionState Capture(MainWindowViewModel vm)
    {
        var snapshot = vm.Session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(snapshot.Workspace), snapshot.Revision, snapshot.StateId,
            vm.Session.Caret, vm.Session.Selection, vm.Session.SelectedNodeId, vm.Session.IsNodeSelected,
            vm.Session.IsDirty, vm.Session.CanUndo, vm.Session.CanRedo, vm.LatexCode,
            vm.LatexCodeDiagnostic, vm.CanExportLatex, vm.CanCopyLatex,
            vm.LatexTextDiagnostics.ToArray(), vm.LatexLayoutDiagnostics.ToArray(),
            vm.LatexIncompleteSlotNodeIds.ToArray());
    }

    private static void AssertUnchanged(SessionState expected, SessionState actual, string label)
    {
        Qa.Equal(expected.Workspace, actual.Workspace, label + " 작업공간");
        Qa.Equal(expected.Revision, actual.Revision, label + " revision");
        Qa.Equal(expected.StateId, actual.StateId, label + " StateId");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " whole-node selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + " undo");
        Qa.Equal(expected.CanRedo, actual.CanRedo, label + " redo");
        Qa.Equal(expected.LatexCode, actual.LatexCode, label + " source");
        Qa.Equal(expected.Diagnostic, actual.Diagnostic, label + " 진단");
        Qa.Equal(expected.CanExport, actual.CanExport, label + " 내보내기 허용 상태");
        Qa.Equal(expected.CanCopy, actual.CanCopy, label + " 복사 허용 상태");
        Qa.Check(expected.TextDiagnostics.SequenceEqual(actual.TextDiagnostics), label + " 텍스트 진단");
        Qa.Check(expected.LayoutDiagnostics.SequenceEqual(actual.LayoutDiagnostics), label + " 배치 진단");
        Qa.Check(expected.IncompleteSlots.SequenceEqual(actual.IncompleteSlots), label + " 미완성 슬롯");
    }

    private sealed record SessionState(
        string Workspace,
        long Revision,
        Guid StateId,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo,
        string LatexCode,
        string Diagnostic,
        bool CanExport,
        bool CanCopy,
        LatexTextDiagnostic[] TextDiagnostics,
        LatexLayoutDiagnostic[] LayoutDiagnostics,
        Guid[] IncompleteSlots);
}
