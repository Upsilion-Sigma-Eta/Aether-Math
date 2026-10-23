# P1 자동 복구·최근 작업 VM/UI 통합 QA

- 상태: 최종 동결 소스에서 Debug 빌드 경고0·오류0, 논리8개+Headless UI1개 9/9, 기존89개를 포함한 전체98/98이 통과했고 메인이 최종 PNG/bounds 시각 검토 후 이 P1 VM/UI 단위를 통합 승인했다. 실제 OS 창·파일 선택기·포커스·UIA·물리 입력은 미검증으로 유지한다.
- 회귀 결과: 기존 89개 + VM 논리 8개 + 실제 App Headless UI 1개 = 전체 98/98 통과.
- 제품 경계: 명시 root의 `IWorkspaceStateService`를 주입한 VM 자동 복구·복원·최근 작업 연결과 실제 MainWindow 사용자 흐름.
- 격리: 모든 서비스 root, 문서 및 복구 파일은 테스트별 `TempDirectory`에 둔다. 실제 사용자 AppData와 기존 사용자 파일에는 접근하지 않는다.
- 화면 제약: 네이티브 OS 창·파일 선택기·사용자 전경 포커스·UIA·물리 키보드/마우스 입력 및 computer-use는 금지한다. 전용 child의 Avalonia Headless + Skia, `STUB`/0 handle 검증과 Headless `Show()`만 허용한다.

## 계약과 결정성

VM에는 `IWorkspaceStateService`와 파일 서비스를 명시 주입한다. 기존 서비스 없는 생성자는 자동 복구 타이머나 사용자 경로 state I/O를 수행하지 않는다. 초기화/수동 flush의 public 완료 결과와 `PropertyChanged`의 `RecoveryFlushSequence`, `LastRecoveryFlushResult`, `LastRecoveryFlushAtUtc`를 TCS로 기다린다. 타이밍 대기는 실패 시 무한 대기를 막는 timeout만 두며 임의 sleep은 사용하지 않는다.

최근 목록·복구 파일 테스트는 `WorkspaceStateService`의 임시 root만 사용한다. 경쟁 사례는 실제 서비스를 장식하는 QA 전용 public `IWorkspaceStateService` wrapper의 `TaskCompletionSource` gate로 실제 저장과 완료 순서를 제어한다. 이는 파일 원본과 VM 알림을 함께 관찰하며 private reflection/source-string 검사는 하지 않는다. 서비스를 통해 완료된 쓰기와 VM queue/ticket 정리가 끝난 시점은 별도 await하여 구분한다.

## 논리 사례 8개

