# P2 텍스트 역할 최종 실행 manifest

결과: 논리8+`TEXT-ROLE-UI`1과 기존 회귀를 포함한 전체165가 통과했고 메인이 §3.3-03 하위 단위를 논리·Headless·offscreen 범위에서 승인했다. 빌드/실행 로그는 `ACTUAL_EXIT_CODE=0`을 보존한다. 원본 로그와 캡처는 수정하지 않았다.

## 단계별 결과

| 단계 | 결과 | 로그 |
|---|---|---|
| build01 | 경고1·오류0·exit0. nullable `template.Value` 접근. | [build01](build-text-role-final-01.log) |
| build02 | 경고0·오류0·exit0. QA가 `HasValue`를 유지하며 non-null `Guid` 지역 변수를 사용. | [build02](build-text-role-final-02.log) |
| 신규9-01 | 논리8 통과·UI1 실패, 8/9·exit1. strict visibility assertion이 `ApplyPropertiesButton` 클릭 직전에 실패. | [new9-01](text-role-new9-final-01.log) |
| build03 | 경고0·오류1·exit1. UI helper 타입 불일치 CS1503. 다음 신규 실행은 보류. | [build03](build-text-role-final-03.log) |
| build04 | 경고0·오류0·exit0. | [build04](build-text-role-final-04.log) |
| 신규9-02 | 9/9·exit0. dedicated expanded 진단 창 캡처가 백색 PNG여서 expanded 시각 승인은 보류. | [new9-02](text-role-new9-final-02.log) |
| build05 | 경고0·오류0·exit0. 캡처만 수정한 소스를 빌드. | [build05](build-text-role-final-05.log) |
| 신규9-03 | 9/9·exit0. 아래의 직접 검토 항목과 비교 JSON hash가 확인됨. | [new9-03](text-role-new9-final-03.log) |
| 전체165-01 | 165/165·exit0. 이 run 캡처는 자동 생성본으로 별도 보존. | [full165-01](text-role-full165-final-01.log) |

## 신규9-01 실패 산출물

첫 UI 실행이 남긴 8개 shell 캡처는 당시 실패 진단용이다. 메인은 그 가운데 최소 창 initial-bottom 1개만 확인했다. 다음 표는 산출물 보존 위치이며 전체 시각 리뷰나 승인 이미지로 취급하지 않는다.

| 화면 | PNG | bounds |
|---|---|---|
| 기본 initial-bottom | [PNG](properties-initial-default-bottom-1360x880-20261022T195158803Z-d62f0b89a07d4c5ebab692ce24114ea0/main-window.png) | [JSON](properties-initial-default-bottom-1360x880-20261022T195158803Z-d62f0b89a07d4c5ebab692ce24114ea0/bounds.json) |
| 기본 initial-top | [PNG](properties-initial-default-top-1360x880-20261022T195158760Z-77babb48f92d4d2e916610fe12412faa/main-window.png) | [JSON](properties-initial-default-top-1360x880-20261022T195158760Z-77babb48f92d4d2e916610fe12412faa/bounds.json) |
| 최소 initial-bottom | [PNG](properties-initial-minimum-bottom-1060x700-20261022T195158731Z-4b5675fa85ec4765b296c4e4224f6a1d/main-window.png) | [JSON](properties-initial-minimum-bottom-1060x700-20261022T195158731Z-4b5675fa85ec4765b296c4e4224f6a1d/bounds.json) |
| 최소 initial-top | [PNG](properties-initial-minimum-top-1060x700-20261022T195158654Z-d030e2c58206454aa19abe488f52e572/main-window.png) | [JSON](properties-initial-minimum-top-1060x700-20261022T195158654Z-d030e2c58206454aa19abe488f52e572/bounds.json) |
| stale 최소 | [PNG](properties-stale-minimum-bottom-1060x700-20261022T195158964Z-d025fd4a12e443dfbc7c1227e85940d7/main-window.png) | [JSON](properties-stale-minimum-bottom-1060x700-20261022T195158964Z-d025fd4a12e443dfbc7c1227e85940d7/bounds.json) |
| Number | [PNG](text-role-supported-number-minimum-1060x700-20261022T195159243Z-c7b46115969742118ae119c8cd03b621/main-window.png) | [JSON](text-role-supported-number-minimum-1060x700-20261022T195159243Z-c7b46115969742118ae119c8cd03b621/bounds.json) |
| 미지원 row | [PNG](text-role-unsupported-row-minimum-1060x700-20261022T195158866Z-35aba4e6823042efb33e8d509e989fdd/main-window.png) | [JSON](text-role-unsupported-row-minimum-1060x700-20261022T195158866Z-35aba4e6823042efb33e8d509e989fdd/bounds.json) |
| 미지원 structure | [PNG](text-role-unsupported-structure-minimum-1060x700-20261022T195158911Z-9d4e07b1e55540a69504d535aed5f628/main-window.png) | [JSON](text-role-unsupported-structure-minimum-1060x700-20261022T195158911Z-9d4e07b1e55540a69504d535aed5f628/bounds.json) |

