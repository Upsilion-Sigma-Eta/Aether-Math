# P2 의미 텍스트 역할 QA

기록 상태: §3.3-03 의미 텍스트 역할 하위 단위는 논리·Headless·offscreen 범위에서 통합 승인됐다. 최종 Debug 빌드05는 경고0·오류0·exit0, 신규 논리8+`TEXT-ROLE-UI` 1건은 9/9·exit0, 기존156을 포함한 전체165는 165/165·exit0이다. 실행별 원로그와 검토 자료는 [최종 manifest](artifacts/p2-text-role/text-role-final-manifest.md)에 있다. 이 기록은 P2 전체나 P1/P0 완료를 뜻하지 않는다.

## 범위와 계약

`MathTextRole`은 `Automatic`, `Identifier`, `MathSymbol`, `Number`, `PlainText`, `Unit`, `Condition`이다. schema v1에서 `TextNode.Role`이 빠지면 `Automatic`을 사용하고 정의되지 않은 enum은 거부한다. 역할은 원문을 해석하거나 교정하지 않는 표시 메타데이터이며 역할 상속은 없다. `Automatic`은 기존 italic heuristic과 LaTeX 경로를 유지하고 `Identifier`는 italic, 그 외 명시 역할은 upright로 표시한다.

`MathPropertiesPatch`의 역할과 네 스타일 필드는 하나의 snapshot 검증과 commit으로 적용한다. `SelectionNode`가 실제 `TextNode`를 대상으로 할 때만 역할을 바꿀 수 있고, 부분 텍스트 선택도 해당 TextNode 전체에 적용한다. 구조 노드와 다른 범위는 지원하지 않으며 잘못된 enum은 무변경 거절이다. 네 스타일 필드 reset은 역할을 보존하고 `Automatic` 적용으로 역할을 초기화한다. 문자 편집·붙여넣기·분할·템플릿 삽입·복제 경로의 역할과 ID 보존도 확인했다.

