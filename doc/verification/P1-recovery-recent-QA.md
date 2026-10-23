# P1 복구본·최근 작업 저장 서비스 QA 계획 및 기록

- 상태: QA 실행 및 저장 서비스 기반 단위 통합 승인 완료. 최종 Debug 빌드 경고 0·오류 0, 신규 8/8, 전체 회귀 87/87이 통과했다. 승인은 명시 root의 recent/recovery 저장 계층에 한정하며 VM/앱 사용자 흐름과 P1 전체는 미완료다.
- 회귀 목표: 기존 79개 + 저장 서비스 논리 8개 = 전체 87개.
- 범위: `WorkspaceStateService` recent index와 recovery envelope 저장·읽기·보호 경계. VM, UI, 타이머, 앱 자동 복구 흐름은 별도 후속 단위다.
- 담당: 제품 구현은 엔진 담당, 논리 테스트·`Program.cs`·문서는 QA 담당. 실행은 동결 승인 뒤 QA 담당 단독.
- 격리: 각 케이스의 root와 fixture는 `TempDirectory` 아래에 둔다. 실제 사용자 AppData, 실제 프로젝트 상태 경로와 사용자 workspace 파일은 사용하지 않는다.

## 승인된 저장 계약

`WorkspaceStateService(string rootDirectory)`는 명시된 root만 사용한다. 공개 파일 이름은 `RecentIndexFileName` (`recent-workspaces.json`), `RecoveryDirectoryName` (`recovery`), `RootLockFileName` (`.state.lock`)이다. JSON은 camelCase, case-sensitive, 미등록 필드 거부 규칙을 사용한다. recent index는 `{ version, entries: [{ fullPath, displayName, lastUsedAtUtc, isPinned }] }`, recovery는 `{ version, recoveryId, capturedAtUtc, originalPath, snapshot: { revision, stateId, workspace } }` 형태다. 화면 표시용 파일 상태와 메타데이터는 persisted index에 저장하지 않고 조회 시 계산한다.

recent API는 `ReadRecentWorkspacesAsync`, `RecordSuccessfulUseAsync`, `SetPinnedAsync`, `RemoveRecentAsync`다. entry의 `FullPath`는 정규화된 절대 경로이며 `LastUsedAtUtc`는 파일의 `FileModifiedAtUtc`와 별개다. 파일 상태는 `Available`, `Missing`, `MetadataUnavailable`로 구분한다. 중복 경로는 `OrdinalIgnoreCase`로 통합하며, 고정 항목 우선·최근 사용 내림차순·경로 동률 정렬을 쓴다. 고정되지 않은 항목은 최대 20개이고 고정 항목은 보존한다. 없는 경로의 pin/remove는 phantom 항목을 만들지 않고 `NotFound`; 같은 pin 값은 `Unchanged`다.

recovery API는 `SaveRecoveryAsync`, `ListRecoveriesAsync`, `ReadRecoveryAsync(Guid)`, `DeleteRecoveryAsync(Guid)`다. canonical 파일은 `recovery/{recoveryId:D}.json`이다. snapshot AST와 필수 슬롯을 검증하며, 저장 입력의 경로·시각을 정규화한다. 읽을 때 envelope ID가 파일 ID와 다르거나 저장된 `originalPath`가 상대 경로이면 손상으로 진단하고 파일을 덮어쓰지 않는다. 잘못된 JSON/AST는 `RecoveryInvalid`, 미래 버전은 `RecoveryUnsupportedVersion`, ID 불일치는 `RecoveryIdentityMismatch`, root 아래 위험 경로는 `UnsafePath` 진단을 사용한다. `Guid.Empty`는 `ArgumentException`, 선취소 token은 `OperationCanceledException`이다. 목록 중 한 파일의 I/O 실패는 해당 파일 진단을 남기고 다른 정상 복구본을 계속 반환한다.

index의 손상·필드 오류는 `RecentIndexInvalid`, 미래 버전은 `RecentIndexUnsupportedVersion`과 `CanWrite=false`를 반환한다. 이후 mutation은 `WriteBlocked`이고 기존 원본 바이트를 보존한다. recovery의 corrupt/unsupported 기존 파일도 `WriteBlocked`로 재저장을 막는다. 동일 root의 여러 service 인스턴스는 process-wide gate로 read-modify-write를 직렬화하고 `.state.lock`을 `FileShare.None`으로 연다. `DeleteRecoveryAsync`는 지정된 canonical GUID 파일만 삭제하며 자동 정리는 하지 않는다.

## 논리 회귀

테스트는 [WorkspaceStateCases.cs](../../tests/AetherMath.LogicQA/WorkspaceStateCases.cs)에 8개를 추가했고 `Program.cs`에 등록해 신규 8개, 전체 87개가 되도록 준비했다.

