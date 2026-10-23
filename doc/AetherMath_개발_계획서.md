# AetherMath 개발 계획서

- 작성·개정일: 2026-10-23
- 제품 정의: **수식의 구조 편집과 레이아웃에 특화된 데스크톱 WYSIWYG 수식 편집기**
- 확정 기술: **C# · Avalonia 12**
- 기준 자료: 사용자의 제품 범위 정정, `img/AetherMath_UI_Design_20261022/`의 README·원본 5종·주석 5종
- 문서 상태: 개발 계획. 애플리케이션 구현이나 기술 검증을 완료했다는 보고가 아니다.

## 1. 제품 목적과 범위

### 1.1 핵심 정의

AetherMath는 **문서를 작성하면서 수식을 삽입하는 앱이 아니라, 수식 자체를 화면에서 직접 작성·수정하고 원하는 모양으로 배치하는 WYSIWYG 수식 편집기**다.

편집 대상은 단일 수식, 여러 줄 수식, 정렬된 연립식·전개식, 수식 그룹이다. 수식이 길어져 작업 영역이나 출력물이 여러 페이지처럼 보여도 데이터와 편집 동작의 중심은 수식이어야 한다. 페이지를 채우기 위한 본문 작성 엔진을 만들지 않는다.

**WYSIWYG(What You See Is What You Get) 원칙:** 렌더링된 수식 자체가 편집 대상이다. 사용자는 화면에 보이는 분자·분모·첨자·행렬 셀을 직접 선택하고 수정하며, 변경된 수식과 배치를 즉시 확인한다. LaTeX를 수정한 뒤 읽기 전용 미리보기를 갱신하는 방식만으로는 이 요구를 충족하지 않는다. LaTeX 코드는 보조 입력·표시·변환 수단이며 핵심 편집을 위해 코드 작성을 강제하지 않는다.

같은 글꼴·수학 스타일·폭·출력 설정에서는 편집 화면의 수식과 이미지·벡터 출력의 기호, 크기, 간격, 정렬, 줄바꿈이 일치해야 한다. 커서·선택 경계·빈 슬롯 안내 같은 편집 보조 표시는 결과물에서 제외한다. 출력 폭이나 Display/Inline 모드가 바뀌면 그 설정의 결과를 먼저 미리 보여준다. 코드 출력의 구조 보존은 별도 계약으로 관리한다.

핵심 사용자 흐름은 다음과 같다.

1. 화면에 표시되는 수식을 직접 입력하거나 기호·구조 템플릿으로 구성한다.
2. 렌더링된 수식의 분자·분모·첨자·행렬 셀 등 필요한 부분을 직접 선택해 편집하고 결과를 즉시 확인한다.
3. 등호 정렬, 줄바꿈, 간격, 크기, 괄호 등 수식의 레이아웃을 조정한다.
4. 편집 가능한 작업 파일로 저장하고, 이미지·벡터·수식 코드로 복사하거나 내보낸다.

### 1.2 포함 범위

- 수학 기호와 구조 템플릿을 이용한 WYSIWYG 수식 직접 편집.
- 구조적 커서 이동, 부분 선택, 삽입·삭제·교체, 복사·붙여넣기, 실행 취소·다시 실행.
- 긴 수식, 여러 줄 전개식, 연립식의 정렬과 배치.
- 분수·근호·첨자·적분·합·극한·행렬·경우 나누기·괄호의 구조와 모양 조정.
- 수식에 붙는 짧은 조건·설명·식별자. 수식의 일부 또는 수식 그룹의 보조 정보로 관리한다.
- 수식 작업의 저장·재열기, 최근 작업, 재사용 가능한 수식 템플릿.
- PNG·SVG·PDF·LaTeX·MathML 출력과 클립보드 전달.

### 1.3 제외 범위와 경계

| 구분 | 처리 원칙 |
|---|---|
| 일반 문서 작성 | 장문 본문, 문단 스타일 체계, 장·절 구성, 논문·강의노트 작성 기능은 개발 범위에서 제외 |
| 문서 부속 기능 | 정리·증명 블록, 그림 삽입·캡션, 일반 표, 목차, 서지·인용 관리, 본문 상호참조 엔진은 제외 |
| 수식 안의 텍스트 | 조건·단위·연산자 이름·짧은 설명은 지원 대상으로 유지. 독립적인 리치 문서 본문으로 확장하지 않음 |
| 행렬·경우 나누기 | 수식 구조로 지원. 일반 스프레드시트나 문서 표 편집과 구분 |
| 병렬 배치 | 여러 수식의 나란한 배치·정렬은 수식 레이아웃으로 검토. 본문이 흐르는 신문식 다단 조판과 구분 |
| 페이지 | 긴 수식의 표시·출력 단위. 페이지 자체가 수식 데이터의 소유자가 되지 않음 |
| 계산 기능 | 자동 계산·간소화·풀이·증명·그래프 생성은 이번 수식 편집기 계획에 포함하지 않음 |
| 외부 문서 | 복사·내보내기한 수식을 사용하는 소비자. 앱 내부에서 외부 문서 본문을 작성하지 않음 |

사용자의 제품 정의가 디자인에 담긴 문서 작성 예시보다 우선한다. 따라서 원본 화면에 보이는 모든 요소를 필수 기능으로 승격하지 않는다. 원본·주석 이미지는 보존하고, 적용 여부와 재해석은 이 계획서에서 구분한다.

### 1.4 확정 사항과 제안

- **확정:** WYSIWYG 방식의 수식 편집·수식 레이아웃 중심 제품, C#, Avalonia 12.
- **구현 제안:** .NET 10, MVVM, 구조화된 수식 트리, 공통 레이아웃 결과를 사용하는 화면·출력.
- **제품 제안:** 로컬 작업 파일, 자동 복구, Windows 우선 검증. 추가 OS와 UI 언어는 별도 결정한다.
- **명세가 필요한 항목:** 지원 LaTeX 문법과 직접 입력 범위, 자동 줄바꿈 규칙, 수식 그룹 배치 방식, 출력 조합별 세부 계약.
- 디자인의 `Saved` 아이콘으로 클라우드 저장을 가정하지 않는다. 한국어 UI 제공 여부와 수식 내 한글 설명 입력은 별개로 관리한다.

## 2. 디자인 해석과 기능 추적

[자료 안내](img/AetherMath_UI_Design_20261022/README.md)의 화면 명칭은 출처를 식별하기 위해 사용한다. 실제 제품의 기능명과 구현 범위를 확정하는 명칭은 아니다.

| ID | 화면과 근거 | 적용 방향 | 수식 편집기에서의 역할 |
|---|---|---|---|
| H | 홈: [원본](img/AetherMath_UI_Design_20261022/원본/01_홈.png) · [주석](img/AetherMath_UI_Design_20261022/주석/01_홈_주석.png) | 재해석 | 새 수식 작업, 최근 작업, 수식 템플릿, 선택 작업 미리보기 |
| E | 원본의 문서 편집기: [원본](img/AetherMath_UI_Design_20261022/원본/02_문서_편집기.png) · [주석](img/AetherMath_UI_Design_20261022/주석/02_문서_편집기_주석.png) | 수식 작업 화면으로 재해석 | 긴 수식·전개식 편집, 부분 선택, 수식 속성·구조, 빠른 복사 |
| F | 수식 빌더: [원본](img/AetherMath_UI_Design_20261022/원본/03_수식_빌더.png) · [주석](img/AetherMath_UI_Design_20261022/주석/03_수식_빌더_주석.png) | 핵심 기능 유지 | 기호·템플릿 삽입, 구조 편집, 코드 표시, 선택 노드 속성 |
| L | 레이아웃: [원본](img/AetherMath_UI_Design_20261022/원본/04_레이아웃.png) · [주석](img/AetherMath_UI_Design_20261022/주석/04_레이아웃_주석.png) | 수식 배치 중심으로 재설계 | 수식의 폭·줄바꿈·정렬·간격·그룹 배치 및 출력 경계 확인 |
| X | 내보내기: [원본](img/AetherMath_UI_Design_20261022/원본/05_내보내기.png) · [주석](img/AetherMath_UI_Design_20261022/주석/05_내보내기_주석.png) | 유지·대상 재정의 | 부분 수식·단일 수식·수식 그룹·전체 작업 출력, 클립보드, 프리셋 |
| C | 5개 화면의 공통 영역 | 유지·문구 조정 | 작업 이름, 저장 상태, 실행 취소, 배율, 탐색, 내보내기 |

### 2.1 문서처럼 보이는 요소의 처리

| 원본 요소 | 계획에서의 처리 |
|---|---|
| Documents, New Document, Open Document | 수식 작업 파일의 목록·생성·열기로 재해석. 실제 UI 문구는 `작업`, `수식 파일` 등으로 조정하는 안을 검토 |
| Lecture Notes, Research Paper, Problem Set | 문서 템플릿으로 구현하지 않음. 연립식·여러 줄 전개식·행렬/경우 나누기 같은 수식 템플릿으로 교체 제안 |
| Equation Sheet | 수식 또는 수식 그룹의 재사용 가능한 배치 템플릿으로 해석 |
| 본문·절 제목·정리·그림·표 예시 | 앱의 필수 편집 대상에서 제외. 수식이 외부 문서에 쓰이는 맥락을 보여주는 시각 자료로만 참고 |
| 문단 서식 도구 | 수식 글꼴·수학 스타일·선택 노드 속성 도구로 재설계. 일반 문단 편집 명령은 추가하지 않음 |
| Insert into Document | 현재 수식 작업의 선택 위치에 구조 삽입·적용하는 명령으로 변경 제안. 별도 문서 작성기로 전송하지 않음 |
| Equation / Document 출력 탭 | 선택 수식 / 전체 수식 작업 범위로 변경 제안. 본문 문서 출력이 아님 |
| 페이지 추가·복제·삭제·이동 | 내용 변경은 수식·행·그룹 단위 명령으로 제공. 출력 페이지 조작과 혼동되는 문구는 그대로 이식하지 않음 |
| 용지 크기·여백 | 페이지 출력 미리보기와 PDF 출력 경계에 적용. 수식 구조를 바꾸지 않음 |
| 머리말·꼬리말·절 스타일·그림/표 캡션 | 수식 편집기의 필수 기능에서 제외. 이 항목을 위해 문서 조판 엔진을 구축하지 않음 |
| 문서 다단 설정 | 본문 흐름 기능은 제외. 수식 그룹의 병렬 배치가 필요한 경우 별도의 수식 정렬 제약으로 설계 |
| Publication / Article 프리셋 | 수식 PDF / LaTeX 수식 프리셋으로 조정 제안. 논문 생성 기능을 뜻하지 않음 |
| 다운로드 링크·Share to | 편집 핵심과 분리한 부가 기능 검토. 호스팅·OS 연동 계약 없이 필수 서버 구축을 가정하지 않음 |

## 3. 핵심 편집 기능

### 3.1 수식 작업 영역 — E, C