비교 Automatic 그림은 shell set과 분리돼 있다: [equation-Automatic.png](text-role-comparison-20261022T195159232Z-f73d9e56dd3b4787bed4bb8e564912b9/equation-Automatic.png).

## 신규9-02 직접 검토 set

메인은 2026-10-22 20:14 UTC 실행에서 shell PNG9/bounds9 및 비교 PNG4/JSON을 직접 검토했다. dedicated expanded 진단 PNG는 하얗게 캡처되어 별도 시각 검토는 실패/보류로 기록한다. 이 white PNG는 이력 보존용이며 승인 증거가 아니다.

| 화면 | PNG | bounds |
|---|---|---|
| 기본 initial-bottom | [PNG](properties-initial-default-bottom-1360x880-20261022T201403362Z-25a46b8ed10b4e8fa56e346b82b0ceac/main-window.png) | [JSON](properties-initial-default-bottom-1360x880-20261022T201403362Z-25a46b8ed10b4e8fa56e346b82b0ceac/bounds.json) |
| 기본 initial-top | [PNG](properties-initial-default-top-1360x880-20261022T201403323Z-fd01f1f20a9c4d69b4405a80c36f3e77/main-window.png) | [JSON](properties-initial-default-top-1360x880-20261022T201403323Z-fd01f1f20a9c4d69b4405a80c36f3e77/bounds.json) |
| 최소 initial-bottom | [PNG](properties-initial-minimum-bottom-1060x700-20261022T201403295Z-8ce9237a3c024aefa94247c74fdbb6bf/main-window.png) | [JSON](properties-initial-minimum-bottom-1060x700-20261022T201403295Z-8ce9237a3c024aefa94247c74fdbb6bf/bounds.json) |
| 최소 initial-top | [PNG](properties-initial-minimum-top-1060x700-20261022T201403234Z-e4d5795638ad4d79b95e35f109cda2a7/main-window.png) | [JSON](properties-initial-minimum-top-1060x700-20261022T201403234Z-e4d5795638ad4d79b95e35f109cda2a7/bounds.json) |
| stale 최소 | [PNG](properties-stale-minimum-bottom-1060x700-20261022T201403511Z-0e92aa9d4f3f4e6e870582847152f375/main-window.png) | [JSON](properties-stale-minimum-bottom-1060x700-20261022T201403511Z-0e92aa9d4f3f4e6e870582847152f375/bounds.json) |
| Number | [PNG](text-role-supported-number-minimum-1060x700-20261022T201403748Z-4464f65d335849b2b736606158445d50/main-window.png) | [JSON](text-role-supported-number-minimum-1060x700-20261022T201403748Z-4464f65d335849b2b736606158445d50/bounds.json) |
| 미지원 row | [PNG](text-role-unsupported-row-minimum-1060x700-20261022T201403421Z-dded9db3ddcf4a5d8847c7f0dd857145/main-window.png) | [JSON](text-role-unsupported-row-minimum-1060x700-20261022T201403421Z-dded9db3ddcf4a5d8847c7f0dd857145/bounds.json) |
| 미지원 structure | [PNG](text-role-unsupported-structure-minimum-1060x700-20261022T201403465Z-bb2d2b38d5b84171b04a3269fd4aedb2/main-window.png) | [JSON](text-role-unsupported-structure-minimum-1060x700-20261022T201403465Z-bb2d2b38d5b84171b04a3269fd4aedb2/bounds.json) |
| 접힌 diagnostic | [PNG](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201404933Z-b60eff874aa5489eb66512a0c88de34f/main-window.png) | [JSON](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201404933Z-b60eff874aa5489eb66512a0c88de34f/bounds.json) |
| expanded diagnostic 실패 캡처 | [흰 PNG](latex-diagnostic-20261022T201404901Z-dca22a9a254a4c9ca36613b7df45fe27/latex-diagnostic-shell.png) | [JSON](latex-diagnostic-20261022T201404901Z-dca22a9a254a4c9ca36613b7df45fe27/latex-diagnostic-shell.json) |

