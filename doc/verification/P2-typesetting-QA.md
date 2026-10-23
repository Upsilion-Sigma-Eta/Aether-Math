# P2 조판 preset QA 기록

- 상태: 통합 승인 — 논리/Headless/offscreen 범위
- 기준선: 앞서 승인한 전체 회귀 138건
- 결과: Debug 빌드 경고 0·오류 0, 신규 9/9, 전체 147/147, 각 단계 exit 0
- 근거 문서: [개발 계획서](../AetherMath_개발_계획서.md) §3–§4, §12.3
- 산출물: [`artifacts/p2-typesetting/`](artifacts/p2-typesetting/)의 단계별 로그와 실행별 PNG/bounds manifest

이 단위는 Display/Text/Script/ScriptScript 조판 preset의 상속·전이·레이아웃과 LaTeX/SVG 소비 경계를 검증했다. 메인은 최종 소스·로그와 신규 실행 화면을 검토해 이 slice를 통합 승인했다. 외부 Display/Inline 출력 옵션은 별도 후속 단위이며, 이 승인은 P2 전체나 P0/P1 전체 완료를 뜻하지 않는다.

## 고정 범위

`MathTypesettingStyle` 네 값과 `MathStyle`/`MathStyleOverride`의 schema v1 additive 필드, `MathPropertiesPatch`의 4필드 단일 적용, 기존 세 필드 `ApplyMathStyle` 호환 wrapper, 4필드 override 초기화를 확인한다. 상속 출처는 Workspace→Group→Row→조상 node→대상 node 순서다. 누락된 legacy root preset은 Display, 누락된 override는 상속(null)이며 알 수 없는 enum 값은 거부한다.

UI의 “변경 안 함” 항목은 Avalonia null `SelectedValue` 처리에 기대지 않고 안정된 `SelectedTypesettingStyleChoice` 항목으로 표시한다. DraftTypesettingStyle의 null 의미와 combined patch는 그대로 유지한다.

레이아웃 계약은 Display 분수 자식이 Text 수준의 같은 크기이고 Text 분수와 Script 자식은 다음 수준으로 줄어드는 것, Matrix/Cases 자식이 compact 기준을 쓰되 같은 수준을 유지하는 것, 깊이 스케일 `.71^depth`와 1px 하한이다. 명시 FontSize는 해당 node의 절대 크기이며 그 아래 구조 전이는 그 값에 상대 적용한다. 더 얕은 preset으로 들어갈 때는 유효 크기를 보존하고 새 기준 수준에서 후속 첨자를 계산한다. 큰 유효 workspace 크기를 임의 상한으로 자르지 않는다.

합·극한은 Display에서 상하 한계를, compact에서 옆 한계를 쓰고 적분 한계는 두 preset 모두 옆에 둔다. compact 합은 한계가 연산자 높이와 세로로 교차하면서 서로의 bounds와 겹치지 않아야 하며 전체 구조 bounds가 큰 명시 한계와 자식을 포함해야 한다. Display 큰 연산자와 Text compact 실제 높이도 비교한다. 화면·SVG 비교는 같은 `LayoutResult` 소비를 검사한다.

