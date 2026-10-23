# Aether-Math 인계 문서

기준일: 2026-10-23  
저장소 상태: main 브랜치, 아직 커밋 없음. 저장소 파일은 untracked이며 스테이징하지 않았다.  
기존 자동 회귀 기준선: 205개. LaTeX 구문 강조 신규 5건을 더한 전체210 검증은 통과했으며 이번 slice는 승인됐다.

이 저장소는 문서 편집기가 아니라 구조적 수식 편집기다. P1/P2의 여러 하위 단위는 논리·Headless·offscreen 범위에서 승인됐지만 제품 전체 P0/P1/P2 완료나 실제 OS 편집을 뜻하지 않는다. 세부 상태는 [구현 상태 추적표](IMPLEMENTATION_STATUS.md)와 해당 QA 기록을 기준으로 판단한다.

## 환경과 검증 방법

- 기준 환경은 Windows, .NET SDK 10.0.402(global.json), .NET 10, Avalonia 12.1.4다.
- 계획서의 주요 명령은 다음과 같다. 새 실행은 메인 승인과 현재 실행 경계를 먼저 확인한다.

```powershell
dotnet restore AetherMath.sln
dotnet build AetherMath.sln -c Debug --no-restore
dotnet run --project tests/AetherMath.LogicQA/AetherMath.LogicQA.csproj -c Debug --no-build --no-restore
```

- 기존 UI 자동 검증은 전용 Avalonia Headless + Skia child와 offscreen 렌더를 사용하고 STUB/0 handle을 확인한다. 실제 OS 창, 사용자 전경 포커스, 물리 키보드·마우스, UIA, IME, 실제 clipboard runtime, 사용자 AppData, computer-use는 계속 보류다. Headless 결과로 물리 게이트를 통과 처리하지 않는다.
- 실제 앱 실행 명령은 계획서 §12.1에 참고로 남아 있으나, 현재 OS 경계가 해제되기 전에는 실행하지 않는다. 외부 TeX 컴파일도 검증 범위 밖이다.
- 파일 및 recovery QA는 테스트 TempDirectory만 사용한다. 운영 AppData와 사용자의 원본 파일을 대상으로 하지 않는다.

## 역할과 리뷰 경계

- 현재 운영은 메인과 세 담당(engine_input, font_visual, qa_regression)으로 진행한다. 세 담당은 GPT 6 Luna xhigh 역할로 유지한다.
- 메인은 Orchestrator로 범위·API 승인과 코드 리뷰를 맡고, 지정된 PNG/bounds를 직접 검토한다. 자동 생성 자료와 직접 본 증거를 구분한다.
- engine_input은 Core/Editing/Layout/Persistence 및 C# VM/serializer를 소유한다. font_visual은 Avalonia UI와 Headless UI 사례를 담당한다. qa_regression은 논리·Headless 검증, 단독 실행, Program 등록과 QA 문서를 맡는다.
- 별도 상시 UI 검증/Advisor 에이전트를 늘리지 않는다. 정기 감사에서는 기존 담당 한 명을 임시 Advisor/Watchdog로 전환하고, 다른 작업자는 정지한다. 감사 중 코드·문서 편집과 빌드·실행을 하지 않는다.

## 주요 구현 상태

### 수식 편집과 데이터

- 불변 수식 AST와 EditingSession이 텍스트 caret/부분 선택/구조 선택, undo/redo, 템플릿, 행·그룹 조작을 다룬다.
- 10종 구조 템플릿, 7개 기호 분류, curated emoji와 페이지 단위 Unicode 브라우저가 있다. 필수 빈 슬롯과 미완성 출력 진단을 유지한다.
- 구조 패널은 실제 MathNode ID 기반 projection을 제공한다. Clear는 Selection/CurrentRow/CurrentGroup/Workspace 범위로 동작한다.
- 속성 slice는 네 scope의 크기·글꼴·색·조판 스타일, MathTextRole, 괄호 종류, 분수 local Display pin(dfrac), 그룹 행 간격과 MathSpaceNode(Thin)를 다룬다.
- JSON schema v1 저장/열기와 dirty New/Open/Close guard, 저장 경쟁 보호, recent/recovery 수명주기가 있다. 복구는 새 dirty Session을 만들고 원본 사용자 경로에 자동 저장하지 않는다.

