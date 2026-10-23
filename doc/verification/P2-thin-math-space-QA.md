# P2 얇은 수학 공백 QA

- 상태: build07 경고0·오류0·exit0이다. 별도 신규 실행 `new10-01`은 논리9 통과·UI1 실패로 9/10·exit1이었다. 이후 UI03은 1/1·exit0, 전체191은 191/191·exit0이며 전체191 안에서 신규10도 모두 PASS했다. 메인의 직접 검토 후 얇은 공백 하위 단위는 논리·Headless·offscreen 범위에서 통합 승인됐다. 전체-run 자동 캡처는 UI03 직접 검토 세트와 별도 보존한다.
- 기존 기준선: 승인된 전체 회귀 181건.
- 목표: 신규 논리 9건 + `THIN-MATH-SPACE-UI` 1건, 전체 191건.
- UI child: `--thin-math-space-ui-headless THIN-MATH-SPACE-UI`.
- 산출물 위치: `doc/verification/artifacts/p2-thin-math-space/`.
- 범위: 명시적 얇은 수학 공백 모델·편집·레이아웃·직렬화·복구 및 출력 소비 경계.

이번 기능은 원문 `TextNode.Text`에 들어 있는 U+2009와 구분되는 `MathSpaceNode(MathSpaceKind.Thin)`를 추가한다. schema는 v1을 유지하며 새 `$kind: "mathSpace"` discriminator와 `MathNodeKind.MathSpace`를 추가한다. 공백은 수학 내용으로는 비어 있지만 Clear에서는 제거할 내용이다. 이 구분을 필수 콘텐츠 검증, Clear, preview/SVG 출력 경계에서 다룬다. 기준선 181건에 논리9건과 Headless UI 1건을 더한 전체191이 통과했으며, 승인 범위와 한계는 아래 실행 이력에 구분해 기록한다.

## 고정 계약

`MathSpaceNode`는 유효한 `MathSpaceKind.Thin`만 받아야 한다. JSON에서 discriminator·kind·전역 ID 검증은 기존 strict 규칙을 유지한다. bare spacer 또는 spacer만 있는 Sequence는 필수 root/slot의 실질 내용이 아니므로 거절하고, marked-empty Text anchor와 함께 있는 spacer는 허용한다. 이 경우 기존 empty-slot diagnostic, LaTeX 차단, preview `Incomplete`, SVG 거절을 유지한다.

삽입은 Text caret 또는 하나의 TextNode 안에 완전히 포함된 정·역방향 부분 텍스트 선택에서만 허용된다. 전체 노드 선택(선택 대상이 TextNode인 경우 포함)과 노드/텍스트를 가로지르는 선택은 원자적으로 거절한다. 텍스트 분할은 원문·Role·스타일을 보존하고 원래 Text ID를 prefix에 우선 배치하며, prefix가 없을 때는 suffix에 보존한다. 완전 빈 슬롯 또는 전체 치환에서도 기존 ID의 marked-empty Text anchor 하나를 유지한다. 삽입 뒤 caret은 유효한 Text anchor에 놓인다. 각 삽입 요청은 spacer 하나와 독립된 편집 commit 하나를 만들며 Undo 한 번으로 그 요청만 되돌린다. 반복 요청은 각각 별도 이력이다.

시작·중간·끝·전체 선택·빈 슬롯마다 spacer 삽입→문자 입력→문자 삭제→파일 저장/재열기·layout·preview의 연속 경계를 확인한다. 완성된 `Text("a")` 끝에서 반복 삽입한 unmarked caret anchor는 계속 unmarked/Ready여야 한다. 반면 `[Text("a"), MathSpace, unmarked empty Text]`에서 spacer 선삭제 후 `a` 삭제, `a` 범위 전체 선택 삭제, Text whole-node DeleteSelection/Clear, 또는 `a` 범위를 새 spacer로 치환해 semantic-empty가 된 경로에서는 남은 empty Text anchor가 marked되어 strict 파일 validator를 통과해야 한다. 새 marker나 기존 node의 ID·Role·style·순서와 단일 commit/Undo를 확인한다. 연속 semantic-empty Sequence 조상과 nested empty sibling Sequence도 좁은 정규화 경계에 포함한다. validator 정책은 완화하지 않으며 일반 파일 재열기 시 자동 정규화를 요구하지 않는다. 단일 TextNode 분할 전후의 전체 폭 동일성은 요구하지 않는다.