| 케이스 | 검증 범위 | 현재 상태 |
|---|---|---|
| `STATE-RECENT-INDEX` | `./` 경로 정규화, 대소문자 무시 중복 통합, 표시명/최근 사용 갱신, 비 UTC 입력의 UTC 저장·읽기. | 8/8 및 87/87에서 통과 |
| `STATE-RECENT-BOUNDS` | 오래된 고정 항목 보존, 고정 우선·최근순, 일반 항목 20개 상한, 동률 정렬의 결정성. | 8/8 및 87/87에서 통과 |
| `STATE-RECENT-METADATA` | 최근 사용 시각과 실제 수정 시각 분리, Available/Missing, 디렉터리 대상에서 MetadataUnavailable 및 진단. | 8/8 및 87/87에서 통과 |
| `STATE-RECENT-MUTATION` | pin/unpin/remove 상태, 같은 값 `Unchanged`, 없는 path `NotFound`, phantom index 미생성. | 8/8 및 87/87에서 통과 |
| `STATE-INDEX-PROTECTION` | 잘못된 필드·미래 버전, 진단 코드, `CanWrite=false`, mutation 차단 및 원본 bytes 보존. | 8/8 및 87/87에서 통과 |
| `STATE-RECOVERY-ROUNDTRIP` | 중첩 Unicode AST/snapshot, ID·revision·state·UTC·원본 경로 왕복, GUID canonical path, 목록·명시 삭제, 임의 이름 무시. 잠긴 개별 복구 파일이 있어도 정상 항목 반환과 진단을 확인. | 8/8 및 87/87에서 통과 |
| `STATE-RECOVERY-PROTECTION` | 선취소, `Guid.Empty`, 신규 invalid AST 저장 거부, envelope/file ID 불일치, 미래·손상 JSON, persisted 상대 `originalPath` 진단, 기존 파일 재저장 거부와 bytes 보존. | 8/8 및 87/87에서 통과 |
| `STATE-CONCURRENCY` | 여러 서비스 인스턴스의 16개 동시 RMW, 별도 root 격리, `.state.lock` 충돌, 읽기 허용 핸들로 atomic 교체 실패, 기존 index/recovery bytes 및 임시 파일 정리, 모든 async API의 선취소 불변성. | 8/8 및 87/87에서 통과 |

교체 실패는 Windows 파일 공유 규칙을 이용해 대상 파일을 `FileAccess.Read`/`FileShare.Read`로 열어 읽기는 허용하고 replace/delete를 막는다. `.state.lock` 충돌은 별도 `FileShare.None` 핸들로 만든다. 동시성은 임의 sleep 없이 여러 서비스 인스턴스의 `Task.WhenAll`로 재현한다. 테스트는 공개 API와 실제 임시 파일만 사용하며 private reflection과 소스 문자열 검사는 하지 않는다.

## 미검증 경계 및 후속 작업

- 서비스에 결정적 mid-write 취소 seam이 없으므로 현재 테스트는 모든 async API의 **선취소**와 기존 파일 불변성만 확인한다. 직렬화/flush 도중 취소 동작은 미검증이다.
- 메타데이터 오류는 디렉터리 경로로 `MetadataUnavailable`과 `UnsafePath` 진단을 확인한다. ACL 거부·플랫폼별 파일 metadata 실패는 별도 검증하지 않았다.
- 실제 cross-process 경쟁, 재부팅 후 복구 흐름, VM의 자동 복구 시점·사용자 선택, 최근 작업 UI, 사용자 AppData ACL, 네이티브 OS 창/파일 선택기와 물리 입력은 이 단위 밖이다.
- 저장-Undo 후속 경계는 이후 엔진의 regular-save ticket 처리와 `LIFECYCLE-SAVE-UNDO`, `LIFECYCLE-SAVE-CANCELLED-WRITE` 회귀로 검증됐다. 기존 lifecycle 9/9·전체79/79와 이 저장 서비스 당시 전체87/87에는 포함되지 않았지만, 통합 전체89/89에서 두 후속 사례까지 통과했고 메인이 해당 VM 후속 단위를 승인했다. 세부 결과는 [P1 lifecycle QA 기록](P1-lifecycle-protection-QA.md)에 별도 적었다.
- 이번 저장 서비스 단위는 자동 복구·최근 작업 기능 전체, 전체 P1 또는 P0 완료를 의미하지 않는다. 네이티브 OS 사용 제한과 물리 검증 보류는 그대로 유지한다.

## 실행 결과

| 단계 | 결과 | 로그 |
|---|---|---|
| 첫 Debug 빌드 | 종료 코드 0, 오류 0, nullable `ImmutableArray` 요소 타입 차이 경고 CS8620 1건. 이 빌드에서는 전체 회귀를 진행하지 않았다. | `doc/verification/artifacts/p1-workspace-state/build-workspace-state-01.log` |
| 초기 신규 8개 실행 | 8/8 통과, 종료 코드 0. 경고를 수정하기 전 동결 코드에서 실행한 이력으로 보존한다. | `doc/verification/artifacts/p1-workspace-state/state-new-8-01.log` |
| 엔진 nullable annotation 수정 후 Debug 빌드 | 종료 코드 0, 경고 0·오류 0. | `doc/verification/artifacts/p1-workspace-state/build-workspace-state-02.log` |
| 신규 저장 서비스 회귀 | 8/8 통과, 종료 코드 0. | `doc/verification/artifacts/p1-workspace-state/state-new-8-02.log` |
| 전체 회귀 | 기존 79개를 포함한 87/87 통과, 종료 코드 0. | `doc/verification/artifacts/p1-workspace-state/regression-full-87-01.log` |

