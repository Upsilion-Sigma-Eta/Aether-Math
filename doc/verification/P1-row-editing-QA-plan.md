# P1 행 편집 단위 QA 계획

## 목적과 실행 경계

이 문서는 추가·복제·삭제·위/아래 이동이 있는 행 편집 단위의 검증 계획과 실행 결과를 기록한다. 제품 코드 구현은 engine/UI 담당자가 수행하고 QA는 `tests/`, 이 문서 및 개발 계획서의 검증 현황만 소유한다. 공통 통합 동결과 메인의 실행 승인을 받은 뒤 QA 담당자가 단독으로 빌드와 검증을 순차 실행했다.

실제 운영체제 창·포커스·UIA·물리 입력·computer-use는 사용하지 않는다. UI 검증은 별도 Avalonia.Headless 12.1.4 + Skia child에서 실제 `App.axaml`, FluentTheme, AetherTheme, `MainWindow`를 사용한다. `Show()` 직전과 직후 platform handle이 `STUB`이고 `IntPtr.Zero`인지 확인하며 둘 중 하나라도 다르면 표시·입력 테스트를 중단한다. Headless 합성 이벤트와 offscreen 이미지는 물리 입력 합격으로 세지 않는다. UI artifact는 `doc/verification/artifacts/p1-row-editing/`에 보존한다.

## 동결 전 확정된 편집 계약

- 활성 행은 전체 구조 선택 시 `SelectedNodeId`가 포함된 행, 그 외에는 caret의 행이다. 구조 선택 대상과 caret가 서로 다른 행일 수 있다.
- 추가는 활성 행 바로 뒤, 같은 그룹에 새 row와 빈 필수 `TextNode`를 새 ID로 삽입하고 그 텍스트의 caret 0으로 이동한다.
- 복제는 대상 row와 모든 subtree를 복제하고 모든 node ID 및 row ID를 새로 만든다. 모델 속성, Unicode 텍스트, `IsEmptySlot`, 행렬 크기, 스타일과 flag를 보존한다. 대상 안의 caret·부분 선택·구조 선택은 새 ID로 매핑한다. 교차 행 구조 선택에서 원 caret가 복제 범위 밖이면 복제된 선택 구조의 첫 텍스트 caret 0으로 이동하며 undo는 원래 교차 행 상태를 복원한다.
- 삭제는 그룹의 유일한 행이면 완전 무변경 거절한다. 그 외 대상 행을 삭제하고 다음 행, 없으면 이전 행의 첫 텍스트 caret 0으로 이동한다.
- 위/아래 이동은 같은 그룹의 바로 이웃과 row 순서만 교환한다. 경계에서는 완전 무변경 거절하며 성공 때는 ID와 caret·선택 상태를 유지한다.
- 성공 동작은 undo 한 번으로 되돌리고 redo 한 번으로 복원한다. 실패 경계는 workspace, revision, dirty, undo/redo 가용성 및 선택 상태를 바꾸지 않는다. 저장 snapshot은 기존 StateId 의미를 보존한다.
- 공개 구현 계약은 `EditingSession`의 `ActiveGroupNumber`, `ActiveRowNumber`, `ActiveRowCount`, `CanAddRowAfterCurrent`, `CanDuplicateCurrentRow`, `CanDeleteCurrentRow`, `CanMoveCurrentRowUp`, `CanMoveCurrentRowDown` 및 해당 다섯 `bool` 편집 메서드다. VM은 대응하는 command와 활성 행 속성을 제공한다.

## 논리 QA 계획

`RowEditingCases`는 테스트 대상의 실제 `EditingSession`, `WorkspaceValidator`, `WorkspaceFileService`, `LatexSerializer`, `MathLayoutEngine`, `SvgExportService` API를 호출한다. 구현 소스 문자열이나 제품의 private field를 검사하지 않는다. 각 성공 사례에서는 ID 전체 고유성 및 대상 밖 보존을 확인하고, 실패 사례에서는 전체 workspace fingerprint와 revision·undo/redo 상태를 비교한다.

