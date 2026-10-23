# P2 구조 탐색·범위 비우기 QA 검증 기록

- 상태: 메인 소스·PNG 리뷰를 마치고, 논리·Headless/offscreen 범위에서 이 slice를 통합 승인했다.
- 결과: Debug 빌드 경고 0개·오류 0개(exit 0), 신규 논리8+`STRUCTURE-CLEAR-UI` 1은 9/9(exit 0), 기준선120을 포함한 전체 회귀는 129/129(exit 0)다.
- 승인 범위: 구조 투영·선택·Clear 계약, 지정 Headless UI 상호작용과 offscreen 셸 캡처에 한정한다. 전체 P2/P1/P0 완료나 네이티브 OS 물리 입력 승인으로 확대하지 않는다.
- 기준선: P2 팔레트 통합 승인 전체 회귀 120건.
- 격리: 논리 테스트는 인메모리 모델을 사용한다. UI는 실제 `App`/`MainWindow`를 로드하는 전용 Avalonia Headless + Skia 자식에서만 표시하며 `STUB`/0 handle을 확인한다. 네이티브 OS 창·포커스·UIA·물리 키보드/마우스 입력은 금지한다.
- 로그와 산출물: `doc/verification/artifacts/p2-structure-clear/`에 원로그와 각 실행의 PNG/bounds를 보존했다. [신규9 직접 검토 세트](artifacts/p2-structure-clear/structure-clear-new9-review-final-01-artifacts.md)는 메인이 화면을 직접 검토한 근거이며, [전체129 자동 산출 세트](artifacts/p2-structure-clear/structure-clear-full129-final-01-artifacts.md)는 별도 실행에서 새로 생성한 증거다.
- 절차 감사: 2026-10-22 약 16:39, 16:54, 17:09 UTC에 전체 작업을 정지했다. 세 감사 모두 중단급 문제는 없었으며, 각 감사 종료 뒤 메인이 재개를 명시했다.

## 계약과 검증 범위

활성 위치는 전체 구조 선택이 있을 때 `SelectedNodeId`가 속한 행·그룹이며, 그 외에는 caret의 행·그룹이다. `EditingSession.Clear(MathClearScope)`는 `Selection`, `CurrentRow`, `CurrentGroup`, `Workspace`를 지원한다. 잘못된 enum 및 지울 내용이 없는 범위는 false/no-op이고 revision·history·dirty·caret·selection·workspace를 바꾸지 않는다. 빈 텍스트와 재귀적으로 빈 Sequence는 내용 없음으로 취급하지만 빈 슬롯만 가진 Fraction/Matrix 등 구조는 지울 수 있다.

선택 비우기는 기존 `DeleteSelection`의 텍스트/전체 구조 선택 의미를 따른다. 행·그룹·작업 비우기는 해당 행 root만 새 ID의 필수 빈 TextNode로 바꾸고 그룹/행 ID와 순서, 정렬·행 간격·스타일 및 비대상 구조를 보존한다. 활성 root에 caret 0을 두고 선택을 해제하며 성공은 단일 Commit이다. Undo/redo는 전체 workspace와 caret·selection·selected-node flag·StateId를 복원한다.

구조 투영은 각 행의 실제 root를 최상위에 두고, 자식은 MathTree의 preorder와 슬롯 순서를 유지한다. 투영 item의 `Id`, `MathNodeId`는 실제 `MathNode.Id`와 동일해야 하며 GUID 중복이 없어야 한다. `SelectNode(Guid)`만 전체 구조 선택과 reveal을 요청한다. `SelectedItem` 바인딩 setter의 null 및 동일/다른 non-null 값 반향은 세션 선택 상태를 변경하지 않아야 한다. 표면 caret·정/역 부분선택은 트리 대상을 갱신하되 전체 구조 선택으로 바꾸지 않는다.

`MainWindowViewModel.StructurePanel`은 Session 교체 뒤에도 같은 인스턴스를 유지한다. 범위 비우기 API는 root ViewModel 소유이며 `ClearScopeChoices`는 고정 선택지 목록이다. caret/선택 변경, Session 교체, projection 재생성, undo/redo 뒤에도 선택된 범위가 보존되는지 검사한다. 무효 범위 값은 기존 선택을 덮거나 모델을 바꾸면 안 된다.

## 논리 사례 8개

| ID | 검증 범위 |
|---|---|
| `STRUCT-PROJECTION` | 여러 그룹·행과 깊은 Fraction/Radical/Script/Matrix/Integral/Summation/Limit/Brackets/Cases 투영, root-per-row, preorder·슬롯 label·실제 ID 및 빈 슬롯. |
| `STRUCT-SELECT-PURITY` | 트리 선택·동일 항목 재선택/reveal, 없는 GUID 거절, null/동일 item 바인딩 echo, caret 및 정·역 부분선택 동기화, revision/history/dirty 불변, Session 교체 후 동일 panel 재사용. |
| `CLEAR-SELECTION` | Unicode grapheme의 정/역 부분선택과 전체 구조 선택 clear, 이웃 보존, caret 및 undo/redo 복원. |
| `CLEAR-ROW` | caret와 전체 구조 선택이 교차 행일 때 선택 노드가 속한 행만 clear, 같은 그룹의 앞/뒤 행과 IDs·설정 보존. |
| `CLEAR-GROUP` | caret와 구조 선택이 교차 그룹일 때 선택 구조 그룹만 clear, 행 순서/IDs·정렬/간격 보존. |
| `CLEAR-WORKSPACE` | 다중 그룹·행 전체를 빈 root로 바꾸되 그룹/행 식별자, 순서, 설정, style을 보존하고 활성 선택 행의 caret 위치를 확인. |
| `CLEAR-NOOP` | invalid enum, 빈 선택, 빈 Text/재귀 빈 Sequence의 완전 no-op 및 필수 빈 슬롯 구조의 clear 가능성. |
| `CLEAR-HISTORY` | 단일 Commit, root VM 고정 scope choice·설명·영향 행 수·CanExecute, undo/redo 선택 복원, 빈 필수 슬롯 SVG 거절 및 undo 후 동일 출력 경로 복구. |

