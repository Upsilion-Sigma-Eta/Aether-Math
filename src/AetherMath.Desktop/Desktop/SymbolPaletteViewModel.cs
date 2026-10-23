using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using AetherMath.Editing;

namespace AetherMath.Desktop;

public sealed class SymbolPaletteViewModel : INotifyPropertyChanged
{
    private readonly Func<string, bool> _insertText;
    private readonly Func<MathTemplateKind, bool> _insertTemplate;
    private readonly Func<string> _getPrimaryFontFamily;
    private readonly IMathGlyphCoverageProbe _glyphCoverageProbe;
    private readonly ParameterizedCommand _insertPaletteEntryCommand;
    private readonly ParameterizedCommand _insertEmojiCommand;
    private readonly ParameterizedCommand _insertUnicodeScalarCommand;
    private readonly RelayCommand _previousUnicodePageCommand;
    private readonly RelayCommand _nextUnicodePageCommand;
    private readonly ParameterizedCommand _browseUnicodeRangeCommand;
    private readonly RelayCommand _lookupUnicodeCommand;
    private SymbolPaletteMode _mode;
    private MathSymbolCategory _selectedCategory = MathSymbolCategory.Algebra;
    private string _searchText = string.Empty;
    private ImmutableArray<MathPaletteEntry> _entries = ImmutableArray<MathPaletteEntry>.Empty;
    private MathPaletteEntry? _selectedPaletteEntry;
    private MathGlyphCoverageResult? _selectedPaletteGlyphCoverage;
    private string _emojiSearchText = string.Empty;
    private ImmutableArray<CuratedEmojiEntry> _emojiEntries = CuratedEmojiCatalog.Entries;
    private CuratedEmojiEntry? _selectedEmojiEntry;
    private MathGlyphCoverageResult? _selectedEmojiGlyphCoverage;
    private string _unicodeQuery = string.Empty;
    private int _unicodePageIndex;
    private ImmutableArray<UnicodeScalarEntry> _unicodeEntries;
    private UnicodeLookupResult? _unicodeLookupResult;
    private string _unicodeLookupError = string.Empty;
    private UnicodeScalarEntry? _selectedUnicodeEntry;
    private MathGlyphCoverageResult? _selectedUnicodeGlyphCoverage;

