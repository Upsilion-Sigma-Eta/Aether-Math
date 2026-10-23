# P1 그룹 편집 QA 계획 및 실행 기록

- 대상: 개발 계획서 §3.1의 수식 그룹 추가·복제·삭제·순서 변경
- 기준: 그룹/행/노드 스키마는 유지한다. 그룹 추가·복제·삭제·이동은 각각 한 번의 편집 명령이어야 한다.
- 상태: 코드 검증 70/70 통과 및 메인 통합 승인 완료. 실제 OS 창·포커스·물리 입력 게이트는 미검증.
- 산출물 위치: `doc/verification/artifacts/p1-group-editing/`

## 계약과 검증 범위

활성 그룹은 전체 구조 선택 상태에서는 `SelectedNodeId`가 속한 그룹이고, 그 외에는 caret가 속한 그룹이다. 그룹 추가는 활성 그룹 바로 뒤에 새 ID의 그룹·행·필수 빈 Text 슬롯을 넣고 기존 정렬과 행 간격을 상속한다. 복제는 그룹의 모든 행과 하위 노드를 새 ID로 만들고 텍스트, 구조, 빈 슬롯 flag, 정렬, 간격을 보존한다. 같은 그룹 안 다른 행에 caret가 있고 전체 구조가 선택된 경우 둘 다 그룹 전체 ID 대응으로 매핑한다. caret가 다른 그룹에 있으면 선택 구조를 복제 ID로 매핑하고 caret는 복제된 선택 구조의 첫 Text offset 0으로 둔다. 그룹 삭제는 마지막 하나를 남기며, 삭제 뒤 다음 그룹을 우선 선택하고 마지막 그룹을 지운 경우 이전 그룹을 선택한다. 인접 그룹 이동은 그룹만 교환하고 내부 행 순서와 편집 위치를 보존한다.

실패 및 경계 명령은 모델, revision, undo/redo, dirty 상태, 선택과 caret를 바꾸지 않아야 한다. 성공 명령은 한 번의 undo/redo로 workspace와 편집 위치를 복원한다. 작업 스키마 버전은 바뀌지 않는다.

기존 P1 행 단위 회귀 60개는 보존한다. 이 단위에는 신규 논리 9개와 실제 `App`을 사용하는 headless UI 1개를 추가해 전체 70개를 목표로 한다.

| ID | 검증 내용 |
|---|---|
| `GROUP-ADD` | 활성 그룹 뒤 삽입, 정렬·간격 상속, 빈 필수 슬롯/caret 0, 선택 해제, style·schema 보존, dirty 및 단일 undo/redo |
| `GROUP-ACTIVE` | caret와 다른 그룹의 구조를 전체 선택했을 때 선택 그룹을 이동 대상으로 삼고 교차 그룹 caret/선택을 보존 |
| `GROUP-DUPLICATE` | 여러 행과 깊은 분수·근호·첨자·행렬, Unicode·빈 슬롯 복제, 모든 node/row/group ID 신규화; 역방향 부분 선택과 같은 그룹 다른 행 caret·구조 선택 매핑; undo/redo |
| `GROUP-DUP-CROSS` | caret가 다른 그룹에 있고 선택 구조가 활성 그룹에 있을 때 clone subtree 첫 Text로 fallback, 선택 ID clone 매핑, undo/redo 원래 교차 그룹 상태 복원 |
| `GROUP-DELETE` | 중간 그룹 삭제는 다음 그룹 첫 행 첫 Text, 마지막 그룹 삭제는 이전 그룹으로 이동; undo/redo·dirty 복원 |
| `GROUP-MOVE` | 위/아래 인접 그룹 교환, 그룹 내부 행 및 ID·caret·역방향 선택 보존, undo/redo |
| `GROUP-NOOP` | 유일 그룹 삭제/이동 및 첫·마지막 그룹 경계 이동의 전체 무변경; redo stack도 보존 |
| `GROUP-IO` | 파일 저장·재열기에서 그룹 ID/속성/순서 보존, LaTeX 그룹·행 순서, layout 논리 행 ID와 SVG baseline/text 논리 순서, 필수 빈 그룹 슬롯 SVG 출력 거절 |
| `GROUP-PASTE` | reader가 대기 중인 동안 그룹 이동 또는 삭제가 일어나면 paste 결과를 폐기하고, stale 결과가 상태를 바꾸지 않으며 후속 paste를 허용 |
| `GROUP-UI` | 전용 headless child의 실제 `App`/`MainWindow`, 그룹·행 명령 binding/AutomationId/`IsEffectivelyEnabled`, indicator 순서, disabled 사유, 합성 버튼/단축키·undo/redo, 다수 그룹·긴 행 caret 접근, 두 창 크기 전체 셸 PNG/bounds |

## UI 격리 및 아티팩트

`GROUP-UI`는 `--group-ui-headless GROUP-UI`로 시작하는 별도 자식 프로세스에서 Avalonia Headless와 Skia를 사용한다. 표시 전후 창 handle이 `STUB`/`0`인지 확인한 뒤에만 headless `Show`를 실행한다. 검증 대상은 1060×700과 1360×880이다. 스크린샷과 bounds JSON은 `p1-group-editing/` 아래에 보존하고 메인 검토를 받는다.

이 검증은 네이티브 OS 창, 사용자 포커스, UIA, 실제 키보드·마우스 입력을 실행하지 않는다. 물리 입력 게이트는 미검증 상태로 유지한다. headless의 합성 입력 결과를 물리 UI 통과로 해석하지 않는다.

## 실행 순서와 판정