explicit spacer 폭은 effective font size의 1/6 논리 px이다. spacer에는 glyph primitive와 caret stop이 없지만 양의 line-height bounds가 있다. spacer가 직접 인접한 Sequence child pair의 자동 `SequenceGap`은 0이며, spacer가 없는 나머지 직접 인접 pair는 기존 간격 규칙을 그대로 쓴다. 따라서 단일 TextNode를 분할하기 전후 전체 폭이 같다고 가정하지 않는다. 완전 TeX atom spacing이나 외부 TeX 렌더링 폭도 보장하지 않는다.

`MathTree.IsSemanticallyEmpty(space)`는 true여도 Clear는 spacer를 제거할 수 있어야 한다. 선택 Clear에서 선택 spacer 하나를 삭제할 때 주변 Text anchor ID·내용·스타일과 행 root를 보존한다. CurrentRow/CurrentGroup/Workspace Clear는 기존 계약대로 대상 행의 root 전체를 새 marked-empty TextNode로 바꾸며, 대상 row/group/workspace 식별자와 배치·상위 스타일 및 비대상 행을 보존한다. 이 범위에서는 지워진 기존 child/marker ID 보존을 요구하지 않고, 새 root의 유효한 caret과 Undo 복원을 확인한다. Backspace/Delete는 선택 spacer 또는 텍스트 경계에서 spacer 하나만 제거하고 이웃 텍스트와 다른 노드를 보존한다. 선택 spacer가 직접 부모 Sequence의 유일한 child이면 같은 Sequence ID/style을 유지하며 새 marked-empty Text anchor 하나를 넣고 caret을 그 슬롯으로 이동한다. 이 예외는 인접 sibling이 있는 일반 spacer 삭제에는 적용하지 않는다. 방향키/Tab은 spacer를 넘어 같은 직접 Sequence 안의 유효한 Text caret으로 이동한다.

