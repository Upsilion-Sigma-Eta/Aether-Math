# P2 기호·Unicode 팔레트 QA 계획

- 상태: 최종 Debug 빌드 04는 경고 0·오류 0, 신규 9개(논리 8+`PALETTE-UI` 1)는 9/9, 기존 111개를 포함한 전체 120개는 120/120이며 각 종료 코드는 0이다. 메인은 16:18 UTC 대표 이미지 10개와 16:22 UTC 이미지 6개 및 최신 신규9 bounds를 검토해 우측 11px inset 이후 두 라디오 레이블이 내부 가로 범위에 들어오는 것을 확인했고, 2026-10-22 이 slice를 논리·Headless·offscreen 범위에서 통합 승인했다. 최신 전체120 실행의 16:23 UTC PNG/bounds 22쌍은 별도 자동 산출물이며 메인이 전부 시각 검토한 세트는 아니다.
- 기준선: 승인된 P2 템플릿 포함 회귀 111개. 신규 목표는 논리 8개와 실제 `App`/`MainWindow` Headless UI 1개를 더한 전체 120개다.
- 격리: UI는 전용 Avalonia.Headless + Skia child에서만 표시하고 `Show()` 전후 platform handle `STUB`/0을 확인한다. 파일 저장·재열기는 테스트 `TempDirectory`를 사용한다.
- 산출물 경로: `doc/verification/artifacts/p2-symbol-palette/`.

## 고정된 API·데이터 계약

순수 catalog/model은 `AetherMath.Editing`에 있고 VM은 `AetherMath.Desktop`에 있다. `MathSymbolCategory`는 Algebra, Calculus, LinearAlgebra, GreekLetters, Logic, Arrows, Structures의 7종이다. `SelectedCategory`는 비-nullable이며 기본값은 Algebra이고 전체 category 모드는 없다. 잘못된 category enum은 no-op이다. `MathSymbolCatalog`는 안정 ID로 canonical 항목을 찾고, 구조 category는 `MathTemplateKind` 10종으로 빠른 삽입과 같은 `EditingSession.InsertTemplate` 경로를 사용한다. 항목 ID는 전역 유일해야 하지만 alias는 전역 유일할 필요가 없다. 같은 alias가 여러 category에 있을 수 있고 검색 결과는 항목 ID 기준으로 중복 없이 보존한다.

`UnicodeScalarBrowser`는 128 scalar/page, 전체 1,112,064 scalar·8,688 page, 0-based page index 0..8687이다. Unicode scalar 순번에서 surrogate 영역을 건너뛰며 page 431 마지막은 U+D7FF, page 432 첫 scalar는 U+E000, 마지막 page는 U+10FF80..U+10FFFF이다. 순수 `GetPage`에 잘못된 page index를 주면 `ArgumentOutOfRangeException`이다. VM 사용 UI에는 페이지 번호 직접 입력이 없고 이전/다음 및 `BrowseUnicodeRangeCommand`를 사용한다. 테스트 경계의 `SetUnicodePageIndex(int)`는 범위 안 값만 받아 페이지 목록·조회 상태를 전환하며 범위 밖 값은 false/no-op이다. 빠른 범위는 `unicode.range.math`, `unicode.range.greek`, `unicode.range.hangul`이며 해당 시작 scalar가 속한 page로 이동한다. 성공한 U+ 조회가 현재 page 밖이면 `VisibleUnicodeEntries`에 조회 결과 한 건을 표시하고 query/page 변경 뒤 현재 page 목록으로 복귀한다.

U+ 조회는 trim 뒤 대소문자 무관한 `U+`와 ASCII 16진수 1~6자리만 받는다. 빈 조회는 결과·오류·선택을 지우고 현재 page를 유지한다. 잘못된 문법·범위 초과·surrogate는 한국어 진단과 빈 결과를 설정하고 page를 유지한다. 유효하지만 직접 삽입할 수 없는 scalar는 선택해 이유를 볼 수 있으나 안전 placeholder를 표시하고 삽입 명령만 비활성화한다. category/search/page/selection/lookup 탐색은 workspace, revision, caret, selection, dirty, undo/redo를 바꾸지 않는다.

