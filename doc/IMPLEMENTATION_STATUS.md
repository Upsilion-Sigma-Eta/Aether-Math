# 구현 상태 및 요구사항 추적표

- 기준일: 2026-10-23
- 기준 문서: [AetherMath 개발 계획서](AetherMath_개발_계획서.md) §3–§10
- 목적: 계획 요구사항·제안·제외 범위·물리 검증 보류를 구현 근거와 미완료 작업에 연결한다.
- 이 문서는 코드 변경 이력이나 제품 완료 선언이 아니다. 특정 테스트 통과는 그 테스트가 다룬 하위 계약만 확인한다.

## 상태 표기

| 표기 | 뜻 |
|---|---|
| 확인됨 | 명시한 테스트/산출물이 해당 범위를 실제로 검증했다. 전체 기능 완료를 뜻하지 않는다. |
| 부분 구현 | 일부 소스 경로와 사례는 있으나 계획의 전체 범위·사용자 흐름이 충족되지 않았다. |
| 미구현 | 현재 소스와 검증에서 기능을 확인하지 못했다. |
| 결정 대기 | 계획서에서 제안·권장하거나 P0/P1에서 계약을 정하도록 한 항목이다. 결정 전에는 요구사항 완료로 세지 않는다. |
| 제외 | 계획서가 제품 범위에서 명시적으로 제외했다. |
| 물리 검증 보류 | OS 창·포커스·UIA·실제 키보드/마우스/클립보드 검증이 필요한 게이트. Headless·API 검사는 대체 근거가 아니다. |

“필수”는 계획서에서 지원·수용 기준·완료 기준으로 정한 제품 기능을 뜻한다. “제안/권장”은 계획서가 승인이나 후속 계약 없이는 필수로 승격하지 않은 항목이다. “제외/미채택”은 문서 편집기 기능이나 미확정 외부 서비스를 만들지 말라는 경계다.

## 현재 단계 요약

| 범위 | 상태 | 근거 및 한계 |
|---|---|---|
| P0 기술 검증 | 일부 하위 slice 확인 | 중첩 구조 편집, 레이아웃, JSON 원본, SVG의 기술 경로가 있다. 지원 문법 전체, 편집기·SVG 전 범위의 동일성, 엔진 선택·라이선스, 실제 OS 편집을 끝내지 않았다. 이전 근거는 [P0-R1-F05 QA](verification/P0-R1-F05-QA.md) 및 `doc/verification/artifacts/`의 실행 로그다. |
| P1 수식 모델·행·그룹 | 모델·행/그룹, dirty New/Open/Close 보호, 저장 서비스, 저장-Undo ticket 및 VM 자동 복구·최근 작업 통합 slice 통과 | [행 QA](verification/P1-row-editing-QA-plan.md): 전체60/60. [그룹 QA](verification/P1-group-editing-QA-plan.md): 전체70/70. [Lifecycle QA](verification/P1-lifecycle-protection-QA.md): 원 신규9/9·전체79/79 및 ticket 후속 신규2/2·통합89/89. [저장 서비스 QA](verification/P1-recovery-recent-QA.md): 신규8/8·기준선 전체87/87. [VM/UI 통합 QA](verification/P1-recovery-recent-integration-QA.md): 신규9/9·통합 전체98/98 및 메인 시각 승인. 관련 빌드는 경고0·오류0. 실제 사용자 AppData와 OS 물리 입력은 미검증이다. |
| P2 구조 편집·빌더 | 템플릿·기호/Unicode 팔레트·구조 탐색/Clear·기본 Properties·조판 preset·외부 Display/Inline 미리보기·의미 텍스트 역할·분수 Display pin/dfrac·그룹 행 간격 편집·얇은 수학 공백·괄호 종류·전체 LaTeX 복사 하위 단위 승인; 나머지 P2 미완료 | [P2 템플릿 QA](verification/P2-structure-templates-QA.md): 신규13/13·전체111/111. [팔레트 QA](verification/P2-symbol-palette-QA.md): 신규9/9·전체120/120. [구조 탐색/Clear QA](verification/P2-structure-clear-QA.md): 신규9/9·전체129/129. [Properties QA](verification/P2-properties-QA.md): 신규9/9·전체138/138. [조판 preset QA](verification/P2-typesetting-QA.md): 신규9/9·전체147/147. [외부 출력 미리보기 QA](verification/P2-output-preview-QA.md): 신규9/9·전체156/156. [의미 텍스트 역할 QA](verification/P2-text-role-QA.md): 신규9/9·전체165/165. [분수 Display pin QA](verification/P2-dfrac-pin-QA.md): 신규6/6·TYPESETTING-UI 단독1/1·전체171/171 승인. 기존 `EquationGroup.RowSpacing` 저장 모델을 재사용한 그룹 간격 편집은 build06 0/0, 신규10-02 10/10, 기존 `TEXT-ROLE-UI` 단독02 1/1, 전체181-02 181/181로 승인됐다. [그룹 행 간격 QA](verification/P2-group-row-spacing-QA.md), [manifest](verification/artifacts/p2-group-row-spacing/group-row-spacing-final-manifest.md). 얇은 공백은 build07 경고0·오류0, 별도 `new10-01`은 9/10·exit1(UI 실패), UI03은 1/1·exit0, 전체191-01은 191/191·exit0이며 전체191 안에서 신규10 모두 PASS했다. [얇은 공백 QA](verification/P2-thin-math-space-QA.md), [실행 manifest](verification/artifacts/p2-thin-math-space/thin-math-space-manifest.md). 괄호 종류 편집은 build02 경고0·오류0·exit0, 신규7-01 7/7·exit0, 전체198-01 198/198·exit0으로 승인됐다. build01의 누락 `Find` helper CS0103 16건/exit1은 원로그로 보존하고 담당 UI helper 수정 후 재빌드했다. 메인은 신규7-01의 23:18:07–08 UTC PNG10개와 bounds15개의 핵심 필드를 직접 검토했으며 전체198-01의 23:20:16.394–23:20:17.878 UTC PNG/bounds 15쌍은 별도 자동 산출물이다. [괄호 종류 QA](verification/P2-bracket-kind-QA.md), [manifest](verification/artifacts/p2-bracket-kind/bracket-kind-manifest.md). 전체 LaTeX 복사 하위 단위는 build03 0/0·exit0, 신규7-02 7/7·exit0, 전체205-01 205/205·exit0으로 승인됐다. 이전 build01은 CS0103 두 오류/exit1, 신규7-01은 논리6 PASS·UI1 FAIL(6/7·exit1)이며 collapsed 캡처 PNG/bounds가 없었다. 메인은 신규7-02의 PNG9와 bounds11 핵심 필드를 직접 검토했고, 전체205-01의 23:46:06–07 UTC PNG/bounds 11쌍은 별도 자동 산출물이다. [복사 QA](verification/P2-latex-copy-QA.md), [실행 manifest](verification/artifacts/p2-latex-copy/latex-copy-manifest.md). | 승인된 하위 단위만 확인됨. 얇은 공백의 구버전 앱 호환성·실제 TeX 폭 일치, 괄호 숨김·한쪽/좌우 분리 표시·직접 크기 조절, 일반 간격 정책과 나머지 P2, 전체 P0/P1, 실제 사용자 AppData·OS 물리 검증은 미완료/미검증이다. |
| P3 수식 레이아웃 | 부분 구현 | 고정 논리 행, 등호 정렬, 일부 내부 수식 배치를 측정한다. 자동 줄바꿈·페이지 분할·연속/페이지 전환은 미완료다. |
| P4 출력·재사용 | 부분 구현 | SVG 및 LaTeX 생성 경로 일부만 검증됐다. recent 목록·pin·missing 경로 조치 및 복구 라이브러리는 P1 VM/UI에서 연결·검증했다. 전체 LaTeX 코드 복사 하위 단위는 논리·fake sink·Headless/offscreen 범위에서 승인됐다: build03 0/0·exit0, 신규7-02 7/7·exit0, 전체205-01 205/205·exit0. 메인은 신규 UI PNG9와 bounds11 핵심 필드를 검토했으며 전체205 캡처 11쌍은 자동 산출물이다. 실제 OS clipboard 검증은 보류다. PNG/PDF/MathML·Home 시작 화면·썸네일·재사용 템플릿도 미완료다. |
| P5 통합·배포 | 미착수 | 접근성·성능·설치 패키지·깨끗한 지원 OS 통합 시나리오 미검증. |
| OS 물리 입력 | 보류 | 실제 OS 창·포커스·UIA·물리 입력을 금지한 상태에서 구현을 이어 간다. 보류는 합격이나 전체 완료가 아니다. |

