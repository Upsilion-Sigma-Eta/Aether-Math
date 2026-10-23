# P2 그룹 행 간격 QA

- 상태: 그룹 행 간격 slice 통합 승인. build06 경고0·오류0, 신규10-02 10/10, `TEXT-ROLE-UI` 단독02 1/1, 전체181-02 181/181이며 각 실제 종료 코드 0이다. 신규10 시각 리뷰와 진단 UI 리뷰는 메인 직접 검토를 통과했다. 승인 범위는 이 slice에 한정하며 전체 P2/P3/P0/P1은 미완료다.
- 기존 기준선: 승인된 전체 회귀 171건
- 목표: 신규 논리 9건 + `GROUP-ROW-SPACING-UI` 1건, 전체 181건
- UI child: `--group-row-spacing-ui-headless GROUP-ROW-SPACING-UI`
- 산출물 위치: `doc/verification/artifacts/p2-group-row-spacing/`
- 범위: 기존 그룹 행 간격 데이터의 사용자 편집·적용·검증, geometry 및 표시 소비자 경계
- QA 소스: [논리 사례](../../tests/AetherMath.LogicQA/GroupRowSpacingCases.cs), [Program 등록](../../tests/AetherMath.LogicQA/Program.cs)

이 작업은 `EquationGroup.RowSpacing` 모델 필드나 기존 파일 저장 경로를 새로 도입하는 단위가 아니다. 모델과 파일 저장 경계는 이미 존재한다. 이번 단위는 `CurrentGroup` 속성 편집, 유효성 검사, 그룹 내부 행 사이 geometry, LaTeX layout advisory 및 출력 미리보기 소비를 검증해 승인받았다. 전체 행·그룹 간격 정책이나 수평 간격 표준이 완료됐다는 뜻은 아니다.

## 고정 계약

`MathPropertiesPatch`의 후행 `GroupRowSpacing`은 nullable이며 null은 변경 안 함이다. 직접 patch의 명시 값은 `CurrentGroup`에서만 허용되고 유한한 0–512 논리 px이어야 한다. 같은 double 값은 텍스트 표기가 달라도 `NoChange`로 이력과 revision을 건드리지 않는다. 같은 patch의 다른 필드가 무효이거나 범위가 지원되지 않으면 간격도 부분 적용하지 않는다.

속성 패널의 `DraftGroupRowSpacingText`는 불변 기준값을 `InvariantCulture` round-trip `R` 형식으로 보여 준다. 빈 입력은 변경하지 않는다. 기존 저장값 512 초과도 읽기·표시·파일 왕복을 유지하며, 간격 초안이 바뀌지 않은 상태에서 글꼴·색 등 별도 스타일만 편집하는 것을 막지 않는다. 이는 새로 입력한 512 초과 값의 거절과 구분한다. 변경 초안 중 scope·대상·revision·세션이 바뀌면 입력 문자열을 보존하면서 stale 처리하고, 적용은 막는다. 지원되지 않는 범위에서 새 간격 변경 의도가 있으면 다른 필드만 조용히 적용하지 않고 전체 patch를 차단한다.

그룹 내부 행 사이의 실제 간격은 해당 그룹의 `RowSpacing`을 사용한다. 그룹 경계 사이 간격은 `MathLayoutOptions.RowSpacing`을 계속 사용한다. 간격만 변경하면 행 ID·노드 ID·caret·selection을 보존하는 단일 편집 commit이어야 한다. 스타일·preset 고정·간격을 함께 적용할 때도 한 commit이며, 스타일 override 초기화는 간격과 TextRole을 보존한다.

