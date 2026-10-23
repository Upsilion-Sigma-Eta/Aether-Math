using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class RowShellVisualCases
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void CaptureAndVerify(MainWindow window, string label, string artifactScope = "p1-row-editing")
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (artifactScope != "p1-row-editing" && artifactScope != "p1-group-editing" &&
            artifactScope != "p1-lifecycle-protection" && artifactScope != "p1-recovery-recent" &&
            artifactScope != "p2-templates" && artifactScope != "p2-structure-clear" &&
            artifactScope != "p2-properties" && artifactScope != "p2-typesetting" &&
            artifactScope != "p2-dfrac-pin" &&
            artifactScope != "p2-text-role" &&
            artifactScope != "p2-symbol-unicode" &&
            artifactScope != "p2-symbol-palette" &&
            artifactScope != "p2-group-row-spacing" &&
            artifactScope != "p2-thin-math-space" &&
            artifactScope != "p2-bracket-kind" &&
            artifactScope != "p2-latex-highlight")
            throw new ArgumentException("시각 증거 범위는 승인된 P1 편집 또는 P2 도구 디렉터리여야 합니다.", nameof(artifactScope));
        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            throw new ArgumentException("시각 증거 라벨은 단일 파일 이름이어야 합니다.", nameof(label));

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.HandleDescriptor != "STUB" ||
            platformHandle.Handle != IntPtr.Zero)
            throw new InvalidOperationException("RowShell 시각 검증에서 STUB/0 headless 창 handle을 확인하지 못했습니다.");

        Dispatcher.UIThread.RunJobs();
        var clientSize = window.ClientSize;
        var isMinimum = Near(clientSize.Width, 1060) && Near(clientSize.Height, 700);
        var isDefault = Near(clientSize.Width, 1360) && Near(clientSize.Height, 880);
        Qa.Check(isMinimum || isDefault,
            $"지원하지 않는 셸 검증 크기입니다: {clientSize.Width:R}×{clientSize.Height:R}.");
        var artifactDirectory = Path.Combine(FindRepositoryRoot(), "doc", "verification", "artifacts",
            artifactScope, $"{label}-{(isMinimum ? "1060x700" : "1360x880")}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactDirectory);

        var clientBounds = new Rect(0, 0, clientSize.Width, clientSize.Height);
        var headerBounds = new Rect(0, 0, clientSize.Width, 70);
        BoundsRecord? lifecycleDialogRecord = null;
        BoundsRecord? lifecycleTargetCardRecord = null;
        BoundsRecord? lifecycleTargetRecord = null;
        NamedBounds[]? lifecycleChoiceRecords = null;
        string? lifecycleErrorText = null;
        BoundsRecord? lifecycleErrorRecord = null;
        BoundsRecord? libraryViewportRecord = null;
        BoundsRecord? libraryHeaderRecord = null;
        BoundsRecord? libraryRecentActionsRecord = null;
        BoundsRecord? libraryRecoveryActionsRecord = null;
        BoundsRecord? libraryPreviewRecord = null;
        BoundsRecord? libraryPreviewNameRecord = null;
        BoundsRecord? libraryPreviewPathRecord = null;
        BoundsRecord? libraryPreviewStatusRecord = null;
        BoundsRecord? libraryPreviewModifiedRecord = null;
        double? libraryVerticalOffset = null;
        BoundsRecord? templatePanelRecord = null;
        BoundsRecord? templateScrollViewerRecord = null;
        NamedBounds[]? templateButtonRecords = null;
        double? templateVerticalOffset = null;
        string? templateCaptureEdge = null;
        BoundsRecord? structureInspectorBoundsRecord = null;
        BoundsRecord? structureClearPanelBoundsRecord = null;
        BoundsRecord? structureTreeBoundsRecord = null;
        BoundsRecord? structureScrollViewerBoundsRecord = null;
        BoundsRecord? structureEffectiveViewportBoundsRecord = null;
        BoundsRecord? structureHeaderScrollViewerBoundsRecord = null;
        StructureScrollMetrics? structureScrollMetrics = null;
        BoundsRecord? structureSelectedItemBoundsRecord = null;
        bool? structureSelectedItemIsSelected = null;
        string? structureSelectionMode = null;
        string? structureHorizontalScrollVisibility = null;
        NamedBounds[]? structureControlRecords = null;
        string? structureSelectedNodeId = null;
        string? structureActivePath = null;
        string? structureClearDescription = null;
        string? structureClearDisabledReason = null;
        int? structureClearAffectedRows = null;
        bool? structureCanClear = null;
        PropertiesCaptureEvidence? propertiesEvidenceRecord = null;
        NamedBounds[]? groupRowSpacingControlRecords = null;
        BoundsRecord? latexDiagnosticsViewportRecord = null;
        BoundsRecord? palettePanelRecord = null;
        BoundsRecord? paletteTabsRecord = null;
        BoundsRecord? paletteScrollRecord = null;
        NamedBounds[]? paletteControlRecords = null;
        double? paletteVerticalOffset = null;
        string? paletteCaptureMode = null;
        var headerActionNames = new[]
        {
            "NewWorkspaceButton", "OpenWorkspaceButton", "SaveWorkspaceButton",
            "UndoButton", "RedoButton", "ExportSvgButton"
        };
        var headerActionBounds = headerActionNames
            .Select(name => GetBounds(Find<Button>(window, name), window, name))
            .ToArray();
        foreach (var pair in headerActionBounds.Select((bounds, index) => (bounds, index)))
        {
            Qa.Check(Contains(headerBounds, pair.bounds),
                $"상단 도구 {headerActionNames[pair.index]}가 헤더에서 잘리거나 벗어났습니다: {Format(pair.bounds)}.");
            for (var otherIndex = pair.index + 1; otherIndex < headerActionBounds.Length; otherIndex++)
                Qa.Check(!Overlaps(pair.bounds, headerActionBounds[otherIndex]),
                    $"상단 도구가 겹칩니다: {headerActionNames[pair.index]} / {headerActionNames[otherIndex]}.");
        }
        var groupIndicator = Find<Border>(window, "ActiveGroupIndicator");
        var addGroupButton = Find<Button>(window, "AddGroupButton");
        var duplicateGroupButton = Find<Button>(window, "DuplicateGroupButton");
        var deleteGroupButton = Find<Button>(window, "DeleteGroupButton");
        var moveGroupUpButton = Find<Button>(window, "MoveGroupUpButton");
        var moveGroupDownButton = Find<Button>(window, "MoveGroupDownButton");
        var rowIndicator = Find<Border>(window, "ActiveRowIndicator");
        var addButton = Find<Button>(window, "AddRowButton");
        var duplicateButton = Find<Button>(window, "DuplicateRowButton");
        var deleteButton = Find<Button>(window, "DeleteRowButton");
        var moveUpButton = Find<Button>(window, "MoveRowUpButton");
        var moveDownButton = Find<Button>(window, "MoveRowDownButton");
        var scrollViewer = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");

        var groupToolbarBounds = new[]
        {
            GetBounds(groupIndicator, window, "현재 그룹 표시"),
            GetBounds(addGroupButton, window, "그룹 추가"),
            GetBounds(duplicateGroupButton, window, "그룹 복제"),
            GetBounds(deleteGroupButton, window, "그룹 삭제"),
            GetBounds(moveGroupUpButton, window, "그룹 위로 이동"),
            GetBounds(moveGroupDownButton, window, "그룹 아래로 이동")
        };
        var groupToolbarNames = new[]
        {
            "ActiveGroupIndicator", "AddGroupButton", "DuplicateGroupButton", "DeleteGroupButton",
            "MoveGroupUpButton", "MoveGroupDownButton"
        };
        var rowToolbarBounds = new[]
        {
            GetBounds(rowIndicator, window, "현재 행 표시"),
            GetBounds(addButton, window, "행 추가"),
            GetBounds(duplicateButton, window, "행 복제"),
            GetBounds(deleteButton, window, "행 삭제"),
            GetBounds(moveUpButton, window, "행 위로 이동"),
            GetBounds(moveDownButton, window, "행 아래로 이동")
        };
        var rowToolbarNames = new[]
        {
            "ActiveRowIndicator", "AddRowButton", "DuplicateRowButton", "DeleteRowButton",
            "MoveRowUpButton", "MoveRowDownButton"
        };
        var allToolbarBounds = groupToolbarBounds.Concat(rowToolbarBounds).ToArray();
        var allToolbarNames = groupToolbarNames.Concat(rowToolbarNames).ToArray();

        foreach (var pair in allToolbarBounds.Select((bounds, index) => (bounds, index)))
        {
            Qa.Check(Contains(clientBounds, pair.bounds),
                $"{allToolbarNames[pair.index]}가 창 경계 밖으로 잘립니다: {Format(pair.bounds)}.");
            for (var otherIndex = pair.index + 1; otherIndex < allToolbarBounds.Length; otherIndex++)
                Qa.Check(!Overlaps(pair.bounds, allToolbarBounds[otherIndex]),
                    $"그룹/행 도구가 겹칩니다: {allToolbarNames[pair.index]} / {allToolbarNames[otherIndex]}.");
        }

        var viewerBounds = GetBounds(scrollViewer, window, "수식 편집 viewport");
        Qa.Check(Contains(clientBounds, viewerBounds),
            $"수식 편집 viewport가 창 안에 온전히 배치되지 않았습니다: {Format(viewerBounds)}.");
        Qa.Check(viewerBounds.Width >= 240 && viewerBounds.Height >= 220,
            $"수식 편집 viewport의 크기가 지나치게 작습니다: {Format(viewerBounds)}.");

        var editorBounds = GetBounds(editor, window, "수식 편집 컨트롤");
        var visibleEditorBounds = Intersect(editorBounds, viewerBounds);
        visibleEditorBounds = Intersect(visibleEditorBounds, clientBounds);
        var minimumVisibleEditorWidth = artifactScope == "p2-thin-math-space"
            ? Math.Min(120, editorBounds.Width)
            : 120;
        Qa.Check(editor.IsVisible && visibleEditorBounds.Width >= minimumVisibleEditorWidth &&
                 visibleEditorBounds.Height >= 160,
            $"MathEditorSurface의 보이는 viewport 교집합이 유효하지 않습니다: {Format(visibleEditorBounds)}, " +
            $"필요 가로={minimumVisibleEditorWidth:0.###} DIP, 자연 가로={editorBounds.Width:0.###} DIP.");
        foreach (var pair in allToolbarBounds.Select((bounds, index) => (bounds, index)))
            Qa.Check(!Overlaps(pair.bounds, visibleEditorBounds),
                $"그룹/행 도구 {allToolbarNames[pair.index]}가 편집 viewport를 가립니다.");

        if (artifactScope == "p2-templates")
        {
            Qa.Check(viewerBounds.Height >= 257,
                $"P2 템플릿이 중앙 편집 viewport 최소 높이를 257px 아래로 줄였습니다: {Format(viewerBounds)}.");
            var templatePanel = Find<Border>(window, "TemplateToolsPanel");
            var templateScroll = Find<ScrollViewer>(window, "TemplateToolsScrollViewer");
            var templatePanelBounds = GetBounds(templatePanel, window, "템플릿 도구 패널");
            var templateScrollBounds = GetBounds(templateScroll, window, "템플릿 도구 스크롤 viewport");
            Qa.Check(Contains(clientBounds, templatePanelBounds) && Contains(templatePanelBounds, templateScrollBounds),
                $"템플릿 도구 패널/스크롤 경계가 창을 벗어났습니다: panel={Format(templatePanelBounds)}, scroll={Format(templateScrollBounds)}.");
            Qa.Check(templateScrollBounds.Width >= 110 && templateScrollBounds.Height >= 180,
                $"템플릿 도구의 스크롤 viewport가 지나치게 작습니다: {Format(templateScrollBounds)}.");
            Qa.Check(!Overlaps(templatePanelBounds, visibleEditorBounds), "템플릿 도구 패널이 편집 viewport를 가립니다.");

            var templateNames = new[]
            {
                "InsertFractionButton", "InsertRootButton", "InsertSuperscriptButton", "InsertMatrixButton",
                "InsertSubscriptButton", "InsertIntegralButton", "InsertSummationButton", "InsertLimitButton",
                "InsertBracketsButton", "InsertCasesButton"
            };
            var templateButtons = templateNames.Select(name => Find<Button>(window, name)).ToArray();
            Qa.Equal(templateNames.Length,
                templateButtons.Select(button => AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .Distinct(StringComparer.Ordinal).Count(),
                "템플릿 버튼 AutomationId 고유 개수");
            var templateButtonBounds = templateButtons.Select((button, index) =>
                GetBounds(button, window, templateNames[index])).ToArray();
            for (var index = 0; index < templateButtonBounds.Length; index++)
            {
                Qa.Check(templateButtonBounds[index].Left >= templatePanelBounds.Left - 0.5 &&
                         templateButtonBounds[index].Right <= templatePanelBounds.Right + 0.5,
                    $"{templateNames[index]}가 템플릿 패널의 가로 경계 밖으로 나갑니다: {Format(templateButtonBounds[index])}.");
                for (var other = index + 1; other < templateButtonBounds.Length; other++)
                    Qa.Check(!Overlaps(templateButtonBounds[index], templateButtonBounds[other]),
                        $"템플릿 도구 버튼이 겹칩니다: {templateNames[index]} / {templateNames[other]}.");
            }

            var maximumOffset = Math.Max(0, templateScroll.Extent.Height - templateScroll.Viewport.Height);
            var atTop = templateScroll.Offset.Y <= 0.5;
            var atBottom = templateScroll.Offset.Y >= maximumOffset - 0.5;
            var expectedEdge = label.EndsWith("-top", StringComparison.Ordinal) ? "top"
                : label.EndsWith("-bottom", StringComparison.Ordinal) ? "bottom"
                : throw new ArgumentException("P2 템플릿 캡처 라벨은 -top 또는 -bottom으로 끝나야 합니다.", nameof(label));
            Qa.Check(expectedEdge == "top" ? atTop : atBottom,
                $"템플릿 {expectedEdge} 캡처에서 실제 ScrollViewer offset이 기대 위치가 아닙니다: offset={templateScroll.Offset.Y:R}, max={maximumOffset:R}.");
            var edgeButtonIndex = expectedEdge == "top" ? 0 : templateButtons.Length - 1;
            Qa.Check(Contains(templateScrollBounds, templateButtonBounds[edgeButtonIndex]),
                $"템플릿 {expectedEdge} 항목 {templateNames[edgeButtonIndex]}가 viewport 안에 완전히 표시되지 않습니다: {Format(templateButtonBounds[edgeButtonIndex])} / {Format(templateScrollBounds)}.");

            templatePanelRecord = Format(templatePanelBounds);
            templateScrollViewerRecord = Format(templateScrollBounds);
            templateButtonRecords = templateNames
                .Select((name, index) => new NamedBounds(name, Format(templateButtonBounds[index])))
                .ToArray();
            templateVerticalOffset = templateScroll.Offset.Y;
            templateCaptureEdge = expectedEdge;
        }

        if (artifactScope is "p2-symbol-unicode" or "p2-symbol-palette" or "p2-thin-math-space")
        {
            Qa.Check(viewerBounds.Height >= 257,
                $"P2 기호·문자 도구가 중앙 편집 viewport 최소 높이를 257px 아래로 줄였습니다: {Format(viewerBounds)}.");
            var palettePanel = Find<Border>(window, "TemplateToolsPanel");
            var palettePanelBounds = GetBounds(palettePanel, window, "기호·문자 도구 패널");
            var modeTabs = Find<TabControl>(window, "ToolModeTabs");
            var modeTabsBounds = GetBounds(modeTabs, window, "도구 모드 선택");
            Qa.Check(Contains(clientBounds, palettePanelBounds) && Contains(palettePanelBounds, modeTabsBounds),
                $"기호·문자 패널이나 탭이 창 경계 밖으로 나갑니다: panel={Format(palettePanelBounds)}, tabs={Format(modeTabsBounds)}.");
            Qa.Check(palettePanelBounds.Width >= 200 && palettePanelBounds.Width <= 230,
                $"기호·문자 패널 너비가 승인된 좁은 도구 영역을 벗어났습니다: {Format(palettePanelBounds)}.");
            Qa.Check(!Overlaps(palettePanelBounds, visibleEditorBounds),
                "기호·문자 패널이 편집 viewport와 겹칩니다.");

            var expectedMode = label.StartsWith("palette-structure-", StringComparison.Ordinal) ||
                               artifactScope == "p2-thin-math-space" &&
                               label.StartsWith("thin-space-structure-", StringComparison.Ordinal) ? "structure"
                : label.StartsWith("palette-symbols-", StringComparison.Ordinal) ? "symbols"
                : label.StartsWith("palette-unicode-", StringComparison.Ordinal) ? "unicode"
                : label.StartsWith("palette-emoji-", StringComparison.Ordinal) ? "emoji"
                : throw new ArgumentException("팔레트 캡처 라벨은 palette-structure/symbols/unicode/emoji 접두사를 사용해야 합니다.", nameof(label));
            var expectedEdge = label.EndsWith("-top", StringComparison.Ordinal) ? "top"
                : label.EndsWith("-bottom", StringComparison.Ordinal) ? "bottom"
                : throw new ArgumentException("팔레트 캡처 라벨은 -top 또는 -bottom으로 끝나야 합니다.", nameof(label));

            var structureTab = Find<TabItem>(window, "StructureToolsTab");
            var symbolsTab = Find<TabItem>(window, "SymbolPaletteTab");
            var charactersTab = Find<TabItem>(window, "UnicodeBrowserTab");
            Qa.Check(expectedMode switch
            {
                "structure" => structureTab.IsSelected && modeTabs.SelectedIndex == 0,
                "symbols" => symbolsTab.IsSelected && modeTabs.SelectedIndex == 1,
                _ => charactersTab.IsSelected && modeTabs.SelectedIndex == 2
            }, $"캡처 라벨 {expectedMode}와 실제 도구 모드 선택이 일치하지 않습니다.");

            if (expectedMode != "structure")
            {
                Qa.Check(window.DataContext is MainWindowViewModel,
                    "팔레트 시각 검증 창에 실제 MainWindowViewModel이 연결되지 않았습니다.");
                var viewModel = (MainWindowViewModel)window.DataContext!;
                var expectedPaletteMode = expectedMode switch
                {
                    "symbols" => SymbolPaletteMode.Symbols,
                    "emoji" => SymbolPaletteMode.Emoji,
                    _ => SymbolPaletteMode.Unicode
                };
                Qa.Equal(expectedPaletteMode, viewModel.SymbolPalette.Mode,
                    "상단 탭/문자 모드와 SymbolPalette.Mode 일치");
            }

            string[] controlNames;
            ScrollViewer activeScroll;
            if (expectedMode == "structure")
            {
                activeScroll = Find<ScrollViewer>(window, "TemplateToolsScrollViewer");
                controlNames =
                [
                    "InsertFractionButton", "InsertRootButton", "InsertSuperscriptButton", "InsertMatrixButton",
                    "InsertSubscriptButton", "InsertIntegralButton", "InsertSummationButton", "InsertLimitButton",
                    "InsertBracketsButton", "InsertCasesButton", "InsertThinSpaceButton", "ThinSpaceNotice",
                    "ThinSpaceHelpText", "ThinSpaceDisabledReason"
                ];
            }
            else if (expectedMode == "symbols")
            {
                activeScroll = Find<ScrollViewer>(window, "SymbolPaletteScrollViewer");
                controlNames =
                [
                    "SymbolCategoryComboBox", "SymbolSearchBox", "SymbolResultsList",
                    "SelectedSymbolDetails", "SelectedSymbolGlyphStatus", "InsertSelectedSymbolButton"
                ];
            }
            else
            {
                activeScroll = Find<ScrollViewer>(window, "UnicodeBrowserScrollViewer");
                var unicodeMode = Find<Avalonia.Controls.RadioButton>(window, "UnicodeModeButton");
                var emojiMode = Find<Avalonia.Controls.RadioButton>(window, "EmojiModeButton");
                Qa.Check(expectedMode == "unicode"
                    ? unicodeMode.IsChecked == true && emojiMode.IsChecked != true
                    : emojiMode.IsChecked == true && unicodeMode.IsChecked != true,
                    $"캡처 라벨 {expectedMode}와 문자 하위 모드가 일치하지 않습니다.");
                controlNames = expectedMode == "unicode"
                    ? new[]
                    {
                        "CharacterModeSelector", "UnicodeBrowserPanel", "UnicodeQueryBox", "UnicodeLookupButton",
                        "UnicodeModeButton", "EmojiModeButton",
                        "UnicodeLookupError",
                        "MathUnicodeRangeButton", "GreekUnicodeRangeButton", "HangulUnicodeRangeButton",
                        "UnicodePreviousPageButton", "UnicodePageIndicator", "UnicodeNextPageButton", "UnicodePageCount",
                        "UnicodeResultsList", "SelectedUnicodeCodePoint", "SelectedUnicodeDetails",
                        "UnicodeDisabledReason", "SelectedUnicodeGlyphStatus", "UnicodeInsertButton"
                    }
                    : new[]
                    {
                        "CharacterModeSelector", "UnicodeModeButton", "EmojiModeButton",
                        "EmojiBrowserPanel", "EmojiSearchBox", "EmojiResultsList",
                        "SelectedEmojiName", "SelectedEmojiDetails", "SelectedEmojiGlyphStatus", "EmojiInsertButton"
                    };
            }

            var activeScrollBounds = GetBounds(activeScroll, window, "기호·문자 도구의 스크롤 viewport");
            Qa.Check(Contains(palettePanelBounds, activeScrollBounds),
                $"활성 도구 스크롤 viewport가 패널을 벗어났습니다: {Format(activeScrollBounds)}.");
            var maximumPaletteOffset = Math.Max(0, activeScroll.Extent.Height - activeScroll.Viewport.Height);
            var atPaletteTop = activeScroll.Offset.Y <= 0.5;
            var atPaletteBottom = activeScroll.Offset.Y >= maximumPaletteOffset - 0.5;
            Qa.Check(expectedEdge == "top" ? atPaletteTop : atPaletteBottom,
                $"팔레트 {expectedEdge} 캡처의 실제 스크롤 위치가 기대와 다릅니다: offset={activeScroll.Offset.Y:R}, max={maximumPaletteOffset:R}.");

            var paletteControls = controlNames.Select(name => Find<Control>(window, name)).ToArray();
            var optionalPaletteControlNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "UnicodeLookupError", "SelectedSymbolDetails", "SelectedSymbolGlyphStatus",
                "SelectedUnicodeDetails", "SelectedUnicodeCodePoint", "UnicodeDisabledReason",
                "SelectedUnicodeGlyphStatus", "SelectedEmojiDetails", "SelectedEmojiName",
                "SelectedEmojiGlyphStatus", "ThinSpaceNotice", "ThinSpaceDisabledReason"
            };
            var paletteControlBounds = paletteControls.Select((control, index) =>
                optionalPaletteControlNames.Contains(controlNames[index])
                    ? GetOptionalBounds(control, window, controlNames[index])
                    : GetBounds(control, window, controlNames[index])).ToArray();
            foreach (var pair in paletteControlBounds.Select((bounds, index) => (bounds, index)))
            {
                if (!paletteControls[pair.index].IsEffectivelyVisible)
                {
                    Qa.Check(optionalPaletteControlNames.Contains(controlNames[pair.index]),
                        $"팔레트 도구 {controlNames[pair.index]}가 표시되지 않습니다.");
                    continue;
                }
                if (pair.bounds.Width <= 0 || pair.bounds.Height <= 0)
                {
                    Qa.Check(optionalPaletteControlNames.Contains(controlNames[pair.index]),
                        $"팔레트 도구 {controlNames[pair.index]}에 필수 가시 영역이 없습니다.");
                    continue;
                }
                Qa.Check(pair.bounds.Left >= palettePanelBounds.Left - 0.5 &&
                         pair.bounds.Right <= palettePanelBounds.Right + 0.5,
                    $"팔레트 도구 {controlNames[pair.index]}가 패널의 가로 경계를 벗어났습니다: {Format(pair.bounds)}.");
                Qa.Check(!Overlaps(pair.bounds, visibleEditorBounds),
                    $"팔레트 도구 {controlNames[pair.index]}가 편집 viewport를 가립니다.");
            }

            if (expectedMode is "unicode" or "emoji")
            {
                var unicodeModeIndex = Array.IndexOf(controlNames, "UnicodeModeButton");
                var emojiModeIndex = Array.IndexOf(controlNames, "EmojiModeButton");
                var scrollContent = activeScroll.Content as Control;
                Qa.Check(scrollContent is not null,
                    "문자 모드 ScrollViewer의 실제 콘텐츠 control을 찾지 못했습니다.");
                var scrollContentBounds = GetBounds(scrollContent!, window, "문자 도구 ScrollViewer의 실제 안쪽 콘텐츠");
                Qa.Check(unicodeModeIndex >= 0 && emojiModeIndex >= 0 &&
                         !Overlaps(paletteControlBounds[unicodeModeIndex], paletteControlBounds[emojiModeIndex]),
                    "유니코드와 이모지 모드 선택 버튼이 겹칩니다.");
                Qa.Check(Contains(scrollContentBounds, paletteControlBounds[unicodeModeIndex]) &&
                         Contains(scrollContentBounds, paletteControlBounds[emojiModeIndex]),
                    "유니코드 또는 이모지 모드 레이블이 ScrollViewer 콘텐츠의 안쪽 가로 bounds를 벗어납니다.");
            }

            if (artifactScope == "p2-thin-math-space" && expectedMode == "structure")
            {
                var thinButtonIndex = Array.IndexOf(controlNames, "InsertThinSpaceButton");
                var helpIndex = Array.IndexOf(controlNames, "ThinSpaceHelpText");
                Qa.Check(thinButtonIndex >= 0 && helpIndex >= 0,
                    "얇은 수학 공백 버튼이나 안내의 캡처 대상을 찾지 못했습니다.");
                var edgeTargetIndex = expectedEdge == "top" ? 0 : thinButtonIndex;
                var edgeTargetBounds = paletteControlBounds[edgeTargetIndex];
                Qa.Check(Contains(activeScrollBounds, edgeTargetBounds),
                    $"구조 도구 {expectedEdge} 캡처의 대상 버튼이 실제 스크롤 viewport 안에 완전히 표시되지 않습니다: " +
                    $"{Format(edgeTargetBounds)} / {Format(activeScrollBounds)}.");

                var help = Find<TextBlock>(window, "ThinSpaceHelpText");
                var helpText = help.Text
                    ?? throw new InvalidOperationException("얇은 수학 공백 도움말 텍스트가 null입니다.");
                Qa.Check(helpText.Contains("U+2009", StringComparison.Ordinal) &&
                         helpText.Contains("Ctrl+클릭", StringComparison.Ordinal) &&
                         helpText.Contains("구조 트리", StringComparison.Ordinal) &&
                         helpText.Contains("삭제", StringComparison.Ordinal),
                    "얇은 수학 공백 안내가 Unicode 문자와의 차이 또는 선택·삭제 경로를 설명하지 않습니다.");
                Qa.Check(help.TextWrapping == Avalonia.Media.TextWrapping.Wrap &&
                         paletteControlBounds[helpIndex].Width > 0,
                    "얇은 수학 공백 안내가 좁은 도구 패널에서 줄바꿈 가능한 크기로 배치되지 않았습니다.");
                Qa.Equal("얇은 수학 공백 삽입",
                    AutomationProperties.GetName(Find<Button>(window, "InsertThinSpaceButton")) ?? string.Empty,
                    "얇은 수학 공백 버튼의 접근성 이름");
                var thinButton = Find<Button>(window, "InsertThinSpaceButton");
                if (label.Contains("disabled", StringComparison.Ordinal))
                {
                    var disabledReason = Find<TextBlock>(window, "ThinSpaceDisabledReason");
                    Qa.Check(!thinButton.IsEffectivelyEnabled && disabledReason.IsEffectivelyVisible &&
                             !string.IsNullOrWhiteSpace(disabledReason.Text),
                        "비활성 캡처에서 얇은 수학 공백 버튼 또는 사유가 실제 UI에 반영되지 않았습니다.");
                    var reasonIndex = Array.IndexOf(controlNames, "ThinSpaceDisabledReason");
                    Qa.Check(Contains(activeScrollBounds, paletteControlBounds[reasonIndex]),
                        $"비활성 사유가 구조 도구 viewport에 완전히 표시되지 않습니다: {Format(paletteControlBounds[reasonIndex])} / {Format(activeScrollBounds)}.");
                }
                else if (window.DataContext is MainWindowViewModel thinViewModel)
                {
                    Qa.Equal(thinViewModel.CanInsertThinSpace, thinButton.IsEffectivelyEnabled,
                        "얇은 수학 공백 삽입 가능 상태의 실제 Button binding");
                }
            }

            palettePanelRecord = Format(palettePanelBounds);
            paletteTabsRecord = Format(modeTabsBounds);
            paletteScrollRecord = Format(activeScrollBounds);
            paletteControlRecords = controlNames
                .Select((name, index) => new NamedBounds(name, Format(paletteControlBounds[index]),
                    paletteControls[index].IsEffectivelyVisible))
                .ToArray();
            paletteVerticalOffset = activeScroll.Offset.Y;
            paletteCaptureMode = $"{expectedMode}-{expectedEdge}";
        }

        if (artifactScope is "p2-properties" or "p2-typesetting" or "p2-dfrac-pin" or "p2-text-role" or
            "p2-group-row-spacing" or "p2-bracket-kind")
        {
            Qa.Check(viewerBounds.Height >= 257,
                $"속성 도구가 중앙 편집 viewport 최소 높이를 257px 아래로 줄였습니다: {Format(viewerBounds)}.");
            if (window.DataContext is not MainWindowViewModel viewModel)
                throw new InvalidOperationException("속성 시각 검증 창에 실제 MainWindowViewModel이 연결되지 않았습니다.");

            var inspector = Find<TabControl>(window, "EquationInspector");
            var propertiesTab = Find<TabItem>(window, "PropertiesTab");
            Qa.Check(ReferenceEquals(inspector.SelectedItem, propertiesTab) || propertiesTab.IsSelected,
                "속성 PNG 캡처 전에 속성 탭을 선택해야 합니다.");
            var propertiesScrollViewer = Find<ScrollViewer>(window, "PropertiesScrollViewer");
            var panel = Find<StackPanel>(window, "PropertiesPanelContent");
            var scrollBounds = GetBounds(propertiesScrollViewer, window, "수식 속성 세로 스크롤");
            var panelBounds = GetBounds(panel, window, "수식 속성 패널 내용");
            var scrollOrigin = propertiesScrollViewer.TranslatePoint(new Point(0, 0), window);
            Qa.Check(scrollOrigin is Point, "속성 스크롤 viewport의 창 기준 좌표를 얻지 못했습니다.");
            var viewportBounds = new Rect(scrollOrigin!.Value, propertiesScrollViewer.Viewport);
            var scrollViewerValid = Contains(clientBounds, scrollBounds) &&
                                    propertiesScrollViewer.Viewport.Width >= 150 && propertiesScrollViewer.Viewport.Height >= 100;
            if (!scrollViewerValid)
                SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                    panelBounds, scrollBounds, viewportBounds, "PropertiesScrollViewer", scrollBounds,
                    propertiesScrollViewer);
            Qa.Check(scrollViewerValid,
                $"속성 패널의 실제 가시 viewport가 너무 작거나 창을 벗어났습니다: viewer={Format(scrollBounds)}, viewport={Format(viewportBounds)}.");
            var panelWithinViewport = panelBounds.Left >= scrollBounds.Left - 0.5 &&
                                      panelBounds.Right <= scrollBounds.Right + 0.5 &&
                                      panelBounds.Width <= propertiesScrollViewer.Viewport.Width + 1;
            if (!panelWithinViewport)
                SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                    panelBounds, scrollBounds, viewportBounds, "PropertiesPanelContent", panelBounds,
                    propertiesScrollViewer);
            Qa.Check(panelWithinViewport,
                $"속성 패널 콘텐츠가 가로 viewport 안에 들어오지 않습니다: panel={Format(panelBounds)}, viewport={Format(viewportBounds)}.");
            Qa.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                ScrollViewer.GetHorizontalScrollBarVisibility(propertiesScrollViewer), "속성 패널 가로 잘림 방지 설정");
            Qa.Check(propertiesScrollViewer.Extent.Width <= propertiesScrollViewer.Viewport.Width + 1,
                $"속성 패널 내용이 가로 viewport를 넘어갑니다: extent={propertiesScrollViewer.Extent.Width:R}, viewport={propertiesScrollViewer.Viewport.Width:R}.");

            var propertyControlNames = new[]
            {
                "PropertyTargetSummary", "PropertySelectionNotice", "PropertyScopeComboBox",
                "FontSizeEffectiveValue", "FontSizeExplicitValue", "FontSizeSourceLabel", "FontSizeDraftTextBox",
                "FontSizeValidationError", "FontSizeLatexDiagnostic",
                "FontFamilyEffectiveValue", "FontFamilyExplicitValue", "FontFamilySourceLabel", "FontFamilyDraftTextBox",
                "FontFamilyValidationError", "FontMeasurementDiagnostic", "FontFamilyLatexDiagnostic",
                "ForegroundEffectiveValue", "ForegroundExplicitValue", "ForegroundSourceLabel", "ForegroundDraftTextBox",
                "ForegroundValidationError", "ForegroundLatexDiagnostic", "PropertiesUnitsNotice",
                "TypesettingStyleEffectiveValue", "TypesettingStyleExplicitValue", "TypesettingStyleSourceLabel",
                "TypesettingStyleComboBox", "TypesettingStyleContextNotice",
                "TypesettingPinCheckBox", "TypesettingPinCheckBoxLabel", "TypesettingPinNotice", "TypesettingPinDisabledReason",
                "FractionDisplayNotice",
                "TextRoleCurrentValue", "TextRoleScopeNotice", "TextRoleDisabledReason", "TextRoleComboBox",
                "TextRoleSemanticsNotice",
                "GroupRowSpacingCard", "GroupRowSpacingCurrentValue", "GroupRowSpacingMeaningNotice",
                "GroupRowSpacingDraftTextBox", "GroupRowSpacingValidationError", "GroupRowSpacingDisabledReason",
                "GroupRowSpacingNotice", "GroupRowSpacingLatexDiagnostic",
                "BracketKindCard", "CurrentBracketKindValue", "BracketKindComboBox",
                "BracketKindDisabledReason", "BracketKindNotice",
                "PropertyBlankMeansUnchangedNotice",
                "PropertiesStaleNotice", "ApplyPropertiesButton", "ResetPropertiesDraftButton",
                "ResetPropertiesOverrideButton", "ResetPropertiesOverrideDisabledReason",
                "PropertiesResetOverrideNotice"
            };
            var propertyControls = propertyControlNames.Select(name => Find<Control>(window, name)).ToArray();
            var propertyControlBounds = propertyControls.Select((control, index) =>
            {
                if (control is TextBlock text && string.IsNullOrWhiteSpace(text.Text))
                    return new Rect();
                return control.IsEffectivelyVisible
                    ? GetBounds(control, window, propertyControlNames[index])
                    : new Rect();
            }).ToArray();
            for (var index = 0; index < propertyControls.Length; index++)
            {
                var bounds = propertyControlBounds[index];
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;
                var controlWithinPanel = bounds.Left >= scrollBounds.Left - 0.5 && bounds.Right <= scrollBounds.Right + 0.5;
                if (!controlWithinPanel)
                    SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                        panelBounds, scrollBounds, viewportBounds, propertyControlNames[index], bounds,
                        propertiesScrollViewer);
                Qa.Check(controlWithinPanel,
                    $"속성 컨트롤 {propertyControlNames[index]}이 패널 가로폭 밖으로 잘립니다: {Format(bounds)} / {Format(scrollBounds)}.");
            }

            var isTop = label.EndsWith("-top", StringComparison.Ordinal);
            var isBottom = label.EndsWith("-bottom", StringComparison.Ordinal);
            if (isTop)
            {
                Qa.Check(propertiesScrollViewer.Offset.Y <= 1,
                    $"속성 상단 캡처에서 스크롤 위치가 위쪽이 아닙니다: {propertiesScrollViewer.Offset.Y:R}.");
                foreach (var name in new[] { "PropertyTargetSummary", "PropertyScopeComboBox", "FontSizeEffectiveValue", "FontSizeExplicitValue", "FontSizeSourceLabel", "FontSizeDraftTextBox" })
                {
                    var index = Array.IndexOf(propertyControlNames, name);
                    var controlInViewport = Contains(viewportBounds, propertyControlBounds[index]);
                    if (!controlInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, name, propertyControlBounds[index],
                            propertiesScrollViewer);
                    Qa.Check(controlInViewport,
                        $"속성 상단 필드가 실제 viewport에서 잘리거나 가려집니다: {name}={Format(propertyControlBounds[index])}.");
                }
            }
            if (isBottom)
            {
                var maxOffset = Math.Max(0, propertiesScrollViewer.Extent.Height - propertiesScrollViewer.Viewport.Height);
                Qa.Check(propertiesScrollViewer.Offset.Y >= maxOffset - 2,
                    $"속성 하단 캡처에서 스크롤 끝에 도달하지 않았습니다: offset={propertiesScrollViewer.Offset.Y:R}, max={maxOffset:R}.");
                foreach (var name in new[] { "ApplyPropertiesButton", "ResetPropertiesDraftButton", "ResetPropertiesOverrideButton" })
                {
                    var index = Array.IndexOf(propertyControlNames, name);
                    var controlInViewport = Contains(viewportBounds, propertyControlBounds[index]);
                    if (!controlInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, name, propertyControlBounds[index],
                            propertiesScrollViewer);
                    Qa.Check(controlInViewport,
                        $"속성 하단 동작 버튼이 실제 viewport 안에 완전히 표시되지 않습니다: {name}={Format(propertyControlBounds[index])}.");
                }
            }

            if (artifactScope != "p2-group-row-spacing" && label.Contains("invalid", StringComparison.Ordinal))
                Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "FontSizeValidationError").Text) ||
                         !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "ForegroundValidationError").Text),
                    "invalid 속성 캡처에 초안 검증 오류가 없습니다.");
            if (label.Contains("stale", StringComparison.Ordinal))
            {
                var staleNotice = Find<TextBlock>(window, "PropertiesStaleNotice");
                Qa.Check(viewModel.Properties.IsDraftStale &&
                         !string.IsNullOrWhiteSpace(staleNotice.Text) &&
                         !viewModel.Properties.CanApply,
                    "stale 속성 초안의 사유 또는 적용 차단이 캡처에 표시되지 않습니다.");
                if (isBottom)
                {
                    var staleNoticeIndex = Array.IndexOf(propertyControlNames, "PropertiesStaleNotice");
                    Qa.Check(staleNoticeIndex >= 0, "stale 사유가 속성 bounds 기록에 없습니다.");
                    var staleNoticeBounds = propertyControlBounds[staleNoticeIndex];
                    var staleNoticeInViewport = Contains(viewportBounds, staleNoticeBounds);
                    if (!staleNoticeInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, "PropertiesStaleNotice", staleNoticeBounds,
                            propertiesScrollViewer);
                    Qa.Check(staleNoticeInViewport,
                        $"stale 하단 캡처에서 사유 문구 전체가 실제 viewport에 표시되지 않습니다: {Format(staleNoticeBounds)}.");
                    Qa.Check(!Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled &&
                             Find<Button>(window, "ResetPropertiesDraftButton").IsEffectivelyEnabled,
                        "stale 하단 캡처에서 적용은 비활성이고 초안 되돌리기는 활성인 상태가 아닙니다.");
                }
            }
            if (label.Contains("source", StringComparison.Ordinal))
                Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "FontSizeSourceLabel").Text) &&
                         !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "FontFamilySourceLabel").Text) &&
                         !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "ForegroundSourceLabel").Text),
                    "속성값의 상속/출처가 표시되지 않습니다.");

            if (artifactScope == "p2-typesetting" && label.Contains("typesetting-detail", StringComparison.Ordinal))
            {
                foreach (var name in new[]
                         {
                             "TypesettingStyleEffectiveValue", "TypesettingStyleExplicitValue",
                             "TypesettingStyleSourceLabel", "TypesettingStyleComboBox", "TypesettingStyleContextNotice"
                         })
                {
                    var index = Array.IndexOf(propertyControlNames, name);
                    var controlBounds = propertyControlBounds[index];
                    var isVisibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                              Contains(viewportBounds, controlBounds);
                    if (!isVisibleInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, name, controlBounds,
                            propertiesScrollViewer);
                    Qa.Check(isVisibleInViewport,
                        $"조판 스타일 상세 캡처에서 {name} 전체가 실제 viewport에 표시되지 않습니다: {Format(controlBounds)}.");
                }
            }

            if (artifactScope == "p2-dfrac-pin")
            {
                var pinCheckBox = Find<CheckBox>(window, "TypesettingPinCheckBox");
                var pinDisabledReason = Find<TextBlock>(window, "TypesettingPinDisabledReason");
                var fractionNotice = Find<TextBlock>(window, "FractionDisplayNotice");
                Qa.Equal(viewModel.Properties.CanPinTypesettingStyle, pinCheckBox.IsEffectivelyEnabled,
                    "조판 기준 pin 체크박스 활성 상태 바인딩");
                Qa.Equal(viewModel.Properties.IsFractionSelection, fractionNotice.IsEffectivelyVisible,
                    "Fraction SelectionNode 전용 dfrac 안내 visibility");
                Qa.Equal(!viewModel.Properties.CanPinTypesettingStyle &&
                         !string.IsNullOrWhiteSpace(viewModel.Properties.TypesettingPinDisabledReason),
                    pinDisabledReason.IsEffectivelyVisible,
                    "조판 기준 pin 비활성 사유 표시 상태");

                if (label.Contains("dfrac-pin-detail", StringComparison.Ordinal))
                {
                    foreach (var name in new[] { "TypesettingStyleComboBox", "TypesettingPinCheckBox", "TypesettingPinCheckBoxLabel", "TypesettingPinNotice" })
                    {
                        var index = Array.IndexOf(propertyControlNames, name);
                        var isVisibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                                  Contains(viewportBounds, propertyControlBounds[index]);
                        if (!isVisibleInViewport)
                            SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                                panelBounds, scrollBounds, viewportBounds, name, propertyControlBounds[index],
                                propertiesScrollViewer);
                        Qa.Check(isVisibleInViewport,
                            $"pin 상세 캡처에서 {name} 전체가 실제 속성 viewport 안에 표시되지 않습니다: {Format(propertyControlBounds[index])}.");
                    }

                    var pinCheckboxBounds = propertyControlBounds[Array.IndexOf(propertyControlNames, "TypesettingPinCheckBox")];
                    var pinCheckboxLabel = Find<TextBlock>(window, "TypesettingPinCheckBoxLabel");
                    var pinCheckboxLabelBounds = propertyControlBounds[Array.IndexOf(propertyControlNames, "TypesettingPinCheckBoxLabel")];
                    Qa.Check(pinCheckboxLabel.TextWrapping == Avalonia.Media.TextWrapping.Wrap &&
                             Contains(pinCheckboxBounds, pinCheckboxLabelBounds),
                        $"pin 체크박스의 한국어 설명이 내부에서 줄바꿈되거나 잘립니다: checkbox={Format(pinCheckboxBounds)}, label={Format(pinCheckboxLabelBounds)}.");

                    if (viewModel.Properties.IsFractionSelection)
                    {
                        var index = Array.IndexOf(propertyControlNames, "FractionDisplayNotice");
                        var isVisibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                                  Contains(viewportBounds, propertyControlBounds[index]);
                        if (!isVisibleInViewport)
                            SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                                panelBounds, scrollBounds, viewportBounds, "FractionDisplayNotice",
                                propertyControlBounds[index], propertiesScrollViewer);
                        Qa.Check(isVisibleInViewport,
                            $"선택 분수의 LaTeX 안내가 실제 속성 viewport 안에 표시되지 않습니다: {Format(propertyControlBounds[index])}.");
                    }
                }
            }

            if (artifactScope == "p2-text-role" && label.Contains("text-role", StringComparison.Ordinal))
            {
                var unsupportedRoleTarget = label.Contains("unsupported", StringComparison.Ordinal);
                var roleControlNames = unsupportedRoleTarget
                    ? new[] { "TextRoleComboBox", "TextRoleSemanticsNotice", "TextRoleDisabledReason" }
                    : new[] { "TextRoleCurrentValue", "TextRoleScopeNotice", "TextRoleComboBox", "TextRoleSemanticsNotice" };

                foreach (var name in roleControlNames)
                {
                    var index = Array.IndexOf(propertyControlNames, name);
                    var controlBounds = propertyControlBounds[index];
                    var isVisibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                              Contains(viewportBounds, controlBounds);
                    if (!isVisibleInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, name, controlBounds,
                            propertiesScrollViewer);
                    Qa.Check(isVisibleInViewport,
                        $"텍스트 역할 상세 캡처에서 {name} 전체가 실제 viewport에 표시되지 않습니다: {Format(controlBounds)}.");
                }

                var roleCombo = Find<ComboBox>(window, "TextRoleComboBox");
                if (unsupportedRoleTarget)
                {
                    Qa.Check(!Find<TextBlock>(window, "TextRoleCurrentValue").IsEffectivelyVisible &&
                             !Find<TextBlock>(window, "TextRoleScopeNotice").IsEffectivelyVisible,
                        "지원되지 않는 텍스트 역할 대상에 기본값 자동을 실제 역할처럼 표시하거나 안내를 중복했습니다.");
                    Qa.Check(!roleCombo.IsEffectivelyEnabled &&
                             Find<TextBlock>(window, "TextRoleDisabledReason").IsEffectivelyVisible &&
                             !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "TextRoleDisabledReason").Text),
                        "지원되지 않는 텍스트 역할 범위에 편집 비활성 사유가 표시되지 않습니다.");
                }
                else
                    Qa.Check(roleCombo.IsEffectivelyEnabled &&
                             Find<TextBlock>(window, "TextRoleScopeNotice").IsEffectivelyVisible &&
                             !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "TextRoleCurrentValue").Text),
                        "지원되는 텍스트 역할 대상에 현재 역할과 활성 선택이 표시되지 않습니다.");
            }

            if (artifactScope == "p2-bracket-kind")
            {
                var properties = viewModel.Properties;
                var currentValue = Find<TextBlock>(window, "CurrentBracketKindValue");
                var combo = Find<ComboBox>(window, "BracketKindComboBox");
                var disabledReason = Find<TextBlock>(window, "BracketKindDisabledReason");
                var notice = Find<TextBlock>(window, "BracketKindNotice");
                var expectedChoice = properties.BracketKindChoices.Single(choice =>
                    choice.Value == properties.DraftBracketKind);

                Qa.Equal(properties.CanEditBracketKind, combo.IsEffectivelyEnabled,
                    "괄호 모양 편집 가능 상태의 실제 ComboBox binding");
                Qa.Equal(properties.CanEditBracketKind && properties.CurrentBracketKind is not null,
                    currentValue.IsEffectivelyVisible,
                    "현재 괄호 모양은 전체 Brackets 선택에서만 표시");
                Qa.Equal(!properties.CanEditBracketKind &&
                         !string.IsNullOrWhiteSpace(properties.BracketKindDisabledReason),
                    disabledReason.IsEffectivelyVisible,
                    "지원되지 않는 괄호 선택/범위의 비활성 이유 표시");
                Qa.Equal(properties.CanEditBracketKind && !string.IsNullOrWhiteSpace(properties.BracketKindNotice),
                    notice.IsEffectivelyVisible,
                    "전체 괄호 편집 안내의 표시 상태");
                Qa.Check(ReferenceEquals(expectedChoice, combo.SelectedItem),
                    "괄호 초안이 안정된 SelectedBracketKindChoice 항목과 동기화되지 않습니다.");

                var detail = label.Contains("bracket-kind-detail", StringComparison.Ordinal);
                var unsupported = label.Contains("bracket-kind-unsupported", StringComparison.Ordinal);
                var stale = label.Contains("bracket-kind-stale", StringComparison.Ordinal);
                if (detail || unsupported || stale)
                {
                    var visibleNames = detail
                        ? new[] { "BracketKindCard", "CurrentBracketKindValue", "BracketKindComboBox", "BracketKindNotice" }
                        : new[] { "BracketKindCard", "BracketKindComboBox", "BracketKindDisabledReason" };
                    foreach (var name in visibleNames)
                    {
                        var index = Array.IndexOf(propertyControlNames, name);
                        var visibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                                Contains(viewportBounds, propertyControlBounds[index]);
                        if (!visibleInViewport)
                            SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                                panelBounds, scrollBounds, viewportBounds, name, propertyControlBounds[index],
                                propertiesScrollViewer);
                        Qa.Check(visibleInViewport,
                            $"괄호 모양 상세에서 {name} 전체가 실제 속성 viewport 안에 표시되지 않습니다: {Format(propertyControlBounds[index])}.");
                    }
                }

                if (unsupported)
                    Qa.Check(!properties.CanEditBracketKind && !currentValue.IsEffectivelyVisible &&
                             !combo.IsEffectivelyEnabled && !string.IsNullOrWhiteSpace(disabledReason.Text),
                        "괄호 전체 선택이 아닌 상태를 지원되는 괄호의 실제 모양처럼 표시했습니다.");

                if (stale)
                    Qa.Check(properties.IsDraftStale && properties.DraftBracketKind is not null &&
                             !properties.CanApply && !combo.IsEffectivelyEnabled &&
                             !string.IsNullOrWhiteSpace(disabledReason.Text),
                        "stale 괄호 초안이 보존되면서 적용 차단과 비활성 사유가 표시되지 않습니다.");
            }

            if (artifactScope == "p2-group-row-spacing")
            {
                var properties = viewModel.Properties;
                var spacingCard = Find<Border>(window, "GroupRowSpacingCard");
                var savedSpacing = Find<TextBlock>(window, "GroupRowSpacingCurrentValue");
                var meaningNotice = Find<TextBlock>(window, "GroupRowSpacingMeaningNotice");
                var draftBox = Find<TextBox>(window, "GroupRowSpacingDraftTextBox");
                var validation = Find<TextBlock>(window, "GroupRowSpacingValidationError");
                var disabledReason = Find<TextBlock>(window, "GroupRowSpacingDisabledReason");
                var notice = Find<TextBlock>(window, "GroupRowSpacingNotice");
                var latexNotice = Find<TextBlock>(window, "GroupRowSpacingLatexDiagnostic");
                var groupRowControlNames = new[]
                {
                    "GroupRowSpacingCard", "GroupRowSpacingCurrentValue", "GroupRowSpacingMeaningNotice",
                    "GroupRowSpacingDraftTextBox", "GroupRowSpacingValidationError", "GroupRowSpacingDisabledReason",
                    "GroupRowSpacingNotice", "GroupRowSpacingLatexDiagnostic"
                };
                var groupRowControls = new Control[]
                {
                    spacingCard, savedSpacing, meaningNotice, draftBox, validation, disabledReason, notice, latexNotice
                };
                var groupRowBounds = groupRowControls.Select((control, index) =>
                    !control.IsEffectivelyVisible || control is TextBlock text && string.IsNullOrWhiteSpace(text.Text)
                        ? new Rect()
                        : GetBounds(control, window, groupRowControlNames[index])).ToArray();

                Qa.Equal(properties.CanEditGroupRowSpacing, draftBox.IsEffectivelyEnabled,
                    "그룹 내부 행 간격 편집 가능 범위 binding");
                Qa.Equal(!properties.CanEditGroupRowSpacing && !string.IsNullOrWhiteSpace(properties.GroupRowSpacingDisabledReason),
                    disabledReason.IsEffectivelyVisible,
                    "현재 그룹 외 범위의 행 간격 비활성 이유 표시");
                Qa.Equal(properties.CanEditGroupRowSpacing && properties.CurrentGroupRowSpacing is not null,
                    savedSpacing.IsEffectivelyVisible,
                    "현재 그룹 저장값의 범위별 표시 상태");
                Qa.Equal(properties.CanEditGroupRowSpacing && !string.IsNullOrWhiteSpace(properties.GroupRowSpacingNotice),
                    notice.IsEffectivelyVisible,
                    "현재 그룹 행 간격 안내의 범위별 표시 상태");
                Qa.Equal(properties.CanEditGroupRowSpacing && !string.IsNullOrWhiteSpace(properties.GroupRowSpacingLatexDiagnostic),
                    latexNotice.IsEffectivelyVisible,
                    "현재 그룹의 LaTeX 미보존 안내 표시 상태");
                if (properties.CanEditGroupRowSpacing)
                {
                    var current = properties.CurrentGroupRowSpacing
                        ?? throw new InvalidOperationException("편집 가능한 그룹 행 간격의 현재값이 null입니다.");
                    var expectedSavedSpacingText = $"현재 그룹 저장값: {current.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} px";
                    if (!string.Equals(expectedSavedSpacingText, savedSpacing.Text, StringComparison.Ordinal))
                        SavePropertiesControlFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, "GroupRowSpacingCurrentValue",
                            groupRowBounds[1], propertiesScrollViewer, expectedSavedSpacingText,
                            savedSpacing.Text, current);
                    Qa.Equal(expectedSavedSpacingText, savedSpacing.Text ?? string.Empty,
                        "현재 그룹 저장값 round-trip 표시");
                    Qa.Equal(properties.DraftGroupRowSpacingText, draftBox.Text ?? string.Empty,
                        "그룹 행 간격 초안의 실제 TwoWay 표시");
                    Qa.Check(ToolTip.GetShowOnDisabled(draftBox),
                        "비활성 행 간격 입력의 사유 tooltip이 disabled 상태에서 표시되지 않습니다.");
                }
                else
                {
                    Qa.Check(!savedSpacing.IsEffectivelyVisible &&
                             string.Equals(disabledReason.Text, properties.GroupRowSpacingDisabledReason, StringComparison.Ordinal),
                        "현재 그룹이 아닌 범위에 저장값이 실제처럼 보이거나 비활성 이유가 다릅니다.");
                }

                if (label.Contains("group-spacing-detail", StringComparison.Ordinal))
                {
                    foreach (var name in new[]
                             {
                                 "GroupRowSpacingCard", "GroupRowSpacingCurrentValue", "GroupRowSpacingMeaningNotice",
                                 "GroupRowSpacingDraftTextBox", "GroupRowSpacingNotice"
                             })
                    {
                        var index = Array.IndexOf(propertyControlNames, name);
                        Qa.Check(index >= 0, "행 간격 상세 bounds 목록에 control이 없습니다: " + name);
                        var bounds = propertyControlBounds[index];
                        var visibleInViewport = propertyControls[index].IsEffectivelyVisible &&
                                                Contains(viewportBounds, bounds);
                        if (!visibleInViewport)
                            SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                                panelBounds, scrollBounds, viewportBounds, name, bounds, propertiesScrollViewer);
                        Qa.Check(visibleInViewport,
                            $"행 간격 상세에서 {name} 전체가 실제 속성 viewport에 표시되지 않습니다: {Format(bounds)}.");
                    }
                }

                if (label.Contains("group-spacing-disabled", StringComparison.Ordinal))
                {
                    var bounds = groupRowBounds[5];
                    var visibleInViewport = disabledReason.IsEffectivelyVisible && Contains(viewportBounds, bounds);
                    if (!visibleInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, "GroupRowSpacingDisabledReason", bounds,
                            propertiesScrollViewer);
                    Qa.Check(visibleInViewport,
                        $"비활성 범위의 그룹 행 간격 이유가 실제 viewport에 표시되지 않습니다: {Format(bounds)}.");
                }

                if (label.Contains("group-spacing-invalid", StringComparison.Ordinal))
                {
                    var bounds = groupRowBounds[4];
                    var visibleInViewport = validation.IsEffectivelyVisible && Contains(viewportBounds, bounds);
                    if (!visibleInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, "GroupRowSpacingValidationError", bounds,
                            propertiesScrollViewer);
                    Qa.Check(visibleInViewport && !string.IsNullOrWhiteSpace(validation.Text) &&
                             !properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
                        $"잘못된 간격 값의 오류/적용 차단이 보이지 않습니다: {Format(bounds)}.");
                }

                if (label.Contains("group-spacing-stale", StringComparison.Ordinal))
                {
                    var bounds = groupRowBounds[6];
                    var visibleInViewport = notice.IsEffectivelyVisible && Contains(viewportBounds, bounds);
                    if (!visibleInViewport)
                        SavePropertiesContainmentFailureEvidence(window, artifactDirectory, label,
                            panelBounds, scrollBounds, viewportBounds, "GroupRowSpacingNotice", bounds,
                            propertiesScrollViewer);
                    Qa.Check(visibleInViewport && properties.IsDraftStale &&
                             (notice.Text?.Contains("오래", StringComparison.Ordinal) ?? false) &&
                             !properties.CanApply && draftBox.Text == properties.DraftGroupRowSpacingText,
                        $"오래된 행 간격 초안 안내/차단/문자열 보존이 확인되지 않습니다: {Format(bounds)}.");
                }

                if (label.Contains("group-spacing-single-row", StringComparison.Ordinal))
                    Qa.Check(notice.Text?.Contains("화면상 변화가 없습니다", StringComparison.Ordinal) == true &&
                             !latexNotice.IsEffectivelyVisible,
                        "단일 행 그룹에 화면 효과 또는 LaTeX 간격 진단을 잘못 표시합니다.");

                var latexDiagnosticsViewer = Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer");
                var latexCodeExpander = Find<Expander>(window, "LatexCodeExpander");
                var layoutDiagnosticBanner = Find<Border>(window, "LatexLayoutDiagnosticBanner");
                var layoutDiagnosticList = Find<ItemsControl>(window, "LatexLayoutDiagnosticsList");
                groupRowSpacingControlRecords = groupRowControlNames.Select((name, index) => new NamedBounds(
                    name, Format(groupRowBounds[index]), groupRowControls[index].IsEffectivelyVisible)).ToArray();
                if (latexCodeExpander.IsExpanded)
                {
                    var viewerAttachedToWindow = latexDiagnosticsViewer.GetVisualAncestors()
                        .Any(ancestor => ReferenceEquals(ancestor, window));
                    var viewportOrigin = latexDiagnosticsViewer.TranslatePoint(new Point(0, 0), window);
                    if (!latexDiagnosticsViewer.IsEffectivelyVisible || !viewerAttachedToWindow || viewportOrigin is null)
                    {
                        SaveLatexDiagnosticsViewportFailureEvidence(window, artifactDirectory, label,
                            latexCodeExpander.IsExpanded, latexDiagnosticsViewer.IsEffectivelyVisible,
                            viewerAttachedToWindow, viewportOrigin, latexDiagnosticsViewer);
                        throw new InvalidOperationException(
                            $"펼친 LaTeX 진단 viewport가 실제 MainWindow 좌표에 연결되지 않았습니다: " +
                            $"expanded={latexCodeExpander.IsExpanded}, visible={latexDiagnosticsViewer.IsEffectivelyVisible}, " +
                            $"attached={viewerAttachedToWindow}, origin={viewportOrigin?.ToString() ?? "없음"}.");
                    }

                    var origin = viewportOrigin.GetValueOrDefault();
                    latexDiagnosticsViewportRecord = Format(new Rect(origin, latexDiagnosticsViewer.Viewport));
                }
                else
                {
                    // 접힌 Expander의 분리된 content는 IsEffectivelyVisible만으로 창 좌표가 보장되지 않는다.
                    // optional 진단 viewport는 측정하지 않고 JSON에는 null로 기록한다.
                    latexDiagnosticsViewportRecord = null;
                }

                if (label.Contains("latex-layout", StringComparison.Ordinal))
                {
                    Qa.Check(viewModel.CanExportLatex && !viewModel.LatexLayoutDiagnostics.IsEmpty &&
                             layoutDiagnosticBanner.IsEffectivelyVisible && layoutDiagnosticList.Items.Count > 0 &&
                             latexDiagnosticsViewer.IsEffectivelyVisible &&
                             !Find<Border>(window, "LatexCodeDiagnosticBanner").IsEffectivelyVisible,
                        "행 간격 충실도 안내와 차단형 LaTeX 제한을 구분하지 못했거나 진단이 없습니다.");
                }
            }

            propertiesEvidenceRecord = new PropertiesCaptureEvidence(
                Format(panelBounds), Format(scrollBounds), Format(viewportBounds),
                propertiesScrollViewer.Viewport.Width, propertiesScrollViewer.Viewport.Height,
                propertiesScrollViewer.Extent.Width, propertiesScrollViewer.Extent.Height,
                propertiesScrollViewer.Offset.X, propertiesScrollViewer.Offset.Y,
                viewModel.Properties.SelectedScope.ToString(), viewModel.Properties.TargetSummary,
                viewModel.Properties.SelectionNotice, viewModel.Properties.IsDraftStale,
                viewModel.Properties.StaleReason, viewModel.Properties.CanApply,
                viewModel.Properties.EffectiveTypesettingStyle.ToString(),
                viewModel.Properties.ExplicitTypesettingStyle?.ToString(),
                viewModel.Properties.TypesettingStyleSourceLabel,
                viewModel.Properties.DraftTypesettingStyle?.ToString(),
                viewModel.Properties.TypesettingStyleContextNotice,
                viewModel.Properties.DraftPinTypesettingStyle,
                viewModel.Properties.CanPinTypesettingStyle,
                viewModel.Properties.TypesettingPinDisabledReason,
                viewModel.Properties.TypesettingPinNotice,
                viewModel.Properties.IsFractionSelection,
                viewModel.Properties.FractionDisplayNotice,
                viewModel.Properties.CurrentTextRole.ToString(),
                viewModel.Properties.TextRoleScopeNotice,
                viewModel.Properties.TextRoleDisabledReason,
                viewModel.Properties.CanEditTextRole,
                viewModel.Properties.DraftTextRole?.ToString(),
                viewModel.LatexCodeDiagnostic,
                viewModel.CanExportLatex,
                viewModel.LatexTextDiagnostics.Length,
                viewModel.LatexIncompleteSlotNodeIds.Length,
                propertyControlNames.Select((name, index) => new NamedBounds(name,
                    Format(propertyControlBounds[index]), propertyControls[index].IsEffectivelyVisible)).ToArray(),
                label.EndsWith("-top", StringComparison.Ordinal) ? "top" :
                label.EndsWith("-bottom", StringComparison.Ordinal) ? "bottom" : "state",
                viewModel.Properties.CurrentGroupRowSpacing,
                viewModel.Properties.DraftGroupRowSpacingText,
                viewModel.Properties.CanEditGroupRowSpacing,
                viewModel.Properties.GroupRowSpacingValidationError,
                viewModel.Properties.GroupRowSpacingDisabledReason,
                viewModel.Properties.GroupRowSpacingNotice,
                viewModel.Properties.GroupRowSpacingLatexDiagnostic,
                viewModel.LatexLayoutDiagnostics.Length,
                latexDiagnosticsViewportRecord,
                groupRowSpacingControlRecords ?? [],
                viewModel.Properties.CurrentBracketKind?.ToString(),
                viewModel.Properties.CurrentBracketKindLabel,
                viewModel.Properties.DraftBracketKind?.ToString(),
                viewModel.Properties.CanEditBracketKind,
                viewModel.Properties.BracketKindDisabledReason,
                viewModel.Properties.BracketKindNotice);
        }

        if (artifactScope == "p2-structure-clear")
        {
            Qa.Check(viewerBounds.Height >= 257,
                $"구조/비우기 도구가 중앙 편집 viewport 최소 높이를 257px 아래로 줄였습니다: {Format(viewerBounds)}.");
            if (window.DataContext is not MainWindowViewModel viewModel)
                throw new InvalidOperationException("구조 시각 검증 창에 실제 MainWindowViewModel이 연결되지 않았습니다.");

            var inspector = Find<TabControl>(window, "EquationInspector");
            var structureTab = Find<TabItem>(window, "StructureInspectorTab");
            Qa.Check(ReferenceEquals(inspector.SelectedItem, structureTab) || structureTab.IsSelected,
                "구조/비우기 캡처 전에 오른쪽 구조 탭을 선택해야 합니다.");

            var inspectorPanel = Find<Grid>(window, "StructureInspectorPanel");
            var clearPanel = Find<Border>(window, "StructureClearPanel");
            var tree = Find<TreeView>(window, "StructureTree");
            var structureScrollViewer = tree.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault()
                ?? throw new InvalidOperationException("TreeView 내부 ScrollViewer를 하나로 특정하지 못했습니다.");
            var inspectorPanelBounds = GetBounds(inspectorPanel, window, "구조 inspector 콘텐츠");
            var clearPanelBounds = GetBounds(clearPanel, window, "범위 비우기 도구");
            var treeBounds = GetBounds(tree, window, "수식 구조 트리 viewport");
            var structureScrollViewerBounds = GetBounds(structureScrollViewer, window, "실제 구조 Tree ScrollViewer");
            var scrollViewerOrigin = structureScrollViewer.TranslatePoint(new Point(0, 0), window);
            Qa.Check(scrollViewerOrigin is Point, "실제 구조 ScrollViewer의 창 기준 좌표를 얻지 못했습니다.");
            var structureEffectiveViewportBounds = new Rect(scrollViewerOrigin!.Value, structureScrollViewer.Viewport);
            Qa.Check(Contains(clientBounds, inspectorPanelBounds) &&
                     Contains(inspectorPanelBounds, clearPanelBounds) &&
                     Contains(inspectorPanelBounds, treeBounds),
                $"구조/비우기 inspector가 창 경계를 벗어났습니다: panel={Format(inspectorPanelBounds)}, clear={Format(clearPanelBounds)}, tree={Format(treeBounds)}.");
            Qa.Check(treeBounds.Width >= 185 && treeBounds.Height >= 100,
                $"수식 구조 트리 viewport가 지나치게 작습니다: {Format(treeBounds)}.");
            Qa.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                ScrollViewer.GetHorizontalScrollBarVisibility(tree),
                "깊은 수식 구조의 가로 스크롤 접근성");
            Qa.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                ScrollViewer.GetHorizontalScrollBarVisibility(structureScrollViewer),
                "실제 TreeView 내부 ScrollViewer의 가로 스크롤 설정");
            Qa.Check(!Overlaps(treeBounds, visibleEditorBounds), "수식 구조 트리가 편집 viewport를 가립니다.");

            var selectionHeader = Find<StackPanel>(window, "StructureSelectionHeader");
            var selectionHeaderBounds = GetBounds(selectionHeader, window, "현재 구조 선택 경로");
            var activePathText = Find<TextBlock>(window, "StructureActivePath");
            var selectionModeText = Find<TextBlock>(window, "StructureSelectionModeText");
            Qa.Check(Contains(inspectorPanelBounds, selectionHeaderBounds) &&
                     Contains(selectionHeaderBounds, GetBounds(activePathText, window, "현재 구조 경로 텍스트")) &&
                     Contains(selectionHeaderBounds, GetBounds(selectionModeText, window, "구조 선택 방식 표시")),
                "현재 선택 경로가 구조 inspector 안에 표시되지 않습니다.");
            Qa.Check(activePathText.TextWrapping == Avalonia.Media.TextWrapping.Wrap &&
                     activePathText.MaxLines >= 2 && !string.IsNullOrWhiteSpace(activePathText.Text),
                "선택 경로에 읽을 수 있는 줄바꿈/요약이 없습니다.");
            var expectedSelectionMode = viewModel.Session.IsNodeSelected
                ? "전체 구조 선택"
                : viewModel.Session.Selection is not null ? "표면 부분 선택" : "표면 입력 위치";
            Qa.Equal(expectedSelectionMode, selectionModeText.Text, "구조 선택 모드 설명");

            var scopeCombo = Find<ComboBox>(window, "StructureClearScopeComboBox");
            var clearButton = Find<Button>(window, "ClearActiveScopeButton");
            var clearDescriptionText = Find<TextBlock>(window, "StructureClearDescription");
            var affectedRowsText = Find<TextBlock>(window, "StructureClearAffectedRows");
            var disabledReasonText = Find<TextBlock>(window, "StructureClearDisabledReason");
            var clearButtonLabel = Find<TextBlock>(window, "ClearActiveScopeButtonLabel");
            var clearControlNames = new[]
            {
                "StructureClearScopeComboBox", "ClearActiveScopeButton", "StructureClearDescription",
                "StructureClearAffectedRows", "StructureClearDisabledReason", "ClearActiveScopeButtonLabel"
            };
            var clearControls = new Control[]
            {
                scopeCombo, clearButton, clearDescriptionText, affectedRowsText, disabledReasonText, clearButtonLabel
            };
            var clearControlBounds = clearControls.Select((control, index) =>
                index == 4 && !control.IsEffectivelyVisible
                    ? GetOptionalBounds(control, window, clearControlNames[index])
                    : GetBounds(control, window, clearControlNames[index])).ToArray();
            Qa.Check(Contains(clearPanelBounds, clearControlBounds[0]) &&
                     Contains(clearPanelBounds, clearControlBounds[1]) &&
                     Contains(clearPanelBounds, clearControlBounds[2]) &&
                     Contains(clearPanelBounds, clearControlBounds[3]),
                "범위 선택·대상 설명·영향 행·비우기 버튼이 고정 Clear 구역 안에 들어오지 않습니다.");
            var clearButtonBounds = clearControlBounds[1];
            var clearButtonContentBounds = new Rect(
                clearButtonBounds.X + clearButton.BorderThickness.Left + clearButton.Padding.Left,
                clearButtonBounds.Y + clearButton.BorderThickness.Top + clearButton.Padding.Top,
                Math.Max(0, clearButtonBounds.Width - clearButton.BorderThickness.Left - clearButton.BorderThickness.Right -
                            clearButton.Padding.Left - clearButton.Padding.Right),
                Math.Max(0, clearButtonBounds.Height - clearButton.BorderThickness.Top - clearButton.BorderThickness.Bottom -
                            clearButton.Padding.Top - clearButton.Padding.Bottom));
            Qa.Check(clearButtonLabel.Text == "비우기" &&
                     clearButtonLabel.DesiredSize.Width <= clearButtonLabel.Bounds.Width + 0.5 &&
                     Contains(clearButtonContentBounds, clearControlBounds[5]),
                $"비우기 버튼 텍스트가 버튼 내부 폭에서 잘립니다: button={Format(clearButtonBounds)}, " +
                $"content={Format(clearButtonContentBounds)}, label={Format(clearControlBounds[5])}, " +
                $"desiredWidth={clearButtonLabel.DesiredSize.Width:R}.");
            Qa.Check(clearButton.IsEnabled == viewModel.CanClearActiveScope,
                "Clear 버튼의 활성 상태가 ViewModel CanClearActiveScope와 다릅니다.");
            Qa.Check(ToolTip.GetShowOnDisabled(clearButton),
                "비활성 Clear 버튼의 이유 tooltip이 disabled 상태에서 표시되지 않습니다.");
            if (viewModel.CanClearActiveScope)
                Qa.Check(string.IsNullOrWhiteSpace(viewModel.ClearScopeDisabledReason),
                    "실행 가능한 범위에 비활성 이유가 남아 있습니다.");
            else
                Qa.Check(!string.IsNullOrWhiteSpace(viewModel.ClearScopeDisabledReason) &&
                         disabledReasonText.IsEffectivelyVisible &&
                         string.Equals(disabledReasonText.Text, viewModel.ClearScopeDisabledReason, StringComparison.Ordinal),
                    "비활성 Clear 범위의 이유가 화면 텍스트로 표시되지 않습니다.");
            Qa.Check(string.Equals(clearDescriptionText.Text, viewModel.ClearScopeDescription, StringComparison.Ordinal) &&
                     int.TryParse(affectedRowsText.Text, out var affectedRows) &&
                     affectedRows == viewModel.ClearScopeAffectedRowCount,
                "Clear target/영향 행 표시가 ViewModel 선택 범위와 다릅니다.");
            Qa.Check(scopeCombo.SelectedValue is MathClearScope,
                "Clear 범위 ComboBox가 enum 값을 선택하지 않았습니다.");
            Qa.Equal(viewModel.SelectedClearScope, (MathClearScope)scopeCombo.SelectedValue!, "Clear 범위 ComboBox 선택");

            var expectedNodeId = viewModel.Session.IsNodeSelected
                ? viewModel.Session.SelectedNodeId
                : viewModel.Session.Caret.NodeId;
            Qa.Equal(expectedNodeId, viewModel.StructurePanel.SelectedNodeId,
                "구조 트리의 선택 노드가 현재 Session의 caret/구조 선택과 동기화되지 않았습니다.");
            Qa.Check(viewModel.StructurePanel.Items.Length == viewModel.Session.Workspace.Groups.Sum(group => group.Rows.Length),
                "구조 트리 최상위 항목이 실제 논리 행 root 수와 다릅니다.");
            foreach (var root in viewModel.StructurePanel.Items)
                AssertNodeProjection(root);

            var selectedItem = viewModel.StructurePanel.SelectedItem
                ?? throw new InvalidOperationException("현재 Session 선택에 대응하는 구조 트리 항목이 없습니다.");
            var selectedContainer = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .FirstOrDefault(candidate => candidate.DataContext is StructurePanelItem item &&
                                             item.MathNodeId == selectedItem.MathNodeId)
                ?? throw new InvalidOperationException("선택된 구조 노드의 TreeViewItem container가 생성되지 않았습니다.");
            Qa.Check(selectedContainer.IsSelected, "현재 활성 NodeId의 TreeViewItem 선택 상태가 표시되지 않습니다.");
            var selectedHeader = selectedContainer.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(candidate => candidate.Classes.Contains("structure-tree-entry") &&
                                             ReferenceEquals(candidate.DataContext, selectedItem))
                ?? throw new InvalidOperationException("선택 노드의 실제 header Border를 찾지 못했습니다.");
            var selectedItemBounds = GetBounds(selectedHeader, window, "실제 선택 구조 노드 header");
            var headerOriginInScrollViewer = selectedHeader.TranslatePoint(new Point(0, 0), structureScrollViewer);
            Qa.Check(headerOriginInScrollViewer is Point,
                "선택 header의 실제 ScrollViewer 기준 좌표를 얻지 못했습니다.");
            var headerScrollViewerBounds = new Rect(headerOriginInScrollViewer!.Value, selectedHeader.Bounds.Size);
            var scrollMetrics = new StructureScrollMetrics(
                structureScrollViewer.Viewport.Width,
                structureScrollViewer.Viewport.Height,
                structureScrollViewer.Extent.Width,
                structureScrollViewer.Extent.Height,
                structureScrollViewer.Offset.X,
                structureScrollViewer.Offset.Y,
                ScrollViewer.GetHorizontalScrollBarVisibility(tree).ToString(),
                ScrollViewer.GetHorizontalScrollBarVisibility(structureScrollViewer).ToString());
            var isHeaderFullyVisible = Contains(structureEffectiveViewportBounds, selectedItemBounds);
            if (!isHeaderFullyVisible)
                SaveStructureRevealFailureEvidence(window, artifactDirectory, label,
                    treeBounds, structureScrollViewerBounds, structureEffectiveViewportBounds,
                    selectedItemBounds, headerScrollViewerBounds, selectedItem.MathNodeId, scrollMetrics);
            Qa.Check(isHeaderFullyVisible,
                $"선택된 깊은 수식 노드 header가 실제 내부 ScrollViewer viewport 안에 완전히 노출되지 않습니다: " +
                $"item={Format(selectedItemBounds)}, tree={Format(treeBounds)}, " +
                $"viewport={Format(structureEffectiveViewportBounds)}, " +
                $"headerInViewer={Format(headerScrollViewerBounds)}, extent={scrollMetrics.ExtentWidth:R}×{scrollMetrics.ExtentHeight:R}, " +
                $"offset={scrollMetrics.OffsetX:R},{scrollMetrics.OffsetY:R}, " +
                $"horizontal={scrollMetrics.InnerHorizontalScrollVisibility}.");
            foreach (var parentItem in selectedContainer.GetVisualAncestors().OfType<TreeViewItem>())
                Qa.Check(parentItem.IsExpanded,
                    $"선택된 구조 노드의 상위 항목이 접혀 있습니다: {parentItem.DataContext}.");

            structureInspectorBoundsRecord = Format(inspectorPanelBounds);
            structureClearPanelBoundsRecord = Format(clearPanelBounds);
            structureTreeBoundsRecord = Format(treeBounds);
            structureScrollViewerBoundsRecord = Format(structureScrollViewerBounds);
            structureEffectiveViewportBoundsRecord = Format(structureEffectiveViewportBounds);
            structureHeaderScrollViewerBoundsRecord = Format(headerScrollViewerBounds);
            structureScrollMetrics = scrollMetrics;
            structureSelectedItemBoundsRecord = Format(selectedItemBounds);
            structureSelectedItemIsSelected = selectedContainer.IsSelected;
            structureSelectionMode = selectionModeText.Text;
            structureHorizontalScrollVisibility = ScrollViewer.GetHorizontalScrollBarVisibility(tree).ToString();
            structureControlRecords = clearControlNames
                .Select((name, index) => new NamedBounds(name, Format(clearControlBounds[index]),
                    clearControls[index].IsEffectivelyVisible))
                .ToArray();
            structureSelectedNodeId = selectedItem.MathNodeId.ToString("D");
            structureActivePath = activePathText.Text;
            structureClearDescription = viewModel.ClearScopeDescription;
            structureClearDisabledReason = viewModel.ClearScopeDisabledReason;
            structureClearAffectedRows = viewModel.ClearScopeAffectedRowCount;
            structureCanClear = viewModel.CanClearActiveScope;
        }

        var lifecycleOverlay = Find<Grid>(window, "PendingWorkspaceActionOverlay");
        if (artifactScope == "p1-lifecycle-protection" && lifecycleOverlay.IsVisible)
        {
            var overlayBounds = GetBounds(lifecycleOverlay, window, "수명 보호 modal overlay");
            Qa.Check(Contains(clientBounds, overlayBounds),
                $"수명 보호 overlay가 창 경계를 벗어났습니다: {Format(overlayBounds)}.");

            var dialogBounds = GetBounds(Find<Border>(window, "PendingWorkspaceActionDialog"), window,
                "수명 보호 확인 대화상자");
            Qa.Check(Contains(clientBounds, dialogBounds) && Contains(overlayBounds, dialogBounds),
                $"수명 보호 대화상자가 창 안에 온전히 배치되지 않았습니다: {Format(dialogBounds)}.");
            lifecycleDialogRecord = Format(dialogBounds);

            var targetCardBounds = GetBounds(Find<Border>(window, "PendingWorkspaceActionTargetCard"), window,
                "수명 보호 대상 카드");
            var target = Find<TextBlock>(window, "PendingWorkspaceActionTarget");
            var targetBounds = GetBounds(target, window, "수명 보호 대상 이름");
            Qa.Check(Contains(dialogBounds, targetCardBounds) && Contains(targetCardBounds, targetBounds),
                $"긴 대상 이름 또는 대상 카드가 dialog 밖으로 넘쳤습니다: card={Format(targetCardBounds)}, target={Format(targetBounds)}.");
            Qa.Check(target.TextWrapping == Avalonia.Media.TextWrapping.Wrap,
                "수명 보호 대상 이름에 줄바꿈이 설정되지 않았습니다.");
            if ((target.Text?.Length ?? 0) >= 160)
                Qa.Check(targetBounds.Height >= 24,
                    $"긴 대상 이름이 줄바꿈되지 않았습니다: {Format(targetBounds)}.");
            lifecycleTargetCardRecord = Format(targetCardBounds);
            lifecycleTargetRecord = Format(targetBounds);

            var choiceNames = new[]
            {
                "SavePendingWorkspaceActionButton", "DiscardPendingWorkspaceActionButton",
                "CancelPendingWorkspaceActionButton"
            };
            var choiceBounds = choiceNames
                .Select(name => GetBounds(Find<Button>(window, name), window, name))
                .ToArray();
            foreach (var pair in choiceBounds.Select((bounds, index) => (bounds, index)))
            {
                Qa.Check(Contains(dialogBounds, pair.bounds),
                    $"수명 보호 버튼이 대화상자 밖으로 벗어났습니다: {choiceNames[pair.index]} {Format(pair.bounds)}.");
                for (var otherIndex = pair.index + 1; otherIndex < choiceBounds.Length; otherIndex++)
                    Qa.Check(!Overlaps(pair.bounds, choiceBounds[otherIndex]),
                        $"수명 보호 선택 버튼이 서로 겹칩니다: {choiceNames[pair.index]} / {choiceNames[otherIndex]}.");
            }
            lifecycleChoiceRecords = choiceNames
                .Select((name, index) => new NamedBounds(name, Format(choiceBounds[index])))
                .ToArray();

            var error = Find<TextBlock>(window, "PendingWorkspaceActionError");
            lifecycleErrorText = string.IsNullOrWhiteSpace(error.Text) ? null : error.Text;
            if (lifecycleErrorText is not null && error.IsVisible)
            {
                var errorBounds = GetBounds(error, window, "수명 보호 저장 오류");
                Qa.Check(Contains(dialogBounds, errorBounds),
                    $"저장 오류 메시지가 대화상자 밖으로 넘쳤습니다: {Format(errorBounds)}.");
                Qa.Check(choiceBounds.All(choice => !Overlaps(errorBounds, choice)),
                    "저장 오류 메시지가 confirmation 버튼을 덮습니다.");
                lifecycleErrorRecord = Format(errorBounds);
            }
        }

        if (artifactScope == "p1-recovery-recent")
        {
            var libraryTab = Find<TabItem>(window, "WorkspaceLibraryTab");
            Qa.Check(libraryTab.IsSelected,
                "최근 작업/복구본 시각 검증 전에 작업 탭이 선택되어 있지 않습니다.");
            var libraryScroll = Find<ScrollViewer>(window, "WorkspaceLibraryScrollViewer");
            var libraryBounds = GetBounds(libraryScroll, window, "최근 작업/복구본 viewport");
            Qa.Check(Contains(clientBounds, libraryBounds),
                $"최근 작업/복구본 viewport가 창 경계를 벗어났습니다: {Format(libraryBounds)}.");
            Qa.Check(libraryBounds.Width >= 210 && libraryBounds.Height >= 220,
                $"최근 작업/복구본 viewport가 지나치게 작습니다: {Format(libraryBounds)}.");
            Qa.Check(!Overlaps(libraryBounds, visibleEditorBounds),
                "최근 작업/복구본 inspector와 수식 편집 viewport가 겹칩니다.");

            var libraryHeaderBounds = GetBounds(Find<Grid>(window, "RecentWorkspacesHeader"), window,
                "최근 작업 제목/새로 고침 도구");
            var recentActions = Find<WrapPanel>(window, "RecentWorkspaceActions");
            var recentActionsBounds = GetBounds(recentActions, window, "최근 작업 버튼 묶음");
            var visibleRecentActions = Intersect(recentActionsBounds, libraryBounds);
            var preview = Find<Border>(window, "RecentWorkspacePreview");
            var previewBounds = preview.IsVisible
                ? GetBounds(preview, window, "최근 작업 선택 미리보기")
                : default;
            var visiblePreview = preview.IsVisible ? Intersect(previewBounds, libraryBounds) : default;
            var recoveryActions = Find<WrapPanel>(window, "RecoveryActions");
            var recoveryActionsBounds = GetBounds(recoveryActions, window, "복구본 버튼 묶음");
            var visibleRecoveryActions = Intersect(recoveryActionsBounds, libraryBounds);
            var deleteConfirmation = Find<Border>(window, "RecoveryDeleteConfirmation");
            var visibleDeleteConfirmation = deleteConfirmation.IsVisible
                ? Intersect(GetBounds(deleteConfirmation, window, "복구본 삭제 확인"), libraryBounds)
                : default;
            var maximumOffset = Math.Max(0, libraryScroll.Extent.Height - libraryScroll.Viewport.Height);
            var atTop = libraryScroll.Offset.Y <= 0.5;
            var atBottom = libraryScroll.Offset.Y >= maximumOffset - 0.5;
            if (atTop)
            {
                Qa.Check(Contains(libraryBounds, libraryHeaderBounds),
                    $"최근 작업 제목/새로 고침 도구가 스크롤 viewport 안에 놓이지 않았습니다: {Format(libraryHeaderBounds)}.");
                Qa.Check(Contains(libraryBounds, recentActionsBounds),
                    $"최근 작업 버튼 묶음이 초기 inspector viewport 안에 완전히 표시되지 않습니다: {Format(recentActionsBounds)}.");
            }
            else if (atBottom)
            {
                Qa.Check((visibleRecoveryActions.Width > 0 && visibleRecoveryActions.Height > 0) ||
                         (visibleDeleteConfirmation.Width > 0 && visibleDeleteConfirmation.Height > 0),
                    "스크롤 끝에서 복구본 작업 도구나 삭제 확인이 inspector viewport에 보이지 않습니다.");
            }
            else
            {
                Qa.Check((visibleRecentActions.Width > 0 && visibleRecentActions.Height > 0) ||
                         (visibleRecoveryActions.Width > 0 && visibleRecoveryActions.Height > 0) ||
                         (visiblePreview.Width > 0 && visiblePreview.Height > 0),
                    "최근 작업/복구본 작업 도구나 미리보기가 inspector viewport에서 전혀 보이지 않습니다.");
            }
            if (visiblePreview.Width > 0 && visiblePreview.Height > 0)
                libraryPreviewRecord = Format(visiblePreview);

            if (label == "library-preview")
            {
                Qa.Check(preview.IsVisible && Contains(libraryBounds, previewBounds),
                    "전용 미리보기 캡처에서 전체 미리보기가 viewport에 들어오지 않았습니다.");
                var nameText = Find<TextBlock>(window, "SelectedRecentWorkspaceNameText");
                var pathText = Find<TextBlock>(window, "SelectedRecentWorkspacePathText");
                var statusText = Find<TextBlock>(window, "SelectedRecentWorkspaceStatusText");
                var modifiedText = Find<TextBlock>(window, "SelectedRecentWorkspaceModifiedText");
                var nameBounds = GetBounds(nameText, window, "최근 작업 이름 미리보기");
                var pathBounds = GetBounds(pathText, window, "최근 작업 경로 미리보기");
                var statusBounds = GetBounds(statusText, window, "최근 작업 상태 미리보기");
                var modifiedBounds = GetBounds(modifiedText, window, "최근 작업 수정 시각 미리보기");
                foreach (var metadataBounds in new[] { nameBounds, pathBounds, statusBounds, modifiedBounds })
                    Qa.Check(Contains(previewBounds, metadataBounds) && Contains(libraryBounds, metadataBounds),
                        $"미리보기 metadata가 preview/viewport 경계를 벗어났습니다: {Format(metadataBounds)}.");
                Qa.Check(nameText.MaxLines == 1 &&
                         nameText.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis,
                    "최근 작업 미리보기 이름이 한 줄 ellipsis로 제한되지 않았습니다.");
                Qa.Check(pathText.MaxLines is >= 2 and <= 3 &&
                         pathText.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis &&
                         !string.IsNullOrWhiteSpace(statusText.Text) &&
                         !string.IsNullOrWhiteSpace(modifiedText.Text),
                    "긴 작업명 때문에 경로·상태·수정 시각 metadata가 화면에서 밀렸습니다.");
                Qa.Check(nameBounds.Top < pathBounds.Top && pathBounds.Top < statusBounds.Top &&
                         statusBounds.Top < modifiedBounds.Top,
                    "최근 작업 미리보기 metadata 순서가 예상과 다릅니다.");
                libraryPreviewNameRecord = Format(nameBounds);
                libraryPreviewPathRecord = Format(pathBounds);
                libraryPreviewStatusRecord = Format(statusBounds);
                libraryPreviewModifiedRecord = Format(modifiedBounds);
            }

            libraryViewportRecord = Format(libraryBounds);
            libraryHeaderRecord = Format(libraryHeaderBounds);
            libraryRecentActionsRecord = Format(visibleRecentActions);
            libraryRecoveryActionsRecord = visibleRecoveryActions.Width > 0 && visibleRecoveryActions.Height > 0
                ? Format(visibleRecoveryActions)
                : null;
            libraryVerticalOffset = libraryScroll.Offset.Y;
        }

        var pngPath = Path.Combine(artifactDirectory, "main-window.png");
        var bitmapSize = new PixelSize((int)Math.Ceiling(clientSize.Width), (int)Math.Ceiling(clientSize.Height));
        using (var bitmap = new RenderTargetBitmap(bitmapSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "전체 셸 오프스크린 PNG가 유효하지 않습니다.");
        var evidence = new RowShellVisualEvidence(
            label,
            artifactScope,
            clientSize.Width,
            clientSize.Height,
            platformHandle.HandleDescriptor,
            platformHandle.Handle.ToInt64(),
            Format(viewerBounds),
            Format(editorBounds),
            Format(visibleEditorBounds),
            minimumVisibleEditorWidth,
            headerActionNames.Select((name, index) => new NamedBounds(name, Format(headerActionBounds[index]))).ToArray(),
            groupToolbarNames.Select((name, index) => new NamedBounds(name, Format(groupToolbarBounds[index]))).ToArray(),
            rowToolbarNames.Select((name, index) => new NamedBounds(name, Format(rowToolbarBounds[index]))).ToArray(),
            lifecycleDialogRecord,
            lifecycleTargetCardRecord,
            lifecycleTargetRecord,
            lifecycleChoiceRecords,
            lifecycleErrorText,
            lifecycleErrorRecord,
            libraryViewportRecord,
            libraryHeaderRecord,
            libraryRecentActionsRecord,
            libraryRecoveryActionsRecord,
            libraryPreviewRecord,
            libraryPreviewNameRecord,
            libraryPreviewPathRecord,
            libraryPreviewStatusRecord,
            libraryPreviewModifiedRecord,
            libraryVerticalOffset,
            palettePanelRecord,
            paletteTabsRecord,
            paletteScrollRecord,
            paletteControlRecords,
            paletteVerticalOffset,
            paletteCaptureMode,
            templatePanelRecord,
            templateScrollViewerRecord,
            templateButtonRecords,
            templateVerticalOffset,
            templateCaptureEdge,
            structureInspectorBoundsRecord,
            structureClearPanelBoundsRecord,
            structureTreeBoundsRecord,
            structureScrollViewerBoundsRecord,
            structureEffectiveViewportBoundsRecord,
            structureHeaderScrollViewerBoundsRecord,
            structureScrollMetrics,
            structureSelectedItemBoundsRecord,
            structureSelectedItemIsSelected,
            structureSelectionMode,
            structureHorizontalScrollVisibility,
            structureControlRecords,
            structureSelectedNodeId,
            structureActivePath,
            structureClearDescription,
            structureClearDisabledReason,
            structureClearAffectedRows,
            structureCanClear,
            propertiesEvidenceRecord,
            Path.GetFileName(pngPath),
            Convert.ToHexString(SHA256.HashData(pngBytes)));
        File.WriteAllText(Path.Combine(artifactDirectory, "bounds.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    public static void CaptureTextRoleAndVerify(MainWindow window, string label) =>
        CaptureAndVerify(window, label, "p2-text-role");

    public static void CaptureBracketKindAndVerify(MainWindow window, string label) =>
        CaptureAndVerify(window, label, "p2-bracket-kind");

    public static void CaptureLatexCopyAndVerify(MainWindow window, string label)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("LaTeX 복사 캡처 전에 Headless 창 handle이 없습니다.");
        var handleDescriptor = handle.HandleDescriptor
            ?? throw new InvalidOperationException("LaTeX 복사 캡처의 handle 설명자가 없습니다.");
        Qa.Check(handleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "LaTeX 복사 UI 증거는 STUB/0 Headless 창에서만 생성할 수 있습니다.");
        if (window.DataContext is not MainWindowViewModel viewModel)
            throw new InvalidOperationException("LaTeX 복사 캡처 창에 실제 MainWindowViewModel이 없습니다.");

        Dispatcher.UIThread.RunJobs();
        var client = window.ClientSize;
        var isMinimum = Near(client.Width, 1060) && Near(client.Height, 700);
        var isDefault = Near(client.Width, 1360) && Near(client.Height, 880);
        Qa.Check(isMinimum || isDefault,
            $"지원하지 않는 LaTeX 복사 캡처 크기입니다: {client.Width:R}×{client.Height:R}.");
        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            throw new ArgumentException("LaTeX 복사 캡처 라벨은 파일 이름 하나여야 합니다.", nameof(label));

        var directory = Path.Combine(FindRepositoryRoot(), "doc", "verification", "artifacts", "p2-latex-copy",
            $"{label}-{(isMinimum ? "1060x700" : "1360x880")}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var pngPath = Path.Combine(directory, "main-window.png");
        using (var bitmap = new RenderTargetBitmap(
                   new PixelSize((int)Math.Ceiling(client.Width), (int)Math.Ceiling(client.Height)),
                   new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }
        var pngBytes = File.ReadAllBytes(pngPath);

        var button = Find<Button>(window, "CopyLatexButton");
        var buttonLabel = Find<TextBlock>(window, "CopyLatexButtonLabel");
        var expander = Find<Expander>(window, "LatexCodeExpander");
        var codeGrid = Find<Grid>(window, "LatexCodePanelGrid");
        var codeView = Find<LatexCodeSurface>(window, "LatexCodeView");
        var diagnostics = Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer");
        var statusBanner = Find<Border>(window, "LatexCopyStatusBanner");
        var statusText = Find<TextBlock>(window, "LatexCopyStatusText");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var copyButtonBounds = GetBounds(button, window, "전체 LaTeX 코드 복사 버튼");
        var copyLabelBounds = GetBounds(buttonLabel, window, "전체 LaTeX 코드 복사 버튼 설명");
        var expanderBounds = GetBounds(expander, window, "LaTeX 코드 패널");
        var editorViewportBounds = GetBounds(editorViewport, window, "중앙 편집 viewport");
        var expanderExpanded = expander.IsExpanded;
        var expanderContentAttached = HasVisualAncestor(codeGrid, window);
        var hasCopyMessage = !string.IsNullOrWhiteSpace(viewModel.LatexCopyMessage);
        var codeGridCoordinatesAvailable = TryGetLatexCopyContentBounds(
            codeGrid, window, expanderExpanded, out var codeGridBounds);
        var codeViewCoordinatesAvailable = TryGetLatexCopyContentBounds(
            codeView, window, expanderExpanded, out var codeViewBounds);
        var diagnosticsCoordinatesAvailable = TryGetLatexCopyContentBounds(
            diagnostics, window, expanderExpanded, out var diagnosticsBounds);
        var statusCoordinatesAvailable = TryGetLatexCopyContentBounds(
            statusBanner, window, expanderExpanded, out var statusBounds);
        var statusTextCoordinatesAvailable = TryGetLatexCopyContentBounds(
            statusText, window, expanderExpanded, out var statusTextBounds);
        var diagnosticsViewportBounds = diagnosticsCoordinatesAvailable
            ? new Rect(new Point(diagnosticsBounds.X, diagnosticsBounds.Y), diagnostics.Viewport)
            : new Rect();
        var ancestors = button.GetVisualAncestors().ToArray();
        var buttonBelongsToExpander = ancestors.Any(ancestor => ReferenceEquals(ancestor, expander));
        var tooltip = ToolTip.GetTip(button)?.ToString() ?? string.Empty;
        var statusVisibilityRequested = hasCopyMessage && expanderExpanded;
        var statusRawVisible = statusBanner.IsVisible;
        var statusVisible = statusVisibilityRequested && statusCoordinatesAvailable &&
                            statusBanner.IsEffectivelyVisible;
        var isCollapsedCapture = label.Contains("collapsed", StringComparison.Ordinal);
        var isExpandedCapture = label.Contains("expanded", StringComparison.Ordinal);
        var evidence = new LatexCopyShellEvidence(
            "LATEX-COPY-UI", label, "Headless UI 상태의 검토용 전체 창 캡처이며 복사한 LaTeX 코드나 제품 출력물이 아닙니다.",
            client.Width, client.Height, handleDescriptor, handle.Handle.ToInt64(),
            Format(copyButtonBounds), Format(copyLabelBounds), Format(expanderBounds),
            Format(codeGridBounds), Format(codeViewBounds), Format(diagnosticsBounds),
            Format(diagnosticsViewportBounds), Format(statusBounds), Format(statusTextBounds),
            button.IsEffectivelyEnabled, viewModel.CanCopyLatex, viewModel.IsCopyingLatex,
            buttonLabel.Text ?? string.Empty, ToolTip.GetShowOnDisabled(button),
            tooltip, viewModel.LatexCopyDisabledReason, viewModel.LatexCopyMessage,
            expanderExpanded, expanderContentAttached,
            codeGridCoordinatesAvailable, codeViewCoordinatesAvailable, diagnosticsCoordinatesAvailable,
            statusRawVisible, statusVisibilityRequested, statusCoordinatesAvailable,
            statusTextCoordinatesAvailable,
            codeView.IsReadOnly, codeView.SourceText.Length,
            codeView.SelectionStart, codeView.SelectionEnd, editorViewport.Viewport.Height,
            editorViewportBounds.Width, statusVisible, hasCopyMessage, isCollapsedCapture, isExpandedCapture,
            viewModel.Session.Revision, viewModel.Session.IsDirty, viewModel.Session.CanUndo,
            viewModel.OutputPreview.State.ToString(), viewModel.IsOutputPreviewVisible,
            buttonBelongsToExpander, Path.GetFileName(pngPath),
            Convert.ToHexString(SHA256.HashData(pngBytes)));
        File.WriteAllText(Path.Combine(directory, "bounds.json"), JsonSerializer.Serialize(evidence, JsonOptions));

        Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "LaTeX 복사 UI 전체 창 PNG가 유효하지 않습니다.");
        var clientBounds = new Rect(0, 0, client.Width, client.Height);
        Qa.Check(Contains(clientBounds, copyButtonBounds) && Contains(clientBounds, copyLabelBounds) &&
                 Contains(copyButtonBounds, copyLabelBounds),
            $"복사 버튼/레이블이 창 또는 버튼 안에 온전히 표시되지 않습니다: button={Format(copyButtonBounds)}, label={Format(copyLabelBounds)}.");
        Qa.Check(buttonBelongsToExpander,
            "복사 버튼이 기존 LaTeX Expander 헤더 안에 있지 않습니다.");
        Qa.Equal(viewModel.CanCopyLatex, button.IsEffectivelyEnabled,
            "전체 LaTeX 복사 버튼의 실제 활성 상태 binding");
        Qa.Check(ToolTip.GetShowOnDisabled(button),
            "비활성 복사 버튼에서도 안내 tooltip을 표시하도록 설정되지 않았습니다.");
        Qa.Equal(!string.IsNullOrWhiteSpace(viewModel.LatexCopyDisabledReason),
            !string.IsNullOrWhiteSpace(tooltip), "비활성 사유 tooltip 존재 상태");
        Qa.Equal(viewModel.LatexCopyMessage, statusText.Text ?? string.Empty,
            "복사 상태 배너가 실제 ViewModel 문구와 동기화됨");
        Qa.Equal(statusVisibilityRequested, statusVisible,
            "복사 상태 배너가 펼친 패널 안에서 실제 좌표를 가질 때 표시됨");
        if (statusVisible)
            Qa.Check(diagnosticsCoordinatesAvailable && statusTextCoordinatesAvailable &&
                     diagnostics.IsEffectivelyVisible && Contains(diagnosticsViewportBounds, statusBounds) &&
                     Contains(statusBounds, statusTextBounds),
                $"복사 상태 문구가 기존 진단 viewport 안에 완전히 표시되지 않습니다: " +
                $"viewer={Format(diagnosticsViewportBounds)}, banner={Format(statusBounds)}, text={Format(statusTextBounds)}.");
        if (isCollapsedCapture)
        {
            var expectedHeight = isMinimum ? 257 : 437;
            Qa.Check(!expander.IsExpanded && editorViewport.Viewport.Height >= expectedHeight,
                $"접힌 LaTeX 패널에서 편집 viewport 높이 기준을 지키지 않습니다: expected≥{expectedHeight}, actual={editorViewport.Viewport.Height:R}.");
            Qa.Check(!codeGridCoordinatesAvailable && !codeViewCoordinatesAvailable &&
                     !diagnosticsCoordinatesAvailable && !statusCoordinatesAvailable &&
                     !statusTextCoordinatesAvailable,
                "접힌 Expander 콘텐츠가 좌표 없이 숨겨진 선택 항목으로 기록되지 않았습니다.");
        }
        if (isExpandedCapture)
        {
            Qa.Check(expanderExpanded && expanderContentAttached && codeGridCoordinatesAvailable &&
                     codeViewCoordinatesAvailable && codeGrid.IsEffectivelyVisible && codeView.IsEffectivelyVisible &&
                     codeView.IsReadOnly && codeView.SourceText == viewModel.LatexCode,
                "펼친 LaTeX 패널이 전체 원문을 읽기 전용으로 보여주지 않습니다.");
            if (diagnostics.IsEffectivelyVisible)
                Qa.Check(diagnosticsCoordinatesAvailable,
                    "펼친 패널에서 실제 표시 중인 진단 스크롤의 창 좌표를 얻지 못했습니다.");
            if (statusBanner.IsEffectivelyVisible)
                Qa.Check(statusCoordinatesAvailable && statusTextCoordinatesAvailable,
                    "펼친 패널에서 실제 표시 중인 복사 상태의 창 좌표를 얻지 못했습니다.");
            Qa.Check(codeGrid.Bounds.Height <= 160.5 && codeView.Bounds.Height >= 90 &&
                     codeView.Bounds.Height <= 110.5 && Contains(clientBounds, codeGridBounds) &&
                     Contains(clientBounds, codeViewBounds) && !Overlaps(editorViewportBounds, codeGridBounds),
                $"펼친 LaTeX 패널/원문이 기존 높이 한도, 창 경계 또는 중앙 편집 영역과 겹칩니다: " +
                $"grid={Format(codeGridBounds)}, source={Format(codeViewBounds)}, editor={Format(editorViewportBounds)}.");
        }
        Console.WriteLine($"LATEX-COPY-UI artifact {Path.Combine(directory, "bounds.json")}");
    }

    public static void CaptureLatexHighlightAndVerify(MainWindow window, string label)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("구문 강조 캡처 전에 Headless 창 handle이 없습니다.");
        var handleDescriptor = handle.HandleDescriptor
            ?? throw new InvalidOperationException("구문 강조 캡처의 handle 설명자가 없습니다.");
        Qa.Check(handleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "구문 강조 UI 증거는 STUB/0 Headless 창에서만 생성할 수 있습니다.");
        if (window.DataContext is not MainWindowViewModel viewModel)
            throw new InvalidOperationException("구문 강조 캡처 창에 실제 MainWindowViewModel이 없습니다.");

        Dispatcher.UIThread.RunJobs();
        var client = window.ClientSize;
        var isMinimum = Near(client.Width, 1060) && Near(client.Height, 700);
        var isDefault = Near(client.Width, 1360) && Near(client.Height, 880);
        Qa.Check(isMinimum || isDefault,
            $"지원하지 않는 구문 강조 캡처 크기입니다: {client.Width:R}×{client.Height:R}.");
        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            throw new ArgumentException("구문 강조 캡처 라벨은 파일 이름 하나여야 합니다.", nameof(label));

        var directory = Path.Combine(FindRepositoryRoot(), "doc", "verification", "artifacts", "p2-latex-highlight",
            $"{label}-{(isMinimum ? "1060x700" : "1360x880")}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pngPath = Path.Combine(directory, "main-window.png");
        using (var bitmap = new RenderTargetBitmap(
                   new PixelSize((int)Math.Ceiling(client.Width), (int)Math.Ceiling(client.Height)),
                   new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        var expander = Find<Expander>(window, "LatexCodeExpander");
        var codeGrid = Find<Grid>(window, "LatexCodePanelGrid");
        var codeFrame = Find<Border>(window, "LatexCodeViewFrame");
        var codeSurface = Find<LatexCodeSurface>(window, "LatexCodeView");
        var codeScroll = codeSurface.CodeScrollViewer;
        var textSurface = codeSurface.TextSurface;
        var diagnostics = Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var highlightToggle = Find<ToggleButton>(window, "LatexHighlightToggle");
        var copyButton = Find<Button>(window, "CopyLatexButton");
        var codeGridCoordinatesAvailable = TryGetLatexCopyContentBounds(codeGrid, window, expander.IsExpanded,
            out var codeGridBounds);
        var frameCoordinatesAvailable = TryGetLatexCopyContentBounds(codeFrame, window, expander.IsExpanded,
            out var codeFrameBounds);
        var surfaceCoordinatesAvailable = TryGetLatexCopyContentBounds(codeSurface, window, expander.IsExpanded,
            out var codeSurfaceBounds);
        var codeScrollCoordinatesAvailable = TryGetLatexCopyContentBounds(codeScroll, window, expander.IsExpanded,
            out var codeScrollBounds);
        var textCoordinatesAvailable = TryGetLatexCopyContentBounds(textSurface, window, expander.IsExpanded,
            out var textBounds);
        var diagnosticCoordinatesAvailable = TryGetLatexCopyContentBounds(diagnostics, window, expander.IsExpanded,
            out var diagnosticBounds);
        var editorBounds = GetBounds(editorViewport, window, "중앙 편집 viewport");
        var toggleBounds = GetBounds(highlightToggle, window, "구문 강조 전환");
        var copyBounds = GetBounds(copyButton, window, "전체 코드 복사");
        var source = viewModel.LatexSyntax.Source;
        var inlineCollection = textSurface.Inlines;
        var runs = inlineCollection?.OfType<Run>().ToArray() ?? Array.Empty<Run>();
        var runTexts = runs.Select(run => run.Text ?? string.Empty).ToArray();
        var typography = runs.Select(run => new
        {
            family = run.FontFamily?.Name,
            size = run.FontSize,
            style = run.FontStyle.ToString(),
            weight = run.FontWeight.ToString(),
            stretch = run.FontStretch.ToString(),
            foreground = run.Foreground?.ToString()
        }).ToArray();
        var evidence = new
        {
            caseId = "LATEX-HIGHLIGHT-UI",
            label,
            evidencePurpose = "Headless UI 상태의 검토용 전체 창 캡처이며 LaTeX 제품 출력물이 아닙니다.",
            window = new { width = client.Width, height = client.Height },
            handle = new { descriptor = handleDescriptor, value = handle.Handle.ToInt64(), isStubZero = true },
            expanded = expander.IsExpanded,
            codeGridRawVisible = codeGrid.IsVisible,
            codeGridEffectivelyVisible = codeGrid.IsEffectivelyVisible,
            codeSurfaceRawVisible = codeSurface.IsVisible,
            codeSurfaceEffectivelyVisible = codeSurface.IsEffectivelyVisible,
            textSurfaceRawVisible = textSurface.IsVisible,
            textSurfaceEffectivelyVisible = textSurface.IsEffectivelyVisible,
            codeGridBounds = Format(codeGridBounds),
            codeGridCoordinatesAvailable,
            codeFrameBounds = Format(codeFrameBounds),
            frameCoordinatesAvailable,
            codeSurfaceBounds = Format(codeSurfaceBounds),
            surfaceCoordinatesAvailable,
            codeScrollBounds = Format(codeScrollBounds),
            codeScrollCoordinatesAvailable,
            codeScrollViewport = new { width = codeScroll.Viewport.Width, height = codeScroll.Viewport.Height },
            codeScrollExtent = new { width = codeScroll.Extent.Width, height = codeScroll.Extent.Height },
            codeScrollOffset = new { x = codeScroll.Offset.X, y = codeScroll.Offset.Y },
            textBounds = Format(textBounds),
            textCoordinatesAvailable,
            textDesiredSize = new { width = textSurface.DesiredSize.Width, height = textSurface.DesiredSize.Height },
            diagnosticBounds = Format(diagnosticBounds),
            diagnosticCoordinatesAvailable,
            diagnosticViewportHeight = diagnostics.Viewport.Height,
            diagnosticExtentHeight = diagnostics.Extent.Height,
            editorViewportBounds = Format(editorBounds),
            editorViewportHeight = editorViewport.Viewport.Height,
            toggleBounds = Format(toggleBounds),
            toggleChecked = highlightToggle.IsChecked,
            vmHighlightingEnabled = viewModel.IsLatexHighlightingEnabled,
            copyButtonBounds = Format(copyBounds),
            sourceLength = source.Length,
            surfaceSource = codeSurface.SourceText,
            surfaceSyntaxReferenceMatches = ReferenceEquals(codeSurface.Syntax, viewModel.LatexSyntax),
            hasCompleteTokenCoverage = codeSurface.HasCompleteTokenCoverage,
            tokenCount = viewModel.LatexSyntax.Tokens.Length,
            runTexts,
            runTypography = typography,
            selectionStart = codeSurface.SelectionStart,
            selectionEnd = codeSurface.SelectionEnd,
            selectedText = codeSurface.SelectedText,
            isReadOnly = codeSurface.IsReadOnly,
            sourceVerticalScroll = ScrollViewer.GetVerticalScrollBarVisibility(codeScroll).ToString(),
            inlineCollectionAvailable = inlineCollection is not null,
            gridMaxHeight = codeGrid.MaxHeight,
            png = Path.GetFileName(pngPath),
            pngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
        };
        File.WriteAllText(Path.Combine(directory, "bounds.json"), JsonSerializer.Serialize(evidence, JsonOptions));

        Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "구문 강조 전체 창 PNG가 유효하지 않습니다.");
        Qa.Check(ReferenceEquals(codeSurface.Syntax, viewModel.LatexSyntax) &&
                 codeSurface.SourceText == viewModel.LatexCode && codeSurface.HasCompleteTokenCoverage,
            "코드 표면이 하나의 현재 syntax snapshot에서 원문과 전체 토큰 범위를 읽지 못했습니다.");
        Qa.Equal(viewModel.IsLatexHighlightingEnabled, highlightToggle.IsChecked == true,
            "강조 전환 컨트롤과 ViewModel 상태");
        Qa.Check(codeSurface.IsReadOnly,
            "선택 가능한 실제 코드 표면이 읽기 전용이 아닙니다.");
        Qa.Check(ScrollViewer.GetVerticalScrollBarVisibility(codeScroll) ==
                 Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            "LaTeX 원문이 내부 세로 스크롤을 제공하지 않습니다.");

        if (!expander.IsExpanded)
        {
            var minimumViewport = isMinimum ? 257 : 437;
            Qa.Check(editorViewport.Viewport.Height >= minimumViewport,
                $"접힌 코드 패널에서 중앙 편집 viewport 기준을 지키지 않습니다: expected≥{minimumViewport}, actual={editorViewport.Viewport.Height:R}.");
            Qa.Check(!codeGridCoordinatesAvailable && !frameCoordinatesAvailable && !surfaceCoordinatesAvailable &&
                     !codeScrollCoordinatesAvailable && !textCoordinatesAvailable,
                "접힌 코드 콘텐츠가 좌표 없는 상태로 기록되지 않았습니다.");
        }
        else
        {
            Qa.Check(codeGridCoordinatesAvailable && frameCoordinatesAvailable && surfaceCoordinatesAvailable &&
                     codeScrollCoordinatesAvailable && textCoordinatesAvailable &&
                     codeSurface.IsEffectivelyVisible && textSurface.IsEffectivelyVisible &&
                     inlineCollection is not null,
                "펼친 코드 표면의 실제 창 좌표를 얻지 못했습니다.");
            Qa.Check(codeGrid.Bounds.Height <= 160.5 && codeFrame.Bounds.Height >= 90 &&
                     codeFrame.Bounds.Height <= 110.5 && Contains(new Rect(0, 0, client.Width, client.Height), codeGridBounds) &&
                     Contains(codeGridBounds, codeFrameBounds) && Contains(codeSurfaceBounds, codeScrollBounds) &&
                     Overlaps(codeScrollBounds, textBounds) && !Overlaps(editorBounds, codeGridBounds),
                $"펼친 LaTeX 코드 표면이 높이·창 경계·중앙 편집 영역 조건을 지키지 않습니다: " +
                $"grid={Format(codeGridBounds)}, frame={Format(codeFrameBounds)}, surface={Format(codeSurfaceBounds)}, " +
                $"scroll={Format(codeScrollBounds)}, text={Format(textBounds)}, editor={Format(editorBounds)}.");
            if (diagnostics.IsEffectivelyVisible)
                Qa.Check(diagnosticCoordinatesAvailable && diagnostics.Viewport.Height <= 58.5 &&
                         Contains(codeGridBounds, diagnosticBounds),
                    "펼친 LaTeX 진단 영역이 실제 코드 패널 안에서 58 DIP 이내로 표시되지 않습니다.");

            Qa.Check(string.Concat(runTexts) == source,
                "선택 표면의 Inline Run 연결이 원문 code unit을 정확히 보존하지 않습니다.");
            Qa.Check(runs.All(run => run.Foreground is not null),
                "실제 표시 중인 코드 Run 중 전경색이 빠진 항목이 있습니다.");
            if (typography.Length > 1)
                Qa.Check(typography.Select(item => (item.family, item.size, item.style, item.weight, item.stretch))
                             .Distinct().Count() == 1,
                    "구문 강조가 LaTeX 토큰에 색 이외의 서체 속성을 다르게 적용했습니다.");
            if (viewModel.IsLatexHighlightingEnabled)
            {
                Qa.Equal(viewModel.LatexSyntax.Tokens.Length, runs.Length,
                    "강조 상태에서 snapshot 토큰 하나당 Inline Run 개수");
                for (var index = 0; index < runs.Length; index++)
                {
                    var token = viewModel.LatexSyntax.Tokens[index];
                    Qa.Equal(source.Substring(token.Start, token.Length), runTexts[index],
                        $"강조 Run {index}의 UTF-16 원문 범위");
                }
            }
        }

        Qa.Check(Contains(new Rect(0, 0, client.Width, client.Height), toggleBounds) &&
                 Contains(new Rect(0, 0, client.Width, client.Height), copyBounds) &&
                 !Overlaps(toggleBounds, copyBounds),
            $"강조 전환과 전체 복사 동작의 헤더 bounds가 겹치거나 창 밖입니다: toggle={Format(toggleBounds)}, copy={Format(copyBounds)}.");
        Console.WriteLine($"LATEX-HIGHLIGHT-UI artifact {Path.Combine(directory, "bounds.json")}");
    }

    public static void CaptureGroupRowSpacingLatexAndVerify(MainWindow window, string label)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("행 간격 LaTeX 증거 전에 Headless 창 handle이 없습니다.");
        Qa.Check(handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "행 간격 LaTeX 증거는 STUB/0 Headless 창에서만 생성할 수 있습니다.");
        if (window.DataContext is not MainWindowViewModel viewModel)
            throw new InvalidOperationException("행 간격 LaTeX 증거 창에 실제 MainWindowViewModel이 없습니다.");

        Dispatcher.UIThread.RunJobs();
        var client = window.ClientSize;
        var isMinimum = Near(client.Width, 1060) && Near(client.Height, 700);
        var isDefault = Near(client.Width, 1360) && Near(client.Height, 880);
        Qa.Check(isMinimum || isDefault, $"지원하지 않는 LaTeX 진단 캡처 크기입니다: {client.Width:R}×{client.Height:R}.");
        var repositoryRoot = FindRepositoryRoot();
        var directory = Path.Combine(repositoryRoot, "doc", "verification", "artifacts", "p2-group-row-spacing",
            $"{label}-{(isMinimum ? "1060x700" : "1360x880")}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var codeGrid = Find<Grid>(window, "LatexCodePanelGrid");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var diagnosticViewer = Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer");
        var layoutBanner = Find<Border>(window, "LatexLayoutDiagnosticBanner");
        var layoutList = Find<ItemsControl>(window, "LatexLayoutDiagnosticsList");
        var source = Find<LatexCodeSurface>(window, "LatexCodeView");
        var codeExpander = Find<Expander>(window, "LatexCodeExpander");
        var layoutMessages = viewModel.LatexLayoutDiagnostics.Select(item => item.Message).ToArray();
        var diagnosticViewerAttachedToWindow = diagnosticViewer.GetVisualAncestors()
            .Any(ancestor => ReferenceEquals(ancestor, window));
        var diagnosticViewportOrigin = diagnosticViewer.TranslatePoint(new Point(0, 0), window);
        if (!codeExpander.IsExpanded || !diagnosticViewer.IsEffectivelyVisible ||
            !diagnosticViewerAttachedToWindow || diagnosticViewportOrigin is null)
        {
            SaveLatexDiagnosticsViewportFailureEvidence(window, directory, label,
                codeExpander.IsExpanded, diagnosticViewer.IsEffectivelyVisible,
                diagnosticViewerAttachedToWindow, diagnosticViewportOrigin, diagnosticViewer);
            throw new InvalidOperationException(
                $"전용 LaTeX 진단 캡처의 펼친 viewport가 실제 창에 연결되지 않았습니다: " +
                $"expanded={codeExpander.IsExpanded}, visible={diagnosticViewer.IsEffectivelyVisible}, " +
                $"attached={diagnosticViewerAttachedToWindow}, " +
                $"origin={diagnosticViewportOrigin?.ToString() ?? "없음"}.");
        }
        var diagnosticViewportBounds = new Rect(diagnosticViewportOrigin.GetValueOrDefault(), diagnosticViewer.Viewport);
        var codeGridBounds = GetBounds(codeGrid, window, "LaTeX 코드/진단 통합 영역");
        var editorViewportBounds = GetBounds(editorViewport, window, "LaTeX 확장 상태의 편집 viewport");
        var diagnosticViewerBounds = GetBounds(diagnosticViewer, window, "LaTeX 레이아웃 진단 스크롤");
        var sourceBounds = GetBounds(source, window, "LaTeX 읽기 전용 원문");
        var messageControls = layoutList.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => !string.IsNullOrWhiteSpace(text.Text) &&
                           layoutMessages.Any(message => string.Equals(message, text.Text, StringComparison.Ordinal)))
            .ToArray();
        var suffix = label.EndsWith("-top", StringComparison.Ordinal) ? "top" :
            label.EndsWith("-first", StringComparison.Ordinal) ? "first" :
            label.EndsWith("-bottom", StringComparison.Ordinal) ? "bottom" : "state";
        var maxOffset = Math.Max(0, diagnosticViewer.Extent.Height - diagnosticViewer.Viewport.Height);
        Qa.Check(suffix is "top" or "first" or "bottom",
            "LaTeX 진단 캡처 라벨은 -top, -first 또는 -bottom으로 끝나야 합니다.");
        if (suffix == "top")
        {
            diagnosticViewer.Offset = new Vector(diagnosticViewer.Offset.X, 0);
        }
        else if (suffix == "bottom")
        {
            diagnosticViewer.Offset = new Vector(diagnosticViewer.Offset.X, maxOffset);
        }
        else if (messageControls.Length > 0)
        {
            var firstMessageOrigin = messageControls[0].TranslatePoint(new Point(0, 0), diagnosticViewer)
                ?? throw new InvalidOperationException("첫 LaTeX layout 진단의 스크롤 상대 좌표를 얻지 못했습니다.");
            var messageHeight = messageControls[0].Bounds.Height;
            const double messageMargin = 8;
            var desiredTop = messageHeight + messageMargin * 2 <= diagnosticViewer.Viewport.Height
                ? Math.Max(messageMargin, (diagnosticViewer.Viewport.Height - messageHeight) / 2)
                : 0;
            var targetOffset = Math.Clamp(diagnosticViewer.Offset.Y + firstMessageOrigin.Y - desiredTop, 0, maxOffset);
            diagnosticViewer.Offset = new Vector(diagnosticViewer.Offset.X, targetOffset);
        }
        Dispatcher.UIThread.RunJobs();
        var refreshedMessageBounds = messageControls.Select(text =>
        {
            var origin = text.TranslatePoint(new Point(0, 0), diagnosticViewer);
            return origin is Point point ? new Rect(point, text.Bounds.Size) : new Rect();
        }).ToArray();
        var layoutBannerBounds = layoutBanner.IsEffectivelyVisible
            ? GetBounds(layoutBanner, window, "행 간격 LaTeX 진단 배너")
            : new Rect();
        var layoutTitle = layoutBanner.GetVisualDescendants().OfType<TextBlock>()
            .SingleOrDefault(text => text.Text == "행 간격 미보존 안내")
            ?? throw new InvalidOperationException("LaTeX 행 간격 안내 제목을 찾지 못했습니다.");
        var layoutIntroduction = layoutBanner.GetVisualDescendants().OfType<TextBlock>()
            .SingleOrDefault(text => text.Text?.StartsWith("그룹 내부 행 간격은 LaTeX 코드에 보존되지 않습니다.", StringComparison.Ordinal) == true)
            ?? throw new InvalidOperationException("LaTeX 행 간격 안내 설명을 찾지 못했습니다.");
        var layoutTitleBounds = GetBounds(layoutTitle, window, "행 간격 LaTeX 안내 제목");
        var layoutIntroductionBounds = GetBounds(layoutIntroduction, window, "행 간격 LaTeX 안내 설명");

        var pngPath = Path.Combine(directory, "main-window.png");
        using (var bitmap = new RenderTargetBitmap(
                   new PixelSize((int)Math.Ceiling(client.Width), (int)Math.Ceiling(client.Height)), new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }
        var pngBytes = File.ReadAllBytes(pngPath);
        var evidence = new
        {
            caseId = "GROUP-ROW-SPACING-UI",
            label,
            evidencePurpose = "확장된 LaTeX 패널에서 행 간격 미보존 안내를 읽을 수 있는지 확인하는 Headless UI 캡처이며 제품 출력물이 아닙니다.",
            window = new { width = client.Width, height = client.Height },
            handle = new { descriptor = handle.HandleDescriptor, value = "0x0", isStubZero = true },
            codeExpanderExpanded = codeExpander.IsExpanded,
            codeGridBounds = Format(codeGridBounds),
            codeGridHeight = codeGrid.Bounds.Height,
            editorViewportBounds = Format(editorViewportBounds),
            editorViewportHeight = editorViewport.Viewport.Height,
            diagnosticViewerBounds = Format(diagnosticViewerBounds),
            diagnosticViewportBounds = Format(diagnosticViewportBounds),
            diagnosticViewport = new { width = diagnosticViewer.Viewport.Width, height = diagnosticViewer.Viewport.Height },
            diagnosticExtent = new { width = diagnosticViewer.Extent.Width, height = diagnosticViewer.Extent.Height },
            diagnosticOffset = new { x = diagnosticViewer.Offset.X, y = diagnosticViewer.Offset.Y },
            diagnosticVerticalScroll = ScrollViewer.GetVerticalScrollBarVisibility(diagnosticViewer).ToString(),
            layoutBannerVisible = layoutBanner.IsEffectivelyVisible,
            layoutBannerBounds = Format(layoutBannerBounds),
            layoutTitleBounds = Format(layoutTitleBounds),
            layoutIntroductionBounds = Format(layoutIntroductionBounds),
            layoutDiagnosticMessages = layoutMessages,
            renderedDiagnosticMessageBounds = refreshedMessageBounds.Select(Format).ToArray(),
            sourceBounds = Format(sourceBounds),
            sourceHeight = source.Bounds.Height,
            sourceVerticalScroll = ScrollViewer.GetVerticalScrollBarVisibility(source.CodeScrollViewer).ToString(),
            totalGridHeight = diagnosticViewer.Bounds.Height + source.Bounds.Height,
            canExportLatex = viewModel.CanExportLatex,
            blockingCodeDiagnosticVisible = Find<Border>(window, "LatexCodeDiagnosticBanner").IsEffectivelyVisible,
            png = Path.GetFileName(pngPath),
            pngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
        };
        File.WriteAllText(Path.Combine(directory, "bounds.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));

        Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "LaTeX 진단 전체창 PNG가 유효하지 않습니다.");
        Qa.Check(codeExpander.IsExpanded && codeGrid.IsEffectivelyVisible,
            "전용 LaTeX 진단 캡처에서 코드 패널이 펼쳐져 있지 않습니다.");
        Qa.Check(codeGrid.Bounds.Height <= 160.5 && source.Bounds.Height >= 90 &&
                 diagnosticViewer.Bounds.Height + source.Bounds.Height <= 160.5,
            $"LaTeX 진단/원문 높이 계약을 지키지 않습니다: grid={codeGrid.Bounds.Height:R}, " +
            $"diagnostic={diagnosticViewer.Bounds.Height:R}, source={source.Bounds.Height:R}.");
        Qa.Check(ScrollViewer.GetVerticalScrollBarVisibility(diagnosticViewer) ==
                 Avalonia.Controls.Primitives.ScrollBarVisibility.Auto &&
                 ScrollViewer.GetVerticalScrollBarVisibility(source.CodeScrollViewer) == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            "LaTeX 진단과 원문 각각의 세로 스크롤이 활성화되지 않았습니다.");
        Qa.Check(diagnosticViewer.IsEffectivelyVisible && layoutBanner.IsEffectivelyVisible &&
                 layoutMessages.Length == viewModel.LatexLayoutDiagnostics.Length &&
                 layoutList.Items.Count == layoutMessages.Length && messageControls.Length == layoutMessages.Length,
            "비차단 행 간격 진단이 모두 실제 목록 컨트롤에 표시되지 않습니다.");
        Qa.Check(viewModel.CanExportLatex && !Find<Border>(window, "LatexCodeDiagnosticBanner").IsEffectivelyVisible,
            "행 간격 미보존 안내가 차단형 LaTeX 제한으로 잘못 표시되었습니다.");
        Qa.Check(!Overlaps(editorViewportBounds, codeGridBounds) && Contains(new Rect(0, 0, client.Width, client.Height), codeGridBounds) &&
                 Contains(new Rect(0, 0, client.Width, client.Height), editorViewportBounds),
            "LaTeX 코드 패널과 수식 편집 viewport가 겹치거나 창 경계를 벗어났습니다.");
        Qa.Check(diagnosticViewer.Extent.Height > diagnosticViewer.Viewport.Height + 1,
            "진단 표본이 ScrollViewer의 실제 넘침/스크롤을 만들지 않습니다.");
        var diagnosticViewport = new Rect(0, 0, diagnosticViewer.Viewport.Width, diagnosticViewer.Viewport.Height);
        if (suffix == "top")
        {
            Qa.Check(Contains(diagnosticViewportBounds, layoutTitleBounds) &&
                     Contains(diagnosticViewportBounds, layoutIntroductionBounds),
                $"LaTeX 진단 상단에서 제목과 안내 설명 전체가 보이지 않습니다: " +
                $"title={Format(layoutTitleBounds)}, intro={Format(layoutIntroductionBounds)}, " +
                $"offset={diagnosticViewer.Offset.Y:R}/{maxOffset:R}.");
        }
        else
        {
            var edgeMessageIndex = suffix == "first" ? 0 : layoutMessages.Length - 1;
            Qa.Check(edgeMessageIndex >= 0 && refreshedMessageBounds.Length == layoutMessages.Length &&
                     Contains(diagnosticViewport, refreshedMessageBounds[edgeMessageIndex]),
                $"LaTeX 진단 {suffix}에서 대응 문구 전체가 진단 viewport 안에 있지 않습니다: " +
                $"offset={diagnosticViewer.Offset.Y:R}/{maxOffset:R}; textBounds=" +
                (edgeMessageIndex >= 0 && edgeMessageIndex < refreshedMessageBounds.Length
                    ? Format(refreshedMessageBounds[edgeMessageIndex]).ToString()
                    : "없음"));
        }
        Console.WriteLine($"GROUP-ROW-SPACING-UI expanded artifact {Path.Combine(directory, "bounds.json")}");
    }

    public static void CaptureOutputPreviewAndVerify(
        MainWindow window, string label, string artifactScope = "p2-output-preview")
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (artifactScope is not ("p2-output-preview" or "p2-thin-math-space"))
            throw new ArgumentException("출력 미리보기 증거는 승인된 출력 또는 얇은 수학 공백 범위여야 합니다.", nameof(artifactScope));
        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            throw new ArgumentException("출력 미리보기 증거 라벨은 단일 파일 이름이어야 합니다.", nameof(label));

        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle != IntPtr.Zero)
            throw new InvalidOperationException("출력 미리보기 캡처에서 STUB/0 headless 창 handle을 확인하지 못했습니다.");
        var handleDescriptor = handle.HandleDescriptor
            ?? throw new InvalidOperationException("출력 미리보기 캡처의 headless handle 설명자가 없습니다.");
        if (handleDescriptor != "STUB")
            throw new InvalidOperationException("출력 미리보기 캡처에서 STUB/0 headless 창 handle을 확인하지 못했습니다.");
        Dispatcher.UIThread.RunJobs();

        var clientSize = window.ClientSize;
        var isMinimum = Near(clientSize.Width, 1060) && Near(clientSize.Height, 700);
        var isDefault = Near(clientSize.Width, 1360) && Near(clientSize.Height, 880);
        Qa.Check(isMinimum || isDefault,
            $"지원하지 않는 출력 미리보기 셸 크기입니다: {clientSize.Width:R}×{clientSize.Height:R}.");
        Qa.Check(window.DataContext is MainWindowViewModel,
            "출력 미리보기 셸이 실제 MainWindowViewModel을 사용하지 않습니다.");
        var viewModel = (MainWindowViewModel)window.DataContext!;
        Qa.Check(viewModel.IsOutputPreviewVisible && ReferenceEquals(window.FindControl<TabItem>("OutputPreviewTab"),
                window.FindControl<TabControl>("EquationInspector")?.SelectedItem),
            "출력 미리보기 mode와 inspector tab 선택이 일치하지 않습니다.");

        var editorSurface = Find<Border>(window, "EditorWorkspaceSurface");
        var previewSurface = Find<Border>(window, "OutputPreviewSurface");
        var editorTools = Find<Border>(window, "TemplateToolsPanel");
        var previewTools = Find<Border>(window, "OutputPreviewToolsPanel");
        var inspector = Find<TabControl>(window, "EquationInspector");
        var previewCanvasHeader = Find<Border>(window, "OutputPreviewCanvasHeader");
        var previewScroll = Find<ScrollViewer>(window, "OutputPreviewScrollViewer");
        var settingsScroll = Find<ScrollViewer>(window, "OutputPreviewSettingsScrollViewer");
        var actionsPanel = Find<StackPanel>(window, "OutputPreviewActionsPanel");
        var canvas = Find<MathOutputPreviewControl>(window, "OutputPreviewCanvas");
        var previewTitle = Find<TextBlock>(window, "OutputPreviewSurfaceTitle");
        var previewScaleNotice = Find<TextBlock>(window, "OutputPreviewSurfaceScaleNotice");
        var mode = Find<ComboBox>(window, "OutputPreviewModeComboBox");
        var refresh = Find<Button>(window, "OutputPreviewRefreshButton");
        var save = Find<Button>(window, "OutputPreviewSaveSvgButton");
        var returnButton = Find<Button>(window, "OutputPreviewReturnToEditorButton");
        var snapshot = viewModel.OutputPreview.PreviewSnapshot
            ?? throw new InvalidOperationException("미리보기 snapshot이 없어 전체 셸을 캡처할 수 없습니다.");
        var canvasUsesSnapshotLayout = ReferenceEquals(canvas.Layout, snapshot.Layout);
        var canvasAllocationMatchesLayout = canvas.Bounds.Width >= snapshot.Layout.Width &&
            canvas.Bounds.Width - snapshot.Layout.Width < 1 &&
            canvas.Bounds.Height >= snapshot.Layout.Height &&
            canvas.Bounds.Height - snapshot.Layout.Height < 1;

        var editorBounds = GetOptionalBounds(editorSurface, window, "숨겨진 편집 표면");
        var previewBounds = GetBounds(previewSurface, window, "중앙 출력 미리보기 표면");
        var editorToolsBounds = GetOptionalBounds(editorTools, window, "숨겨진 수식 도구 패널");
        var previewToolsBounds = GetBounds(previewTools, window, "출력 상태 도구 패널");
        var inspectorBounds = GetBounds(inspector, window, "오른쪽 출력 설정 inspector");
        var canvasHeaderBounds = GetBounds(previewCanvasHeader, window, "출력 미리보기 표면 헤더");
        var scrollBounds = GetBounds(previewScroll, window, "출력 미리보기 스크롤 viewport");
        var settingsScrollBounds = GetBounds(settingsScroll, window, "출력 설정 스크롤 영역");
        var settingsViewportBounds = new Rect(settingsScrollBounds.X, settingsScrollBounds.Y,
            settingsScroll.Viewport.Width, settingsScroll.Viewport.Height);
        var actionsPanelBounds = GetBounds(actionsPanel, window, "고정 출력 작업 영역");
        var canvasBounds = GetBounds(canvas, window, "읽기 전용 layout control");
        var previewTitleBounds = GetBounds(previewTitle, window, "출력 surface 제목");
        var previewScaleBounds = GetBounds(previewScaleNotice, window, "출력 surface 크기 안내");
        var modeBounds = GetBounds(mode, window, "출력 조판 모드 선택");
        var refreshBounds = GetBounds(refresh, window, "미리보기 새로 고침 버튼");
        var saveBounds = GetBounds(save, window, "SVG 저장 버튼");
        var returnBounds = GetBounds(returnButton, window, "편집 복귀 버튼");
        var inspectorTabs = new[]
        {
            Find<TabItem>(window, "PropertiesTab"),
            Find<TabItem>(window, "StructureInspectorTab"),
            Find<TabItem>(window, "WorkspaceLibraryTab"),
            Find<TabItem>(window, "OutputPreviewTab")
        };
        var inspectorTabHeaderBounds = inspectorTabs.Select(tab =>
        {
            var headerText = tab.Header?.ToString()
                ?? throw new InvalidOperationException("Inspector 탭 header가 문자열이 아닙니다.");
            var header = FindInspectorTabHeader(tab, headerText);
            var tabBounds = GetBounds(tab, window, $"{headerText} inspector 탭");
            var headerBounds = GetBounds(header, window, $"{headerText} inspector 탭 제목");
            Qa.Check(tab.IsEffectivelyEnabled && Contains(tabBounds, headerBounds) &&
                     Contains(inspectorBounds, headerBounds),
                $"'{headerText}' inspector 탭 제목이 잘렸거나 클릭 영역 밖에 있습니다: tab={Format(tabBounds)} header={Format(headerBounds)}.");
            return new NamedBounds(headerText, Format(headerBounds), true);
        }).ToArray();
        for (var index = 0; index < inspectorTabHeaderBounds.Length; index++)
        for (var otherIndex = index + 1; otherIndex < inspectorTabHeaderBounds.Length; otherIndex++)
            Qa.Check(!Overlaps(ToRect(inspectorTabHeaderBounds[index].Bounds),
                    ToRect(inspectorTabHeaderBounds[otherIndex].Bounds)),
                $"Inspector 탭 제목이 겹칩니다: {inspectorTabHeaderBounds[index].Name}, {inspectorTabHeaderBounds[otherIndex].Name}.");
        var clientBounds = new Rect(0, 0, clientSize.Width, clientSize.Height);
        Qa.Check(!editorSurface.IsEffectivelyVisible && previewSurface.IsVisible && !editorTools.IsVisible && previewTools.IsVisible,
            "출력 mode에서 편집/출력 surface 또는 도구 패널의 전환이 잘못되었습니다.");
        Qa.Check(editorBounds == default && editorToolsBounds == default,
            "숨겨야 할 편집 UI가 여전히 화면 bounds를 차지합니다.");
        Qa.Check(Contains(clientBounds, previewBounds) && Contains(clientBounds, previewToolsBounds) &&
                 Contains(clientBounds, inspectorBounds) && Contains(previewBounds, canvasHeaderBounds) &&
                 Contains(previewBounds, scrollBounds) && Intersect(canvasBounds, scrollBounds).Width > 0 &&
                 Intersect(canvasBounds, scrollBounds).Height > 0,
            "출력 surface나 내부 control bounds가 부모 영역 밖으로 나갔습니다.");
        Qa.Check(Contains(canvasHeaderBounds, previewTitleBounds) && Contains(canvasHeaderBounds, previewScaleBounds) &&
                 !Overlaps(previewTitleBounds, previewScaleBounds),
            "중앙 출력 헤더가 겹치거나 출력 surface 경계를 벗어났습니다.");
        Qa.Check(previewBounds.Height >= 257,
            $"출력 미리보기 중앙 viewport가 최소 높이 257 DIP보다 작습니다: {previewBounds.Height:R}.");
        Qa.Check(!Overlaps(previewToolsBounds, previewBounds) && !Overlaps(previewBounds, inspectorBounds),
            "좌측 도구·중앙 출력 viewport·오른쪽 inspector가 겹칩니다.");
        Qa.Check(!canvas.Focusable && !canvas.IsHitTestVisible,
            "출력 preview surface가 편집 입력 또는 키보드 focus 대상입니다.");

        var artifactDirectory = Path.Combine(FindRepositoryRoot(), "doc", "verification", "artifacts",
            artifactScope, $"{label}-{(isMinimum ? "1060x700" : "1360x880")}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactDirectory);
        var pngPath = Path.Combine(artifactDirectory, "main-window.png");
        var bitmapSize = new PixelSize((int)Math.Ceiling(clientSize.Width), (int)Math.Ceiling(clientSize.Height));
        using (var bitmap = new RenderTargetBitmap(bitmapSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "출력 미리보기 전체 셸 오프스크린 PNG가 유효하지 않습니다.");
        var evidence = new OutputPreviewShellEvidence(
            label,
            clientSize.Width,
            clientSize.Height,
            handleDescriptor,
            handle.Handle.ToInt64(),
            Format(previewToolsBounds),
            Format(previewBounds),
            Format(canvasHeaderBounds),
            Format(scrollBounds),
            Format(canvasBounds),
            Format(previewTitleBounds),
            Format(previewScaleBounds),
            Format(inspectorBounds),
            Format(settingsScrollBounds),
            Format(settingsViewportBounds),
            settingsScroll.Extent.Width,
            settingsScroll.Extent.Height,
            settingsScroll.Offset.Y,
            Format(actionsPanelBounds),
            Format(modeBounds),
            Format(refreshBounds),
            Format(saveBounds),
            Format(returnBounds),
            Contains(previewToolsBounds, returnBounds) && Contains(inspectorBounds, modeBounds) &&
            Contains(inspectorBounds, actionsPanelBounds) && Contains(actionsPanelBounds, refreshBounds) &&
            Contains(actionsPanelBounds, saveBounds),
            canvasUsesSnapshotLayout,
            canvasAllocationMatchesLayout,
            inspectorTabHeaderBounds,
            snapshot.Mode.ToString(),
            snapshot.Revision,
            viewModel.OutputPreview.State.ToString(),
            viewModel.OutputPreview.StateSummary,
            viewModel.OutputPreview.CurrentRevisionSummary,
            viewModel.OutputPreview.ScopeSummary,
            viewModel.OutputPreview.CanExportSvg,
            viewModel.OutputPreview.ExportDisabledReason,
            snapshot.Layout.Width,
            snapshot.Layout.Height,
            Path.GetFileName(pngPath),
            Convert.ToHexString(SHA256.HashData(pngBytes)));
        File.WriteAllText(Path.Combine(artifactDirectory, "bounds.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));

        Qa.Check(canvasUsesSnapshotLayout,
            "화면의 readonly preview control이 snapshot의 동일 Layout 인스턴스를 사용하지 않습니다.");
        Qa.Check(canvasAllocationMatchesLayout,
            $"preview control 할당이 frozen Layout보다 작거나 반올림 범위를 벗어납니다: " +
            $"control={canvas.Bounds.Size}; layout={snapshot.Layout.Width:R}×{snapshot.Layout.Height:R}; " +
            $"delta={(canvas.Bounds.Width - snapshot.Layout.Width):R}×{(canvas.Bounds.Height - snapshot.Layout.Height):R} DIP.");
        Qa.Check(Contains(previewToolsBounds, returnBounds) && Contains(inspectorBounds, modeBounds) &&
                 Contains(inspectorBounds, settingsScrollBounds) && Contains(settingsScrollBounds, settingsViewportBounds) &&
                 Contains(settingsViewportBounds, modeBounds) &&
                 Contains(inspectorBounds, actionsPanelBounds) && Contains(actionsPanelBounds, refreshBounds) &&
                 Contains(actionsPanelBounds, saveBounds),
            $"출력 mode 선택, 설정 viewport, 갱신/저장 또는 편집 복귀 동작이 해당 패널 밖에 배치되었습니다. " +
            $"inspector={Format(inspectorBounds)} settings={Format(settingsScrollBounds)} " +
            $"viewport={Format(settingsViewportBounds)} actions={Format(actionsPanelBounds)} " +
            $"mode={Format(modeBounds)} refresh={Format(refreshBounds)} save={Format(saveBounds)} return={Format(returnBounds)}; " +
            $"settingsExtent={settingsScroll.Extent.Width:R}×{settingsScroll.Extent.Height:R}, " +
            $"settingsOffset={settingsScroll.Offset.Y:R}.");
    }

    private static void SavePropertiesContainmentFailureEvidence(
        Window window,
        string artifactDirectory,
        string label,
        Rect panelBounds,
        Rect scrollBounds,
        Rect viewportBounds,
        string controlName,
        Rect controlBounds,
        ScrollViewer scrollViewer)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "STUB" || handle.Handle != IntPtr.Zero)
            throw new InvalidOperationException("속성 containment 실패 증거를 저장하기 전에 STUB/0 handle을 확인하지 못했습니다.");

        var pngPath = Path.Combine(artifactDirectory, "properties-containment-failure.png");
        var pixelSize = new PixelSize((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        var evidence = new
        {
            Label = label,
            HandleDescriptor = handle!.HandleDescriptor,
            Handle = handle.Handle.ToInt64(),
            PanelBounds = Format(panelBounds),
            ScrollViewerBounds = Format(scrollBounds),
            ViewportBounds = Format(viewportBounds),
            ControlName = controlName,
            ControlBounds = Format(controlBounds),
            ViewportSize = new { Width = scrollViewer.Viewport.Width, Height = scrollViewer.Viewport.Height },
            ExtentSize = new { Width = scrollViewer.Extent.Width, Height = scrollViewer.Extent.Height },
            Offset = new { X = scrollViewer.Offset.X, Y = scrollViewer.Offset.Y },
            PngFile = Path.GetFileName(pngPath),
            PngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
        };
        File.WriteAllText(Path.Combine(artifactDirectory, "properties-containment-failure.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static void SavePropertiesControlFailureEvidence(
        Window window,
        string artifactDirectory,
        string label,
        Rect panelBounds,
        Rect scrollBounds,
        Rect viewportBounds,
        string controlName,
        Rect controlBounds,
        ScrollViewer scrollViewer,
        string expectedText,
        string? actualText,
        double currentGroupRowSpacing)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "STUB" || handle.Handle != IntPtr.Zero)
            throw new InvalidOperationException("속성 표시 실패 증거를 저장하기 전에 STUB/0 handle을 확인하지 못했습니다.");

        var pngPath = Path.Combine(artifactDirectory, "properties-display-failure.png");
        var pixelSize = new PixelSize((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        var evidence = new
        {
            Label = label,
            Phase = "현재 그룹 저장값 TextBlock과 VM 값 대조",
            HandleDescriptor = handle.HandleDescriptor,
            Handle = handle.Handle.ToInt64(),
            IsStubZero = handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            CurrentGroupRowSpacing = currentGroupRowSpacing,
            ExpectedText = expectedText,
            ActualText = actualText,
            PanelBounds = Format(panelBounds),
            ScrollViewerBounds = Format(scrollBounds),
            ViewportBounds = Format(viewportBounds),
            ControlName = controlName,
            ControlBounds = Format(controlBounds),
            ViewportSize = new { Width = scrollViewer.Viewport.Width, Height = scrollViewer.Viewport.Height },
            ExtentSize = new { Width = scrollViewer.Extent.Width, Height = scrollViewer.Extent.Height },
            Offset = new { X = scrollViewer.Offset.X, Y = scrollViewer.Offset.Y },
            PngFile = Path.GetFileName(pngPath),
            PngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
        };
        File.WriteAllText(Path.Combine(artifactDirectory, "properties-display-failure.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static void SaveStructureRevealFailureEvidence(
        Window window,
        string artifactDirectory,
        string label,
        Rect treeBounds,
        Rect scrollViewerBounds,
        Rect effectiveViewportBounds,
        Rect headerWindowBounds,
        Rect headerScrollViewerBounds,
        Guid selectedNodeId,
        StructureScrollMetrics scrollMetrics)
    {
        var pngPath = Path.Combine(artifactDirectory, "structure-reveal-failure.png");
        var pixelSize = new PixelSize((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        var evidence = new StructureRevealFailureEvidence(
            label,
            selectedNodeId.ToString("D"),
            Format(treeBounds),
            Format(scrollViewerBounds),
            Format(effectiveViewportBounds),
            Format(headerWindowBounds),
            Format(headerScrollViewerBounds),
            scrollMetrics,
            Path.GetFileName(pngPath),
            Convert.ToHexString(SHA256.HashData(pngBytes)));
        File.WriteAllText(Path.Combine(artifactDirectory, "structure-reveal-failure.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static void SaveLatexDiagnosticsViewportFailureEvidence(
        Window window,
        string artifactDirectory,
        string label,
        bool expanderExpanded,
        bool viewerEffectivelyVisible,
        bool viewerAttachedToWindow,
        Point? viewportOrigin,
        ScrollViewer viewer)
    {
        var platformHandle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("LaTeX 진단 viewport 실패 증거 전에 Headless handle이 없습니다.");
        var handleDescriptor = platformHandle.HandleDescriptor
            ?? throw new InvalidOperationException("LaTeX 진단 viewport 실패 증거의 handle 설명자가 없습니다.");
        if (handleDescriptor != "STUB" || platformHandle.Handle != IntPtr.Zero)
            throw new InvalidOperationException("LaTeX 진단 viewport 실패 증거는 STUB/0 창에서만 저장할 수 있습니다.");

        var pngPath = Path.Combine(artifactDirectory, "latex-diagnostics-viewport-failure.png");
        var pixelSize = new PixelSize((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var pngBytes = File.ReadAllBytes(pngPath);
        var evidence = new
        {
            Label = label,
            Phase = "펼친 LaTeX 진단 viewport 좌표 확인",
            ExpanderExpanded = expanderExpanded,
            ViewerEffectivelyVisible = viewerEffectivelyVisible,
            ViewerAttachedToWindow = viewerAttachedToWindow,
            ViewportOriginX = viewportOrigin?.X,
            ViewportOriginY = viewportOrigin?.Y,
            HandleDescriptor = handleDescriptor,
            Handle = platformHandle.Handle.ToInt64(),
            IsStubZero = handleDescriptor == "STUB" && platformHandle.Handle == IntPtr.Zero,
            ViewerBounds = new { viewer.Bounds.Width, viewer.Bounds.Height },
            Viewport = new { viewer.Viewport.Width, viewer.Viewport.Height },
            Extent = new { viewer.Extent.Width, viewer.Extent.Height },
            Offset = new { viewer.Offset.X, viewer.Offset.Y },
            VerticalScrollBarVisibility = ScrollViewer.GetVerticalScrollBarVisibility(viewer).ToString(),
            PngFile = Path.GetFileName(pngPath),
            PngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
        };
        File.WriteAllText(Path.Combine(artifactDirectory, "latex-diagnostics-viewport-failure.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"MainWindow visual tree에서 {name} 컨트롤을 찾지 못했습니다.");

    private static TextBlock FindInspectorTabHeader(TabItem tab, string headerText) =>
        tab.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == headerText && text.IsEffectivelyVisible)
        ?? throw new InvalidOperationException($"Inspector 탭 '{headerText}'의 실제 header TextBlock을 찾지 못했습니다.");

    private static Rect GetBounds(Control control, Control target, string label)
    {
        Qa.Check(control.IsVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"{label}이 visual tree에서 보이지 않거나 크기가 없습니다.");
        var origin = control.TranslatePoint(new Point(0, 0), target);
        Qa.Check(origin is Point, $"{label}의 창 기준 좌표를 얻지 못했습니다.");
        return new Rect(origin!.Value, control.Bounds.Size);
    }

    private static Rect GetOptionalBounds(Control control, Control target, string label)
    {
        if (!control.IsEffectivelyVisible)
            return new Rect();

        var origin = control.TranslatePoint(new Point(0, 0), target);
        Qa.Check(origin is Point, $"선택 사항인 {label}의 창 기준 좌표를 얻지 못했습니다.");
        return new Rect(origin!.Value, control.Bounds.Size);
    }

    private static bool HasVisualAncestor(Control control, Control target) =>
        control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target));

    private static bool TryGetLatexCopyContentBounds(
        Control control,
        Window window,
        bool expanderExpanded,
        out Rect bounds)
    {
        bounds = new Rect();
        if (!expanderExpanded || !control.IsEffectivelyVisible || !HasVisualAncestor(control, window))
            return false;

        var origin = control.TranslatePoint(new Point(0, 0), window);
        if (origin is not Point point || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return false;

        bounds = new Rect(point, control.Bounds.Size);
        return true;
    }

    private static void AssertNodeProjection(StructurePanelItem item)
    {
        Qa.Equal(item.Id, item.MathNodeId, "구조 projection item ID/실제 AST NodeId 일치");
        Qa.Check(!string.IsNullOrWhiteSpace(item.Label), "구조 projection label이 비어 있습니다.");
        foreach (var child in item.Children)
            AssertNodeProjection(child);
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : new Rect();
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static bool Overlaps(Rect first, Rect second)
    {
        var overlap = Intersect(first, second);
        return overlap.Width > 0.5 && overlap.Height > 0.5;
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= 0.5;

    private static BoundsRecord Format(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToRect(BoundsRecord bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln을 기준으로 저장소 루트를 찾지 못했습니다.");
    }

    private sealed record RowShellVisualEvidence(
        string Label,
        string ArtifactScope,
        double ClientWidth,
        double ClientHeight,
        string HandleDescriptor,
        long Handle,
        BoundsRecord ScrollViewerBounds,
        BoundsRecord EditorBounds,
        BoundsRecord VisibleEditorBounds,
        double MinimumVisibleEditorWidth,
        NamedBounds[] HeaderActionBounds,
        NamedBounds[] GroupToolbarBounds,
        NamedBounds[] RowToolbarBounds,
        BoundsRecord? LifecycleDialogBounds,
        BoundsRecord? LifecycleTargetCardBounds,
        BoundsRecord? LifecycleTargetBounds,
        NamedBounds[]? LifecycleChoiceBounds,
        string? LifecycleErrorText,
        BoundsRecord? LifecycleErrorBounds,
        BoundsRecord? LibraryViewportBounds,
        BoundsRecord? LibraryHeaderBounds,
        BoundsRecord? LibraryRecentActionsVisibleBounds,
        BoundsRecord? LibraryRecoveryActionsVisibleBounds,
        BoundsRecord? LibraryPreviewVisibleBounds,
        BoundsRecord? LibraryPreviewNameBounds,
        BoundsRecord? LibraryPreviewPathBounds,
        BoundsRecord? LibraryPreviewStatusBounds,
        BoundsRecord? LibraryPreviewModifiedBounds,
        double? LibraryVerticalOffset,
        BoundsRecord? PalettePanelBounds,
        BoundsRecord? PaletteModeTabsBounds,
        BoundsRecord? PaletteActiveScrollBounds,
        NamedBounds[]? PaletteControlBounds,
        double? PaletteVerticalOffset,
        string? PaletteCaptureMode,
        BoundsRecord? TemplatePanelBounds,
        BoundsRecord? TemplateScrollViewerBounds,
        NamedBounds[]? TemplateButtonBounds,
        double? TemplateVerticalOffset,
        string? TemplateCaptureEdge,
        BoundsRecord? StructureInspectorBounds,
        BoundsRecord? StructureClearPanelBounds,
        BoundsRecord? StructureTreeBounds,
        BoundsRecord? StructureScrollViewerBounds,
        BoundsRecord? StructureEffectiveViewportBounds,
        BoundsRecord? StructureHeaderScrollViewerBounds,
        StructureScrollMetrics? StructureScrollMetrics,
        BoundsRecord? StructureSelectedItemBounds,
        bool? StructureSelectedItemIsSelected,
        string? StructureSelectionMode,
        string? StructureHorizontalScrollVisibility,
        NamedBounds[]? StructureControlBounds,
        string? StructureSelectedNodeId,
        string? StructureActivePath,
        string? StructureClearDescription,
        string? StructureClearDisabledReason,
        int? StructureClearAffectedRows,
        bool? StructureCanClear,
        PropertiesCaptureEvidence? Properties,
        string PngFile,
        string PngSha256);

    private sealed record OutputPreviewShellEvidence(
        string Label,
        double ClientWidth,
        double ClientHeight,
        string HandleDescriptor,
        long Handle,
        BoundsRecord ToolsPanelBounds,
        BoundsRecord PreviewSurfaceBounds,
        BoundsRecord PreviewHeaderBounds,
        BoundsRecord PreviewScrollViewerBounds,
        BoundsRecord PreviewCanvasBounds,
        BoundsRecord PreviewTitleBounds,
        BoundsRecord PreviewScaleNoticeBounds,
        BoundsRecord InspectorBounds,
        BoundsRecord SettingsScrollViewerBounds,
        BoundsRecord SettingsViewportBounds,
        double SettingsExtentWidth,
        double SettingsExtentHeight,
        double SettingsVerticalOffset,
        BoundsRecord ActionsPanelBounds,
        BoundsRecord ModeComboBoxBounds,
        BoundsRecord RefreshButtonBounds,
        BoundsRecord SaveButtonBounds,
        BoundsRecord ReturnButtonBounds,
        bool ActionsContained,
        bool CanvasUsesSnapshotLayout,
        bool CanvasAllocationMatchesLayout,
        NamedBounds[] InspectorTabHeaderBounds,
        string Mode,
        long SnapshotRevision,
        string State,
        string StateSummary,
        string CurrentRevisionSummary,
        string ScopeSummary,
        bool CanExportSvg,
        string ExportDisabledReason,
        double LayoutWidth,
        double LayoutHeight,
        string PngFile,
        string PngSha256);

    private sealed record LatexCopyShellEvidence(
        string CaseId,
        string Label,
        string EvidencePurpose,
        double ClientWidth,
        double ClientHeight,
        string HandleDescriptor,
        long Handle,
        BoundsRecord CopyButtonBounds,
        BoundsRecord CopyButtonLabelBounds,
        BoundsRecord ExpanderBounds,
        BoundsRecord CodeGridBounds,
        BoundsRecord CodeViewBounds,
        BoundsRecord DiagnosticsBounds,
        BoundsRecord DiagnosticsViewportBounds,
        BoundsRecord CopyStatusBounds,
        BoundsRecord CopyStatusTextBounds,
        bool CopyButtonEnabled,
        bool CanCopyLatex,
        bool IsCopyingLatex,
        string CopyButtonLabel,
        bool ToolTipShowOnDisabled,
        string CopyButtonToolTip,
        string CopyDisabledReason,
        string CopyMessage,
        bool ExpanderExpanded,
        bool ExpanderContentAttached,
        bool CodeGridCoordinatesAvailable,
        bool CodeViewCoordinatesAvailable,
        bool DiagnosticsCoordinatesAvailable,
        bool CopyStatusRawVisible,
        bool CopyStatusVisibilityRequested,
        bool CopyStatusCoordinatesAvailable,
        bool CopyStatusTextCoordinatesAvailable,
        bool CodeViewReadOnly,
        int CodeLength,
        int CodeSelectionStart,
        int CodeSelectionEnd,
        double EditorViewportHeight,
        double EditorViewportWidth,
        bool CopyStatusVisible,
        bool HasCopyMessage,
        bool IsCollapsedCapture,
        bool IsExpandedCapture,
        long SessionRevision,
        bool SessionDirty,
        bool SessionCanUndo,
        string OutputPreviewState,
        bool OutputPreviewVisible,
        bool CopyButtonBelongsToExpander,
        string PngFile,
        string PngSha256);

    private sealed record PropertiesCaptureEvidence(
        BoundsRecord PanelBounds,
        BoundsRecord ScrollViewerBounds,
        BoundsRecord ViewportBounds,
        double ViewportWidth,
        double ViewportHeight,
        double ExtentWidth,
        double ExtentHeight,
        double OffsetX,
        double OffsetY,
        string Scope,
        string TargetSummary,
        string SelectionNotice,
        bool IsDraftStale,
        string StaleReason,
        bool CanApply,
        string EffectiveTypesettingStyle,
        string? ExplicitTypesettingStyle,
        string TypesettingStyleSourceLabel,
        string? DraftTypesettingStyle,
        string TypesettingStyleContextNotice,
        bool DraftPinTypesettingStyle,
        bool CanPinTypesettingStyle,
        string TypesettingPinDisabledReason,
        string TypesettingPinNotice,
        bool IsFractionSelection,
        string FractionDisplayNotice,
        string CurrentTextRole,
        string TextRoleScopeNotice,
        string TextRoleDisabledReason,
        bool CanEditTextRole,
        string? DraftTextRole,
        string LatexCodeDiagnostic,
        bool CanExportLatex,
        int LatexTextDiagnosticCount,
        int LatexIncompleteSlotCount,
        NamedBounds[] ControlBounds,
        string CaptureEdge,
        double? CurrentGroupRowSpacing,
        string DraftGroupRowSpacingText,
        bool CanEditGroupRowSpacing,
        string GroupRowSpacingValidationError,
        string GroupRowSpacingDisabledReason,
        string GroupRowSpacingNotice,
        string GroupRowSpacingLatexDiagnostic,
        int LatexLayoutDiagnosticCount,
        BoundsRecord? LatexDiagnosticsViewportBounds,
        NamedBounds[] GroupRowSpacingControlBounds,
        string? CurrentBracketKind,
        string CurrentBracketKindLabel,
        string? DraftBracketKind,
        bool CanEditBracketKind,
        string BracketKindDisabledReason,
        string BracketKindNotice);

    private sealed record StructureScrollMetrics(
        double ViewportWidth,
        double ViewportHeight,
        double ExtentWidth,
        double ExtentHeight,
        double OffsetX,
        double OffsetY,
        string TreeHorizontalScrollVisibility,
        string InnerHorizontalScrollVisibility);

    private sealed record StructureRevealFailureEvidence(
        string Label,
        string SelectedNodeId,
        BoundsRecord TreeBounds,
        BoundsRecord ScrollViewerBounds,
        BoundsRecord EffectiveViewportBounds,
        BoundsRecord HeaderWindowBounds,
        BoundsRecord HeaderScrollViewerBounds,
        StructureScrollMetrics ScrollMetrics,
        string PngFile,
        string PngSha256);

    private sealed record NamedBounds(string Name, BoundsRecord Bounds, bool IsVisible = true);
    private sealed record BoundsRecord(double X, double Y, double Width, double Height);
}
