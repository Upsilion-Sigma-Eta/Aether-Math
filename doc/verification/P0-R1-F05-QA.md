# P0-R1-F05 QA 기록

## 기록 기준

- 대상: 개발 계획서 12.3에 명시된 미완료 입력·글꼴 게이트와 이에 인접한 회귀 조건.
- 제품 정의 및 완료 기준: [개발 계획서](../AetherMath_개발_계획서.md) 1.1, 3.1, 5.1, 12.2–12.3.
- 계획서 12.2의 기존 F05 결과는 이전 검증의 근거로만 인용한다. 아래에는 과거 결과와 이번 실행을 분리해 기록한다.
- 현재 작업에서는 사용자의 전경 사용을 방해하지 않도록 computer-use 도구, 네이티브/전경 창 표시, 포커스 점유, 물리 입력, UIA 자동화를 사용하지 않는다. PTR 자동화의 `Window.Show()`는 `STUB`/0을 확인한 Avalonia.Headless child에서만 호출하며 네이티브 창을 만들지 않는다. 오프스크린 렌더와 합성 컨트롤 이벤트는 물리 입력을 증명하지 않는다.

## 재사용할 검증 자산

| 기존 사례 | 재사용 범위 |
|---|---|
| `SEL-01`, `SEL-02`, `DEL-03` (`EditingCases`) | 실제 편집 명령의 중첩 구조·슬롯 격리·구조 삭제와 undo 복원 |
| `SEL-04`, `STRUCT-SELECT` (`LayoutSvgCases`) | 실제 레이아웃 좌표 hit-test, 선택 대상, 삭제 undo. 물리 포인터 입력의 합격 근거는 아님 |
| `LAY-02`, `SVG-01`, `SVG-02` (`LayoutSvgCases`) | 측정 기하와 공통 레이아웃/SVG 좌표·기울임 속성 비교 |
| `VM-OPEN`, `VM-EXPORT` (`ViewModelCases`) | 세션 교체·출력 실패에서 기존 상태와 파일 보존 |
| 계획서 12.2 | F05 당시 논리 QA 38개 합격 및 당시 UIA/별도 컨트롤 검증 기록. 이번 회귀 검증을 대신하지 않음 |

## 추가 검증 계획

### 클립보드 비동기 경계

제어 가능한 내부 reader와 awaitable 작업 경로로 다음 불변조건을 검사한다.

- 읽기 성공 시 키 입력 FIFO 순서대로 적용되고, 다음 요청은 앞 결과의 새 caret에서 시작한다.
- reader의 실패·취소는 관찰되지 않은 예외를 남기지 않고 모델, revision, caret, 선택, undo 기록에 무변경이며, 실패·취소 뒤 새 요청은 다시 성공할 수 있다.
- read 대기 중 Session 교체, 같은 Session의 revision/caret/selection/선택 구조 변화, caret 이동 후 원위치 복귀, detach 및 같은 Session 재부착은 오래된 결과를 폐기한다.
- 이미 대기열에 든 뒤 발생한 사용자 편집은 현재 read와 뒤따르는 모든 요청을 폐기하고 reader를 다시 호출하지 않는다. 저장 상태(`IsDirty`)만 바뀌는 `MarkSaved`는 입력 위치가 그대로면 paste를 보존한다.
- 각 거절 사례에서 새 Session 또는 현재 편집 상태가 그대로 보존된다.
- 위의 제어 테스트 합격과 실제 Windows에서의 Ctrl+V 성공·실패 입력은 별도 항목으로 기록한다.

### 글꼴과 화면/SVG 일치

- 글꼴 요청 순서(일반→기울임→oblique, 기울임→일반→oblique, oblique→일반→기울임)를 세 신선한 프로세스에서 분리한 프로브로 비교한다. 요청 family/style/weight/stretch, 실제 shaped-run `GlyphTypeface`·platform face·simulation, glyph ID·윤곽 해시, 레이아웃 메트릭과 SVG `font-style`을 기록한다.
- `font-family` 문자열 일치만으로 화면/SVG 시각 일치를 합격 처리하지 않는다. 실제 face가 확인되지 않거나 화면과 SVG glyph 외형 차이가 남으면 미합격으로 둔다.
- 컨트롤 이미지는 화면 표시 없이 `RenderTargetBitmap`으로 만들고, 같은 `LayoutResult`의 SVG를 저장한다. 외부 SVG 소비 확인은 별도 프로필의 Firefox `--headless --no-remote` 실행으로 제한한다.

### 물리 입력 게이트

합성 컨트롤 포인터 이벤트로 Ctrl+클릭 구조 선택, 일반 누름의 capture와 놓기 해제를 확인하고, 구조 삭제와 undo를 연결해 검사한다. 분모·행렬 셀의 논리 hit-test/직접 편집은 재사용하는 `SEL-04` 등 기존 사례와 함께 회귀 실행한다. 이들은 코드 경로의 자동 검증이며 실제 물리 입력 게이트를 닫지 않는다.

## 현재 상태

