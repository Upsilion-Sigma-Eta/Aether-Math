# P2 분수 Display pin 최종 실행 manifest

기록일: 2026-10-22 UTC. 이 manifest는 분수-local Display pin/`\dfrac` 하위 단위의 통합 승인 증거를 실행별로 구분한다. 모든 테스트 파일 I/O는 `TempDirectory`, UI는 전용 Headless child의 `STUB`/0 경계를 사용했다.

## 실행 결과

| 단계 | 로그 | 결과 | 설명 |
|---|---|---|---|
| 최초 Debug 빌드 | [build-01](build-dfrac-pin-final-01.log) | 경고 0, 오류 0, exit 0 | 최초 빌드. |
| 초기 신규 실행 | [new6-01](dfrac-pin-new6-final-01.log) | 5/6, exit 1 | 논리 5건 통과. UI helper가 세로 8 DIP 여유를 가로에도 적용해 정상 Apply 버튼을 거부했다. 실패 PNG/bounds 6쌍을 보존했다. |
| 수정 Debug 빌드 | [build-02](build-dfrac-pin-final-02.log) | 경고 0, 오류 0, exit 0 | UI helper의 가로 strict containment와 세로 여유 검사를 분리한 뒤 빌드. |
| 신규 6건 | [new6-02](dfrac-pin-new6-final-02.log) | 6/6, exit 0 | 논리5+`DFRAC-PIN-UI`1. 메인이 아래 12쌍 PNG/bounds를 직접 검토했다. |
| 기존 조판 UI 단독 회귀 | [TYPESETTING-UI](dfrac-pin-typesetting-ui-final-01.log) | 1/1, exit 0 | 이전 전체 회귀 실패의 ComboBox viewport 경계를 검증했다. 메인은 별도 실행의 PNG 3개와 bounds 10개를 직접 검토했다. |
| 전체 회귀 첫 실행 | [full171-01](dfrac-pin-full171-final-01.log) | 170/171, exit 1 | 기존 `TYPESETTING-UI`의 조판 스타일 ComboBox viewport assertion 실패. 실패 이력은 보존했다. |
| 최종 Debug 빌드 | [build-03](build-dfrac-pin-final-03.log) | 경고 0, 오류 0, exit 0 | helper 수정 후 최종 통합 빌드. |
| 최종 전체 회귀 | [full171-02](dfrac-pin-full171-final-02.log) | 171/171, exit 0 | 기존 실패 UI를 포함해 전체 회귀 통과. 이 결과와 시각 리뷰를 메인이 확인해 해당 하위 단위를 통합 승인했다. |

## 신규6-02 직접 검토 세트

아래 각 폴더에 `main-window.png`와 `bounds.json`이 있으며 메인이 12쌍 모두 직접 검토했다. STUB/0, 속성 패널 224 DIP, 중앙 viewport 최소/기본 257/437 DIP 및 상속·명시·pin 초안·적용·undo·stale·WorkspaceDefault 비활성 안내를 확인했다.

