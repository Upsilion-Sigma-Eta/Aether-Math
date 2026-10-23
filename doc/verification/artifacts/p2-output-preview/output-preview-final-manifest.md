# P2 외부 출력 미리보기 실행 manifest

기록일: 2026-10-22 UTC 실행 결과. Debug 빌드와 신규·전체 회귀는 각각 별도 로그에 보존했다. 실패 실행은 후속 수정·통과 실행과 합치지 않았다.

## 빌드 이력

| 로그 | 결과 | 비고 |
|---|---|---|
| [build-output-preview-final-01.log](build-output-preview-final-01.log) | exit0, 경고2, 오류0 | 첫 통합 빌드. `MathOutputPreviewViewModel.cs` nullable 경고와 `RowShellVisualCases.cs` nullable 인수 경고를 보존했다. |
| [build-output-preview-final-02.log](build-output-preview-final-02.log) | exit0, 경고0, 오류0 | 후속 Debug 빌드. |
| [build-output-preview-final-03.log](build-output-preview-final-03.log) | exit0, 경고0, 오류0 | 다중 glyph primitive 단언과 고정 action 배치를 수정한 뒤 재빌드. layout rounding 실패는 이 빌드 뒤 new9-02에서 확인됐고, helper 수정과 최종 재빌드는 build04에서 이뤄졌다. |
| [build-output-preview-final-04.log](build-output-preview-final-04.log) | exit0, 경고0, 오류0 | 승인된 최종 Debug 빌드. |

## 신규9 실행 이력

| 로그 | 결과 | 산출물 및 해석 |
|---|---|---|
| [output-preview-new9-final-01.log](output-preview-new9-final-01.log) | exit1, 7/9 | `OUT-PREVIEW-MODE`가 grapheme별 다중 glyph primitive를 단일 glyph로 가정했고 UI tool containment도 실패했다. QA는 모든 해당 node glyph 크기를 확인하도록 바꿨고 UI 소유자가 panel 배치/진단을 보강했다. 이 실행 전용 PNG/bounds는 생성되지 않았다. |
| [output-preview-new9-final-02.log](output-preview-new9-final-02.log) | exit1, 8/9 | UI control box가 frozen layout의 자연 DIP 크기와 정확히 같아야 한다는 assertion이 Avalonia 기본 layout rounding에 걸렸다. 공식 [Avalonia 12.1.4 `Layoutable.cs`](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.4/src/Avalonia.Base/Layout/Layoutable.cs)의 `UseLayoutRounding` 기본값과 `MeasureCore`/`ArrangeCore`의 `RoundLayoutSizeUp` 동작에 근거해 helper를 수정했다. 실제 control 크기는 frozen layout 이상이며 각 축 차가 1 DIP 미만임을 검사하고, 동일 `LayoutResult` 참조 검사는 유지했다. 실패 전 PNG/bounds는 생성되지 않았다. |
| [output-preview-new9-final-03.log](output-preview-new9-final-03.log) | exit0, 9/9 | 메인이 19:17:48 UTC에 생성된 아래 6 PNG와 bounds를 직접 검토해 승인했다. STUB/0, 두 크기, 세 출력 모드, stale/incomplete 상태, 탭·고정 작업 도구 비겹침과 viewport를 확인했다. |

### 신규9-03 메인 직접 검토 자료