메인은 최종 build02·신규8·전체87 로그와 저장 서비스 소스를 검토해 이 저장 기반 단위를 통합 승인했다. 승인은 `WorkspaceStateService`의 명시 root, recent index와 recovery CRUD, 기존 원본 보존, 정렬·metadata·동시성·진단 계약에만 적용한다. VM/앱이 자동 복구본을 언제 만들고 어떤 복구본을 사용자에게 제시하는지, 최근 작업을 실제 shell에 어떻게 노출하는지는 아직 구현·검증하지 않았다. 이 단위는 전체 P1/P0 완료를 뜻하지 않는다.

후속 저장 ticket 변경은 이 문서의 **저장 서비스 기준선 전체87**을 덮어쓰지 않는다. 이후 통합 회귀는 Debug 경고0·오류0, 신규 ticket 2/2, 전체89/89를 통과했다. 로그는 `doc/verification/artifacts/p1-lifecycle-save-ticket/`의 `build-lifecycle-save-ticket-01.log`, `lifecycle-save-ticket-new-2-01.log`, `lifecycle-save-ticket-full-89-01.log`에 보존했다. 다음 필수 작업은 명시 root의 state service를 VM·자동 복구·최근 작업 UI 흐름에 연결하는 일이다. 실제 사용자 AppData, OS 창/파일 선택기와 물리 입력은 계속 미검증이며 전체 P1/P0 완료가 아니다.

## 후속 단위: VM 자동 복구·최근 작업 연결

상태 저장 계층 단위의 8/8·전체87/87 승인 뒤, engine이 승인된 `IWorkspaceStateService` 주입 및 VM lifecycle API를 구현 중이며 QA가 별도 논리 8개와 actual App/MainWindow Headless UI 1개의 통합 사례를 작성하고 있다. 새 목표는 기존89개와 합쳐 총98개다. 실행 전 정적 검토/동결 승인을 기다리며 아직 실행하지 않았다. 정확한 사례와 실행 경계는 [P1 VM 자동 복구·최근 작업 통합 QA 기록](P1-recovery-recent-integration-QA.md)에 둔다. 모든 state root는 `TempDirectory`; 실제 사용자 AppData에는 접근하지 않는다.

| 준비 게이트 | 검증할 결과 | 결정성·보호 조건 |
|---|---|---|
| 자동 복구 생성 | dirty AST의 Workspace/Node ID, 구조, Unicode·style 및 snapshot revision이 복구본에 보존된다. 복구 저장은 Session의 dirty/history/saved marker를 바꾸지 않는다. | 임의 sleep 없이 주입된 clock/tick 또는 공개 완료 알림(TCS/PropertyChanged)으로 실제 timer 경로 완료를 await한다. |
| 저장·교체·정지 경합 | 성공한 clean Save 뒤 복구 정리, Save 실패 시 원 복구본 보존, Session 교체·timer 정지 뒤 오래된 tick의 쓰기 차단. | 지연 실제 state service 또는 명시된 테스트 service seam으로 완료 순서를 제어하고 stale 완료 뒤 파일 bytes와 VM 상태를 확인한다. |
| 재시작·명시 복원 | 새 VM의 후보 목록, 사용자가 지정한 recovery 복원, 새 dirty Session, 복구 AST의 원래 Workspace/Node ID 보존, `CurrentPath == null`. | 서비스 root를 재사용한 별도 VM으로 재시작을 재현하며 복원 완료를 명시적으로 await한다. |
| dirty 복원 보호 | dirty 현재 작업에서 recovery Restore를 요청하면 Cancel은 현재 편집/경로/history를 보존하고, Save는 현재 파일을 저장한 뒤 복원하며, Discard는 저장 없이 복원한다. | 기존 lifecycle confirmation 계약을 재사용하고 각 결과가 Session/경로/파일·dirty snapshot에 정확히 반영되는지 확인한다. |
| recent 반영·오류 상태 | successful Save/Open만 recent index를 갱신한다. pin/unpin/remove, 누락 경로, 명시적 relocate, metadata unavailable 진단 및 원본 경로/파일 보존을 확인한다. | 실패·취소한 Open/Save는 recent를 바꾸지 않는다. picker를 호출하지 않고 테스트 경로를 API에 직접 주입한다. |

UI child는 `LibraryUiInteractionCases`에서 복구 후보 미리보기·선택(선택만으로 Session/selection 불변), restore Cancel/Save/Discard, recent pin/remove/missing/relocate 및 오류/재시도 표현을 합성 상호작용으로 확인하고, 1060×700·1360×880 shell PNG/bounds를 전용 artifact 경로에 저장한다. Program child route는 `--library-ui-headless LIBRARY-UI`로 연결 중이다. 네이티브 OS 창·파일 선택기·포커스·UIA·물리 입력은 금지하고, 전용 Headless child의 STUB/0 표시만 허용한다. 새 단위는 아직 실행·통합 승인 전이다.