자동 QA의 최신 그룹 통합 결과는 `doc/verification/artifacts/p1-group-editing/build-group-final-01.log`(경고 0, 오류 0), `group-new10-final-01.log`(10/10), `regression70-final-01.log`(70/70)이다. 두 번의 세션별 합격은 §3–§10 전체 완료를 뜻하지 않는다.

## §3 핵심 편집 기능

| ID | 분류 | 요구사항 | 현재 상태·소스/검증 근거 | 미완료·우선순위 |
|---|---|---|---|---|
| 3.1-01 | 필수 | 단일/다중 수식 행과 그룹을 같은 편집 모델로 표시한다. | 부분 구현: `Core/MathModels.cs`, `Editing/EditingSession.cs`, `Layout/MathLayoutEngine.cs`, `Desktop/MathEditorControl.cs`; 행·그룹 저장/레이아웃 사례 `ROW-IO`, `GROUP-IO`. | 실사용 직접 편집 흐름의 폭·페이지·장문 검증은 P0/P3에 남음. |
| 3.1-02 | 필수 | 렌더된 분모·근호·첨자·행렬 셀 등을 직접 선택하고 해당 구조만 수정한다. | 논리 hit-test/명령 사례 `SEL-01`–`SEL-04`, `STRUCT-SELECT`, 구조 수정 사례가 확인됨. `MathLayoutEngine`/`MathEditorControl`이 노드 bounds·caret stop을 사용. | 실제 OS 포커스/마우스·키보드 입력은 **물리 검증 보류**. 현재 headless 합성 이벤트는 물리 게이트를 닫지 않음. |
| 3.1-03 | 필수 | 텍스트·숫자·기호 입력과 구조 템플릿 삽입을 caret 슬롯에 적용한다. | 부분 확인: `EditingSession`, `TextElementOffsets`; `SEL-*`, `UNI-01`, `UNI-04`, `VM-COMMAND`, `TPL-*`, `PAL-INSERT-REPLACE`, `PAL-EMOJI-GRAPHEME`. canonical 팔레트 삽입과 선택 교체/undo를 논리·Headless 회귀에서 확인했다. | 실제 IME preedit, OS 입력 및 전체 수식 기호 지원은 미검증. |
| 3.1-04 | 필수 | 좌우/상하·슬롯 이동, 부분 선택, 구조 교체/복제/삭제를 정의한다. | 논리 구조 caret·선택·undo 테스트 `SEL-*`, `DEL-*`, `UND-*`; row/group 복제 및 selection map 테스트 통과. | 복잡한 자동 줄바꿈 뒤 이동, 모든 구조 종류, 실제 포인터 경로는 부분/보류. |
| 3.1-05 | 필수 | Backspace/Delete 경계에서 내용 보존·해제·삭제 규칙과 undo를 제공한다. | 부분 검증: `DEL-01`–`DEL-03`, `DEL-TEXT`, `PTR-01`(headless 합성), `EditingSession` 명령. | 물리 Ctrl+클릭→삭제→undo는 미검증; 구조 “해제” 전체 정책도 확정 필요. |
| 3.1-06 | 필수 | 행·그룹 추가/복제/삭제/순서 변경과 undo/redo. | 이 단위 확인됨: `EditingSession` 그룹/행 API, `GroupEditingCases`, `RowEditingCases`; 새 테스트 10/10, 전체 70/70. UI는 실제 App Headless/Skia로 검증. | OS 물리 입력 및 전체 P1 완료와 분리. |
| 3.1-07 | 필수 | 빈 필수 슬롯을 원본에 보존하고 미완성 출력은 이유를 알린다. | 확인됨: `WorkspaceValidator`, `MathLayoutEngine` 진단, `SvgExportService.RejectIncomplete`; `SLOT-INVARIANT`, `SVG-04`, `ROW-IO`, `GROUP-IO`. | 전체 PNG/PDF/MathML 미완성 동작은 해당 형식 구현 때 검증. |
| 3.1-08 | 필수 | 선택·배율·스크롤 변경만으로 dirty를 만들지 않는다. | 부분 확인: 선택/경계 no-op `UND-03`, 레이아웃 purity `LAY-03`; `EditingSession`의 selection/caret은 별도 history 변경을 만들지 않음. | 실제 배율·DPI·페이지 전환 기능은 미구현; 각 기능 추가 시 불변성 테스트 필요. |
| 3.2-01 | 필수 | 기호 분류 7종: Algebra, Calculus, Linear Algebra, Greek Letters, Logic, Arrows, Structures. | 구현 및 자동 검증: `MathSymbolCatalog`, `CuratedEmojiCatalog`, `UnicodeScalarBrowser`, `SymbolPaletteViewModel`; `PAL-CATALOG-IDENTITY`, `PAL-SEARCH-ALIAS`, `PAL-UNICODE-*`, `PALETTE-UI`. 최종 Debug 빌드 경고0·오류0, 신규9/9, 전체120/120. 메인이 16:18 UTC 대표 이미지 10개와 16:22 UTC 이미지 6개·bounds를 확인하고 이번 slice를 논리·Headless·offscreen 범위에서 승인했다. | Alias는 category별로 검색하며 전역 유일성을 요구하지 않는다. OS/IME 입력, 구버전 앱 런타임 및 글꼴별 보편 지원은 미검증. |
| 3.2-02 | 필수 | 템플릿 10종: 분수, 근호, 위/아래 첨자, 적분, 합, 극한, 행렬, 괄호, 경우 나누기. | `MathTemplateKind` 10종, additive AST/discriminator(`integral`, `summation`, `limit`, `brackets`, `cases`) 및 schema v1 JSON 왕복을 검증했다. Debug 빌드 경고0·오류0, TPL 논리12/12·전체111/111. 팔레트 slice에서 Structures 항목 10종의 공통 삽입 경로도 전체120 회귀까지 통과했다. | 구버전 앱 실행 호환은 미검증. Properties/Structure 편집·코드 parser 등 나머지 P2 통합은 미완료. |
| 3.2-03 | 필수 | 팔레트와 빠른 삽입은 같은 편집 명령을 사용한다. | 템플릿 10종과 canonical 기호 ID·curated emoji·Unicode scalar 삽입이 기존 Session 편집 경로를 사용한다. `PAL-INSERT-REPLACE`, `PAL-EMOJI-GRAPHEME`, `PAL-ROUNDTRIP-GLYPH`, `PALETTE-UI`가 최종 자동 회귀와 시각 검토를 통과했다. | OS 물리 입력·IME·구버전 앱 런타임·보편 glyph 지원은 별도 미검증. |
| 3.2-04 | 필수 | 템플릿 빈 슬롯·초기 caret·선택 감싸기/교체 동작. | `TPL-SHAPES`, text/node selection wrapping, 깊은 navigation/history, 합성 Tab/Shift+Tab·slot 입력·undo/redo가 신규 논리/UI 회귀에서 통과했다. 괄호/Cases delimiter와 content bounds의 비겹침도 offscreen 검사했다. | 네이티브 포인터/키 입력은 물리 검증 보류. 전체 P2 범위 미완료. |
| 3.2-05 | 필수 | Properties/Structure 선택과 표면 편집은 같은 Node ID를 사용. | 각 실제 행 root/하위 MathNode ID를 `StructurePanel`에 투영하고 preorder·슬롯 순서, caret/부분선택 동기화를 구현했다. 신규 `STRUCT-PROJECTION`/`STRUCT-SELECT-PURITY`와 UI 사례가 통과했다. `SelectNode`만 명시 전체 구조 선택/reveal을 수행하며, 표면 선택 echo는 편집 상태를 바꾸지 않는다. 크기·글꼴·색의 네 scope별 원본/effective 값과 Apply/undo도 Properties 신규9/9·전체138/138에서 검증하고 신규9 PNG 8쌍을 직접 검토해 slice를 승인했다. | 이 요구 범위는 논리·Headless/offscreen에서 승인. 네이티브 OS 물리 입력은 별도 미검증. |
| 3.2-06 | 필수 | Clear All은 활성 범위를 표시하고 실행 취소 가능. | `MathClearScope` 네 범위와 root VM 명령, `EditingSession.CanClear/Clear`를 사용한다. 신규 논리8+Headless UI1과 전체129/129 통과. 활성 대상 우선, 빈 범위 완전 no-op, 행 root/ID 보존, 단일 undo/redo 및 미완성 SVG 거절을 확인했다. | Headless/offscreen slice에서 승인. 네이티브 입력과 P2 전체 완료는 별도 미검증/미완료다. |
| 3.2-07 | 필수 | 화면 도구 구성은 한 수식 모델/세션을 편집한다. | 현재 `MainWindowViewModel.Session`과 명령이 공용 모델을 사용. row/group UI headless 테스트에서 실제 명령 binding 확인. | 기호·코드 패널 추가 후 같은 세션 유지 검증. P2. |
| 3.3-01 | 필수 | 글자 크기/색/수학 글꼴·스타일 및 Display/Inline. | 기본 크기·색·글꼴·조판 preset은 전체147 회귀로, 외부 WorkspaceStyle/Display/Inline frozen SVG 미리보기는 build04 0/0·신규9/9·전체156/156·exit0으로 각각 하위 단위 승인을 받았다. 미리보기 논리·Headless/offscreen 검증 및 메인 신규9-03 화면 6쌍 직접 리뷰를 수행했다. | 이번 미리보기 승인은 외부 TeX/구버전 앱과 네이티브 OS 창·입력/포커스/UIA를 포함하지 않는다. 나머지 P2 및 P0/P1 전체도 미완료. |
| 3.3-02 | 필수 | 괄호, `\dfrac`, 얇은 공백, 수평/수직 간격을 수학 표현 규칙과 연결. | 부분 구현: 분수 자체 Display pin→`\dfrac`과 기존 `EquationGroup.RowSpacing` 사용자 편집 slice가 승인됐다. 얇은 공백은 schema v1의 `MathSpaceNode(MathSpaceKind.Thin)`이며 괄호 종류는 기존 BracketsNode 네 kind(Parentheses/Square/Curly/Angle) 속성 편집 하위 단위로 승인됐다. 괄호는 build02 경고0·오류0, 신규7-01 7/7, 전체198-01 198/198이며 모두 exit0이다. 신규7-01에서 메인이 23:18:07–08 UTC PNG10개와 bounds15의 핵심 필드를 직접 검토했고, 전체198-01에서 23:20:16–17 UTC 생성된 15쌍은 별도 자동 산출물이다. [괄호 종류 QA](verification/P2-bracket-kind-QA.md), [manifest](verification/artifacts/p2-bracket-kind/bracket-kind-manifest.md). 얇은 공백은 build07 0/0, 별도 `new10-01`은 9/10·exit1(UI 실패), UI03 1/1·exit0, 전체191-01 191/191·exit0이며 전체191 안에서 신규10 모두 PASS했다. [얇은 공백 QA](verification/P2-thin-math-space-QA.md), [manifest](verification/artifacts/p2-thin-math-space/thin-math-space-manifest.md). | 각 승인 범위는 괄호 네 종류 선택·표시·소비와 Thin spacer 하나, 그리고 기존 그룹 간격 편집 slice에 한정한다. 괄호 숨김·한쪽/좌우 분리 표시·크기 직접 조절, 일반 간격 정책, OS/IME/clipboard 입력, 구버전 앱 호환성 및 외부 TeX는 미검증이며 3.3-02 전체와 나머지 P2/P1/P0는 미완료. |
| 3.3-03 | 필수 | 숫자/수학 기호와 일반 텍스트·단위·조건의 의미·서체 구분. | `MathTextRole` schema v1 기본값/검증, role-aware shaping·LaTeX 표시/진단을 논리8+Headless `TEXT-ROLE-UI`1로 검증했다. build05 경고0·오류0, 신규9/9·전체165/165 모두 exit0이며 메인은 신규9-02/03의 명시된 캡처와 bounds를 직접 검토해 slice를 승인했다. [QA 기록](verification/P2-text-role-QA.md), [실행 manifest](verification/artifacts/p2-text-role/text-role-final-manifest.md). | 승인 범위는 논리·Headless·offscreen 하위 단위다. 외부 TeX/parser, OS/IME/clipboard, 사용자 AppData, LaTeX role provenance 역변환 및 전체 P2/P1/P0는 미검증/미완료. |
| 3.3-04 | 제안 | 수식 내부 텍스트와 행/그룹의 짧은 주석. | 결정 대기: 계획서에서 제안, 길이·위치·출력 범위는 P0 확정 항목. | 범위가 승인되기 전 필수 기능으로 계산하지 않음. |
| 3.3-05 | 필수 | 한글 IME·결합 문자·붙여넣기. | 부분: grapheme-aware 논리 편집과 한글/ZWJ/결합 문자열 테스트 `UNI-01`, `UNI-04`; clipboard seam `CLIP-01`–`CLIP-06`. | actual IME preedit/candidate, OS clipboard와 물리 입력은 보류. |
| 3.3-06 | 선택 기능 | 행/그룹 번호·라벨과 복제/순서 변경 충돌 처리. | 미구현/결정 대기: 모델에 번호·라벨 없음. | 번호 필요 여부·형식·중복 정책 결정 후 P2/P3. |
| 3.4-01 | 필수 | 시각 편집 결과의 LaTeX 표시·구문 강조·복사. | LaTeX 표시와 fake sink 기반 전체 코드 복사 slice가 승인됐고, 표시용 token snapshot 및 선택 가능한 강조 표면도 하위 단위로 승인됐다. 구문 강조는 build05 경고0·오류0·exit0, 신규5-04 5/5·exit0, 기준선205 포함 full210-01 210/210·exit0이다. 메인은 신규5-04 PNG8개와 같은 실행 bounds8의 주요 필드를 검토했고, full210 별도 PNG/bounds8쌍은 자동 산출물이다. [구문 강조 QA](verification/P2-latex-highlight-QA.md), [manifest](verification/artifacts/p2-latex-highlight/latex-highlight-manifest.md). | LaTeX parser/지원 문법 완성, 다른 scope/형식의 복사 matrix는 미완료다. 실제 OS clipboard·입력·포커스/UIA는 물리 검증 보류. |
| 3.4-02 | 필수 | 코드 없이 지원 수식을 생성·수정·배치·저장·출력. | 부분: 기본 템플릿·저장·SVG 존재. | 현재 10개 템플릿·확정 수식 기능을 포괄하지 않음. P2–P4. |
| 3.4-03 | 제안 | 지원 문법 범위 내 LaTeX 직접 입력/붙여넣기. | 미구현/결정 대기: serializer만 확인; parser가 없음. | P0에서 문법·오류 복구·초안과 마지막 유효 트리 정책 결정 후 P2. |
| 3.4-04 | 필수 | 미완성/미지원 입력 보존, 성공 파싱만 반영, 오류 위치 표시, 왕복 의미 보존. | 부분: serializer 출력 및 지원 subset 테스트만 존재. | 직접 편집 parser와 왕복 test suite 전부 미완료. P0/P2. |

