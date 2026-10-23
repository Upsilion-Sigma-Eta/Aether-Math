# P1 파일 수명주기 보호 QA 계획 및 기록

- 상태: 원 lifecycle 9/9·전체79/79 및 후속 저장 ticket 2/2·전체89/89가 통과했고 메인이 저장 ticket 후속을 통합 승인했다. 승인은 논리/Headless/offscreen에 한정하며, 새 후속은 VM 논리 경계라 추가 UI 이미지가 없다.
- 목표 회귀 수: 기존 70개 + 논리 8개 + UI 1개 = 79개.
- 담당: 논리 QA 및 실행은 QA 담당 단독, 제품 VM은 엔진 담당, lifecycle UI/XAML/helper 및 Row/Group UI 정리는 UI 담당.
- 실행 승인: 메인 통합 정적 검토와 동결을 확인했으며, QA 담당 단독 Debug 빌드→신규 9개→성공 시 전체 79개 실행을 승인받았다.
- 작업 기준: 2026-10-22.
- 회귀 기준선: 원 lifecycle은 신규9/전체79, 저장 서비스 기반은 신규8/전체87로 각각 승인되어 있다. 이후 저장 ticket 회귀 2개를 더해 통합 전체89를 실행했다. 저장 서비스 8개는 기존 기준선에 포함되므로 후속 단독 신규 목록에는 다시 포함되지 않는다.

## 승인된 계약과 경계

dirty `New`/`Open`/`Close`는 `WorkspaceActionKind`와 `WorkspaceActionRequestResult`를 사용해 확인 필요/완료/거부/실패/취소/오래된 요청을 구분한다. Save/Discard/Cancel은 `WorkspaceActionResolutionResult`를 반환한다. `OpenFromPathAsync`는 실제 picker 없이 경로를 받아 읽고 검증한다. pending Save도 명시 경로 API를 사용하므로 테스트 파일은 모두 QA 전용 `TempDirectory` 안에 둔다.

현재 승인된 공개 계약은 `RequestNewWorkspace()`, `OpenFromPathAsync(path, ct)`, `RequestClose()`, `SavePendingWorkspaceActionToPathAsync(path, ct)`, `DiscardPendingWorkspaceAction()`, `CancelPendingWorkspaceAction()`, `TryConsumeCloseApproval(Guid)` 및 `IWorkspaceFileService` 주입이다. Close 승인은 원 Session, revision, navigation generation에 결속된 일회용 token이어야 한다. 취소 뒤 늦게 끝난 작업은 새 상태·경로·saved marker·pending action을 바꾸지 않아야 한다.

일반 저장과 pending 저장/열기는 공유 FIFO I/O 큐를 사용한다. 저장 A 뒤 편집 B가 생기고 pending 저장 B가 이어지면 최종 동일 경로 파일은 B snapshot이어야 하며 그 저장 전 Close/New 전환은 일어나지 않는다. 취소된 대기 작업은 선행 I/O가 끝난 뒤 실행 없이 건너뛰고 다음 작업과 겹치지 않아야 한다. clean/Discard Close도 앞선 I/O를 drain하고 그동안 Session/revision/generation이 변하지 않았을 때만 승인을 발급한다.

## 논리 회귀 계획

