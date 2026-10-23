# 구조 탐색·비우기 신규9 직접 검토 산출물

- 실행: `structure-clear-new9-final-04.log`, 2026-10-22 17:13:50–17:13:52 UTC, exit 0, 신규9/9.
- 직접 검토: 메인이 아래 7개 PNG와 7개 bounds 파일을 확인했다. root/leaf 단일 선택 표시, 깊은 header 가시성, 최소/기본 Tree viewport(159px/339px), `비우기` 버튼의 세 글자 표시 및 `STUB`/0을 확인했다.
- 경계: 이 세트는 신규9 실행의 UI 근거다. 전체129 실행에서 생성된 별도 산출물은 [별도 자동 산출 manifest](structure-clear-full129-final-01-artifacts.md)에 기록했으며, 전체129 세트를 직접 검토했다고 주장하지 않는다.

## 화면과 bounds

| 상태 | 화면 크기 | PNG | bounds |
|---|---:|---|---|
| 초기 트리 | 1060×700 | [main-window.png](structure-initial-minimum-1060x700-20261022T171350146Z-25b90868b050420799599b51ab8dc7e5/main-window.png) | [bounds.json](structure-initial-minimum-1060x700-20261022T171350146Z-25b90868b050420799599b51ab8dc7e5/bounds.json) |
| root 전체 선택 | 1060×700 | [main-window.png](structure-root-selection-minimum-1060x700-20261022T171350267Z-2f6db772549542489472fbc218175fc2/main-window.png) | [bounds.json](structure-root-selection-minimum-1060x700-20261022T171350267Z-2f6db772549542489472fbc218175fc2/bounds.json) |
| 여러 행 채움 | 1060×700 | [main-window.png](structure-populated-minimum-1060x700-20261022T171350689Z-1ef1a0f6cc1b4a32b64c0ecd9492672f/main-window.png) | [bounds.json](structure-populated-minimum-1060x700-20261022T171350689Z-1ef1a0f6cc1b4a32b64c0ecd9492672f/bounds.json) |
| 여러 행 채움 | 1360×880 | [main-window.png](structure-populated-default-1360x880-20261022T171350720Z-965367f603164a88a635a40bb9ac3e54/main-window.png) | [bounds.json](structure-populated-default-1360x880-20261022T171350720Z-965367f603164a88a635a40bb9ac3e54/bounds.json) |
| 깊은 caret | 1060×700 | [main-window.png](structure-deep-minimum-1060x700-20261022T171352006Z-471bcfd8115946debb5f4324001fb88b/main-window.png) | [bounds.json](structure-deep-minimum-1060x700-20261022T171352006Z-471bcfd8115946debb5f4324001fb88b/bounds.json) |
| 깊은 caret | 1360×880 | [main-window.png](structure-deep-default-1360x880-20261022T171352035Z-d05ecfebefe847ccb656c3c5e45cba84/main-window.png) | [bounds.json](structure-deep-default-1360x880-20261022T171352035Z-d05ecfebefe847ccb656c3c5e45cba84/bounds.json) |
| leaf 선택 | 1360×880 | [main-window.png](structure-leaf-selection-default-1360x880-20261022T171352086Z-6fd113c426284bd883e6bdded1079c0b/main-window.png) | [bounds.json](structure-leaf-selection-default-1360x880-20261022T171352086Z-6fd113c426284bd883e6bdded1079c0b/bounds.json) |

## 실행 로그

- [Debug build](build-structure-clear-final-06.log): 경고0·오류0, exit0.
- [신규9 회귀](structure-clear-new9-final-04.log): 9/9, exit0.