## §4 수식 레이아웃

| ID | 분류 | 요구사항 | 현재 상태·소스/검증 근거 | 미완료·우선순위 |
|---|---|---|---|---|
| 4.1-01 | 필수 | 글리프 폭·baseline·높이·수학 축·첨자 위치 측정. | 부분: `IMathTextMetrics`, `AvaloniaMathTextMetrics`, `MathLayoutEngine`; `LAY-02`, FontProbe. | glyph coverage와 다양한 font/OS, 모든 구조의 수학 조판은 P0 검증 중. |
| 4.1-02 | 필수 | 분수·근호·괄호·큰 연산자·행렬 및 연산자별 간격. | fraction/radical/script/matrix 및 sum/limit/integral/Cases 배치에 대한 기본 조판 전이와 선택된 큰 한계 bounds를 `TYPE-*` 논리 회귀와 동일 layout SVG 경계로 검증했다. build05/new9-03/full147-03 승인 결과와 직접 검토 근거는 [조판 QA](verification/P2-typesetting-QA.md)에 기록했다. | 전체 TeX 간격 표준, 더 넓은 font/OS 행렬, 줄바꿈·페이지 분할은 미완료. P0/P3 후속. |
| 4.1-03 | 필수 | Display/Text/Script 크기 및 중첩 스케일. | Display/Text/Script/ScriptScript preset, 분수·첨자·연산자 전이와 중첩 scale `.71^depth`/1px 하한을 신규8 논리+Headless UI와 전체147 회귀에서 확인했다. 별도 전체 작업 WorkspaceStyle/Display/Inline SVG 미리보기도 build04 0/0·신규9/9·전체156/156·exit0으로 승인했다. | 승인된 전체 작업 SVG 미리보기 하위 범위를 넘어 외부 출력 형식·앱/OS 통합은 미검증이며 P0/P2 전체 완료가 아니다. |
| 4.1-04 | 필수 | 폰트 대체·누락 표시 및 화면/출력에서 같은 layout 사용. | 부분: 실제 폰트 face 순서와 offscreen control/SVG 비교 `FONT-PROBE-ORDERS`, `FONT-OFFSCREEN`. | 전체 수식·모든 글자 픽셀 동일, 외부 폰트 보유 여부·배포 라이선스는 미결정. OS/환경 보장 범위 제한. |
| 4.2-01 | 필수 | 논리 행과 폭에 따른 파생 시각 행을 분리한다. | 부분: 저장 `EquationRow`, `LogicalRowLayout`/`VisualRowLayout` 모델은 있으나 자동 wrap 기능을 확인하지 못함. | P3 구현과 폭 전환·caret/hit-test 검증. |
| 4.2-02 | 필수 | 등호/관계 정렬, 행·그룹 간격, 들여쓰기·번호. | 부분: `AlignmentMode.Equals`, `RowSpacing`; `LAY-01`, `ROW-IO`, `GROUP-IO`. | 관계 기호/그룹 간격/번호 정책 전체 미완료. P3. |
| 4.2-03 | 필수 | 명시 줄바꿈/정렬점 저장; 자동 wrap은 의미 안전한 연산자 경계에서만 한다. | 미완료: 저장된 row 구조와 Equals alignment만 있음; wrap·manual-break node/API 미확인. | P3 설계·구현. |
| 4.2-04 | 필수 | 긴 구조를 임의로 쪼개거나 숨기지 말고 폭 초과 진단; wrap toggle도 원본 보존. | 부분: logical rows와 구조 tree 분리. | 넘침 diagnostics/width/wrap 제어 미구현. P3. |
| 4.2-05 | 제안 | 그룹 병렬 배치를 간격/정렬 제약으로 표현한다. | 결정 대기: 자유 배치가 아닌 constrained layout 제안. | P0에서 필요성·수식 모델과 소비 형식 확정. |
| 4.3-01 | 권장 | 기본은 연속 확장 작업 영역, scroll/zoom/width control. | 부분: `MainWindow`에 `ScrollViewer`, 긴 행/group headless caret 노출. | zoom/width control 및 수식 의미·선택 보존 미완료. P3. |
| 4.3-02 | 필수 | 용지 크기/방향/여백 적용한 output preview, 허용 경계 페이지 분할. | 미구현: 현재 원본 모델에 page settings 없음, PDF/page preview 없음. | P3 output preview/P4 PDF. |
| 4.3-03 | 필수 | 페이지는 layout 파생물이고 원본 수식 노드/선택은 유지. | 부분 모델은 `EquationWorkspace` 중심이며 page entities 없음. | paging 기능을 만들 때 전환·저장·selection 테스트 추가. |
| 4.3-04 | 필수 | 화면 zoom, 수식 자체 크기, 출력 DPI 분리. | 부분: global MathStyle.FontSize와 layout options는 있으나 actual zoom/output DPI setting 없음. | P3/P4. |

