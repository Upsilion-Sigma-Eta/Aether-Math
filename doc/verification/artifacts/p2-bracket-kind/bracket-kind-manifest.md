# 괄호 종류 속성 실행 manifest

현재 상태: 괄호 종류 편집 하위 단위가 논리·Headless·offscreen 범위에서 통합 승인됐다. 기준선191에 신규 논리6+`BRACKET-KIND-UI`1을 더한 전체198/198이 통과했다. 2026-10-22 23:09 UTC 감사는 절차 기록이며 통과 근거가 아니다.

| 단계 | 로그 | 상태 |
|---|---|---|
| Debug 통합 빌드 | [build01](build-bracket-kind-final-01.log), [build02](build-bracket-kind-final-02.log) | build01: 경고0·CS0103 오류16·exit1 (`Find` helper 누락). build02: 경고0·오류0·exit0. |
| 신규 논리6 + `BRACKET-KIND-UI` | [신규7-01](bracket-kind-new7-final-01.log) | 7/7·exit0. 메인은 23:18:07–08 UTC PNG10개와 bounds15개의 핵심 필드를 직접 검토했다. |
| 기준선 포함 전체198 | [전체198-01](bracket-kind-full198-final-01.log) | 198/198·exit0. UI의 PNG/bounds 15쌍은 23:20:16.394–23:20:17.878 UTC 별도 자동 산출물이며 전체 직접 검토로 기록하지 않는다. |

등록된 Case ID: `BRACKET-KIND-CORE`, `BRACKET-KIND-APPLY`, `BRACKET-KIND-VM`, `BRACKET-KIND-IO`, `BRACKET-KIND-LATEX`, `BRACKET-KIND-LAYOUT-PREVIEW`, `BRACKET-KIND-UI` (총 7건). `Program.cs`의 Headless 초기화, child dispatch, 일반 case `.Concat(...)` 등록을 실행 경로로 확인했다. UI child는 `--bracket-kind-ui-headless BRACKET-KIND-UI`이며 캡처는 artifact scope `p2-bracket-kind` 아래 보존한다. 메인이 직접 검토한 신규7 PNG 10개는 네 종류 detail-minimum, initial-minimum/default-bottom, stale-minimum, unsupported-partial-selection-minimum, detail-style-reset-minimum, initial-default-top이다. 같은 실행 bounds15는 주요 필드만 검토했다. 전체198의 UI PNG/bounds 15쌍은 별도 자동 산출물이며 직접 검토 세트가 아니다.

정적 선언 대조와 런타임 실행: Core enum은 `Parentheses`, `Square`, `Curly`, `Angle`, node 생성자는 `BracketsNode(Guid Id, MathBracketKind Kind, MathNode Content)`다. `MathPropertiesPatch`의 `BracketKind`는 positional record의 마지막 nullable parameter이며 `ApplyMathProperties`는 `MathPropertyChangeResult`를 반환한다. stale 검사가 먼저 수행되고, 유효하지 않은 enum과 비지원 scope는 각각 `InvalidValue`/`UnsupportedScope`로 처리된다. VM choice, selected proxy, draft/current nullable property와 UI child 진입점 및 실제 SelectedItem binding을 확인했다. 이 UI 검증은 popup pointer나 실제 OS 입력을 포함하지 않는다.

범위는 네 괄호 종류의 선택·표시·소비에 한정된다. 괄호 숨김, 한쪽/좌우 분리 표시, 크기 직접 조절은 미포함이며, 전체 §3.3-02나 나머지 P2/P1/P0 완료를 뜻하지 않는다. 실제 OS 창·포커스·물리 입력·IME·clipboard·UIA·사용자 AppData·외부 TeX는 미검증이다.