## Headless UI 사례

UI 사례 계약은 `STRUCTURE-CLEAR-UI` / `--structure-clear-ui-headless STRUCTURE-CLEAR-UI`이며 실제 `App`/`MainWindow`를 owned Headless child에서만 사용했다. 범위는 구조 트리 항목 선택·reveal, clear scope ComboBox/설명/영향 행 수/CanExecute, 합성 클릭, clear·undo·redo의 바인딩·상태 동기화 및 1060×700·1360×880 셸 offscreen bounds/PNG다. 신규9 실행에서 초기·root 선택·populated 최소/기본·deep 최소/기본·leaf 선택의 7개 PNG/bounds를 만들었다. 메인은 신규9 실행의 17:13:50–17:13:52 UTC 세트를 직접 검토해 root/leaf 선택 표시, 깊은 header 전체 가시성, 최소/기본 Tree viewport 159px/339px, 폭52px 비우기 버튼의 세 글자 가시성 및 `STUB`/0를 확인했다. 전체129 실행에서는 같은 상태의 PNG/bounds 7개를 다시 자동 생성했지만, 해당 7개 전체를 메인이 직접 검토한 것으로 기록하지 않는다.

## 실행 기록

| 로그 | 결과 | 비고 |
|---|---|---|
| `build-structure-clear-final-01.log` | 실패, exit 1, 경고0·오류2 | `AetherTheme.axaml`의 `TreeViewItem.HorizontalContentAlignment`를 Avalonia가 해석하지 못했다(AVLN2000/AVLN2200). UI 소유자가 미지원 setter를 제거했다. |
| `build-structure-clear-final-02.log` | 성공, exit 0, 경고2·오류0 | UI 테스트 `StructureClearUiInteractionCases.cs`의 nullable 경고 2개(CS8602). 같은 빌드 기준 `structure-clear-new9-final-01.log`는 논리8/8 통과, UI는 header 폭이 Tree viewport를 넘어 실패(exit1). assertion 전에 캡처가 실행되지 않아 이 실패 run의 PNG/bounds는 없다. |
| `build-structure-clear-final-03.log` | 성공, exit 0, 경고0·오류0 | 경고와 header 너비 관련 최소 수정 후 재빌드. `structure-clear-new9-final-02.log` 역시 논리8/8, UI 실패(exit1): 180px header가 226px Tree의 오른쪽 경계를 15px 넘어갔다. 이 첫 캡처 전 실패에는 PNG/bounds가 없다. |
| `build-structure-clear-final-04.log` | 실패, exit 1, 경고0·오류1 | `MainWindow.axaml.cs`의 `Border.TranslatePoint` 확장 메서드가 해석되지 않은 CS1061. UI 소유자가 `using Avalonia;`를 추가했다. |
| `build-structure-clear-final-05.log` | 성공, exit 0, 경고0·오류0 | `structure-clear-new9-final-03.log`는 논리8/8 통과 후 최소창 Tree viewport 높이85px 검사에서 실패(exit1). 이 run은 초기·root 선택·populated 최소/기본의 4개 PNG/bounds를 만들었다. 실패 단계에는 캡처 파일이 없고, 빈 deep-minimum 폴더만 남았다. |
| `build-structure-clear-final-06.log` | 성공, exit 0, 경고0·오류0 | 하단 안내를 압축하고 Clear 버튼 폭/레이블 가시성을 개선한 최종 UI 동결본. |
| `structure-clear-new9-final-04.log` | 성공, exit 0, 9/9 | 논리8+Headless UI1 모두 통과. 메인이 이 실행의 7개 이미지/bounds 세트를 직접 검토했다. |
| `structure-clear-full129-final-01.log` | 성공, exit 0, 129/129 | 기준선120+신규9. 별도 자동 PNG/bounds 7세트를 만들었다. |

최종 실행의 주요 UI 개선 이력은 첫 두 UI 실행의 Tree header overflow, build04의 API 참조 컴파일 오류, 세 번째 UI 실행의 85px Tree viewport 제한에서 시작했다. 첫 세 실패를 통과시키려고 bounds/viewport 검사를 느슨하게 만들지 않았다. 최종 helper는 Tree 내부 ScrollViewer 가시성, 최소 Tree 높이100px, 전체 header containment 및 Clear 버튼 내부 label bounds 검사를 유지하고, MainWindow는 하단 정보 밀도를 줄여 최소창에서 공간을 확보했다.

Headless 합성 선택/클릭은 실제 OS 입력이 아니다. 네이티브 OS 창·포커스·UIA·물리 키보드/마우스·IME, 실제 사용자가 보는 화면과 사용자 AppData는 미검증 또는 미사용이다. Properties 전체, parser, 모든 OS/플랫폼 및 P2/P1/P0 전체 완료로 확대하지 않는다.