## §5 데이터 모델 및 구현 구조

| ID | 분류 | 요구사항 | 현재 상태·소스/검증 근거 | 미완료·우선순위 |
|---|---|---|---|---|
| 5.1-01 | 필수 | Workspace→Group→Row→MathNode 모델, 안정 ID; 편집 세션/레이아웃/프리셋 분리. | 부분: `Core/MathModels.cs`, `EditingSession.cs`, `LayoutResult`; global unique ID validation와 저장 테스트. `ExportPreset` 모델 없음. | 주석/번호/출력 설정/프리셋 등은 미완료. |
| 5.1-02 | 필수 | page 객체가 원본 수식 데이터의 소유자가 아니며 임시 좌표/픽셀을 저장하지 않는다. | 현재 저장 모델은 workspace/group/row/node 기반이며 layout은 계산 결과. `IO-01` 왕복. | 페이지 기능 추가 시 원본/파생 경계 유지. |
| 5.2-01 | 필수 | caret/selection은 node ID·slot/grapheme offset으로 표현; 화면 좌표는 원본이 아님. | 확인됨(논리): `MathCaret`, `MathSelection`, `TextElementOffsets`, `EditingSession`; SEL/UNI/ROW/GROUP suite. | physical pointer gate 및 visual rows 뒤 caret 이동은 미완료/보류. |
| 5.2-02 | 필수 | undo transaction, clone new ID, 저장 snapshot/dirty 정확성. | 부분: history + `WorkspaceSnapshot`/StateId, clone IDs; `UND-*`, `IO-SNAPSHOT`, `ROW-*`, `GROUP-*`. | style/code/parser/history 통합·전체 모델 transaction은 후속 기능별 검증. |
| 5.2-03 | 제안 | `.aethermath` versioned JSON model. | 현재 채택 상태: `WorkspaceFileService.Extension`, schema v1 JSON, validator; roundtrip/invalid/cancel tests. 계획서 원래 “제안”에서 실제 구현 선택이 된 사실을 기록. | schema migration, crash recovery/backup, user file ownership/retention 추가 필요. |
| 5.2-04 | 필수 | 원자 저장, 진행 중 snapshot revision, 실패 시 기존 파일·dirty 보존. | `WorkspaceFileService.SaveAsync`, `IO-02`–`IO-CANCEL`, `IO-SNAPSHOT`, `VM-SAVE`와 lifecycle FIFO/pending continuation 검증. `build-lifecycle-final-02.log`, 신규 9/9 및 전체 79/79. | 성공·취소 경합 및 격리 TempDirectory는 검증. 실제 OS 저장 대화상자, 네이티브 파일 선택기와 플랫폼별 동작은 미검증. |
| 5.2-05 | 필수 | 자동 복구본은 사용자 파일과 분리; 손상/미지원 schema를 덮어쓰지 않는다. | `WorkspaceStateService` 명시 root 저장 계층 8/8·기준선 전체87/87과 VM 자동 tick·복원/정리·recent 연결 신규9/9·전체98/98이 각각 통합 승인됐다. 손상/미지원 복구본 원본 보존, pending Restore 보호, 명시 삭제, TempDirectory 기반 실제 App/MainWindow Headless UI를 검증했다. | 실제 사용자 AppData 연결, 운영 환경의 타이머 수명주기와 다른 OS/권한 조건은 미검증. |
| 5.3-01 | 확정 | C# + Avalonia 12; UI XAML/MVVM; 앱 수식 전용 control. | 사용 중: `AetherMath.Desktop.csproj`, `MainWindow.axaml`, `MathEditorControl`, direct MVVM Commands. .NET 10/Avalonia 12.1.4. | 지원 SDK/OS release matrix와 packages 확인 지속. |
| 5.3-02 | 필수 P0 | 편집 가능한 engine/renderer, license, supported grammar, output suitability 비교. | 부분: 자체 basic AST/layout/SVG 경로가 구현됨. | 기술 후보 비교, feature coverage, license/re-distribution 기록 미완료. P0. |
| 5.3-03 | 필수 | screen/preview/export share same LayoutResult; OS file/clipboard/IME/DPI validate. | 부분: control/SVG common layout, headless font/offscreen evidence. | 실제 OS platform services/clipboard/file picker/IME/DPI 미검증. |
| 5.4-01 | 권장 구조 | Core/Editing/Layout/Infrastructure/Desktop 책임 분리, Avalonia-free core. | 부분: 폴더·namespace가 분리되고 `Core` model에는 Avalonia using 없음; 단일 Desktop project 내 구성. | 프로젝트 분리 자체는 계획상 필수가 아님; 순환/의존 경계는 static checks/추가 tests 필요. |
| 5.4-02 | 필수 | 편집 명령→모델→영향 범위 layout→화면 갱신; stale async result를 차단. | 부분: EditingSession revision·paste epoch/target guards; CLIP-01–06. | 전체 workspace relayout 성능/async layout cancellation 및 cache growth 미측정. |
| 5.5-01 | 필수 플랫폼 | Avalonia 12 TopLevel/dispatcher/storage/clipboard API와 ownership 규칙을 따른다. | 부분: `MathEditorControl` uses modern DataTransfer API; Headless Skia on tests. | 실제 OS clipboard object ownership/lifetime, clipboard formats, picker, dispatcher concurrency 미검증. |
| 5.5-02 | 권장 | default text shaping; Skia/HarfBuzz when needed; shaping/layout/input separately test. | 부분: Avalonia text shaping/Skia product probe, real glyph face signatures. | OS breadth, HarfBuzz configuration and all fallback/script combinations not covered. |

