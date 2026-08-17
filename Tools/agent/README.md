# 디렉터 도구 (에이전트는 이 폴더를 쓰지 않는다)

## lock_audit.py — 웨이브 락 감사

실제 사고 2건(락 밖 수정 미검출, 중단된 에이전트의 부분 수정 잔류 → CS0117)에서 나온 도구.

웨이브 의식에 편입된 사용 순서:

1. **웨이브 시작 전** `python3 Tools/agent/lock_audit.py snapshot`
2. 에이전트 파견 (락 목록을 브리핑에 적은 것과 동일하게 보관)
3. **웨이브 종료 후** `python3 Tools/agent/lock_audit.py audit <브리핑에 적은 허용 경로들>`
   - 종료 코드 1이면 배포 금지. 위반 파일을 커밋본에서 복원하거나 락 목록 오류를 기록.
4. **사용자가 에이전트를 중단시켰으면 즉시** `audit` (허용 0개) — 부분 수정 잔류 검출.

## 작업본-실물 지문 대조 (배포 전)

작업본(/tmp/work)이 실제 PC와 어긋난 채 웨이브를 돌면 에이전트가 낡은 코드를 읽는다
(실제 사고: 낡은 씬을 읽고 "곰 보장 스폰 설정이 없다"고 오보).

양쪽에서 같은 명령을 돌려 해시가 일치해야 웨이브를 시작한다:

```sh
(find Assets/_Project/Scripts -name '*.cs'; echo Assets/Scenes/SampleScene.unity; echo VERSION) \
  | sort | xargs md5sum | sed 's#  #|#' | md5sum
```

주의: 작업본은 부분 사본이다(.cs와 씬만 동기화). `.meta`·프리팹·`Art/` 등은 없는 게 정상이며,
"작업본에 없다"를 "프로젝트에 없다"로 읽으면 안 된다(CLAUDE.md 위임 규칙 8).