    public SymbolPaletteViewModel(
        Func<string, bool> insertText,
        Func<MathTemplateKind, bool> insertTemplate,
        Func<string> getPrimaryFontFamily,
        IMathGlyphCoverageProbe glyphCoverageProbe)
    {
        _insertText = insertText ?? throw new ArgumentNullException(nameof(insertText));
        _insertTemplate = insertTemplate ?? throw new ArgumentNullException(nameof(insertTemplate));
        _getPrimaryFontFamily = getPrimaryFontFamily ?? throw new ArgumentNullException(nameof(getPrimaryFontFamily));
        _glyphCoverageProbe = glyphCoverageProbe ?? throw new ArgumentNullException(nameof(glyphCoverageProbe));
        _entries = FilterPaletteEntries();
        _unicodeEntries = UnicodeScalarBrowser.GetPage(_unicodePageIndex).Entries;

        _insertPaletteEntryCommand = new ParameterizedCommand(
            parameter => { if (parameter is string id) TryInsertPaletteEntry(id); },
            parameter => parameter is string id && MathSymbolCatalog.Find(id) is not null);
        _insertEmojiCommand = new ParameterizedCommand(
            parameter => { if (parameter is string id) TryInsertEmoji(id); },
            parameter => parameter is string id && CuratedEmojiCatalog.Find(id) is not null);
        _insertUnicodeScalarCommand = new ParameterizedCommand(
            parameter => { if (parameter is int codePoint) TryInsertUnicodeScalar(codePoint); },
            CanInsertUnicodeScalarParameter);
        _previousUnicodePageCommand = new RelayCommand(() => NavigateToPage(_unicodePageIndex - 1),
            () => _unicodePageIndex > 0);
        _nextUnicodePageCommand = new RelayCommand(() => NavigateToPage(_unicodePageIndex + 1),
            () => _unicodePageIndex < UnicodeScalarBrowser.PageCount - 1);
        _browseUnicodeRangeCommand = new ParameterizedCommand(
            parameter => { if (parameter is string id) BrowseUnicodeRange(id); },
            parameter => parameter is string id && UnicodeScalarBrowser.TryFindQuickRange(id, out _));
        _lookupUnicodeCommand = new RelayCommand(LookupUnicode);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImmutableArray<MathPaletteCategoryInfo> Categories => MathSymbolCatalog.Categories;

    public SymbolPaletteMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(typeof(SymbolPaletteMode), value) || _mode == value)
                return;
            _mode = value;
            OnPropertyChanged(nameof(Mode));
        }
    }

    public MathSymbolCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Enum.IsDefined(typeof(MathSymbolCategory), value) || _selectedCategory == value)
                return;
            _selectedCategory = value;
            OnPropertyChanged(nameof(SelectedCategory));
            RefreshPaletteEntries();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_searchText == normalized)
                return;
            _searchText = normalized;
            OnPropertyChanged(nameof(SearchText));
            RefreshPaletteEntries();
        }
    }

    public ImmutableArray<MathPaletteEntry> Entries => _entries;

    public MathPaletteEntry? SelectedPaletteEntry
    {
        get => _selectedPaletteEntry;
        set
        {
            if (value is null)
            {
                SetSelectedPaletteEntry(null);
                return;
            }

            var canonical = MathSymbolCatalog.Find(value.Id);
            if (canonical is null || !_entries.Any(entry => entry.Id == canonical.Id))
                return;
            SetSelectedPaletteEntry(canonical);
        }
    }

    public MathGlyphCoverageResult? SelectedPaletteGlyphCoverage => _selectedPaletteGlyphCoverage;

    public string EmojiSearchText
    {
        get => _emojiSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_emojiSearchText == normalized)
                return;
            _emojiSearchText = normalized;
            OnPropertyChanged(nameof(EmojiSearchText));
            RefreshEmojiEntries();
        }
    }

    public ImmutableArray<CuratedEmojiEntry> EmojiEntries => _emojiEntries;

    public CuratedEmojiEntry? SelectedEmojiEntry
    {
        get => _selectedEmojiEntry;
        set
        {
            if (value is null)
            {
                SetSelectedEmojiEntry(null);
                return;
            }

            var canonical = CuratedEmojiCatalog.Find(value.Id);
            if (canonical is null || !_emojiEntries.Any(entry => entry.Id == canonical.Id))
                return;
            SetSelectedEmojiEntry(canonical);
        }
    }

    public MathGlyphCoverageResult? SelectedEmojiGlyphCoverage => _selectedEmojiGlyphCoverage;

    public string UnicodeQuery
    {
        get => _unicodeQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (_unicodeQuery == normalized)
                return;
            _unicodeQuery = normalized;
            OnPropertyChanged(nameof(UnicodeQuery));
            ClearUnicodeLookup();
        }
    }

    public int UnicodePageIndex => _unicodePageIndex;
    public int UnicodePageCount => UnicodeScalarBrowser.PageCount;
    public ImmutableArray<UnicodeScalarEntry> UnicodeEntries => _unicodeEntries;
    public ImmutableArray<UnicodeScalarEntry> VisibleUnicodeEntries =>
        _unicodeLookupResult?.Entry is { } lookupEntry
            ? ImmutableArray.Create(lookupEntry)
            : _unicodeEntries;
    public UnicodeLookupResult? UnicodeLookupResult => _unicodeLookupResult;
    public string UnicodeLookupError => _unicodeLookupError;

    public UnicodeScalarEntry? SelectedUnicodeEntry
    {
        get => _selectedUnicodeEntry;
        set
        {
            if (value is null)
            {
                SetSelectedUnicodeEntry(null);
                return;
            }

            if (!UnicodeScalarBrowser.TryCreateEntry(value.CodePoint, out var canonical) || canonical is null)
                return;
            var belongsToPage = UnicodeScalarBrowser.GetPageIndexForCodePoint(canonical.CodePoint) == _unicodePageIndex;
            var isLookupResult = _unicodeLookupResult?.Entry?.CodePoint == canonical.CodePoint;
            if (!belongsToPage && !isLookupResult)
                return;
            SetSelectedUnicodeEntry(canonical);
        }
    }

    public MathGlyphCoverageResult? SelectedUnicodeGlyphCoverage => _selectedUnicodeGlyphCoverage;
    public ImmutableArray<UnicodeQuickRange> UnicodeQuickRanges => UnicodeScalarBrowser.QuickRanges;

    public ICommand InsertPaletteEntryCommand => _insertPaletteEntryCommand;
    public ICommand InsertEmojiCommand => _insertEmojiCommand;
    public ICommand InsertUnicodeScalarCommand => _insertUnicodeScalarCommand;
    public ICommand PreviousUnicodePageCommand => _previousUnicodePageCommand;
    public ICommand NextUnicodePageCommand => _nextUnicodePageCommand;
    public ICommand BrowseUnicodeRangeCommand => _browseUnicodeRangeCommand;
    public ICommand LookupUnicodeCommand => _lookupUnicodeCommand;

    public bool TryInsertPaletteEntry(string? stableId)
    {
        var entry = MathSymbolCatalog.Find(stableId);
        if (entry is null)
            return false;

        if (entry.Text is string text && entry.TemplateKind is null)
            return _insertText(text);
        if (entry.Text is null && entry.TemplateKind is MathTemplateKind kind &&
            Enum.IsDefined(typeof(MathTemplateKind), kind))
            return _insertTemplate(kind);
        return false;
    }

    public bool TryInsertEmoji(string? stableId)
    {
        var entry = CuratedEmojiCatalog.Find(stableId);
        return entry is not null && _insertText(entry.Text);
    }

    public bool TryInsertUnicodeScalar(int codePoint)
    {
        if (!UnicodeScalarBrowser.TryCreateEntry(codePoint, out var entry) || entry is null || !entry.CanInsert ||
            !Rune.TryCreate(codePoint, out var rune))
            return false;
        return _insertText(rune.ToString());
    }

    public bool BrowseUnicodeRange(string? stableId)
    {
        if (!UnicodeScalarBrowser.TryFindQuickRange(stableId, out var range) || range is null)
            return false;
        var pageIndex = UnicodeScalarBrowser.GetPageIndexForCodePoint(range.StartCodePoint);
        NavigateToPage(pageIndex);
        return true;
    }

    public bool SetUnicodePageIndex(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= UnicodeScalarBrowser.PageCount)
            return false;
        NavigateToPage(pageIndex);
        return true;
    }

    internal void RefreshSelectedGlyphCoverage()
    {
        UpdatePaletteGlyphCoverage(_selectedPaletteEntry);
        UpdateEmojiGlyphCoverage(_selectedEmojiEntry);
        UpdateUnicodeGlyphCoverage(_selectedUnicodeEntry);
    }

    private void RefreshPaletteEntries()
    {
        _entries = FilterPaletteEntries();
        OnPropertyChanged(nameof(Entries));
        if (_selectedPaletteEntry is not null && !_entries.Any(entry => entry.Id == _selectedPaletteEntry.Id))
            SetSelectedPaletteEntry(null);
    }

    private ImmutableArray<MathPaletteEntry> FilterPaletteEntries()
    {
        var categoryEntries = MathSymbolCatalog.ForCategory(_selectedCategory);
        var query = _searchText.Trim();
        return string.IsNullOrEmpty(query)
            ? categoryEntries
            : categoryEntries.Where(entry => Matches(query, entry.NameKo, entry.NameEn,
                entry.PreviewText, entry.Text, entry.CodePointLabel, entry.CodePoints, entry.Aliases))
                .ToImmutableArray();
    }

    private void RefreshEmojiEntries()
    {
        var query = _emojiSearchText.Trim();
        _emojiEntries = string.IsNullOrEmpty(query)
            ? CuratedEmojiCatalog.Entries
            : CuratedEmojiCatalog.Entries.Where(entry => Matches(query, entry.NameKo, entry.NameEn,
                entry.PreviewText, entry.Text, entry.CodePointLabel, entry.CodePoints, entry.Aliases))
                .ToImmutableArray();
        OnPropertyChanged(nameof(EmojiEntries));
        if (_selectedEmojiEntry is not null && !_emojiEntries.Any(entry => entry.Id == _selectedEmojiEntry.Id))
            SetSelectedEmojiEntry(null);
    }

    private void LookupUnicode()
    {
        var lookup = UnicodeScalarBrowser.Lookup(_unicodeQuery);
        _unicodeLookupResult = lookup.Status == UnicodeLookupStatus.Found ? lookup : null;
        _unicodeLookupError = lookup.Diagnostic ?? string.Empty;
        OnPropertyChanged(nameof(VisibleUnicodeEntries));
        OnPropertyChanged(nameof(UnicodeLookupResult));
        OnPropertyChanged(nameof(UnicodeLookupError));
        SetSelectedUnicodeEntry(lookup.Entry);
    }

    private void ClearUnicodeLookup()
    {
        _unicodeLookupResult = null;
        _unicodeLookupError = string.Empty;
        OnPropertyChanged(nameof(VisibleUnicodeEntries));
        OnPropertyChanged(nameof(UnicodeLookupResult));
        OnPropertyChanged(nameof(UnicodeLookupError));
        SetSelectedUnicodeEntry(null);
    }

    private void NavigateToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= UnicodeScalarBrowser.PageCount)
            return;

        _unicodePageIndex = pageIndex;
        _unicodeEntries = UnicodeScalarBrowser.GetPage(pageIndex).Entries;
        if (_unicodeQuery.Length > 0)
        {
            _unicodeQuery = string.Empty;
            OnPropertyChanged(nameof(UnicodeQuery));
        }
        _unicodeLookupResult = null;
        _unicodeLookupError = string.Empty;
        OnPropertyChanged(nameof(UnicodePageIndex));
        OnPropertyChanged(nameof(UnicodeEntries));
        OnPropertyChanged(nameof(VisibleUnicodeEntries));
        OnPropertyChanged(nameof(UnicodeLookupResult));
        OnPropertyChanged(nameof(UnicodeLookupError));
        SetSelectedUnicodeEntry(null);
        _previousUnicodePageCommand.NotifyCanExecuteChanged();
        _nextUnicodePageCommand.NotifyCanExecuteChanged();
    }

    private void SetSelectedPaletteEntry(MathPaletteEntry? entry)
    {
        if (_selectedPaletteEntry?.Id == entry?.Id)
            return;
        _selectedPaletteEntry = entry;
        OnPropertyChanged(nameof(SelectedPaletteEntry));
        UpdatePaletteGlyphCoverage(entry);
    }

    private void SetSelectedEmojiEntry(CuratedEmojiEntry? entry)
    {
        if (_selectedEmojiEntry?.Id == entry?.Id)
            return;
        _selectedEmojiEntry = entry;
        OnPropertyChanged(nameof(SelectedEmojiEntry));
        UpdateEmojiGlyphCoverage(entry);
    }

    private void SetSelectedUnicodeEntry(UnicodeScalarEntry? entry)
    {
        if (_selectedUnicodeEntry?.CodePoint == entry?.CodePoint)
            return;
        _selectedUnicodeEntry = entry;
        OnPropertyChanged(nameof(SelectedUnicodeEntry));
        UpdateUnicodeGlyphCoverage(entry);
    }

    private void UpdatePaletteGlyphCoverage(MathPaletteEntry? entry)
    {
        _selectedPaletteGlyphCoverage = entry is null
            ? null
            : entry.Text is string text
                ? Probe(text)
                : UnknownGlyphCoverage("구조 템플릿은 단일 글리프가 아닙니다.");
        OnPropertyChanged(nameof(SelectedPaletteGlyphCoverage));
    }

    private void UpdateEmojiGlyphCoverage(CuratedEmojiEntry? entry)
    {
        _selectedEmojiGlyphCoverage = entry is null ? null : Probe(entry.Text);
        OnPropertyChanged(nameof(SelectedEmojiGlyphCoverage));
    }

    private void UpdateUnicodeGlyphCoverage(UnicodeScalarEntry? entry)
    {
        _selectedUnicodeGlyphCoverage = entry is null || !entry.CanInsert
            ? null
            : Probe(char.ConvertFromUtf32(entry.CodePoint));
        OnPropertyChanged(nameof(SelectedUnicodeGlyphCoverage));
    }

    private MathGlyphCoverageResult Probe(string text)
    {
        string fontFamily;
        try
        {
            fontFamily = _getPrimaryFontFamily();
        }
        catch (Exception exception)
        {
            return UnknownGlyphCoverage($"기본 수학 글꼴 정보를 확인할 수 없습니다: {exception.Message}");
        }

        try
        {
            return _glyphCoverageProbe.Probe(text, fontFamily);
        }
        catch (Exception exception)
        {
            return new MathGlyphCoverageResult(MathGlyphCoverageStatus.Unknown, fontFamily,
                $"글리프 정보를 확인할 수 없습니다: {exception.Message}");
        }
    }

    private MathGlyphCoverageResult UnknownGlyphCoverage(string reason)
    {
        string fontFamily;
        try
        {
            fontFamily = _getPrimaryFontFamily();
        }
        catch
        {
            fontFamily = string.Empty;
        }
        return new MathGlyphCoverageResult(MathGlyphCoverageStatus.Unknown, fontFamily, reason);
    }

    private bool CanInsertUnicodeScalarParameter(object? parameter) =>
        parameter is int codePoint && UnicodeScalarBrowser.TryCreateEntry(codePoint, out var entry) &&
        entry is { CanInsert: true };

    private static bool Matches(string query, string nameKo, string nameEn, string preview,
        string? text, string codePointLabel, string codePoints, ImmutableArray<string> aliases)
    {
        static bool Contains(string value, string query) =>
            value.Contains(query, StringComparison.OrdinalIgnoreCase);

        return Contains(nameKo, query) || Contains(nameEn, query) || Contains(preview, query) ||
               (text is not null && Contains(text, query)) || Contains(codePointLabel, query) ||
               Contains(codePoints, query) || aliases.Any(alias => Contains(alias, query));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class ParameterizedCommand(
        Action<object?> execute,
        Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
                execute(parameter);
        }

        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