| 사례 | 검증할 동작 | 주요 단언 |
|---|---|---|
| `VM-STATE-INIT` | 서비스 없는 기존 VM 격리, 임시 저장소의 초기 recent/recovery 로드 및 초기화 재호출 | 서비스 없는 VM은 recovery I/O를 하지 않는다. 주입 VM은 readiness와 목록 알림을 완료한 뒤 반환하고 Workspace/Recovery ID를 중복 생성하지 않는다. |
| `AUTO-RECOVERY-TICK` | 실제 timer tick으로 dirty snapshot 기록 | Unicode·스타일·구조·Node ID·revision·StateId가 보존되고 Session/history/selection/dirty/saved marker는 그대로다. TCS가 완료 sequence 변경을 받는다. |
| `AUTO-RECOVERY-CLEANUP` | 사용자 Save 성공·실패와 복구 정리 | 성공 Save는 유효한 active recovery만 지운다. Save 실패는 recovery bytes를 보존한다. future-version recovery는 정상 문서 Save 뒤에도 진단 상태와 원본 bytes를 보존한다. 현재 작업의 `ActiveRecoveryId` 복구 후보를 직접 Restore하고 Discard해도 원본 recovery 파일/bytes를 보존한다. |
| `AUTO-RECOVERY-STALE` | Stop, Session 교체, Initialize/Stop 경합 | 이미 수락된 manual write는 Stop의 queue drain 뒤 `Stale`가 되고 성공 write를 기다린다. 실제 timer tick은 Stop token으로 취소되어 `Canceled`/stale 결과를 현재 VM에 게시하지 않는다. Stop 뒤 새 flush는 `Disabled`, I/O·sequence 변화 없음. 취소를 무시하는 장식 wrapper의 실제 늦은 write는 Session 교체 뒤 stale 처리되고 old RecoveryId cleanup 뒤에도 파일이 남지 않는다. recent read가 막힌 Initialize와 Stop 경합 후 timer가 재시작되지 않으며, 명시 재초기화 후에만 TCS 관찰 tick이 재개된다. |
| `RECOVERY-RESTORE` | 재시작 VM에서 선택한 recovery 명시 복원 | 복구 Workspace/Node ID·AST·스타일을 보존하고 새 dirty Session, 새 RecoveryId, `CurrentPath == null`을 만들며 원본 recovery는 자동 삭제하지 않는다. 현재 Session 자신의 active recovery를 선택한 dirty Restore의 Discard·Save 경유에서도 출처 파일/bytes가 보존되는지 확인한다. pending Restore 중 Flush는 `Skipped`와 진단을 내며 원본 bytes를 보존하고, Cancel 후 정상 `Written`으로 재개된다. Save 경유는 복원 전 최신 현재 snapshot의 실제 저장도 비교한다. |
| `RECOVERY-DIRTY-GUARD` | dirty 현재 작업에서 복원 Cancel/Save/Discard | Cancel은 기존 Session/history/path/selection을 유지한다. Save는 원래 작업 snapshot을 지정 경로에 저장한 뒤 복원하고, Discard는 저장 없이 복원한다. 두 완료 결과 모두 복원 작업은 dirty이며 경로가 없다. |
| `RECENT-SUCCESS` | recent 등록과 선택 미리보기 | 성공한 Save/Open만 등록한다. Save 실패, 잘못된 Open, 실제 유효 파일에 대한 선취소 Open은 등록하지 않고 취소 전 Session/path를 보존한다. recent 항목 선택과 미리보기는 수식 Session/selection을 바꾸지 않는다. `.state.lock` 충돌 시에도 문서 Save는 성공·clean·경로 유지되고 recent 오류는 별도 진단으로 표시된다. 잠금 해제 후 갱신 성공은 진단을 지운다. |
| `RECENT-MISSING-RELINK` | Metadata 상태, pin/unpin/remove, 명시 경로 재연결 | `Missing`의 수정 시각은 null, metadata 조회 실패는 per-entry 진단으로 구분한다. Relink는 실제 picker 없이 지정 경로 API로 성공하며 원본·대체 파일 bytes를 바꾸지 않는다. replacement recent 기록이 `WriteBlocked`이면 이전 항목이 남고 제거 API를 호출하지 않는다. 더 최신 refresh가 진행 중일 때 늦게 끝난 WriteBlocked 결과가 그 refresh의 목록/진단을 덮지 않는 경합도 TCS gate로 확인한다. Pin/unpin/remove는 VM 목록에 반영되고 remove는 사용자 디렉터리를 삭제하지 않는다. |

`AUTO-RECOVERY-STALE`의 수락된 manual write는 Stop으로 취소되는 timer tick과 구분한다. 취소를 존중하는 timer gate에서는 실제 서비스 저장 전에 취소가 관찰되어야 한다. 취소를 무시하는 gate는 오직 Session 교체 늦은 성공 하위 사례에만 설정하고, 동일 gate 인스턴스의 TCS·취소 기록을 공유한다. Stop 및 replacement는 queue tail drain 완료 후 파일·VM 상태를 확인한다.

## UI 사례 및 실행 분리

