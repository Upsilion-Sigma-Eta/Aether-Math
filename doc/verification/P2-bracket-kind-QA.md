# P2 괄호 종류 속성 QA

- 상태: 괄호 종류 편집 하위 단위가 논리·Headless·offscreen 범위에서 통합 승인됐다.
- 기준선: 승인된 전체 회귀 191건.
- 결과: Debug build02 경고 0·오류 0·exit 0, 신규 논리 6건 + `BRACKET-KIND-UI` 1건 7/7·exit 0, 전체 198/198·exit 0.
- UI child 계약: `--bracket-kind-ui-headless BRACKET-KIND-UI`.
- 산출물 위치: `doc/verification/artifacts/p2-bracket-kind/`.
- 2026-10-22 23:09 UTC 감사는 절차 점검으로 기록하며 테스트 실행이나 제품 검증 근거로 사용하지 않는다.

## 고정 API 계약

Core 종류는 기존 `MathBracketKind`의 `Parentheses`, `Square`, `Curly`, `Angle` 네 값이다. `MathPropertiesPatch`에는 후행 nullable `MathBracketKind? BracketKind = null`을 추가하며, null은 변경하지 않음을 뜻한다. `SelectionNode`의 snapshot이 `IsNodeSelected == true`, `SelectedNodeId == TargetNodeId`이고 실제 target이 `BracketsNode` 전체 선택일 때만 kind 변경이 가능하다. 내부 caret/부분 텍스트 선택, 다른 scope 및 Brackets가 아닌 대상은 `UnsupportedScope`다. stale 검사가 기존 우선순위를 따르고 잘못된 enum은 `InvalidValue`로 거절한다. 실패한 kind patch는 결합된 글꼴·크기·색·조판·역할·간격 변경도 모두 차단한다. 같은 kind만 요청한 경우 `NoChange`이며 이력/dirty 상태를 바꾸지 않는다.

종류 변경은 BracketsNode의 기존 `Content` subtree를 유지하고 `with { Kind = ... }` 의미로 한 commit에 적용한다. 내용의 모든 node ID, Text role/style, Brackets style, caret·selection·selected-node 상태를 보존한다. 다른 style/preset과 kind가 결합된 Apply는 단일 Undo/Redo 경계이며 `ResetMathStyleOverride`는 4개 style override만 초기화하고 BracketKind는 보존한다.

패널 선택지는 null(변경 안 함), Parentheses, Square, Curly, Angle 순이다. 표시명에는 종류별 기호가 포함되며 `CurrentBracketKindLabel`은 짧은 한국어 이름을 유지한다. `CurrentBracketKind`/label은 현재 대상을 표시하고, `DraftBracketKind` null은 미변경이다. 같은 kind 초안은 `CanApply=false`와 session/history 무변경으로 확인하며 draft의 null 정규화는 요구하지 않는다. dirty 초안은 target/scope/session 이동 뒤 원래 baseline에 대한 draft로 보존되어 stale 적용이 차단되어야 한다. ResetDraft는 새 baseline을 수집한다. 저장된 선언은 `MathBracketKind? BracketKind = null`, `MathBracketKindChoice? SelectedBracketKindChoice`, `MathBracketKind? DraftBracketKind`, `MathBracketKind? CurrentBracketKind`이며 코드에서 확인했다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `BRACKET-KIND-CORE` | null/same-kind no-op의 revision·StateId·dirty·undo/redo·선택 불변, 네 종류 유효 변경, invalid enum 및 결합 patch의 원자 거절, stale 우선순위를 검사한다. explicit whole BracketsNode 선택만 허용하고 whole Text/구조의 내부 caret·부분 선택·다른 scope/비괄호 target은 거절한다. |
| `BRACKET-KIND-APPLY` | kind와 style/preset 결합 단일 commit, ID·Content subtree·Text Role/style·caret·selection 보존, Undo/Redo 및 style override reset 뒤 kind 유지. 기존 명시 style 적용 뒤 kind 변경도 검사해 값 덮어쓰기를 잡는다. |
| `BRACKET-KIND-VM` | canonical choices 및 label, null/same-kind draft no-op, whole Brackets target 편집 가능성, dirty draft의 selection/content/scope/session 변경 후 stale 보존·차단과 ResetDraft 재수집을 검사한다. |
| `BRACKET-KIND-IO` | 네 종류의 schema v1 JSON 및 실제 파일/recovery 왕복, clone 후 kind 유지·새 clone ID·content의 구조/Role/style 보존을 검사한다. 저장 JSON의 별도 변형 사본으로 잘못된 enum/알 수 없는 필드 거절을 확인한다. |
| `BRACKET-KIND-LATEX` | 네 괄호 종류의 정확한 LaTeX delimiter와 delimiter 사이 content 출력 순서를 확인한다. 필수 빈 content의 slot diagnostic과 preview SVG 저장 차단을 검사한다. 외부 TeX 실행이나 별도 LaTeX export command 검증은 하지 않는다. |
| `BRACKET-KIND-LAYOUT-PREVIEW` | 네 delimiter의 layout primitive/geometry 차이와 content bounds를 확인하고, 같은 frozen layout을 Ready preview 및 실제 SVG 파일로 소비하는 경계를 검사한다. preview Layout의 content node ID, SVG glyph 문자열/순서·좌표, 네 모양의 실제 출력 차이를 대조한다. |

