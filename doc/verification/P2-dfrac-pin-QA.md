# P2 분수 Display pin 및 dfrac QA

- 상태: 신규 6건·기존 `TYPESETTING-UI` 단독 재검증·전체 171 회귀 및 메인 시각 검토 통과; 분수 Display pin/dfrac 하위 단위 통합 승인
- 기준선: 승인된 전체 회귀 165건
- 목표: 신규 논리 5건 + `DFRAC-PIN-UI` 1건, 전체 171건
- 최종 실행: build-03 경고 0/오류 0, 신규 6건 6/6, `TYPESETTING-UI` 단독 1/1, 전체 171건 171/171; 모든 단계 exit 0.
- 산출물 위치: [`artifacts/p2-dfrac-pin/`](artifacts/p2-dfrac-pin/)
- 실행별 결과와 직접 검토/자동 산출물 구분은 [최종 실행 manifest](artifacts/p2-dfrac-pin/dfrac-pin-final-manifest.md)를 참조한다.

이 범위는 이미 상속된 Display preset을 분수 노드 자체에 명시적으로 고정하는 동작을 검증한다. `FractionNode`에 자체 `StyleOverride.TypesettingStyle == Display`가 있을 때만 LaTeX에서 `\dfrac`을 사용하고, Workspace/Group/Row 상속 Display 및 Text/Script preset은 `\frac`을 유지한다. `\dfrac`의 문서상 사용에는 `amsmath`가 필요하며, [`amsmath.dtx` 612–627행](https://raw.githubusercontent.com/latex3/latex2e/main/required/amsmath/amsmath.dtx)은 이 계약의 참고 자료다. 이는 serializer 문자열 계약이며 외부 TeX 컴파일 결과를 뜻하지 않는다.

## 검증 계약

기존 `MathPropertiesPanelViewModel` 속성 `DraftPinTypesettingStyle`, `CanPinTypesettingStyle`, `TypesettingPinDisabledReason`, `TypesettingPinNotice`, `IsFractionSelection`, `FractionDisplayNotice`를 사용한다. pin은 `SelectionNode`, `CurrentRow`, `CurrentGroup` 대상에서만 허용되고, `WorkspaceDefault`에서는 비활성화된다. pin 의도와 기존 스타일 draft는 같은 captured target 검증과 단일 `MathPropertiesPatch` 적용을 따른다. preset draft가 선택되어 있으면 그 값을, “변경 안 함”이면 기준 effective preset을 대상 scope의 explicit override로 기록한다. 일반적인 같은 effective preset 선택은 변경이 아니지만 pin은 상속 관계를 대상에 명시하는 변경이다. 이미 조판 preset이 명시된 대상의 중복 pin은 허용되지 않는다.

stale target, scope 변경, 선택 변경 및 Session 교체에서는 pin draft를 보존하고 적용을 막는다. stale 상태에서는 분수 전용 안내가 이전 분수 문맥을 새 대상에 섞지 않도록 숨겨야 한다. 유효하지 않은 다른 스타일 필드가 함께 있으면 pin도 원자적으로 차단한다. 성공 적용과 override reset은 undo/redo 경계와 기존 스타일·역할·ID·caret·selection 보존을 확인한다. 부분 텍스트 선택도 해당 TextNode 전체 대상으로 역할과 네 스타일/pin을 한 commit으로 적용하고 원문·부분 선택·saved StateId를 Undo로 복원한다. 4개 스타일 override reset은 TextRole을 초기화하지 않는다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `PIN-APPLY` | SelectionNode/CurrentRow/CurrentGroup의 단일 pin 적용, 다른 스타일 필드와 결합, 각 scope의 문서 지문·StateId·dirty Undo/Redo 및 원래 foreground/family 복원, 기존 자식 override·역할·선택 상태 보존, 부분 Text 선택의 역할+4 스타일/pin 결합 적용과 Undo, 4필드 override reset. |
| `PIN-DRAFT` | 동일 effective 값의 일반 선택 no-op, 상속 Display pin, null preset 선택 시 baseline effective 값 pin, 중복 explicit pin 및 WorkspaceDefault 거절, invalid 필드 원자 차단, Fraction→다른 Fraction/Text·scope·Session 변경 뒤 stale pin 보존·적용 차단·문맥 안내 분리와 ResetDraft 복구. |
| `DFRAC-LATEX` | Fraction-local explicit Display만 `\dfrac`; Workspace/Group/Row 상속 Display와 local Text/Script는 `\frac`; 실제 중첩 Display fraction의 ScriptScript child, 뒤 sibling 범위 격리 및 필수 빈 슬롯의 소스·진단 ID. |
| `DFRAC-IO` | 분수/행 복제의 새 ID와 explicit override·TextRole 보존, 실제 TempDirectory 파일 저장/열기 및 `WorkspaceStateService` recovery 저장/읽기에서 fingerprint 보존. |
| `DFRAC-LAYOUT-PREVIEW` | 이미 유효한 Display를 pin해도 frozen layout이 달라지지 않는 경계, 중첩 local Display, Inline projection에서 local override 우선, exporter에 같은 `LayoutResult` 인스턴스를 전달하고 SVG 파일의 크기·문자열을 확인. |

## Headless UI 및 실행 경계

UI case는 `DfracPinUiInteractionCases.All()`의 `DFRAC-PIN-UI`, child route `--dfrac-pin-ui-headless DFRAC-PIN-UI`를 사용한다. `Program.cs`에는 Headless/Skia child 초기화와 라우트, 논리·UI 등록이 연결되어 있다. helper의 artifact scope는 `p2-dfrac-pin`이다. UI assertion은 pin 상태와 안내 의미를 확인하며 문구 전체의 고정 문자열에 의존하지 않는다. 분수 안내는 UI의 TextBlock 값과 VM 값이 일치하고 `amsmath`, `\frac`/`\dfrac` 의미가 드러나는지 확인한다.

파일 저장과 복구는 테스트 임시 디렉터리에만 수행한다. UI는 전용 owned Headless child의 실제 `App`/`MainWindow`와 `STUB`/0 platform handle 경계를 사용한다. 실제 OS 앱 입력·포커스·UIA·클립보드·파일 선택기·사용자 AppData·computer-use 및 외부 TeX 실행은 범위 밖이다.

## 실행 결과 및 산출물

2026-10-22 20:39 UTC 감사 종료 뒤, 전원 소스 동결과 메인 정적 리뷰를 전제로 승인된 순서로 QA 단독 실행했다. 20:40 UTC경 Debug 빌드가 경고 0/오류 0, exit 0으로 끝났다. [빌드 원로그](artifacts/p2-dfrac-pin/build-dfrac-pin-final-01.log)

신규 6건 실행 결과는 5/6 통과, exit 1이다. `PIN-APPLY`, `PIN-DRAFT`, `DFRAC-LATEX`, `DFRAC-IO`, `DFRAC-LAYOUT-PREVIEW`는 통과했고, `DFRAC-PIN-UI`는 `ApplyPropertiesButton`의 viewport 여유폭 검사에서 실패했다. 실패 시점의 bounds는 `0, 0, 36, 30`, 화면 원점은 `791, 563`, viewport 크기는 `234 × 387`, 원점은 `791, 214`, scroll offset은 `0, 1126.5`, extent는 `234 × 1587`이었다. [신규 6건 원로그](artifacts/p2-dfrac-pin/dfrac-pin-new6-final-01.log)

실패 원인의 정적 진단은 UI helper의 세로 여유 8 검사에 `Rect.Deflate(8)`를 사용해 가로 여유까지 적용한 것이며, 이로 인해 정상적인 왼쪽 정렬 Apply 버튼을 거부한 것이었다. font 소유자는 가로 strict viewport containment와 세로 8 DIP 여유를 분리하고, 스크롤 중앙 이동/hover 뒤 실제 클릭 좌표 확인 및 실패 전 PNG/bounds 저장을 보강했다. 체크 라벨도 짧게 바꿨다. 제품 동작 수정은 없었다.

이번 실행에서 PNG와 `bounds.json`이 각각 6개 생성됐다. 메인은 `dfrac-pin-draft-minimum` PNG 한 장만 확인했으며, 이는 전체 시각 검토나 시각 승인에 해당하지 않는다.

| 캡처 | PNG | Bounds |
|---|---|---|
| 초기 최소 창 위 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-initial-minimum-top-1060x700-20261022T204015478Z-ec8e19ea84a84b4e9adf6ae4e89a2f5b/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-initial-minimum-top-1060x700-20261022T204015478Z-ec8e19ea84a84b4e9adf6ae4e89a2f5b/bounds.json) |
| 초기 최소 창 아래 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-initial-minimum-bottom-1060x700-20261022T204015533Z-2ff990171d70454f8d68a95e6e75cf46/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-initial-minimum-bottom-1060x700-20261022T204015533Z-2ff990171d70454f8d68a95e6e75cf46/bounds.json) |
| 초기 기본 창 위 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-initial-default-top-1360x880-20261022T204015579Z-e6e107b16cc74c66a8ac2a66c38cea26/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-initial-default-top-1360x880-20261022T204015579Z-e6e107b16cc74c66a8ac2a66c38cea26/bounds.json) |
| 초기 기본 창 아래 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-initial-default-bottom-1360x880-20261022T204015616Z-6f7136b34bed49458f534d3a32e8dad3/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-initial-default-bottom-1360x880-20261022T204015616Z-6f7136b34bed49458f534d3a32e8dad3/bounds.json) |
| 분수 상세 최소 창 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-detail-minimum-1060x700-20261022T204015674Z-3734b6ab28d74a19baa47d7956104d86/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-detail-minimum-1060x700-20261022T204015674Z-3734b6ab28d74a19baa47d7956104d86/bounds.json) |
| pin 초안 최소 창 | [main-window.png](artifacts/p2-dfrac-pin/dfrac-pin-draft-minimum-1060x700-20261022T204015747Z-2253225b1fbd439995ab18ba4c91a8d0/main-window.png) | [bounds.json](artifacts/p2-dfrac-pin/dfrac-pin-draft-minimum-1060x700-20261022T204015747Z-2253225b1fbd439995ab18ba4c91a8d0/bounds.json) |