`LibraryUiInteractionCases.All()`의 `LIBRARY-UI`는 전용 자식 `--library-ui-headless LIBRARY-UI`로 실행한다. 실제 `App`/`MainWindow`와 주입된 임시 state root를 사용하고 표시 전후 platform handle이 `STUB`/`IntPtr.Zero`인지 확인한다. 최근 목록 선택·미리보기, pin/remove/missing 경로 재연결, 복구 후보 선택·미리보기와 Restore Discard, Open Cancel, 삭제 확인 취소/승인, 진단 상태를 합성 명령으로 검증한다. Restore Save/Cancel 경로는 논리 사례의 근거이며 UI 통과로 집계하지 않는다. helper는 1060×700 및 1360×880 전체 셸 PNG/bounds를 `doc/verification/artifacts/p1-recovery-recent/`에 남기는 계약이다. 이 검증은 물리 OS 입력 게이트를 통과시키지 않는다.

Program은 8개 논리 사례를 등록하고 `--library-ui-headless LIBRARY-UI` child route를 연결했다. UI는 Pin 후 선택 항목 상태를 보존하고, 긴 미리보기를 이름·경로·상태·수정 시각으로 분리해 각 텍스트 bounds를 확인한다. 메인은 초기 화면에서 Unpin이 비활성인 실행 결과를 제품 동작으로 분류해 선택 복원 순서를 수정하도록 요청했다. 최종 동결 소스의 빌드·신규9·전체98과 화면 시각 검토가 모두 통과해 이번 단위를 통합 승인했다.

## 실행 결과

| 단계 | 결과 | 로그 |
|---|---|---|
| 첫 Debug 빌드 | 종료 코드1, 경고0·오류2. 제품에 `IsRecoveryStopped` 미정의 참조가 있어 QA 실행을 시작하지 않았다. 제품 담당 수정 후 재시도했다. | `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-01.log` |
| 두 번째 Debug 빌드 | 종료 코드1, 경고0·오류2. UI 소유 QA 파일에서 helper 인수 형식이 맞지 않았다. UI 담당 수정 후 재시도했다. | `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-02.log` |
| 세 번째 Debug 빌드 | 종료 코드0, 경고0·오류0. | `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-03.log` |
| 신규 9개 실행 | 논리8/8 통과, UI1 실패, 전체 exit1. 실패는 최초 최소창 viewport에서 최근 작업 액션 버튼 묶음을 찾지 못한 화면 배치 단언이다. 실패가 첫 캡처 전 발생해 PNG/bounds는 생성되지 않았다. 전체98은 실행하지 않았다. | `doc/verification/artifacts/p1-recovery-recent/recovery-recent-new-9-final-01.log` |
| 네 번째 Debug 빌드 | 종료 코드0, 경고0·오류0. | `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-04.log` |
| 신규 9개 재실행 | 논리8/8 통과, UI1 실패(exit1). Pin 후 Unpin 버튼이 `IsEffectivelyEnabled == false`여서 UI 클릭 검증이 중단됐다. 이 실행은 초기·미리보기·기본 창 캡처와 bounds를 생성했다. | `doc/verification/artifacts/p1-recovery-recent/recovery-recent-new-9-final-02.log` |
| 최종 Debug 빌드 | 종료 코드0, 경고0·오류0. | `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-05.log` |
| 신규 9개 최종 실행 | 종료 코드0, 9/9 통과. Headless UI는 두 크기의 셸과 분리된 긴 미리보기를 검사했다. | `doc/verification/artifacts/p1-recovery-recent/recovery-recent-new-9-final-03.log` |
| 통합 전체 회귀 | 종료 코드0, 98/98 통과. | `doc/verification/artifacts/p1-recovery-recent/regression-98-final-01.log` |

실행 대상은 저장 service에 VM을 연결한 논리 계약과 실제 App/MainWindow Headless child뿐이다. native OS UI, 파일 선택기, 사용자 AppData 및 물리 입력은 사용하지 않았다. 실제 실행 도구가 반환한 세 단계의 종료 코드 0은 `doc/verification/artifacts/p1-recovery-recent/final-run-manifest.md`에 기록했다.

## 현재 상태와 남은 범위