LaTeX에서는 `Automatic`의 일반 문자와 단독 등호 출력을 유지한다. 다중 행 정렬에서 `=`와 combining overlay 또는 variation selector가 한 grapheme를 이루면 그 grapheme를 anchor나 `&=` marker로 쪼개지 않고 원문을 보존한다. 같은 grapheme 뒤의 다음 Sequence 자식에 단독 `=`가 있으면 그 첫 실제 등호만 정렬점이 된다. 따라서 Automatic의 호환성은 일반 입력과 단독 등호에 한정되며, 과거 raw 문자 검색으로 결합 등호를 쪼개던 출력은 수정된 예외다. `Identifier`는 `\mathit`, `MathSymbol`/`Number`/`Unit`은 `\mathrm`, `PlainText`/`Condition`은 `\text`로 표시한다. 명시 역할의 공백은 문자마다 `\ `로 내보내고 예약 문자는 역할별 safe macro로 처리한다. `\text`의 수식 내 텍스트/크기 동작에 대한 [`amstext.dtx`](https://raw.githubusercontent.com/latex3/latex2e/main/required/amsmath/amstext.dtx)는 serializer 계약 참고이며 TeX 실행 결과가 아니다.

제어 문자는 원래 code point를 바꾸지 않고 `LatexTextDiagnostics`에 노드 ID, 역할, UTF-16 위치, code point와 이유를 기록한다. 빈 필수 슬롯 ID는 별도 진단이다. `CanExportLatex`는 제어 문자 진단 또는 빈 필수 슬롯이 있으면 false가 되지만 `LatexCode` 생성은 유지한다. 이번 범위는 LaTeX parser, 외부 TeX 컴파일, 파일 export/복사 명령을 검증하지 않는다. 역할 매크로는 provenance를 역변환할 수 있는 단사 인코딩이 아니므로 `.aethermath` 역할/ID 보존과 LaTeX 표시를 구분한다.

## 논리 사례

| 사례 | 검증 내용 |
|---|---|
| `TEXT-ROLE-SCHEMA` | schema v1 누락 역할의 `Automatic` 기본값, invalid enum 읽기 거절 및 원본 bytes 보존, 파일 저장/열기와 테스트 `TempDirectory`의 실제 `WorkspaceStateService` recovery Save/Read에서 Role·ID 지문 왕복. |
| `TEXT-ROLE-LAYOUT` | 명시 역할의 upright/Identifier italic, Automatic 기존 run 분할, 비가산 metrics로 명시 문자열 한 shaping run 측정, glyph·caret 폭 일치. |
| `TEXT-ROLE-LATEX` | 역할별 macro, 공백·예약 문자·Unicode 처리, C0/C1 및 CR/LF/TAB 진단의 UTF-16 위치/code point, 빈 필수 슬롯 ID와 원문 보존. |
| `TEXT-ROLE-ALIGN-SVG` | 중첩 수평 Sequence의 Automatic/MathSymbol 등호만 정렬점으로 사용하고 PlainText·분수 내부 등호는 제외. 결합 grapheme 등호의 marker/anchor 보존 및 뒤따르는 단독 등호의 단일 정렬을 확인하고 같은 layout을 SVG에 전달. |
| `TEXT-ROLE-APPLY` | 역할+네 스타일 필드의 원자적 단일 commit, stale/no-op/invalid/unsupported 거절, caret·selection·ID 보존, style reset·undo/redo 및 저장 StateId. |
| `TEXT-ROLE-EDIT-PRESERVE` | 부분 입력 교체, 주입 reader 붙여넣기, 선택 감싸기 및 caret 분할에서 주변 텍스트의 ID·역할·style과 새 wrapper/slot 기본 역할. OS clipboard는 사용하지 않는다. |
| `TEXT-ROLE-TEMPLATE-CLONE` | Subscript/Integral/Summation/Limit/Brackets/Cases 빈 content 역할·style·ID, 새 슬롯의 Automatic 기본값, focus 유효성, 행 복제의 신규 ID·역할 보존과 undo/redo. |
| `TEXT-ROLE-VM-DIAGNOSTIC` | 속성 패널의 역할/구조 범위 안내, 빈 필수 슬롯·제어 문자 진단 구분, `CanExportLatex`와 미리보기 코드 생성 분리. |

`TEXT-ROLE-ALIGN-SVG`의 공개 API 회귀는 styled Automatic root Text/Sequence, 명시 MathSymbol 및 구조 안 원문 보존 경계를 실행한다. 구조 내부 `emitStyle=false` 자식은 `topLevel=false`이므로 그 내부에서 정렬 marker를 내는 경로를 실행했다고 주장하지 않는다. 소스의 helper 호출부는 별도 정적 대조했다.

## Headless UI 및 결과

UI case는 `TEXT-ROLE-UI`, child 인자는 `--text-role-ui-headless TEXT-ROLE-UI`다. `Program.cs`에 Headless/Skia 초기화, child route, 논리/UI 등록이 연결됐다. 모든 테스트 파일 I/O는 `TempDirectory`, UI 증거는 소유 Headless `STUB`/0 child와 offscreen 렌더에 한정했다. 화면 크기별 기본/선택/진단/stale 상태, 역할별 수식 렌더, 코드 패널 접힘·확장 경계를 확인했다.

신규9-03의 기본 상태에서 중앙 편집 viewport는 257px/437px이다. 코드 패널을 연 expanded 상태는 source/grid의 실제 배분을 별도로 확인했으며 source 높이 90px 이상, grid 143px, 편집 viewport 81px, 접힘 대비 감소 176px이었다. 패널 재접힘 뒤 257px로 돌아오는 것도 확인했다. expanded 상태에는 기존 257px 하한을 적용하지 않았으며 새 임의의 최소 높이를 두지 않았다. 전체 run의 산출물은 자동 생성본이어서 신규 run의 직접 시각 검토와 구분한다.

## 빌드 및 실행 이력

| 단계 | 결과 | 로그 |
|---|---|---|
| build01 | exit0, 경고1·오류0. CS8629 nullable `template.Value` 접근. | [build01](artifacts/p2-text-role/build-text-role-final-01.log) |
| build02 | QA가 `HasValue` 의미를 보존하면서 non-null `Guid` 지역 변수를 사용하도록 수정한 뒤 경고0·오류0·exit0. | [build02](artifacts/p2-text-role/build-text-role-final-02.log) |
| 신규9-01 | exit1, 논리8 통과·UI1 실패. `ApplyPropertiesButton` 직전 strict viewport visibility assertion. 전체165 미실행. | [new9-01](artifacts/p2-text-role/text-role-new9-final-01.log) |
| build03 | 경고0·오류1·exit1. UI helper 호출의 CS1503. 신규9-02는 실행하지 않음. | [build03](artifacts/p2-text-role/build-text-role-final-03.log) |
| build04 | UI helper 정리 후 경고0·오류0·exit0. | [build04](artifacts/p2-text-role/build-text-role-final-04.log) |
| 신규9-02 | 9/9·exit0. 다만 dedicated expanded 진단 PNG가 흰 이미지여서 expanded UI 시각 검토는 보류했다. 실패 PNG로 보존했으며 승인 근거로 쓰지 않았다. | [new9-02](artifacts/p2-text-role/text-role-new9-final-02.log) |
| build05 | 캡처 경로 수정 후 경고0·오류0·exit0. | [build05](artifacts/p2-text-role/build-text-role-final-05.log) |
| 신규9-03 | 논리8+UI1, 9/9·exit0. 메인이 직접 검토한 UI 산출물과 bounds는 manifest에 구분 기록. | [new9-03](artifacts/p2-text-role/text-role-new9-final-03.log) |
| 전체165-01 | 기존156+신규9, 165/165·exit0. 별도 자동 산출물이며 신규 run 직접 검토와 구분. | [full165-01](artifacts/p2-text-role/text-role-full165-final-01.log) |

전체 단계의 최종 로그, PNG/bounds, 직접 검토와 자동 산출물의 구분은 [text-role-final-manifest](artifacts/p2-text-role/text-role-final-manifest.md)에 있다. 비교 수식 PNG/JSON의 네 SHA256은 신규9-02와 신규9-03에서 동일했다. full165 산출물을 메인이 전부 직접 시각 검토했다고 보지 않는다.

2026-10-22 19:39, 19:54, 20:09 UTC 감사는 절차상 작업 정지/재개 이력이며 시험 근거가 아니다. 첫 UI 실패와 expanded 흰 PNG는 실패 이력으로 계속 보존한다.

## 남은 검증 경계

이번 slice는 논리·Headless·offscreen 범위에서 승인됐다. 실제 OS 창·키보드 입력·포커스·UIA·IME·clipboard·사용자 AppData, 외부 TeX 컴파일/parser, 구버전 앱 런타임 및 LaTeX에서 역할 provenance를 되읽는 왕복은 검증하지 않았다. 역할 메타데이터를 보존하는 `.aethermath` JSON/recovery round-trip은 검증했지만 P2/P1/P0 전체 완료나 OS 물리 입력 승인을 뜻하지 않는다.
