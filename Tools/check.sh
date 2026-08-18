#!/usr/bin/env bash
# check.sh — 작업 종료 시 딱 한 번 돌리는 통합 검증. 성공하면 한 줄만 출력한다.
#
# 왜 한 줄인가: 에이전트가 뱉은 출력은 그 뒤 모든 턴에서 다시 처리된다(실측 평균 70배).
# 검사 결과가 200줄이면 그 200줄을 70번 다시 읽는다. 성공은 "OK" 한 줄이면 충분하고,
# 실패했을 때만 원인을 보여주면 된다.
#
#   Tools/check.sh            # 전체 검사
#   Tools/check.sh -v         # 실패하지 않아도 각 단계 결과를 보여준다(디버그용, 평소 쓰지 말 것)
#
# 종료 코드 0 = 전부 통과. 그 외 = 실패(원인이 출력된다).
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2
VERBOSE=0; [ "${1:-}" = "-v" ] && VERBOSE=1
FAIL=0
say() { [ "$VERBOSE" = 1 ] && echo "$1"; return 0; }

SRC=$(find Assets/_Project/Scripts -name '*.cs' 2>/dev/null)
N=$(echo "$SRC" | grep -c . )

# 1) 정적 게이트: 괄호 짝 / #if-#endif / OnGUI 금지
G=$(python3 Tools/agent/gate.py $SRC 2>&1)
if ! echo "$G" | grep -q 'STATIC GATE: PASS'; then
  echo "== STATIC GATE 실패"; echo "$G" | grep -v 'STATIC GATE' | head -20; FAIL=1
else say "gate ok"; fi

# 2) 구문 파싱 (mcs가 있을 때만)
if command -v mcs >/dev/null 2>&1; then
  P=$(mcs --parse -langversion:latest $SRC 2>&1)
  PE=$(echo "$P" | grep -c 'error CS' || true)
  if [ "$PE" != "0" ]; then
    echo "== 파싱 오류 ${PE}건"; echo "$P" | grep 'error CS' | head -10; FAIL=1
  else say "parse ok"; fi
else say "mcs 없음 - 파싱 생략"; fi

# 3) 금지 API
# grep 출력이 "파일:줄:내용" 이라 주석 판정은 줄 번호 뒤부터 봐야 한다(^ 로는 못 잡는다).
B=$(grep -rn 'GetInstanceID()\|void OnGUI[[:space:]]*(' --include=*.cs Assets/_Project/Scripts 2>/dev/null \
    | grep -v ':[0-9]*:[[:space:]]*//' | head -10)
if [ -n "$B" ]; then echo "== 금지 API"; echo "$B"; FAIL=1; else say "banned-api ok"; fi

# 4) 버전 일치 (VERSION 파일 vs MainMenuController.DisplayVersion)
V=$(cat VERSION 2>/dev/null | tr -d ' \n')
D=$(grep -o 'DisplayVersion = "[^"]*"' Assets/_Project/Scripts/UI/MainMenuController.cs 2>/dev/null | head -1 | sed 's/.*"\(.*\)"/\1/')
if [ -n "$V" ] && [ "$V" != "$D" ]; then
  echo "== 버전 불일치: VERSION=$V DisplayVersion=$D"; FAIL=1
else say "version ok ($V)"; fi

[ "$FAIL" = 0 ] && echo "CHECK OK (${N} files, v${V})" || echo "CHECK FAILED"
exit $FAIL