LaTeX는 explicit Thin spacer를 `\,`로 출력한다. Workspace·Group·Row의 조판 preset 상속과 spacer 자체의 Script/Display override가 스타일 그룹 안에서 유지되는지도 정확한 source로 확인한다. 일반 TextNode 안의 U+2009는 명시 spacer 노드로 변환하지 않고 원문 문자로 보존한다. `\,` 사용에 amsmath 의존성을 요구하지 않으며 외부 TeX 실행이나 폭 일치를 주장하지 않는다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `THIN-JSON-VALIDATOR` | schema v1 additive discriminator, Thin kind·ID 왕복, invalid kind/unknown discriminator·field/duplicate ID 거절. bare MathSpace와 spacer-only Sequence를 필수 root 및 Fraction 필수 child slot에서 각각 거절하고, marked-empty Text+space는 허용하면서 empty-slot diagnostic·LaTeX 차단·preview incomplete·실제 SVG 거절을 유지한다. |
| `THIN-INSERT-SEMANTICS` | Text caret 및 정·역방향 같은 TextNode 선택 삽입, 다른 노드/교차 선택/전체 노드 선택 거절, invalid enum 예외, 텍스트·Role·스타일·ID 분할 보존, caret·반복 삽입·단일 commit/Undo/Redo. |
| `THIN-ANCHOR-SELECTION` | 완전 빈 슬롯과 전체 Text 치환에서 원 ID·Role·스타일의 marked-empty anchor를 보존하고 prefix가 없을 때 suffix가 원 ID를 유지하는지 확인한다. 완성 식 끝의 반복 spacer는 unmarked caret anchor와 Ready 상태를 유지한다. spacer 선삭제 뒤 마지막 문자 삭제, whole-node DeleteSelection, 마지막 문자를 새 spacer로 치환하면 semantic-empty 경로의 기존 빈 anchor가 marker로 승격되는지 확인한다. spacer 없는 Sequence, 연속/nested semantic-empty Sequence, nested sibling 및 빈 Sequence child의 새 anchor 정규화에서도 ID·Role·스타일·순서·Undo와 저장/재열기를 검증한다. |
| `THIN-DELETE-NAVIGATION` | 선택 spacer 삭제 및 이웃 Text의 시작/끝 Backspace/Delete가 spacer 하나만 제거하는지 확인한다. 구조 사이 spacer는 같은 Sequence의 가장 가까운 구조 경계 Text로 이동·삭제하고 다른 구조나 행을 건너지 않는다. 유일 child spacer 삭제는 parent Sequence ID·스타일을 유지하며 새 marked-empty anchor와 유효 caret을 만들고 Undo/Redo·저장/재열기를 확인한다. |
| `THIN-CLEAR-SEMANTIC-EMPTY` | Selection Clear는 선택 spacer 하나만 제거하고 Sequence root·주변 anchor ID·내용·Role·스타일을 보존한다. CurrentRow·CurrentGroup·Workspace Clear는 기존 Clear 계약대로 대상 root 전체를 새 marked-empty Text로 바꾸며 대상 row/group/workspace 식별자·설정·상위 스타일과 비대상 행을 보존하고 Undo로 복원한다. spacer는 semantic-empty여도 Clear 대상 내용으로 취급된다. |
| `THIN-LAYOUT-GAPS` | 여러 effective font size·preset에서 폭이 fontSize/6인지, glyph/caret stop 없음과 양의 line bounds, 앞/뒤/연속 spacer·구조 인접 및 중첩 배치. spacer 직접 인접 pair는 자동 간격 0, 기존 spacer 없는 pair는 기존 간격 규칙을 적용한다. 동일 split AST의 spacer 유무 비교로 검증하고 split 전후 전체 폭 동일성은 요구하지 않는다. |
| `THIN-LATEX-OUTPUT` | explicit node의 정확한 `\,` 출력과 TextNode U+2009 원문 보존·비변환을 확인한다. Workspace·Group·Row 상속과 spacer-local Script/Display preset의 정확한 스타일 그룹도 검사한다. 필수 empty marker가 있으면 source 생성은 유지하되 재사용 차단 진단을 남긴다. `\,`는 일반 LaTeX 수학 명령이며 외부 TeX 실행/폭 일치는 검증하지 않는다. |
| `THIN-PREVIEW-SVG` | WorkspaceStyle/Display/Inline이 각 frozen Layout 인스턴스를 그대로 소비하는지 확인한다. 실제 임시 SVG의 viewBox·glyph 텍스트·순서·x/y를 Layout primitive와 0.00051 허용오차로 대조하고 spacer ID의 glyph 부재를 확인한다. 불완전 상태는 SVG 생성/저장을 차단한다. |
| `THIN-CLONE-IO-RECOVERY` | spacer를 선택한 상태의 행·그룹 복제 및 AST leaf clone에서 새 ID·Thin kind·스타일을 보존한다. 선택 spacer가 그룹의 두 번째 행에 있고 caret은 다른 그룹에 있어도 복제 후 선택 ID와 같은 복제 행의 유효 Text caret을 확인한다. 파일·recovery JSON 왕복에서 explicit spacer와 U+2009 텍스트를 별도로 보존한다. |

## UI·실행 결과 및 검토 범위

UI 사례는 `ThinMathSpaceUiInteractionCases.All()`의 `THIN-MATH-SPACE-UI`이며, `Program.cs`의 `--thin-math-space-ui-headless` child route와 일반 case 목록에 등록되어 있다. 실행은 Avalonia Headless/Skia, `STUB`/0, 합성 입력과 TempDirectory만 사용했다. build07은 경고0·오류0·exit0이며, `thin-math-space-new10-final-01.log`의 신규 실행은 논리9건 PASS, UI 1건 실패로 9/10·exit1을 기록했다. 초기 UI 실패는 편집기 자연 폭 73px에 공통 120px 최소폭을 적용한 helper 검사였다. 얇은 공백 캡처에만 `min(120, naturalWidth)`를 반영하고 높이·viewport·겹침 검사를 유지해 보완했다.