- 단일 수식부터 여러 줄 전개식까지 같은 편집 표면과 수식 모델로 다룬다.
- WYSIWYG 편집 표면에서 렌더링된 수식에 커서를 놓고 부분 구조를 직접 선택·수정한다. 변경 즉시 해당 수식과 배치를 갱신하며 코드 창 전환이나 별도 컴파일 명령을 요구하지 않는다.
- 문자·숫자·기호 입력과 템플릿 삽입을 지원한다. 입력은 커서가 가리키는 수식 슬롯에 적용한다.
- 분자·분모, 근호 내부, 밑·지수, 적분 상하한, 행렬 셀, 경우 나누기의 식·조건을 개별 선택한다.
- 좌우/상하 이동과 슬롯 간 이동, 부분 범위 선택, 선택 구조의 교체·복제·삭제를 정의한다.
- Backspace/Delete로 구조 경계를 지울 때 내용 보존·구조 해제·전체 삭제의 규칙을 명세한다. 입력 한 번으로 예상치 못하게 하위 구조 전체를 잃지 않게 한다.
- 수식 행·그룹의 추가·복제·삭제·순서 변경을 지원한다. 구조 변경과 레이아웃 변경을 실행 취소할 수 있어야 한다.
- 빈 슬롯은 편집 상태로 보존하되 출력 시에는 미완성 상태를 알린다. 임의의 숫자나 기호로 채우지 않는다.
- 선택·배율·스크롤만 바꿔도 저장되지 않은 변경이 생기는 구조로 만들지 않는다.

**수용 기준:** 코드 패널을 닫은 상태에서 렌더링된 중첩 분수의 분모나 특정 행렬 셀을 직접 선택·수정할 수 있고 해당 부분만 즉시 변경된다. 구조 삭제·행 복제·속성 변경을 실행 취소하면 원래 수식과 편집 위치가 복구된다.

### 3.2 기호 팔레트와 구조 템플릿 — F

- 기호 분류 7종: Algebra, Calculus, Linear Algebra, Greek Letters, Logic, Arrows, Structures.
- 구조 템플릿 10종: 분수, 근호, 위첨자, 아래첨자, 적분, 합, 극한, 행렬, 괄호, 경우 나누기.
- 팔레트와 작업 영역의 빠른 삽입 도구는 동일한 편집 명령을 사용한다.
- 템플릿의 빈 슬롯과 초기 커서 위치를 정의한다. 템플릿을 다시 삽입할 때 선택 노드를 감쌀지 교체할지 동작을 구분한다.
- Properties/Structure 탭의 선택은 편집 표면과 같은 노드 ID를 사용한다.
- Clear All은 활성 편집 범위를 명확히 표시하고 실행 취소 가능하게 한다.
- 빌더를 별도 문서 앱의 보조 도구로 만들지 않는다. 빌더와 수식 작업 화면은 같은 수식을 편집하는 다른 도구 구성이다.

**수용 기준:** 모든 템플릿을 중첩할 수 있고 슬롯 이동·부분 선택·복사·다시 편집이 가능하다. 화면별로 서로 다른 수식 복사본을 편집하지 않는다.

### 3.3 수식 속성·텍스트·번호

- 글자 크기, 색상, 수학 글꼴/스타일, Display/Inline 스타일을 제공한다.
- 원본의 괄호 표시, `\dfrac`, 얇은 공백 `\,`, 가로·세로 간격 옵션을 수식 표현 규칙과 연결한다.
- 속성의 적용 범위를 선택 노드 / 행 / 그룹 / 작업 기본값으로 구분한다. 지원하지 않는 조합은 이유를 표시한다.
- 수식의 숫자·기호와 일반 텍스트를 구분한다. 단위·조건·짧은 설명의 표기와 글꼴을 명시한다.
- **제안:** 수식 내부의 텍스트 노드와 행/그룹에 붙는 짧은 주석을 제공한다. 주석 길이·위치·LaTeX/MathML 표현 범위는 P0에서 확정하며 일반 본문 편집기로 확장하지 않는다.
- 한글 IME, 결합 문자, 붙여넣기는 이 텍스트 입력 범위에서 검증한다. 한글 리치 문서 엔진 구현을 선행 조건으로 두지 않는다.
- 수식 번호·라벨은 행 또는 그룹을 식별하는 선택 기능으로 설계한다. 순서 변경·복제 시 번호와 라벨 충돌을 처리하되 문서 본문의 상호참조 시스템은 만들지 않는다.
- Display/Inline은 수식의 수학 스타일 및 외부 삽입용 출력 모드다. 인라인 수식이 들어가는 본문을 앱 안에 만들라는 의미가 아니다.

### 3.4 LaTeX 코드와 직접 입력

- 시각 편집 결과의 LaTeX 표시·구문 강조·복사는 기본 기능으로 둔다.
- 코드 패널은 WYSIWYG 편집의 보조 도구다. 코드 패널 없이도 지원 기호·템플릿의 수식을 생성·수정·배치·저장·출력할 수 있어야 한다.
- 지원 문법의 직접 입력·붙여넣기는 수식 편집 인터페이스 제안으로 검증한다. 전체 TeX 문서 편집이나 임의 매크로 실행을 전제하지 않는다.
- 지원 명령·환경과 미지원 문법의 처리 목록을 P0에서 확정한다.
- 직접 편집을 제공하면 문자열 초안과 마지막 유효 수식 트리를 분리한다. 미완성 명령이나 닫히지 않은 괄호의 입력을 지우지 않는다.
- 파싱 성공 시에만 구조에 반영하고 오류 위치를 표시한다. 미지원 명령을 조용히 삭제하거나 다른 기호로 대체하지 않는다.
- 왕복 변환은 지원 문법의 구조·의미 보존을 기준으로 검증한다. 공백이나 명령의 표준화와 원문 손실을 구분한다.
- 자동 코드 생성이 다시 파싱되어 중복 변경이나 무한 갱신을 일으키지 않도록 한다.

## 4. 수식 레이아웃 설계 — L

### 4.1 수식 내부 레이아웃

- 기호 폭, baseline, 위·아래 높이, 수학 축, 첨자 위치를 계산한다.
- 분수선·분자/분모 간격, 근호의 범위, 괄호 크기, 큰 연산자와 상하한, 행렬 셀 간격을 조정한다.
- 연산자·관계 기호·괄호 등 기호의 수학적 역할에 따라 기본 간격을 적용한다. 단순 문자열에 공백을 넣어 모양을 맞추지 않는다.
- Display/Text/Script 계열 수학 스타일과 중첩 구조의 크기 변화를 구분한다.
- 폰트가 바뀌어도 같은 규칙으로 측정·배치하며 글리프 누락과 대체 폰트 문제를 표시한다.

### 4.2 긴 수식과 여러 줄 수식

**논리 행과 시각 행을 구분한다.** 사용자가 만든 전개 단계·연립식의 행은 저장할 구조이고, 화면 폭 때문에 생긴 자동 줄바꿈은 파생 레이아웃이다.

- 등호·부등호·관계 연산자 등의 정렬 기준점을 지정한다.
- 행 간격, 이어지는 행의 들여쓰기, 그룹 사이 간격, 번호 위치를 정의한다.
- 수동 줄바꿈과 정렬점은 명시적으로 저장한다. 자동 줄바꿈은 허용된 연산자 경계에서만 수행하도록 검증한다.
- 의미를 바꾸는 연산자 추가·삭제나 전개 단계 재구성은 하지 않는다. 레이아웃을 위해 수식을 자동 간소화하지 않는다.
- 분수·근호·행렬·큰 괄호 등 분할하기 어려운 구조는 기본적으로 한 단위로 취급한다. 폭을 넘으면 진단하고, 임의 축소·잘라내기로 숨기지 않는다.
- 자동 줄바꿈을 끄거나 폭을 바꿔도 원래 수식 트리와 논리 행은 유지한다.
- 여러 그룹을 나란히 놓는 경우 자유로운 문서 객체 배치가 아니라 그룹 간 간격·정렬 같은 수식 배치 제약으로 표현하는 안을 검토한다.

**수용 기준:** 같은 전개식을 넓고 좁은 작업 영역에서 볼 때 논리 구조와 정렬 기준이 보존된다. 줄이 바뀐 뒤에도 커서 이동·부분 선택·hit-test가 원래 수식 노드를 가리킨다.

### 4.3 작업 영역과 출력 페이지

- **권장 기본 표시:** 수식 길이에 따라 확장되는 연속 작업 영역. 스크롤·확대·폭 제한을 제공한다.
- **출력 표시:** 필요할 때 용지 크기·방향·여백을 적용해 같은 수식 배치를 페이지로 나누어 미리 본다.
- 페이지는 레이아웃의 결과이며 작업 파일에 본문 페이지 객체를 원본 데이터로 저장하지 않는다.
- 페이지 분할은 그룹 사이 또는 허용된 논리 행 경계에서 수행한다. 한 행의 분수·행렬을 페이지 경계로 임의 분할하지 않는다.
- 분할 불가능한 수식이 용지를 넘으면 폭·용지 조정이나 명시적 행 나눔을 안내한다. 수식 일부를 출력하지 않고 성공 처리하지 않는다.
- 내용 추가·복제·삭제·이동의 단위는 수식 행·그룹이다. 페이지 번호가 바뀌어도 같은 수식 노드가 유지되어야 한다.
- 화면 확대 배율, 수식 자체의 크기, 출력 DPI를 별개로 관리한다.

**수용 기준:** 연속 보기↔페이지 미리보기 전환과 용지 변경이 수식 내용·선택·논리 행을 바꾸지 않는다. 출력 페이지가 여러 장이어도 문서 작성 기능을 요구하지 않는다.

## 5. 데이터 모델과 구현 구조

### 5.1 수식 중심 데이터 모델

| 모델 | 저장할 내용 | 저장하지 않을 내용 |
|---|---|---|
| `EquationWorkspace` | 작업 ID, 스키마 버전, 이름, 수식 그룹, 기본 스타일, 배치·출력 설정 | 일반 문서 본문, 장·절·서지 구조 |
| `EquationGroup` | 수식 행의 순서, 정렬 기준, 행/그룹 간격, 번호 정책, 제한된 보조 정보 | 페이지별 본문 컨테이너 |
| `EquationRow` | 수식 루트, 명시적 줄바꿈·정렬점, 식별자·선택적 번호 | 화면 폭에 따른 임시 시각 행 |
| `MathNode` | 안정 ID와 기호·구조·자식 슬롯, 필요한 노드별 표현 속성 | 최종 화면 좌표나 렌더링 이미지 |
| `EditingSession` | 메모리상의 revision, 저장 revision, 구조적 커서·선택, undo/redo | UI 선택을 수식 문자로 변환한 가짜 데이터 |
| `LayoutResult` | 재생성 가능한 노드별 위치, baseline, 시각 행, hit-test 정보, 출력 경계 | 편집 원본을 대체하는 픽셀 데이터 |
| `ExportPreset` | 대상 범위, 형식, 버전 있는 옵션 | 특정 작업의 일시적 선택 ID |