| 상태 | PNG | bounds |
|---|---|---|
| 최소 크기 Ready | [main-window.png](output-ready-minimum-1060x700-20261022T191748259Z-92a7ba6a713446e7838e0765ed443ca8/main-window.png) | [bounds.json](output-ready-minimum-1060x700-20261022T191748259Z-92a7ba6a713446e7838e0765ed443ca8/bounds.json) |
| 기본 크기 Ready | [main-window.png](output-ready-default-1360x880-20261022T191748420Z-c250ff23050f4c729f0ac9ce51764fac/main-window.png) | [bounds.json](output-ready-default-1360x880-20261022T191748420Z-c250ff23050f4c729f0ac9ce51764fac/bounds.json) |
| Display 모드 | [main-window.png](output-display-default-1360x880-20261022T191748579Z-f8e9807f51a746b690564c8c649a99a5/main-window.png) | [bounds.json](output-display-default-1360x880-20261022T191748579Z-f8e9807f51a746b690564c8c649a99a5/bounds.json) |
| Inline 모드 | [main-window.png](output-inline-default-1360x880-20261022T191748725Z-d79118a96a5840149c23dc336db312db/main-window.png) | [bounds.json](output-inline-default-1360x880-20261022T191748725Z-d79118a96a5840149c23dc336db312db/bounds.json) |
| 최소 크기 Stale | [main-window.png](output-stale-minimum-1060x700-20261022T191748784Z-915dc5c90cbd47bca65b18f51b53f433/main-window.png) | [bounds.json](output-stale-minimum-1060x700-20261022T191748784Z-915dc5c90cbd47bca65b18f51b53f433/bounds.json) |
| 최소 크기 Incomplete | [main-window.png](output-incomplete-minimum-1060x700-20261022T191748890Z-540560e5a9e746958df50ffa74af52b4/main-window.png) | [bounds.json](output-incomplete-minimum-1060x700-20261022T191748890Z-540560e5a9e746958df50ffa74af52b4/bounds.json) |

## 전체156-01 자동 산출물

전체 run은 19:20:01–19:20:02 UTC에 별도 캡처를 생성했다. 아래는 자동 산출물이며 신규9-03의 직접 검토 근거를 대체하지 않는다.

| 상태 | PNG | bounds |
|---|---|---|
| 최소 크기 Ready | [main-window.png](output-ready-minimum-1060x700-20261022T192001905Z-678955d275014559897bd3006b8f9b97/main-window.png) | [bounds.json](output-ready-minimum-1060x700-20261022T192001905Z-678955d275014559897bd3006b8f9b97/bounds.json) |
| 기본 크기 Ready | [main-window.png](output-ready-default-1360x880-20261022T192002071Z-9d4076faeb7449beaec1f6eb4c08aaea/main-window.png) | [bounds.json](output-ready-default-1360x880-20261022T192002071Z-9d4076faeb7449beaec1f6eb4c08aaea/bounds.json) |
| Display 모드 | [main-window.png](output-display-default-1360x880-20261022T192002210Z-cbd4bd8c124b413cb8a5cf5fad933eb8/main-window.png) | [bounds.json](output-display-default-1360x880-20261022T192002210Z-cbd4bd8c124b413cb8a5cf5fad933eb8/bounds.json) |
| Inline 모드 | [main-window.png](output-inline-default-1360x880-20261022T192002344Z-f88d3111bef84603a8c60adfa02c1240/main-window.png) | [bounds.json](output-inline-default-1360x880-20261022T192002344Z-f88d3111bef84603a8c60adfa02c1240/bounds.json) |
| 최소 크기 Stale | [main-window.png](output-stale-minimum-1060x700-20261022T192002403Z-63fdbb600a4e47fca422f858287fc81b/main-window.png) | [bounds.json](output-stale-minimum-1060x700-20261022T192002403Z-63fdbb600a4e47fca422f858287fc81b/bounds.json) |
| 최소 크기 Incomplete | [main-window.png](output-incomplete-minimum-1060x700-20261022T192002510Z-6a648df73d544237866e4aa78a69359d/main-window.png) | [bounds.json](output-incomplete-minimum-1060x700-20261022T192002510Z-6a648df73d544237866e4aa78a69359d/bounds.json) |

## 승인 범위

`build-output-preview-final-04.log` 경고0·오류0, `output-preview-new9-final-03.log` 9/9, `output-preview-full156-final-01.log` 156/156이며 모두 exit0이다. 메인 시각 검토는 신규9-03 자료 6쌍에 한정되고 전체156-01 자료는 별도 자동 산출물이다. 이번 승인은 논리·Headless/offscreen 범위의 외부 Display/Inline 전체 작업 SVG 미리보기 하위 단위에 한정한다. 네이티브 OS 창·물리 입력·포커스·UIA·clipboard·사용자 AppData·외부 TeX·구버전 앱 런타임은 검증하지 않았으며, P2/P1/P0 전체 완료를 뜻하지 않는다.
