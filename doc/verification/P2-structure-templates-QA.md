# P2 구조 템플릿 QA 계획

- 상태: 10종 템플릿 하위 단위가 논리·Headless·offscreen 범위에서 통합 승인됐다. Debug 빌드 경고 0·오류 0, 신규 13/13, 기존 98개를 포함한 전체 111/111이 통과했다.
- 기준선: 기존 98개 회귀를 보존했다. 신규 논리 사례는 12개이며 실제 `App`/`MainWindow`를 사용하는 Headless UI 1개를 포함한다.
- 최종 실행 근거: `artifacts/p2-templates/build-p2-templates-04.log`, `template-new-13-02.log`, `regression-full-111-01.log`; 각 단계 종료 코드 0.
- 격리: 파일 I/O는 테스트 `TempDirectory`만 사용한다. UI는 전용 Avalonia.Headless + Skia 자식에서만 `Show()`를 허용하고 표시 전후 플랫폼 handle `STUB`/0을 확인한다.

## 승인된 모델과 명령 계약

`AetherMath.Editing.MathTemplateKind`는 `Fraction`, `Radical`, `Superscript`, `Subscript`, `Integral`, `Summation`, `Limit`, `Matrix`, `Brackets`, `Cases`의 10개 값을 가진다. 공용 삽입 API는 `EditingSession.InsertTemplate(kind)`이며 기존 분수·근호·위첨자·행렬 wrapper도 유지한다. 기본 괄호는 `Parentheses`다.

새 AST discriminator는 `$kind`의 `integral`, `summation`, `limit`, `brackets`, `cases`다. `.aethermath` schema version은 1을 유지하고 새 구조를 additive discriminator로 저장한다. 이 QA는 현재 serializer가 알 수 없는 discriminator를 거절하는 경계를 확인할 뿐, 구버전 애플리케이션을 실행하거나 구버전 바이너리 호환을 검증하지 않는다.

필수 child는 null일 수 없다. 적분은 `Integrand`와 `DifferentialVariable`, 합은 `Body`, 극한은 `Approach`와 `Body`, 괄호는 `Content`, 경우 나누기는 각 행의 `Expression`과 `Condition`이 필수다. 적분·합 하한/상한만 nullable이다. 기본 적분·합 템플릿은 두 bound 모두 필수 빈 슬롯으로 시작하고, bound가 없는 AST는 null을 유지한다. 경우 나누기 기본 행은 두 개다.

MathTree child 순서는 적분 하한→상한→피적분식→미분 변수, 합 하한→상한→본문, 극한 접근식→본문, 괄호 내용, 경우 나누기 행별 식→조건이다. `TPL-NAV`는 이 논리 순서와 grapheme caret 경계를 확인한다. 실제 Tab/Shift+Tab 이벤트 동작은 `TEMPLATE-UI`에서 합성 키 입력으로 별도 검사한다.

LaTeX는 적분·합의 본문을 brace group으로 감싸고 nullable bound의 해당 첨자만 생략한다. Angle 괄호는 인접 영문자와 control word가 합쳐지지 않도록 닫는 경계를 출력한다. LaTeX 코드 미리보기는 빈 필수 슬롯을 표현할 수 있으므로 필수 슬롯 거절의 근거로 쓰지 않는다. 완성되지 않은 구조는 layout 진단과 SVG 출력 경계에서 거절하는지 확인한다. LaTeX 파일 내보내기나 입력 parser의 완료를 주장하지 않는다.

## 논리 사례 12개

| ID | 검증 범위 |
|---|---|
| `TPL-CATALOG` | 공개 enum의 10개 값, 공용 삽입 API의 반환 ID·AST 종류·caret/history, 기존 네 wrapper 호환 및 잘못된 enum 거절. |
| `TPL-SHAPES` | 새 여섯 템플릿의 필수 빈 슬롯·nullable 여부·기본 괄호·두 Cases 행·초기 caret. |
| `TPL-WRAP-TEXT` | 정방향·역방향 Unicode 부분 선택을 10종으로 감싸고 주변 문자, 문자소 순서, 새 ID와 선택 정리를 보존. |
| `TPL-WRAP-NODE` | 전체 구조 선택을 각 템플릿으로 감싸고 대상 구조/주변 식을 유지하며 undo/redo에서 caret·selection을 복구. |
| `TPL-BOUNDS` | null/한쪽 bound의 LaTeX·JSON 왕복, optional null과 필수 빈 슬롯의 구분, 별도 WorkspaceStateService 복구 envelope 왕복에서 10종 AST·ID·style·null-bound 보존. |
| `TPL-BRACKETS-CASES` | 괄호 네 종류의 서로 다른 출력, Cases의 행별 식/조건 입력 격리와 serialization 순서. |
| `TPL-NAV` | 새 구조를 포함한 깊은 중첩 child 순서, Unicode grapheme caret 경계 및 구조 node의 caret-stop 부재. 실제 키 이동은 UI case에서만 판정. |
| `TPL-HISTORY` | 모든 새 노드의 깊은 row clone에서 전역 ID 재발급·selection/caret offset 매핑, Cases 삭제와 undo/redo 복원. |
| `TPL-JSON` | 기존 schema v1 JSON fixture 읽기/재저장 및 기존 10종 템플릿 AST의 새 discriminator 왕복. 구버전 앱 실행은 포함하지 않음. |
| `TPL-JSON-REJECT` | Cases default/누락/null/빈 행 및 null child, 필수 child null, invalid bracket enum, 중복 child ID, 미지 discriminator 거절과 입력 파일 바이트 보존. |
| `TPL-LATEX` | 구조별 LaTeX 의미 token·순서, null bound 생략, 적분/합/Angle 괄호의 control-word 경계. 이는 serializer 출력 검사이며 parser나 파일 export 검사가 아님. |
| `TPL-LAYOUT-SVG` | node/glyph bounds, operator와 bound·body 간 겹침, 긴 중첩 bound, limit 접근식, 같은 `LayoutResult`의 SVG 좌표·순서, 빈 필수 슬롯 SVG 거절 및 기존 출력 파일 보존. |

