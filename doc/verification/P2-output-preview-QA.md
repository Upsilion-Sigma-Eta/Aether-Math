# P2 외부 Display/Inline 미리보기 QA 기록

- 상태: 논리8건+Headless UI1건 및 전체156 회귀 통과; 메인 신규 화면 직접 검토 후 외부 Display/Inline 미리보기 하위 단위 통합 승인
- 기준선: 승인된 전체 회귀 147건
- 목표: 신규 논리 8건 + `OUTPUT-PREVIEW-UI` 1건, 전체 156건
- 산출물 위치: `doc/verification/artifacts/p2-output-preview/`
- 제품 계약 근거: 사용자 외부 삽입 Display/Inline 미리보기 하위 단위에 대한 승인된 공개 API

이 단위는 기존 WorkspaceStyle 수식 출력을 유지하면서 Display 또는 Inline 문맥으로 고정된 출력 미리보기를 검증한다. 별도 frozen preview에서만 모드를 바꾸며 원본 편집 트리나 작업 스타일을 변경하지 않는다. 이 하위 단위는 논리·Headless/offscreen 범위에서 통합 승인됐으며, 전체 P2/P1/P0 또는 실제 OS 검증 완료를 의미하지 않는다.

## 검증 범위

공개 API는 `MathOutputMode { WorkspaceStyle, Display, Inline }`, `MathOutputPreviewState { Empty, Ready, Stale, Incomplete, Failed }`, `MathOutputPreviewSnapshot`, `SvgPreviewExportStatus`, `SvgPreviewExportResult`, `ISvgExportService.ExportAsync(path, layout, ct)`를 사용한다. `MathOutputPreviewViewModel`은 `EditingSession`, `IMathTextMetrics`, exporter를 받고 `RefreshPreview`, `ExportSvgToPathAsync`, `SetSession`으로 조작한다. `MainWindowViewModel.OutputPreview`는 안정된 인스턴스이며 `ShowOutputPreviewCommand`, `IsOutputPreviewVisible` 및 기존 `ExportSvg` 경로를 보존한다.

모드 투영은 workspace root preset에만 적용한다. Display는 root 조판을 Display, Inline은 Text로 해석하고 WorkspaceStyle은 저장된 설정을 사용한다. group/row/node의 명시 override는 각 원래 우선순위대로 유지한다. 미리보기 snapshot은 WorkspaceId·Revision·StateId·Mode와 동일한 frozen `LayoutResult`를 보유한다. 탐색만으로는 stale 상태가 되지 않고, 편집·mode 변경·session 교체 뒤에는 이전 frozen 결과를 보존한 채 stale로 표시한다. 새 결과는 명시 refresh로만 만든다.

빈 필수 슬롯은 Incomplete이며 SVG 저장을 차단한다. 저장 시작 뒤 busy 재진입은 거절하고, 저장은 시작 때 캡처한 LayoutResult를 그대로 사용한다. export 도중 편집·모드 변경·session 교체·새 refresh가 생긴 늦은 완료는 `SavedStale`을 반환할 수 있어도 현재 preview 상태나 export 요약/경로를 덮어쓰지 않는다. picker가 열린 동안 새 미리보기가 만들어진 경우 오래된 expected snapshot을 넘겨도 I/O 없이 Stale로 반환하고 새 Ready 상태·진단·요약을 유지한다. 취소를 무시한 exporter가 실제로 저장을 끝낸 경우에는 이를 `Canceled`로 잘못 표시하지 않는다. 서비스의 실제 취소/실패 경로는 기존 파일 bytes와 임시파일 정리를 확인한다.

## 논리 사례

| ID | 검증 경계 |
|---|---|
| `OUT-PREVIEW-MODE` | WorkspaceStyle/Display/Inline 투영과 group/row/node 명시 override 우선순위 및 보존 |
| `OUT-PREVIEW-PURITY` | 생성·모드 전환 중 AST/ID/dirty/revision/history/caret/selection 불변, layout 참조 동결 |
| `OUT-PREVIEW-REFRESH` | navigation-only의 Ready 유지, edit/mode/session 변경의 Stale 전이, 측정 예외 실패·snapshot 보존·복구, 측정 중 mode 재진입 차단, 명시 refresh만 갱신 |
| `OUT-PREVIEW-INCOMPLETE` | 필수 빈 슬롯 진단, Incomplete 상태, exporter 미호출 및 파일 미생성 |
| `OUT-SVG-EXACT` | 동일 frozen LayoutResult 참조 전달, 실제 SVG 파일의 XML geometry/text와 해당 layout 일치 |
| `OUT-SVG-RACE` | TCS gate 중 Busy 재진입, edit·mode/session 변경·새 refresh 후 늦은 완료의 `SavedStale` 및 현재 요약/경로 비침범 |
| `OUT-SVG-CANCEL-FAIL` | 실제 SVG 서비스 취소·실패 시 기존 bytes 보존과 임시파일 정리; 취소 무시 성공은 성공 상태로 분리 |
| `OUT-SVG-STALE-START` | stale snapshot 저장 시작 차단, exporter 호출·파일 I/O 0회 |

