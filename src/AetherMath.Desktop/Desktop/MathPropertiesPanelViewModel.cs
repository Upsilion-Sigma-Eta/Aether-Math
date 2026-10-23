using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Layout;

namespace AetherMath.Desktop;

public sealed record MathPropertyScopeChoice(MathPropertyScope Scope, string DisplayName, string Description);
public sealed record MathTypesettingStyleChoice(MathTypesettingStyle? Value, string DisplayName, string Description);
public sealed record MathTextRoleChoice(MathTextRole? Value, string DisplayName, string Description);
public sealed record MathBracketKindChoice(MathBracketKind? Value, string DisplayName, string Description);

public sealed class MathPropertiesPanelViewModel : INotifyPropertyChanged
{
    private static readonly ImmutableArray<MathPropertyScopeChoice> AvailableScopes = ImmutableArray.Create(
        new MathPropertyScopeChoice(MathPropertyScope.SelectionNode, "선택 노드", "전체 구조 선택은 해당 구조와 하위 노드에 적용합니다. 텍스트 일부 선택은 선택 문자가 포함된 텍스트 전체에 적용합니다."),
        new MathPropertyScopeChoice(MathPropertyScope.CurrentRow, "현재 행", "활성 논리 행의 루트와 하위 노드에 적용합니다."),
        new MathPropertyScopeChoice(MathPropertyScope.CurrentGroup, "현재 그룹", "활성 그룹의 행과 노드에 적용합니다."),
        new MathPropertyScopeChoice(MathPropertyScope.WorkspaceDefault, "작업 기본값", "작업 전체의 상속 기본값을 바꿉니다."));
    private static readonly ImmutableArray<MathTypesettingStyleChoice> AvailableTypesettingStyles = ImmutableArray.Create(
        new MathTypesettingStyleChoice(null, "변경 안 함", "이번 적용에서는 조판 기준을 바꾸지 않습니다."),
        new MathTypesettingStyleChoice(MathTypesettingStyle.Display, "독립 수식 (Display)", "기본 수식 크기를 사용하고 합·극한의 한계를 위아래로 배치합니다."),
        new MathTypesettingStyleChoice(MathTypesettingStyle.Text, "텍스트 크기 (Text)", "텍스트 크기 조판 기준입니다. 수식 구조에 따른 자식 전이는 계속 적용됩니다."),
        new MathTypesettingStyleChoice(MathTypesettingStyle.Script, "첨자 (Script)", "첫 번째 첨자 수준 크기를 기준으로 조판합니다."),
        new MathTypesettingStyleChoice(MathTypesettingStyle.ScriptScript, "작은 첨자 (ScriptScript)", "더 깊은 첨자 수준 크기를 기준으로 조판합니다."));
    private static readonly ImmutableArray<MathTextRoleChoice> AvailableTextRoles = ImmutableArray.Create(
        new MathTextRoleChoice(null, "변경 안 함", "이번 적용에서는 텍스트 역할을 바꾸지 않습니다."),
        new MathTextRoleChoice(MathTextRole.Automatic, "자동", "기존 문자별 기울임·정렬·LaTeX 동작을 유지합니다."),
        new MathTextRoleChoice(MathTextRole.Identifier, "식별자", "수학 변수처럼 기울여 표시합니다."),
        new MathTextRoleChoice(MathTextRole.MathSymbol, "수학 기호", "수학 기호처럼 세워서 표시하고 등호 정렬에 사용할 수 있습니다."),
        new MathTextRoleChoice(MathTextRole.Number, "숫자", "숫자 표기로 세워서 표시합니다."),
        new MathTextRoleChoice(MathTextRole.PlainText, "일반 텍스트", "수식 기호가 아닌 문구로 표시합니다."),
        new MathTextRoleChoice(MathTextRole.Unit, "단위", "수식 단위 표기로 세워서 표시합니다."),
        new MathTextRoleChoice(MathTextRole.Condition, "조건", "조건 문구로 표시합니다."));

    private static readonly ImmutableArray<MathBracketKindChoice> AvailableBracketKinds = ImmutableArray.Create(
        new MathBracketKindChoice(null, "변경 안 함", "이번 적용에서는 괄호 종류를 바꾸지 않습니다."),
        new MathBracketKindChoice(MathBracketKind.Parentheses, "소괄호 ( )", "둥근 소괄호로 표시합니다."),
        new MathBracketKindChoice(MathBracketKind.Square, "대괄호 [ ]", "각진 대괄호로 표시합니다."),
        new MathBracketKindChoice(MathBracketKind.Curly, "중괄호 { }", "중괄호로 표시합니다."),
        new MathBracketKindChoice(MathBracketKind.Angle, "꺾쇠 ⟨ ⟩", "꺾쇠 괄호로 표시합니다."));

    private readonly IMathTextMetrics? _textMetrics;
    private readonly IMathGlyphCoverageProbe? _glyphProbe;
    private readonly RelayCommand _applyCommand;
    private readonly RelayCommand _resetDraftCommand;
    private readonly RelayCommand _resetOverrideCommand;
    private EditingSession _session;
    private MathPropertyScope _selectedScope = MathPropertyScope.SelectionNode;
    private MathPropertyTargetSnapshot? _targetSnapshot;
    private ResolvedMathStyle? _resolved;
    private bool _isDraftStale;
    private string _staleReason = string.Empty;
    private string _draftFontSizeText = string.Empty;
    private string _draftFontFamily = string.Empty;
    private string _draftForeground = string.Empty;
    private MathTypesettingStyle? _draftTypesettingStyle;
    private bool _draftPinTypesettingStyle;
    private MathTextRole? _draftTextRole;
    private MathBracketKind? _draftBracketKind;
    private MathBracketKind? _baselineBracketKind;
    private string _draftGroupRowSpacingText = string.Empty;
    private double? _baselineGroupRowSpacing;
    private string _baselineFontSizeText = string.Empty;
    private string _baselineFontFamily = string.Empty;
    private string _baselineForeground = string.Empty;
    private MathTypesettingStyle _baselineTypesettingStyle = MathTypesettingStyle.Display;
    private string _fontSizeValidationError = string.Empty;
    private string _fontFamilyValidationError = string.Empty;
    private string _foregroundValidationError = string.Empty;
    private string _groupRowSpacingValidationError = string.Empty;
    private string _fontMeasurementDiagnostic = string.Empty;
    private bool _fontMeasurementFailed;
    private bool _updatingDraft;