UI 단독01과 단독02는 Ctrl+클릭 후 spacer 대신 기존 caret TextNode가 선택 상태로 남아 실패했다. 단독02의 [pointer 진단 JSON](artifacts/p2-thin-math-space/thin-space-control-click-diagnostic-20261022T224747964Z-9bf1bf59a2414978912d7575f8e78489/pointer-diagnostic.json)과 [PNG](artifacts/p2-thin-math-space/thin-space-control-click-diagnostic-20261022T224747964Z-9bf1bf59a2414978912d7575f8e78489/main-window.png)를 메인이 직접 검토했다. 좌표와 모델 hit target은 spacer를 가리켰지만 실제 window event source는 `ScrollContentPresenter/PART_ContentPresenter`, editor `InputHitTest`는 null, 이벤트는 미처리, editor focus는 발생하지 않았다. 엔진은 editor 전체 bounds에 투명 fill을 추가했고, UI 회귀는 실제 입력 target·event source·modifier·선택 결과를 검사하도록 보강했다. build07 및 UI03에서 이 경계가 통과했다.

`build-thin-math-space-final-07.log`은 경고0·오류0·exit0, `thin-math-space-ui-final-03.log`은 1/1·exit0이다. 메인은 UI03의 2026-10-22 22:50 UTC 세트에서 shell PNG 9개(최소/기본 하단, 비활성, 트리 선택, Ctrl overlay, 필수 슬롯 하단, 최소/기본 preview, incomplete)를 검토했다. bounds 12개는 핵심 필드를 확인했으며 모든 필드를 전부 읽었다고 주장하지 않는다. pointer 진단 JSON 1개, inserted-layout JSON 1개, preview JSON 2개도 직접 확인했다. 나머지 shell PNG 3개와 별도 state 캡처는 자동 산출물이며 직접 검토 세트에 포함하지 않는다.

`thin-math-space-full191-final-01.log`에서 같은 build07 산출물로 전체 191/191이 통과하고 실제 종료 코드는 0이었다. 여기에는 신규 논리9와 UI1이 재실행되어 모두 PASS했다. full 실행은 `20261022T2255` 표식 아래 별도 자동 산출물 PNG 24개와 JSON 27개를 남겼다. 이 산출물들은 UI03의 직접 검토 자료와 분리되며, 전체 자동 캡처를 메인이 모두 직접 검토한 것으로 기록하지 않는다. 상세 단계·실패 원로그와 캡처 구분은 [실행 manifest](artifacts/p2-thin-math-space/thin-math-space-manifest.md)에 있다.

한계: 이번 승인 범위는 `MathSpaceKind.Thin` 하나와 논리·Headless·offscreen 회귀다. 다른 spacer 종류나 일반 간격 정책은 포함하지 않는다. schema v1에 새 discriminator가 추가되므로 구버전 앱이 이를 읽는 호환성은 보장·검증하지 않았다. 기존 파일 읽기에서 marker 자동 정규화는 하지 않는다. spacer와 직접 인접한 pair에서는 기존 자동 SequenceGap이 0으로 대체된다. TextNode 분할에 따른 shaping 폭 변화 및 외부 TeX에서의 폭 일치는 보장하지 않으며 실제 TeX 실행도 하지 않았다. 실제 OS 창·IME·물리 키/포인터·clipboard·UIA·사용자 AppData·computer-use는 검증하지 않았다. 이 하위 단위 승인은 전체 P2/P1/P0 완료를 뜻하지 않는다.

2026-10-22 22:26·22:41 감사는 절차 확인만 했고, 22:54 UTC 조기 감사도 회귀 실행 직전의 절차 기록이다. 감사 자체는 테스트 결과나 승인 근거가 아니다.
