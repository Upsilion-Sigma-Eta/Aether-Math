# P2 전체 LaTeX 코드 복사 QA

- 상태: 전체 LaTeX 코드 복사 하위 단위가 논리·Headless/offscreen 범위에서 통합 승인됐다. build03 경고0·오류0·exit0, 신규7-02 7/7·exit0, 전체205-01 205/205·exit0이다.
- 기준선: 승인된 전체 회귀 198건.
- 목표: 논리 6건 + `LATEX-COPY-UI` 1건, 기준선 포함 전체 205건.
- artifact 위치: `doc/verification/artifacts/p2-latex-copy/`.
- 2026-10-22 23:24 UTC 감사는 절차 기록일 뿐 검증 근거가 아니다.
- 2026-10-22 23:39 UTC 감사도 절차 기록이며 통과 근거가 아니다.
- 메인의 직접 시각 검토는 신규7-02의 23:43:42–43 UTC PNG9개와 bounds11의 핵심 필드에 한정된다. 전체205-01의 23:46:06–07 UTC PNG/bounds 11쌍은 별도 자동 산출물이며 직접 검토 세트가 아니다.

## 공개 API와 고정 경계

공개 결과는 `LatexCopyStatus`(Copied, CopiedPreviousSnapshot, Blocked, Busy, Unavailable, Failed, Canceled)와 `LatexCopyResult(Status, Message)`다. `MainWindowViewModel.CopyLatexAsync(Func<string, Task>? writeTextAsync)`는 호출 시점의 현재 전체 `LatexCode`를 한 번 고정해 전달한다. `CanCopyLatex`, `IsCopyingLatex`, `LatexCopyDisabledReason`, `LatexCopyMessage`가 UI 상태를 제공한다.

텍스트 진단 또는 필수 빈 슬롯이 있으면 sink를 부르지 않고 `Blocked`를 반환한다. 행 간격처럼 LaTeX에 표현되지 않는 layout 안내만 있는 완성 코드는 복사를 허용한다. null sink는 `Unavailable`, 취소 예외는 `Canceled`, 다른 sink 예외는 `Failed`이며 이후 재시도가 가능하다. 복사 중 재진입은 `Busy`이고 상태를 덮지 않는다. 새 복사를 시작하면 이전 결과 메시지를 비우고 진행 중 상태를 먼저 게시한다.

await 중 수식 revision/state 또는 session이 바뀌어도 요청 당시 source만 전달하며 완료 상태는 `CopiedPreviousSnapshot`이다. 선택 이동만으로는 source snapshot을 stale 처리하지 않는다. Workspace·Session 변경 알림에서 source cache stamp가 현재 작업과 다르면 복사는 임시 차단되고 sink는 호출되지 않는다. source, diagnostics, export 허용 상태는 알림 전에 같은 Workspace에서 모두 계산·대입되어 observer가 한 snapshot을 본다. 성공 메시지는 소유 Session/Revision이 바뀐 때만 지우며 같은 Commit의 늦은 Revision 알림은 지우지 않는다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `LATEX-COPY-SUCCESS` | 전체 source를 정확히 한 번 보내고 session/workspace/revision/StateId/dirty/history/caret/selection/path 및 Output Preview 상태를 보존한다. 복사 메시지와 코드 진단은 별도 상태다. |
| `LATEX-COPY-GATES` | 제어 문자와 필수 빈 슬롯에서 sink 무호출 `Blocked`, layout-only 안내에서 정상 복사, null sink의 `Unavailable`을 검사한다. |
| `LATEX-COPY-BUSY` | 실제 대기 sink 중 진행 상태·이전 메시지 제거·busy 비활성 사유를 확인하고 재진입은 sink 없이 `Busy`를 반환한다. |
| `LATEX-COPY-SNAPSHOT` | gate sink 동안 선택 이동은 같은 snapshot으로 복사된다. 편집 후 Undo로 원 StateId에 돌아와도 revision 변화로 이전 snapshot임을 알리고, Session 교체 중에도 호출 당시 코드를 한 번 전달한다. |
| `LATEX-COPY-FAIL-CANCEL` | 동기/비동기 sink 예외와 취소 결과, 상태 불변성, 이후 정상 재시도를 검사한다. |
| `LATEX-COPY-NOTIFY-ORDER` | LatexCode/진단 관련 알림을 받는 즉시 observer가 현재 Workspace와 일치하는 source/진단/CanExport/CanCopy를 읽는지 검사한다. 허용된 각 코드 알림의 source를 그 시점에 캡처해 sink 입력과 순서대로 비교하고, 제어 문자·새 작업·빈 슬롯 해제 전환을 포함한다. Session 교체 알림의 재진입 복사는 stale cache 때문에 차단되어야 한다. 코드 publish 뒤 재진입 복사는 허용되어야 하며 같은 Commit의 Revision 알림 뒤 성공 메시지가 남아야 한다. |