## Headless UI 사례

`TEMPLATE-UI`는 실제 앱 리소스와 `MainWindow`를 로드한다. 10개 도구의 command binding·AutomationId·tooltip, 동일 Session에서의 삽입과 빈 슬롯 입력, 합성 Tab/Shift+Tab leaf 이동과 AST/revision 불변, 입력 undo/redo, 템플릿 도구 스크롤의 위·아래 접근성을 검사한다. 최소 1060×700과 기본 1360×880 화면의 offscreen 이미지/bounds 및 `MathEditorSurface`를 사용한 완성 템플릿 10종 PNG/bounds를 생성했다. 창은 전용 Headless child 안에서만 표시하고 `STUB`/0 guard를 유지했다.

Headless 키와 클릭은 실제 OS 입력이 아니다. 네이티브 OS 창·포커스·UIA·물리 키보드/마우스·IME·실제 사용자 AppData 및 구버전 앱은 이 단위의 범위 밖이며, 이 계획에서 합격 처리하지 않는다.

## 정적 검토·최종 실행 결과

테스트 실행 전 정적 검토에서 `TPL-NAV`는 실제 중첩 fixture의 `IntegralNode` ID를 사용하고, 구조 node에는 leaf의 caret stop이 없음을 확인하도록 했다. 행/그룹 clone 소비자도 Integral/Summation/Limit/Brackets/Cases를 순회해 하위 ID를 검증하도록 확장했다. 레이아웃 사례의 필수 빈 슬롯 오류 문구는 SVG 출력 경계로 정확히 한정했다. 기존 schema v1 JSON literal은 JSON 파서로 따로 검사해 그룹 1개·행 1개·root child 2개의 유효한 구조임을 확인했고, tuple 배열 초기화 두 곳의 C# 중괄호 문법도 바로잡았다. 이 정적 확인은 제품 파일 호환성이나 빌드 통과의 증거가 아니다.

실행 순서는 Debug 통합 빌드 → 신규 13건 → 전체 111건이었다. 첫 `build-p2-templates-01.log`는 XAML literal brace 파싱 실패, `build-p2-templates-02.log`는 QA helper의 익명 객체 `Kind` 중복으로 실패했다. XAML brace escape와 fingerprint의 `BracketKind` 분리 뒤 `build-p2-templates-03.log`가 경고 0·오류 0으로 통과했다. 첫 `template-new-13-01.log`는 `TPL-JSON`의 legacy fixture에서 `alignment`/`rowSpacing`을 row에 둔 오류로 12/13이었다. 두 필드를 `EquationGroup`에 옮겨 fixture를 고쳤고, 최초 로그는 보존했다.

첫 화면 검토에서 delimiter 곡선이 본문 영역을 침범하고 Parentheses와 Cases 형태가 충분히 구분되지 않는 점을 찾아, 레이아웃 reserved column/gap/stroke 경계를 개선했다. QA 캡처는 흰 배경과 실제 control bounds를 사용하도록 보완했다. 이 수정 뒤 최종 `build-p2-templates-04.log`는 경고 0·오류 0, `template-new-13-02.log`는 13/13, `regression-full-111-01.log`는 111/111이며 모두 종료 코드 0이다. 모든 초기 실패 로그와 최종 로그는 `doc/verification/artifacts/p2-templates/`에 보존했다.

전체111 실행의 2026-10-22 15:30:56 UTC 셸 캡처 4개(최소 1060×700 및 기본 1360×880의 위·아래 상태)와 bounds는 `artifacts/p2-templates/templates-min-top-1060x700-20261022T153056620Z-e17258ae6bf6422ba7914a4a59f6a4a0/`, `templates-min-bottom-1060x700-20261022T153056680Z-ba1b601f34d34f8fb7193fca132d9048/`, `templates-default-top-1360x880-20261022T153056712Z-a42695a83d3b406b999f09ff52b89856/`, `templates-default-bottom-1360x880-20261022T153056757Z-234d09074e1f41829209e1dc453c1367/`에 있다. 메인은 이를 검토해 STUB/0, 최소·기본 viewport 257px/437px, 도구와 편집 영역의 접근성 및 비겹침을 확인했다. 15:30:58 UTC의 `artifacts/p2-templates/template-examples-20261022T153058731Z-249bf9437ae14876a69b60cb147c1831/template-examples.png`와 `template-examples-bounds.json`은 흰 배경 199×827, control bounds 일치, rowCount 10을 기록한다. 메인은 괄호가 내용과 분리되고 Parentheses와 Cases 모양이 식별됨을 확인했다.

2026-10-22 15:23–15:24 UTC Watchdog 감사 동안 모든 작업을 정지했고 중단급 문제는 보고되지 않았다. 이 감사 뒤 실행을 재개했다. 이번 승인은 논리·Headless 합성 입력·offscreen 렌더링 범위에 한정된다. 네이티브 OS 창/포커스/UIA/물리 입력, 사용자 AppData, 구버전 앱 런타임, LaTeX 입력 parser, 모든 글리프의 pixel equality는 검증하지 않았다. Schema v1의 additive discriminator와 현 serializer 거절 경계는 확인 범위이나 구버전 바이너리 호환 실행은 아니다. 7개 기호 분류, Properties/Structure 전체, 직접 LaTeX 편집, 나머지 P2/P0 및 제품 전체는 미완료다.
