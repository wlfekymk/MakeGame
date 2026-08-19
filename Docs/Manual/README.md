# 플레이어용 생존 안내서 — 원본

`manual.html` + `style.css` 를 헤드리스 크로미움으로 인쇄해 PDF를 만든다.

```
python3 Docs/Manual/render.py     # → LOST_SURVIVOR_생존안내서_vX.pdf
```

## 왜 HTML인가
표가 많고(무기 피해·제작법 33종·건축 티어) 한글 조판이 필요해서, CSS 인쇄 규칙이
reportlab보다 압도적으로 싸게 먹힌다. 크로미움은 이미 컨테이너에 있다
(`/opt/pw-browsers/chromium`, `PLAYWRIGHT_BROWSERS_PATH` 설정됨 — 새로 받지 마라).

## 고칠 때 지킬 것
- **수치는 전부 코드/에셋 실측값이다.** 밸런스를 바꿨으면 여기 숫자도 같이 고쳐라.
  특히 잘 어긋나는 곳: 생존 수치 5종(씬 값이 코드 기본값을 덮어쓴다), 제작법 33종,
  무기 피해, 엔딩 조건 일수·비축량,
  **맵 배치**(월드 크기·섬 간 거리·시작 섬 위치·상어 마릿수 — `MaldivesLayout.cs` 머리 주석이
  단일 소스이고, 상어는 `SharkSpawner.ResolveSharkCount`가 분포 폭에 비례해 자동으로 늘린다),
  **특대 섬 이동 조건**(`IslandTravel.currentBypassRequirement` — 지금은 대양 규격 + 모터.
  8·10·12·13장이 한꺼번에 거짓말이 되므로 이 값이 바뀌면 네 장을 전부 다시 봐라).
- 문서(`Docs/Design_*.md`)가 아니라 **코드가 정답**이다. 이 안내서를 쓸 때 실측한 결과,
  Design_Ending(엔딩 2종·비축 30/30) · Design_Progression(섬 9개)은 전부 낡은 값이었다.
  실제는 엔딩 3종 · 비축 12/12/1 · 섬 50개다.
- **디버그·치트 키는 일부러 뺐다.** 플레이어용 문서다.
- 표지 높이 259mm는 A4에서 크로미움 기본 여백을 뺀 값이다. 여백을 바꾸면 같이 고쳐라.