비교 이미지와 hash 요약: [Automatic](text-role-comparison-20261022T201403735Z-d581b5e7414a44d9906c612eda7bfc98/equation-Automatic.png), [Automatic after Undo](text-role-comparison-20261022T201403735Z-d581b5e7414a44d9906c612eda7bfc98/equation-AutomaticAfterUndo.png), [Identifier](text-role-comparison-20261022T201403735Z-d581b5e7414a44d9906c612eda7bfc98/equation-Identifier.png), [Number](text-role-comparison-20261022T201403735Z-d581b5e7414a44d9906c612eda7bfc98/equation-Number.png), [JSON](text-role-comparison-20261022T201403735Z-d581b5e7414a44d9906c612eda7bfc98/text-role-comparison.json).

## 신규9-03 직접 검토 및 최종 산출물

최종 신규9 run의 main-screen shell 9개와 bounds9는 아래에 보존했다. 메인은 2026-10-22 20:16:24 UTC에 expanded diagnostic PNG/JSON을 확인해 source/banner 노출, STUB/0, source 90px, grid 143px, 접힌 257px 대비 expanded 편집 viewport 81px(감소176px), 재접힘 후 257px 복귀를 검증했다. 같은 run에서 stale, Number-draft, diagnostic-collapsed 3개 UI PNG를 직접 검토했고 bounds9 전체를 확인했다. shell table의 나머지 이미지는 실행 산출물이며 각각 직접 리뷰했다고 주장하지 않는다.

| 화면 | PNG | bounds |
|---|---|---|
| 기본 initial-bottom | [PNG](properties-initial-default-bottom-1360x880-20261022T201652486Z-c3c1f21cac3a45e18bccf52903b057f0/main-window.png) | [JSON](properties-initial-default-bottom-1360x880-20261022T201652486Z-c3c1f21cac3a45e18bccf52903b057f0/bounds.json) |
| 기본 initial-top | [PNG](properties-initial-default-top-1360x880-20261022T201652448Z-ff5908caaa13431fa7f518203994c7ed/main-window.png) | [JSON](properties-initial-default-top-1360x880-20261022T201652448Z-ff5908caaa13431fa7f518203994c7ed/bounds.json) |
| 최소 initial-bottom | [PNG](properties-initial-minimum-bottom-1060x700-20261022T201652420Z-8d21e62798e5401bb1e193717f574851/main-window.png) | [JSON](properties-initial-minimum-bottom-1060x700-20261022T201652420Z-8d21e62798e5401bb1e193717f574851/bounds.json) |
| 최소 initial-top | [PNG](properties-initial-minimum-top-1060x700-20261022T201652349Z-6ff4639b5f9c40d5976c4d1e4ca96466/main-window.png) | [JSON](properties-initial-minimum-top-1060x700-20261022T201652349Z-6ff4639b5f9c40d5976c4d1e4ca96466/bounds.json) |
| stale 최소 | [PNG](properties-stale-minimum-bottom-1060x700-20261022T201652636Z-2f51d797ddeb4907b5fc8d6be90cfaf4/main-window.png) | [JSON](properties-stale-minimum-bottom-1060x700-20261022T201652636Z-2f51d797ddeb4907b5fc8d6be90cfaf4/bounds.json) |
| Number | [PNG](text-role-supported-number-minimum-1060x700-20261022T201652868Z-b16cd32bd33848338053196847112852/main-window.png) | [JSON](text-role-supported-number-minimum-1060x700-20261022T201652868Z-b16cd32bd33848338053196847112852/bounds.json) |
| 미지원 row | [PNG](text-role-unsupported-row-minimum-1060x700-20261022T201652548Z-91699c69ab98411d88452638fa17eafd/main-window.png) | [JSON](text-role-unsupported-row-minimum-1060x700-20261022T201652548Z-91699c69ab98411d88452638fa17eafd/bounds.json) |
| 미지원 structure | [PNG](text-role-unsupported-structure-minimum-1060x700-20261022T201652590Z-dfe782bfbaca4362b6dc1d7a4f7518be/main-window.png) | [JSON](text-role-unsupported-structure-minimum-1060x700-20261022T201652590Z-dfe782bfbaca4362b6dc1d7a4f7518be/bounds.json) |
| diagnostic-collapsed | [PNG](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201654059Z-91728e35a08d46ce9706d7678f5c9fe6/main-window.png) | [JSON](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201654059Z-91728e35a08d46ce9706d7678f5c9fe6/bounds.json) |
| expanded diagnostic | [PNG](latex-diagnostic-20261022T201654021Z-bd47d093582848798b6e80fdfb120ee4/latex-diagnostic-shell.png) | [JSON](latex-diagnostic-20261022T201654021Z-bd47d093582848798b6e80fdfb120ee4/latex-diagnostic-shell.json) |