재빌드 [build-02](artifacts/p2-dfrac-pin/build-dfrac-pin-final-02.log)는 경고 0/오류 0, exit 0이었다. [new6-02](artifacts/p2-dfrac-pin/dfrac-pin-new6-final-02.log)는 논리 5건과 `DFRAC-PIN-UI`를 포함해 6/6 통과, exit 0이었다. 메인은 new6-02에서 생성한 PNG/bounds 12쌍을 직접 검토했다. 해당 시각 게이트에서 화면은 STUB/0, 속성 패널 224 DIP, 중앙 편집 viewport는 최소 창 257 DIP·기본 창 437 DIP였고, 상속/명시값, dfrac 안내, stale, WorkspaceDefault 비활성 표시가 맞았다. 이 신규 실행의 시각 게이트는 통과했지만 전체 회귀 통합 승인은 보류됐다.

new6-02 직접 검토 세트의 각 디렉터리에는 `main-window.png`와 `bounds.json`이 있다: `dfrac-pin-initial-minimum-top-1060x700-20261022T204435736Z-bcf336ab364a462c82b61cbc15c20e3d`, `dfrac-pin-initial-minimum-bottom-1060x700-20261022T204435790Z-f4593487af5b4cc9922605a4590403f0`, `dfrac-pin-initial-default-top-1360x880-20261022T204435832Z-346044d28f7546e4ab18193e5c0e7347`, `dfrac-pin-initial-default-bottom-1360x880-20261022T204435869Z-1383ea824d57486f8287cba3092234a0`, `dfrac-pin-detail-minimum-1060x700-20261022T204435928Z-f062395a398d47ec88982cb3e402a625`, `dfrac-pin-draft-minimum-1060x700-20261022T204436001Z-30a9efd8ccbd4d50b96ea0469da3d9c0`, `dfrac-pin-applied-minimum-1060x700-20261022T204436105Z-1070d41a85cc4a50b4b39f4812528d93`, `dfrac-pin-applied-default-1360x880-20261022T204436133Z-064a5275621444e7938cb90673290588`, `dfrac-pin-undo-minimum-1060x700-20261022T204436243Z-4a08032d5a414c93ba94a0502a6ea68a`, `dfrac-pin-stale-bottom-1060x700-20261022T204436776Z-a7303838c2bb4128a4f75acc20db6a5c`, `dfrac-pin-workspace-disabled-minimum-1060x700-20261022T204436870Z-9afa50760c2648369dff04585c3fdbb6`, `dfrac-pin-workspace-disabled-default-1360x880-20261022T204436899Z-5e576bc8832f444ea9180ff0a5f3870e` under `artifacts/p2-dfrac-pin/`.