### 렌더링과 LaTeX

- LayoutResult는 편집 화면·SVG·외부 Display/Inline 미리보기에서 공유된다. SVG 실패·취소 시 기존 파일 보호와 미완성 수식 거절은 QA 범위에서 확인했다.
- LatexSerializer는 AST를 LaTeX 수식 조각으로 직렬화한다. 출력 구조에는 분수/근호/첨자/행렬/적분/합/극한/괄호/cases, 역할별 텍스트, 조판 preset, 얇은 공백, 논리 행의 aligned와 등호 정렬이 포함된다.
- 이 serializer는 직접 입력 허용 문법이나 parser 완성을 뜻하지 않는다. 제어 문자·필수 빈 슬롯은 외부 재사용 차단 진단이며, 글꼴/크기/색과 일부 행 간격은 LaTeX에서 표현되지 않는다고 안내한다.
- 전체 수식 코드 복사는 fake sink와 Headless 경계로 승인됐다. 실제 OS clipboard는 미검증이다.
- 이번 구현은 Editing/LatexSyntaxTokenizer.cs에 9종 토큰과 UTF-16 범위 tokenizer를 추가하고 MainWindowViewModel.LatexSyntax 불변 snapshot 및 IsLatexHighlightingEnabled에 연결했다. build05는 경고0·오류0·exit0, 신규5-04는 5/5·exit0, 전체210-01은 210/210·exit0이다. 메인은 신규5-04의 PNG 8개와 bounds 8개 핵심 필드를 직접 검토해 가독성, 일반 문자·한글·emoji, 강조 on/off, 역선택, 스크롤/reset, fake copy를 승인했다. 전체210의 PNG/bounds 8쌍은 자동 산출물이며 메인이 전부 직접 봤다는 뜻은 아니다. [P2 LaTeX 구문 강조 QA](verification/P2-latex-highlight-QA.md)와 artifact 로그에 근거가 있다. source 원문은 유지하고 parser/serializer/AST 동작은 바꾸지 않는다.

## 검증 근거와 실패 이력

각 QA 승인은 문서에 명시된 하위 계약에만 적용된다.

- [P0-R1-F05 QA](verification/P0-R1-F05-QA.md): Debug 0 경고/0 오류, 자동 회귀 50/50. 합성 포인터와 offscreen 결과이며 물리 OS 게이트는 보류다.
- [P1 행 편집 QA](verification/P1-row-editing-QA-plan.md), [P1 그룹 편집 QA](verification/P1-group-editing-QA-plan.md), [P1 lifecycle QA](verification/P1-lifecycle-protection-QA.md): 행·그룹 편집, dirty navigation과 save-ticket 경계.
- [P1 저장 서비스 QA](verification/P1-recovery-recent-QA.md), [P1 recent/recovery 통합 QA](verification/P1-recovery-recent-integration-QA.md): 임시 저장소, recent/recovery 및 자동 복구 수명주기.
- [P2 템플릿 QA](verification/P2-structure-templates-QA.md), [기호 팔레트 QA](verification/P2-symbol-palette-QA.md), [구조 탐색/Clear QA](verification/P2-structure-clear-QA.md), [기본 Properties QA](verification/P2-properties-QA.md), [조판 preset QA](verification/P2-typesetting-QA.md), [외부 출력 미리보기 QA](verification/P2-output-preview-QA.md), [분수 Display pin QA](verification/P2-dfrac-pin-QA.md): 각 선행 slice와 해당 실패·재검증 경계.
- [P2 의미 텍스트 역할 QA](verification/P2-text-role-QA.md), [그룹 행 간격 QA](verification/P2-group-row-spacing-QA.md), [얇은 수학 공백 QA](verification/P2-thin-math-space-QA.md), [괄호 종류 QA](verification/P2-bracket-kind-QA.md): schema·편집·layout·LaTeX 소비자와 실패 후 수정 이력.
- [P2 전체 LaTeX 복사 QA](verification/P2-latex-copy-QA.md): 승인 결과 build03 0/0, 신규 7/7, 전체 205/205. 최초 UI 캡처 실패와 초기 빌드 오류도 원로그로 보존되어 있다.
- [P2 LaTeX 구문 강조 QA](verification/P2-latex-highlight-QA.md): 최종 build05 경고0·오류0·exit0, 신규5-04 5/5·exit0, 전체210-01 210/210·exit0. 메인은 신규5-04의 PNG 8개와 bounds 8개 핵심을 직접 검토했고, 전체210의 8쌍은 자동 산출물이다.
- P2 선행 slice는 [구현 상태 추적표](IMPLEMENTATION_STATUS.md)의 §3.2–§3.4와 링크된 개별 QA 기록을 따른다. 전체 자동 PNG/bounds를 모두 메인이 직접 리뷰했다고 해석하지 않는다.