수식 하나는 한 행을 가진 그룹으로 표현할 수 있다. 여러 줄 구조도 동일한 수식 노드와 편집 규칙을 사용한다. `Document` 중심 모델을 이름만 바꿔 유지하지 않는다.

### 5.2 편집·저장 불변 조건

- 편집 위치는 노드 ID와 슬롯/문자 오프셋으로 표현한다. 레이아웃 좌표를 원본 커서 위치로 사용하지 않는다.
- 시각적 줄바꿈·배율 변경 후에도 구조적 선택을 보존한다. 구조 삭제 시 다음 커서 위치를 정의한다.
- 수식 내부 텍스트의 선택은 결합 문자와 IME 조합 범위를 존중한다.
- 구조 입력, 속성 변경, 행 이동, 텍스트 조합 입력의 undo 트랜잭션 경계를 정의한다. 코드·시각 편집이 같은 이력을 사용한다.
- 복제는 새 노드 ID를 생성한다. 번호·라벨은 중복 정책을 적용한다.
- **파일 형식 제안:** `.aethermath`에 버전 있는 수식 모델·스타일·배치 설정을 저장한다. 초기에는 JSON 기반을 우선 검토하고 불필요한 문서 자산 패키지를 전제하지 않는다.
- 저장 후 재열면 원본 구조와 논리적 배치가 복원되어야 한다. PNG/PDF는 재편집 가능한 원본 파일의 대체물이 아니다.
- 저장은 임시 파일 작성 후 교체하며, 저장 중 새 편집이 발생하면 저장한 revision만 기록한다. 실패 시 기존 파일과 변경 상태를 보존한다.
- 자동 복구본은 사용자 저장 파일과 분리한다. 미지원 스키마·손상 파일은 원본을 덮어쓰지 않고 진단한다.

### 5.3 기술 선택

| 영역 | 기준 |
|---|---|
| 언어·UI | C# · Avalonia 12 사용. .NET 10 제안. 실제 사용할 안정 패치와 SDK를 확인·고정 |
| UI 구성 | XAML·MVVM, compiled binding과 `x:DataType` 활용. CommunityToolkit.Mvvm은 유지보수를 어렵게 만들 수 있어 채택하지 않고, 필요한 기능이 있다면 직접 구현 |
| 편집 표면 | 렌더링된 수식을 직접 수정하는 WYSIWYG 전용 컨트롤. 읽기 전용 미리보기나 일반 리치텍스트 편집기로 대체하지 않음 |
| 수식 엔진 | 구조 편집, 측정 좌표, 글꼴, 벡터 출력, 지원 문법, 재배포 라이선스를 함께 검증 |
| 렌더링 | 화면·미리보기·출력에서 같은 수식 레이아웃 결과를 사용. Avalonia UI 스크린샷으로 수식 출력을 대체하지 않음 |
| 플랫폼 연동 | 파일 선택, 클립보드, DPI·글꼴·IME는 실제 지원 OS에서 검증 |

표시만 가능한 수식 라이브러리는 편집 엔진이 아니다. 부분 선택·슬롯 이동·hit-test를 구현할 정보가 없으면 렌더링 품질이 좋아도 핵심 엔진의 요구를 충족하지 못한다. 엔진·PDF 라이브러리는 P0 실증 없이 특정 패키지로 확정하지 않는다.

### 5.4 책임 분리

- `Core`: 수식 트리, 행·그룹·작업 모델, 스타일·정렬 제약. Avalonia에 의존하지 않는다.
- `Editing`: 구조 편집 명령, 커서·선택, undo/redo, LaTeX 파싱·생성.
- `Layout`: 수식 측정, 줄바꿈·정렬, 노드 좌표, 출력 페이지 분할.
- `Infrastructure`: 저장·복구, 출력 인코더, 최근 작업·프리셋, 플랫폼 어댑터.
- `Desktop`: Avalonia 셸·ViewModel·수식 컨트롤, 플랫폼 서비스 호출과 구성 루트.

이는 논리적 책임 구분이며 처음부터 각 책임을 모두 별도 프로젝트로 나누거나 플러그인 계층을 만들 필요는 없다. 순환 의존 없이 테스트 격리·플랫폼 의존성 차단에 필요한 만큼 분리한다.

입력 → 편집 명령 → 수식 모델 변경 → 영향 범위 레이아웃 → 화면 갱신으로 처리한다. 매 키 입력마다 전체 작업을 복사·파싱·재렌더링하지 않는다. 캐시는 수식 revision·스타일·글꼴·폭을 기준으로 관리하고, 오래된 비동기 결과가 새 편집 결과를 덮어쓰지 못하게 한다.

### 5.5 Avalonia 12 주의점

- 플랫폼 서비스는 `TopLevel`에서 얻는다. UI 컨트롤 접근은 해당 dispatcher에서 수행한다.
- 클립보드는 `DataTransfer` / `DataTransferItem` / `IClipboard.SetDataAsync` 계열을 사용한다. 제거된 `IDataObject` 기반으로 신규 구현하지 않는다.
- 읽은 `IAsyncDataTransfer`는 사용 후 해제하고, 쓰기로 넘긴 전송 객체는 Avalonia의 소유권 규칙을 따른다.
- 플랫폼별 형식 식별자와 종료 후 클립보드 데이터 수명은 실제 붙여넣기로 검증한다.
- 기본 텍스트 셰이핑 경로를 우선 사용한다. Skia를 직접 구성하면 HarfBuzz 설정도 검토한다. 글리프 측정·수식 레이아웃과 입력 메서드를 별도로 검증한다.

## 6. 복사·내보내기 — X

### 6.1 출력 대상

1. **선택 부분 수식:** 선택한 하위 구조를 유효한 수식 조각으로 출력. 잘못된 구조 선택이면 범위 조정을 안내한다.
2. **단일 수식:** 선택한 수식 행 또는 단일 수식 구조.
3. **수식 그룹:** 여러 논리 행과 정렬·간격을 포함한 전개식·연립식.
4. **전체 작업:** 작업 내 모든 수식 그룹. 일반 문서 본문을 포함하지 않는다.

출력 범위·포함할 번호/짧은 주석·레이아웃 옵션을 미리보기에 표시한다. 선택 수식이 없으면 임의의 다른 식을 대신 내보내지 않는다.

### 6.2 형식별 계약 제안

| 형식 | 수식·부분 수식 | 그룹·전체 작업 | 검증 기준 |
|---|---|---|---|
| PNG | 수식 경계와 여백에 맞춘 이미지 | 전체 경계 이미지 또는 출력 페이지별 파일 | 투명 배경, DPI·픽셀 크기, 여백. 큰 이미지 한도와 분할 방식을 명시 |
| SVG | 단일 벡터 수식 | 그룹/작업의 벡터 배치 또는 페이지별 파일 | 크기·정렬 보존, 외부 폰트 의존·윤곽선 정책 |
| PDF | 수식 크기 또는 선택 용지의 출력 | 허용된 행·그룹 경계에서 나눈 페이지 | 잘림 없음, 벡터 품질·폰트 라이선스·검색/접근성 정책 |
| LaTeX | 지원 문법의 수식 조각 | 정렬·여러 줄 구조에 맞는 환경과 수식 그룹 | 논리 행·정렬점 보존. 화면의 임시 자동 줄바꿈을 논리 행으로 바꾸지 않음 |
| MathML | Presentation MathML 수식 | 지원되는 행·표 형태의 수식 구조 또는 복수 수식 묶음 | 각 구조의 의미·읽기 순서 보존. 문서용 XHTML 본문을 새로 만들지 않음 |
| Clipboard | 이미지·LaTeX 텍스트·검증된 SVG/MathML 형식 | 명시적으로 고른 수식 범위의 코드 또는 이미지 | 대상 앱의 실제 소비 가능 형식과 대체 표현을 표시 |

복수 수식 묶음의 파일 구조와 지원 환경은 P0에서 샘플로 확정한다. 임의 조합을 지원한다고 표시한 뒤 일부 구조를 조용히 버리지 않는다.

- PNG 해상도, 배경, 잘라내기 여백, Display/Inline, 번호 포함 옵션을 관련 출력에만 적용한다.
- LaTeX는 수식 조각과 필요한 패키지 선언을 구분한다. 독립 컴파일용 최소 래퍼를 제공하더라도 수식 출력 편의를 위한 것이며 논문·본문 생성 기능이 아니다.
- 코드 출력은 수식의 논리 구조를 보존한다. 이미지·SVG·PDF 출력은 선택한 폭과 수식 배치를 반영한다.
- 출력은 시작 시점 revision을 고정한다. 실패·취소 시 부분 파일을 완료 결과로 남기지 않는다.
- SVG를 텍스트로 복사한 것과 대상 앱이 벡터 이미지로 붙여넣는 것을 구분한다. 검증하지 않은 형식 호환을 보장하지 않는다.

### 6.3 프리셋·미리보기·공유

- 빠른 복사와 전체 내보내기는 같은 수식 출력 경로를 사용한다.
- 프리셋은 이미지 복사, 벡터 수식, 수식 PDF, LaTeX 등 수식 재사용 목적에 맞춘다.
- 새 창은 출력 미리보기로 사용하며 원본 작업과 별도의 본문 문서를 만들지 않는다.
- `Share to`는 수식 결과물을 외부 앱에 전달하는 부가 연동으로 검토한다.
- 다운로드 링크는 별도 호스팅·접근 권한·보존 정책이 필요한 부가 서비스다. 적용 여부가 확정되지 않았으며 핵심 수식 편집 기능의 개발을 서버 구축에 종속시키지 않는다. 로컬 경로 복사로 구현했다고 처리하지도 않는다.

## 7. 홈·셸과 사용성 — H, C

- 홈은 새 수식 작업, 최근 작업, 고정된 작업 묶음, 수식 템플릿과 미리보기로 구성한다.
- **템플릿 제안:** 빈 수식, 정렬된 전개식, 연립식, 행렬·경우 나누기. 원본의 문서 템플릿 4종을 그대로 구현하는 요구가 아니다.
- 작업 목록은 이름·경로·썸네일·수정 시각과 열기를 제공한다. 파일이 없으면 경로 재지정 또는 목록 제거를 안내한다.
- 상단은 활성 작업 이름, `변경 없음 / 변경됨 / 저장 중 / 저장 실패`, undo/redo, 배율, 내보내기를 제공한다.
- 탐색 중에도 같은 수식 세션·선택을 유지한다. Editor와 Symbols 이동으로 수식이 별도 복사본으로 분리되지 않게 한다.
- 검색을 제공하면 수식 기호·이름·라벨 탐색 범위부터 명세한다. 일반 본문 검색 기능을 구축하지 않는다.
- Settings는 수식 글꼴·입력·저장·출력 기본값에 집중한다. 상세 화면은 별도 디자인이 없으므로 기능 범위 안에서 설계한다.
- 공통 셸·옅은 청색 배경·흰 패널·파란 강조 등 시각 언어를 참고하되 중앙 작업 영역은 수식 편집에 우선 배정한다.
- 좁은 창에서 속성 패널을 접거나 스크롤할 수 있게 하고, 확대·DPI 변경 후 hit-test 좌표를 검증한다.
- 키보드만으로 기호·슬롯·행·속성에 접근할 수 있게 한다. 선택·focus·오류 상태는 색만으로 전달하지 않는다.