| 영역 | 검증 사례 |
|---|---|
| 행 대상 결정 | 다중 그룹과 여러 행에서 caret 행 대상, 교차 행 전체 구조 선택 시 selected-node 행 대상, 비활성 그룹 불변 |
| 추가·삭제 | 필수 빈 슬롯과 새 ID·caret 0, 삽입 위치, 삭제 후 다음/이전 행 caret, 그룹의 마지막 한 행 삭제·경계 이동의 완전 무변경 |
| 복제 | 깊은 분수·근호·첨자·행렬을 포함한 clone, 모든 ID 재생성 및 전역 유일성, text·empty flag·행렬 크기·구조 값 보존, 행 안 caret/역방향 부분 선택/grapheme offset/구조 선택 remap, 교차 행 caret 및 선택 fallback |
| 재정렬 | 같은 그룹 인접 행만 swap, 다른 그룹 격리, row/node ID와 caret·selection 유지, 위·아래 경계 무변경 |
| history와 저장 | 각 변경의 undo/redo 한 단계 복원, 저장 snapshot의 dirty 전이, 저장된 workspace 재열기 후 fingerprint·ID·행 순서 보존 |
| 소비 경로 | LaTeX의 같은 그룹 row 순서 및 등호 alignment 보존, SVG LayoutResult의 primitive 논리 순서, 복제 또는 추가한 빈 필수 슬롯이 있는 문서의 SVG 출력 거절 |
| 비동기 paste | clipboard read 대기 중 행 삭제·이동 등으로 target이 stale해지면 늦은 결과 폐기, 현재 편집 상태·다음 입력 불변 |

기존 기본 회귀 50개는 모두 계속 등록하고 새 행 사례를 추가한다. 기준/실행 개수는 동결 실행 로그 기준으로 기록하며 과거 `38+6+4+2` 결과와 이번 추가 결과를 혼동하지 않는다.

## 실제 UI headless QA 계획

전용 `--row-ui-headless` 자식 모드를 별도 분기한다. 일반 CLI 및 기존 font/pointer 자식은 이 모드의 `Show()` 경로를 공유하지 않는다. `QaApplication`의 빈 스타일은 재사용하지 않고 `AppBuilder.Configure<AetherMath.Desktop.App>()`로 실제 앱 리소스를 적재하며, headless setup 뒤 테스트용 `MainWindow`를 만든다. 표시 직전·직후 STUB/0 검사를 통과한 때에만 window를 보여 준다.

- 실제 XAML compiled binding, row command의 CanExecute/disabled 상태 및 AutomationId를 탐색한다. 계약상 row 단축키는 추가되지 않으므로 새 row 키 단축키가 있다고 가정하지 않는다. 기존 Ctrl+Z/Ctrl+Y는 headless 합성 키 입력으로 실제 편집 control 경로를 별도로 확인한다.
- 창 크기 1060×700 및 1360×880에서 Dispatcher/layout을 정리한 뒤 실제 control bounds를 읽는다. `ActiveRowIndicator`, 다섯 row 버튼, `MathEditorSurface`가 보이고 버튼끼리 또는 편집 surface와 겹치지 않는지 비교한다. Disabled command와 삭제 마지막 행의 tooltip도 검사한다.
- 전용 headless TopLevel의 합성 MouseDown/MouseUp으로 실제 row 버튼을 클릭하고 row 수, 활성 행, 세션 caret, 명령 상태를 비교한다. 운영체제 포인터 입력은 아니다.
- 여러 행을 추가해 viewport보다 긴 목록을 만들고 맨 아래 행을 활성화한다. 마지막 행 caret가 viewport 안에 들어오도록 스크롤되는지 확인한다.
- 두 크기에서 전체 `MainWindow`를 offscreen bitmap으로 저장하고 실제 화면 크기와 control bounds를 JSON으로 함께 보존한다. 이미지의 검토 범위는 배치·겹침·잘림·기본 리소스 적용이며 실제 화면/OS 창과 동일하다고 주장하지 않는다.

## 이번 단위 완료 보고 형식

결과는 논리 API/저장·출력/paste 회귀와 UI Headless·bounds·artifact를 분리해 보고한다. 빌드 및 각 테스트 로그를 `doc/verification/artifacts/p1-row-editing/`에 새 고유 이름으로 저장하고 기존 F05 자료를 덮어쓰지 않는다. 계획서 12절은 실제 실행 결과로만 갱신한다. OS 물리 입력 게이트는 미검증으로 유지하며, 새 사용 지시에 따라 다음 개발 진행 조건과 별도로 표시한다.

## 실행 결과 — 2026-10-22