이후 [전체 171건 원로그](artifacts/p2-dfrac-pin/dfrac-pin-full171-final-01.log)는 170/171, exit 1로 끝났다. 유일한 실패는 Dfrac pin 사례가 아니라 기존 `TYPESETTING-UI`에서 `조판 스타일 선택 ComboBox가 실제 viewport에서 잘립니다`라는 assertion이다. stack trace는 `TypesettingUiInteractionCases.SelectTypesettingStyleAsync`와 caller `RunWindowChecksAsync`를 가리킨다. 따라서 전체 회귀는 통과하지 않았고 Dfrac 단위의 통합 승인도 보류 상태다. full171에서 자동 생성된 Dfrac UI 캡처 12쌍은 new6-02 직접 검토 세트와 분리해 보존했다. 각 디렉터리에 `main-window.png`/`bounds.json`이 있다: `dfrac-pin-initial-minimum-top-1060x700-20261022T204643009Z-5b60666b56084c2cbc599d256952f01a`, `dfrac-pin-initial-minimum-bottom-1060x700-20261022T204643063Z-087c8533c4c14b8d8d54fc8ddabd0e86`, `dfrac-pin-initial-default-top-1360x880-20261022T204643091Z-0510482e8b9b43c1b3e269e169c3db9b`, `dfrac-pin-initial-default-bottom-1360x880-20261022T204643130Z-f642913018734fd5a8ca5fffe9216521`, `dfrac-pin-detail-minimum-1060x700-20261022T204643189Z-9f1143d7efc54035a2c1978591842951`, `dfrac-pin-draft-minimum-1060x700-20261022T204643260Z-d6bf2a1c80564c2e8c478711be5b0d04`, `dfrac-pin-applied-minimum-1060x700-20261022T204643360Z-afa4e9b932bd43a5b89da926a803fc8c`, `dfrac-pin-applied-default-1360x880-20261022T204643386Z-56c89f0457724f4e877ea90e7bc05cbe`, `dfrac-pin-undo-minimum-1060x700-20261022T204643489Z-d7883696dbea4860968b388cc680818c`, `dfrac-pin-stale-bottom-1060x700-20261022T204644026Z-201ceab0507342008daaf7cffc198039`, `dfrac-pin-workspace-disabled-minimum-1060x700-20261022T204644119Z-b8e03e1b32354d2fac2ba682a393b82c`, `dfrac-pin-workspace-disabled-default-1360x880-20261022T204644148Z-cd90ac6bb7c54e8eaf15c158de94e3bc` under `artifacts/p2-dfrac-pin/`.

