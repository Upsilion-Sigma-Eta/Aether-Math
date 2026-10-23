using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class PaletteUiInteractionCases
{
    private const string CaseId = "PALETTE-UI";
    private const string ArtifactScope = "p2-symbol-palette";
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "기호·Unicode·이모지 찾아보기의 세션 불변성, 명시 삽입과 undo, 두 크기 실제 셸의 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 기호·문자 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("기호·문자 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        try
        {
            await RunWindowChecksAsync();
            Console.WriteLine("PASS " + CaseId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {CaseId}\n{exception}");
            return 1;
        }
        finally
        {
            await CloseOwnedWindowAsync(_window);
            _window = null;
        }
    }

    private static async Task RunHeadlessProcessAsync()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 프로세스 실행 경로가 없습니다.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("dotnet host의 QA 어셈블리 경로를 찾을 수 없습니다.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add("--palette-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("기호·문자 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("기호·문자 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"기호·문자 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("기호·문자 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow(viewModel)
        {
            Width = 1060,
            Height = 700,
            CanResize = true
        };
        _window = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await DrainLayoutAsync(window);

        var session = viewModel.Session;
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(session, editor.Session), "초기 MainWindow 수식 편집기가 현재 Session을 사용하지 않습니다.");
        var initialLeafId = session.Caret.NodeId;
        var initialLeafText = Qa.Leaf(session, initialLeafId).Text;
        Qa.Check(session.TrySetCaret(new MathCaret(initialLeafId, Qa.Elements(initialLeafText))),
            "UI palette fixture caret를 현재 텍스트 끝으로 이동하지 못했습니다.");
        AssertTemplateCommandProtection(window, viewModel);
        AssertPaletteCommandBindings(window, viewModel);
        Qa.Check(Find<TextBlock>(window, "UnicodePageIndicator").Text == FormatPage(0, 8688),
            "유니코드 페이지 표시가 1부터 시작하는 UI 형식과 일치하지 않습니다.");

        var browsingBaseline = CaptureSession(session);
        await CaptureAllModesAsync(window, "minimum");
        Qa.Check(ReferenceEquals(session, viewModel.Session), "최소 크기 캡처 중 탐색 도구가 Session을 교체했습니다.");
        AssertSessionUnchanged(browsingBaseline, session, "최소 크기 셸 캡처");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        await CaptureAllModesAsync(window, "default");
        AssertSessionUnchanged(browsingBaseline, session, "기본 크기 셸 캡처");

        await SelectSymbolsTabAsync(window);
        var palette = viewModel.SymbolPalette;
        var categoryBox = Find<ComboBox>(window, "SymbolCategoryComboBox");
        categoryBox.SelectedValue = MathSymbolCategory.GreekLetters;
        await DrainLayoutAsync(window);
        Qa.Equal(MathSymbolCategory.GreekLetters, palette.SelectedCategory, "기호 분류의 실제 ComboBox 선택 바인딩");
        await ReplaceTextAsync(window, Find<TextBox>(window, "SymbolSearchBox"), "pi");
        Qa.Check(palette.Entries.Any(entry => entry.Id == "symbol.greek.pi"),
            "검색 UI에서 pi 별칭으로 그리스 문자 π를 찾지 못했습니다.");
        AssertSessionUnchanged(browsingBaseline, session, "기호 검색");

        await ReplaceTextAsync(window, Find<TextBox>(window, "SymbolSearchBox"), "no-matching-palette-entry-7f3a");
        Qa.Check(palette.Entries.IsEmpty && Find<ListBox>(window, "SymbolResultsList").Items.Count == 0,
            "일치 항목이 없는 검색 결과가 비어 있지 않습니다.");
        AssertSessionUnchanged(browsingBaseline, session, "빈 기호 검색 결과");

        await ReplaceTextAsync(window, Find<TextBox>(window, "SymbolSearchBox"), string.Empty);
        var pi = MathSymbolCatalog.Find("symbol.greek.pi")
            ?? throw new InvalidOperationException("그리스 문자 π 팔레트 fixture를 찾지 못했습니다.");
        var symbolList = Find<ListBox>(window, "SymbolResultsList");
        symbolList.SelectedItem = pi;
        await DrainLayoutAsync(window);
        Qa.Equal(pi.Id, palette.SelectedPaletteEntry?.Id, "기호 목록 선택이 상세 선택에 반영되지 않았습니다.");
        var symbolInsert = Find<Button>(window, "InsertSelectedSymbolButton");
        Qa.Check(symbolInsert.IsEffectivelyEnabled, "기호 선택 후 삽입 버튼이 활성화되지 않았습니다.");
        Qa.Equal(pi.Id, symbolInsert.CommandParameter as string, "기호 삽입 버튼의 선택 ID 매개변수");
        AssertSessionUnchanged(browsingBaseline, session, "기호 선택");

        var originalLeafId = session.Caret.NodeId;
        var originalLeafText = Qa.Leaf(session, originalLeafId).Text;
        var originalCaret = session.Caret;
        var originalFingerprint = Qa.Fingerprint(session.Workspace);
        var originalStateId = session.CaptureSnapshot().StateId;
        var priorRevision = session.Revision;
        await ClickAsync(window, symbolInsert);
        await DrainLayoutAsync(window);
        Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
            "기호 삽입이 현재 Session 또는 editor 바인딩을 교체했습니다.");
        var piText = pi.Text ?? throw new InvalidOperationException("π 항목에 삽입 문자열이 없습니다.");
        Qa.Equal(originalLeafText + piText, Qa.Leaf(session, originalLeafId).Text, "실제 기호 삽입의 원문");
        Qa.Equal(priorRevision + 1, session.Revision, "기호 버튼이 단일 편집 revision을 만들지 않았습니다.");
        Qa.Check(editor.IsFocused, "기호 삽입 뒤 기존 editor focus 경계가 복원되지 않았습니다.");
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        Qa.Equal(originalFingerprint, Qa.Fingerprint(session.Workspace), "기호 삽입의 Ctrl+Z workspace 복원");
        Qa.Equal(originalCaret, session.Caret, "기호 삽입 undo의 caret 복원");
        Qa.Equal(originalStateId, session.CaptureSnapshot().StateId, "기호 삽입 undo의 원래 상태 ID 복원");
        browsingBaseline = CaptureSession(session);

        await SelectUnicodeTabAsync(window);
        var unicodeList = Find<ListBox>(window, "UnicodeResultsList");
        var nonInsertable = palette.UnicodeEntries.First(entry => !entry.CanInsert);
        unicodeList.SelectedItem = nonInsertable;
        await DrainLayoutAsync(window);
        Qa.Equal(nonInsertable.CodePoint, palette.SelectedUnicodeEntry?.CodePoint ?? -1,
            "삽입 불가 Unicode 항목을 선택해 진단을 읽을 수 없습니다.");
        Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "UnicodeDisabledReason").Text),
            "삽입 불가 Unicode 선택의 이유가 UI에 표시되지 않습니다.");
        Qa.Check(!Find<Button>(window, "UnicodeInsertButton").IsEffectivelyEnabled,
            "삽입 불가 Unicode scalar의 삽입 버튼이 활성화되었습니다.");
        Qa.Check(Find<Border>(window, "SelectedUnicodeDetails").IsEffectivelyVisible,
            "삽입 불가 Unicode 선택에서 상세 정보가 숨겨졌습니다.");
        Qa.Check(!Find<TextBlock>(window, "SelectedUnicodeGlyphStatus").IsEffectivelyVisible,
            "삽입 불가 Unicode가 기본 글꼴 글리프 검사를 제공하는 것처럼 표시됩니다.");
        await CaptureActiveScrollEdgesAsync(window, "unicode-disabled", "default-disabled",
            "UnicodeBrowserScrollViewer");
        AssertSessionUnchanged(browsingBaseline, session, "삽입 불가 Unicode 항목 선택");

        await ClickAsync(window, Find<Button>(window, "GreekUnicodeRangeButton"));
        await DrainLayoutAsync(window);
        Qa.Check(palette.UnicodePageIndex > 0 && Find<TextBlock>(window, "UnicodePageIndicator").Text ==
                 FormatPage(palette.UnicodePageIndex, palette.UnicodePageCount),
            "그리스 추천 범위 이동 또는 1부터 시작하는 페이지 표시가 반영되지 않았습니다.");
        AssertSessionUnchanged(browsingBaseline, session, "Unicode 추천 범위 이동");

        var queryBox = Find<TextBox>(window, "UnicodeQueryBox");
        await ReplaceTextAsync(window, queryBox, "U+not-a-codepoint");
        await ClickAsync(window, Find<Button>(window, "UnicodeLookupButton"));
        Qa.Check(!string.IsNullOrWhiteSpace(palette.UnicodeLookupError), "잘못된 U+ 검색 진단이 표시되지 않았습니다.");
        Qa.Check(!Find<Border>(window, "SelectedUnicodeDetails").IsEffectivelyVisible,
            "잘못된 Unicode 검색에서 이전 선택의 상세 카드가 남아 있습니다.");
        await CaptureActiveScrollEdgesAsync(window, "unicode-invalid-query", "default-invalid",
            "UnicodeBrowserScrollViewer");
        AssertSessionUnchanged(browsingBaseline, session, "잘못된 Unicode query");

        await ReplaceTextAsync(window, queryBox, "U+1F600");
        await ClickAsync(window, Find<Button>(window, "UnicodeLookupButton"));
        await DrainLayoutAsync(window);
        Qa.Equal(0x1F600, palette.SelectedUnicodeEntry?.CodePoint ?? -1, "현재 페이지 밖 Unicode lookup 선택");
        Qa.Equal(1, palette.VisibleUnicodeEntries.Length, "현재 페이지 밖 lookup의 단일 표시 항목 수");
        Qa.Equal(1, unicodeList.Items.Count, "lookup 결과가 실제 Unicode ListBox에 남아 있지 않습니다.");
        Qa.Check(palette.UnicodePageIndex == UnicodeScalarBrowser.GetPageIndexForCodePoint(0x0370),
            "lookup가 현재 페이지를 예상치 않게 이동했습니다.");
        var unicodeInsert = Find<Button>(window, "UnicodeInsertButton");
        Qa.Check(unicodeInsert.IsEffectivelyEnabled, "유효한 outside-page scalar의 삽입 버튼이 활성화되지 않았습니다.");
        Qa.Equal(0x1F600, unicodeInsert.CommandParameter is int scalar ? scalar : -1,
            "Unicode 삽입 버튼에 현재 선택 scalar가 전달되지 않았습니다.");
        Qa.Check(Find<Border>(window, "SelectedUnicodeDetails").IsEffectivelyVisible,
            "페이지 밖 Unicode 조회 결과의 상세 카드가 표시되지 않습니다.");
        await CaptureActiveScrollEdgesAsync(window, "unicode-outside-page", "default-lookup",
            "UnicodeBrowserScrollViewer");
        AssertSessionUnchanged(browsingBaseline, session, "outside-page Unicode lookup");

        var beforeUnicodeInsert = CaptureSession(session);
        var unicodeLeafId = session.Caret.NodeId;
        var unicodeLeafText = Qa.Leaf(session, unicodeLeafId).Text;
        var smile = char.ConvertFromUtf32(0x1F600);
        await ClickAsync(window, unicodeInsert);
        await DrainLayoutAsync(window);
        Qa.Equal(unicodeLeafText + smile, Qa.Leaf(session, unicodeLeafId).Text, "outside-page scalar 삽입 원문");
        Qa.Check(ReferenceEquals(session, viewModel.Session) && editor.IsFocused,
            "Unicode 삽입 뒤 현재 Session 또는 editor focus가 유지되지 않았습니다.");
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        AssertWorkspaceAndCaretRestored(beforeUnicodeInsert, session, "Unicode scalar 삽입 undo");
        browsingBaseline = CaptureSession(session);

        await ClickAsync(window, Find<RadioButton>(window, "EmojiModeButton"));
        await DrainLayoutAsync(window);
        Qa.Equal(SymbolPaletteMode.Emoji, palette.Mode, "엄선 이모지 하위 모드 선택");
        await ReplaceTextAsync(window, Find<TextBox>(window, "EmojiSearchBox"), "technologist");
        var emoji = CuratedEmojiCatalog.Find("emoji.woman-technologist")
            ?? throw new InvalidOperationException("ZWJ 기술자 이모지 fixture를 찾지 못했습니다.");
        Qa.Check(palette.EmojiEntries.Any(entry => entry.Id == emoji.Id), "엄선 이모지 검색에서 ZWJ 기술자를 찾지 못했습니다.");
        var emojiList = Find<ListBox>(window, "EmojiResultsList");
        emojiList.SelectedItem = emoji;
        await DrainLayoutAsync(window);
        Qa.Equal(emoji.Id, palette.SelectedEmojiEntry?.Id, "이모지 목록 선택이 상세 선택에 반영되지 않았습니다.");
        Qa.Check(emoji.Text.EnumerateRunes().Any(rune => rune.Value == 0x200D), "이모지 fixture가 ZWJ 시퀀스가 아닙니다.");
        var emojiInsert = Find<Button>(window, "EmojiInsertButton");
        Qa.Check(emojiInsert.IsEffectivelyEnabled, "이모지 선택 후 삽입 버튼이 활성화되지 않았습니다.");
        Qa.Equal(emoji.Id, emojiInsert.CommandParameter as string, "이모지 삽입 버튼의 선택 ID 매개변수");
        AssertSessionUnchanged(browsingBaseline, session, "이모지 검색·선택");

        var beforeEmojiInsert = CaptureSession(session);
        var emojiLeafId = session.Caret.NodeId;
        var emojiLeafText = Qa.Leaf(session, emojiLeafId).Text;
        await ClickAsync(window, emojiInsert);
        await DrainLayoutAsync(window);
        Qa.Equal(emojiLeafText + emoji.Text, Qa.Leaf(session, emojiLeafId).Text, "ZWJ 이모지 원문 시퀀스 삽입");
        Qa.Equal(beforeEmojiInsert.Revision + 1, session.Revision, "이모지 삽입이 한 번의 session 편집이 아닙니다.");
        Qa.Check(ReferenceEquals(session, viewModel.Session) && editor.IsFocused,
            "이모지 삽입 뒤 현재 Session 또는 editor focus가 유지되지 않았습니다.");
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        AssertWorkspaceAndCaretRestored(beforeEmojiInsert, session, "ZWJ 이모지 삽입 undo");

        var originalSessionFingerprint = Qa.Fingerprint(session.Workspace);
        var originalSessionStateId = session.CaptureSnapshot().StateId;
        var paletteIdentity = viewModel.SymbolPalette;
        await ClickAsync(window, Find<Button>(window, "NewWorkspaceButton"));
        await DrainLayoutAsync(window);
        var replacementSession = viewModel.Session;
        Qa.Check(!ReferenceEquals(session, replacementSession), "새 작업 버튼이 새로운 현재 Session을 만들지 않았습니다.");
        Qa.Check(ReferenceEquals(paletteIdentity, viewModel.SymbolPalette),
            "새 작업 뒤 SymbolPaletteViewModel이 불필요하게 교체되었습니다.");
        Qa.Check(ReferenceEquals(replacementSession, editor.Session),
            "새 작업 뒤 MathEditorSurface가 현재 Session으로 바뀌지 않았습니다.");

        await SelectSymbolsTabAsync(window);
        categoryBox.SelectedValue = MathSymbolCategory.Algebra;
        await DrainLayoutAsync(window);
        await ReplaceTextAsync(window, Find<TextBox>(window, "SymbolSearchBox"), string.Empty);
        var plus = MathSymbolCatalog.Find("symbol.algebra.plus")
            ?? throw new InvalidOperationException("더하기 기호 팔레트 fixture를 찾지 못했습니다.");
        symbolList.SelectedItem = plus;
        await DrainLayoutAsync(window);
        var newWorkspaceLeafId = replacementSession.Caret.NodeId;
        await ClickAsync(window, symbolInsert);
        await DrainLayoutAsync(window);
        var plusText = plus.Text ?? throw new InvalidOperationException("더하기 항목에 삽입 문자열이 없습니다.");
        Qa.Equal(plusText, Qa.Leaf(replacementSession, newWorkspaceLeafId).Text,
            "기존 SymbolPaletteViewModel의 command가 새 현재 Session으로 라우팅되지 않았습니다.");
        Qa.Equal(originalSessionFingerprint, Qa.Fingerprint(session.Workspace),
            "새 Session 기호 삽입이 이전 Session을 변경했습니다.");
        Qa.Equal(originalSessionStateId, session.CaptureSnapshot().StateId,
            "새 Session 기호 삽입이 이전 Session의 저장 상태를 바꿨습니다.");
        Qa.Check(ReferenceEquals(replacementSession, viewModel.Session) && editor.IsFocused,
            "현재 Session 기호 삽입 뒤 managed editor focus가 유지되지 않았습니다.");
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        Qa.Check(Qa.Leaf(replacementSession, newWorkspaceLeafId).Text.Length == 0,
            "현재 Session 기호 삽입의 undo가 빈 작업을 복원하지 않았습니다.");
    }

    private static void AssertTemplateCommandProtection(MainWindow window, MainWindowViewModel viewModel)
    {
        var templates = new (string Name, string CommandName, string AccessibleName)[]
        {
            ("InsertFractionButton", nameof(MainWindowViewModel.InsertFractionCommand), "분수 템플릿 삽입"),
            ("InsertRootButton", nameof(MainWindowViewModel.InsertRootCommand), "근호 템플릿 삽입"),
            ("InsertSuperscriptButton", nameof(MainWindowViewModel.InsertSuperscriptCommand), "위첨자 템플릿 삽입"),
            ("InsertMatrixButton", nameof(MainWindowViewModel.InsertMatrixCommand), "2행 2열 행렬 템플릿 삽입"),
            ("InsertSubscriptButton", nameof(MainWindowViewModel.InsertSubscriptCommand), "아래첨자 템플릿 삽입"),
            ("InsertIntegralButton", nameof(MainWindowViewModel.InsertIntegralCommand), "적분 템플릿 삽입"),
            ("InsertSummationButton", nameof(MainWindowViewModel.InsertSummationCommand), "합 템플릿 삽입"),
            ("InsertLimitButton", nameof(MainWindowViewModel.InsertLimitCommand), "극한 템플릿 삽입"),
            ("InsertBracketsButton", nameof(MainWindowViewModel.InsertBracketsCommand), "소괄호 템플릿 삽입"),
            ("InsertCasesButton", nameof(MainWindowViewModel.InsertCasesCommand), "두 행 경우 나누기 템플릿 삽입")
        };

        foreach (var template in templates)
        {
            var property = typeof(MainWindowViewModel).GetProperty(template.CommandName)
                ?? throw new InvalidOperationException("기존 템플릿 command 속성이 없습니다: " + template.CommandName);
            var expected = property.GetValue(viewModel) as ICommand
                ?? throw new InvalidOperationException("기존 템플릿 command가 ICommand가 아닙니다: " + template.CommandName);
            var button = Find<Button>(window, template.Name);
            Qa.Equal(template.Name, AutomationProperties.GetAutomationId(button) ?? string.Empty,
                template.Name + " AutomationId 보존");
            Qa.Check(ReferenceEquals(expected, button.Command), template.Name + " 기존 ICommand 바인딩 보존");
            Qa.Equal(template.AccessibleName, AutomationProperties.GetName(button) ?? string.Empty,
                template.Name + " 한국어 접근성 이름");
        }
    }

    private static void AssertPaletteCommandBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        var palette = viewModel.SymbolPalette;
        AssertButtonBinding(window, "InsertSelectedSymbolButton", palette.InsertPaletteEntryCommand, "선택한 수학 기호 삽입");
        AssertButtonBinding(window, "UnicodeInsertButton", palette.InsertUnicodeScalarCommand, "선택한 유니코드 문자 삽입");
        AssertButtonBinding(window, "EmojiInsertButton", palette.InsertEmojiCommand, "선택한 이모지 삽입");
        AssertButtonBinding(window, "UnicodeLookupButton", palette.LookupUnicodeCommand, "유니코드 코드 포인트 찾기");
        AssertButtonBinding(window, "UnicodePreviousPageButton", palette.PreviousUnicodePageCommand, "이전 유니코드 페이지");
        AssertButtonBinding(window, "UnicodeNextPageButton", palette.NextUnicodePageCommand, "다음 유니코드 페이지");

        foreach (var (name, stableRangeId, accessibleName) in new[]
        {
            ("MathUnicodeRangeButton", "unicode.range.math", "수학 기호 범위로 이동"),
            ("GreekUnicodeRangeButton", "unicode.range.greek", "그리스 문자 범위로 이동"),
            ("HangulUnicodeRangeButton", "unicode.range.hangul", "한글 음절 범위로 이동")
        })
        {
            var button = Find<Button>(window, name);
            Qa.Check(ReferenceEquals(button.Command, palette.BrowseUnicodeRangeCommand), name + " 추천 범위 command binding");
            Qa.Equal(stableRangeId, button.CommandParameter as string, name + " 안정 범위 ID");
            Qa.Equal(accessibleName, AutomationProperties.GetName(button) ?? string.Empty, name + " 한국어 접근성 이름");
        }
    }

    private static void AssertButtonBinding(Window window, string name, ICommand expected, string accessibleName)
    {
        var button = Find<Button>(window, name);
        Qa.Equal(name, AutomationProperties.GetAutomationId(button) ?? string.Empty, name + " AutomationId");
        Qa.Check(ReferenceEquals(expected, button.Command), name + " VM command binding");
        Qa.Equal(accessibleName, AutomationProperties.GetName(button) ?? string.Empty, name + " 접근성 이름");
    }

    private static async Task CaptureAllModesAsync(MainWindow window, string sizeLabel)
    {
        await ActivateStructureModeAsync(window);
        await CaptureActiveScrollEdgesAsync(window, "structure", sizeLabel, "TemplateToolsScrollViewer");

        await SelectSymbolsTabAsync(window);
        var viewModel = window.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("기호 캡처에 MainWindowViewModel이 연결되지 않았습니다.");
        var palette = viewModel.SymbolPalette;
        Find<ComboBox>(window, "SymbolCategoryComboBox").SelectedValue = MathSymbolCategory.Algebra;
        await ReplaceTextAsync(window, Find<TextBox>(window, "SymbolSearchBox"), string.Empty);
        var plus = MathSymbolCatalog.Find("symbol.algebra.plus")
            ?? throw new InvalidOperationException("기호 시각 캡처의 더하기 항목을 찾지 못했습니다.");
        Find<ListBox>(window, "SymbolResultsList").SelectedItem = plus;
        await DrainLayoutAsync(window);
        Qa.Equal(plus.Id, palette.SelectedPaletteEntry?.Id, "기호 상세 캡처 전 선택");
        Qa.Check(Find<Border>(window, "SelectedSymbolDetails").IsEffectivelyVisible,
            "기호를 선택했지만 상세 카드가 표시되지 않습니다.");
        await CaptureActiveScrollEdgesAsync(window, "symbols", sizeLabel, "SymbolPaletteScrollViewer");

        await SelectUnicodeTabAsync(window);
        var unicodeList = Find<ListBox>(window, "UnicodeResultsList");
        var unicodeEntry = palette.UnicodeEntries.First(entry => entry.CanInsert);
        unicodeList.SelectedItem = unicodeEntry;
        await DrainLayoutAsync(window);
        Qa.Equal(unicodeEntry.CodePoint, palette.SelectedUnicodeEntry?.CodePoint ?? -1,
            "Unicode 상세 캡처 전 기본 페이지 선택");
        Qa.Check(Find<Border>(window, "SelectedUnicodeDetails").IsEffectivelyVisible,
            "Unicode 항목을 선택했지만 상세 카드가 표시되지 않습니다.");
        await CaptureActiveScrollEdgesAsync(window, "unicode", sizeLabel, "UnicodeBrowserScrollViewer");

        await ClickAsync(window, Find<RadioButton>(window, "EmojiModeButton"));
        await DrainLayoutAsync(window);
        await ReplaceTextAsync(window, Find<TextBox>(window, "EmojiSearchBox"), "technologist");
        var emoji = CuratedEmojiCatalog.Find("emoji.woman-technologist")
            ?? throw new InvalidOperationException("이모지 시각 캡처의 기술자 항목을 찾지 못했습니다.");
        Find<ListBox>(window, "EmojiResultsList").SelectedItem = emoji;
        await DrainLayoutAsync(window);
        Qa.Equal(emoji.Id, palette.SelectedEmojiEntry?.Id, "이모지 상세 캡처 전 선택");
        Qa.Check(Find<Border>(window, "SelectedEmojiDetails").IsEffectivelyVisible,
            "이모지를 선택했지만 상세 카드가 표시되지 않습니다.");
        await CaptureActiveScrollEdgesAsync(window, "emoji", sizeLabel, "UnicodeBrowserScrollViewer");
    }

    private static async Task CaptureActiveScrollEdgesAsync(MainWindow window, string mode, string sizeLabel,
        string scrollName)
    {
        var scroll = Find<ScrollViewer>(window, scrollName);
        scroll.Offset = new Vector(scroll.Offset.X, 0);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, mode + " 상단 셸 렌더 전");
        RowShellVisualCases.CaptureAndVerify(window, $"palette-{mode}-{sizeLabel}-top", ArtifactScope);

        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maximum);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, mode + " 하단 셸 렌더 전");
        RowShellVisualCases.CaptureAndVerify(window, $"palette-{mode}-{sizeLabel}-bottom", ArtifactScope);
    }

    private static async Task ActivateStructureModeAsync(MainWindow window)
    {
        var tab = Find<TabItem>(window, "StructureToolsTab");
        if (!tab.IsSelected)
            await ClickAsync(window, tab);
        await DrainLayoutAsync(window);
        Qa.Check(tab.IsSelected && Find<TabControl>(window, "ToolModeTabs").SelectedIndex == 0,
            "구조 도구 탭을 활성화하지 못했습니다.");
    }

    private static async Task SelectSymbolsTabAsync(MainWindow window)
    {
        var tab = Find<TabItem>(window, "SymbolPaletteTab");
        if (!tab.IsSelected)
            await ClickAsync(window, tab);
        await DrainLayoutAsync(window);
        Qa.Check(tab.IsSelected && Find<TabControl>(window, "ToolModeTabs").SelectedIndex == 1,
            "기호 도구 탭을 활성화하지 못했습니다.");
        if (window.DataContext is MainWindowViewModel viewModel)
            Qa.Equal(SymbolPaletteMode.Symbols, viewModel.SymbolPalette.Mode, "기호 탭의 palette mode");
        else
            throw new InvalidOperationException("기호 탭 시나리오에 MainWindowViewModel이 없습니다.");
    }

    private static async Task SelectUnicodeTabAsync(MainWindow window)
    {
        var tab = Find<TabItem>(window, "UnicodeBrowserTab");
        if (!tab.IsSelected)
            await ClickAsync(window, tab);
        await DrainLayoutAsync(window);
        Qa.Check(tab.IsSelected && Find<TabControl>(window, "ToolModeTabs").SelectedIndex == 2,
            "문자 도구 탭을 활성화하지 못했습니다.");
        await ClickAsync(window, Find<RadioButton>(window, "UnicodeModeButton"));
        await DrainLayoutAsync(window);
        if (window.DataContext is MainWindowViewModel viewModel)
            Qa.Equal(SymbolPaletteMode.Unicode, viewModel.SymbolPalette.Mode, "Unicode 하위 모드");
    }

    private static async Task ReplaceTextAsync(Window window, TextBox textBox, string text)
    {
        EnsureStubHandle(window, "검색 입력 전");
        await BringControlIntoViewAsync(window, textBox);
        Qa.Check(IsFullyVisible(window, textBox), "검색 입력 대상이 창/스크롤 viewport 안에 완전히 표시되지 않았습니다.");
        textBox.Focus();
        textBox.SelectAll();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        if (text.Length > 0)
            window.KeyTextInput(text);
        await DrainLayoutAsync(window);
        Qa.Equal(text, textBox.Text ?? string.Empty, "합성 text input 뒤 실제 TextBox 내용");
    }

    private static async Task ClickAsync(Window window, Control control)
    {
        EnsureStubHandle(window, "합성 UI 입력 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 UI 항목에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "합성 클릭 대상의 창 기준 좌표를 얻지 못했습니다: " + control.Name);
        Qa.Check(IsFullyVisible(window, control), "합성 클릭 대상이 창/스크롤 viewport 안에 완전히 표시되지 않았습니다: " + control.Name);
        var point = origin!.Value + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task BringControlIntoViewAsync(Window window, Control control)
    {
        var scroll = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
        {
            control.BringIntoView();
            await DrainLayoutAsync(window);
            return;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var origin = control.TranslatePoint(new Point(0, 0), scroll);
            Qa.Check(origin is Point, "스크롤 내부 합성 대상의 좌표를 얻지 못했습니다: " + control.Name);
            var top = origin!.Value.Y;
            var bottom = top + control.Bounds.Height;
            var nextOffset = scroll.Offset.Y;
            if (top < 0)
                nextOffset += top;
            else if (bottom > scroll.Viewport.Height)
                nextOffset += bottom - scroll.Viewport.Height;
            else
                break;

            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(nextOffset, 0, maximum));
            await DrainLayoutAsync(window);
        }

        Qa.Check(IsFullyVisible(window, control),
            "스크롤 후에도 합성 클릭 대상이 viewport 안에 완전히 들어오지 않습니다: " + control.Name);
    }

    private static bool IsFullyVisible(Window window, Control control)
    {
        var target = BoundsInWindow(control, window);
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
        if (!Contains(client, target))
            return false;

        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = scroll.TranslatePoint(new Point(0, 0), window);
            if (origin is not Point)
                return false;
            var viewport = new Rect(origin!.Value, scroll.Viewport);
            if (!Contains(viewport, target))
                return false;
        }
        return control.IsVisible;
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "합성 대상의 창 기준 bounds를 얻지 못했습니다: " + control.Name);
        return new Rect(origin!.Value, control.Bounds.Size);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static void SendShortcut(Window window, Key key, PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "편집기 undo 합성 입력 전");
        window.KeyPress(key, RawInputModifiers.Control, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static SessionState CaptureSession(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.CaptureSnapshot().StateId, session.Revision,
        session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
        session.IsDirty, session.CanUndo, session.CanRedo);

    private static void AssertSessionUnchanged(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Workspace, Qa.Fingerprint(actual.Workspace), label + " workspace");
        Qa.Equal(expected.StateId, actual.CaptureSnapshot().StateId, label + " state ID");
        Qa.Equal(expected.Revision, actual.Revision, label + " revision");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " structure selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + " undo history");
        Qa.Equal(expected.CanRedo, actual.CanRedo, label + " redo history");
    }

    private static void AssertWorkspaceAndCaretRestored(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Workspace, Qa.Fingerprint(actual.Workspace), label + " workspace");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " structure selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.StateId, actual.CaptureSnapshot().StateId, label + " state ID");
    }

    private static string FormatPage(int zeroBasedPage, int pageCount) =>
        $"{(zeroBasedPage + 1).ToString("N0", CultureInfo.CurrentCulture)} / {pageCount.ToString("N0", CultureInfo.CurrentCulture)}";

    private static async Task DrainLayoutAsync(Window window)
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            Dispatcher.UIThread.RunJobs();
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnsureStubHandle(Window window, string phase)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is not null && handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            $"{phase}: STUB/0 handle이 아닙니다. 입력과 캡처를 중단합니다.");
    }

    private static async Task CloseOwnedWindowAsync(MainWindow? window)
    {
        if (window is null)
            return;

        if (window.DataContext is MainWindowViewModel viewModel && viewModel.IsPendingWorkspaceActionVisible)
            viewModel.CancelPendingWorkspaceAction();
        window.Close();
        if (window.DataContext is MainWindowViewModel closingViewModel &&
            closingViewModel.IsPendingWorkspaceActionVisible &&
            closingViewModel.PendingWorkspaceAction == WorkspaceActionKind.CloseWindow)
            closingViewModel.DiscardPendingWorkspaceAction();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "팔레트 UI가 소유한 headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "팔레트 UI child 종료 뒤 STUB가 아닌 native 창 handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private sealed record SessionState(
        string Workspace,
        Guid StateId,
        long Revision,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo);
}