| 단계 | 결과 | 증거 |
|---|---|---|
| 최종 Debug 통합 빌드 | 종료 코드 0, 경고 0, 오류 0. 셸 속성 패널 우측 inset 수정까지 포함 | `artifacts/p1-row-editing/build-row-final-05.log` |
| 행 편집 논리·소비자 사례 | 9/9 합격: ADD, ACTIVE, DUPLICATE, DUP-CROSS, DELETE, MOVE, NOOP, IO, PASTE. 깊은 clone ID/selection mapping, 저장 재열기, LaTeX/SVG 행 정렬, 빈 슬롯 출력 거절, 행 변경 중 paste stale을 확인 | `artifacts/p1-row-editing/row-targeted-final-03.log` |
| 실제 MainWindow Headless UI | 1/1 합격: Avalonia.Headless 12.1.4 + Skia, 표시 전후 STUB/0, 앱 XAML/바인딩/명령, 두 창 크기, 활성 행 도구 bounds, 합성 클릭/undo/redo, 18행 이상 및 viewport보다 긴 행 끝 caret 노출 확인. PNG 검토에서 최소 크기 속성 콘텐츠와 overlay scrollbar 간격을 확인해 내부 StackPanel 우측 inset 10px 적용 후 재검증 | 전체 회귀 ROW-UI 및 `artifacts/p1-row-editing/shell-minimum-1060x700-20261022T115737638Z-52f010cad76c42afbf8e671d72add3aa/`, `shell-default-1360x880-20261022T115737696Z-099925bcead74c54be3b940aa4feaab1/`의 PNG와 `bounds.json` |
| 기존 회귀 포함 전체 | UI inset 수정 후 60/60 합격, 실패 0, 종료 코드 0. 기존 50개와 행 편집 10개를 함께 실행 | 최종 통합: `artifacts/p1-row-editing/regression-final-60-02.log`; 앞선 통합 60/60: `regression-final-60-01.log` |

초기 진단 증거도 보존한다. 최초 빌드에서 QA 키/Automation API 참조 7건이 컴파일되지 않아 `RowUiInteractionCases.cs`를 Avalonia 12.1.4 공개 심볼에 맞췄고, shell helper nullable 경고 2건은 소유자가 해소했다. 첫 행 테스트에서는 SVG 스타일별 text run이 분리되는 정상 출력에 단일 문자열을 기대한 테스트 오류와 `Button.IsEnabled` 대신 `IsEffectivelyEnabled`를 보아야 하는 Avalonia 상태 차이를 바로잡았다. 다음 UI 시도는 DrawCaret의 실제 node 상하 bounds와 caret stop X를 stroke 폭 기준으로 확인하면서 마지막 caret가 viewport 아래 약 0.186px 벗어나는 제품 경계 문제를 검출했다. 엔진 담당이 BringCaretIntoView target에 상하 2px 여유를 넣은 뒤 동일한 엄격한 확인이 통과했다. 해당 실패 실행 로그와 생성물을 지우거나 성공으로 재표기하지 않았다.

완료 판정은 논리 모델/API 및 앱의 Headless UI 경로에 한정한다. `Show()`는 전용 Avalonia.Headless child 안에서 STUB/0을 확인한 뒤에만 호출했으며, 운영체제 창·포커스·UIA·실제 키보드/마우스 입력은 사용하지 않았다. 따라서 실제 Windows 화면/물리 입력 게이트는 미검증이고 별도 보류 상태다. 초기 build 실패(`build-row-final-01.log`), 첫 UI 테스트 기대 수정(`row-targeted-final-01.log`), 실제 caret clipping 발견(`row-targeted-final-02.log`)도 그대로 보존한다. 최종 60/60 통과 결과가 이 실패 이력을 대체하거나 과거 실행을 소급해 통과로 바꾸지 않는다.

## 메인 최종 리뷰 및 단위 승인

메인은 최종 Debug 빌드·전체 60/60 로그와 최종 1060×700/1360×880 PNG 및 bounds를 직접 검토했다. 최소 크기에서 속성 콘텐츠와 scrollbar 간격이 확보됐고, 두 크기 모두 행 도구와 수식 편집 영역의 겹침·잘림이 없음을 확인해 행 편집 단위를 통합 승인했다. 이 승인도 논리/Headless/offscreen 결과에 한정되며 물리 OS 입력은 미검증으로 유지한다. P1 단위 승인이 P1/P0 전체 개발·검증 완료를 뜻하지 않는다.

2026-10-22 11:49–11:52 UTC watchdog 감사 동안 타 작업과 검증 실행을 중지했고, 중단급 문제는 없었다. 감사 권고에 따라 caret 가시성 검사는 임의 글꼴 높이 대신 실제 `DrawCaret`의 node 상·하 bounds와 caret-stop X, stroke 폭 여유를 기준으로 삼았으며, 실제 clipping을 검출하고 엔진의 2px target 여유 수정 뒤 재검증했다.