`LatexSerializer.GetLayoutDiagnostics`는 행이 두 개 이상인 각 그룹에 대해 간격이 0인 경우를 포함해 별도 안내를 내고, 단일 행 그룹에는 내지 않는다. 안내는 완성된 LaTeX source 생성을 막지 않는다. 간격만 바꾼 전후의 LaTeX 문자열은 정확히 같아야 한다. WorkspaceStyle/Display/Inline 미리보기는 간격을 적용한 고정 `LayoutResult`를 내보내며 SVG service는 같은 인스턴스를 사용한다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `GRS-CORE` | `CurrentGroup` patch의 0·512 경계, 잘못된 값, 동일 수치 no-op, SelectionNode/CurrentRow/WorkspaceDefault 거절, 무효 스타일·지원되지 않는 scope와 간격을 함께 보냈을 때의 원자 거절, revision/selection/session이 달라진 snapshot 거절 및 stale 호출의 전체 session 상태 불변. |
| `GRS-DRAFT-STALE` | invariant 입력 검증, 빈 초안·ResetDraft, 변경 초안 뒤 그룹·범위 이동, 실제 텍스트 편집으로 생긴 revision 변화, Session 교체, 비지원 범위의 새 간격 초안, 잘못된 색과 유효 간격 조합에서 초안 보존·Apply 차단·전체 상태 무변경. |
| `GRS-LEGACY-PRECISION` | 기존 600 저장값을 그대로 표시하고 스타일 단독 편집 및 파일 왕복에서 보존, 새 명시 600 거절, `28.1234567890123`의 `R` 표시·스타일 단독 편집·저장 정밀도, 동등 숫자의 다른 표기 no-op. |
| `GRS-GEOMETRY` | 간격만 변경 전후 같은 그룹 내 두 행 간격과 비대상 그룹을 비교하고, `MathLayoutOptions.RowSpacing`인 그룹 경계 간격은 분리해 검사한다. caret·selection·ID 보존과 단일 commit도 확인한다. 단일 행 그룹은 편집·이력 적용 뒤 새 간격을 임시 파일에 저장·재열기하며, 전후 Layout 크기와 glyph 좌표가 그대로이고 패널에는 화면 효과 없음 안내가 있으며 간격 LaTeX 진단은 비어 있는지 검사한다. |
| `GRS-ATOMIC-HISTORY` | 글꼴 크기·전경색·preset pin·간격을 한 번 적용하고 Undo/Redo의 workspace·StateId·dirty 상태를 확인한다. 4개 스타일 override 초기화가 간격과 TextRole을 유지하는지 검사한다. |
| `GRS-IO-CLONE` | 그룹 복제 시 새 group/row/node ID와 기존 간격·스타일·Role 보존, 단일 행 그룹 값 보존, 실제 TempDirectory 파일과 `WorkspaceStateService` recovery 저장·읽기 fingerprint 왕복. |
| `GRS-LATEX-DIAGNOSTICS` | 0 및 정밀 간격을 가진 여러 행 그룹별 진단, 단일 행 무진단, MainVM의 비차단 안내와 LaTeX 재사용 가능 상태, 간격만 변경 전후 LaTeX 문자열의 정확한 동일성. |
| `GRS-PREVIEW-SVG` | WorkspaceStyle/Display/Inline 각각에서 그룹 내부 간격과 그룹 경계 옵션을 확인하고, 실제 TempDirectory SVG 파일의 LayoutResult 참조·문자열·XML 크기/viewBox/glyph 순서 및 각 `<text x/y>`를 `GlyphPrimitive.BaselineOrigin`에 0.00051 이내로 대조한다. 원본 세션 상태 불변도 확인한다. |
| `SESSION-ACTIVE-TARGET-NOTIFY` | 공개 Workspace/Caret/Selection/SelectedNodeId/IsNodeSelected 및 활성 행 속성 알림 콜백 안에서 실제 AST 포함 행과 활성 그룹·행·행 수, CurrentGroup/CurrentRow 대상 ID가 이미 일치하는지 확인한다. 다른 그룹·행의 caret/부분 선택/전체 노드 선택, 행·그룹 구조 Commit, Undo/Redo 복원과 속성 패널의 현재 간격 갱신을 검증한다. 대기 중 간격 초안은 stale로 차단되면서 문자열을 유지해야 하며, 탐색 선택은 workspace/revision/history/dirty를 바꾸지 않는다. |

## Headless UI 경계