## Headless UI 및 실행 결과

font 담당 UI 계약은 `BracketKindUiInteractionCases.All()`/`RunHeadlessChildAsync(string)`, case `BRACKET-KIND-UI`, child flag `--bracket-kind-ui-headless`, artifact scope `p2-bracket-kind`다. `Program.cs`의 Headless 초기화 분기, 전용 child dispatch, 일반 case 목록의 논리6/UI1 연결을 실제 실행했다. UI는 실제 `SelectedItem` 컨트롤 binding을 검증하며 팝업 포인터 동작이나 실제 OS 입력은 검증하지 않는다.

첫 build01은 UI 사례 파일 `BracketKindUiInteractionCases.cs`의 누락된 `Find` helper로 CS0103 16건, 경고 0·오류 16·exit 1이었다. font가 helper를 추가한 뒤 build02는 경고 0·오류 0·exit 0으로 통과했다. 신규7-01은 7/7·exit 0, 전체198-01은 198/198·exit 0이다. 원로그는 [build01](artifacts/p2-bracket-kind/build-bracket-kind-final-01.log), [build02](artifacts/p2-bracket-kind/build-bracket-kind-final-02.log), [신규7](artifacts/p2-bracket-kind/bracket-kind-new7-final-01.log), [전체198](artifacts/p2-bracket-kind/bracket-kind-full198-final-01.log)에 보존한다.

메인은 신규7-01의 2026-10-22 23:18:07–23:18:08 UTC UI 세트에서 PNG 10개를 직접 검토했다: 네 종류 detail-minimum, initial-minimum-bottom, initial-default-bottom, stale-minimum, unsupported-partial-selection-minimum, detail-style-reset-minimum, initial-default-top. 같은 실행의 bounds 15개에서는 viewport·visible editor·STUB/0·카드/선택기/사유 좌표와 kind/draft/stale 핵심 필드를 확인했으며 모든 필드를 읽은 것은 아니다. 괄호 종류 현재값·비활성 사유, stale Curly 초안 보존, ResetOverride 뒤 Angle 보존, editor viewport 257/437px을 검토했다. 나머지 신규 PNG 5개는 직접 리뷰 범위에 포함되지 않는다.

전체198-01의 UI 자동 캡처 15개 PNG와 bounds 15개는 2026-10-22 23:20:16.394–23:20:17.878 UTC에 생성된 별도 자동 산출물이며 메인이 직접 시각 검토한 신규7 세트와 구분한다. 메인은 이 전체 실행 산출물 전부를 검토하지 않았다.

이 승인은 논리·Headless·offscreen 범위에 한정한다. 허용 범위는 TempDirectory와 소유 Headless/Skia child의 `STUB`/0이다. 실제 OS 창·포커스·물리 입력·IME·clipboard·UIA·사용자 AppData·computer-use 및 실제 TeX 실행은 포함하지 않는다.

## 범위 한계

이번 하위 단위는 기존 괄호 구조의 네 종류 선택·표시·소비 경계에 한정한다. 괄호 숨김, 한쪽 또는 좌우 분리 표시, 크기 직접 조절은 포함하지 않는다. 괄호 구조 전체 편집 정책, 직접 LaTeX 입력/parser, 외부 TeX 컴파일, 실제 OS 입력 및 전체 P2/P1/P0 완료로 확대하지 않는다.
