# 얇은 수학 공백 실행 manifest

최종 상태: 기준선 181건에 신규 논리9건과 `THIN-MATH-SPACE-UI` 1건을 더한 전체191/191이 통과했다. 최종 build07은 경고0·오류0·exit0, 별도 UI03은 1/1·exit0, 전체191-01은 191/191·exit0이다. 메인은 full191 원로그에서 신규10 모두 PASS인 것을 확인하고, UI03 직접 시각 검토와 함께 이 하위 단위를 논리·Headless·offscreen 범위에서 통합 승인했다. 전체 제품 또는 P2/P1/P0 완료 승인은 아니다.

| 단계 | 원로그 | 결과 |
|---|---|---|
| Debug build01 | [build-thin-math-space-final-01.log](build-thin-math-space-final-01.log) | exit1, 경고0·오류2. `currentSelection` 지역변수 중복 선언 및 미할당 사용. |
| Debug build02 | [build-thin-math-space-final-02.log](build-thin-math-space-final-02.log) | exit1, 경고2·오류10. UI API/형식 불일치와 QA 메서드의 directory 범위/await 오류. |
| Debug build03 | [build-thin-math-space-final-03.log](build-thin-math-space-final-03.log) | exit1, 경고0·오류1. `ReadOnlySpan<byte>`가 await 경계를 넘는 CS4007. |
| Debug build04 | [build-thin-math-space-final-04.log](build-thin-math-space-final-04.log) | exit0, 경고0·오류0. |
| 신규 논리9+UI1, `new10-01` | [thin-math-space-new10-final-01.log](thin-math-space-new10-final-01.log) | exit1, 9/10. 논리9 PASS, UI 실패. 자연 viewport 폭 73px에 공통 helper의 고정 120px 최소폭이 적용된 검사 문제였고 제품 결함으로 판정하지 않았다. 실패 PNG/state JSON은 보존했다. |
| Debug build05 | [build-thin-math-space-final-05.log](build-thin-math-space-final-05.log) | exit0, 경고0·오류0. |
| UI 단독01 | [thin-math-space-ui-final-01.log](thin-math-space-ui-final-01.log) | exit1, 0/1. 합성 Ctrl+클릭 뒤 spacer 대신 기존 caret TextNode가 선택된 실패. |
| Debug build06 | [build-thin-math-space-final-06.log](build-thin-math-space-final-06.log) | exit0, 경고0·오류0. |
| UI 단독02 | [thin-math-space-ui-final-02.log](thin-math-space-ui-final-02.log) | exit1, 0/1. pointer 진단에서 모델 hit target은 spacer였지만 실제 window event source는 `ScrollContentPresenter/PART_ContentPresenter`, editor hit test는 null, event 미처리 및 editor focus 없음이 확인됐다. [pointer JSON](thin-space-control-click-diagnostic-20261022T224747964Z-9bf1bf59a2414978912d7575f8e78489/pointer-diagnostic.json), [PNG](thin-space-control-click-diagnostic-20261022T224747964Z-9bf1bf59a2414978912d7575f8e78489/main-window.png)은 메인이 직접 검토했다. |
| Debug build07 | [build-thin-math-space-final-07.log](build-thin-math-space-final-07.log) | exit0, 경고0·오류0. editor 전체 bounds 투명 fill과 실제 pointer/event 회귀 계측이 포함된 동결 소스. |
| UI 단독03 | [thin-math-space-ui-final-03.log](thin-math-space-ui-final-03.log) | exit0, 1/1. UI03의 직접 시각 검토 후 입력 target·event·선택 경계 통과. |
| 전체 회귀, `full191-01` | [thin-math-space-full191-final-01.log](thin-math-space-full191-final-01.log) | 같은 build07로 전체191/191·exit0. 신규 논리9+UI1도 전체 run 안에서 재확인 PASS. |

## 직접 검토와 자동 산출물 구분

메인은 UI03의 2026-10-22 22:50 UTC 세트에서 shell PNG 9개(최소/기본 하단, 비활성, 트리 선택, Ctrl overlay, 필수 슬롯 하단, 최소/기본 preview, incomplete)를 직접 검토했다. bounds 12개에서는 핵심 필드를 읽었으며 전 필드를 모두 읽었다고 주장하지 않는다. 같은 run의 pointer diagnostic JSON 1개, inserted layout JSON 1개, preview JSON 2개도 직접 확인했다. 이 자료에는 [pointer 진단](thin-space-control-click-diagnostic-20261022T225038366Z-1b4014ad8bad402297aefeada4061fbe/pointer-diagnostic.json), [layout](thin-space-inserted-layout-20261022T225038192Z-7fc91002ba134c09a5cffeeb3548dffc/thin-space-layout.json), [최소 preview](thin-space-preview-minimum-20261022T225038903Z-8b4a191fee5240bb8651745babb63c9d/thin-space-preview.json), [incomplete preview](thin-space-incomplete-minimum-20261022T225039761Z-82fa67aaac8040558327f670c60ea62f/thin-space-preview.json)가 포함된다. 나머지 shell PNG 3개와 별도 state 전체는 자동 산출물이며 직접 시각 검토하지 않았다.

full191-01은 2026-10-22 22:55:39–40 UTC 표식의 별도 자동 산출물을 생성했다. 이 run에는 PNG 24개와 JSON 27개가 있으며, 해당 디렉터리들은 `p2-thin-math-space/` 아래 `20261022T2255` 이름 표식으로 보존되어 있다. 이는 UI03의 직접 검토 캡처 세트와 다르며 메인이 전부 직접 검토한 것으로 기록하지 않는다.

이전 실패 증거도 보존했다. new10-01의 자연폭 검사 실패 PNG/state JSON 한 쌍, UI 단독01의 제한된 tree/layout 확인, UI 단독02의 pointer 실패 PNG/JSON은 후속 승인 자료와 혼합하지 않는다. 2026-10-22 22:26·22:41 감사 및 회귀 실행 직전 22:54 UTC 조기 감사는 절차 기록이며 테스트 결과나 승인 근거가 아니다.

## 승인 범위와 한계

승인은 `MathSpaceKind.Thin` 하나에 대한 논리·Headless·offscreen 하위 단위다. 일반 행/그룹 간격 정책, 다른 종류의 spacer, 실제 OS 창·포커스·IME·물리 입력·clipboard·UIA·사용자 AppData·computer-use, 외부 TeX 실행은 이 검증에 포함되지 않는다. schema v1에 새 `mathSpace` discriminator가 추가되므로 이를 모르는 구버전 앱의 읽기 호환성을 보장하지 않는다. 파일 읽기 때 자동 anchor 정규화도 하지 않는다. spacer와 직접 인접한 Sequence pair에서는 기존 자동 gap을 0으로 대체한다. TextNode 분할 후 reshaping 폭과 외부 TeX 폭의 일치는 보장하지 않는다. 전체 P2/P1/P0 완료로 확대하지 않는다.