## §6 복사·내보내기

| ID | 분류 | 요구사항 | 현재 상태·소스/검증 근거 | 미완료·우선순위 |
|---|---|---|---|---|
| 6.1-01 | 필수 | selection / single expression / group / whole-workspace output scopes. | 부분: SVG exporter consumes `LayoutResult` of whole workspace; `GROUP-IO` checks whole groups in serialization/LaTeX/SVG ordering. | partial selection/single row/group scoped export APIs and UI absent. P4. |
| 6.1-02 | 필수 | explicit output range, number/annotation/layout choices; no fallback to arbitrary expression. | 미구현 beyond full-workspace SVG. | P0 output contract, P4 controls. |
| 6.2-01 | 필수 | PNG alpha/DPI/margins; SVG geometry/font policy; PDF page/vector rules. | SVG path exists and offscreen PNG is diagnostic only. `SvgExportService`, `SVG-*`, `FONT-OFFSCREEN`. | product PNG/PDF encoder, DPI/crop/background, PDF pagination/fonts/licensing absent. P4. |
| 6.2-02 | 필수 | LaTeX group alignment/order; MathML structure/reading order. | Partial LaTeX serialization in `LatexSerializer`; `LATEX-NESTED-SCRIPT`, `ROW-IO`, `GROUP-IO`. | grammar coverage, MathML encoder and consumption tests absent. P0/P4. |
| 6.2-03 | 필수 | Clipboard only advertised formats actually consumable; distinguish SVG text from vector data. | Partial paste path: `MathEditorControl` DataTransfer reader seam and CLIP cases. | product copy/export and real target app paste are unverified. P4 + physical hold. |
| 6.2-04 | 필수 | output fixed to starting revision; failure/cancel leaves no partial completed file. | SVG file atomic replacement/cancellation tests `SVG-FILE`; `LayoutResult` built from snapshot path. | other formats unimplemented; VM in-flight save/export cross-session semantics require coverage. |
| 6.3-01 | 필수 | quick copy and full export share same encoding path; preview is not a second document. | Partial SVG exporter and shared layout. | no preview window/export preset/product clipboard. P4. |
| 6.3-02 | 제안/미확정 | presets and Share-to external app/hosting. | Not adopted: no preset/hosting/share code. Plan explicitly says hosting/link access/retention undecided. | Keep outside core until explicit decision; never claim local path copy is hosting. |

## §7 홈·셸·사용성

| ID | 분류 | 요구사항 | 현재 상태·소스/검증 근거 | 미완료·우선순위 |
|---|---|---|---|---|
| 7-01 | 필수 | Home: new workspace, recent/pinned work, math templates and preview. | 부분: `MainWindow`의 최근 작업·복구 라이브러리 탭에서 목록 선택/미리보기, pin, 복구 및 삭제 확인을 연결했고 Headless UI 신규9/전체98로 확인했다. 제품은 editor로 시작하며 별도 Home 시작 화면과 템플릿 갤러리는 없다. | P4 Home shell, 새 작업 진입점/재사용 템플릿 갤러리 구현 필요. |
| 7-02 | 필수 | list name/path/thumbnail/modified time; missing path recovery or removal. | 부분 검증: `WorkspaceStateService`와 VM에서 이름·전체경로·최근 사용·파일 수정 시각·누락/metadata 진단, pin/unpin/remove, 명시 경로 relink를 구현했다. `WorkspaceLibraryTab` Headless UI 신규9/전체98 통과. | 썸네일 생성/표시와 실제 사용자 AppData 저장은 미완료·미검증. |
| 7-03 | 필수 | header workspace name/status, undo/redo, zoom, export. | 부분: 작업명·상태·undo/redo·SVG가 있고 저장 상태 보호는 lifecycle과 VM/UI 통합 회귀를 통과했다. `MainWindow.axaml`, ViewModel 및 Headless UI에서 확인. | 배율 및 줌 동작은 P3. |
| 7-04 | 필수 | Navigation keeps same session/selection; no parallel screen copies. | `MainWindowViewModel`은 단일 Session을 유지하고 최근/복구 탭 선택은 수식 Session/selection을 바꾸지 않는다. `LIBRARY-UI` 및 신규9/전체98 통과. | 별도 Home 전환과 템플릿 갤러리는 미구현. |
| 7-05 | 제안 | 기호·이름·라벨 검색. | 구현 및 하위 범위 검증: 기호 팔레트에서 선택 category 내 이름·alias 검색을 지원하며 `PAL-SEARCH-ALIAS`와 Headless UI에서 확인했다. | 여러 category를 한 번에 검색하는 전역 검색은 현재 계약에 포함되지 않는다. |
| 7-06 | 필수 | Settings for math font/input/save/export defaults. | Not implemented. | P4 per proposed sequence. |
| 7-07 | 필수 | responsive panels, zoom/DPI hit-test, keyboard access and non-color status. | Partial: min/max shell layout and group/row control bounds headless; button automation names. | Accessibility, keyboard-only command coverage, DPI and actual OS focus not verified. P5/physical hold. |

## §8 개발 단계

