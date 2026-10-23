using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using AetherMath.Editing;

namespace AetherMath.Desktop;

/// <summary>하나의 읽기 전용 선택 표면에서 LaTeX 원문과 토큰 강조를 표시합니다.</summary>
public sealed partial class LatexCodeSurface : UserControl
{
    public static readonly StyledProperty<LatexSyntaxSnapshot?> SyntaxProperty =
        AvaloniaProperty.Register<LatexCodeSurface, LatexSyntaxSnapshot?>(nameof(Syntax));

    public static readonly StyledProperty<bool> IsHighlightingEnabledProperty =
        AvaloniaProperty.Register<LatexCodeSurface, bool>(nameof(IsHighlightingEnabled), true);

    private string? _renderedSource;
    private LatexSyntaxSnapshot? _renderedSnapshot;
    private Run[] _renderedRuns = Array.Empty<Run>();

    private static readonly IBrush ControlWordBrush = new SolidColorBrush(Color.Parse("#075FA8"));
    private static readonly IBrush ControlSymbolBrush = new SolidColorBrush(Color.Parse("#7352A1"));
    private static readonly IBrush BraceBrush = new SolidColorBrush(Color.Parse("#9A5200"));
    private static readonly IBrush AlignmentBrush = new SolidColorBrush(Color.Parse("#A43B62"));
    private static readonly IBrush ScriptBrush = new SolidColorBrush(Color.Parse("#604B9A"));
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.Parse("#16764D"));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#718096"));
    private static readonly IBrush FallbackTextBrush = new SolidColorBrush(Color.Parse("#21496F"));

    public LatexCodeSurface()
    {
        InitializeComponent();
        RebuildSurface();
    }

    public LatexSyntaxSnapshot? Syntax
    {
        get => GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    public bool IsHighlightingEnabled
    {
        get => GetValue(IsHighlightingEnabledProperty);
        set => SetValue(IsHighlightingEnabledProperty, value);
    }

    /// <summary>원문은 스냅샷에서만 읽으며 Inlines의 Text 값에 의존하지 않습니다.</summary>
    public string SourceText => Syntax?.Source ?? string.Empty;

    public string SelectedText => InnerTextSurface.SelectedText ?? string.Empty;

    public int SelectionStart
    {
        get => InnerTextSurface.SelectionStart;
        set => InnerTextSurface.SelectionStart = value;
    }

    public int SelectionEnd
    {
        get => InnerTextSurface.SelectionEnd;
        set => InnerTextSurface.SelectionEnd = value;
    }

    public Vector ScrollOffset
    {
        get => InnerScrollViewer.Offset;
        set => InnerScrollViewer.Offset = value;
    }

    public ScrollViewer CodeScrollViewer => InnerScrollViewer;

    public SelectableTextBlock TextSurface => InnerTextSurface;

    public bool IsReadOnly => true;

    public bool HasCompleteTokenCoverage => Syntax is not null && HasCompleteCoverage(Syntax);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SyntaxProperty || change.Property == IsHighlightingEnabledProperty)
            RebuildSurface();
    }

    private void RebuildSurface()
    {
        if (InnerTextSurface is null || InnerScrollViewer is null)
            return;

        var source = SourceText;
        var sourceChanged = !string.Equals(_renderedSource, source, StringComparison.Ordinal);
        var snapshot = Syntax;
        var snapshotChanged = !ReferenceEquals(_renderedSnapshot, snapshot);

        if (sourceChanged || snapshotChanged)
        {
            var selectionStart = sourceChanged ? 0 : InnerTextSurface.SelectionStart;
            var selectionEnd = sourceChanged ? 0 : InnerTextSurface.SelectionEnd;
            var previousOffset = InnerScrollViewer.Offset;
            var inlines = InnerTextSurface.Inlines;
            if (inlines is null)
            {
                inlines = new InlineCollection();
                InnerTextSurface.Inlines = inlines;
            }

            inlines.Clear();
            if (source.Length > 0)
            {
                if (snapshot is not null && snapshot.Source == source && HasCompleteCoverage(snapshot))
                {
                    _renderedRuns = new Run[snapshot.Tokens.Length];
                    for (var index = 0; index < snapshot.Tokens.Length; index++)
                    {
                        var token = snapshot.Tokens[index];
                        var run = new Run(source.Substring(token.Start, token.Length));
                        _renderedRuns[index] = run;
                        inlines.Add(run);
                    }
                }
                else
                {
                    var run = new Run(source);
                    _renderedRuns = new[] { run };
                    inlines.Add(run);
                }
            }
            else
            {
                _renderedRuns = Array.Empty<Run>();
            }

            if (sourceChanged)
            {
                InnerTextSurface.ClearSelection();
                InnerTextSurface.SelectionStart = 0;
                InnerTextSurface.SelectionEnd = 0;
                InnerScrollViewer.Offset = Vector.Zero;
            }
            else
            {
                InnerTextSurface.SelectionStart = Math.Clamp(selectionStart, 0, source.Length);
                InnerTextSurface.SelectionEnd = Math.Clamp(selectionEnd, 0, source.Length);
                InnerScrollViewer.Offset = previousOffset;
            }

            _renderedSource = source;
            _renderedSnapshot = snapshot;
        }

        ApplyTokenColors(snapshot, source);
    }

    private void ApplyTokenColors(LatexSyntaxSnapshot? snapshot, string source)
    {
        var defaultBrush = InnerTextSurface.Foreground ?? FallbackTextBrush;
        if (snapshot is not null && ReferenceEquals(_renderedSnapshot, snapshot) &&
            snapshot.Source == source && HasCompleteCoverage(snapshot) &&
            _renderedRuns.Length == snapshot.Tokens.Length)
        {
            for (var index = 0; index < _renderedRuns.Length; index++)
                _renderedRuns[index].Foreground = IsHighlightingEnabled
                    ? GetTokenBrush(snapshot.Tokens[index].Kind) ?? defaultBrush
                    : defaultBrush;
            return;
        }

        foreach (var run in _renderedRuns)
            run.Foreground = defaultBrush;
    }

    private static bool HasCompleteCoverage(LatexSyntaxSnapshot snapshot)
    {
        if (snapshot.Source.Length == 0)
            return snapshot.Tokens.IsEmpty;

        var nextStart = 0;
        foreach (var token in snapshot.Tokens)
        {
            if (token.Start != nextStart || token.Length <= 0 ||
                (long)token.Start + token.Length > snapshot.Source.Length)
                return false;
            nextStart += token.Length;
        }

        return nextStart == snapshot.Source.Length;
    }

    private static IBrush? GetTokenBrush(LatexTokenKind kind) => kind switch
    {
        LatexTokenKind.ControlWord => ControlWordBrush,
        LatexTokenKind.ControlSymbol => ControlSymbolBrush,
        LatexTokenKind.Brace => BraceBrush,
        LatexTokenKind.Alignment => AlignmentBrush,
        LatexTokenKind.Script => ScriptBrush,
        LatexTokenKind.Number => NumberBrush,
        LatexTokenKind.Comment => CommentBrush,
        LatexTokenKind.Text or LatexTokenKind.Whitespace => null,
        _ => null
    };
}
