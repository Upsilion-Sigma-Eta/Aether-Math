# P2 Properties 하위 단위 QA 계획 및 결과

- 상태: Debug 빌드05는 경고0·오류0, 논리8+`PROPERTIES-UI` 1건은 9/9·exit0, 기준선129를 포함한 전체138 회귀는 138/138·exit0으로 통과했다. 메인은 신규9-04 실행의 PNG/bounds 8쌍을 직접 검토해 기본 Properties(세 필드/네 scope) 단위를 논리·Headless·offscreen 범위에서 통합 승인했다. 전체138 실행의 별도 PNG/bounds 8쌍은 자동 산출물로 분리 보존한다. 이전 실패와 수정 이력은 아래 표 및 manifest에 보존한다.
- 기준선: 구조 탐색/Clear 통합 회귀 129건.
- 이번 범위: 크기(논리 DIP), 글꼴, 색의 세 필드와 `SelectionNode`, `CurrentRow`, `CurrentGroup`, `WorkspaceDefault` 네 scope만 다룬다. Typesetting 및 Display/Inline은 후속 단위다.
- 격리: 논리 검사는 실제 public API와 in-memory workspace를 사용한다. UI는 별도 실제 `App`/`MainWindow`를 띄우는 Avalonia Headless + Skia 자식에서만 검사하며 표시 전후 `STUB`/0 handle을 확인한다. 파일 테스트는 `TempDirectory` 안에서만 수행한다. 네이티브 OS 창·포커스·UIA·실제 키/마우스 입력·사용자 AppData는 사용하지 않는다.
- 회귀 범위: 논리 8건과 `PROPERTIES-UI` 1건, 기존 129건을 포함한 전체 138건이다. 신규9 및 전체138이 모두 통과했다.

## 승인된 계약

세 nullable 필드의 effective value는 Workspace→Group→Row→바깥에서 안쪽 node override 순으로 결정하고 각 값의 출처도 노출한다. 명시된 자식 override는 부모 scope 적용·초기화 후에도 유지한다. 새 크기 override는 유한한 1–512 논리 px만 허용하며, 기존 schema v1 작업 기본 크기의 기존 유효 입력 경계는 좁히지 않는다. 글꼴은 공백이 아닌 값, 색은 기존 색 검증 규칙을 따른다. 부분 텍스트 선택은 선택 범위의 glyph들만이 아니라 포함된 전체 `TextNode`에 적용된다고 명시한다.

Apply snapshot에는 Session, revision, target/scope 식별자, caret·선택 상태를 고정한다. 하나라도 달라지면 stale로 거절하고 모델·revision·history를 바꾸지 않는다. null patch 값은 변경 안 함이며, 같은 저장된 명시값은 no-op이다. 상위 effective 값과 같아도 새 명시 override를 만드는 변경은 허용한다. 성공한 Apply는 한 commit으로 ID·caret·selection을 보존하고, Reset은 해당 node/row/group scope의 세 override를 한 번에 제거하되 자식 및 상위 override를 보존한다. `WorkspaceDefault` Reset은 지원하지 않는다. all-null override는 저장 시 null로 정규화한다. Clone은 override 값을 복사하면서 node/group/row ID를 새로 만든다. 기존 Clear가 구조 node를 없애면 그 node override만 제거한다.

schema v1을 유지하며 새 optional override 필드 누락은 legacy 기본값으로 읽는다. 알 수 없는 필드와 무효 enum·override 값은 거절한다. foreground 원문은 `#RRGGBB`/`#AARRGGBB` 그대로 저장하고, SVG 출력에서만 RGB와 opacity로 변환한다. LaTeX는 현재 구조 출력 경로를 유지하며 크기·글꼴·색 미표현을 field별 진단과 명시 안내로 드러낸다. 진단은 보편 글꼴 지원이나 속성의 LaTeX 변환 완료를 뜻하지 않는다.

## 논리 검증 사례