LaTeX는 schema와 별개로 preset을 명령과 국소 그룹으로 보존한다. Equals 정렬의 `&=`는 style group 밖에 남고, 각 행은 유효 row preset으로 독립 출력한다. 자동 fraction/script child preset과 명시 node group을 정확한 문자열로 비교하며 명시 node preset이 다음 sibling에 새지 않는지 확인한다. 기본 Display 명령을 포함한 전체 aligned 문자열을 검사하고 외부 TeX 컴파일은 이 단위에서 요구하지 않는다. 기존 크기·글꼴·색 진단은 유지하고 조판 preset 자체를 미표현 속성으로 잘못 보고하지 않는지도 확인한다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `TYPE-CASCADE` | 네 preset의 Workspace/Group/Row/Node 유효값과 `MathStyle` 출처·소유자 우선순위 |
| `TYPE-APPLY-RESET` | 네 필드 단일 Apply/Undo/Redo, 명시 동일값 no-op, 잘못된 patch 무변경, 기존 3필드 wrapper 보존, 전체 reset과 WorkspaceDefault 적용 |
| `TYPE-JSON` | schema v1 roundtrip, preset 필드가 없는 legacy 기본값/nullable 상속, 알 수 없는 enum 거절과 원본 bytes 보존 |
| `TYPE-FRACTION` | Display와 Text 분수 자식의 수준 전이·크기 및 위/아래 배치 |
| `TYPE-NESTED` | Script 자식 축소, Matrix/Cases compact 동일 수준, 512px 위/아래첨자 단독·쌍의 부모/layout bounds 포함, base와의 2D 비겹침 및 쌍 사이 세로 간격 |
| `TYPE-DEPTH` | `.71` 반복·1px 하한, 명시 크기 이후 상대 축소, 상위 Script 크기 anchor 뒤 Text preset 재설정, 1,500,001 크기의 유효 workspace 기본값 보존 |
| `TYPE-OPERATORS` | Display/compact 합·극한·적분 한계, compact 합의 실제 전체 높이 감소와 옆 배치/세로 교차/비겹침, 양한계 및 상한·하한 단독 큰 한계의 부모/layout bounds 포함, hit-test의 Session 불변성 |
| `TYPE-LATEX-SVG` | multi-row `aligned`의 정확한 행별 명령 및 정렬자 위치, 명시 node scope와 다음 sibling 격리, 자동 child transition, 동일 layout의 SVG 순서·크기·글꼴 |

## UI/실행 경계

UI 담당 테스트는 `TypesettingUiInteractionCases.All()`의 `TYPESETTING-UI`, 전용 `--typesetting-ui-headless` 자식 분기와 `RunHeadlessChildAsync(string)`를 사용한다. `Program.cs`에 case 등록과 child route가 연결되어 있다. actual `App`/`MainWindow`는 소유 Avalonia.Headless+Skia child에서만 사용하고 표시 전후 `STUB`/0 platform handle을 확인한다. 창·OS focus·UIA·물리 키보드/마우스·클립보드·파일 선택기·사용자 AppData 및 컴퓨터 사용 도구는 사용하지 않는다. `TempDirectory`만 파일 I/O에 사용한다.

전용 owned Headless child에서 실제 `App`/`MainWindow`와 Skia를 사용했고 표시 전후 platform handle은 `STUB`/0이었다. 파일 I/O는 테스트 `TempDirectory`에 한정했다. 네이티브 OS 창·포커스·UIA·물리 키보드/마우스·클립보드·파일 선택기·사용자 AppData·computer-use는 사용하지 않았다.

## 실행 및 검토 결과

| 단계 | 결과와 로그 |
|---|---|
| 최종 Debug 빌드 | `build-typesetting-final-05.log`: 경고 0·오류 0, exit 0. |
| 신규 slice | `typesetting-new9-final-03.log`: 9/9, exit 0. |
| 전체 회귀 | `typesetting-full147-final-03.log`: 147/147, exit 0. 전체 회귀의 첫 실행은 `typesetting-full147-final-02.log`이며 `final-01` 전체 실행 로그는 없다. |

이전 실패는 원로그와 함께 보존했다. `build-typesetting-final-01.log`는 CS0173 1건, 경고 0·오류 1·exit 1이었다. `build-typesetting-final-02.log`는 QA fixture CS0029와 UI nullable/`Length` 컴파일 오류로 경고 1·오류 2·exit 1이었다. `build-typesetting-final-03.log`와 `-04.log`는 경고·오류 없이 통과했다. `typesetting-new9-final-01.log`는 TYPE-JSON의 group/row 기대 위치 오류, TYPE-OPERATORS의 Display 상·하한 측면 배치 오기대, TYPESETTING-UI의 부분 선택 안내 단언 실패로 6/9·exit 1이었다. 새 UI 단언 수정 뒤 `typesetting-new9-final-02.log`는 9/9·exit 0이었다.