공유 실행기는 논리 6개와 UI 1개를 `Program.cs`에서 일반 case 목록에 연결한다. UI child 계약은 `LatexCopyUiInteractionCases.All()`/`RunHeadlessChildAsync(string)`, case `LATEX-COPY-UI`, flag `--latex-copy-ui-headless LATEX-COPY-UI`다. UI 검사는 실제 `App`/`MainWindow`의 Headless/Skia 창과 `LatexClipboardWriterOverride` fake sink를 사용한다. OS clipboard 호출은 하지 않는다.

## 실행 결과와 시각 검토 범위

`build-latex-copy-final-01.log`는 경고0·오류2·실제 exit1로 실패했다. 두 CS0103은 `LatexCopyUiInteractionCases.cs`의 369·381행에서 선택적 캡처 영역 좌표를 검사하는 `Contains` helper가 누락된 오류였다. 수정 뒤 `build-latex-copy-final-02.log`와 `build-latex-copy-final-03.log`는 각각 경고0·오류0·exit0으로 통과했다.

`latex-copy-new7-final-01.log`는 논리6 PASS, `LATEX-COPY-UI` FAIL로 6/7·exit1이다. 첫 `latex-copy-initial-collapsed-minimum` 캡처에서 선택적 LaTeX 코드 전체 영역의 창 기준 좌표 획득이 실패했다(`RowShellVisualCases.GetOptionalBounds`, `RowShellVisualCases.CaptureLatexCopyAndVerify`). PNG와 bounds는 생성되지 않았고 캡처 디렉터리는 비어 있다. 이후 UI helper를 보완해 재실행한 `latex-copy-new7-final-02.log`는 7/7·exit0이다. [build01](artifacts/p2-latex-copy/build-latex-copy-final-01.log), [build02](artifacts/p2-latex-copy/build-latex-copy-final-02.log), [build03](artifacts/p2-latex-copy/build-latex-copy-final-03.log), [신규7-01](artifacts/p2-latex-copy/latex-copy-new7-final-01.log), [신규7-02](artifacts/p2-latex-copy/latex-copy-new7-final-02.log)의 원로그를 보존한다.

메인은 신규7-02의 직접 PNG9개(초기 접힘 최소/기본, 성공 펼침, 실패 펼침, busy 펼침, 이전 snapshot 펼침, unavailable 펼침, blocked 펼침, 성공 접힘 최소)와 같은 run bounds11의 핵심 상태·좌표 필드를 검토했다. bounds의 모든 필드를 전부 확인했다고 보지는 않는다. 확인된 상태는 STUB/0, 접힘 viewport 최소/기본 257/437px, source 90px·진단 58px·grid 148px, 펼침 최소 editor 76px, 부분 TextBox 선택 0–3 보존, busy 레이블/비활성 상태, 이전 snapshot·실패·사용 불가 안내다. `latex-copy-expanded-minimum`과 `latex-copy-retry-expanded-minimum` PNG2개는 직접 검토하지 않았다.

메인 승인 뒤 같은 build03 산출물로 실행한 [전체205-01](artifacts/p2-latex-copy/latex-copy-full205-final-01.log)은 205/205·실제 exit0이다. 이 run의 23:46:06–07 UTC LaTeX 복사 PNG/bounds 11쌍은 자동 산출물이며 직접 검토 대상이 아니다. 이는 전체 LaTeX 코드 복사 slice 승인이지 P2 전체나 출력 형식 전체 matrix의 완료가 아니다.

허용된 검사 경계는 테스트 `TempDirectory`, 실제 App을 쓰는 전용 Headless/Skia child, fake writer, `STUB`/0 및 저장소 artifact 경로다. 실제 OS clipboard runtime·OS 창/입력/포커스·UIA·사용자 AppData·computer-use는 미검증이다. LaTeX syntax highlighting, parser 왕복, 다른 copy/출력 전체 matrix 및 외부 TeX 컴파일도 이번 단위에 포함되지 않는다. 2026-10-22 23:39 UTC 감사는 절차 기록일 뿐 검증 근거가 아니다.