| 게이트 | 상태 | 근거 |
|---|---|---|
| Debug 솔루션 빌드 | 성공, 경고 0·오류 0 | `build-final-01.log` |
| 기존 논리·레이아웃·저장/출력 사례 | 38/38 합격 | `regression-final-50-01.log` |
| 클립보드 예외·취소·경쟁 조건 | 6/6 합격 | `regression-final-50-01.log`; CLIP-02 수정 후 단독 실행 `regression-clip02-headless-01.log`도 합격 |
| 합성 포인터·구조 편집 회귀 | 4/4 합격(전용 headless child) | `regression-final-50-01.log`; Avalonia.Headless 12.1.4 + Skia, Show 전후 `STUB`/`IntPtr.Zero`, 실제 visual-tree detach/reattach 확인. 합성 입력임 |
| 글꼴 요청 순서와 face 의미 | 3개 신선한 프로세스·세 순서 합격. Normal은 upright, 제품 Italic은 native Oblique와 같은 face·glyph 윤곽 | `font-probe-orders-final-01.log`, `font-probe-order-matrix-final-01.json`, `font-probe/font-{normal,italic,oblique}-first-20261022T112925.../font-probe-*.json` |
| 컨트롤 PNG와 동일 LayoutResult SVG | 오프스크린 생성·외부 headless 소비 1/1 합격 | `font-offscreen-final-01.log`; 최종 50개 실행에서도 재합격. `font-probe/standalone-20261022T112928817Z-.../equation.png`, `equation.svg`, `equation-firefox.png` |
| Windows 물리 입력 게이트 | 미검증·별도 보류(다음 단위 진행과 분리) | 계획서 12.3; 실제 창, OS 포인터·키보드, 포커스, Ctrl+V 성공/실패는 이번 실행에서 검증하지 않음 |
| 전체 자동 검증 | 50/50 합격 | 기존 38 + CLIP 6 + PTR 4 + FONT-PROBE-ORDERS 1 + FONT-OFFSCREEN 1; `regression-final-50-01.log` |

## 이번 실행의 해석과 미완료 항목

최종 동결 빌드에서 전체 기본 QA 50개가 한 번에 합격했다(`regression-final-50-01.log`). 여기에는 기존 논리·레이아웃·저장/출력 38개, CLIP-01–06, PTR-01–04와 글꼴·오프스크린 진단 2개가 포함된다. CLIP-02의 최종 `NoSession` 확인은 재시도와 undo 뒤에도 최초 snapshot을 비교한 테스트 기준 오류였으며, 경계 직전 snapshot으로 고친 뒤 단독 실행과 전체 회귀에서 통과했다.

포인터 합성 테스트는 테스트 프로젝트 전용 `Avalonia.Headless` 12.1.4와 Skia를 사용하는 별도 child에서 실행한다. `Window.Show()` 직전과 직후 platform handle의 `HandleDescriptor == "STUB"` 및 `Handle == IntPtr.Zero`를 확인한다. headless root에서 합성 press/release, capture loss, 실제 visual-tree content 제거/재부착 및 Session 교체가 통과했다. 이는 운영체제 장치의 물리 입력이나 사용자의 실제 foreground 창 동작을 증명하지 않으므로 계획서 12.3 물리 입력 게이트는 미검증이다.

초기 실패의 normal-first·italic-first·oblique-first JSON은 Normal face 사전 확보 전의 raw `System` 관측 증거로 유지한다. 수정 후 프로브는 `InitializationPhase=after-normal-face-warmup`으로 세 개의 새 프로세스에서 ProductResolver 결과를 비교했다. 세 순서 모두 Normal은 Cambria Math glyph/platform family의 Normal face와 simulation 없음으로 같았고, Italic 및 Oblique 요청은 Cambria Math의 native Oblique platform face·simulation 없음·동일 glyph ID/윤곽으로 같았다. 제품의 Italic 요청은 운영체제 Cambria Math Oblique face에 위임한다. 세 순서 간 요청·실제 face·폭·glyph·윤곽 서명과 원본 child report는 `font-probe-order-matrix-final-01.json` 및 `font-probe/font-{normal,italic,oblique}-first-20261022T112925...` 아래에 남겼다.

이 수정이 보장하는 초기화 경계는 앱의 `MainWindowViewModel`이 `AvaloniaMathTextMetrics`를 만들며 `EnsureRegistered`를 실행하고, 그 안에서 SystemFonts Normal face를 먼저 확보한 다음, `MainWindow.InitializeComponent()`가 XAML을 만드는 순서다. `MainWindowViewModel`의 metrics 필드 초기화는 MainWindow의 `InitializeComponent()`보다 먼저 진행된다. 앱 바깥 코드가 이보다 앞서 `SystemFonts`를 조회해 내부 캐시를 오염시킨 경우에는 복구를 보장하지 않는다. SystemFonts가 반환한 native Oblique glyph face를 재사용하는 우회는 Windows의 Avalonia 12.1.4에서 직접 관측해 적용한 것이며, 검증하지 않은 플랫폼·버전 전체의 일반 보장으로 확대하지 않는다.

FONT-OFFSCREEN은 control-only `RenderTargetBitmap`, 같은 LayoutResult로 만든 SVG, 별도 Firefox 임시 profile의 `--headless --no-remote` SVG 소비를 통과했다. PNG와 Firefox PNG에서는 숫자 직립 및 변수 기울임을 오프스크린 범위에서 확인했다. 이는 모든 기호의 픽셀 단위 동일성이나 실제 Windows 창의 모양을 증명하지 않는다. 물리 OS 입력·실제 포커스·실제 클립보드 접근은 미검증으로 남긴다. 최신 사용자 지시에 따라 이 물리 게이트는 별도 보류하며, 논리 및 UI headless/offscreen 승인 후 다음 단위로 진행하는 조건과 분리한다.