비교 PNG와 JSON의 신규9-02 대비 동일 hash 확인: [Automatic](text-role-comparison-20261022T201652859Z-8fe0af3b8be044eeb0fb577890ed0688/equation-Automatic.png), [Automatic after Undo](text-role-comparison-20261022T201652859Z-8fe0af3b8be044eeb0fb577890ed0688/equation-AutomaticAfterUndo.png), [Identifier](text-role-comparison-20261022T201652859Z-8fe0af3b8be044eeb0fb577890ed0688/equation-Identifier.png), [Number](text-role-comparison-20261022T201652859Z-8fe0af3b8be044eeb0fb577890ed0688/equation-Number.png), [JSON](text-role-comparison-20261022T201652859Z-8fe0af3b8be044eeb0fb577890ed0688/text-role-comparison.json). 네 hash는 신규9-02 set과 동일했다.

## 전체165-01 자동 산출물

전체 회귀 run은 별도 디렉터리에 9개 shell PNG/bounds, 역할 비교 PNG/JSON 및 expanded diagnostic PNG/JSON을 새로 생성했다. 아래는 자동 산출물 위치이며 메인이 전체 세트를 직접 시각 검토한 결과가 아니다.

| 자동 산출물 | PNG | bounds/JSON |
|---|---|---|
| 기본 initial-bottom | [PNG](properties-initial-default-bottom-1360x880-20261022T201839376Z-2cf5bf2a6e9b423897f69df8d7689eb2/main-window.png) | [JSON](properties-initial-default-bottom-1360x880-20261022T201839376Z-2cf5bf2a6e9b423897f69df8d7689eb2/bounds.json) |
| 기본 initial-top | [PNG](properties-initial-default-top-1360x880-20261022T201839339Z-e15a4ad541804888a21d720de86d8bce/main-window.png) | [JSON](properties-initial-default-top-1360x880-20261022T201839339Z-e15a4ad541804888a21d720de86d8bce/bounds.json) |
| 최소 initial-bottom | [PNG](properties-initial-minimum-bottom-1060x700-20261022T201839311Z-744e438629e24296bad111b73e209beb/main-window.png) | [JSON](properties-initial-minimum-bottom-1060x700-20261022T201839311Z-744e438629e24296bad111b73e209beb/bounds.json) |
| 최소 initial-top | [PNG](properties-initial-minimum-top-1060x700-20261022T201839257Z-c5cdfe91ce034eda8b1ae732276b85ac/main-window.png) | [JSON](properties-initial-minimum-top-1060x700-20261022T201839257Z-c5cdfe91ce034eda8b1ae732276b85ac/bounds.json) |
| stale 최소 | [PNG](properties-stale-minimum-bottom-1060x700-20261022T201839524Z-264cd38bfcc84769af969e3b1ecf1be5/main-window.png) | [JSON](properties-stale-minimum-bottom-1060x700-20261022T201839524Z-264cd38bfcc84769af969e3b1ecf1be5/bounds.json) |
| Number | [PNG](text-role-supported-number-minimum-1060x700-20261022T201839749Z-8440066bec9649f39a4c4852a7e69ee2/main-window.png) | [JSON](text-role-supported-number-minimum-1060x700-20261022T201839749Z-8440066bec9649f39a4c4852a7e69ee2/bounds.json) |
| 미지원 row | [PNG](text-role-unsupported-row-minimum-1060x700-20261022T201839435Z-20d8d52a388846d684948c347a67819e/main-window.png) | [JSON](text-role-unsupported-row-minimum-1060x700-20261022T201839435Z-20d8d52a388846d684948c347a67819e/bounds.json) |
| 미지원 structure | [PNG](text-role-unsupported-structure-minimum-1060x700-20261022T201839479Z-7123dc369aed466f9c5af88f2174abab/main-window.png) | [JSON](text-role-unsupported-structure-minimum-1060x700-20261022T201839479Z-7123dc369aed466f9c5af88f2174abab/bounds.json) |
| diagnostic-collapsed | [PNG](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201840930Z-264ecfadde714461bf7710ccce9b2a96/main-window.png) | [JSON](text-role-latex-diagnostic-minimum-collapsed-1060x700-20261022T201840930Z-264ecfadde714461bf7710ccce9b2a96/bounds.json) |
| expanded diagnostic | [PNG](latex-diagnostic-20261022T201840891Z-6e1eacc2caaa4e5aafebfba251c5f469/latex-diagnostic-shell.png) | [JSON](latex-diagnostic-20261022T201840891Z-6e1eacc2caaa4e5aafebfba251c5f469/latex-diagnostic-shell.json) |
| 비교 PNG/JSON | [Automatic](text-role-comparison-20261022T201839742Z-d19b784c875049f181d69127edfa3c78/equation-Automatic.png), [Automatic after Undo](text-role-comparison-20261022T201839742Z-d19b784c875049f181d69127edfa3c78/equation-AutomaticAfterUndo.png), [Identifier](text-role-comparison-20261022T201839742Z-d19b784c875049f181d69127edfa3c78/equation-Identifier.png), [Number](text-role-comparison-20261022T201839742Z-d19b784c875049f181d69127edfa3c78/equation-Number.png) | [JSON](text-role-comparison-20261022T201839742Z-d19b784c875049f181d69127edfa3c78/text-role-comparison.json) |

## 한정 및 근거

- `TEXT-ROLE-ALIGN-SVG`는 결합 grapheme 내부 등호가 정렬 marker가 되지 않고, 다음 실제 단독 등호가 단일 anchor가 되는 계약을 보강했다. Automatic 일반 입력/단독 등호의 레거시 출력은 유지하되 이 결합 입력에서 raw `IndexOf`로 문자를 분리하던 오류를 예외적으로 수정했다. 공개 경로 실행은 styled Automatic root Text/Sequence와 MathSymbol, 구조 내부 원문 보존에 한정하며 구조 내부 `topLevel=false`에서 정렬 anchor를 생성한다고 주장하지 않는다.
- Main 직접 검토 이미지는 신규9-02와 신규9-03로 한정한다. 전체165의 재생성 이미지는 자동 산출물이다.
- 2026-10-22 19:39, 19:54, 20:09 UTC 정기 감사는 절차상 정지/재개였으며 테스트 증거가 아니다.
- 실제 OS 앱/입력/포커스/UIA/IME/clipboard, 사용자 AppData, 외부 TeX 실행/parser, 구버전 앱, LaTeX 역할 provenance 역변환과 전체 P2/P1/P0 완료는 검증하지 않았다.