| 단계 | 계획된 exit evidence | 현재 상태 | 다음 작업 |
|---|---|---|---|
| P0 | Avalonia app: nested selection→instant update→long-row alignment→save/reopen→vector output; engine/grammar/license evidence. | 일부 technical slice 있음; overall P0 not complete. F05/P0 QA says automated cases pass but physical input and broad engine decisions remain. | Finish lifecycle protection dependency; document supported grammar/engine/license and end-to-end scope before declaring. |
| P1 | 작업/세션/history/save/open/recovery/common shell. | 모델·행·그룹, dirty New/Open/Close 보호, 저장-Undo ticket, 명시 root 저장 서비스와 VM 자동 복구·복원·recent UI를 논리·Headless/offscreen 범위에서 확인했다. 최신 VM/UI slice 신규9/9·전체98/98 및 메인 시각 승인 완료. OS 물리 입력과 실제 AppData는 미검증이며 Home 시작 화면/썸네일은 미완료다. | 검증된 P1 구현 slice 승인. 실제 OS 입력과 AppData 미검증, Home 시작 화면·썸네일 미완료. |
| P2 | 기호 7분류, 템플릿 10종, 선택/복사, 슬롯 이동, 속성, 코드 생성 및 확정된 parser. | 10종 템플릿 전체111/111, 팔레트 전체120/120, 구조 탐색/Clear 전체129/129, 기본 Properties 전체138/138, 조판 preset 전체147/147, 외부 Display/Inline 미리보기 전체156/156, 의미 텍스트 역할 전체165/165, 그룹 행 간격 전체181/181, 얇은 공백 전체191/191, 괄호 종류 전체198/198, 전체 LaTeX 복사 전체205/205, 구문 강조 전체210/210이 각각 하위 단위로 승인됐다. 구문 강조는 build05 경고0·오류0·exit0, 신규5-04 5/5·exit0, full210-01 210/210·exit0이다. 신규5-04 PNG8개와 bounds8의 주요 필드를 메인이 직접 검토했고, full210의 별도 PNG/bounds 8쌍은 자동 산출물이다. [구문 강조 QA](verification/P2-latex-highlight-QA.md), [manifest](verification/artifacts/p2-latex-highlight/latex-highlight-manifest.md). | P2 나머지 요구, parser/외부 TeX, 구버전 앱, 실제 OS/IME/clipboard/AppData는 미검증/미완료. 하위 slice 승인으로 P2 또는 P0/P1 전체 완료를 주장하지 않는다. |
| P3 | math spacing/baseline, alignment/logical rows, auto-wrap/long formulas/group arrangement/continuous and print preview. | Partial internal math measurement/alignment. | Wrap, zoom, width behavior and output preview; preserve logical tree/edit positions. |
| P4 | PNG/SVG/PDF/LaTeX/MathML, clipboard/presets, home/recent/templates. | SVG/LaTeX partial; other formats/workflows absent. | Complete approved format×scope matrix and test external consumers. |
| P5 | integrated UI/accessibility/DPI/performance/file safety and supported-OS packaging. | Not started. | Run after P1–P4 and supported OS/matrix decision. |

## §9 검증·완료 기준 추적

| ID | 계획 검증 | 현재 근거 | 빠진 증거 |
|---|---|---|---|
| 9.1-01 | WYSIWYG nested partial edit→same settings export. | `SEL-01`–`SEL-04`, `LAY-02`, `SVG-01`, font offscreen; P0 QA. | Physical actual window editing; product export matrix beyond SVG; full pixel/layout equality. |
| 9.1-02 | structure, cursor, Unicode/IME, undo, LaTeX, layout and file failure boundaries. | P1/P0 자동 회귀와 새 템플릿 사례를 포함한 최신 통합 전체111/111이 통과했다. 신규 TPL 논리12 및 TEMPLATE-UI 1은 승인을 받은 10종 템플릿 하위 slice다. | OS IME/clipboard/물리 입력, 직접 LaTeX parser, auto-wrap, 누락 출력 형식, 구버전 앱 런타임은 미검증 또는 미구현이다. |
| 9.1-03 | real input/external consumer separate from deterministic model tests. | Headless tests explicitly guard `STUB`/0; Firefox headless consumer only for SVG smoke. | Physical input and user foreground tests intentionally deferred. |
| 9.2-01 | p95 100ms initial goal; large nodes/depth/rows/matrices; memory growth. | No benchmark/long-run measurement evidence. | Performance fixture/hardware baseline P0; cancellation/responsiveness and cache/memory tests P5. |
| 9.3-01 | integrated workflow from empty expression through nested structures, rows, zoom/wrap, reopen, external export/PDF. | Automated portions across SEL/ROW/GROUP/IO/SVG; group full UI shell screenshots. | No complete actual OS user journey, template breadth, zoom/wrap, PDF or consumer matrix. |
| 9.1/9.3 physical | actual focus, Ctrl+V, denominator/matrix edit, Ctrl+click/capture/delete/undo. | **물리 검증 보류.** P0 QA documents this explicitly. | User computer availability and separate explicit test slot. Do not block current non-interfering development, do not call passed. |

## §10 미결정·위험·비채택 추적

| ID | 유형 | 계획 항목 | 현재 판단/근거 | 결정/대응 |
|---|---|---|---|---|
| 10-01 | 결정 대기, P0 | Engine/renderer suitability, structure coordinates, license. | Basic engine implemented internally; no recorded complete candidate comparison/re-distribution analysis. | Record exact supported features, dependency/license inventory, tradeoffs before P0 closure. |
| 10-02 | 결정 대기, P0/P2 | Supported symbols/environments/direct LaTeX input and incomplete grammar. | Serializer subset exists; parser absent. | Publish allowlist/deny behavior and roundtrip fixtures before direct input. |
| 10-03 | 결정 대기, P0/P3 | wrap/alignment/splitting/overflow and side-by-side groups. | Equals alignment/group sequential order exists; automatic wrapping/page split not implemented. | Define only supported operator breakpoints and immutable logical rows. |
| 10-04 | 결정 대기, P0/P4 | output scope, number/annotations, format combinations and consumers. | WorkspaceStyle/Display/Inline 전체 작업 SVG frozen preview 하위 slice는 build04·전체156 회귀와 메인 화면 리뷰로 승인됐다. LaTeX text 경로는 별도이며 형식·범위 조합 전체를 검증하지 않았다. | 지원하는 scope×format 조합만 더 검증하고, 전체 matrix로 과장하지 않는다. |
| 10-05 | 결정 대기, P0/P1 | OS/platform, default UI language, math font fallback/distribution. | Windows/Avalonia 12.1.4 observed in probes. | Avoid generalizing to unchecked OS/font versions; record release matrix and license. |
| 10-06 | P1 필수 | 파일 스키마, 확장자, 원본 안전 및 복구. | `.aethermath`, JSON schema v1, atomic save와 잘못된 작업 파일 거부가 있다. Dirty New/Open/Close, FIFO 저장/닫기 drain, delayed Save→Undo ticket 및 명시 root recent/recovery 저장·VM 자동 복구·복원·recent UI가 Debug 경고0·오류0, 신규9/9·전체98/98로 통합 승인됐다. | 실제 사용자 AppData, native picker와 OS 물리 저장/열기 및 다른 플랫폼은 미검증. |
| 10-07 | design proposal | change Document/Pages/Insert into product naming. | Current shell uses “수식 작업”, “행/그룹”, save/open; no broad home yet. | Continue math-native naming; do not port document semantics. |
| 10-08 | not adopted/optional | cloud hosting, share links, server, OS share target. | Explicitly undecided/optional in plan; no code or service. | Do not build until a specific product decision; no implied cloud save. |
| 10-09 | exclusion | long-form document, paper/lecture templates, body/page ownership, bibliographic features, automatic solving/proving/graphing. | Explicit exclusions in plan §1.3/§2.1. | Keep excluded; retain math group/row layout only. |
| 10-10 | bonus | 이모지/Unicode 브라우저는 사용자 요청 보너스 기능이며 핵심 완료 기준은 아님. | 구현 및 하위 slice 통합 승인: `CuratedEmojiCatalog`, `UnicodeScalarBrowser`, glyph probe, `SymbolPaletteViewModel`; 최종 빌드 경고0·오류0, 신규9/9·전체120/120. | 실제 glyph 결과는 단일 환경 관측이며 미지원 글리프를 대체하지 않는다. 구버전 앱 런타임·OS 입력은 미검증. |

## 개발 단위 상태와 다음 우선순위

