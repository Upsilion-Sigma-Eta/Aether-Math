# LaTeX 구문 강조 실행 manifest

기준선은 205건이다. 별도 신규 실행은 논리4+`LATEX-HIGHLIGHT-UI`1로 5/5·exit0이며, 전체 회귀는 210/210·exit0으로 메인 통합 승인됐다. 전체210 실행에서 신규5도 모두 통과했다. 자동 캡처와 메인이 직접 검토한 신규 실행 캡처를 분리해서 기록한다.

## 실행 결과

| 단계 | 로그 | 실제 결과 |
|---|---|---|
| Debug build01 | [build-latex-highlight-final-01.log](build-latex-highlight-final-01.log) | 경고2·오류2·exit1; UI helper의 누락 타입 선언과 nullable 이슈. |
| Debug build02 | [build-latex-highlight-final-02.log](build-latex-highlight-final-02.log) | 경고0·오류0·exit0. |
| 신규5-01 | [latex-highlight-new5-final-01.log](latex-highlight-new5-final-01.log) | 4/5·exit1; UI 선택 표면 descendants 기대 실패, PNG/bounds 없음. |
| Debug build03 | [build-latex-highlight-final-03.log](build-latex-highlight-final-03.log) | 경고0·오류0·exit0. |
| 신규5-02 | [latex-highlight-new5-final-02.log](latex-highlight-new5-final-02.log) | 4/5·exit1; 강조 off 전환 뒤 scrollY 기대 1322, 관측 1250. PNG/bounds 실패 자료 보존. |
| Debug build04 | [build-latex-highlight-final-04.log](build-latex-highlight-final-04.log) | 경고0·오류0·exit0. |
| 신규5-03 | [latex-highlight-new5-final-03.log](latex-highlight-new5-final-03.log) | 5/5·exit0이나 메인 시각 리뷰 거절. 강조 on의 일반 텍스트와 off의 전체 코드가 표시되지 않는 화면 결함을 확인. 자료는 자동 통과와 시각 거절을 함께 보존한다. |
| Debug build05 | [build-latex-highlight-final-05.log](build-latex-highlight-final-05.log) | 경고0·오류0·exit0. |
| 신규5-04 | [latex-highlight-new5-final-04.log](latex-highlight-new5-final-04.log) | 5/5·exit0; 신규 PNG8/bounds8 모두 메인 검토 후 시각 승인. |
| 전체210-01 | [latex-highlight-full210-final-01.log](latex-highlight-full210-final-01.log) | 210/210·exit0, build05 산출물 사용. 전체 회귀 및 구문 강조 하위 단위 승인. |

## 신규5-04 직접 검토 세트

각 폴더에 `main-window.png`와 `bounds.json`이 있다. 메인은 이 8개 PNG 모두와 같은 실행 bounds8의 주요 필드를 확인했다.

| 화면 | 산출물 폴더 |
|---|---|
| 접힘 최소 | `highlight-collapsed-minimum-1060x700-20261023T001903406Z-a46fdf6f089d423b9d34daf9fbccd64b` |
| 접힘 기본 | `highlight-collapsed-default-1360x880-20261023T001903460Z-cce39425759547da8931e5b00b8a5f6f` |
| 펼침 최소 상단 | `highlight-expanded-minimum-top-1060x700-20261023T001903847Z-340c69ae25d64e909d0403185887f0e8` |
| 강조 off, combining, 최소 하단 | `highlight-off-combining-minimum-bottom-1060x700-20261023T001904410Z-a6f59b7d0ec64ed3bc3a631693c9d415` |
| 강조 on, 역방향 선택, 최소 하단 | `highlight-on-reverse-selection-minimum-bottom-1060x700-20261023T001904640Z-bb95cc4035d44156a3fd6154fdb16a91` |
| 강조 on, 기본 하단 | `highlight-on-default-bottom-1360x880-20261023T001904784Z-3cc0ad1ede354946bc9841b1849a06dc` |
| source 변경 후 선택 초기화 | `highlight-source-changed-reset-minimum-1060x700-20261023T001905800Z-a1522bee8a384fe1844eba513ae529e7` |
| 부분 선택 fake copy | `highlight-copy-partial-selection-minimum-1060x700-20261023T001905993Z-8de35818656f4651a70a3480e0929b4e` |

검토에서 STUB/0, 전체 토큰 coverage, source 110, 접힘 viewport 257/437, 펼침 최소 viewport 114, copy 상태의 grid 137·diagnostic 27·editor 87을 확인했다. 일반 문자·한글·emoji는 강조 on/off 모두 표시됐다. off/reverse selection의 scrollY는 1322, 선택은 각각 4915–4917 및 4917–4915였다. 같은 source의 toggle은 선택/스크롤을 보존하고 실제 source 변경은 선택0·스크롤 시작 위치로 초기화됐다. 표의 수치는 bounds 주요 필드 검토 내용이며 모든 bounds 필드를 전부 읽었다는 뜻은 아니다.

## 전체210-01 별도 자동 산출물

아래 8개 폴더도 각각 `main-window.png`와 `bounds.json`을 포함하지만 자동 생성 자료이며 메인이 전부 직접 검토하지 않았다.

| 화면 | 산출물 폴더 |
|---|---|
| 접힘 최소 | `highlight-collapsed-minimum-1060x700-20261023T002126497Z-309f711a51a84859ba569f817f390836` |
| 접힘 기본 | `highlight-collapsed-default-1360x880-20261023T002126551Z-57830bbed090401c8b88d84b1f830b91` |
| 펼침 최소 상단 | `highlight-expanded-minimum-top-1060x700-20261023T002126944Z-a7a3538077894d83ae5ec58c21730d76` |
| 강조 off, combining, 최소 하단 | `highlight-off-combining-minimum-bottom-1060x700-20261023T002127901Z-8db0b6f3ddb54e3f8f6e0dfd5c61dabf` |
| 강조 on, 역방향 선택, 최소 하단 | `highlight-on-reverse-selection-minimum-bottom-1060x700-20261023T002128797Z-01c97162858443169b67539c23742ded` |
| 강조 on, 기본 하단 | `highlight-on-default-bottom-1360x880-20261023T002129350Z-86b13c596fc840fda113834064c23f1a` |
| source 변경 후 선택 초기화 | `highlight-source-changed-reset-minimum-1060x700-20261023T002129984Z-2b3cdc9d023d4041a597e5486b3888ba` |
| 부분 선택 fake copy | `highlight-copy-partial-selection-minimum-1060x700-20261023T002130148Z-4e1317c8bc8943a5a1cef21f4c26e83a` |

## 한계 및 절차 기록

토큰화는 표시용 lexical 분류다. parser, 지원 문법 검증, 외부 TeX 컴파일을 하지 않았으며 OS clipboard/native 입력/포커스/UIA/사용자 AppData/computer-use도 미검증이다. fake writer와 Headless/Skia STUB/0만 사용했다. 승인 범위는 구문 강조 하위 단위이며 P0/P1/P2 전체 완료를 뜻하지 않는다.

2026-10-22 23:53 UTC 및 2026-10-23 00:08 UTC 감사는 절차 기록이고 테스트 결과 근거가 아니다.