## 8. 단계별 개발 계획

| 단계 | 선행 조건 | 주요 산출물 | 완료 기준 |
|---|---|---|---|
| P0 — 핵심 기술 검증 | 제품 정의와 디자인 해석 | 엔진·폰트 후보 비교, 수식 예제 집합, WYSIWYG 구조 선택·슬롯·IME 실험, 정렬·줄바꿈·출력 샘플, 지원 문법·출력 계약 | 실제 Avalonia 12 앱에서 렌더링된 중첩 수식 직접 편집→즉시 갱신→긴 전개식 정렬→원본 저장·재열기→벡터 출력 확인. 편집·출력 배치 일치와 호환·라이선스 근거 기록 |
| P1 — 수식 모델·세션·기반 | P0의 구조·엔진 경계 | 작업/그룹/행/노드 모델, 구조적 커서, undo 기반, 저장·열기·복구, 공통 셸 | 재시작 후 수식 구조·스타일·명시적 정렬이 복원. 저장 실패 시 데이터와 변경 상태 보존 |
| P2 — WYSIWYG 구조 편집과 빌더 | P1 | 7종 기호 분류, 10종 템플릿, 부분 선택·삭제·복사, 슬롯 이동, 구조 탭, 속성, 코드 생성과 확정된 직접 입력 | 코드 패널 없이 중첩·행렬·경우 나누기를 화면에서 직접 편집. 변경 즉시 반영, undo와 코드 왕복에서 구조 보존 |
| P3 — 수식 레이아웃 | P2의 노드·측정 계약 | baseline·간격·괄호·첨자, 등호 정렬·논리 행, 긴 수식 줄바꿈, 그룹 배치, 연속 보기·출력 미리보기 | 폭·배율·용지 변경 후 수식 의미·커서·정렬 보존. 넘침을 숨기지 않고 페이지 분할 단위 준수 |
| P4 — 출력과 작업 재사용 | P2, 최종 배치 출력은 P3 | PNG/SVG/PDF/LaTeX/MathML, 클립보드·프리셋, 홈·최근 작업·수식 템플릿 | 확정된 대상×형식 조합을 외부 앱에서 열거나 붙여넣어 검증. 출력과 편집 가능한 원본 저장을 구분 |
| P5 — 통합·배포 검증 | P1–P4 | 시각 검수, 접근성·DPI·성능·파일 안전성 검증, 지원 OS 패키지, 사용 안내 | 깨끗한 지원 OS에서 설치 후 아래 통합 시나리오 통과. 데이터 손실·구조 손상·잘못된 성공 표시 없음 |

수식 인코더와 홈의 일부 작업은 핵심 편집과 병행할 수 있지만, 구조·레이아웃 계약을 먼저 공유해야 한다. UI 화면을 먼저 복제하고 수식 엔진을 나중에 붙이는 순서로 진행하지 않는다.

### 8.1 작업량 산정 원칙

기존 계획의 문서 편집·문서 조판을 전제로 한 **31–47 인·주 추정은 폐기한다.** 같은 수치를 수식 편집기 일정에 재사용하지 않는다.

P0에서 다음을 확인한 뒤 P1–P5의 작업량을 산정한다.

- 표시 전용 엔진에 구조 편집을 보완할지, 측정·배치까지 직접 구현해야 하는지.
- 지원 기호·환경·템플릿 목록과 직접 LaTeX 입력 범위.
- 자동 줄바꿈·복수 그룹 배치·출력 페이지 분할의 복잡도.
- 클립보드 소비 대상과 지원 OS의 수.

계획의 진척은 화면 개수가 아니라 구조 편집·레이아웃·외부 재사용의 완료 기준으로 판단한다. 부가 공유 서비스는 채택 시 별도 산정하며 핵심 편집기 개발량에 암묵적으로 포함하지 않는다.

## 9. 검증 계획과 완료 기준

### 9.1 기능·경계 검증

| 영역 | 대표 시나리오 | 합격 기준 |
|---|---|---|
| WYSIWYG | 코드 패널을 닫고 화면의 분모·첨자·행렬 셀을 직접 수정한 뒤 같은 설정으로 출력 | 코드 작성 없이 핵심 편집 가능, 변경 즉시 표시, 편집 보조 표시를 제외한 화면·이미지/벡터 배치 일치 |
| 구조 편집 | 중첩 분수의 분모·첨자·적분 상하한·행렬 셀 변경 | 선택한 구조만 변경되고 이웃 노드 보존 |
| 커서·선택 | 빈 슬롯·구조 경계·여러 시각 행 이동, 선택 후 교체 | 좌표가 아닌 논리 위치 기준으로 일관된 이동 |
| 텍스트 입력 | 수식 조건·설명에 한글 조합·결합 문자 입력 및 삭제 | 조합·선택 경계 보존, 인접 수식 훼손 없음 |
| undo/redo | 구조 삭제·행 이동·속성 변경·코드 입력 | 수식 구조와 편집 상태가 올바르게 복원 |
| LaTeX | 지원 문법 왕복, 미완성 명령, 미지원 환경 | 구조 보존 또는 오류·제한 명시, 조용한 손실 없음 |
| 수식 배치 | 등호 정렬·연립식·긴 전개식·큰 괄호·행렬 | 정렬·간격 정확성, 의미를 바꾸는 줄바꿈 없음 |
| 표시 전환 | 폭·배율·DPI 변경, 연속 보기↔페이지 보기 | 원본 구조·논리 행·선택 보존 |
| 출력 | 부분 수식/그룹/전체 작업의 확정 형식 조합 | 범위·번호·스타일·출력 경계 일치, 외부 앱에서 소비 가능 |
| 파일 수명 | 저장·재시작·재열기, 쓰기 실패·손상·복구 | 수식·배치 보존, 기존 파일 덮어쓰기 손상 없음 |

모델·명령·파서·레이아웃 경계·저장 왕복은 결정적 자동 테스트로 보호한다. 실제 수식 컨트롤의 입력·선택과 외부 앱 붙여넣기는 실제 실행으로 별도 검증한다. 수식 렌더링 성공만으로 편집 성공을 판단하지 않는다.

### 9.2 성능 목표 제안

문서 페이지 수 대신 수식 노드 수·중첩 깊이·행 수·행렬 크기로 시험 집합을 정의한다. 구체적인 규모와 시험 장비는 P0에서 고정한다.

- 일반 수식 입력·부분 편집의 화면 반응 p95 100ms 이내를 초기 목표로 두고 측정한다. 이는 아직 측정한 결과가 아니다.
- 긴 전개식과 큰 행렬은 별도 사례로 측정하고 변경된 노드·행부터 다시 계산한다.
- 긴 출력·저장 중에도 UI가 응답하고 취소가 가능해야 한다.
- 반복 편집·작업 열기/닫기 후 레이아웃·글꼴·이미지 캐시 메모리가 지속 증가하지 않아야 한다.

### 9.3 통합 사용자 시나리오

1. 코드 패널을 닫은 채 빈 수식 작업을 만들고 화면에서 분수·근호·적분·첨자를 조합한다.
2. 렌더링된 분모만 직접 선택해 수정하고 즉시 바뀐 결과를 확인한다. 구조를 삭제·복원하며 슬롯 이동을 확인한다.
3. 여러 줄 전개식을 추가하고 등호 정렬·행 간격·이어지는 행의 들여쓰기를 조절한다.
4. 행렬·경우 나누기와 짧은 한글 조건을 작성한다.
5. 좁은 폭과 넓은 폭에서 보며 자동/수동 줄바꿈, 선택 위치, 넘침 표시를 확인한다.
6. 저장·재시작·재열기 후 수식 구조와 명시적 레이아웃을 다시 편집한다.
7. 선택 부분·수식 그룹을 이미지·벡터·수식 코드로 내보내고 외부 앱에서 열거나 붙여넣는다.
8. 전체 작업을 PDF 등으로 출력해 허용된 행·그룹 경계에서만 페이지가 나뉘는지 확인한다.

**완료 판정:** 코드 작성을 강제하지 않는 WYSIWYG 수식 직접 편집, 즉시 시각 반영, 긴 수식 레이아웃, 원본 저장·재편집, 확정 출력 계약이 실제로 이어져야 한다. 같은 출력 설정에서는 편집 보조 표시를 제외한 화면과 이미지·벡터 출력의 배치가 일치해야 한다. 논문이나 강의노트를 작성할 수 있는지는 이 제품의 완료 기준이 아니다. 반대로 문서 기능을 제외했다는 이유로 부분 선택·정렬·긴 수식 처리의 품질을 낮추지 않는다.

## 10. 주요 결정 항목과 위험 대응

| 항목 | 결정·검증 내용 | 시점 |
|---|---|---|
| 수식 엔진 | 부분 편집에 필요한 구조·좌표 제공, 측정·벡터 출력·라이선스 | P0 |
| 지원 문법 | 기호·환경 목록, 직접 입력 범위, 미지원·미완성 수식 정책 | P0 |
| 줄바꿈·정렬 | 논리 행/시각 행, 분할 가능 경계, 정렬점·넘침 정책 | P0에서 계약, P3에서 완성 |
| 짧은 설명·병렬 배치 | 수식에 필요한 표현 범위와 코드 출력 방식. 문서 기능과 구분 | P0 |
| 출력 계약 | 부분 선택 범위, 번호/주석 포함, 복수 수식 묶음, 소비 대상 앱 | P0에서 샘플, P4에서 검증 |
| 플랫폼·글꼴 | 지원 OS·기본 UI 언어, 수학 글꼴·한글 대체·배포 조건 | P0–P1 |
| 작업 파일 | 스키마·확장자·원본 보존·복구 정책 | P1 |
| UI 명칭 | 원본의 Document·문서 템플릿·페이지 명령을 수식 제품 용어로 조정 | P1 |
| 공유 | 호스팅·OS 공유의 채택 여부. 수식 편집 엔진과 별도 계약 | 채택 결정 시 별도 계획 |

첫 기술 검증은 **중첩 수식 부분 편집 → 여러 줄 수식 정렬 → 원본 저장·재열기 → 벡터 출력**이다. 장문 본문과 수식이 섞인 문서 작성 시연을 선행 목표로 삼지 않는다.

## 11. 참고 자료와 검토 범위