1. **P1 완료 slice:** 파일 수명주기 보호, 저장-Undo ticket, 저장 service 및 VM 자동 복구·최근 작업 연결은 각 QA의 합격 범위에서 승인됐다. 기존 근거와 한계는 각 QA 문서에 남아 있으며 실제 OS 입력과 사용자 AppData는 계속 미검증이다.
2. **P2 승인 slice — 10종 구조 템플릿:** 기존 4개 구조 명령과 아래첨자·적분·합·극한·괄호·경우 나누기를 포함한 10종 API/AST/UI 경로가 통합 승인됐다. Debug 빌드 경고0·오류0, 신규 논리12+Headless UI1 통과, 기존98 포함 전체111/111이다. 실제 Tab/Shift+Tab은 Headless 합성 키로만 검증했다. 2026-10-22 15:30:56 UTC 셸 캡처 4개에서 최소/기본 viewport 257px/437px 및 STUB/0을, 15:30:58 UTC 완성 템플릿 이미지에서 흰 배경 199×827·bounds 일치·rowCount10을 메인이 확인했다. 전체 결과와 실패 이력은 [P2 템플릿 QA](verification/P2-structure-templates-QA.md)에 있다. 7개 기호 분류, Properties/Structure 전체, 직접 LaTeX 입력/parser, 구버전 앱 실행, OS 물리 입력과 P2 전체는 미완료다.
3. **P2 일부 slice 승인 — 구조 템플릿·기호/Unicode 팔레트:** 템플릿 10종은 논리12+Headless UI1, 전체111/111로 통합 승인됐다. 기호/Unicode 팔레트는 최종 빌드 경고0·오류0, 논리8+Headless UI1 9/9, 전체120/120이며 메인 시각 검토까지 통합 승인됐다. 상세 로그·실패 이력·각 실행의 구분된 PNG/bounds는 [P2 템플릿 QA](verification/P2-structure-templates-QA.md), [P2 팔레트 QA](verification/P2-symbol-palette-QA.md)와 `doc/verification/artifacts/`에 있다. OS 물리 입력, IME와 글꼴별 보편 지원은 미검증이다. Properties/Structure 전체·코드 parser 등 P2 전체는 미완료다.
4. **P2 slice 승인 — 구조 탐색·범위 비우기:** `STRUCT-PROJECTION`, `STRUCT-SELECT-PURITY`, 6개 CLEAR 논리 사례와 `STRUCTURE-CLEAR-UI`가 통과했다. Debug 빌드 경고0·오류0, 신규9/9, 전체129/129이며 메인은 신규9 실행의 17:13:50–17:13:52 UTC UI PNG/bounds 7쌍을 검토했다. 전체129 실행에서 17:14:24–17:14:26 UTC에 생성된 별도 7쌍은 자동 산출물이다. 상세 이력은 [P2 구조 탐색·Clear QA](verification/P2-structure-clear-QA.md)에 있다. 네이티브 OS 창/입력·UIA·IME는 미검증이다.
5. **P2 Properties — 승인:** 크기·글꼴·색 세 필드와 네 scope의 논리8+Headless UI1이 Debug 빌드05 경고0·오류0, 신규9/9·exit0, 전체138/138·exit0으로 통과했다. 메인은 신규9-04 PNG/bounds 8쌍을 직접 검토했으며 전체138-01 PNG/bounds 8쌍은 별도 자동 산출물이다. 초기 빌드 오류4와 UI helper의 컨트롤 이름·viewport containment 실패는 원로그로 보존한다. `Qa.cs` fingerprint에는 node/row/group override가 포함되고, `PropertiesUiInteractionCases.cs`의 WorkspaceDefault explicit 세 값 검사는 QA가 좁게 추가한 뒤 UI 소유자인 font에게 인계했다. 빈 patch/공백 draft, Core 공백 글꼴 거절, WorkspaceDefault root style, 여섯 템플릿 빈 content ID/style/focus/undo도 검증했다. 2026-10-22 17:24·17:39·17:54 UTC 감사는 절차 기록이지 테스트 근거가 아니다.
6. **P2 조판 preset — 승인:** Display/Text/Script/ScriptScript, schema v1 additive preset, 4필드 combined Apply/Reset, `.71^depth`·explicit size anchor, 구조별 자식 전이, 큰 첨자/연산자 bounds, aligned LaTeX scope와 SVG 소비를 논리8+Headless UI1로 확인했다. build05 경고0·오류0, 신규9/9·exit0, 기존138 포함 전체147/147·exit0이다. 메인은 신규9-02의 18:31:28–29 UTC 셸 PNG/bounds10과 비교식 PNG4/JSON을, 신규9-03 18:36:24 UTC의 final-default-top·detail-row-script·stale-bottom PNG3와 같은 실행 bounds10을 직접 검토했다. 전체147-03의 18:37 UTC PNG14/JSON은 별도 자동 산출물이다. 실패 이력과 정확한 manifest는 [P2 조판 QA](verification/P2-typesetting-QA.md)에 있다. 18:09·18:24·18:39 UTC 감사는 절차 기록이다. 외부 Display/Inline 미리보기, parser/외부 TeX, 구버전 앱 및 OS 물리 입력은 미검증.
7. **P2 slice 승인 — 외부 Display/Inline 미리보기:** WorkspaceStyle/Display/Inline 투영, frozen layout/SVG, stale·미완성·저장 경계를 논리8+Headless UI1로 확인했다. build04 0/0, 신규9/9, 전체156/156 및 모두 exit0이며, 메인은 신규9-03의 PNG/bounds 6쌍을 직접 검토했다. 전체156-01의 별도 6쌍은 자동 산출물이다. 실패 이력과 실행별 자료는 [P2 출력 미리보기 QA](verification/P2-output-preview-QA.md) 및 [manifest](verification/artifacts/p2-output-preview/output-preview-final-manifest.md)에 있다. P2 나머지, 외부 TeX/구버전 앱과 OS 물리 입력은 미검증/미완료다.
8. **P2 텍스트 역할 — 승인된 하위 slice:** `MathTextRole` schema v1 기본값/검증, role-aware shaping/LaTeX·C0/C1 진단·grapheme 정렬, 원자 속성 적용과 편집/복구 보존을 논리8+`TEXT-ROLE-UI`1로 검증했다. build05 경고0·오류0, 신규9/9, 전체165/165 및 실제 exit0으로 메인이 논리·Headless·offscreen 범위를 승인했다. 신규9-02/03의 직접 리뷰 범위와 full165 자동 산출물은 [실행 manifest](verification/artifacts/p2-text-role/text-role-final-manifest.md)에 분리 기록했다. 19:39·19:54·20:09 UTC 감사는 절차 기록이다. 외부 TeX/parser, LaTeX 역할 provenance 역변환, 실제 OS/IME/clipboard/AppData와 전체 P2/P1/P0는 미검증/미완료.
9. **P3 레이아웃:**
10. **P4 재사용/내보내기:**
11. **P5 통합/배포:**

## 현재 P1 파일 수명주기 보호 QA

계약과 API는 메인 승인 및 엔진 협의를 마쳤고 VM·UI·QA 소스는 저장되어 정적 검토를 거쳤다. FIFO 저장 경합, 취소된 대기 작업 건너뛰기, Close I/O drain 중 편집, stale completion, 성공 Open 뒤 저장 재개를 포함한 신규 9/9와 전체 회귀 79/79가 통과했고 메인이 시각 산출물을 확인해 이 단위를 통합 승인했다. 상세 실행 기록은 [P1 lifecycle QA 기록](verification/P1-lifecycle-protection-QA.md)에 둔다.

