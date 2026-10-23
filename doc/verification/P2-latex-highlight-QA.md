# P2 LaTeX 구문 강조 QA

- 상태: 이 하위 단위는 Debug 빌드, 신규5, 전체210 및 메인 시각 검토를 통과해 통합 승인됐다. 승인 범위는 표시용 토큰화와 LaTeX 코드 표면의 논리·Headless/offscreen 동작이다.
- 기준선: LaTeX 구문 강조 추가 전 승인된 전체 회귀 205건.
- 결과: 별도 신규 실행 `latex-highlight-new5-final-04`는 논리4+`LATEX-HIGHLIGHT-UI` 1, 총5/5·exit0. 기준선 포함 `latex-highlight-full210-final-01`은 210/210·exit0이며 신규5도 모두 통과했다.
- 산출물: [실행 manifest](artifacts/p2-latex-highlight/latex-highlight-manifest.md), 실행 로그와 캡처는 `doc/verification/artifacts/p2-latex-highlight/`에 보존한다.
- 제외: parser/문법 유효성, 외부 TeX 실행, native OS clipboard·입력·포커스·UIA·사용자 AppData·computer-use.

## 고정 API와 동작 경계

제품 API는 `LatexTokenKind`(Text, Whitespace, ControlWord, ControlSymbol, Brace, Alignment, Script, Number, Comment), `LatexTokenRange(int Start, int Length, LatexTokenKind Kind)`, `LatexSyntaxSnapshot(string Source, ImmutableArray<LatexTokenRange> Tokens)`, `LatexSyntaxTokenizer.Tokenize(string)`다. `MainWindowViewModel`은 원문과 범위를 함께 가진 `LatexSyntax` snapshot 및 `IsLatexHighlightingEnabled`(기본 true)를 노출한다.

토큰 범위는 원문 UTF-16 전체를 빠짐·중첩 없이 덮고 길이 0인 범위가 없어야 한다. 유효 surrogate pair를 가르지 않으며 빈 원문, CRLF, unpaired surrogate, combining 문자를 원래 code unit 그대로 보존한다. 인접한 control word와 control symbol은 각각 구분하고, raw `%` 주석과 escape된 `%`, brace, `&`, `^`/`_`, ASCII 숫자와 공백을 표면 분류한다. 이는 표시용 어휘 분류이며 LaTeX parser나 지원 문법 판정이 아니다.

강조 on/off는 동일 원문이면 선택과 스크롤을 보존한다. 원문이 실제로 바뀌면 선택과 스크롤을 시작 위치로 초기화한다. 토글은 원문, AST, revision, StateId, dirty/history, 기존 진단, export/copy 허용 판정을 바꾸지 않는다. UI는 같은 source snapshot의 run을 유지하면서 강조색을 바꾸며, 강조 off에서도 일반 문자·한글·emoji가 읽히는 brush를 사용한다.

## 사례

| ID | 검증 경계 |
|---|---|
| `LATEX-HIGHLIGHT-COVERAGE` | 결정성, UTF-16 전체 coverage·빈틈/중첩/0길이 없음, 빈 원문, CRLF·surrogate·unpaired surrogate·combining 원문 재구성과 유효 surrogate pair 경계 보존. |
| `LATEX-HIGHLIGHT-CONTEXT` | 제어어·제어 기호, raw/escaped `%`, 주석 줄 끝, raw/escaped brace, 정렬 `&`, `^`/`_`, ASCII 숫자·공백의 분류와 미분류 입력 coverage. |
| `LATEX-HIGHLIGHT-REFRESH` | 원문 변경 시 immutable snapshot의 Source/토큰이 새 serializer 원문과 일치하며, 알림 재진입에서 코드·토큰·진단 및 허용 상태가 한 Workspace에 맞춰 공개되는지 확인. |
| `LATEX-HIGHLIGHT-PURITY` | on/off의 원문, AST fingerprint, revision/StateId/dirty/history, 진단과 허용 상태 불변. 정상 fake writer는 전체 원문을 한 번 받으며, 제어 문자 및 필수 빈 슬롯 차단도 토글 뒤 유지되는지 확인. 별도 copy slice가 맡는 layout advisory의 허용 경계는 여기서 중복 주장하지 않는다. |
| `LATEX-HIGHLIGHT-UI` | 실제 `LatexCodeSurface`의 선택 가능한 표시를 최소/기본 창 및 접힘/펼침에서 검사. 강조 토글 때 부분 선택·스크롤 보존, Unicode/combining 표시, source 변경 시 선택/스크롤 초기화, 전체 fake copy 및 진단/버튼 상태를 확인. |

