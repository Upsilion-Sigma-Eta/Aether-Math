# 최종 실행 종료 코드 기록

2026-10-22에 동결된 P1 VM/UI 소스로 아래 검증을 순서대로 실행했다. 종료 코드는 실행 도구가 보고한 프로세스 반환값이며 각 로그 본문에도 결과 행이 포함된다.

| 단계 | 명령 | 프로세스 종료 코드 | 로그 |
|---|---|---:|---|
| Debug 통합 빌드 | `dotnet build AetherMath.sln -c Debug --no-restore` | 0 (경고 0, 오류 0) | `build-p1-recovery-integration-final-05.log` |
| 신규 9개 | `dotnet run --project tests/AetherMath.LogicQA/AetherMath.LogicQA.csproj -c Debug --no-build -- VM-STATE-INIT AUTO-RECOVERY-TICK AUTO-RECOVERY-CLEANUP AUTO-RECOVERY-STALE RECOVERY-RESTORE RECOVERY-DIRTY-GUARD RECENT-SUCCESS RECENT-MISSING-RELINK LIBRARY-UI` | 0 (9/9) | `recovery-recent-new-9-final-03.log` |
| 기존 포함 전체 | `dotnet run --project tests/AetherMath.LogicQA/AetherMath.LogicQA.csproj -c Debug --no-build` | 0 (98/98) | `regression-98-final-01.log` |

시각 검토를 받은 전체98 실행 산출물은 `library-initial-1060x700-20261022T144345471Z-4f913e01efb64df3b0864744bdc4f7ca`, `library-preview-1060x700-20261022T144345518Z-3c8c6362877c4c4183bca7c926d94ea2`, `library-default-1360x880-20261022T144345546Z-8f5855c9f3694df0a15bae12473ec94f`, `library-final-minimum-1060x700-20261022T144347136Z-f01c15e8c5404ecc82484954c50ed364` 폴더의 PNG와 `bounds.json`이다.