전용 `GroupRowSpacingUiInteractionCases.All()`은 `GROUP-ROW-SPACING-UI` 한 건을 제공하며, `Program.cs`가 `--group-row-spacing-ui-headless` child route를 연결한다. helper 산출물 범위는 `p2-group-row-spacing`이다. 고정 AutomationId에는 `GroupRowSpacingCard`, `GroupRowSpacingCurrentValue`, `GroupRowSpacingMeaningNotice`, `GroupRowSpacingDraftTextBox`, `GroupRowSpacingValidationError`, `GroupRowSpacingDisabledReason`, `GroupRowSpacingNotice`, `GroupRowSpacingLatexDiagnostic`, `LatexDiagnosticsScrollViewer`, `LatexLayoutDiagnosticBanner`, `LatexLayoutDiagnosticsList`가 포함된다.

승인된 실행은 소유 Headless child의 `STUB`/0 handle과 `TempDirectory` 파일 경계만 사용했다. 실제 OS 창·포커스·UIA·물리 입력·파일 선택기·사용자 AppData·computer-use는 수행하지 않았다.

## 실행 기록

| 단계 | 결과 | 이력 및 증거 |
|---|---|---|
| `build-group-row-spacing-final-01.log` + `group-row-spacing-new9-final-01.log` | 빌드 0/0·exit0; 신규9는 8/9·exit1 | 논리8은 통과했다. 첫 UI 실행은 collapsed diagnostic viewport의 좌표 변환에서 실패했고 PNG/bounds는 생성되지 않았다(빈 캡처 폴더만 존재). |
| `build-group-row-spacing-final-02.log` + `group-row-spacing-ui-final-01.log` | 빌드 0/0·exit0; UI 0/1·exit1 | 그룹 이동 후 VM 현재 간격은 24px인데 화면은 이전 68px을 표시했다. 당시 shell PNG/bounds 7쌍과 비교 PNG2/JSON1이 자동 생성됐다. 메인은 적용/잘못된 입력 최소창과 before/after 비교 4장만 확인했고, 전체 bounds는 검토하지 않아 통합 승인이 아니었다. |
| `build-group-row-spacing-final-03.log` + `group-row-spacing-new10-final-01.log` | 빌드 0/0·exit0; 신규10은 9/10·exit1 | UI 상단 캡처가 실제 phase label 대신 offset 1127.5를 보고한 helper 오류였다. 자동 실패 산출물 PNG/bounds 12쌍과 비교 PNG2/JSON1은 성공 실행과 별도 보존했다. |
| `build-group-row-spacing-final-04.log` + `group-row-spacing-new10-final-02.log` | 빌드 0/0·exit0; 신규10 10/10·exit0 | 실행 시각 2026-10-22 21:41:46–47 UTC. 메인은 bounds22 전체, shell PNG10, 비교 PNG2/JSON1을 직접 검토했다. 적용 최소, 최종 상세 기본, Workspace 비활성, 단일행 최소, stale 최소, 최소 expanded top/first/bottom, 기본 expanded bottom, 최소 재접힘 상태를 확인했다. 직접 검토 내용에는 stale 현재값24/초안88 구분·Apply 차단, 단일행 안내, 재접힘 viewport257/437px, expanded 진단, 비교 144×626→144×726 크기 변화가 포함된다. |
| `group-row-spacing-full181-final-01.log` | 180/181·exit1 | 그룹 행 간격 사례는 통과했다. 기존 `TEXT-ROLE-UI` 재접힘 상세의 viewport containment가 실패했다(`TextRoleCurrentValue`, X800 Y40 W206 H14). full run의 비교 PNG2/JSON1 및 UI PNG/bounds22쌍은 자동 산출물이다. |
| `build-group-row-spacing-final-05.log` + `group-row-spacing-text-role-ui-final-01.log` | 빌드 0/0·exit0; 기존 UI 0/1·exit1 | 진단 목록 scroll assertion이 실패했다. viewport484×58, TextBlock54, top y9..63/message-end y8..62/tail y42..54였다. helper의 8 DIP 여유를 무조건 적용하고 위쪽에서 전체 블록을 요구한 가정이 원인이었다. diagnostic PNG3/JSON1과 비교 PNG4/JSON1은 자동 실패 자료이며 당시 직접 검토되지 않았다. |
| `build-group-row-spacing-final-06.log` + `group-row-spacing-text-role-ui-final-02.log` | 빌드 0/0·exit0; 기존 UI 1/1·exit0 | 메인은 2026-10-22 21:50:23 UTC의 diagnostic PNG3/JSON을 직접 검토했다. STUB/0, source94/grid152/diagnostic58, top0→message7→list74, 차단 문구 전체 가시성, advisory 말미, viewport257→72 및 layout delta0을 확인했다. 단독 실행의 비교 PNG4 전체를 직접 검토한 것은 아니다. |
| `group-row-spacing-full181-final-02.log` | 181/181·exit0 | 전체 회귀 자동 산출물은 `20261022T2151` 접두사 UI PNG/bounds22쌍과 비교 PNG2/JSON1이다. 이는 신규10-02와 TEXT-ROLE 단독02의 메인 직접 검토 세트와 분리되며, 전체 자동 캡처 전부를 직접 검토했다고 주장하지 않는다. |