    public MathPropertiesPanelViewModel(EditingSession session, IMathTextMetrics? textMetrics = null,
        IMathGlyphCoverageProbe? glyphProbe = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _textMetrics = textMetrics;
        _glyphProbe = glyphProbe;
        _applyCommand = new RelayCommand(Apply, () => CanApply);
        _resetDraftCommand = new RelayCommand(ResetDraft, CanResetDraft);
        _resetOverrideCommand = new RelayCommand(ResetOverride, () => CanResetOverride && !_isDraftStale);
        _session.PropertyChanged += OnSessionPropertyChanged;
        CaptureBaseline();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImmutableArray<MathPropertyScopeChoice> ScopeChoices => AvailableScopes;
    public ImmutableArray<MathTypesettingStyleChoice> TypesettingStyleChoices => AvailableTypesettingStyles;
    public ImmutableArray<MathTextRoleChoice> TextRoleChoices => AvailableTextRoles;
    public ImmutableArray<MathBracketKindChoice> BracketKindChoices => AvailableBracketKinds;
    public ICommand ApplyCommand => _applyCommand;
    public ICommand ResetDraftCommand => _resetDraftCommand;
    public ICommand ResetOverrideCommand => _resetOverrideCommand;

    public MathPropertyScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!Enum.IsDefined(typeof(MathPropertyScope), value) || _selectedScope == value)
                return;
            _selectedScope = value;
            OnPropertyChanged(nameof(SelectedScope));
            RefreshTargetState();
        }
    }

    public string TargetSummary { get; private set; } = string.Empty;
    public string SelectionNotice { get; private set; } = string.Empty;
    public bool IsDraftStale => _isDraftStale;
    public string StaleReason => _staleReason;
    public bool CanApply => !_isDraftStale && _targetSnapshot is not null && HasDraftChanges &&
        (_draftTextRole is null || CanEditTextRole) &&
        (!HasBracketKindDraftChange || CanEditBracketKind) &&
        _fontSizeValidationError.Length == 0 && _fontFamilyValidationError.Length == 0 &&
        _foregroundValidationError.Length == 0 && _groupRowSpacingValidationError.Length == 0 &&
        !_fontMeasurementFailed && BuildPatch() is not null;
    public bool CanResetOverride => _selectedScope != MathPropertyScope.WorkspaceDefault &&
        _resolved?.TargetOverride is not null;
    public string ResetOverrideDisabledReason => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? "작업 기본값은 상속 초기화 대상이 아닙니다."
        : CanResetOverride ? string.Empty : "이 범위에 초기화할 명시 스타일이 없습니다.";

    public double EffectiveFontSize => _resolved?.FontSize.Value ?? _session.Workspace.Style.FontSize;
    public double? ExplicitFontSize => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? _session.Workspace.Style.FontSize
        : _resolved?.TargetOverride?.FontSize;
    public string FontSizeSourceLabel => SourceLabel(_resolved?.FontSize.Origin);
    public string DraftFontSizeText
    {
        get => _draftFontSizeText;
        set => SetDraft(ref _draftFontSizeText, value ?? string.Empty, nameof(DraftFontSizeText));
    }
    public string FontSizeValidationError => _fontSizeValidationError;
    public string FontSizeLatexDiagnostic => "LaTeX 출력은 글자 크기 차이를 표현하지 않습니다.";

    public string EffectiveFontFamily => _resolved?.FontFamily.Value ?? _session.Workspace.Style.FontFamily;
    public string? ExplicitFontFamily => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? _session.Workspace.Style.FontFamily
        : _resolved?.TargetOverride?.FontFamily;
    public string FontFamilySourceLabel => SourceLabel(_resolved?.FontFamily.Origin);
    public string DraftFontFamily
    {
        get => _draftFontFamily;
        set => SetDraft(ref _draftFontFamily, value ?? string.Empty, nameof(DraftFontFamily));
    }
    public string FontFamilyValidationError => _fontFamilyValidationError;
    public string FontFamilyLatexDiagnostic => "LaTeX 출력은 수학 글꼴 차이를 표현하지 않습니다.";
    public string FontMeasurementDiagnostic => _fontMeasurementDiagnostic;

    public string EffectiveForeground => _resolved?.Foreground.Value ?? _session.Workspace.Style.Foreground;
    public string? ExplicitForeground => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? _session.Workspace.Style.Foreground
        : _resolved?.TargetOverride?.Foreground;
    public string ForegroundSourceLabel => SourceLabel(_resolved?.Foreground.Origin);
    public string DraftForeground
    {
        get => _draftForeground;
        set => SetDraft(ref _draftForeground, value ?? string.Empty, nameof(DraftForeground));
    }
    public string ForegroundValidationError => _foregroundValidationError;
    public string ForegroundLatexDiagnostic => "LaTeX 출력은 수식 색상 차이를 표현하지 않습니다.";

    public string DraftGroupRowSpacingText
    {
        get => _draftGroupRowSpacingText;
        set => SetDraft(ref _draftGroupRowSpacingText, value ?? string.Empty, nameof(DraftGroupRowSpacingText));
    }
    public bool CanEditGroupRowSpacing => _selectedScope == MathPropertyScope.CurrentGroup &&
        TryGetCurrentGroupRowSpacing(out _);
    public double? CurrentGroupRowSpacing => TryGetCurrentGroupRowSpacing(out var value) ? value : null;
    public string GroupRowSpacingValidationError => _groupRowSpacingValidationError;
    public string GroupRowSpacingDisabledReason => CanEditGroupRowSpacing
        ? string.Empty
        : _selectedScope != MathPropertyScope.CurrentGroup
            ? "그룹 내부 행 간격은 현재 그룹 범위에서만 편집할 수 있습니다."
            : "현재 그룹의 행 간격을 확인할 수 없습니다.";
    public string GroupRowSpacingNotice
    {
        get
        {
            if (_isDraftStale)
                return "초안이 오래되어 현재 그룹에 적용할 수 없습니다.";
            if (!CanEditGroupRowSpacing || !TryGetCurrentGroup(out var group))
                return string.Empty;
            return group.Rows.Length <= 1
                ? "현재 그룹은 행이 하나여서 화면상 변화가 없습니다. 다음 행을 추가하면 이 값이 적용됩니다."
                : "앞 행 수식과 다음 행 수식 사이에 더하는 간격이며 그룹 사이 간격과는 별개입니다.";
        }
    }
    public string GroupRowSpacingLatexDiagnostic => !_isDraftStale && CanEditGroupRowSpacing &&
        TryGetCurrentGroup(out var group) && group.Rows.Length > 1
            ? "LaTeX 코드에는 현재 그룹의 행 간격이 반영되지 않습니다."
            : string.Empty;

    public MathTypesettingStyle EffectiveTypesettingStyle =>
        _resolved?.TypesettingStyle.Value ?? _session.Workspace.Style.TypesettingStyle;
    public MathTypesettingStyle? ExplicitTypesettingStyle => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? _session.Workspace.Style.TypesettingStyle
        : _resolved?.TargetOverride?.TypesettingStyle;
    public string TypesettingStyleSourceLabel => SourceLabel(_resolved?.TypesettingStyle.Origin);
    public MathTypesettingStyle? DraftTypesettingStyle
    {
        get => _draftTypesettingStyle;
        set
        {
            if (value is MathTypesettingStyle style && !Enum.IsDefined(typeof(MathTypesettingStyle), style))
                return;
            if (_draftTypesettingStyle == value)
                return;
            _draftTypesettingStyle = value;
            OnPropertyChanged(nameof(DraftTypesettingStyle));
            OnPropertyChanged(nameof(SelectedTypesettingStyleChoice));
            OnPropertyChanged(nameof(FractionDisplayNotice));
            NotifyState();
        }
    }
    public bool DraftPinTypesettingStyle
    {
        get => _draftPinTypesettingStyle;
        set
        {
            var next = value && CanPinTypesettingStyle;
            if (_draftPinTypesettingStyle == next)
                return;
            _draftPinTypesettingStyle = next;
            OnPropertyChanged(nameof(DraftPinTypesettingStyle));
            OnPropertyChanged(nameof(FractionDisplayNotice));
            NotifyState();
        }
    }
    public bool CanPinTypesettingStyle => _selectedScope != MathPropertyScope.WorkspaceDefault &&
        _targetSnapshot is not null && _resolved is not null &&
        _resolved.TargetOverride?.TypesettingStyle is null;
    public string TypesettingPinDisabledReason => _selectedScope == MathPropertyScope.WorkspaceDefault
        ? "작업 기본값은 상속되는 기준이므로 명시 고정 대상이 아닙니다."
        : _targetSnapshot is null || _resolved is null
            ? "속성 적용 대상을 확인할 수 없습니다."
            : CanPinTypesettingStyle ? string.Empty : "이 범위에는 이미 명시된 조판 기준이 있습니다.";
    public string TypesettingPinNotice =>
        "현재 조판 스타일을 이 범위의 명시값으로 저장합니다. 현재 상속값도 이 범위에 직접 지정할 수 있습니다.";
    public bool IsFractionSelection => !_isDraftStale && _selectedScope == MathPropertyScope.SelectionNode &&
        _targetSnapshot?.TargetNodeId is Guid nodeId &&
        MathTree.TryFindNode(_session.Workspace, nodeId, out var node, out _) && node is FractionNode;
    public string FractionDisplayNotice
    {
        get
        {
            if (!IsFractionSelection)
                return string.Empty;
            MathTypesettingStyle? resultingLocalStyle = ExplicitTypesettingStyle;
            if (_draftTypesettingStyle is MathTypesettingStyle selectedStyle &&
                selectedStyle != _baselineTypesettingStyle)
                resultingLocalStyle = selectedStyle;
            else if (DraftPinTypesettingStyle && CanPinTypesettingStyle)
                resultingLocalStyle = _draftTypesettingStyle ?? _baselineTypesettingStyle;

            if (resultingLocalStyle == MathTypesettingStyle.Display)
                return ExplicitTypesettingStyle == MathTypesettingStyle.Display
                    ? "이 분수: \\dfrac (Display 명시 적용, amsmath 필요)."
                    : "적용 후 이 분수: \\dfrac (Display 명시 적용, amsmath 필요).";
            if (resultingLocalStyle is not null)
                return "이 조판 스타일은 \\frac로 출력합니다. 이 분수에 Display를 명시 적용하면 \\dfrac가 됩니다 (amsmath 필요).";
            return "이 분수는 상속값을 사용해 \\frac로 출력합니다. Display를 명시 적용하면 \\dfrac가 됩니다 (amsmath 필요).";
        }
    }
    public MathTypesettingStyleChoice? SelectedTypesettingStyleChoice
    {
        get => AvailableTypesettingStyles.FirstOrDefault(choice => choice.Value == _draftTypesettingStyle);
        set
        {
            if (value is null)
                return;
            DraftTypesettingStyle = value.Value;
        }
    }
    public MathTextRole CurrentTextRole => TryGetRoleTarget(out var textNode) ? textNode!.Role : MathTextRole.Automatic;
    public bool CanEditTextRole => _selectedScope == MathPropertyScope.SelectionNode && TryGetRoleTarget(out _);
    public string TextRoleDisabledReason => CanEditTextRole
        ? string.Empty
        : _selectedScope != MathPropertyScope.SelectionNode
            ? "텍스트 역할은 선택 노드 범위에서만 편집할 수 있습니다."
            : "현재 대상이 텍스트 노드가 아니므로 역할을 편집할 수 없습니다.";
    public string TextRoleScopeNotice => _selectedScope != MathPropertyScope.SelectionNode
        ? "텍스트 역할은 선택 노드 범위에서만 변경할 수 있습니다."
        : CanEditTextRole && _session.IsNodeSelected
            ? "선택한 텍스트 노드 전체의 역할을 변경합니다."
            : _session.IsNodeSelected
            ? "전체 구조 선택은 텍스트 역할을 변경하지 않습니다. 텍스트 노드를 선택하세요."
            : _session.Selection is not null
                ? "일부 문자를 선택했습니다. 선택 문자가 포함된 텍스트 전체의 역할을 변경합니다."
                : "커서가 있는 텍스트 전체의 역할을 변경합니다.";
    public MathTextRole? DraftTextRole
    {
        get => _draftTextRole;
        set
        {
            if (value is MathTextRole role && !Enum.IsDefined(typeof(MathTextRole), role))
                return;
            if (value is not null && !CanEditTextRole)
                return;
            var nextValue = value is MathTextRole requestedRole && requestedRole == CurrentTextRole
                ? null
                : value;
            if (_draftTextRole == nextValue)
                return;
            _draftTextRole = nextValue;
            OnPropertyChanged(nameof(DraftTextRole));
            OnPropertyChanged(nameof(SelectedTextRoleChoice));
            NotifyState();
        }
    }
    public MathTextRoleChoice? SelectedTextRoleChoice
    {
        get => AvailableTextRoles.FirstOrDefault(choice => choice.Value == _draftTextRole);
        set
        {
            if (value is null)
                return;
            DraftTextRole = value.Value;
        }
    }
    public MathBracketKind? CurrentBracketKind => TryGetCurrentSelectedBrackets(out var brackets)
        ? brackets!.Kind
        : null;
    public string CurrentBracketKindLabel => CurrentBracketKind is MathBracketKind kind
        ? BracketName(kind)
        : string.Empty;
    public MathBracketKind? DraftBracketKind
    {
        get => _draftBracketKind;
        set
        {
            if (value is MathBracketKind kind && !Enum.IsDefined(typeof(MathBracketKind), kind))
                return;
            if (_draftBracketKind == value)
                return;
            _draftBracketKind = value;
            OnPropertyChanged(nameof(DraftBracketKind));
            OnPropertyChanged(nameof(SelectedBracketKindChoice));
            OnPropertyChanged(nameof(BracketKindNotice));
            NotifyState();
        }
    }
    public MathBracketKindChoice? SelectedBracketKindChoice
    {
        get => AvailableBracketKinds.FirstOrDefault(choice => choice.Value == _draftBracketKind);
        set
        {
            if (value is null)
                return;
            DraftBracketKind = value.Value;
        }
    }
    public bool CanEditBracketKind => TryGetCurrentSelectedBrackets(out _);
    public string BracketKindDisabledReason => _isDraftStale
        ? "초안 대상이 바뀌었습니다. 초안을 되돌린 뒤 괄호를 다시 선택하세요."
        : CanEditBracketKind
            ? string.Empty
            : _selectedScope != MathPropertyScope.SelectionNode
                ? "괄호 종류는 선택 노드 범위에서만 변경할 수 있습니다."
                : _targetSnapshot is null
                    ? "괄호 적용 대상을 확인할 수 없습니다."
                    : !_targetSnapshot.IsNodeSelected || _targetSnapshot.SelectedNodeId != _targetSnapshot.TargetNodeId
                        ? "괄호 전체를 선택해야 종류를 바꿀 수 있습니다."
                        : "현재 전체 선택 대상이 괄호가 아닙니다.";
    public string BracketKindNotice => _isDraftStale
        ? "초안이 오래되어 현재 괄호에 적용할 수 없습니다."
        : CanEditBracketKind
            ? "괄호 모양만 바꾸며 내용과 하위 구조는 유지됩니다."
            : "전체 선택한 괄호의 종류를 변경합니다.";
    public string TypesettingStyleContextNotice =>
        "이 값은 해당 범위의 조판 기준입니다. 분수·첨자·행렬·경우 나누기의 자식은 구조에 따라 자동으로 작은 조판 단계로 전이됩니다.";
    public string TypesettingStyleLatexDiagnostic =>
        "LaTeX 출력에는 조판 기준을 반영하는 스타일 명령이 포함됩니다.";

    public void SetSession(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ReferenceEquals(_session, session))
            return;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;
        RefreshTargetState();
    }

    private bool HasDraftChanges =>
        !string.Equals(_draftFontSizeText, _baselineFontSizeText, StringComparison.Ordinal) ||
        !string.Equals(_draftFontFamily, _baselineFontFamily, StringComparison.Ordinal) ||
        !string.Equals(_draftForeground, _baselineForeground, StringComparison.Ordinal) ||
        (_draftTypesettingStyle is MathTypesettingStyle draftStyle && draftStyle != _baselineTypesettingStyle) ||
        _draftPinTypesettingStyle ||
        _draftTextRole is not null ||
        HasBracketKindDraftChange ||
        HasGroupRowSpacingDraftChange;

    private bool HasBracketKindDraftChange =>
        _draftBracketKind is MathBracketKind kind && kind != _baselineBracketKind;

    private bool HasGroupRowSpacingDraftChange
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_draftGroupRowSpacingText))
                return false;
            if (double.TryParse(_draftGroupRowSpacingText, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var parsed) && double.IsFinite(parsed) &&
                _baselineGroupRowSpacing is double baseline && parsed.Equals(baseline))
                return false;
            return true;
        }
    }

    private bool CanResetDraft() => _targetSnapshot is not null;

    private void SetDraft(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;
        field = value;
        OnPropertyChanged(propertyName);
        if (!_updatingDraft)
        {
            ValidateDraft();
            UpdateFontMeasurementDiagnostic();
            NotifyState();
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditingSession.Workspace) or nameof(EditingSession.Revision) or
            nameof(EditingSession.Caret) or nameof(EditingSession.Selection) or
            nameof(EditingSession.SelectedNodeId) or nameof(EditingSession.IsNodeSelected))
            RefreshTargetState();
    }

    private void RefreshTargetState()
    {
        UpdateSelectionNotice();
        if (!_session.TryCaptureMathPropertyTarget(_selectedScope, out var current) || current is null)
        {
            _resolved = null;
            TargetSummary = "속성 적용 대상을 확인할 수 없습니다.";
            OnPropertyChanged(nameof(TargetSummary));
            if (!HasDraftChanges)
            {
                _targetSnapshot = null;
                SetStale(false, string.Empty);
                SetDefaultDraft();
            }
            else
            {
                SetStale("현재 선택에서 속성을 적용할 대상을 찾을 수 없습니다.");
            }
            NotifyResolvedProperties();
            ValidateDraft();
            UpdateFontMeasurementDiagnostic();
            NotifyState();
            return;
        }

        _resolved = _session.ResolveMathStyle(current);
        TargetSummary = DescribeTarget(current);
        OnPropertyChanged(nameof(TargetSummary));
        if (!HasDraftChanges)
        {
            _targetSnapshot = current;
            SetStale(false, string.Empty);
            LoadDraftFromResolved();
        }
        else if (_targetSnapshot is null || !_targetSnapshot.Equals(current))
        {
            SetStale("선택 범위, 편집 위치 또는 수식 내용이 바뀌었습니다. 초안을 되돌려 현재 대상으로 다시 불러오세요.");
        }
        else
        {
            SetStale(false, string.Empty);
        }
        NotifyResolvedProperties();
        ValidateDraft();
        UpdateFontMeasurementDiagnostic();
        NotifyState();
    }

    private void CaptureBaseline()
    {
        if (!_session.TryCaptureMathPropertyTarget(_selectedScope, out _targetSnapshot) || _targetSnapshot is null)
        {
            _resolved = null;
            TargetSummary = "속성 적용 대상을 확인할 수 없습니다.";
            SetStale(false, string.Empty);
            SetDefaultDraft();
            OnPropertyChanged(nameof(TargetSummary));
            NotifyResolvedProperties();
            NotifyState();
            return;
        }
        _resolved = _session.ResolveMathStyle(_targetSnapshot);
        TargetSummary = DescribeTarget(_targetSnapshot);
        OnPropertyChanged(nameof(TargetSummary));
        _isDraftStale = false;
        _staleReason = string.Empty;
        LoadDraftFromResolved();
        OnPropertyChanged(nameof(IsDraftStale));
        OnPropertyChanged(nameof(StaleReason));
        UpdateSelectionNotice();
        NotifyResolvedProperties();
        ValidateDraft();
        UpdateFontMeasurementDiagnostic();
        NotifyState();
    }

    private void LoadDraftFromResolved()
    {
        if (_resolved is null)
        {
            SetDefaultDraft();
            return;
        }
        _updatingDraft = true;
        _draftFontSizeText = FormatNumber(_resolved.FontSize.Value);
        _draftFontFamily = _resolved.FontFamily.Value;
        _draftForeground = _resolved.Foreground.Value;
        _draftTypesettingStyle = _resolved.TypesettingStyle.Value;
        _draftPinTypesettingStyle = false;
        _draftTextRole = null;
        _draftBracketKind = null;
        _baselineBracketKind = TryGetCurrentSelectedBrackets(out var selectedBrackets)
            ? selectedBrackets!.Kind
            : null;
        _baselineGroupRowSpacing = _targetSnapshot?.Scope == MathPropertyScope.CurrentGroup &&
                                   TryGetGroupRowSpacing(_session.Workspace, _targetSnapshot.GroupId, out var rowSpacing)
            ? rowSpacing
            : null;
        _draftGroupRowSpacingText = _baselineGroupRowSpacing is double spacing
            ? FormatRoundTripNumber(spacing)
            : string.Empty;
        _baselineFontSizeText = _draftFontSizeText;
        _baselineFontFamily = _draftFontFamily;
        _baselineForeground = _draftForeground;
        _baselineTypesettingStyle = _resolved.TypesettingStyle.Value;
        _updatingDraft = false;
        OnPropertyChanged(nameof(DraftFontSizeText));
        OnPropertyChanged(nameof(DraftFontFamily));
        OnPropertyChanged(nameof(DraftForeground));
        OnPropertyChanged(nameof(DraftTypesettingStyle));
        OnPropertyChanged(nameof(SelectedTypesettingStyleChoice));
        OnPropertyChanged(nameof(DraftPinTypesettingStyle));
        OnPropertyChanged(nameof(DraftTextRole));
        OnPropertyChanged(nameof(SelectedTextRoleChoice));
        OnPropertyChanged(nameof(DraftBracketKind));
        OnPropertyChanged(nameof(SelectedBracketKindChoice));
        OnPropertyChanged(nameof(DraftGroupRowSpacingText));
        ValidateDraft();
    }

    private void SetDefaultDraft()
    {
        _updatingDraft = true;
        _draftFontSizeText = string.Empty;
        _draftFontFamily = string.Empty;
        _draftForeground = string.Empty;
        _draftTypesettingStyle = _session.Workspace.Style.TypesettingStyle;
        _draftPinTypesettingStyle = false;
        _draftTextRole = null;
        _draftBracketKind = null;
        _baselineBracketKind = null;
        _draftGroupRowSpacingText = string.Empty;
        _baselineGroupRowSpacing = null;
        _baselineFontSizeText = string.Empty;
        _baselineFontFamily = string.Empty;
        _baselineForeground = string.Empty;
        _baselineTypesettingStyle = _session.Workspace.Style.TypesettingStyle;
        _updatingDraft = false;
        OnPropertyChanged(nameof(DraftFontSizeText));
        OnPropertyChanged(nameof(DraftFontFamily));
        OnPropertyChanged(nameof(DraftForeground));
        OnPropertyChanged(nameof(DraftTypesettingStyle));
        OnPropertyChanged(nameof(SelectedTypesettingStyleChoice));
        OnPropertyChanged(nameof(DraftPinTypesettingStyle));
        OnPropertyChanged(nameof(DraftTextRole));
        OnPropertyChanged(nameof(SelectedTextRoleChoice));
        OnPropertyChanged(nameof(DraftBracketKind));
        OnPropertyChanged(nameof(SelectedBracketKindChoice));
        OnPropertyChanged(nameof(DraftGroupRowSpacingText));
    }

    private void ResetDraft() => CaptureBaseline();

    private void Apply()
    {
        if (!CanApply || _targetSnapshot is null)
            return;
        var patch = BuildPatch();
        if (patch is null)
            return;
        var result = _session.ApplyMathProperties(_targetSnapshot, patch);
        if (result.Status == MathPropertyChangeStatus.Applied || result.Status == MathPropertyChangeStatus.NoChange)
            CaptureBaseline();
        else if (result.Status == MathPropertyChangeStatus.StaleTarget)
            SetStale(result.Message ?? "적용 대상이 변경되었습니다. 초안을 되돌려 다시 시도하세요.");
        NotifyState();
    }

    private void ResetOverride()
    {
        if (!CanResetOverride || _isDraftStale || _targetSnapshot is null)
            return;
        var result = _session.ResetMathStyleOverride(_targetSnapshot);
        if (result.Status is MathPropertyChangeStatus.Applied or MathPropertyChangeStatus.NoChange)
            CaptureBaseline();
        else if (result.Status == MathPropertyChangeStatus.StaleTarget)
            SetStale(result.Message ?? "적용 대상이 변경되었습니다. 초안을 되돌려 다시 시도하세요.");
        NotifyState();
    }

    private MathPropertiesPatch? BuildPatch()
    {
        if (_resolved is null || _fontSizeValidationError.Length != 0 ||
            _fontFamilyValidationError.Length != 0 || _foregroundValidationError.Length != 0 ||
            _groupRowSpacingValidationError.Length != 0)
            return null;
        // Keep a spacing intent atomic with the rest of the draft. This also
        // preserves stale drafts across scope changes without silently dropping
        // them when another property is applied in an unsupported scope.
        if (HasGroupRowSpacingDraftChange && !CanEditGroupRowSpacing)
            return null;
        double? size = null;
        if (!string.IsNullOrWhiteSpace(_draftFontSizeText) &&
            !string.Equals(_draftFontSizeText, _baselineFontSizeText, StringComparison.Ordinal) &&
            double.TryParse(_draftFontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize))
            size = parsedSize;
        var family = !string.IsNullOrWhiteSpace(_draftFontFamily) &&
                     !string.Equals(_draftFontFamily, _baselineFontFamily, StringComparison.Ordinal)
            ? _draftFontFamily
            : null;
        var foreground = !string.IsNullOrWhiteSpace(_draftForeground) &&
                         !string.Equals(_draftForeground, _baselineForeground, StringComparison.Ordinal)
            ? _draftForeground
            : null;
        MathTypesettingStyle? typesettingStyle = _draftTypesettingStyle is MathTypesettingStyle selectedStyle &&
                                                  selectedStyle != _baselineTypesettingStyle
            ? selectedStyle
            : _draftPinTypesettingStyle && CanPinTypesettingStyle
                ? _draftTypesettingStyle ?? _baselineTypesettingStyle
                : null;
        MathTextRole? textRole = _draftTextRole is MathTextRole selectedRole && selectedRole != CurrentTextRole
            ? selectedRole
            : null;
        if (HasBracketKindDraftChange && !CanEditBracketKind)
            return null;
        MathBracketKind? bracketKind = HasBracketKindDraftChange ? _draftBracketKind : null;
        double? groupRowSpacing = null;
        if (CanEditGroupRowSpacing && HasGroupRowSpacingDraftChange &&
            double.TryParse(_draftGroupRowSpacingText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var parsedSpacing))
            groupRowSpacing = parsedSpacing;
        return size is null && family is null && foreground is null && typesettingStyle is null && textRole is null &&
               bracketKind is null && groupRowSpacing is null
            ? null
            : new MathPropertiesPatch(new MathStylePatch(size, family, foreground), typesettingStyle, textRole,
                groupRowSpacing, bracketKind);
    }

    private void ValidateDraft()
    {
        _fontSizeValidationError = string.Empty;
        var fontSizeChanged = !string.Equals(_draftFontSizeText, _baselineFontSizeText, StringComparison.Ordinal);
        if (fontSizeChanged && !string.IsNullOrWhiteSpace(_draftFontSizeText) &&
            (!double.TryParse(_draftFontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
             !double.IsFinite(size) || size is < 1 or > 512))
            _fontSizeValidationError = "1~512 사이의 유한한 논리 px 값을 입력하세요.";

        _fontFamilyValidationError = string.Empty;
        var foregroundChanged = !string.Equals(_draftForeground, _baselineForeground, StringComparison.Ordinal);
        _foregroundValidationError = foregroundChanged && !string.IsNullOrWhiteSpace(_draftForeground) &&
                                     !WorkspaceValidator.IsHexColor(_draftForeground)
            ? "#RRGGBB 또는 #AARRGGBB 색상을 입력하세요."
            : string.Empty;

        _groupRowSpacingValidationError = string.Empty;
        if (CanEditGroupRowSpacing && !string.IsNullOrWhiteSpace(_draftGroupRowSpacingText) &&
            HasGroupRowSpacingDraftChange &&
            (!double.TryParse(_draftGroupRowSpacingText, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var rowSpacing) || !double.IsFinite(rowSpacing) || rowSpacing is < 0 or > 512))
            _groupRowSpacingValidationError = "0~512 사이의 유한한 논리 px 값을 입력하세요.";

        OnPropertyChanged(nameof(FontSizeValidationError));
        OnPropertyChanged(nameof(FontFamilyValidationError));
        OnPropertyChanged(nameof(ForegroundValidationError));
        OnPropertyChanged(nameof(GroupRowSpacingValidationError));
    }

    private void UpdateFontMeasurementDiagnostic()
    {
        _fontMeasurementFailed = false;
        var familyChanged = !string.IsNullOrWhiteSpace(_draftFontFamily) &&
                            !string.Equals(_draftFontFamily, _baselineFontFamily, StringComparison.Ordinal);
        var sizeChanged = !string.IsNullOrWhiteSpace(_draftFontSizeText) &&
                          !string.Equals(_draftFontSizeText, _baselineFontSizeText, StringComparison.Ordinal);
        var familyIsValid = _fontFamilyValidationError.Length == 0;
        var sizeIsValid = _fontSizeValidationError.Length == 0;

        // Invalid or blank draft values never cross the metrics boundary. Unchanged legacy values
        // remain valid inputs even when they are outside the range for new edits.
        if ((familyChanged && !familyIsValid) || (sizeChanged && !sizeIsValid))
        {
            SetFontMeasurementDiagnostic(string.Empty, failed: false);
            return;
        }

        var family = familyChanged ? _draftFontFamily : EffectiveFontFamily;
        var size = EffectiveFontSize;
        if (sizeChanged && double.TryParse(_draftFontSizeText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsedSize))
            size = parsedSize;

        var message = string.Empty;
        var measurementFailed = false;
        if (_textMetrics is not null)
        {
            try
            {
                var measurement = _textMetrics.Measure("M", family, size, italic: false);
                if (!double.IsFinite(measurement.Width) || measurement.Width <= 0 ||
                    !double.IsFinite(measurement.Ascent) || measurement.Ascent < 0 ||
                    !double.IsFinite(measurement.Descent) || measurement.Descent < 0)
                    throw new InvalidOperationException("측정 결과가 유한한 양수 범위가 아닙니다.");
            }
            catch (Exception exception)
            {
                measurementFailed = familyChanged || sizeChanged;
                message = measurementFailed
                    ? $"적용 후보 글꼴을 측정하지 못해 적용할 수 없습니다: {exception.Message}"
                    : $"현재 글꼴 측정 경고입니다. 이번 속성 변경은 계속할 수 있습니다: {exception.Message}";
            }
        }

        if (_glyphProbe is not null && !string.IsNullOrWhiteSpace(family))
        {
            try
            {
                var probe = _glyphProbe.Probe("M", family);
                if (message.Length == 0 && probe.Status == MathGlyphCoverageStatus.Unknown)
                    message = probe.Reason;
                else if (message.Length == 0 && probe.Status == MathGlyphCoverageStatus.Missing)
                    message = $"요청 글꼴의 문자표에서 기본 측정 문자 M을 찾지 못했습니다. 관측 글꼴: {probe.FontFamily}.";
                else if (message.Length == 0 && !string.Equals(probe.FontFamily, family, StringComparison.OrdinalIgnoreCase))
                    message = $"요청한 글꼴 대신 '{probe.FontFamily}' face가 관측되었습니다.";
            }
            catch (Exception exception)
            {
                if (message.Length == 0)
                    message = $"글리프 정보를 확인할 수 없습니다: {exception.Message}";
            }
        }

        SetFontMeasurementDiagnostic(message, measurementFailed);
    }

    private void SetFontMeasurementDiagnostic(string message, bool failed)
    {
        _fontMeasurementFailed = failed;
        if (!string.Equals(_fontMeasurementDiagnostic, message, StringComparison.Ordinal))
        {
            _fontMeasurementDiagnostic = message;
            OnPropertyChanged(nameof(FontMeasurementDiagnostic));
        }
        NotifyState();
    }

    private void UpdateSelectionNotice()
    {
        var notice = _selectedScope != MathPropertyScope.SelectionNode
            ? "선택한 적용 범위의 모든 하위 노드에 속성을 적용합니다."
            : _session.IsNodeSelected
            ? "전체 구조 선택 중입니다. 해당 구조와 하위 노드에 적용합니다."
            : _session.Selection is not null
                ? "일부 문자를 선택했습니다. 선택 문자가 포함된 텍스트 전체에 스타일을 적용합니다."
                : "커서가 있는 텍스트 전체에 스타일을 적용합니다.";
        if (string.Equals(SelectionNotice, notice, StringComparison.Ordinal))
            return;
        SelectionNotice = notice;
        OnPropertyChanged(nameof(SelectionNotice));
        OnPropertyChanged(nameof(TextRoleScopeNotice));
    }

    private bool TryGetRoleTarget(out TextNode? textNode)
    {
        textNode = null;
        if (_selectedScope != MathPropertyScope.SelectionNode ||
            _targetSnapshot?.TargetNodeId is not Guid targetNodeId)
            return false;
        textNode = MathTree.FindTextNode(_session.Workspace, targetNodeId);
        return textNode is not null;
    }

    private bool TryGetCurrentSelectedBrackets(out BracketsNode? brackets)
    {
        brackets = null;
        var snapshot = _targetSnapshot;
        if (_isDraftStale || snapshot is null || _selectedScope != MathPropertyScope.SelectionNode ||
            snapshot.Scope != MathPropertyScope.SelectionNode || !snapshot.IsNodeSelected ||
            snapshot.SelectedNodeId is not Guid selectedNodeId || snapshot.TargetNodeId != selectedNodeId ||
            !_session.IsNodeSelected || _session.SelectedNodeId != selectedNodeId ||
            !_session.TryCaptureMathPropertyTarget(MathPropertyScope.SelectionNode, out var current) ||
            current is null || !snapshot.Equals(current) ||
            !MathTree.TryFindNode(_session.Workspace, selectedNodeId, out var node, out _) ||
            node is not BracketsNode selectedBrackets)
            return false;

        brackets = selectedBrackets;
        return true;
    }

    private bool TryGetCurrentGroupRowSpacing(out double rowSpacing)
    {
        rowSpacing = default;
        return TryGetCurrentGroup(out var group) && (rowSpacing = group.RowSpacing) >= 0;
    }

    private bool TryGetCurrentGroup(out EquationGroup group)
    {
        group = null!;
        if (!_session.TryCaptureMathPropertyTarget(MathPropertyScope.CurrentGroup, out var snapshot) ||
            snapshot?.GroupId is not Guid groupId)
            return false;
        return TryGetGroup(_session.Workspace, groupId, out group);
    }

    private static bool TryGetGroupRowSpacing(EquationWorkspace workspace, Guid? groupId, out double rowSpacing)
    {
        rowSpacing = default;
        return groupId is Guid id && TryGetGroup(workspace, id, out var group) &&
               (rowSpacing = group.RowSpacing) >= 0;
    }

    private static bool TryGetGroup(EquationWorkspace workspace, Guid groupId, out EquationGroup group)
    {
        foreach (var candidate in workspace.Groups)
        {
            if (candidate.Id != groupId)
                continue;
            group = candidate;
            return true;
        }
        group = null!;
        return false;
    }

    private string DescribeTarget(MathPropertyTargetSnapshot snapshot)
    {
        if (snapshot.Scope == MathPropertyScope.WorkspaceDefault)
            return "전체 작업 기본값";
        if (snapshot.GroupId is not Guid groupId)
            return "속성 적용 대상";
        var workspace = _session.Workspace;
        for (var groupIndex = 0; groupIndex < workspace.Groups.Length; groupIndex++)
        {
            var group = workspace.Groups[groupIndex];
            if (group.Id != groupId)
                continue;
            if (snapshot.Scope == MathPropertyScope.CurrentGroup)
                return $"그룹 {groupIndex + 1} · {group.Rows.Length}행";
            if (snapshot.RowId is not Guid rowId)
                return $"그룹 {groupIndex + 1}";
            for (var rowIndex = 0; rowIndex < group.Rows.Length; rowIndex++)
            {
                var row = group.Rows[rowIndex];
                if (row.Id != rowId)
                    continue;
                if (snapshot.Scope == MathPropertyScope.CurrentRow)
                    return $"그룹 {groupIndex + 1} · {rowIndex + 1}행";
                if (snapshot.TargetNodeId is Guid nodeId)
                {
                    if (MathTree.TryFindNode(workspace, nodeId, out var node, out _) && node is not null)
                        return $"그룹 {groupIndex + 1} · {rowIndex + 1}행 · {DescribeNode(node)}";
                    return $"그룹 {groupIndex + 1} · {rowIndex + 1}행 · 선택한 수식";
                }
            }
        }
        return "속성 적용 대상";
    }

    private static string DescribeNode(MathNode node) => node switch
    {
        TextNode { IsEmptySlot: true } => "빈 텍스트 슬롯",
        TextNode { Text.Length: 0 } => "빈 텍스트",
        TextNode text => $"텍스트 ‘{PreviewText(text.Text)}’",
        MathSpaceNode => "얇은 수학 공백",
        SequenceNode => "수식 구문",
        FractionNode => "분수",
        RadicalNode => "근호",
        ScriptNode => "첨자",
        MatrixNode matrix => $"행렬 {matrix.Rows}×{matrix.Columns}",
        IntegralNode => "적분",
        SummationNode => "합",
        LimitNode => "극한",
        BracketsNode brackets => $"{BracketName(brackets.Kind)} 수식",
        CasesNode cases => $"경우 나누기 {cases.Rows.Length}행",
        _ => "수식"
    };

    private static string BracketName(MathBracketKind kind) => kind switch
    {
        MathBracketKind.Parentheses => "소괄호",
        MathBracketKind.Square => "대괄호",
        MathBracketKind.Curly => "중괄호",
        MathBracketKind.Angle => "꺾쇠",
        _ => "괄호"
    };

    private static string PreviewText(string text)
    {
        var runes = text.EnumerateRunes().Take(18).ToArray();
        var preview = string.Concat(runes.Take(16).Select(rune => rune.Value switch
        {
            '\n' or '\r' => "↵",
            '\t' => "⇥",
            ' ' => "␠",
            _ when Rune.IsControl(rune) => "�",
            _ => rune.ToString()
        }));
        return runes.Length > 16 ? $"{preview}…" : preview;
    }

    private string SourceLabel(MathStyleOrigin? origin)
    {
        if (origin is null)
            return "상속 출처를 확인할 수 없습니다.";
        var source = origin.Kind switch
        {
            MathStyleSourceKind.WorkspaceDefault => "작업 기본값",
            MathStyleSourceKind.Group => $"그룹 {origin.GroupNumber}",
            MathStyleSourceKind.Row => $"그룹 {origin.GroupNumber} · {origin.RowNumber}행",
            MathStyleSourceKind.Node => MathTree.TryFindNode(_session.Workspace, origin.OwnerId, out var node, out _) && node is not null
                ? DescribeNode(node)
                : "상위 수식 노드",
            _ => "상속"
        };
        return $"적용 출처: {source}";
    }

    private static string FormatNumber(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string FormatRoundTripNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void SetStale(string reason)
    {
        SetStale(true, reason);
    }

    private void SetStale(bool isStale, string reason)
    {
        var changed = _isDraftStale != isStale || !string.Equals(_staleReason, reason, StringComparison.Ordinal);
        _isDraftStale = isStale;
        _staleReason = reason;
        if (changed)
        {
            OnPropertyChanged(nameof(IsDraftStale));
            OnPropertyChanged(nameof(StaleReason));
            OnPropertyChanged(nameof(IsFractionSelection));
            OnPropertyChanged(nameof(FractionDisplayNotice));
            OnPropertyChanged(nameof(GroupRowSpacingNotice));
            OnPropertyChanged(nameof(CurrentBracketKind));
            OnPropertyChanged(nameof(CurrentBracketKindLabel));
            OnPropertyChanged(nameof(CanEditBracketKind));
            OnPropertyChanged(nameof(BracketKindDisabledReason));
            OnPropertyChanged(nameof(BracketKindNotice));
        }
    }

    private void NotifyResolvedProperties()
    {
        OnPropertyChanged(nameof(EffectiveFontSize));
        OnPropertyChanged(nameof(ExplicitFontSize));
        OnPropertyChanged(nameof(FontSizeSourceLabel));
        OnPropertyChanged(nameof(EffectiveFontFamily));
        OnPropertyChanged(nameof(ExplicitFontFamily));
        OnPropertyChanged(nameof(FontFamilySourceLabel));
        OnPropertyChanged(nameof(EffectiveForeground));
        OnPropertyChanged(nameof(ExplicitForeground));
        OnPropertyChanged(nameof(ForegroundSourceLabel));
        OnPropertyChanged(nameof(EffectiveTypesettingStyle));
        OnPropertyChanged(nameof(ExplicitTypesettingStyle));
        OnPropertyChanged(nameof(TypesettingStyleSourceLabel));
        OnPropertyChanged(nameof(TypesettingStyleContextNotice));
        OnPropertyChanged(nameof(TypesettingStyleLatexDiagnostic));
        OnPropertyChanged(nameof(CanPinTypesettingStyle));
        OnPropertyChanged(nameof(TypesettingPinDisabledReason));
        OnPropertyChanged(nameof(TypesettingPinNotice));
        OnPropertyChanged(nameof(IsFractionSelection));
        OnPropertyChanged(nameof(FractionDisplayNotice));
        OnPropertyChanged(nameof(CanEditGroupRowSpacing));
        OnPropertyChanged(nameof(CurrentGroupRowSpacing));
        OnPropertyChanged(nameof(GroupRowSpacingDisabledReason));
        OnPropertyChanged(nameof(GroupRowSpacingNotice));
        OnPropertyChanged(nameof(GroupRowSpacingLatexDiagnostic));
        OnPropertyChanged(nameof(CurrentTextRole));
        OnPropertyChanged(nameof(CanEditTextRole));
        OnPropertyChanged(nameof(TextRoleDisabledReason));
        OnPropertyChanged(nameof(TextRoleScopeNotice));
        OnPropertyChanged(nameof(CurrentBracketKind));
        OnPropertyChanged(nameof(CurrentBracketKindLabel));
        OnPropertyChanged(nameof(CanEditBracketKind));
        OnPropertyChanged(nameof(BracketKindDisabledReason));
        OnPropertyChanged(nameof(BracketKindNotice));
        OnPropertyChanged(nameof(CanResetOverride));
        OnPropertyChanged(nameof(ResetOverrideDisabledReason));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanPinTypesettingStyle));
        OnPropertyChanged(nameof(TypesettingPinDisabledReason));
        OnPropertyChanged(nameof(TypesettingPinNotice));
        OnPropertyChanged(nameof(CanEditGroupRowSpacing));
        OnPropertyChanged(nameof(GroupRowSpacingDisabledReason));
        OnPropertyChanged(nameof(GroupRowSpacingNotice));
        OnPropertyChanged(nameof(GroupRowSpacingLatexDiagnostic));
        OnPropertyChanged(nameof(CanEditBracketKind));
        OnPropertyChanged(nameof(BracketKindDisabledReason));
        OnPropertyChanged(nameof(BracketKindNotice));
        OnPropertyChanged(nameof(CanResetOverride));
        OnPropertyChanged(nameof(ResetOverrideDisabledReason));
        _applyCommand.NotifyCanExecuteChanged();
        _resetDraftCommand.NotifyCanExecuteChanged();
        _resetOverrideCommand.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