font 소유 helper 수정 후 [build-03](artifacts/p2-dfrac-pin/build-dfrac-pin-final-03.log)은 경고 0/오류 0, exit 0이었다. 기존 실패 사례의 단독 [TYPESETTING-UI 재검증](artifacts/p2-dfrac-pin/dfrac-pin-typesetting-ui-final-01.log)은 1/1 통과, exit 0이며 비교 증거는 [조판 비교 JSON](artifacts/p2-typesetting/equation-style-comparison-20261022T205322899Z-e35f57cc3a3045788b0c5d13dd44a8f3/equation-typesetting-comparison.json)에 있다. 이 단독 재검증은 당시 전체171 실패를 대체하지 않았으나, 그 뒤 승인된 [전체171-02](artifacts/p2-dfrac-pin/dfrac-pin-full171-final-02.log)가 171/171·exit 0으로 통과해 해당 전체 회귀 대기도 끝났다.

## 최종 실행 및 승인

2026-10-22 20:54 UTC 감사는 절차상 정지였으며 검증 결과 근거가 아니다. 감사 종료 뒤 build-03 바이너리로 전체171 회귀를 재실행했다. [build-03](artifacts/p2-dfrac-pin/build-dfrac-pin-final-03.log)은 경고 0/오류 0·exit 0, [new6-02](artifacts/p2-dfrac-pin/dfrac-pin-new6-final-02.log)은 6/6·exit 0, [TYPESETTING-UI 단독](artifacts/p2-dfrac-pin/dfrac-pin-typesetting-ui-final-01.log)은 1/1·exit 0, [full171-02](artifacts/p2-dfrac-pin/dfrac-pin-full171-final-02.log)은 171/171·exit 0이었다. 이전 [full171-01](artifacts/p2-dfrac-pin/dfrac-pin-full171-final-01.log)의 170/171·exit 1은 기존 `TYPESETTING-UI` ComboBox viewport assertion 실패로 남겨 두었다. font가 legacy helper를 수정한 뒤 단독 TYPESETTING-UI 재검증과 전체 회귀가 통과했다.

메인은 new6-02의 PNG/bounds 12쌍을 직접 검토해 분수 pin의 상속·명시·초안·적용·undo, stale 차단, WorkspaceDefault 비활성 안내, STUB/0, 속성 패널 224 DIP 및 중앙 viewport 257/437 DIP를 확인했다. 같은 실행의 직접 검토 자료는 [manifest](artifacts/p2-dfrac-pin/dfrac-pin-final-manifest.md)에 연결했다. TYPESETTING-UI 단독 실행의 메인 검토 범위는 20:53:22–20:53:24 UTC 생성한 상세 Display 최소창, 행 Script 최소창, stale 최소창 PNG 3개와 해당 실행 bounds 10개다. full171-02에서 생성된 Dfrac 캡처 12쌍은 별도 자동 산출물이며 new6-02의 직접 검토 자료로 간주하지 않는다.

이로써 분수 자체에 Display를 명시 적용하는 pin과 `\dfrac` 출력 경계의 논리·Headless·offscreen 하위 단위가 통합 승인됐다. 외부 TeX 실행, 실제 OS 입력/포커스/UIA, clipboard, 사용자 AppData, computer-use는 검증하지 않았으며 P2/P1/P0 전체 완료를 뜻하지 않는다.

2026-10-22 20:24, 20:39, 20:54 UTC 감사는 절차상 안전 정지였으며 테스트 합격 근거가 아니다. 모두 중단급 위반 없이 종료됐고 각 종료 뒤 명시 재개됐다.