- [UI 자료 안내](img/AetherMath_UI_Design_20261022/README.md) 및 2절의 원본·주석 이미지 10종.
- [Avalonia 12 주요 변경점](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes): .NET 요구, compiled binding, 클립보드·텍스트 셰이퍼 변경.
- [Avalonia Clipboard](https://docs.avaloniaui.net/docs/services/clipboard): 형식, 소유권, 플랫폼별 데이터 전달과 수명.

이 개정은 디자인을 일반 문서 작성 앱의 명세로 해석했던 전제를 바로잡는다. 현재 구현과 실제 검증 범위는 아래에 구분해 기록한다. 기술 단위의 통과를 전체 P0 또는 제품 완료로 판정하지 않는다.

## 12. 개발 실행 및 검증 현황

### 12.1 실행 환경과 명령

- `global.json`의 .NET SDK 10.0.402(`latestPatch`), .NET 10, Avalonia 12.1.4를 사용한다.
- MVVM은 직접 구현하며 `CommunityToolkit.Mvvm`을 사용하지 않는다.
- 기본 수학 글꼴은 Cambria Math다. 현재 SVG는 외부 글꼴을 참조하며 글꼴 번들·임베딩·윤곽선 변환을 제공하지 않는다.
- 아래 검증은 Windows에서 수행했다.

```powershell
dotnet restore AetherMath.sln
dotnet build AetherMath.sln -c Debug --no-restore
dotnet run --project tests/AetherMath.LogicQA/AetherMath.LogicQA.csproj -c Debug --no-build --no-restore
.\src\AetherMath.Desktop\bin\Debug\net10.0\AetherMath.Desktop.exe
```

### 12.2 현재 기술 단위의 구현과 확인 결과

P0-R1-F05는 첫 기술 검증 단위다. 구현 범위는 불변 수식 AST, 문자소 단위 caret·부분/구조 선택·undo/redo 명령, 분수·근호·첨자·2×2 행렬 삽입, 실제 글꼴 측정과 등호 정렬, 원본 JSON 저장·열기, 공통 레이아웃 기반 SVG 출력이다. LaTeX는 AST 직렬화와 코드 표시 경로를 제공하며, 전체 지원 문법·파서·출력 계약의 완료를 의미하지 않는다.

| 검증 | 실제 결과 |
|---|---|
| F05 제품·QA 통합 빌드 | 종료 코드 0, 경고 0, 오류 0 |
| F05 논리 소비자 검증 | 38개 전체 합격, 실패 0, 종료 코드 0. 중첩 첨자 LaTeX grouping 회귀 포함 |
| 별도 제품 컨트롤 실행 | 축소 선택 뒤 Backspace·undo의 내용/caret 복원, 반복 렌더의 동일 이미지, Session 교체와 visual-tree 분리·재부착 후 내용/화면 갱신 확인 |
| 실제 Windows UI·외부 소비 | F05의 UIA 분수 삽입·undo/redo 재그리기, 원본 저장·SVG 출력·격리 Chrome 소비, 빈 필수 슬롯의 동일 대상 출력 실패와 기존 파일 보존, 최종 undo 후 원본 동일성 및 정상 종료 확인. 물리 편집 게이트와 별도 |

2026-10-22 추가 QA는 위 과거 F05 결과를 대체하지 않고, 글꼴 초기화 순서와 비동기 붙여넣기·포인터 수명주기를 확장 검증한다.

| 추가 검증 | 실제 결과 |
|---|---|
| Debug 통합 빌드 | 종료 코드 0, 경고 0, 오류 0. `doc/verification/artifacts/build-final-01.log` |
| 전체 자동 QA | 50/50 합격: 기존 논리 38, 클립보드 6, headless 합성 포인터 4, 글꼴 순서 1, 오프스크린 출력 1. `doc/verification/artifacts/regression-final-50-01.log` |
| 글꼴 요청 순서 | 새 프로세스 3개에서 Normal/Italic/Oblique 선요청 순서를 비교해 1/1 합격. `after-normal-face-warmup` 뒤 Normal은 Cambria Math Normal, 제품 Italic은 native Oblique 요청과 동일한 실제 face·glyph·윤곽을 사용했다. 이전 사전확보 전 실패와 raw System 관측 파일은 `doc/verification/artifacts/font-probe`에 보존한다. 요약: `font-probe-order-matrix-final-01.json` |
| 화면 외 수식·SVG | 실제 수식 컨트롤 PNG, 같은 LayoutResult의 SVG 및 별도 프로필 Firefox `--headless --no-remote` PNG 생성·소비 합격. 변수 기울임과 숫자 직립은 오프스크린 이미지에서 확인했다. `font-offscreen-final-01.log` |
| 포인터 수명주기 | Avalonia.Headless 12.1.4 + Skia의 별도 프로세스에서 4/4 합격. 플랫폼 handle이 `STUB`/0인 것을 표시 전후 검사하고 실제 visual-tree 연결·분리와 합성 pointer 이벤트를 확인했다. 물리 입력 검증은 아니다. |

글꼴 초기화 보장 경계는 `MainWindowViewModel`의 metrics 생성 → `EnsureRegistered`와 SystemFonts Normal face 사전 확보 → `MainWindow.InitializeComponent()`의 XAML 생성 순서다. 이 경계 전에 앱 외부 코드가 `SystemFonts`를 조회해 캐시를 오염시키는 경우에는 보장을 주장하지 않는다. Italic 요청을 SystemFonts의 native Oblique face로 위임하는 경로는 Windows/Avalonia 12.1.4 실측에 근거한 우회이며 다른 플랫폼·버전 전체로 일반화하지 않는다. 기존 사전확보 전 요청 순서 실패 자료는 수정 후 보고와 함께 보존한다.

렌더 스모크는 실제 컨트롤을 별도 소유 창에 연결하고 이미지를 확인했다. 물리 마우스·키 입력, 클립보드 성공/실패, 캐시 메모리 증가 여부를 검증한 것은 아니다. 직립/기울임 요청의 실제 글꼴이 다르므로 요청한 `font-family` 문자열만으로 화면과 외부 SVG의 글꼴 일치를 판정하지 않는다.

### 12.3 남은 필수 게이트와 전체 범위

- 2026-10-23 전체 LaTeX 코드 복사 하위 단위가 기준선198에 논리 `LATEX-COPY-SUCCESS`, `LATEX-COPY-GATES`, `LATEX-COPY-BUSY`, `LATEX-COPY-SNAPSHOT`, `LATEX-COPY-FAIL-CANCEL`, `LATEX-COPY-NOTIFY-ORDER` 6건과 Headless `LATEX-COPY-UI` 1건을 더한 전체205 회귀로 통합 승인됐다. build03은 경고0·오류0·exit0, 신규7-02는 7/7·exit0, 전체205-01은 205/205·exit0이다. 초기 build01은 `Contains` helper 누락 CS0103 두 오류/exit1, 신규7-01은 논리6 PASS·UI1 FAIL(6/7·exit1)이며 첫 collapsed 캡처에서 선택적 LaTeX 코드 영역의 window-relative 좌표를 얻지 못해 PNG/bounds가 없었다. 메인은 신규7-02의 23:43:42–43 UTC PNG9개(초기 접힘 최소/기본, 성공 펼침, 실패 펼침, busy 펼침, 이전 snapshot 펼침, unavailable 펼침, blocked 펼침, 성공 접힘 최소)와 같은 실행 bounds11의 핵심 상태·좌표 필드를 검토했다. 확인된 값은 STUB/0, 접힘 viewport257/437px, source90px·진단58px·grid148px, 펼침 최소 editor76px 및 부분 TextBox 선택 0–3 보존이다. initial-expanded/retry PNG2는 직접 검토하지 않았다. 전체205-01의 23:46:06–07 UTC 복사 UI PNG/bounds 11쌍은 별도 자동 산출물이다. 상세 실패 이력 및 실행별 자료는 [P2 전체 LaTeX 코드 복사 QA](verification/P2-latex-copy-QA.md)와 [manifest](verification/artifacts/p2-latex-copy/latex-copy-manifest.md)에 있다. 이 승인은 fake sink 기반 논리·Headless/offscreen 복사 경계에 한정되며 실제 OS clipboard runtime·물리 입력·사용자 AppData·computer-use·외부 TeX, syntax highlighting/parser, 다른 scope 및 전체 출력 matrix 또는 §3.4-01 전체 완료를 뜻하지 않는다. 2026-10-22 23:24·23:39 UTC 감사는 절차 기록이며 통과 근거가 아니다.
- 2026-10-23 LaTeX 구문 강조 하위 단위는 기준선205에 논리4개와 Headless `LATEX-HIGHLIGHT-UI`1을 더한 전체210 회귀로 통합 승인됐다. build05 경고0·오류0·exit0, 별도 신규5-04는 5/5·exit0, 전체210-01은 210/210·exit0이다. 메인은 신규5-04의 PNG8개와 bounds8의 주요 필드를 직접 검토해 강조 on/off의 문자 표시, 선택·스크롤 보존, source 변경 후 초기화 및 fake writer 상태를 확인했다. 전체210-01의 PNG/bounds8쌍은 별도 자동 산출물이다. 초기 build01의 2 warning/2 error, new5-01/-02 실패, new5-03의 자동 통과 후 시각 거절을 포함한 실행 이력은 [P2 LaTeX 구문 강조 QA](verification/P2-latex-highlight-QA.md)와 [manifest](verification/artifacts/p2-latex-highlight/latex-highlight-manifest.md)에 보존했다. 이 승인은 표시용 UTF-16 tokenization 및 논리·Headless/offscreen UI에 한정된다. parser/지원 문법 확정, 외부 TeX, 실제 OS clipboard·입력·포커스/UIA/AppData, P0–P5 전체 완료는 포함하지 않는다. 2026-10-23 00:08 및 00:23 UTC 감사는 절차 기록이며 테스트 근거가 아니다.
- 2026-10-22 P2 괄호 종류 속성 편집 하위 단위는 기준선191에 논리6+Headless `BRACKET-KIND-UI`1을 더한 전체198/198으로 통합 승인됐다. build02 경고0·오류0·exit0, 신규7-01 7/7·exit0, 전체198-01 198/198·exit0이다. 최초 build01은 UI helper의 `Find` 선언 누락으로 CS0103 16건/exit1이었으며 원로그를 보존하고 helper 추가 후 통과했다. 메인은 신규7-01의 2026-10-22 23:18:07–08 UTC PNG10개(네 종류 detail-minimum, 초기 두 크기 bottom, stale, 부분선택 비활성, style reset, 기본 top)와 같은 실행 bounds15의 핵심 필드를 직접 검토했다. 전체198-01의 UI 캡처 15 PNG/bounds 쌍은 23:20:16.394–23:20:17.878 UTC 별도 자동 산출물이며 직접 검토 세트가 아니다. 실행 로그와 상세 범위는 [괄호 종류 QA](verification/P2-bracket-kind-QA.md), [실행 manifest](verification/artifacts/p2-bracket-kind/bracket-kind-manifest.md)에 있다. 이 slice는 네 기존 괄호 종류의 선택·표시·소비 및 실제 `SelectedItem` binding에 한정된다. 팝업 포인터/OS 입력, 괄호 숨김·한쪽/좌우 분리 표시·직접 크기 조절, 외부 TeX, 나머지 P2/P1/P0 완료는 포함하지 않는다. 2026-10-22 23:09 UTC 감사는 절차 기록이며 실행 근거가 아니다.

- 이전 글꼴 요청 순서에서 재현된 기울임/숫자 face 차이는 Normal face 사전 확보와 제품 Italic→native Oblique 위임 후, 지정된 Windows/Avalonia 12.1.4 오프스크린 PNG와 같은 LayoutResult의 SVG·Firefox 소비 이미지에서 수정됨을 확인했다. 초기화 전 raw System 증거와 수정 후 `after-normal-face-warmup` 결과를 구분해 보존한다. 이 오프스크린 검증은 모든 기호의 픽셀 단위 동일성이나 실제 Windows 창 전부의 동일성을 확정하지 않는다.
- 현재 사용자는 컴퓨터를 계속 사용 중이며, 이 작업에서는 computer-use 도구·전경 창 표시·포커스 점유·물리 입력·UIA 자동화를 금지한다. 오프스크린 `RenderTargetBitmap`과 합성 컨트롤 이벤트로 가능한 동작을 검증하되, 이 결과는 물리 입력 검증으로 표시하지 않는다.
- 물리 편집 게이트(실제 포커스, Ctrl+V 성공/실패, 분모 안 중첩 분수의 부분 편집, 행렬 셀 편집, Ctrl+클릭 후 캡처 해제와 구조 삭제·undo)는 미검증 상태로 유지한다. UIA 명령·API 호출·오프스크린 렌더를 물리 입력 합격으로 대체하지 않는다.
- 다음 개발 단위는 논리 QA와 UI의 headless/offscreen QA를 완료하고 메인이 통합 승인한 뒤 진행한다. 실제 OS 물리 입력 게이트는 사용자 컴퓨터 사용 제약에 따라 별도 보류할 수 있으며, 이 보류를 합격으로 표시하지 않고 다음 단위 진행 승인과 구분한다.
- 2026-10-22 P1 행 편집 단위(추가·복제·삭제·같은 그룹 내 이동)의 Debug 통합 빌드는 경고 0·오류 0으로 끝났고, 새 논리 9건과 실제 `App`/`MainWindow`를 사용하는 Avalonia.Headless 12.1.4 + Skia UI 1건, 기존 회귀 50건을 포함한 전체 60/60이 최종 통과했다. 행별 저장 재열기, LaTeX/SVG 순서, 빈 필수 슬롯 출력 거절, undo/redo 및 비동기 paste stale을 확인했다. 상세 로그와 실패 이력은 `doc/verification/P1-row-editing-QA-plan.md` 및 `doc/verification/artifacts/p1-row-editing/`에 기록했다.
- Headless UI는 표시 전후 `STUB`/0 handle을 확인한 전용 child만 사용했으며, 1060×700과 1360×880 전체 셸 이미지/bounds, row command binding, 합성 클릭·Ctrl+Z/Y, 긴 문서 caret 노출을 확인했다. 첫 시각 검토에서 최소 크기 속성 패널 콘텐츠와 overlay scrollbar 간격을 개선해 내부 StackPanel 우측 inset 10px를 추가했고, 그 변경 뒤 Debug 빌드 및 전체 60/60을 다시 통과했다. 초기 0.186px caret clipping도 검출 후 엔진이 caret target 상하 2px 여유를 추가해 수정했고, 엄격한 실제 DrawCaret bounds 검사로 통과를 확인했다.
- 이 P1 결과는 논리/Headless/offscreen 범위만 판정한다. 실제 운영체제 창·포커스·UIA·물리 키보드/마우스 입력은 수행하지 않았으므로 기존 물리 편집 게이트는 계속 미검증이며, 위의 다음 단위 진행 기준에 따라 별도 보류할 수 있다.
- 2026-10-22 P1 그룹 편집 단위(그룹 추가·복제·삭제·순서 변경)는 Debug 통합 빌드 경고 0·오류 0, 신규 논리 9개+실제 `App`/`MainWindow` Avalonia.Headless+Skia UI 1개 통과, 전체 회귀 70/70으로 검증됐다. 실행 기록은 `doc/verification/P1-group-editing-QA-plan.md` 및 `doc/verification/artifacts/p1-group-editing/`에 보존한다. 테스트 정적 검토에서 undo/redo revision 단일 증가 기대, 신규 그룹 LaTeX 토큰(`NEW=6`, `B=3`), SVG 실제 문서 순서 검사를 바로잡은 뒤 실행했다. 메인이 시각 검토한 것은 GROUP10 실행에서 12:19 UTC에 생성된 초기/다수 그룹 화면 1060×700·1360×880 PNG 4개와 bounds다. 검토에서 그룹·행 도구 구분, 겹침/잘림 없음, 최소 viewport 257px 및 긴 행 끝 caret 노출을 확인해 이번 단위를 논리/headless/offscreen 범위에서 통합 승인했다. 전체70 실행 중 12:26 UTC에 다시 생성된 PNG 4개는 별도 최신 산출물이며 승인 근거로 혼동하지 않는다. 약 12:07–12:09 및 12:24–12:26 UTC 두 차례 Watchdog 감사 동안 작업을 정지했으며 중단급 문제는 없었다. 설정 읽기 오류 후 기존 로그와 프로세스 상태를 확인해 중복 실행을 피했고 사용자 설정 파일은 수정하지 않았다. 실제 OS 창·포커스·UIA·물리 입력은 계속 미검증이며, 이 승인으로 P1/P0 전체 완료를 뜻하지 않는다.
- 2026-10-22 P1 dirty New/Open/Close 파일 수명주기 보호는 Debug 통합 빌드 `build-lifecycle-final-02.log`에서 경고 0·오류 0, 신규 회귀 9/9, 전체 79/79로 통과했다. 메인이 신규9 실행에서 13:10:51–13:10:52 UTC에 생성된 초기/확인/긴 대상/저장 오류 Headless 화면 PNG 7개와 bounds를 검토해 겹침·잘림 없음, 긴 경로 2줄 표시, 오류 문구 가독성, 최소 viewport 257px을 확인하고 논리/Headless/offscreen 범위에서 통합 승인했다. 전체79 실행에서 13:11:19–13:11:20 UTC에 다시 생성된 화면 산출물 7개는 별도 최신 세트이며 시각 검토 근거는 신규9 세트다. 상세 결과와 실패 이력은 `doc/verification/P1-lifecycle-protection-QA.md`, 로그와 이미지/bounds는 `doc/verification/artifacts/p1-lifecycle-protection/`에 있다. 최초 빌드 `build-lifecycle-final-01.log`에서 QA 소유 두 비동기 span 비교가 CS4007을 냈고, 결과 byte array를 await 뒤 지역 변수로 분리해 수정한 다음 재빌드에 통과했다. 네이티브 OS 창·파일 선택기·첫 저장 picker·포커스·UIA·IME·물리 입력·다른 플랫폼은 미검증이며, 자동 복구와 최근 작업은 다음 P1 단위다. 이 결과는 전체 P0/P1 완료를 뜻하지 않는다.
- 2026-10-22 P1 명시 root 저장 서비스 단위에서 `WorkspaceStateService` recent index와 recovery envelope를 구현·검토했다. 최종 Debug 빌드 `build-workspace-state-02.log`는 경고0·오류0, 신규 8/8 및 기존79 포함 전체87/87이 통과해 메인이 저장 계층 단위를 통합 승인했다. 초기 `build-workspace-state-01.log`의 CS8620 경고와 초기 신규8 로그는 보존하고, private envelope nullable 요소 annotation 최소 수정 뒤 재빌드했다. 상세 결과는 `doc/verification/P1-recovery-recent-QA.md`, 모든 로그는 `doc/verification/artifacts/p1-workspace-state/`에 있다. fixtures/root는 테스트 `TempDirectory`만 사용하고 실제 사용자 AppData는 건드리지 않았다. 본 결과는 저장 계층에 한정하며 VM 자동 복구 타이머·복구 UI·최근 작업 UI는 미구현이다. mid-write 취소, ACL 메타데이터 실패 및 cross-process 동시성은 미검증이다.
- P1 lifecycle 뒤 일반 Save(A) 지연 중 Undo로 저장된 이전 StateId로 돌아간 뒤 New/Open/Close를 요청하는 경계를 same-session save ticket으로 보완했다. `LIFECYCLE-SAVE-UNDO`와 `LIFECYCLE-SAVE-CANCELLED-WRITE`가 Debug 빌드 경고0·오류0, 신규2/2 및 기존 저장 서비스 기준선 87개를 포함한 통합 전체89/89로 통과해 메인이 승인했다. 로그는 `doc/verification/artifacts/p1-lifecycle-save-ticket/`에 있다. 과거 lifecycle 9/79와 저장 service 8/87 결과는 당시 기준선으로 별도 보존하며, 89개 통합 결과가 범위 전체나 P1/P0 완료를 뜻하지 않는다.
- 2026-10-22 P1 명시 root 저장 서비스의 VM 자동 복구·복원·최근 작업 연결 단위는 최종 Debug 빌드05 경고0·오류0, 신규9/9, 전체98/98로 통과했다. 메인은 전체98 실행 중 14:43:45–14:43:47 UTC에 생성된 PNG 8개와 bounds를 검토해 1060×700·1360×880 화면의 초기·긴 미리보기·기본·최종 최소 상태에서 읽기 쉬운 필드와 접근성, 겹침 없음, STUB/0 및 최소 viewport 257px을 확인하고 이 P1 VM/UI 단위를 통합 승인했다. 로그는 `doc/verification/artifacts/p1-recovery-recent/build-p1-recovery-integration-final-05.log`, `recovery-recent-new-9-final-03.log`, `regression-98-final-01.log`에 있으며 실행 도구가 반환한 종료 코드0은 `doc/verification/artifacts/p1-recovery-recent/final-run-manifest.md`에도 기록했다. 이미지/bounds와 이전 실패 이력도 같은 artifact 경로에 보존한다. 저장 서비스 단위 8/87 및 직전 save-ticket 2/89는 당시 기준선으로 구분한다. QA는 자동 복구 tick·stop/initialize/session 교체 경합, snapshot·cleanup·dirty restore Save/Discard/Cancel, pending Restore 중 원본 보호, 성공한 Save/Open만 recent 갱신, pin/missing/relink/remove, WriteBlocked relink 원본 보존과 오래된 진단 차단을 검증했다. 두 컴파일 실패와 두 UI 실패도 이전 로그에 보존했다. 모든 파일은 임시 `TempDirectory`에 격리했다. 실제 사용자 AppData·네이티브 OS 창·파일 선택기·포커스·UIA·물리 입력은 미검증이며, 이 승인은 전체 P0/P1 완료를 뜻하지 않는다.
- 2026-10-22 약 14:04–14:06 UTC, 14:19–14:20 UTC 및 14:35–14:36 UTC Watchdog 감사 동안 모든 개발/검증을 정지했다. 감사 중 중단급 문제는 없었고 매번 종료 뒤 메인이 재개 범위를 명시했다. 마지막 감사 뒤 메인 승인으로 최종 빌드05·신규9·전체98 검증을 수행해 이 VM/UI slice를 통합 승인했다. 실제 OS 창·파일 선택기·포커스·UIA·물리 입력/computer-use는 금지 상태이며 전용 `STUB`/0 Headless child `Show()`만 허용했다. 물리 게이트는 합격으로 처리하지 않으며, 이후 비간섭 개발과 별도로 보류한다.
- IME 확정 문자열 입력과 preedit/candidate/caret rectangle 지원을 구분한다. 후자는 현재 구현·검증 완료 범위가 아니다.
- 전체 P0 엔진·문법·레이아웃·출력 계약과 P2 템플릿·팔레트·선택/속성·코드 입력, P3–P5의 긴 수식·페이지·PNG/PDF/MathML·클립보드 출력·홈/설정 기능은 해당 범위의 전체 완료가 아니다. P1 slice 승인 기록은 이 과거 F05 기술 단위와 분리해 추적한다.
- 요청된 이모티콘·유니코드 브라우저의 기호/Unicode 팔레트 하위 slice는 논리·Headless·offscreen 범위에서 승인됐다. 전체 palette 기능, 보편 glyph 지원과 OS 물리 검증은 완료로 판정하지 않는다.
- 2026-10-22 P2 10종 구조 템플릿 하위 단위가 논리/Headless/offscreen 범위에서 통합 승인됐다. 최종 Debug 빌드 `build-p2-templates-04.log`는 경고0·오류0, 신규 논리12+TEMPLATE-UI 1은 13/13, 기존98을 포함한 전체111은 111/111이며 각 실행 종료 코드는 0이다. 초기 XAML literal brace, QA fingerprint 익명 `Kind` 중복, 첫 실행 legacy JSON fixture의 `alignment`/`rowSpacing` 위치 오류 이력은 수정 근거와 함께 보존했다. 이미지 검토에서 delimiter가 내용 영역을 침범하고 Parentheses/Cases 형태 구분이 약한 점을 수정했으며, 흰 배경·실제 control bounds 기준으로 다시 캡처했다. 전체111 실행 중 15:30:56 UTC 셸 캡처 4개에서 `STUB`/0 및 최소·기본 viewport 257px/437px를, 15:30:58 UTC의 10종 수식 PNG/bounds에서 199×827·control bounds 일치·rowCount10을 메인이 확인했다. 상세 로그/산출물/실패 이력은 `doc/verification/P2-structure-templates-QA.md`와 `doc/verification/artifacts/p2-templates/`에 있다. 2026-10-22 15:23–15:24 UTC Watchdog 감사 중 전원 작업을 정지했고 중단급 문제는 없었다. 이 검증은 schema v1의 additive discriminator와 현재 serializer/layout 경계, 합성 키 및 offscreen 렌더에 한정한다. 구버전 앱 실행 호환, LaTeX 입력 parser, 7개 기호 분류, Properties/Structure 전체, 실제 사용자 AppData/OS 창·포커스·UIA·물리 입력, P2 전체 및 P0/P1 전체 완료는 주장하지 않는다.
- 2026-10-22 P2 기호·Unicode 팔레트는 최종 Debug 빌드04 경고0·오류0, 신규 논리8+`PALETTE-UI` 1개 9/9, 기존111을 포함한 전체120/120으로 통과했고 실제 종료 코드는 모두 0이다. 최초 빌드01의 Avalonia `TextBox.Watermark` obsolete 경고3개, `Qa.cs` 익명 속성 중복, legacy JSON fixture의 그룹 필드 위치 오류 및 첫 이미지 검토에서 발견한 괄호 경계/배경·마진 문제를 각각 보존·수정했다. 최종 로그는 `doc/verification/artifacts/p2-symbol-palette/build-symbol-palette-final-04.log`, `palette-new9-final-03.log`, `palette-full120-final-03.log`이며 최종 전체120 실행에서 16:23 UTC 생성된 별도 PNG/bounds 22쌍은 `palette-full120-final-03-artifacts.md`에 기록했다. 메인은 16:18:26–16:18:28 UTC의 신규9 대표 이미지10개(상세6·진단3·최소 Unicode 상단1)와 16:22:46–16:22:48 UTC의 신규9 대표 이미지6개(두 크기 구조 상·하단4, Unicode 상단2), 같은 run의 22개 bounds를 직접 검토했다. 검토한 PNG 6개와 bounds 경로는 `palette-new9-final-03-review-artifacts.md`에도 정리했다. 라디오 버튼 레이블에 우측11px inset을 적용한 뒤 inner bounds 안에 들고 scrollbar와 겹치지 않으며, STUB/0·패널210px·최소/기본 편집 viewport257px/437px임을 확인해 논리·Headless·offscreen 범위에서 이 slice를 통합 승인했다. 16:23 UTC 전체120의 별도 22쌍은 자동 산출물이며 메인이 그 전체 PNG를 직접 검토한 것은 아니다. 이전 상세 카드 여백·glyph 설명 잘림도 선택/진단 상태 포함 후속 이미지에서 읽히는 것을 확인했다. 실제 glyph 관측 `Cambria Math`의 `Present`는 표본 문자표 관측일 뿐 shaping/fallback 또는 보편 지원을 뜻하지 않는다. 네이티브 OS 창·포커스·UIA·물리 입력·IME·실제 사용자 AppData·구버전 앱 런타임은 미검증/금지이며, 이 slice는 P2/P0/P1 전체 완료가 아니다. 상세 실행 이력은 `doc/verification/P2-symbol-palette-QA.md`에 있다.
- 2026-10-22 P2 구조 탐색·Clear 범위 비우기 slice는 최종 Debug 빌드06 경고0·오류0, 신규 논리8+`STRUCTURE-CLEAR-UI` 1개 9/9, 기준선120을 포함한 전체129/129로 통합 승인됐다. 모든 단계의 실제 종료 코드는 0이다. 첫 빌드의 Avalonia 미지원 `TreeViewItem.HorizontalContentAlignment`, 두 nullable 경고, Tree header containment 실패 2회, `TranslatePoint` using 누락, 최소창 Tree viewport 85px 실패 이력을 원로그와 함께 보존하고 수정 뒤 최종 실행했다. 신규9 실행은 초기/root 선택/populated 최소·기본/deep 최소·기본/leaf 선택의 UI PNG/bounds 7쌍을 생성했다. 메인은 17:13:50–17:13:52 UTC 신규9 세트를 직접 확인해 header 가시성, Tree 높이159px/339px, 52px 비우기 버튼 레이블, STUB/0를 검토했다. 전체129 실행에서 17:14:24–17:14:26 UTC에 재생성된 7쌍은 별도 자동 산출물이며 전부 직접 시각 검토한 것은 아니다. 로그·실패 이력·분리 manifest는 `doc/verification/P2-structure-clear-QA.md` 및 `doc/verification/artifacts/p2-structure-clear/`에 있다. Properties 단위의 최종 구현·검증·승인 근거는 이 절 다음의 별도 기록에 정리했다. Typesetting 및 Display/Inline은 후속 범위다. 네이티브 OS 창·포커스·UIA·물리 입력·IME, 사용자 AppData, 전체 P2/P1/P0 완료는 미검증 또는 미완료로 남긴다.

- 2026-10-22 P2 Properties 기본 단위는 크기·글꼴·색 세 필드와 SelectionNode/CurrentRow/CurrentGroup/WorkspaceDefault 네 scope로 한정해 통합 승인했다. 상속 출처·원본/effective 값, 부분 텍스트 선택 안내, draft/apply/reset, stale 거절, 단일 undo 및 caret/selection 보존을 논리8+Headless `PROPERTIES-UI`1로 검증했다. 최종 Debug 빌드05는 경고0·오류0, 신규9는 9/9, 전체 회귀138은 138/138이며 모든 실행 exit0이다. 메인은 신규9-04의 17:57:23–17:57:25 UTC PNG/bounds 8쌍을 직접 검토해 STUB/0, Properties panel 224px, 중앙 viewport 257px/437px, WorkspaceDefault 값 표시, invalid 안내 및 stale-bottom 사유의 viewport 내 표시를 확인했다. 전체138-01에서 17:58:03–17:58:05 UTC에 생성된 별도 PNG/bounds 8쌍은 자동 산출물이며, 이를 전체 직접 시각 검토 근거로 사용하지 않는다. 초기 build01의 QA 컴파일 오류4, build02/new9-01의 `PropertiesUnitsNotice` 이름 실패, build03/new9-02의 `FontSizeSourceLabel` viewport containment 실패는 원로그로 보존했고 이후 동결 UI에서 수정했다. `Program.cs` route와 `Qa.cs`의 node/row/group style fingerprint를 포함한다. 빈 patch/공백 draft의 null patch, Core 직접 공백 글꼴 거절, WorkspaceDefault의 실제 root style 표시, 여섯 빈 템플릿 content의 ID/style/focus/undo, baseline 크기 600·후보 측정 실패와 Unknown glyph advisory 경계도 검증했다. 2026-10-22 17:24·17:39·17:54 UTC 감사는 절차 기록이며 테스트 근거가 아니다. 네이티브 OS 창/입력 및 사용자 AppData는 사용하지 않는다. Typesetting 및 Display/Inline은 후속 범위다.
- 2026-10-22 P2 기본 조판 preset 하위 단위는 Display/Text/Script/ScriptScript 상속·레이아웃과 LaTeX/SVG 소비 경계를 논리8+Headless `TYPESETTING-UI`1 및 기존138을 포함한 전체147 회귀로 검증해 통합 승인했다. 최종 Debug 빌드 `verification/artifacts/p2-typesetting/build-typesetting-final-05.log`는 경고0·오류0, `typesetting-new9-final-03.log`는 9/9, `typesetting-full147-final-03.log`는 147/147이며 세 단계 실제 exit는 모두 0이다. 전체147 첫 실행은 `typesetting-full147-final-02.log`이며 `final-01` 로그는 없다. 초기 실패 이력은 build01의 CS0173(경고0/오류1), build02의 QA CS0029 및 UI nullable/Length 컴파일 오류(경고1/오류2), new9-01의 TYPE-JSON fixture 위치·TYPE-OPERATORS Display 단독 한계 기대·TYPESETTING-UI 안내 assertion 실패, full147-02의 기존 LaTeX 기대 6건 불일치다. QA는 fixture 소유 위치와 Display/compact 한계 배치를 바로잡고, 기존 LaTeX 6개(`LATEX-NESTED-SCRIPT`, `ROW-IO`, `GROUP-IO`, `TPL-BOUNDS`, `TPL-BRACKETS-CASES`, `TPL-LATEX`)의 기대를 기본 Display 포함 정확한 scope/group 문자열로 갱신했으며 의미 검사는 유지했다. 엔진은 TargetSummary baseline capture 성공 경로의 알림을 보완했고 UI는 실제 표시 TextBlock과 TargetSummary 일치 단언을 추가했다. 로그·실패 이력·실행별 산출물은 [P2 조판 QA](verification/P2-typesetting-QA.md), [신규9-02 직접 검토 manifest](verification/artifacts/p2-typesetting/typesetting-new9-final-02-review-artifacts.md), [신규9-03 직접 검토 manifest](verification/artifacts/p2-typesetting/typesetting-new9-final-03-review-artifacts.md), [전체147-03 자동 산출물 manifest](verification/artifacts/p2-typesetting/typesetting-full147-final-03-artifacts.md)에 있다. 메인의 직접 시각 검토 근거는 신규9-02(2026-10-22 18:31:28–29 UTC)의 셸 PNG10/bounds10 및 비교식 PNG4/JSON, 신규9-03(18:36:24 UTC)의 `final-default-top`·`detail-row-script-minimum`·`stale-minimum-bottom` PNG3 및 같은 실행 bounds10이다. 전체147-03(18:37 UTC)의 PNG14와 JSON은 별도 자동 산출물이며 메인이 전부 직접 검토한 것으로 보지 않는다. 신규9-03에서는 TargetSummary의 그룹1·5행 표시, stale 사유, Apply 비활성/ResetDraft 활성, STUB/0, panel224px·중앙 viewport257px/437px을 확인했다. 2026-10-22 18:09·18:24·18:39 UTC 감사 동안 전원 작업을 안전 정지했고 중단급 문제는 없었으며 각 감사 종료 뒤 명시적으로 재개했다. 이번 승인은 3.3-01의 기본 수학 조판 preset과 검증된 소비자 경계에 한정된다. 외부 삽입 Display/Inline 출력 옵션·미리보기, 직접 LaTeX 입력/parser, 실제 TeX 프로그램 컴파일, 구버전 앱 런타임, 네이티브 OS 창·입력·포커스·IME·UIA 및 사용자 AppData는 미검증/금지이며, P2/P1/P0 전체 완료로 확대하지 않는다.
- 2026-10-22 P2 외부 Display/Inline 전체 작업 SVG 미리보기 하위 단위도 논리8+Headless `OUTPUT-PREVIEW-UI`1과 기존 회귀를 포함한 전체156으로 통합 승인했다. Debug build04는 경고0·오류0, 신규9는 9/9, 전체156은 156/156이며 모두 실제 exit0이다. 최초 build01은 경고2·오류0·exit0이었고 build02/03은 경고0·오류0·exit0으로 재확인했다. new9-01은 7/9(exit1)로 다중 glyph primitive 단일화 가정과 UI 도구 containment에서 실패했고, new9-02는 8/9(exit1)로 Avalonia 12.1.4 기본 layout rounding 때문에 자연 DIP 크기 equality 검사에 실패했다. helper는 공식 `Layoutable.cs`의 `UseLayoutRounding` 및 `RoundLayoutSizeUp` 경계를 반영해 control 크기가 frozen layout 이상이면서 차이가 1 DIP 미만임을 검사하도록 바꾸고 동일 LayoutResult 참조 검사는 유지했다. 최종 로그와 두 실패 이력은 [P2 출력 미리보기 QA](verification/P2-output-preview-QA.md)와 [실행 manifest](verification/artifacts/p2-output-preview/output-preview-final-manifest.md)에 있다. 메인은 신규9-03 19:17:48 UTC의 PNG/bounds 6쌍을 직접 검토해 두 창 크기, WorkspaceStyle/Display/Inline, stale/incomplete 진단, SVG 저장 가능 여부, 탭·도구 비겹침, STUB/0 및 preview viewport를 확인했다. 전체156-01 19:20:01–02 UTC의 별도 PNG/bounds 6쌍은 자동 산출물이며 신규9 직접 검토 근거와 구분한다. 19:09 UTC 정기 감사 중 작업을 정지했고 중단급 문제 없이 종료 후 재개했다. 이 승인은 외부 Display/Inline SVG 미리보기의 논리·Headless/offscreen 경계에 한정된다. 실제 OS 창·입력·포커스·UIA·clipboard·사용자 AppData, 외부 TeX, 구버전 앱, 나머지 P2와 전체 P1/P0는 미검증/미완료다.
- 2026-10-22 P2 의미 텍스트 역할(§3.3-03) 하위 단위가 통합 승인됐다. 논리8+Headless `TEXT-ROLE-UI`1 및 기준선156을 포함한 전체165가 통과했고, 최종 Debug 빌드05·신규9-03·전체165-01은 모두 exit0이다(빌드 경고0·오류0, 신규9/9, 전체165/165). 실행 이력은 [P2 텍스트 역할 QA](verification/P2-text-role-QA.md)와 [최종 manifest](verification/artifacts/p2-text-role/text-role-final-manifest.md)에 있다. build01의 nullable 경고, 신규9-01 UI 실패, build03 CS1503 및 신규9-02 전용 expanded 캡처 흰 이미지 이력을 보존했다. 메인은 신규9-02의 shell9/bounds9와 비교 PNG4/JSON, 신규9-03의 expanded diagnostic 및 stale/Number-draft/diagnostic-collapsed 캡처와 bounds9를 직접 검토했다. full165-01 캡처는 별도 자동 산출물이며 전체를 직접 검토한 것으로 기록하지 않는다. 확장 코드 패널은 source90px/grid143px, 접힌 viewport257px에서 expanded viewport81px로 176px 감소했고 재접힘 후 257px로 복귀했다. 결합 grapheme 등호는 Automatic의 일반 입력/단독 등호 호환성과 구분되는 raw 문자 검색 수정 예외다. 19:39·19:54·20:09 UTC 감사는 절차 기록이다. 이 승인은 논리·Headless·offscreen 하위 범위에 한정하며 실제 OS/IME/clipboard/AppData, 외부 TeX/parser, 구버전 앱, LaTeX role provenance 역변환, 전체 P2/P1/P0 완료를 포함하지 않는다.
- 2026-10-22 P2 분수 Display pin/dfrac 하위 단위는 논리5+Headless `DFRAC-PIN-UI`1, 기존 회귀를 포함한 전체171/171로 통합 승인됐다. build03 경고0·오류0, new6-02 6/6, `TYPESETTING-UI` 단독1/1, full171-02 171/171이며 실제 exit는 모두0이다. 초기 new6-01은 UI helper의 세로 여유가 가로에도 적용돼 5/6으로 실패했고, full171-01은 기존 TYPESETTING-UI ComboBox viewport 검사로 170/171을 기록했다. helper 수정 후 단독 UI와 full171-02가 통과했다. 메인은 new6-02의 12 PNG/bounds 쌍과 단독 TYPESETTING-UI의 상세 Display 최소·Row Script 최소·stale 최소 PNG3/bounds10을 직접 검토했다. full171-02의 별도 Dfrac PNG/bounds 12쌍은 자동 산출물이다. 상세 이력과 실행별 구분은 `verification/P2-dfrac-pin-QA.md`, `verification/artifacts/p2-dfrac-pin/dfrac-pin-final-manifest.md`에 있다. 확인 범위는 Fraction-local explicit Display pin, 결합 Apply/Undo, serializer의 `\dfrac`, layout/SVG 및 임시 파일/recovery 경계다. 실제 TeX 실행, OS 입력·clipboard·사용자 AppData, 전체 P2/P1/P0는 검증 범위가 아니다.
- 2026-10-22 그룹 행 간격 사용자 편집 하위 단위가 논리·Headless·offscreen 범위에서 통합 승인됐다. 기존 `EquationGroup.RowSpacing` 모델/저장 경계를 재사용하며, 새로 확인한 범위는 `CurrentGroup` 편집·0–512 유한값 검증, 그룹 내부 geometry, LaTeX source 표현 손실을 알리는 비차단 layout advisory(완성 코드 차단과 구분), frozen preview/SVG 소비다. 최종 build06 경고0·오류0/exit0, 신규 논리9+UI1 10/10, 기존 `TEXT-ROLE-UI` 단독02 1/1, 전체181-02 181/181이며 모두 실제 exit0이다. 메인은 신규10-02의 bounds22 전체, shell PNG10 및 비교 PNG2/JSON을 21:41:46–47 UTC에 직접 검토했다. 이어 기존 `TEXT-ROLE-UI` 단독02의 diagnostic PNG3/JSON을 21:50:23 UTC에 직접 검토했고, 전체181-02의 22 UI쌍과 비교 PNG2/JSON은 별도 자동 산출물로 구분한다. 실패 이력에는 초기 viewport 좌표 변환 및 phase-label helper 오류, 활성 행 cache 갱신 전 알림이라는 Core 결함과 `SESSION-ACTIVE-TARGET-NOTIFY` 회귀 추가, 기존 `TEXT-ROLE-UI` 재접힘 viewport/진단 scroll 실패가 포함되며 원로그를 보존했다. 세부 결과와 검토 구분은 [그룹 행 간격 QA](verification/P2-group-row-spacing-QA.md) 및 [실행 manifest](verification/artifacts/p2-group-row-spacing/group-row-spacing-final-manifest.md)에 있다. 21:09·21:24·21:39·21:56 UTC 감사는 절차 기록이다. 21:56 UTC 감사 중 QA는 작업을 멈추고 대기했으며, 감사 종료 뒤 기록 시각만 정정했다. 기존 600의 보존은 고정 legacy 경계로 한정되며 누적 overflow나 모든 큰 유한 간격을 검증하지 않았다. 실제 OS/IME/clipboard/AppData, 외부 TeX, 전체 행 간격 정책과 전체 P2/P3/P0/P1 완료는 포함하지 않는다.
- 2026-10-22 P2 얇은 수학 공백(`MathSpaceKind.Thin`) 하위 단위가 기준선181에 논리9+Headless `THIN-MATH-SPACE-UI`1을 더한 전체191/191로 통합 승인됐다. build07은 경고0·오류0·exit0, UI03은 1/1·exit0, 전체191-01은 191/191·exit0이며 신규10도 전체 실행에서 재확인 통과했다. build01–03 컴파일 실패, new10-01의 9/10 자연폭 helper 실패, UI 단독01/02의 Ctrl+클릭 선택 및 실제 입력 라우팅 진단 이력은 원로그와 함께 보존했다. 엔진의 editor 전체 bounds 투명 fill과 UI의 입력 target/event 회귀 관측 뒤 UI03이 통과했다. 메인은 UI03 22:50 UTC 세트에서 shell PNG9와 bounds12의 핵심 필드, pointer JSON1/layout JSON1/preview JSON2를 직접 검토했다. 나머지 shell PNG3와 별도 state 캡처는 직접 검토하지 않았다. full191-01의 22:55:39–40 UTC PNG24/JSON27은 별도 자동 산출물이며 직접 리뷰 세트와 분리한다. 실행 이력·검토 범위는 [얇은 공백 QA](verification/P2-thin-math-space-QA.md)와 [manifest](verification/artifacts/p2-thin-math-space/thin-math-space-manifest.md)에 기록했다. 22:26·22:41 및 회귀 직전 조기 22:54 UTC 감사는 절차 기록뿐이다. 이번 승인은 Thin 하나의 논리·Headless·offscreen 범위다. 구버전 앱의 새 discriminator 읽기, 파일 읽기 자동 정규화, 일반 행/그룹 간격 정책, TextNode 분할 후 reshaping 폭 및 외부 TeX 폭 일치, 실제 OS/IME/물리 입력/clipboard/AppData는 미검증이며 전체 P2/P1/P0 완료가 아니다.