모든 테스트 및 제품 소스의 동결과 메인 정적 검토 뒤 QA 담당만 순차 실행한다. 이번 실행은 아래 결과로 완료했다.

1. `dotnet build AetherMath.sln -c Debug --no-restore`를 실행하고 전체 로그를 `artifacts/`에 보존한다.
2. 신규 `GROUP-ADD`부터 `GROUP-UI`까지 10개만 실행해 계약 실패를 먼저 분리한다.
3. 신규 10개가 모두 통과하면 기존 60개를 포함한 전체 70개를 실행한다.
4. 두 크기의 셸 PNG/bounds를 메인에게 제출하고 검토 및 최종 승인을 기록한다.

기존 단위 기준선은 [P1 행 편집 QA 기록](P1-row-editing-QA-plan.md)의 `build-row-final-05.log` 및 `regression-final-60-02.log`다. 이번 새 실행은 다음과 같다. 과거 실패 증거는 덮어쓰지 않았다.

- `build-group-final-01.log`: Debug 통합 빌드 성공, 경고 0건·오류 0건.
- `group-new10-final-01.log`: 신규 그룹 논리 9개와 UI 1개 통과, `전체=10 합격=10 실패=0`, 종료 코드 0.
- `regression70-final-01.log`: 기존 60개와 신규 10개 통과, `전체=70 합격=70 실패=0`, 종료 코드 0.
- `group-shell-initial-{minimum,default}-*/main-window.png`와 `bounds.json`, `group-shell-many-{minimum,default}-*/main-window.png`와 `bounds.json`: 각각 1060×700과 1360×880의 초기 셸 및 다수 그룹 화면 산출물. 메인이 검토한 GROUP10 실행 산출물은 2026-10-22 12:19 UTC 생성본이다: `group-shell-initial-minimum-1060x700-20261022T121913602Z-6f9ebada68184f5aa533fc8c4f22c164`, `group-shell-initial-default-1360x880-20261022T121913660Z-8c22d0f25cf541b4990a512c994685b1`, `group-shell-many-minimum-1060x700-20261022T121917657Z-9c88e47ab0244ff0a8f0522cb2c98791`, `group-shell-many-default-1360x880-20261022T121917549Z-55751118856c4ec3bc1d3f85e0efd92e`. 전체70 실행에서 12:26 UTC에 별도 네 세트를 다시 생성했으며, 이는 GROUP10 검토본과 구분되는 추가 산출물이다.

## 감사 및 현재 상태

약 12:07–12:09 UTC Watchdog 감사 동안 엔진·UI·QA 작성 및 실행을 전체 정지했다. 감사는 이번 §3.1 그룹 단위의 계약과 절차에 한정한 읽기 전용 점검이었다. 중단급 문제는 확인되지 않았고, 구현을 합격 처리하지 않았다. 최소 권고는 복제의 그룹 전체 ID 대응(같은 그룹 다른 행 caret와 구조 선택), 교차 그룹 fallback, 최소 그룹/이동 경계의 무변경, 기존 60개 회귀와 실제 MainWindow headless 범위를 신규 테스트에서 분리 검증하는 것이었다. 재개는 메인의 명시적 지시로 이루어졌다.

약 12:24–12:26 UTC 두 번째 읽기 전용 감사 동안 전체 작업을 정지했다. 중단급 문제는 확인되지 않았다. 직전 로컬 설정 파일 읽기 오류 뒤 기존 빌드·신규 10개 로그와 프로세스 상태를 확인해 이미 성공한 실행을 중복하지 않았으며, 사용자 설정 파일은 수정하지 않았다. 감사 후 메인의 명시적 재개 승인에 따라 전체 70개 회귀만 실행했다.

그룹 엔진·VM, MainWindow/theme/helper와 QA 테스트는 메인의 정적 검토 뒤 동결된 상태에서 검증했다. Debug 빌드가 경고·오류 0건으로 통과했고, 신규 논리 9개+UI 1개 및 전체 70개 회귀가 모두 통과했다. 따라서 이번 코드/헤드리스 검증의 결과는 **신규 10/10, 전체 70/70**이다. 정적 검토에서 undo/redo revision 기대값은 각 단일 commit/undo/redo 증가량에 맞췄고, LaTeX 신규 그룹 토큰은 `NEW=6`, `B=3`으로 수정했으며, SVG baseline 정렬을 제거해 출력의 실제 문서 순서를 검사하도록 바로잡았다.

메인은 GROUP10 실행 중 12:19 UTC에 생성된 위 초기/다수 그룹 × 두 창 크기 PNG 4개와 bounds를 검토해 이번 그룹 편집 단위를 논리·headless·offscreen 범위에서 통합 승인했다. 검토 결과 그룹/행 도구와 숫자 표시가 구분되고, 패널 간 겹침이나 도구 잘림이 없으며, 최소 창의 viewport는 257px(기준 220px 이상), 긴 행의 끝 caret는 의도된 스크롤 영역에서 노출된다. 전체70 실행에서 12:26 UTC에 새로 생성된 네 이미지는 최신 렌더 산출물이며 메인의 시각 검토 증거로 사용하지 않았다. 이 승인은 물리 UI 검증을 포함하지 않는다.

UI 테스트는 실제 `App`/`MainWindow`를 Avalonia Headless+Skia에서 구성하고, headless 표시 전후 `STUB`/`0` handle 경계를 확인한 합성 상호작용과 화면 외 렌더링만 검증했다. 네이티브 OS 창, 사용자 포커스, UIA 또는 실제 키보드·마우스 입력은 검증하지 않았고 물리 입력 게이트는 미검증이다. 이 결과는 §3.1 코드 검증이며 P1/P0 전체 완료를 뜻하지 않는다.

