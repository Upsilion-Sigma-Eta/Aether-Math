using System.Globalization;
using System.Text;
using System.Windows.Input;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class SymbolPaletteCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("PAL-CATALOG-IDENTITY", "7개 기호 분류·안정 ID·Structures 템플릿 대응", CatalogIdentityAndStructureRoutes);
        yield return new("PAL-SEARCH-ALIAS", "분류별 이름·alias 검색과 공유 alias 보존", SearchesWithinCategoryWithoutAliasUniqueness);
        yield return new("PAL-UNICODE-PAGES", "Unicode scalar 페이지 총량·surrogate 경계·첫/마지막 page", UnicodePagesAreScalarIndexed);
        yield return new("PAL-UNICODE-LOOKUP", "U+ 조회·안전 placeholder·금지 scalar 삽입 거절", UnicodeLookupAndSafePlaceholders);
        yield return new("PAL-INSERT-REPLACE", "canonical 기호 삽입이 부분 선택만 교체하고 undo/redo", SymbolInsertionReplacesOnlySelection);
        yield return new("PAL-EMOJI-GRAPHEME", "ZWJ·variation selector·skin-tone 원문과 한 단계 undo", CuratedEmojiPreservesGraphemes);
        yield return new("PAL-BROWSE-PURITY", "탐색 불변성과 forged command 무변경", BrowsingIsSessionNeutralAndCommandsAreRevalidated);
        yield return new("PAL-ROUNDTRIP-GLYPH", "JSON/LaTeX 원문 보존 및 결정적 missing-glyph 대역", PersistsOriginalTextWhenProbeReportsMissing);
    }

    private static void CatalogIdentityAndStructureRoutes()
    {
        var categories = Enum.GetValues<MathSymbolCategory>();
        Qa.Equal(7, categories.Length, "기호 분류 enum 수");
        Qa.Equal(7, MathSymbolCatalog.Categories.Length, "표시용 기호 분류 수");
        Qa.Check(MathSymbolCatalog.Categories.Select(item => item.Category).ToHashSet().SetEquals(categories),
            "표시용 분류와 public enum이 다름");
        Qa.Check(MathSymbolCatalog.Categories.All(item =>
            !string.IsNullOrWhiteSpace(item.NameKo) && !string.IsNullOrWhiteSpace(item.NameEn)),
            "기호 분류의 한국어/영어 이름 누락");

        var entries = MathSymbolCatalog.Entries;
        Qa.Check(entries.Length > 0, "기호 catalog가 비어 있음");
        Qa.Equal(entries.Length, entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count(),
            "canonical symbol ID 전역 유일성");
        foreach (var category in categories)
        {
            var actual = MathSymbolCatalog.ForCategory(category);
            var expected = entries.Where(entry => entry.Category == category).ToArray();
            Qa.Equal(string.Join(',', expected.Select(entry => entry.Id)),
                string.Join(',', actual.Select(entry => entry.Id)), category + " category catalog 순서/구성");
            Qa.Equal(actual.Length, actual.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count(),
                category + " 결과 내 ID 중복");
        }
        Qa.Equal(0, MathSymbolCatalog.ForCategory((MathSymbolCategory)int.MaxValue).Length,
            "잘못된 분류는 결과를 만들지 않음");

        foreach (var entry in entries)
        {
            Qa.Check(!string.IsNullOrWhiteSpace(entry.Id) && MathSymbolCatalog.Find(entry.Id) == entry,
                "canonical symbol ID를 안정적으로 resolve하지 못함: " + entry.Id);
            Qa.Check((entry.Text is null) != (entry.TemplateKind is null),
                "symbol entry는 Text 또는 TemplateKind 중 정확히 하나여야 함: " + entry.Id);
            if (entry.Text is string text)
            {
                var codePoints = string.Join(' ', text.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));
                Qa.Equal(codePoints, entry.CodePoints, entry.Id + " code point metadata");
                Qa.Equal(codePoints, entry.CodePointLabel, entry.Id + " code point label");
            }
            else
            {
                Qa.Equal(MathSymbolCategory.Structures, entry.Category, entry.Id + " 템플릿 분류");
                Qa.Check(Enum.IsDefined(typeof(MathTemplateKind), entry.TemplateKind!.Value),
                    entry.Id + " 알 수 없는 template kind");
            }
        }

        var allKinds = Enum.GetValues<MathTemplateKind>().ToHashSet();
        var structureEntries = MathSymbolCatalog.ForCategory(MathSymbolCategory.Structures);
        Qa.Equal(allKinds.Count, structureEntries.Length, "Structures 템플릿 수");
        Qa.Check(structureEntries.Select(entry => entry.TemplateKind!.Value).ToHashSet().SetEquals(allKinds),
            "Structures 항목이 기존 10 template 종류와 일대일 대응하지 않음");

        foreach (var entry in structureEntries)
        {
            var session = new EditingSession(Qa.Workspace(Qa.Text("")));
            var palette = CreatePalette(
                text => session.InsertText(text),
                kind => session.InsertTemplate(kind).HasValue,
                new RecordingGlyphProbe());
            Qa.Check(palette.TryInsertPaletteEntry(entry.Id), entry.Id + " 공통 template 삽입 경로 실패");
            Qa.Check(Qa.Nodes(session.Workspace).Any(node => node.GetType() == ExpectedTemplateType(entry.TemplateKind!.Value)),
                entry.Id + "가 기대한 구조를 만들지 않음");
            Qa.Check(session.IsDirty && session.CanUndo, entry.Id + " 삽입의 단일 편집 history 없음");
        }

        var forged = new MathPaletteEntry("symbol.algebra.forged", MathSymbolCategory.Algebra,
            "변조", "Forged", [], "?", "?", null, "U+003F", "U+003F");
        Qa.Check(MathSymbolCatalog.Find(forged.Id) is null, "변조 entry ID가 canonical catalog에 등록됨");
    }

    private static void SearchesWithinCategoryWithoutAliasUniqueness()
    {
        var palette = CreatePalette(_ => true, _ => true, new RecordingGlyphProbe());
        foreach (var category in MathSymbolCatalog.Categories)
        {
            var representative = MathSymbolCatalog.ForCategory(category.Category)
                .FirstOrDefault(entry => entry.Aliases.Length > 0);
            Qa.Check(representative is not null, category.Category + " 검색용 alias fixture 없음");
            palette.SelectedCategory = category.Category;
            palette.SearchText = representative!.Aliases[0].ToUpperInvariant();
            Qa.Check(palette.Entries.Any(entry => entry.Id == representative.Id),
                category.Category + " alias 검색이 항목을 찾지 못함: " + representative.Aliases[0]);
            Qa.Check(palette.Entries.All(entry => entry.Category == category.Category),
                category.Category + " 검색 결과에 다른 category 항목이 섞임");
            Qa.Equal(palette.Entries.Length, palette.Entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count(),
                category.Category + " 검색 결과 ID 중복");
        }

        // `therefore`는 미적분과 논리 항목에서 함께 쓰이는 alias다. 전역 alias 유일성을 요구하지 않는다.
        var calculus = MathSymbolCatalog.Find("symbol.calculus.therefore")
            ?? throw new InvalidOperationException("미적분 therefore fixture 없음");
        var logic = MathSymbolCatalog.Find("symbol.logic.implies")
            ?? throw new InvalidOperationException("논리 therefore fixture 없음");
        Qa.Check(calculus.Aliases.Contains("therefore", StringComparer.OrdinalIgnoreCase),
            "미적분 shared-alias fixture 변경");
        Qa.Check(logic.Aliases.Contains("therefore", StringComparer.OrdinalIgnoreCase),
            "논리 shared-alias fixture 변경");
        palette.SelectedCategory = MathSymbolCategory.Calculus;
        palette.SearchText = "therefore";
        Qa.Check(palette.Entries.Any(entry => entry.Id == calculus.Id), "미적분 shared alias 결과 누락");
        palette.SelectedCategory = MathSymbolCategory.Logic;
        Qa.Check(palette.Entries.Any(entry => entry.Id == logic.Id), "논리 shared alias 결과 누락");

        palette.SelectedCategory = MathSymbolCategory.GreekLetters;
        palette.SearchText = "alpha";
        Qa.Check(palette.Entries.Any(entry => entry.Id == "symbol.greek.alpha") &&
            palette.Entries.Any(entry => entry.Id == "symbol.greek.alpha-uppercase"),
            "그리스 alpha 대·소문자 공유 alias 검색 결과 누락");
        Qa.Equal(palette.Entries.Length, palette.Entries.Select(entry => entry.Id)
            .Distinct(StringComparer.Ordinal).Count(), "그리스 공유 alias 결과에 ID 중복");

        palette.SelectedCategory = MathSymbolCategory.Algebra;
        palette.SearchText = "  no-such-symbol-qa  ";
        Qa.Equal(0, palette.Entries.Length, "무결과 검색이 빈 목록을 반환하지 않음");
        palette.SearchText = string.Empty;
        Qa.Equal(MathSymbolCatalog.ForCategory(MathSymbolCategory.Algebra).Length,
            palette.Entries.Length, "빈 검색에서 category 전체 결과 복원");

        var emojiAlias = CuratedEmojiCatalog.Entries.First(entry => entry.Aliases.Contains("developer"));
        palette.EmojiSearchText = " DEVELOPER ";
        Qa.Check(palette.EmojiEntries.Any(entry => entry.Id == emojiAlias.Id), "emoji alias 검색/trim 대소문자 처리");
        Qa.Equal(palette.EmojiEntries.Length,
            palette.EmojiEntries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count(),
            "emoji 검색 결과 ID 중복");
        palette.EmojiSearchText = "no-such-emoji-qa";
        Qa.Equal(0, palette.EmojiEntries.Length, "emoji 무결과 검색");
    }

    private static void UnicodePagesAreScalarIndexed()
    {
        Qa.Equal(128, UnicodeScalarBrowser.PageSize, "Unicode scalar page 크기");
        Qa.Equal(1_112_064, UnicodeScalarBrowser.ScalarCount, "전체 Unicode scalar 수");
        Qa.Equal(8_688, UnicodeScalarBrowser.PageCount, "Unicode scalar page 수");
        Qa.Equal(UnicodeScalarBrowser.ScalarCount,
            UnicodeScalarBrowser.PageSize * UnicodeScalarBrowser.PageCount, "페이지 분할 전체 수");

        CheckPage(0, 0x0000, 0x007F);
        CheckPage(431, 0xD780, 0xD7FF);
        CheckPage(432, 0xE000, 0xE07F);
        CheckPage(8_687, 0x10FF80, 0x10FFFF);
        Qa.Equal(0, UnicodeScalarBrowser.GetPageIndexForCodePoint(0), "U+0000 page index");
        Qa.Equal(431, UnicodeScalarBrowser.GetPageIndexForCodePoint(0xD7FF), "surrogate 직전 page index");
        Qa.Equal(432, UnicodeScalarBrowser.GetPageIndexForCodePoint(0xE000), "surrogate 직후 page index");
        Qa.Equal(8_687, UnicodeScalarBrowser.GetPageIndexForCodePoint(0x10FFFF), "최종 scalar page index");
        Qa.Throws<ArgumentOutOfRangeException>(() => UnicodeScalarBrowser.GetPage(-1), "음수 Unicode page");
        Qa.Throws<ArgumentOutOfRangeException>(() => UnicodeScalarBrowser.GetPage(UnicodeScalarBrowser.PageCount), "마지막 뒤 Unicode page");
        foreach (var invalid in new[] { -1, 0xD800, 0xDFFF, 0x110000 })
        {
            Qa.Check(!UnicodeScalarBrowser.TryCreateEntry(invalid, out var entry) && entry is null,
                $"유효하지 않은 scalar U+{invalid:X} entry 생성");
            Qa.Throws<ArgumentOutOfRangeException>(() => UnicodeScalarBrowser.GetPageIndexForCodePoint(invalid),
                $"유효하지 않은 scalar U+{invalid:X} page mapping");
        }

        void CheckPage(int index, int first, int last)
        {
            var page = UnicodeScalarBrowser.GetPage(index);
            Qa.Equal(index, page.PageIndex, $"page {index} index");
            Qa.Equal(UnicodeScalarBrowser.PageSize, page.Entries.Length, $"page {index} entry count");
            Qa.Equal(first, page.Entries[0].CodePoint, $"page {index} first scalar");
            Qa.Equal(last, page.Entries[^1].CodePoint, $"page {index} last scalar");
            Qa.Check(page.Entries.All(entry => entry.CodePoint is not (>= 0xD800 and <= 0xDFFF)),
                $"page {index}에 surrogate 포함");
            for (var offset = 1; offset < page.Entries.Length; offset++)
                Qa.Equal(page.Entries[offset - 1].CodePoint + 1, page.Entries[offset].CodePoint,
                    $"page {index} 내부 scalar 연속성 {offset}");
        }
    }

    private static void UnicodeLookupAndSafePlaceholders()
    {
        var empty = UnicodeScalarBrowser.Lookup("  \t");
        Qa.Equal(UnicodeLookupStatus.Empty, empty.Status, "blank U+ lookup");
        Qa.Equal<UnicodeScalarEntry?>(null, empty.Entry, "blank U+ entry");
        Qa.Equal<string?>(null, empty.Diagnostic, "blank U+ diagnostic");

        foreach (var (query, expectedCodePoint) in new[]
        {
            (" U+0000 ", 0), ("u+10ffff", 0x10FFFF), (" U+E000 ", 0xE000)
        })
        {
            var result = UnicodeScalarBrowser.Lookup(query);
            Qa.Equal(UnicodeLookupStatus.Found, result.Status, query + " valid lookup status");
            Qa.Equal(expectedCodePoint, result.Entry?.CodePoint ?? -1, query + " resolved code point");
        }

        foreach (var query in new[] { "U+", "0", "U+ZZ", "U+ＦＦ", "U+1234567", "U+110000", "U+D800", "U+DFFF" })
        {
            var invalid = UnicodeScalarBrowser.Lookup(query);
            Qa.Equal(UnicodeLookupStatus.Invalid, invalid.Status, query + " invalid lookup status");
            Qa.Equal<UnicodeScalarEntry?>(null, invalid.Entry, query + " invalid lookup entry");
            Qa.Check(!string.IsNullOrWhiteSpace(invalid.Diagnostic), query + " invalid lookup diagnostic");
        }

        foreach (var codePoint in new[] { 0x0000, 0x0020, 0x00A0, 0x200D, 0x0378, 0xE000, 0x0301, 0x2028, 0x2029 })
        {
            Qa.Check(UnicodeScalarBrowser.TryCreateEntry(codePoint, out var entry) && entry is not null,
                $"U+{codePoint:X4} 유효 scalar entry");
            Qa.Check(!entry!.CanInsert, $"U+{codePoint:X4} 금지 category 삽입 가능 상태");
            Qa.Check(entry.PreviewIsPlaceholder && !string.IsNullOrWhiteSpace(entry.PreviewText),
                $"U+{codePoint:X4} 안전 placeholder 누락");
            Qa.Check(!entry.PreviewText.Contains(char.ConvertFromUtf32(codePoint), StringComparison.Ordinal),
                $"U+{codePoint:X4} 원문 금지 scalar가 preview에 노출됨");
            Qa.Check(entry.DisabledReason is { Length: > 0 }, $"U+{codePoint:X4} 비활성 사유 누락");
            Qa.Check(!entry.PreviewText.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control),
                $"U+{codePoint:X4} preview에 제어문자 노출");
        }

        var session = new EditingSession(Qa.Workspace(Qa.Text("original")));
        var probe = new RecordingGlyphProbe();
        var palette = CreatePalette(text => session.InsertText(text), kind => session.InsertTemplate(kind).HasValue, probe);
        palette.Mode = SymbolPaletteMode.Unicode;
        Qa.Check(!palette.PreviousUnicodePageCommand.CanExecute(null), "첫 page에서 이전 page가 활성화됨");
        var firstPage = palette.UnicodePageIndex;
        palette.PreviousUnicodePageCommand.Execute(null);
        Qa.Equal(firstPage, palette.UnicodePageIndex, "첫 page 이전 no-op");
        palette.SelectedUnicodeEntry = UnicodeScalarBrowser.GetPage(0).Entries[0];
        Qa.Equal(0, palette.SelectedUnicodeEntry?.CodePoint ?? -1, "non-insertable scalar 상세 선택 가능");
        Qa.Equal<MathGlyphCoverageResult?>(null, palette.SelectedUnicodeGlyphCoverage,
            "non-insertable scalar에는 glyph probe를 호출하지 않음");
        Qa.Equal(0, probe.Calls.Count, "non-insertable scalar glyph probe 호출");
        Qa.Check(!palette.InsertUnicodeScalarCommand.CanExecute(0), "U+0000 삽입 command가 disabled가 아님");
        palette.InsertUnicodeScalarCommand.Execute(0);
        Qa.Check(!palette.TryInsertUnicodeScalar(0), "직접 Unicode 삽입 경로가 U+0000을 거절하지 않음");
        Qa.Check(!palette.TryInsertUnicodeScalar(0x0020), "직접 Unicode 삽입 경로가 공백 구분자를 거절하지 않음");
        Qa.Equal("original", Qa.Leaf(session, session.Caret.NodeId).Text, "금지 scalar 삽입 뒤 세션 텍스트");

        palette.BrowseUnicodeRange("unicode.range.greek");
        var retainedPage = palette.UnicodePageIndex;
        palette.UnicodeQuery = "U+1F600";
        palette.LookupUnicodeCommand.Execute(null);
        Qa.Equal(UnicodeLookupStatus.Found, palette.UnicodeLookupResult?.Status ?? UnicodeLookupStatus.Empty,
            "페이지 밖 유효 scalar 조회 결과");
        Qa.Equal(0x1F600, palette.SelectedUnicodeEntry?.CodePoint ?? -1, "페이지 밖 조회 선택 유지");
        Qa.Equal(1, palette.VisibleUnicodeEntries.Length, "페이지 밖 lookup 결과의 visible 목록");
        palette.UnicodeQuery = "  ";
        Qa.Equal(retainedPage, palette.UnicodePageIndex, "blank lookup/page 초기화 전 page 보존");
        Qa.Check(palette.UnicodeLookupResult is null && palette.UnicodeLookupError.Length == 0 &&
            palette.SelectedUnicodeEntry is null && palette.VisibleUnicodeEntries.Length == UnicodeScalarBrowser.PageSize,
            "blank query가 조회/선택을 비우고 현재 page 목록을 복구하지 않음");
        palette.UnicodeQuery = "U+D800";
        palette.LookupUnicodeCommand.Execute(null);
        Qa.Check(palette.UnicodeLookupResult is null && palette.SelectedUnicodeEntry is null &&
            palette.UnicodePageIndex == retainedPage && !string.IsNullOrWhiteSpace(palette.UnicodeLookupError),
            "invalid query가 page를 유지하면서 결과/선택을 비우고 진단하지 않음");

        Qa.Check(palette.SetUnicodePageIndex(UnicodeScalarBrowser.PageCount - 1), "마지막 page 설정 실패");
        Qa.Check(!palette.NextUnicodePageCommand.CanExecute(null), "마지막 page에서 다음 page가 활성화됨");
        var finalPage = palette.UnicodePageIndex;
        palette.NextUnicodePageCommand.Execute(null);
        Qa.Equal(finalPage, palette.UnicodePageIndex, "마지막 page 다음 no-op");
        palette.UnicodeQuery = "U+10FFFF";
        palette.LookupUnicodeCommand.Execute(null);
        var beforeInvalidPage = (palette.UnicodePageIndex, palette.UnicodeQuery,
            palette.UnicodeLookupError, palette.UnicodeLookupResult, palette.SelectedUnicodeEntry);
        Qa.Check(!palette.SetUnicodePageIndex(-1), "음수 page 설정 허용");
        Qa.Check(!palette.SetUnicodePageIndex(UnicodeScalarBrowser.PageCount), "마지막 뒤 page 설정 허용");
        Qa.Equal(beforeInvalidPage, (palette.UnicodePageIndex, palette.UnicodeQuery,
            palette.UnicodeLookupError, palette.UnicodeLookupResult, palette.SelectedUnicodeEntry),
            "범위 밖 page 요청이 query/lookup/selection/page를 변경");
    }

    private static void SymbolInsertionReplacesOnlySelection()
    {
        const string source = "L🙂e\u0301R";
        var leaf = Qa.Text(source);
        var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Select(session, leaf.Id, 1, 3);
        var before = Capture(session);
        var probe = new RecordingGlyphProbe();
        var palette = CreatePalette(session.InsertText, kind => session.InsertTemplate(kind).HasValue, probe);
        var symbol = MathSymbolCatalog.ForCategory(MathSymbolCategory.Algebra)
            .First(entry => entry.Text is not null);
        var symbolText = symbol.Text ?? throw new InvalidOperationException("선택된 대수 symbol에 text가 없습니다.");
        palette.SelectedCategory = symbol.Category;
        palette.SelectedPaletteEntry = symbol;
        Qa.Equal(MathGlyphCoverageStatus.Present, palette.SelectedPaletteGlyphCoverage?.Status ?? MathGlyphCoverageStatus.Unknown,
            "선택 symbol glyph advisory status");
        Qa.Check(palette.InsertPaletteEntryCommand.CanExecute(symbol.Id), "canonical symbol command disabled");
        palette.InsertPaletteEntryCommand.Execute(symbol.Id);

        Qa.Equal(source[..1] + symbolText + source[^1..], Qa.Leaf(session, leaf.Id).Text,
            "symbol 삽입이 선택 범위만 교체하고 이웃 grapheme을 유지");
        Qa.Equal(new MathCaret(leaf.Id, 1 + Qa.Elements(symbolText)), session.Caret, "symbol 삽입 caret 위치");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, "symbol 삽입 history/dirty 경계");
        Qa.Check(session.Revision == before.Revision + 1, "symbol 삽입이 단일 revision commit이 아님");

        session.Undo();
        AssertRestoredState(before, session, "symbol 삽입 undo");
        Qa.Equal(before.Revision + 2, session.Revision, "symbol 삽입 undo revision");
        Qa.Check(session.CanRedo, "symbol 삽입 undo 뒤 redo 없음");
        session.Redo();
        Qa.Equal(source[..1] + symbolText + source[^1..], Qa.Leaf(session, leaf.Id).Text, "symbol 삽입 redo text");
        Qa.Equal(new MathCaret(leaf.Id, 1 + Qa.Elements(symbolText)), session.Caret, "symbol 삽입 redo caret 위치");
        Qa.Equal(before.Revision + 3, session.Revision, "symbol 삽입 redo revision");
        Qa.Check(session.IsDirty && !session.CanRedo, "symbol 삽입 redo history/dirty 경계");

        var mainViewModel = new MainWindowViewModel();
        var originalSession = mainViewModel.Session;
        Qa.Check(originalSession.InsertText("saved old workspace"), "MainWindowViewModel palette 경계 fixture 편집");
        originalSession.MarkSaved(originalSession.CaptureSnapshot());
        var originalFingerprint = Qa.Fingerprint(originalSession.Workspace);
        var paletteIdentity = mainViewModel.SymbolPalette;
        var previousSessionId = originalSession.Workspace.Id;
        var focusRequests = 0;
        mainViewModel.EditorFocusRequested += (_, _) => focusRequests++;
        mainViewModel.NewWorkspaceCommand.Execute(null);
        Qa.Check(!ReferenceEquals(originalSession, mainViewModel.Session), "NewWorkspaceCommand가 Session을 교체하지 않음");
        Qa.Check(ReferenceEquals(paletteIdentity, mainViewModel.SymbolPalette), "Session 교체로 SymbolPaletteViewModel이 불필요하게 교체됨");
        var replacementSession = mainViewModel.Session;
        Qa.Check(replacementSession.Workspace.Id != previousSessionId, "새 Session이 다른 workspace ID를 사용하지 않음");
        var replacementLeaf = Qa.Nodes(replacementSession.Workspace).OfType<TextNode>().First();
        var routedSymbol = MathSymbolCatalog.Find("symbol.greek.pi")
            ?? throw new InvalidOperationException("Session 교체 삽입 fixture 없음");
        var routedSymbolText = routedSymbol.Text
            ?? throw new InvalidOperationException("Session 교체 삽입 fixture가 템플릿 항목임");
        var focusBeforeRejectedInsert = focusRequests;
        Qa.Check(!mainViewModel.SymbolPalette.TryInsertPaletteEntry("symbol.algebra.forged"),
            "MainWindowViewModel 경로에서 변조된 symbol ID 허용");
        Qa.Check(!mainViewModel.SymbolPalette.TryInsertUnicodeScalar(0xD800),
            "MainWindowViewModel 경로에서 surrogate scalar 허용");
        Qa.Equal(focusBeforeRejectedInsert, focusRequests, "실패한 palette 삽입이 편집기 focus 요청을 발생");
        Qa.Check(mainViewModel.SymbolPalette.TryInsertPaletteEntry(routedSymbol.Id),
            "교체 후 SymbolPalette callback이 현재 Session으로 라우팅되지 않음");
        Qa.Equal(focusBeforeRejectedInsert + 1, focusRequests, "성공한 palette 삽입 뒤 편집기 focus 요청 누락/중복");
        Qa.Equal(routedSymbolText, Qa.Leaf(replacementSession, replacementLeaf.Id).Text,
            "새 Session에 기호가 입력되지 않음");
        Qa.Equal(originalFingerprint, Qa.Fingerprint(originalSession.Workspace),
            "새 Session 삽입이 기존 저장 Session을 변경함");
        Qa.Check(!originalSession.IsDirty && replacementSession.IsDirty,
            "palette Session 교체 뒤 dirty 상태가 현재 Session에만 적용되지 않음");
    }

    private static void CuratedEmojiPreservesGraphemes()
    {
        var samples = new[]
        {
            CuratedEmojiCatalog.Find("emoji.heart-red")!,
            CuratedEmojiCatalog.Find("emoji.thumbs-up-medium-skin")!,
            CuratedEmojiCatalog.Find("emoji.woman-technologist")!,
            CuratedEmojiCatalog.Find("emoji.rainbow-flag")!
        };
        Qa.Check(samples.All(entry => entry is not null), "curated emoji sequence fixture 누락");
        Qa.Check(samples.Any(entry => entry.Text.Contains('\uFE0F')), "variation selector emoji 표본 누락");
        Qa.Check(samples.Any(entry => entry.Text.EnumerateRunes().Any(rune => rune.Value == 0x200D)),
            "ZWJ emoji 표본 누락");
        Qa.Check(samples.Any(entry => entry.Text.EnumerateRunes().Any(rune => rune.Value is >= 0x1F3FB and <= 0x1F3FF)),
            "skin-tone emoji 표본 누락");

        foreach (var emoji in samples)
        {
            Qa.Check(CuratedEmojiCatalog.Find(emoji.Id) == emoji, emoji.Id + " canonical emoji resolve");
            var codePoints = string.Join(' ', emoji.Text.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));
            Qa.Equal(codePoints, emoji.CodePoints, emoji.Id + " original code point sequence");
            Qa.Equal(1, Qa.Elements(emoji.Text), emoji.Id + " curated grapheme count");

            const string source = "LxR";
            var leaf = Qa.Text(source);
            var session = new EditingSession(Qa.Workspace(leaf));
            Qa.Select(session, leaf.Id, 1, 2);
            var before = Capture(session);
            var probe = new RecordingGlyphProbe(MathGlyphCoverageStatus.Unknown);
            var palette = CreatePalette(session.InsertText, kind => session.InsertTemplate(kind).HasValue, probe);
            palette.SelectedEmojiEntry = emoji;
            Qa.Equal(emoji.Text, probe.Calls.Single().Text, emoji.Id + " coverage probe exact input sequence");
            Qa.Equal(MathGlyphCoverageStatus.Unknown, palette.SelectedEmojiGlyphCoverage?.Status ?? MathGlyphCoverageStatus.Present,
                emoji.Id + " 복합 문자열 probe 불확실성");
            Qa.Check(palette.InsertEmojiCommand.CanExecute(emoji.Id), emoji.Id + " emoji command disabled");
            palette.InsertEmojiCommand.Execute(emoji.Id);

            Qa.Equal("L" + emoji.Text + "R", Qa.Leaf(session, leaf.Id).Text, emoji.Id + " raw text insertion/neighbor preservation");
            Qa.Equal(new MathCaret(leaf.Id, 2), session.Caret, emoji.Id + " emoji grapheme caret offset");
            Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, emoji.Id + " one-step insertion history");
            session.Undo();
            AssertRestoredState(before, session, emoji.Id + " undo");
            Qa.Equal(before.Revision + 2, session.Revision, emoji.Id + " undo revision");
            session.Redo();
            Qa.Equal("L" + emoji.Text + "R", Qa.Leaf(session, leaf.Id).Text, emoji.Id + " redo raw sequence");
            Qa.Equal(new MathCaret(leaf.Id, 2), session.Caret, emoji.Id + " redo grapheme caret");
            Qa.Equal(before.Revision + 3, session.Revision, emoji.Id + " redo revision");
        }
    }

    private static void BrowsingIsSessionNeutralAndCommandsAreRevalidated()
    {
        var leaf = Qa.Text("x🙂e\u0301");
        var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Caret(session, leaf.Id, 2);
        Qa.Check(session.InsertText(" dirty"), "탐색 불변성 fixture dirty 편집");
        var probe = new RecordingGlyphProbe();
        var palette = CreatePalette(session.InsertText, kind => session.InsertTemplate(kind).HasValue, probe);
        var before = Capture(session);
        Qa.Check(!palette.SetUnicodePageIndex(-1), "범위 밖 page가 browse API에서 허용됨");

        palette.Mode = SymbolPaletteMode.Symbols;
        palette.SelectedCategory = MathSymbolCategory.Calculus;
        palette.SearchText = "integral";
        palette.SelectedPaletteEntry = palette.Entries.First();
        var canonicalSelection = palette.SelectedPaletteEntry;
        var forgedEntry = new MathPaletteEntry("symbol.algebra.forged", MathSymbolCategory.Algebra,
            "변조", "Forged", [], "?", "?", null, "U+003F", "U+003F");
        palette.SelectedPaletteEntry = forgedEntry;
        Qa.Equal(canonicalSelection, palette.SelectedPaletteEntry, "변조된 palette object가 canonical selection을 덮어씀");
        palette.Mode = SymbolPaletteMode.Emoji;
        palette.EmojiSearchText = " developer ";
        palette.SelectedEmojiEntry = palette.EmojiEntries.First();
        palette.Mode = SymbolPaletteMode.Unicode;
        palette.UnicodeQuery = " U+0378 ";
        palette.LookupUnicodeCommand.Execute(null);
        Qa.Equal(UnicodeLookupStatus.Found, palette.UnicodeLookupResult?.Status ?? UnicodeLookupStatus.Empty,
            "조회 가능한 미할당 scalar 상세 결과");
        Qa.Equal(0x0378, palette.SelectedUnicodeEntry?.CodePoint ?? -1, "미할당 scalar 선택 유지");
        Qa.Check(!palette.SelectedUnicodeEntry!.PreviewText.Contains(char.ConvertFromUtf32(0x0378), StringComparison.Ordinal),
            "미할당 scalar 원문을 안전 placeholder 대신 표시");
        Qa.Check(!string.IsNullOrWhiteSpace(palette.SelectedUnicodeEntry.DisabledReason), "미할당 scalar disabled 사유");
        AssertState(before, session, "category/query/list selection/lookup 탐색");

        var pageBefore = palette.UnicodePageIndex;
        palette.BrowseUnicodeRange("unicode.range.greek");
        Qa.Equal(UnicodeScalarBrowser.GetPageIndexForCodePoint(0x0370), palette.UnicodePageIndex,
            "그리스 quick range page 이동");
        Qa.Check(palette.UnicodeQuery.Length == 0 && palette.UnicodeLookupResult is null &&
            palette.UnicodeLookupError.Length == 0 && palette.SelectedUnicodeEntry is null,
            "정상 page 이동이 기존 조회/선택 상태를 초기화하지 않음");
        Qa.Check(palette.UnicodePageIndex != pageBefore, "quick range가 page를 바꾸지 않음");
        palette.PreviousUnicodePageCommand.Execute(null);
        Qa.Equal(UnicodeScalarBrowser.GetPageIndexForCodePoint(0x0370) - 1,
            palette.UnicodePageIndex, "이전 page command");
        palette.NextUnicodePageCommand.Execute(null);
        Qa.Equal(UnicodeScalarBrowser.GetPageIndexForCodePoint(0x0370),
            palette.UnicodePageIndex, "다음 page command");
        palette.BrowseUnicodeRangeCommand.Execute("forged.range.id");
        Qa.Check(!palette.BrowseUnicodeRangeCommand.CanExecute("forged.range.id"), "변조 quick range command 허용");

        var categoryBeforeInvalid = palette.SelectedCategory;
        var entriesBeforeInvalid = string.Join(',', palette.Entries.Select(entry => entry.Id));
        palette.SelectedCategory = (MathSymbolCategory)int.MaxValue;
        Qa.Equal(categoryBeforeInvalid, palette.SelectedCategory, "잘못된 category setter가 값을 변경");
        Qa.Equal(entriesBeforeInvalid, string.Join(',', palette.Entries.Select(entry => entry.Id)),
            "잘못된 category setter가 결과를 변경");
        var modeBeforeInvalid = palette.Mode;
        palette.Mode = (SymbolPaletteMode)int.MaxValue;
        Qa.Equal(modeBeforeInvalid, palette.Mode, "잘못된 mode setter가 값을 변경");

        var callsBeforeForged = probe.Calls.Count;
        var forgedId = "symbol.algebra.forged";
        Qa.Check(!palette.InsertPaletteEntryCommand.CanExecute(forgedId), "변조 symbol ID command 허용");
        palette.InsertPaletteEntryCommand.Execute(forgedId);
        Qa.Check(!palette.InsertEmojiCommand.CanExecute("emoji.forged"), "변조 emoji ID command 허용");
        palette.InsertEmojiCommand.Execute("emoji.forged");
        Qa.Check(!palette.InsertUnicodeScalarCommand.CanExecute(0), "금지 scalar command 허용");
        palette.InsertUnicodeScalarCommand.Execute(0);
        Qa.Check(!palette.TryInsertPaletteEntry(forgedId), "직접 symbol 삽입 경로가 변조 ID 허용");
        Qa.Check(!palette.TryInsertEmoji("emoji.forged"), "직접 emoji 삽입 경로가 변조 ID 허용");
        Qa.Check(!palette.TryInsertUnicodeScalar(0xD800), "직접 Unicode 삽입 경로가 surrogate 허용");
        Qa.Equal(callsBeforeForged, probe.Calls.Count, "변조 command가 glyph probe를 호출");
        AssertState(before, session, "page/range/forged command 탐색");
    }

    private static async Task PersistsOriginalTextWhenProbeReportsMissing()
    {
        const string original = "A";
        var leaf = Qa.Text(original);
        var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Caret(session, leaf.Id, 1);
        var probe = new RecordingGlyphProbe(MathGlyphCoverageStatus.Missing);
        var palette = CreatePalette(session.InsertText, kind => session.InsertTemplate(kind).HasValue, probe);
        var symbol = MathSymbolCatalog.Find("symbol.greek.pi")
            ?? throw new InvalidOperationException("Greek pi catalog fixture 없음");
        var symbolText = symbol.Text ?? throw new InvalidOperationException("Greek pi symbol fixture는 text entry여야 함");
        palette.SelectedCategory = symbol.Category;
        palette.SelectedPaletteEntry = symbol;
        Qa.Equal(MathGlyphCoverageStatus.Missing,
            palette.SelectedPaletteGlyphCoverage?.Status ?? MathGlyphCoverageStatus.Unknown,
            "injected glyph probe missing observation");
        Qa.Equal(symbolText, probe.Calls.Single().Text, "probe에 원문 symbol 문자열 전달");
        Qa.Check(palette.TryInsertPaletteEntry(symbol.Id), "glyph missing 경고 뒤 canonical symbol 삽입이 차단됨");
        Qa.Equal(original + symbolText, Qa.Leaf(session, leaf.Id).Text,
            "missing glyph probe가 삽입 원문을 대체/손실");

        var emoji = CuratedEmojiCatalog.Find("emoji.woman-technologist")
            ?? throw new InvalidOperationException("ZWJ emoji fixture 없음");
        Qa.Check(palette.TryInsertEmoji(emoji.Id), "curated ZWJ emoji 삽입 실패");
        Qa.Check(palette.TryInsertUnicodeScalar(0x1F600), "Unicode scalar 삽입 실패");
        var scalarText = char.ConvertFromUtf32(0x1F600);
        var expected = original + symbolText + emoji.Text + scalarText;
        Qa.Equal(expected, Qa.Leaf(session, leaf.Id).Text, "symbol+emoji+scalar 원문 문자열");
        var latex = LatexSerializer.ToLatex(session.Workspace);
        Qa.Check(latex.Contains(expected, StringComparison.Ordinal), "LaTeX serializer에서 입력 Unicode 원문이 바뀌거나 사라짐");

        using var directory = new TempDirectory();
        var fileService = new WorkspaceFileService();
        var path = directory.File("palette-unicode.aethermath");
        await fileService.SaveAsync(path, session.CaptureSnapshot());
        var reopened = await fileService.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "JSON save/reopen workspace roundtrip");
        Qa.Equal(expected,
            string.Concat(Qa.Nodes(reopened).OfType<TextNode>().Select(node => node.Text)),
            "JSON save/reopen Unicode code-unit sequence");

        // 실제 설치 글꼴 결과는 관측만 남기며 특정 문자가 없다고 가정하지 않는다.
        var desktopProbe = new AvaloniaMathGlyphCoverageProbe().Probe(symbolText, "Cambria Math");
        Qa.Check(Enum.IsDefined(typeof(MathGlyphCoverageStatus), desktopProbe.Status),
            "실제 Desktop font probe가 알 수 없는 상태 enum을 반환");
        Console.WriteLine($"GLYPH-OBSERVATION status={desktopProbe.Status}; family={desktopProbe.FontFamily}; reason={desktopProbe.Reason}");
    }

    private static SymbolPaletteViewModel CreatePalette(
        Func<string, bool> insertText,
        Func<MathTemplateKind, bool> insertTemplate,
        RecordingGlyphProbe probe) =>
        new(insertText, insertTemplate, () => "Cambria Math", probe);

    private static Type ExpectedTemplateType(MathTemplateKind kind) => kind switch
    {
        MathTemplateKind.Fraction => typeof(FractionNode),
        MathTemplateKind.Radical => typeof(RadicalNode),
        MathTemplateKind.Superscript or MathTemplateKind.Subscript => typeof(ScriptNode),
        MathTemplateKind.Integral => typeof(IntegralNode),
        MathTemplateKind.Summation => typeof(SummationNode),
        MathTemplateKind.Limit => typeof(LimitNode),
        MathTemplateKind.Matrix => typeof(MatrixNode),
        MathTemplateKind.Brackets => typeof(BracketsNode),
        MathTemplateKind.Cases => typeof(CasesNode),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static SessionState Capture(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.CaptureSnapshot().StateId, session.Revision,
        session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
        session.IsDirty, session.CanUndo, session.CanRedo);

    private static void AssertState(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Workspace, Qa.Fingerprint(actual.Workspace), label + " workspace");
        Qa.Equal(expected.StateId, actual.CaptureSnapshot().StateId, label + " state ID");
        Qa.Equal(expected.Revision, actual.Revision, label + " revision");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " structural selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + " undo history");
        Qa.Equal(expected.CanRedo, actual.CanRedo, label + " redo history");
    }

    private static void AssertRestoredState(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Workspace, Qa.Fingerprint(actual.Workspace), label + " workspace");
        Qa.Equal(expected.StateId, actual.CaptureSnapshot().StateId, label + " state ID");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " structural selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + " undo history");
    }

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

    private sealed class RecordingGlyphProbe(MathGlyphCoverageStatus status = MathGlyphCoverageStatus.Present)
        : IMathGlyphCoverageProbe
    {
        public List<(string Text, string FontFamily)> Calls { get; } = [];

        public MathGlyphCoverageResult Probe(string text, string fontFamily)
        {
            Calls.Add((text, fontFamily));
            return new MathGlyphCoverageResult(status, fontFamily, "결정적 QA glyph probe 관측");
        }
    }
}