| 화면 | PNG | bounds |
|---|---|---|
| 초기 최소 위 | [PNG](dfrac-pin-initial-minimum-top-1060x700-20261022T204435736Z-bcf336ab364a462c82b61cbc15c20e3d/main-window.png) | [JSON](dfrac-pin-initial-minimum-top-1060x700-20261022T204435736Z-bcf336ab364a462c82b61cbc15c20e3d/bounds.json) |
| 초기 최소 아래 | [PNG](dfrac-pin-initial-minimum-bottom-1060x700-20261022T204435790Z-f4593487af5b4cc9922605a4590403f0/main-window.png) | [JSON](dfrac-pin-initial-minimum-bottom-1060x700-20261022T204435790Z-f4593487af5b4cc9922605a4590403f0/bounds.json) |
| 초기 기본 위 | [PNG](dfrac-pin-initial-default-top-1360x880-20261022T204435832Z-346044d28f7546e4ab18193e5c0e7347/main-window.png) | [JSON](dfrac-pin-initial-default-top-1360x880-20261022T204435832Z-346044d28f7546e4ab18193e5c0e7347/bounds.json) |
| 초기 기본 아래 | [PNG](dfrac-pin-initial-default-bottom-1360x880-20261022T204435869Z-1383ea824d57486f8287cba3092234a0/main-window.png) | [JSON](dfrac-pin-initial-default-bottom-1360x880-20261022T204435869Z-1383ea824d57486f8287cba3092234a0/bounds.json) |
| 분수 상세 | [PNG](dfrac-pin-detail-minimum-1060x700-20261022T204435928Z-f062395a398d47ec88982cb3e402a625/main-window.png) | [JSON](dfrac-pin-detail-minimum-1060x700-20261022T204435928Z-f062395a398d47ec88982cb3e402a625/bounds.json) |
| pin 초안 | [PNG](dfrac-pin-draft-minimum-1060x700-20261022T204436001Z-30a9efd8ccbd4d50b96ea0469da3d9c0/main-window.png) | [JSON](dfrac-pin-draft-minimum-1060x700-20261022T204436001Z-30a9efd8ccbd4d50b96ea0469da3d9c0/bounds.json) |
| 적용 최소 | [PNG](dfrac-pin-applied-minimum-1060x700-20261022T204436105Z-1070d41a85cc4a50b4b39f4812528d93/main-window.png) | [JSON](dfrac-pin-applied-minimum-1060x700-20261022T204436105Z-1070d41a85cc4a50b4b39f4812528d93/bounds.json) |
| 적용 기본 | [PNG](dfrac-pin-applied-default-1360x880-20261022T204436133Z-064a5275621444e7938cb90673290588/main-window.png) | [JSON](dfrac-pin-applied-default-1360x880-20261022T204436133Z-064a5275621444e7938cb90673290588/bounds.json) |
| Undo | [PNG](dfrac-pin-undo-minimum-1060x700-20261022T204436243Z-4a08032d5a414c93ba94a0502a6ea68a/main-window.png) | [JSON](dfrac-pin-undo-minimum-1060x700-20261022T204436243Z-4a08032d5a414c93ba94a0502a6ea68a/bounds.json) |
| stale 하단 | [PNG](dfrac-pin-stale-bottom-1060x700-20261022T204436776Z-a7303838c2bb4128a4f75acc20db6a5c/main-window.png) | [JSON](dfrac-pin-stale-bottom-1060x700-20261022T204436776Z-a7303838c2bb4128a4f75acc20db6a5c/bounds.json) |
| Workspace 비활성 최소 | [PNG](dfrac-pin-workspace-disabled-minimum-1060x700-20261022T204436870Z-9afa50760c2648369dff04585c3fdbb6/main-window.png) | [JSON](dfrac-pin-workspace-disabled-minimum-1060x700-20261022T204436870Z-9afa50760c2648369dff04585c3fdbb6/bounds.json) |
| Workspace 비활성 기본 | [PNG](dfrac-pin-workspace-disabled-default-1360x880-20261022T204436899Z-5e576bc8832f444ea9180ff0a5f3870e/main-window.png) | [JSON](dfrac-pin-workspace-disabled-default-1360x880-20261022T204436899Z-5e576bc8832f444ea9180ff0a5f3870e/bounds.json) |

## 단독 TYPESETTING-UI 직접 검토

2026-10-22 20:53:22–20:53:24 UTC에 생성된 직접 검토 이미지와 bounds는 다음이다. 메인은 Display 상세 최소창, Row Script 상세 최소창, stale 최소창의 PNG 3개 및 같은 실행 bounds 10개를 검토했다.

- [Display 상세 최소창](../p2-typesetting/typesetting-detail-display-minimum-1060x700-20261022T205322667Z-25d327af7dcc419badc3d89cf49bcced/main-window.png)
- [Row Script 상세 최소창](../p2-typesetting/typesetting-detail-row-script-minimum-1060x700-20261022T205323864Z-e6e060a50d59439f9377eae3981af961/main-window.png)
- [stale 최소창 하단](../p2-typesetting/typesetting-stale-minimum-bottom-1060x700-20261022T205324144Z-a290231bd9aa483e92cff35e5483700e/main-window.png)
- [비교 JSON](../p2-typesetting/equation-style-comparison-20261022T205322899Z-e35f57cc3a3045788b0c5d13dd44a8f3/equation-typesetting-comparison.json)

같은 단독 실행의 bounds 10개 경로는 아래와 같다. 처음 세 화면은 위에 연결한 PNG를 메인이 함께 확인했고 나머지 일곱 bounds도 해당 실행의 bounds 검토에 포함됐다.