구문 강조 실패 이력은 각 로그로 보존한다. build01은 경고2·오류2·exit1, 신규5-01은 4/5·exit1(접힌 상태의 시각 확인 순서 문제, PNG 없음), 신규5-02는 4/5·exit1(강조 off 뒤 scroll Y가 1322에서 1250으로 변함, 실패 전 PNG/bounds 3쌍 생성), 신규5-03은 5/5·exit0이었으나 메인 PNG 리뷰에서 강조 on의 일반 텍스트 누락과 off의 전체 코드 비표시로 시각 승인을 받지 못했다. 후속 수정 뒤 build05·신규5-04·전체210-01이 모두 통과해 승인됐다. 실패 기록은 최종 통과 기록과 구분해 유지한다.

## 남은 작업과 우선순위

이번 요청 범위인 구문 강조 slice는 승인됐다. 여기서 개발을 멈춘다. 아래 항목은 다음 재개 때 검토할 계획이며 이번 승인에 포함되거나 착수 승인을 받은 일이 아니다.

1. P0에서 지원 문법표를 확정한다. 구조/역할별 출력 token과 environment, package 의존성, 수식 조각 맥락, escape·Unicode·공백 규칙, 미지원/미완성 정책과 왕복 사례를 구분한다. 입력 허용 목록과 현재 serializer 출력 subset을 혼동하지 않는다.
2. §3.4-02 WYSIWYG 공백을 구체화한다. 현재 템플릿/팔레트 범위를 넘어 코드 없이 생성·수정·배치·저장·출력할 확정 수식 기능을 P0 목록과 대조한다.
3. 직접 LaTeX 입력과 왕복은 지원 문법·오류 복구·문자열 초안/마지막 유효 트리 정책 결정 뒤에 설계한다. 미지원 명령이나 미완성 입력을 지우거나 대체하지 않는다.
4. P3 자동 wrap, 명시 줄바꿈, overflow 표시, width/zoom/DPI 분리는 미완료다. lexer/grammar 목록의 선행 조건은 아니지만 parser 오류 위치와 논리 행·화면 좌표를 연결할 때 별도 계약이 필요하다.
5. P4/P5 PNG/PDF/MathML, 출력 형식 전체 matrix, 외부 앱 소비, 배포·접근성·성능 및 실제 OS 검증이 남아 있다. 실제 OS 입력/포커스/UIA/IME/clipboard/AppData를 자동 테스트로 대체하지 않는다.

## 재개와 git 안전

- 재개 시 계획서 §3–§10, 이 문서, 구현 상태 추적표, 관련 QA의 최종 로그·실패 이력을 확인하고 메인의 현재 단위 승인을 받는다. Advisor/Watchdog 감사 동안은 작업을 멈춘다.
- 현재 branch는 main이며 HEAD 커밋이 없다. 기존 저장소와 이번 문서는 untracked 상태이며 git add/commit/push를 하지 않았다.
- 루트 `.gitignore`는 중첩 `bin/obj`, 테스트·coverage·benchmark 결과, 루트 패키지/publish 및 `/artifacts/`, IDE 개인 상태, `.env` 개인 설정과 임시·OS 파일을 제외한다. `doc/verification/artifacts/`의 QA 로그·PNG·JSON·manifest, `doc/img/`, `.omp/agents/*.md`, `AGENTS.md`, `WATCHDOG.yml`, 소스·설정과 사용자 `.aethermath` 파일은 계속 추적 가능하다. 전체를 일괄 스테이징하지 말고 대상 경로를 먼저 확인한다.
- 말줄임표 이름의 디렉터리는 Windows 경로 정규화 위험 때문에 읽기·쓰기·삭제하지 않는다.