`CuratedEmojiCatalog`의 ZWJ, variation selector, skin-tone 문자열은 원문 그대로 입력한다. JSON/LaTeX 검사는 원문 보존만 입증하며 임의 glyph의 렌더 지원을 의미하지 않는다. 글리프 부재는 특정 설치 폰트에 없는 scalar를 가정하지 않는다. QA의 `IMathGlyphCoverageProbe` 결정적 대역은 선택한 항목에 `Missing`/0 glyph를 반환하도록 하고 원문 보존·진단만 검증한다. 별도 Desktop probe는 실제 설치 폰트의 관측 결과를 기록할 수 있지만 결과를 보편적인 부재/지원 보장으로 해석하지 않는다.

## 논리 사례 8개

| ID | 검증 범위 |
|---|---|
| `PAL-CATALOG-IDENTITY` | 7 category, canonical ID 전역 유일성, category 항목 ID 중복 없음, 각 entry의 Text/TemplateKind 상호 배타성 및 Structures 10종-템플릿 대응. alias의 전역 유일성은 강제하지 않는다. |
| `PAL-SEARCH-ALIAS` | 이름·alias 기반 검색, category 제한/초기화, 빈 검색과 무결과. alias가 실제로 공유되면 관련 항목 모두 조회되며 결과 ID는 중복되지 않는지 확인한다. |
| `PAL-UNICODE-PAGES` | 128×8,688 총량, page 첫/끝과 surrogate 경계, 마지막 U+10FFFF, scalar→page mapping 및 범위 밖 page 인자 거절. |
| `PAL-UNICODE-LOOKUP` | trim/접두부 대소문자/U+0000·U+10FFFF 조회, page 밖 결과 표시, blank 상태 초기화, malformed·비ASCII hex·범위 초과·surrogate 진단, 금지 category의 safe placeholder·DisabledReason·삽입 거절 및 첫/마지막 page no-op. |
| `PAL-INSERT-REPLACE` | canonical symbol ID 삽입이 실제 Session의 선택 범위만 교체하고 이웃 Unicode 텍스트와 Session identity를 보존하는지; NewWorkspace 후 동일 palette callback이 새 Session에만 입력하고 이전 Session을 보존하는지; 잘못되거나 변조된 ID/int 명령은 history/dirty/focus를 바꾸지 않는지. |
| `PAL-EMOJI-GRAPHEME` | curated VS/ZWJ/skin-tone 표본의 정확한 code-unit 문자열 삽입, grapheme caret 위치, 한 번 undo/redo 및 이웃·선택 원복. |
| `PAL-BROWSE-PURITY` | mode/category/query/page/quick-range/list selection/lookup 변경과 양 끝 이전·다음 no-op에서 workspace·revision·caret·selection·dirty·history 불변; 정상 page 이동의 조회 상태 초기화. |
| `PAL-ROUNDTRIP-GLYPH` | symbol·emoji·Unicode scalar 입력 후 `.aethermath` 저장·재열기 및 LaTeX 출력의 원문 scalar 보존. Injected 0-glyph probe는 safe 진단만 제공하고 원문을 치환하지 않는지 검증한다. |

## Headless UI 사례

`PALETTE-UI`는 실제 `App`/`MainWindow`를 전용 Headless child에서 사용한다. Symbols/Emoji/Unicode mode, category·검색, Unicode 이전/다음·수학/그리스/한글 quick range, 빈 결과·잘못된 U+ 진단, non-insertable scalar 상세 이유, 페이지 밖 U+1F600 조회 결과의 선택·삽입, curated emoji sequence 삽입 및 undo를 검사했다. 좌측 palette column은 약 224px이며 기존 10 template AutomationId/binding을 보존하고, 중앙 `MathEditorSurface` viewport는 최소 257px 이상이어야 한다. 실제 합성 상위/하위 스크롤을 반영한 `palette-{structure|symbols|unicode|emoji}-{minimum|default}-{top|bottom}` 캡처를 1060×700과 1360×880에서 만들었다. 첫 전체120 실행에서 2026-10-22 16:10:41–16:10:42 UTC에 생성된 16개 쌍은 이전 화면 상태의 이력으로 보존한다. 두 번째 전체120의 16:19 UTC 22쌍도 당시 수정본의 자동 캡처로 보존한다. 최종 신규9 실행은 16:22:46–16:22:48 UTC에 구조 상단/하단을 두 창 크기에서 캡처한 4개 이미지와 Unicode 상단 2개 이미지(총 6개)를 생성했다. 메인은 이 이미지와 같은 실행의 22개 bounds를 검토해 STUB/0, 패널 210px, 최소/기본 편집 viewport 257px/437px 및 11px inset 안쪽의 라디오 레이블을 확인했다. 이 검토 PNG 6개와 bounds 22개의 파일은 [신규9 시각 검토 manifest](artifacts/p2-symbol-palette/palette-new9-final-03-review-artifacts.md)에 정리했다. 최종 전체120 실행은 16:23 UTC에 별도 22쌍을 생성했으며 각 쌍은 `main-window.png`/`bounds.json`이다. 이 전체120 산출물은 메인이 전부 시각 검토한 세트가 아니며 정확한 경로는 [최종 전체120 산출물 manifest](artifacts/p2-symbol-palette/palette-full120-final-03-artifacts.md)에 있다.