- [초기 최소 상단 bounds](../p2-typesetting/typesetting-initial-minimum-top-1060x700-20261022T205322564Z-83f3abf103e54b999d2e35cfa4f2dd68/bounds.json)
- [초기 최소 하단 bounds](../p2-typesetting/typesetting-initial-minimum-bottom-1060x700-20261022T205322638Z-fb4ce15d93634448b687450d20b1772d/bounds.json)
- [Display 상세 최소 bounds](../p2-typesetting/typesetting-detail-display-minimum-1060x700-20261022T205322667Z-25d327af7dcc419badc3d89cf49bcced/bounds.json)
- [초기 기본 상단 bounds](../p2-typesetting/typesetting-initial-default-top-1360x880-20261022T205322820Z-24ebe179c02d4a9281dd05aae94d25d3/bounds.json)
- [초기 기본 하단 bounds](../p2-typesetting/typesetting-initial-default-bottom-1360x880-20261022T205322861Z-0c07c06c22314a5494a159cd9a0daf4c/bounds.json)
- [Text 상세 기본 bounds](../p2-typesetting/typesetting-detail-text-default-1360x880-20261022T205323025Z-57dbc0ba01ff4ef2a021482fa1f588e5/bounds.json)
- [Row Script 상세 최소 bounds](../p2-typesetting/typesetting-detail-row-script-minimum-1060x700-20261022T205323864Z-e6e060a50d59439f9377eae3981af961/bounds.json)
- [최종 기본 상단 bounds](../p2-typesetting/typesetting-final-default-top-1360x880-20261022T205324250Z-0fa76dfe068141399701089dbc4f6511/bounds.json)
- [최종 기본 하단 bounds](../p2-typesetting/typesetting-final-default-bottom-1360x880-20261022T205324284Z-e23b9aa49d8b4c5ea31446095000c699/bounds.json)
- [stale 하단 bounds](../p2-typesetting/typesetting-stale-minimum-bottom-1060x700-20261022T205324144Z-a290231bd9aa483e92cff35e5483700e/bounds.json)

## 전체171-02 자동 산출물

전체171-02가 2026-10-22 20:55:33–20:55:34 UTC에 새로 만든 Dfrac UI PNG/bounds 12쌍은 자동 산출물이다. 직접 검토한 신규6-02 세트와 구분하며 이 전체 세트 전부를 메인이 직접 검토한 것으로 기록하지 않는다.

- `dfrac-pin-initial-minimum-top-1060x700-20261022T205533669Z-844d47ca32c54b8ea286459cb3c3ed6c`
- `dfrac-pin-initial-minimum-bottom-1060x700-20261022T205533723Z-94eab21471ae46228ee8a197b4340333`
- `dfrac-pin-initial-default-top-1360x880-20261022T205533751Z-2d22e5783356470fbbce863d6866f379`
- `dfrac-pin-initial-default-bottom-1360x880-20261022T205533790Z-5ad8cdd5d8224a9ab77973d109fcc1d1`
- `dfrac-pin-detail-minimum-1060x700-20261022T205533852Z-b7b262dec49548088fdaf6a19a62b6c7`
- `dfrac-pin-draft-minimum-1060x700-20261022T205533924Z-d56316d5498d4c4fb2512783f401f7fb`
- `dfrac-pin-applied-minimum-1060x700-20261022T205534027Z-25884e5d58504591a18d48894a6f2366`
- `dfrac-pin-applied-default-1360x880-20261022T205534054Z-decd258a57284e25acdef27624139213`
- `dfrac-pin-undo-minimum-1060x700-20261022T205534158Z-66e5d7fee12d4f8cb44640173919918b`
- `dfrac-pin-stale-bottom-1060x700-20261022T205534691Z-19441aff89194c1c88ca788cc6226eff`
- `dfrac-pin-workspace-disabled-minimum-1060x700-20261022T205534785Z-72d0f0c08f0941c097191cceb77ab049`
- `dfrac-pin-workspace-disabled-default-1360x880-20261022T205534814Z-fb2d52074a61428fbd16e21da76619f1`

이번 승인은 분수 자체의 explicit Display pin, 한 번의 결합 적용/Undo, `\dfrac` LaTeX 문자열과 layout/SVG/file/recovery 경계에 한정된다. 사용자 AppData, 실제 OS 입력·포커스·UIA, clipboard, 외부 TeX, 구버전 앱 실행 및 전체 P2/P1/P0 완료는 포함하지 않는다.