1. `PROP-CASCADE`: 필드별 상속 출처, node 조상 순서, 명시적 자식 override 보존.
2. `PROP-TARGETS`: 네 scope의 대상 선택, 전체 구조 선택·부분 TextNode 선택, 범위 밖 node 무변경.
3. `PROP-APPLY-RESET`: all-null patch no-op, explicit-state no-op, 부모 effective와 같은 explicit override, 네 scope의 Apply 및 지원 scope Reset 단일 commit/undo/redo.
4. `PROP-STALE`: 캡처 뒤 Session/revision/대상/caret/부분·구조 선택 변경 각각 거절 및 무변경.
5. `PROP-VALIDATION`: 크기 1–512·유한성, Core의 공백 글꼴 patch 거절과 VM 초안 공백을 미변경(null patch)으로 처리하는 구분, 잘못된 색·scope, unsupported Workspace Reset의 no-op, 빈 patch 및 기존 큰 기본 크기에서 글꼴을 보존하고 색만 변경. 후보 측정 예외는 적용을 차단하고 glyph `Unknown`은 advisory로 표시하며, 잘못된 `1e300`은 측정기로 전달하지 않는다.
6. `PROP-EDIT-CLONE-CLEAR`: 문자 편집·부분선택 감싸기·node override Clear·row/group clone의 값/ID·caret/selection·undo 보존. 빈 Text 슬롯에서 Subscript/Integral/Summation/Limit/Brackets/Cases를 삽입할 때 원래 content ID와 override를 보존하고, 새 wrapper/다른 슬롯은 override 없이 생성되며, 기존 focus 목적지와 단일 undo를 유지한다.
7. `PROP-JSON-RECOVERY`: legacy schema v1 기본값, 신규 override의 파일 저장·재열기와 recovery 왕복, 잘못된/알 수 없는 데이터 거절 및 원본 bytes 보존.
8. `PROP-OUTPUT`: 분수/Script의 상속 축소와 명시 첨자 크기 유지, `#80FF0000` SVG RGB/opacity 의미 동등성, LaTeX field별 미표현 진단.

UI 사례는 실제 앱 바인딩에서 선택 scope와 원본/effective 값·출처를 확인하고, `WorkspaceDefault`의 explicit 표시값이 실제 `Workspace.Style`과 일치하는지 검사한다. 미수정 draft의 `CanApply=false`, pristine scope 변경 뒤 초안 재포착, null/공백 draft field의 변경 없음, draft 입력만으로 Session revision/dirty가 바뀌지 않는 점, Apply/Reset·stale·Undo 상태를 확인한다. 논리 VM 사례는 baseline FontSize 600에서 색만 바꾸기, 공백 글꼴 draft를 null patch로 처리해 기존 글꼴을 보존하기, 후보 글꼴 측정 실패의 Apply 차단, glyph-coverage `Unknown` advisory 구분, 잘못된 `1e300` 입력이 측정기에 전달되지 않는 점을 확인한다. 신규9-04의 UI 캡처/bounds 8쌍과 전체138-01의 별도 자동 캡처/bounds 8쌍을 서로 다른 manifest에 저장했다.

논리 테스트는 `PropertiesCases.cs`의 8개 사례와 `Program.cs`의 전용 `--properties-ui-headless` route를 사용한다. 적용·초기화는 세션의 공개 API로 네 scope를 확인하며, 레이아웃은 명시된 첨자 크기 유지와 상속된 분수/Script 축소를 분리해 검사한다. 빈 patch의 no-op, 상속값과 같은 신규 explicit override의 단일 commit, 같은 explicit 값의 no-op을 구분한다. UI는 미수정 draft의 `CanApply=false`와 pristine scope 변경 뒤 effective draft 재포착을 확인하고, 편집 중인 draft는 다른 target으로 바뀌어도 문자열을 보존하며 stale 차단되는지 검사한다. SVG 색은 XML 구조를 읽어 RGB와 opacity를 확인하고, LaTeX는 크기·글꼴·색의 필드별 미표현 진단을 확인한다. 최종 Debug 빌드, 신규9, 전체138이 모두 통과했다. 메인은 신규9-04의 8쌍을 직접 검토했으며, 전체138-01의 8쌍은 별도 자동 산출물이다.

## 미검증 범위

이번 단위는 Typesetting 및 Display/Inline, 전체 P2, P0/P1 전체 완료를 다루지 않는다. 네이티브 OS 입력, 실제 사용자 화면·AppData, 다른 플랫폼의 글꼴 fallback, 모든 LaTeX 표현 가능성 및 전체 앱 배포도 검증하지 않는다. 이번 기본 Properties 단위는 메인 시각 검토 뒤 통합 승인됐고, UI는 전용 Headless STUB/0 child에서만 검사했다.