- 2026-10-22 약 14:04–14:06 UTC Watchdog 감사 동안 모든 개발/실행을 정지했다. 중단급 문제가 없다는 감사 종료 후 메인이 편집 재개를 명시했다.
- 2026-10-22 약 14:19–14:20 UTC Watchdog 감사 동안 모든 작업을 정지했다. 중단급 문제가 없다는 감사 종료 뒤 메인이 모든 담당자의 작업 재개를 명시했다.
- Debug 통합 빌드01은 제품 `MainWindowViewModel.cs`에서 정의되지 않은 `IsRecoveryStopped` 참조 2건으로 실패했다. 빌드02는 엔진 수정 뒤 제품을 빌드했지만 UI 소유 `LibraryUiInteractionCases.cs`의 helper 인수 형식 CS1503 2건으로 실패했다. 두 실패 로그를 보존하고 각각 담당자가 수정했다.
- 빌드03은 종료 코드0, 경고0·오류0이다. 신규9 첫 실행은 논리8/8 통과 후 `library-initial` 최소 화면에서 최근 작업 버튼이 viewport에 보이지 않아 UI가 실패했다. 이 실패는 첫 캡처 전이므로 PNG/bounds가 생성되지 않았다. 전체98은 시작하지 않았다.
- 빌드04도 경고0·오류0이었고 신규9 재실행은 Pin 뒤 Unpin 버튼이 비활성이라 UI에서 실패했다. 이 실행은 초기·기본·미리보기 캡처를 남겼다. 메인은 Unpin이 비활성인 것을 선택 유지 요구와 맞지 않는 제품 동작으로 판단해 engine에 ItemsSource 변경 뒤 선택 복원 순서 수정을 요청했다. UI 담당은 긴 미리보기의 이름·경로·상태·수정 시각을 별도 TextBlock로 나누고 각 bounds를 확인하도록 보완했다.
- 약 14:35–14:36 UTC Watchdog 감사 동안 모든 담당자가 정지했다. 중단급 문제는 없었으며 감사 종료 뒤 메인이 QA/engine/UI 담당자의 재개를 명시했다. 이후 제품/UI 수정과 재동결을 메인이 정적으로 확인하고 재실행을 승인했다.
- 최종 빌드05는 종료 코드0, 경고0·오류0이며 신규9 최종 실행은 9/9, 기존 회귀를 포함한 전체98은 98/98로 종료 코드0이다. 두 성공 실행에서 초기/기본 셸, 최종 최소 크기 셸, 긴 미리보기 PNG 및 bounds가 생성됐다. 전체98 실행의 최종 네 세트는 `library-initial-1060x700-20261022T144345471Z-4f913e01efb64df3b0864744bdc4f7ca`, `library-preview-1060x700-20261022T144345518Z-3c8c6362877c4c4183bca7c926d94ea2`, `library-default-1360x880-20261022T144345546Z-8f5855c9f3694df0a15bae12473ec94f`, `library-final-minimum-1060x700-20261022T144347136Z-f01c15e8c5404ecc82484954c50ed364` 폴더에 각각 PNG와 bounds를 보존한다. 메인은 최종 여덟 이미지와 bounds를 검토해 접근성, 텍스트 가독성, 겹침 없음, STUB/0 및 최소 viewport 257px을 확인하고 이 단위를 통합 승인했다.
- 현재 저장 서비스의 8/8·기존79 포함 전체87/87 및 save-ticket 후속 2/2·전체89/89는 각 시점의 승인된 기준선으로 보존한다. 새 논리8/UI1과 전체98이 통과했으나 이를 P1/P0 전체 완료로 해석하지 않는다.
- 확인된 시험 경계는 Windows/Avalonia 저장 경로와 임시 파일이다. 다른 플랫폼/버전, ACL 동작, 실제 사용자 AppData, 네이티브 file picker, 실제 창·포커스·UIA·IME·물리 입력 및 외부 앱에서의 전체 사용자 여정은 미검증이다.
- P1/P0 전체 완료가 아니다. 이번 VM·Headless/offscreen slice는 논리·전체98 검증과 메인 시각 검토까지 통합 승인됐지만 실제 OS 입력은 여전히 미검증이다. templates, parser, layout/wrap, PNG/PDF/MathML, OS 입력 및 전체 완료 조건도 별도로 남아 있다.