UI child는 `LatexHighlightUiInteractionCases.All()`/`RunHeadlessChildAsync(string)`, case `LATEX-HIGHLIGHT-UI`, child flag `--latex-highlight-ui-headless LATEX-HIGHLIGHT-UI`, scope `p2-latex-highlight`다. Program에는 Headless 초기화 분기, child dispatch, 일반 case 목록 등록이 연결되어 있다. UI는 실제 OS clipboard가 아니라 주입된 fake writer를 사용했다.

## 실행 및 리뷰 결과

Debug `build-latex-highlight-final-05.log`는 경고0·오류0·실제 exit0이다. 신규 `latex-highlight-new5-final-04.log`는 5/5·exit0이고, 기준선 포함 `latex-highlight-full210-final-01.log`는 210/210·exit0이다. 전체 회귀는 동일 build05 결과물로 실행했다. 정확한 실패 이력과 로그별 종료 결과는 [manifest](artifacts/p2-latex-highlight/latex-highlight-manifest.md)에 기록했다.

new5-04의 PNG8개 전부와 같은 실행 bounds8개의 주요 필드(부착/좌표, STUB/0, grid/source/viewport, 원문/토큰/run, brush, 선택/스크롤)를 메인이 직접 검토했다. 일반 문자·한글·emoji가 강조 off/on 모두 표시되고, 같은 원문의 강조 전환에서 가시 행·선택이 보존되며, source 변경에서 선택·스크롤이 초기화되는 점과 fake copy 상태 안내를 확인했다. 측정값에는 source 110, collapsed viewport 257/437, expanded minimum 114, copy 상태의 grid 137·diagnostic 27·editor 87이 포함된다. off/reverse selection의 scrollY는 1322, 선택 범위는 4915–4917 및 역방향 4917–4915다.

full210-01은 신규 실행과 별개의 자동 산출물 PNG8/bounds8을 생성했다. 이 전체 실행 캡처는 메인이 직접 검토한 new5-04 세트와 혼동하지 않는다. 이전 new5-03도 자동 5/5·exit0이었지만 화면의 일반 텍스트 누락 및 off 전체 비표시로 메인 시각 검토에서 거절됐다. 이후 font의 run brush 표시 수정 뒤 new5-04가 시각 승인됐다.

실패 이력은 삭제하지 않았다. build01은 경고2·오류2·exit1, build02–05는 경고0·오류0·exit0이다. new5-01은 4/5·exit1(선택 표면 descendants 검사, 캡처 없음), new5-02는 4/5·exit1(off toggle 후 scroll 기대치 불일치), new5-03은 자동 5/5·exit0이나 시각 검토 거절, new5-04는 5/5·exit0 및 시각 검토 통과다. 실패 로그와 그 run의 자료는 [manifest](artifacts/p2-latex-highlight/latex-highlight-manifest.md)에 별도로 나열했다.

2026-10-22 23:53 UTC와 2026-10-23 00:08 UTC 감사는 절차 기록이며 테스트 통과 근거가 아니다. 이번 승인은 토큰 분류와 표시, Headless/offscreen UI 및 fake writer 경계에만 적용된다. 실제 OS clipboard, 실제 포커스·입력/UIA, 외부 TeX 호환성, parser 완성, 다른 scope·형식의 전체 copy matrix, P0/P1/P2 전체 완료를 의미하지 않는다.