| 케이스 | 검증 경계 | 현재 준비된 단언 |
|---|---|---|
| `VM-OPEN` | 손상/스키마/없는 파일 및 dirty valid Open | 실패는 원 Session/path/selection/history를 보존한다. dirty valid Open은 pending confirmation을 만들고 명시적 Discard 뒤에만 교체한다. |
| `LIFECYCLE-CLEAN` | clean New/Open/Close | 새 Workspace ID와 전체 고유 ID, 최소 그룹·행·필수 빈 슬롯, 경로/history 초기화, clean Open 뒤 저장 재개 및 일회성 Close 승인. 통과. |
| `LIFECYCLE-NEW` | dirty New의 Save/Discard/Cancel | 취소는 원 상태 보존, Discard는 새 빈 작업, Save는 이전 snapshot 저장 후 새 작업으로 전환. 통과. |
| `LIFECYCLE-OPEN` | dirty Open의 Save/Discard/Cancel 및 같은 대상 경로 | 취소 중 원 상태 유지, 저장 뒤 기존 파일 보존 후 후보 파일로 전환, 같은 경로면 저장된 새 snapshot을 활성화. 통과. |
| `LIFECYCLE-CLOSE` | dirty/clean Close 및 승인 token | 취소/Discard, 단일 소비, revision/navigation 변경 시 token 무효화. 통과. |
| `LIFECYCLE-FAIL` | pending Save 실패·취소·재시도 | 파일 원본, 세션·경로·history 및 pending 보존, 재시도 성공 때만 action 전환. 통과. |
| `LIFECYCLE-SAVE-EDIT` | 저장 snapshot, A→B FIFO, 취소된 queue slot | A 저장 중 편집 B와 pending 저장, 최종 파일 순서, B 저장 전 Close 금지, 취소 직후 busy 해제·새 요청 허용 및 늦은 결과 폐기. 통과. |
| `LIFECYCLE-SAVE-UNDO` | 일반 Save(A) 진행 중 Undo, New/Open/Close confirmation·Cancel 보존, Save(old) FIFO | pending New/Close 전 A가 실제 기록되고 뒤의 old snapshot 저장으로 최종 파일 old인지, Close token이 1회 발급·소비되는지 확인. 신규2/2 및 통합 전체89/89 통과. |
| `LIFECYCLE-SAVE-CANCELLED-WRITE` | 취소를 무시하는 I/O의 늦은 성공, 실제 취소·실패 ticket 정리 | 취소 뒤 실제 파일이 A로 쓰여도 saved marker/path/status 불변 및 재요청 보호, 실패/실제 취소 후 FIFO Open barrier로 ticket 제거를 확인. 신규2/2 및 통합 전체89/89 통과. |
| `LIFECYCLE-ASYNC-STALE` | 늦은 Open 및 Close I/O drain | New/Close 요청 뒤 늦은 Open 폐기, Close drain 중 편집 시 승인 발급 안 함·busy 종료, dirty Close 재요청 허용. 통과. |
| `LIFECYCLE-REENTRY` | 중복 요청/해결 | pending 중 두 번째 New/Open/Close 거부, Open 거부 시 service 미호출, 해결은 한 번만 반영. 통과. |
| `LIFECYCLE-UI` | 실제 App/MainWindow Headless, TempDirectory | 저장 확인 overlay와 New/Close 명령을 실제 VM/API로 검증하고 경로를 직접 주입한다. Headless UI 1/1 통과; 메인이 7개 화면 PNG와 bounds를 검토해 이 단위를 시각 승인. |

논리 8개와 UI 1개를 추가하고 기존 회귀 70개를 유지해 총 79개를 목표로 한다. `Program.cs`는 `--lifecycle-ui-headless LIFECYCLE-UI` child를 `App`+Avalonia Headless+Skia로만 초기화하고, `Show()` 전후 platform handle이 `STUB`/`IntPtr.Zero`인지 검사한다. 네이티브 OS 창·파일 선택기·사용자 포커스·UIA·물리 키보드/마우스 입력은 금지한다. STUB/0 Headless `Show()`는 허용하며 이를 물리 입력 합격으로 표시하지 않는다. TempDirectory를 통한 파일 I/O만 사용한다.

파일 서비스 wrapper는 `IWorkspaceFileService`의 실제 저장/열기를 호출하고 `TaskCompletionSource` gate만 추가한다. 임의 대기나 private reflection/source-string 검사는 사용하지 않는다. 공유 FIFO 큐가 A→B 저장과 취소된 queue slot을 직렬화하고, Close 승인은 앞선 I/O drain과 현재 Session/revision/navigation generation 이후에만 발급되는지 확인한다.

2026-10-22 약 12:41–12:44 UTC와 약 12:57–13:00 UTC의 Watchdog 점검 동안 전원 작업을 정지했다. 두 감사 모두 중단급 문제를 보고하지 않았으며, 명시적 재개 후 QA 경계와 저장 경쟁을 보완했다. 설정 파일은 수정하지 않았다.

**lifecycle 결과: 이 하위 단위는 Debug 빌드 경고 0·오류 0, 신규 9/9, 전체 79/79 및 메인 Headless 화면 검토로 통합 승인됐다. 이는 전체 P1/P0 완료를 뜻하지 않는다. 실제 OS 창·파일 선택기·포커스·UIA·IME·물리 입력·다른 플랫폼은 미검증이다. 이후 저장 기반 slice는 별도 8/8·전체87/87을 통과했지만 VM 사용자 흐름은 완료되지 않았다.**

## 현재 P1 복구본·최근 작업 저장 서비스 QA

엔진이 명시 root 기반 `WorkspaceStateService`와 모델을 구현했다. QA는 [P1 recovery/recent QA 기록](verification/P1-recovery-recent-QA.md)의 계약에 따라 `WorkspaceStateCases.cs` 8개와 `Program.cs` 등록을 작성했다. 기존 79개를 유지한 전체 87개 회귀가 통과했다. 신규 케이스는 경로 중복·UTC 정규화·고정 및 20개 상한·파일 메타데이터·손상/미래 index 보호·복구 snapshot 왕복·ID/AST/path 검증·동시 RMW·잠금/atomic 교체 실패 보존을 다룬다.

최종 로그는 `doc/verification/artifacts/p1-workspace-state/build-workspace-state-02.log`(경고0·오류0), `state-new-8-02.log`(8/8), `regression-full-87-01.log`(87/87)이다. 초기 `build-workspace-state-01.log`는 CS8620 경고 1건을 기록했고, 엔진이 private envelope의 nullable 요소 형식을 명시한 뒤 재빌드가 경고 없이 통과했다. 초기 8/8 로그 `state-new-8-01.log`도 이력으로 보존한다. 메인은 최종 build02/신규8/전체87과 저장 서비스 코드를 확인해 명시 root 저장 계층을 통합 승인했다.

**저장 서비스 기반 단위는 통합 승인됐다. 이어진 VM 자동 복구 타이머·복원 확인·최근 작업 연결도 Debug 빌드 경고0·오류0, 신규9/9·전체98/98 및 메인 Headless 화면 검토로 통합 승인됐다. 테스트 파일은 `TempDirectory`에 격리했다. 실제 사용자 AppData, 네이티브 OS 창/파일 선택기 및 물리 입력은 사용하지 않았다. Home 시작 화면·썸네일·전체 P1/P0 완료를 뜻하지 않는다.**

일반 Save(A) 지연 중 Undo 뒤 New/Open/Close 보호는 same-session StateId ticket과 파일 큐를 통해 처리하며, 실제 실패·취소 ticket 정리 및 취소를 무시한 늦은 성공도 검증됐다. Debug 경고0·오류0, 신규2/2, 통합 전체89/89 후 메인이 통합 승인했다. 근거는 [P1 lifecycle QA 기록](verification/P1-lifecycle-protection-QA.md)의 ticket 후속과 `doc/verification/artifacts/p1-lifecycle-save-ticket/` 로그다. 뒤이은 VM 자동 복구·명시 복원·최근 작업 연결도 전체98/98과 UI 시각 검토로 통합 승인됐다.

## P1 VM 복구·최근 작업 통합 QA 승인

최종 동결 소스에서 Debug 빌드05 경고0·오류0, 신규 논리8+UI1 9/9, 기존89를 포함한 전체98/98이 통과했다. 메인은 전체98 실행의 14:43:45–14:43:47 UTC에 생성된 PNG 8개와 bounds를 직접 검토해 1060×700·1360×880 접근성, 긴 미리보기 필드 가독성, 최근 Open guard·복원·삭제 확인·손상 진단 화면, 겹침 없음, `STUB`/0 상태와 최소 viewport 257px을 확인하고 이 P1 VM/UI 단위를 통합 승인했다. 실행 로그와 시각 산출물은 `doc/verification/artifacts/p1-recovery-recent/`에 있다. 두 초기 컴파일 실패와 두 UI 실패를 포함한 전체 이력은 [P1 복구·최근 작업 VM 통합 QA](verification/P1-recovery-recent-integration-QA.md)에 기록했다. 모든 파일 I/O는 `TempDirectory`에 격리했다. 네이티브 OS 창·파일 선택기·포커스/UIA·물리 입력은 금지했고, 전용 child의 `STUB`/0 Headless `Show()`만 사용했다.

2026-10-22 약 14:19–14:20 UTC와 14:35–14:36 UTC Watchdog 감사 동안 모든 작업을 정지했다. 두 감사 모두 중단급 문제는 없었고 감사 종료 뒤 메인이 담당자별 재개를 명시했다. 최종 단위 통합 승인은 이 VM/UI slice에 한정되며 전체 P0/P1 완료나 물리 OS 입력 합격을 뜻하지 않는다.