| 케이스 | 검증 범위 | 준비 상태 |
|---|---|---|
| `VM-OPEN` | 잘못된 JSON/schema/없는 파일 보존. dirty valid Open은 pending 확인 및 원 Session/path/history 보존 후 명시적 Discard에서만 교체. | 전체79에서 통과. |
| `LIFECYCLE-CLEAN` | clean New/Open/Close, 새 Workspace ID·고유 ID, 최소 그룹/행과 필수 빈 텍스트 슬롯, 초기화된 경로/history, Open 뒤 실제 Save 및 단일 승인 소비. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-NEW` | dirty New의 Cancel/Discard/Save. Cancel은 Session/workspace/revision/caret/selection/history/path/dirty를 보존하고 Save는 이전 snapshot만 저장한 뒤 새 빈 작업으로 전환. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-OPEN` | dirty valid Open 취소·저장 뒤 대상 파일 전환. 기존 작업은 선택된 저장 경로에 보존. 같은 경로 저장은 새 snapshot을 활성화. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-CLOSE` | clean/dirty Close, 취소, Discard 승인, token의 일회성 소비 및 edit/navigation 뒤 무효화. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-FAIL` | Save 실패/취소의 원본 파일 bytes, 원 Session/path/history/pending 보존 및 성공 retry만 action 전환. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-SAVE-EDIT` | 저장 중 편집 snapshot, 일반 Save A→pending Save B 직렬화, B 전 Close 방지, busy Save 취소/즉시 busy 해제/새 pending 허용/늦은 결과 폐기, canceled queue slot 뒤 tail 작업. | TCS gate 경합·상태 문구 단언 포함; 신규9 및 전체79에서 통과. |
| `LIFECYCLE-ASYNC-STALE` | New/Close 뒤 늦은 Open은 stale 처리. Close token은 기존 I/O drain 뒤 발급되고, drain 중 편집되면 승인 없이 busy를 풀고 dirty Close 재요청을 허용. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-REENTRY` | pending New 중 중복 New/Open/Close/해결 거부, 거부된 Open에서 파일 service 미호출, 허용된 전환 단 한 번만 적용. | 신규9 및 전체79에서 통과. |
| `LIFECYCLE-SAVE-UNDO` | 일반 Save(A) gate 중 Undo로 이전 StateId를 복구한 뒤 New/Open/Close 확인 요구, Cancel 상태 보존, 같은 Session의 pending Save(old) FIFO, 최종 파일 old 및 Close token 1회 소비. | 신규2 및 전체89에서 통과. |
| `LIFECYCLE-SAVE-CANCELLED-WRITE` | caller 취소 뒤 취소를 무시한 service가 실제 A를 기록하는 경우의 보호·saved marker/path/status 불변, guard Cancel 뒤 재요청, 실제 취소·실패 ticket 제거, 동일 clean StateId의 Close drain. | 신규2 및 전체89에서 통과. |

QA 파일 서비스 wrapper는 실제 `WorkspaceFileService` 저장/열기를 호출하고, 경로별 `TaskCompletionSource` gate와 실패 주입으로 경합을 결정적으로 만든다. 임의 `Task.Delay`, private reflection, 제품 내부 API 우회는 사용하지 않는다.

## Headless UI 검증

`LIFECYCLE-UI`는 별도 자식 프로세스 `--lifecycle-ui-headless LIFECYCLE-UI`에서 실제 `App`/`MainWindow`와 Avalonia Headless+Skia로 실행했다. `Show()` 직전과 직후 platform handle이 `HandleDescriptor == "STUB"` 및 `Handle == IntPtr.Zero`인지 확인했다. 확인 overlay, Save/Discard/Cancel 명령 및 pending 상태를 실제 VM과 연결해 검증하고, 명시 경로 API만 사용했다. 화면 산출물은 `doc/verification/artifacts/p1-lifecycle-protection/` 아래에 보존했다.

네이티브 OS 창과 파일 선택기, 사용자 포커스, UIA, 물리 키보드/마우스/클립보드는 금지한다. STUB/0 headless `Show()`와 합성 상호작용은 허용되지만 물리 입력 검증의 대체나 합격으로 기록하지 않는다.

## 실행 결과

이 절의 원래 lifecycle 단위는 아래 결과로 통합 승인됐다. 아래의 후속 ticket 사례는 이 과거 실행에 포함되지 않는다.

| 단계 | 결과 | 증거 |
|---|---|---|
| 첫 통합 빌드 | 실패, 종료 코드 1, 경고 0·오류 2. QA 소스에서 `SequenceEqual(await File.ReadAllBytesAsync(...))`가 `ReadOnlySpan<byte>`를 await 경계에 넘겨 CS4007을 냈다. | `doc/verification/artifacts/p1-lifecycle-protection/build-lifecycle-final-01.log` |
| QA 최소 수정 뒤 Debug 빌드 | 통과, 종료 코드 0, 경고 0·오류 0. await 결과를 `byte[]` 지역 변수에 먼저 받은 뒤 비교하도록 바꿨다. | `doc/verification/artifacts/p1-lifecycle-protection/build-lifecycle-final-02.log` |
| 신규 lifecycle 회귀 | 9/9 통과, 종료 코드 0. | `doc/verification/artifacts/p1-lifecycle-protection/lifecycle-new-9-final-01.log` |
| 전체 회귀 | 기존 70개 포함 79/79 통과, 종료 코드 0. | `doc/verification/artifacts/p1-lifecycle-protection/lifecycle-full-79-final-01.log` |