## 정적 대조 및 감사 이력

정적 대조에서 제품의 `MathStylePatch`/target snapshot API, UI `MathPropertiesPanelViewModel` 초안·측정 경계, Program child route와 8+1 case 등록을 확인했다. 논리 검증에는 all-null patch no-op, null/blank draft 필드 보존과 Core 직접 공백 글꼴 patch의 거절 구분, 상위 effective와 같은 새 explicit 값의 commit, 동일 explicit 값 no-op, 기존 baseline 크기 600에서 글꼴을 보존하고 색만 변경, 후보 측정 예외 차단, glyph coverage `Unknown` advisory, `1e300` 값을 측정기로 보내지 않는 경계가 포함된다. 또한 여섯 템플릿의 빈 content ID·override·focus 목적지 보존 및 단일 undo를 검사한다. UI 검증에는 네 scope의 pristine draft/effective/explicit 값 재포착(WorkspaceDefault는 실제 root style 표시), 변경 draft의 stale 보존, 빈 초안 비활성화 및 invalid size가 포함된다. `Program.cs`는 `--properties-ui-headless` child route와 8+1 case를 등록한다. `Qa.cs` fingerprint에는 node/row/group `StyleOverride`가 포함된다. QA는 `Qa.cs`의 공용 fingerprint를 확장했고, `PropertiesUiInteractionCases.cs`에 WorkspaceDefault explicit 세 값 검사를 추가한 뒤 UI 소유자인 font에게 해당 파일을 인계했다.

## 실제 실행 결과와 산출물

| 단계 | 로그 | 결과 |
|---|---|---|
| 초기 빌드 | `artifacts/p2-properties/build-properties-final-01.log` | exit1, 경고0·오류4. QA의 `SessionState.Workspace` 참조와 void `Undo/Redo` 조건 사용, UI helper 지역변수 충돌. 테스트 단계 전에 중단. |
| 수정 후 빌드 및 신규9 | `build-properties-final-02.log`, `properties-new9-final-01.log` | 빌드 exit0, 경고0·오류0. 논리8 통과, UI helper가 `PropertiesUnitsNotice`를 찾지 못해 신규9 8/9·exit1. PNG/bounds는 생성되지 않음. |
| 두 번째 UI 시도 | `build-properties-final-03.log`, `properties-new9-final-02.log` | 빌드 exit0, 경고0·오류0. 논리8 통과, `FontSizeSourceLabel` viewport containment 실패로 신규9 8/9·exit1. PNG/bounds는 생성되지 않음. |
| 기존 성공 기준선 | `build-properties-final-04.log`, `properties-new9-final-03.log` | 빌드 exit0, 경고0·오류0. 신규9 9/9·exit0. 이후 메인 리뷰가 stale-bottom 가시성 증거를 추가 요청해 이 실행은 과거 결과로 보존했다. |
| 최신 동결 소스 | `build-properties-final-05.log`, `properties-new9-final-04.log`, `properties-full138-final-01.log` | 빌드 exit0, 경고0·오류0. 신규9 9/9·exit0, 전체138 138/138·exit0. |

신규9-04의 8쌍 PNG/bounds는 `artifacts/p2-properties/properties-new9-final-04-artifacts.md`에 연결했다. 메인은 이 세트를 직접 검토했고, stale-bottom 이유 bounds가 viewport 안에 완전히 표시되며 Apply disabled/ResetDraft enabled 상태임을 확인했다. 전체138-01의 별도 8쌍 자동 생성물은 `artifacts/p2-properties/properties-full138-final-01-artifacts.md`에 연결했으며 전체 PNG를 직접 검토했다는 의미는 아니다. build01의 QA 컴파일 오류4, build02/new9-01의 `PropertiesUnitsNotice` 이름 실패, build03/new9-02의 `FontSizeSourceLabel` viewport containment 실패 이력과 수정은 각각의 원로그로 보존했다. 17:54 UTC 감사는 전원 정지로 수행됐고 중단급 문제는 보고되지 않았다. 이는 절차 기록이지 테스트 합격 근거가 아니다.