headless 합성 키/클릭은 OS 입력이 아니다. 네이티브 창·전경 포커스·UIA·물리 키보드/마우스·IME·OS clipboard·사용자 AppData 접근은 금지한다. 실제 글꼴 probe는 해당 테스트 환경의 단일 관측이며 모든 시스템 font 또는 글리프 지원을 뜻하지 않는다.

## 실행·완료 상태

첫 UI 검토 전 자동 검증 결과와 수정 후 재검증은 다음과 같다.

| 단계 | 결과 | 증거 |
|---|---:|---|
| 최초 Debug 빌드 01 | 성공, 경고 3·오류 0, 종료 코드 0 | `build-symbol-palette-final-01.log`. 세 경고는 `MainWindow.axaml`의 `TextBox.Watermark` obsolete 안내였고, UI 담당자가 `PlaceholderText`로 바꿨다. |
| Debug 빌드 02 | 성공, 경고 0·오류 0, 종료 코드 0 | `build-symbol-palette-final-02.log` |
| 신규 논리 8+UI 1 | 9/9, 종료 코드 0 | `palette-new9-final-01.log` |
| 전체 회귀 | 120/120, 종료 코드 0 | `palette-full120-final-01.log` |
| UI 수정 후 Debug 빌드 03 | 성공, 경고 0·오류 0, 종료 코드 0 | `build-symbol-palette-final-03.log` |
| UI 수정 후 신규 논리 8+UI 1 | 9/9, 종료 코드 0 | `palette-new9-final-02.log` |
| UI 수정 후 전체 회귀 | 120/120, 종료 코드 0 | `palette-full120-final-02.log`; 이전 수정본 22쌍 manifest `palette-full120-final-02-artifacts.md` |
| 라디오 레이블 수정 후 Debug 빌드 04 | 성공, 경고 0·오류 0, 종료 코드 0 | `build-symbol-palette-final-04.log` |
| 최종 신규 논리 8+UI 1 | 9/9, 종료 코드 0 | `palette-new9-final-03.log` |
| 최종 전체 회귀 | 120/120, 종료 코드 0 | `palette-full120-final-03.log`; 새 자동 생성 22쌍은 `palette-full120-final-03-artifacts.md` |

전체 회귀 로그의 실제 Desktop 글리프 관측은 표본 `Cambria Math`에서 `Present`였다. 이는 해당 실행 환경의 문자표 조회 결과이며 shaping·fallback 또는 다른 글꼴/플랫폼의 지원을 보장하지 않는다. 첫 UI 시각 검토 뒤 상세 카드·글리프 설명을 수정했고, 메인은 16:18 UTC 대표 10개 이미지에서 상세·코드값·비삽입 사유·버튼이 읽히는 것을 확인한 뒤 라디오 레이블 간격을 지적했다. UI 담당은 11px inset과 내부 가로 bounds 검사를 추가했다. 최종 신규9의 16:22 UTC 6개 이미지와 22개 bounds를 검토해 레이블/viewport를 확인한 뒤 slice를 통합 승인했다. 최종 전체120의 16:23 UTC 22쌍은 별도 자동 산출물로 보존하며, 이 전체 세트가 모두 직접 시각 검토됐다고 기록하지 않는다. 최초 경고, 실패·수정 이력, 앞선 캡처와 최종 캡처는 각각 보존한다.

headless 합성 키/클릭은 OS 입력이 아니다. 네이티브 OS 창·전경 포커스·UIA·물리 키보드/마우스·IME·OS clipboard·사용자 AppData 접근은 금지한다. 이번 승인은 논리·Headless·offscreen 범위에 한정된다. 실제 OS/물리 입력, 구버전 앱 런타임, 실제 사용자 AppData, LaTeX 입력 parser와 전체 P2/P0/P1 완료를 뜻하지 않는다.