메인은 신규9 실행에서 13:10:51–13:10:52 UTC에 생성된 PNG 7개와 대응 bounds를 시각 검토했다: `lifecycle-initial-minimum`, `lifecycle-initial-default`, `lifecycle-confirmation-minimum`, `lifecycle-confirmation-default`, `lifecycle-open-long-target-minimum`, `lifecycle-open-long-target-default`, `lifecycle-save-error-minimum` 하위 폴더. 헤더·확인 dialog·버튼 영역의 겹침/잘림이 없고, 긴 대상 경로 2줄 표시, 저장 오류 가독성, 최소 viewport 257px을 확인해 이 단위를 논리/Headless/offscreen 범위에서 통합 승인했다. 전체79 실행에서 13:11:19–13:11:20 UTC에 재생성된 PNG 7개와 bounds는 별도 최신 산출물이며, 메인 시각 검토 근거는 신규9 세트다. 산출물은 `doc/verification/artifacts/p1-lifecycle-protection/` 아래 각 상태별 폴더에 보존한다.

2026-10-22 약 12:41–12:44 UTC 및 약 12:57–13:00 UTC Watchdog 감사 중 팀은 작업을 정지했다. 중단급 문제는 보고되지 않았으며, 명시적 재개 후 테스트 경계와 문서를 보완했다. 사용자 설정 파일은 수정하지 않았다.

**원 lifecycle 결과: 이 단위는 논리·Headless/offscreen 범위에서 통합 승인됐다. 네이티브 OS 창·파일 선택기·첫 저장 native picker 경로·포커스·UIA·IME·물리 키보드/마우스/클립보드 및 다른 플랫폼은 미검증이다. 이 결과는 전체 P1/P0 완료를 뜻하지 않는다.**

## 저장 ticket 후속 회귀 결과

엔진의 저장 ticket 변경은 같은 파일 큐에서 ticket 등록과 실제 저장 순서를 맞추고, 완료 전에 성공·실패·취소를 분류한다. 같은 Session의 현재 StateId와 다른 진행/성공 ticket은 New/Open/Close 확인을 요구하며, Cancel은 ticket을 보존한다. 현재 저장 상태 문구는 “변경됨” 또는 “저장 확인 필요”처럼 보호 필요를 나타내야 한다. 실제 실패·취소 ticket은 제거하고, 현재와 같은 clean StateId의 저장은 Close 추가 확인 없이 기존 queue drain 후 단일 token을 발급한다.

두 후속 테스트는 `LifecycleCases.cs`에 있다. `ControlledWorkspaceFileService`의 경로별 gate와 완료 TCS를 사용하고, 취소를 무시해 실제 저장을 끝내는 경우와 실제 cancellation을 분리한다. 실패/실제 취소 ticket이 사라졌는지는 같은 FIFO 뒤에 놓인 공개 `OpenFromPathAsync` 결과로 확인해 service 종료 신호와 ticket 정리 사이의 경쟁을 피한다. 임의 sleep, private reflection, 제품 내부 seam은 쓰지 않는다.

| 단계 | 결과 | 로그 |
|---|---|---|
| 최종 Debug 빌드 | 종료 코드 0, 경고 0·오류 0. | `doc/verification/artifacts/p1-lifecycle-save-ticket/build-lifecycle-save-ticket-01.log` |
| 후속 lifecycle 2개 | 2/2 통과, 종료 코드 0. | `doc/verification/artifacts/p1-lifecycle-save-ticket/lifecycle-save-ticket-new-2-01.log` |
| 전체 통합 회귀 | 저장 서비스 기준선 87개와 후속 2개를 포함해 89/89 통과, 종료 코드 0. | `doc/verification/artifacts/p1-lifecycle-save-ticket/lifecycle-save-ticket-full-89-01.log` |

메인은 최종 빌드와 신규2/전체89 로그, VM 및 테스트 코드를 검토해 저장-Undo ticket 후속을 통합 승인했다. 이는 앞서 승인한 저장 서비스 8/8·전체87 기준선을 대체하거나 과거 결과를 덮지 않는다. 신규 실행은 논리 회귀이며 화면 변경이 없어 추가 UI 산출물 검토 대상이 아니었다.

실제 OS 창/파일 선택기/포커스/UIA/물리 입력은 사용하지 않으며, 경로·파일은 테스트 `TempDirectory`에 한정한다. 이 후속은 논리 QA이며 기존 Headless UI 시각 승인이나 실제 OS 검증을 대체하지 않는다. 자동 복구/최근 작업 서비스의 VM·앱 연결 및 실제 사용자 AppData 사용은 아직 미구현·미검증이고, 전체 P1/P0 완료를 뜻하지 않는다.