그룹 이동 후 화면 stale 표시는 단순 helper 문제로 처리하지 않았다. 공개 `PropertyChanged` observer에서 Core 알림 순서를 조사해, `NotifySessionChanged`/`NotifySelectionChanged`가 활성 행 위치 cache 갱신 전에 Workspace/Caret/Selection 알림을 내보내는 실제 제품 결함을 확인했다. observer가 읽는 시점에 활성 행과 CurrentGroup/CurrentRow target ID가 이전 그룹을 가리켰다. 엔진은 첫 알림 전에 cache를 갱신하도록 수정했고 QA는 `SESSION-ACTIVE-TARGET-NOTIFY` 독립 사례를 추가해 알림 콜백 안의 AST 대상, 활성 그룹/행/행 수, property target ID, 구조 commit, undo/redo 및 pending draft stale 보존을 확인했다. 따라서 신규 목표는 논리8+UI1/전체180에서 논리9+UI1/전체181로 늘었다.

첫 좌표 변환 실패와 phase-label 실패는 UI helper의 증거 수집 문제였으며 제품 결함으로 분류하지 않았다. 기존 `TEXT-ROLE-UI`의 재접힘 상세 viewport 및 진단 스크롤 실패는 그룹 간격 동작 실패가 아니라 전체 suite에서 드러난 별도 UI 검증 경계였다. helper 수정 뒤 기존 UI 단독 실행과 전체 회귀가 통과했다.

## 승인 범위와 한계

2026-10-22 21:09·21:24·21:39·21:56 UTC 감사는 절차 기록이며 테스트 근거가 아니다. 21:56 UTC 감사 동안 QA 작업은 중단·대기 상태였고, 감사 종료 후 문서의 시각만 정정했다. 승인된 범위는 기존 `EquationGroup.RowSpacing` 모델·파일 저장 경계를 재사용한 CurrentGroup 편집, 값 검증, geometry, 비차단 LaTeX layout advisory 및 frozen preview/SVG 소비의 논리·Headless·offscreen slice다. 기존 모델/저장의 존재와 이번 사용자 편집 승인은 구분한다.

기존 600 저장값 보존 검사는 특정 finite legacy 값의 읽기·표시·style-only 수정·파일 왕복만 다룬다. 누적 layout overflow나 모든 큰 유한 간격의 렌더 안전성, 전체 수평/수직 간격 정책을 증명하지 않는다. LaTeX layout advisory는 행 간격의 source 표현 손실을 알리는 비차단 경고다. 완성 코드 생성 차단(예: 제어 문자·필수 빈 슬롯)과 혼동하지 않으며, 실제 TeX 컴파일은 하지 않았다. 실제 OS/IME/clipboard/AppData, 외부 TeX, 전체 P2/P3/P0/P1은 검증되지 않았거나 미완료다.