`typesetting-full147-final-02.log`는 141/147·exit 1이었다. 기존 LaTeX 회귀 6건(`LATEX-NESTED-SCRIPT`, `ROW-IO`, `GROUP-IO`, `TPL-BOUNDS`, `TPL-BRACKETS-CASES`, `TPL-LATEX`)이 새 Display/Script scope 명령 및 grouped 출력과 맞지 않는 이전 기대값을 사용했다. QA는 독립 하위식 조합 기대를 실제 중첩 수학 문맥의 정확한 문자열로 바꾸고, 행·그룹 순서 토큰에 Display 명령을 포함했다. optional bound·Cases 식/조건 분리·10종 구조 의미 검사는 그대로 두고 bound와 Cases의 정확한 scope group도 확인했다. 이후 `typesetting-new9-final-03.log`와 `typesetting-full147-final-03.log`가 통과했다.

메인의 직접 시각 검토 근거와 자동 생성물은 서로 구분한다.

- 신규9-02 실행(2026-10-22 18:31:28–18:31:29 UTC): 셸 PNG 10개와 bounds 10개, 비교식 PNG 4개와 JSON을 메인이 직접 검토했다. 초기/확인 화면, 조판 비교, 최소·기본 창의 STUB/0와 배치를 확인했다.
- 신규9-03 실행(18:36:24 UTC): 메인은 `final-default-top`, `detail-row-script-minimum`, `stale-minimum-bottom` PNG 3개와 같은 실행의 bounds 10개를 직접 검토했다. 대상 설명이 그룹 1·5행으로 갱신되는 점, stale 사유, Apply 비활성/ResetDraft 활성, STUB/0, 패널 224px, viewport 234px와 중앙 편집 영역 257px/437px을 확인했다. 같은 실행의 별도 조판 비교 PNG 4개와 JSON은 [신규9-03 비교 산출물](artifacts/p2-typesetting/equation-style-comparison-20261022T183623369Z-ecddc61407644eaf8b619e62e74f380b/equation-typesetting-comparison.json)에 있다.
- 전체147-03 실행(18:37 UTC): 셸 PNG 10개·bounds 10개와 비교식 PNG 4개·JSON이 자동 생성됐다. 이는 신규 실행과 별도 세트이며 메인이 전체 이미지 14개를 모두 직접 검토했다는 근거가 아니다. 자세한 경로는 [전체147-03 산출물 manifest](artifacts/p2-typesetting/typesetting-full147-final-03-artifacts.md)에 기록했다.

18:09, 18:24, 18:39 UTC Watchdog 감사 동안 작업을 정지했으며 중단급 문제는 없었다. 각 감사 종료 후 메인의 명시적 재개를 받아 진행했다. TargetSummary 알림 누락은 engine이 baseline capture 성공 경로에 알림을 추가해 수정했고, UI 단언은 실제 TextBlock.Text와 `TargetSummary`의 일치를 확인하도록 보강됐다.

## 미완료 및 검증 한계

외부 Display/Inline 출력 옵션과 해당 미리보기 QA, 직접 LaTeX 입력/parser, 외부 TeX 프로그램 컴파일, 구버전 앱 런타임, 실제 OS 글꼴·입력·포커스·IME·UIA·사용자 AppData는 이 단위에서 검증하지 않았다. 이는 전체 P2/P1/P0 완료 선언이 아니다. 다음 QA 경계는 외부 삽입 Display/Inline 미리보기이며, 현재는 읽기 전용 계약 제안 단계다.

2026-10-22 18:09, 18:24, 18:39 UTC 정기 감사 동안 전원은 편집·검증을 안전하게 정지했고 중단급 문제는 없었다. 각 감사 종료 후 명시 재개를 받은 뒤 작업했다. 이 절차 기록은 테스트 합격 근거가 아니다.