논리 테스트는 [`OutputPreviewCases.cs`](../../tests/AetherMath.LogicQA/OutputPreviewCases.cs)에 저장했고 `Program.cs`의 전체 회귀 목록에 등록했다. UI child route `--output-preview-ui-headless`와 `OutputPreviewUiInteractionCases.cs`도 등록했다. 동기화에 사용한 race gate는 TCS 기반이며 임의 sleep은 사용하지 않는다.

기존 lifecycle UI의 상단 SVG 버튼 회귀는 이제 미리보기 진입 버튼의 `ShowOutputPreviewCommand` 및 실제 AutomationName을 검사한다. 논리 purity 사례는 기존 `ExportSvgCommand`가 별도 명령으로 남았음을 확인하고, 기존 ViewModel 사례는 picker 없는 `ExportSvgToPathAsync`의 저장 경계를 계속 검사한다. native picker 자체는 호출하지 않는다. SVG geometry 대조는 serializer가 좌표를 소수 셋째 자리까지 기록하는 반올림을 반영해 0.001 DIP 허용 오차를 명시한다.

## UI 및 실행 경계

UI 소유 테스트는 `OutputPreviewUiInteractionCases.All()`의 `OUTPUT-PREVIEW-UI`, `--output-preview-ui-headless` child route 및 `RunHeadlessChildAsync(string)`를 사용한다. Program 등록은 QA 소유이며, MainWindow UI와 전용 helper는 UI 소유다. actual App/Headless+Skia owned child만 허용하고 표시 전후 platform handle `STUB`/0을 확인한다. UI는 별도 preview control을 사용하며 편집 surface의 모델 상태를 변경하지 않아야 한다. 파일은 `TempDirectory`에만 쓴다. native OS 창·입력·포커스·UIA·clipboard·사용자 AppData와 computer-use는 금지한다.

실제 파일 I/O는 `TempDirectory`에 격리했다. native OS 창·입력·포커스·UIA·clipboard·사용자 AppData와 computer-use는 금지 상태로 두었고, UI는 표시 전후 `STUB`/0을 확인하는 소유 Headless child만 사용했다.

## 결과

| 단계 | 상태 |
|---|---|
| Debug build | 최종 `build-output-preview-final-04.log`: 경고0·오류0, 실제 exit0. 최초 build01은 경고2·오류0·exit0; 후속 build02/03도 0/0·exit0. |
| 신규 9건 | `output-preview-new9-final-03.log`: 9/9, 실제 exit0. 앞선 new9-01은 7/9·exit1, new9-02는 8/9·exit1이며 실패 로그를 보존했다. |
| 전체 156건 | `output-preview-full156-final-01.log`: 156/156, 실제 exit0. 전체 run 산출물은 신규 run 직접 검토 자료와 분리했다. |
| 메인 UI 이미지 리뷰 | 신규9-03의 19:17:48 UTC PNG/bounds 6쌍을 직접 검토했다. 전체156-01의 19:20:01–02 UTC PNG/bounds 6쌍은 별도 자동 산출물이며 직접 검토 근거가 아니다. 상세 manifest는 [실행별 결과와 이미지](artifacts/p2-output-preview/output-preview-final-manifest.md)에 있다. |

2026-10-22 18:54 및 19:09 UTC 정기 감사는 중단급 문제 없이 종료됐으며, 감사 구간에는 요청에 따라 작업을 정지했다. 이는 절차 기록일 뿐 제품 동작의 검증 근거가 아니다. 본 승인 범위는 외부 출력 미리보기의 검증된 하위 계약에 한정한다. 네이티브 OS 창·입력·포커스·UIA·clipboard·사용자 AppData, 외부 TeX 프로그램, 구버전 앱 런타임은 검증하지 않았다. P2/P1/P0 전체 완료로 확대하지 않는다.
