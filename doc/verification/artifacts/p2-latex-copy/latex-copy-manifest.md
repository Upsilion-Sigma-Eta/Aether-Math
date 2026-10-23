# 전체 LaTeX 코드 복사 실행 manifest

현재 상태: 기준선198 + 논리6 + `LATEX-COPY-UI`1 = 전체205건이다. build03, 신규7-02, 전체205-01은 각각 0/0·exit0, 7/7·exit0, 205/205·exit0으로 통합 승인됐다. 이전 build01 및 new7-01 실패 이력도 보존한다. 메인의 직접 시각 검토는 신규7-02 PNG9와 bounds11의 핵심 필드이며, 전체205-01의 별도 PNG/bounds11쌍은 자동 산출물이다. 2026-10-22 23:24 및 23:39 UTC 감사는 절차 기록이며 검증 근거가 아니다.

| 단계 | 로그 | 실제 결과 |
|---|---|---|
| Debug 통합 빌드01 | `build-latex-copy-final-01.log` | 경고0·오류2·실제 exit1. UI 코드의 369·381행에서 누락된 `Contains` helper(CS0103). |
| Debug 통합 빌드02 | `build-latex-copy-final-02.log` | 경고0·오류0·실제 exit0. |
| 신규 논리6 + `LATEX-COPY-UI`, 실행01 | `latex-copy-new7-final-01.log` | 논리6 PASS, UI1 FAIL; 6/7·실제 exit1. 첫 collapsed 캡처의 선택적 LaTeX 코드 영역 window-relative 좌표 획득 실패. PNG/bounds 0개, 당시 캡처 디렉터리 비어 있음. |
| Debug 통합 빌드03 | `build-latex-copy-final-03.log` | 경고0·오류0·실제 exit0. |
| 신규 논리6 + `LATEX-COPY-UI`, 실행02 | `latex-copy-new7-final-02.log` | 7/7·실제 exit0. 23:43:42–43 UTC 캡처 11개 중 PNG9와 bounds11 핵심 필드를 메인이 직접 검토했다. 두 expanded PNG는 직접 검토하지 않음. |
| 기준선 포함 전체 회귀 | `latex-copy-full205-final-01.log` | 205/205·실제 exit0. 23:46:06–07 UTC의 별도 PNG/bounds 11쌍은 자동 산출물이며 직접 시각 검토 세트가 아니다. |

new7-01 실패 캡처 디렉터리 `latex-copy-initial-collapsed-minimum-1060x700-20261022T233823369Z-39b4db40d1ae402a9fd333578136abfe/`는 존재하지만 비어 있고 PNG/bounds JSON이 없다. helper의 collapsed/detached 선택 영역과 expanded strict geometry를 분리한 뒤 new7-02가 통과했다. 신규7-02의 직접 검토 geometry 요약은 STUB/0, 접힘 viewport 최소/기본 257/437px, source90px·진단58px·grid148px, 펼침 최소 editor76px이다. 직접 검토하지 않은 initial-expanded/retry PNG2와 전체205-01 별도 자동 산출물은 승인 근거로 과장하지 않는다. 전체 LaTeX 복사 하위 단위는 승인됐지만 실제 OS clipboard/입력/AppData와 외부 TeX는 미검증이다. 23:39 UTC 감사는 절차 기록일 뿐 테스트 성공 근거가 아니다.

case ID는 `LATEX-COPY-SUCCESS`, `LATEX-COPY-GATES`, `LATEX-COPY-BUSY`, `LATEX-COPY-SNAPSHOT`, `LATEX-COPY-FAIL-CANCEL`, `LATEX-COPY-NOTIFY-ORDER`, `LATEX-COPY-UI`다. Program에는 UI Headless 초기화 분기, `--latex-copy-ui-headless` dispatch, `.Concat(LatexCopyCases.All())` 및 `.Concat(LatexCopyUiInteractionCases.All())` 연결이 들어 있다. 향후 로그와 화면 산출물은 실제 실행 결과가 생성된 뒤 이 manifest에 정확한 경로·종료 코드·리뷰 범위를 추가한다.

UI fake writer는 테스트에서 직접 주입하며 OS clipboard는 호출하지 않는다. 실제 OS/포커스/물리 입력/UIA/AppData/computer-use 및 외부 TeX 검증은 범위 밖이다. 코드 syntax highlighting/parser, 다른 scope의 복사 및 형식×출력 전체 matrix도 이 하위 단위 승인에 포함되지 않는다.
