#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
락 감사 (디렉터 전용 도구 — 클라우드 작업본에서 돌린다)

무인/유인 웨이브에서 실제로 난 사고 두 종류를 기계적으로 잡는다:
 1. 락 위반  — 에이전트가 허용 목록 밖 파일을 고친 경우
              (실제 사고: 상자 배치에서 BuildMenuUI 누락은 반대로 '락이 모자란' 경우였지만,
               위반 검출이 있어야 목록 자체를 신뢰하고 논의할 수 있다)
 2. 중단 오염 — 사용자가 에이전트를 중단시켰을 때 죽기 전 남긴 부분 수정
              (실제 사고: 섬 축소 에이전트의 WorldScale 잔해가 다음 배치에 실려 CS0117 2건)

사용법 (웨이브 의식):
  웨이브 시작 전:  python3 Tools/agent/lock_audit.py snapshot
  웨이브 종료 후:  python3 Tools/agent/lock_audit.py audit <허용경로...>
                   허용경로는 파일 경로 그대로 또는 글롭("Assets/**/Systems/Foo*.cs").
                   신규 파일도 허용 목록에 맞아야 한다.
  중단 직후 점검:  python3 Tools/agent/lock_audit.py audit   (허용 0개 = 변화가 있으면 전부 위반)

종료 코드: 0 = 깨끗함 / 1 = 위반 있음 / 2 = 스냅샷 없음 등 사용 오류.
스냅샷은 /tmp/mg_snapshot.json (저장소 밖 — 커밋되지 않는다).
"""
import fnmatch
import hashlib
import json
import os
import sys

SNAPSHOT_PATH = "/tmp/mg_snapshot.json"

# 감시 대상: 저장소에서 의미 있는 것 전부.
WATCH_ROOTS = ["Assets", "Tools", "Docs", "VERSION", "CLAUDE.md", "README.md"]
EXCLUDE_DIRS = {".git", "Library", "Temp", "Logs", "_incoming", "_to_delete",
                "__pycache__", "_preview", "UserSettings", "obj"}
EXCLUDE_SUFFIX = (".png",)  # 렌더 미리보기 등 — 산출물은 스크립트가 진실


def repo_root():
    # 이 파일 위치 기준 두 단계 위 = 저장소 루트
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def iter_files(root):
    for base in WATCH_ROOTS:
        path = os.path.join(root, base)
        if os.path.isfile(path):
            yield os.path.relpath(path, root)
            continue
        for dirpath, dirnames, filenames in os.walk(path):
            dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
            for name in filenames:
                if name.endswith(EXCLUDE_SUFFIX):
                    continue
                yield os.path.relpath(os.path.join(dirpath, name), root)


def hash_tree(root):
    result = {}
    for rel in iter_files(root):
        h = hashlib.sha256()
        with open(os.path.join(root, rel), "rb") as f:
            for chunk in iter(lambda: f.read(1 << 16), b""):
                h.update(chunk)
        result[rel.replace(os.sep, "/")] = h.hexdigest()
    return result


def allowed(rel, patterns):
    return any(fnmatch.fnmatch(rel, p) or rel == p or rel.startswith(p.rstrip("*").rstrip("/") + "/")
               for p in patterns)


def main():
    root = repo_root()
    if len(sys.argv) < 2 or sys.argv[1] not in ("snapshot", "audit"):
        print(__doc__)
        return 2

    if sys.argv[1] == "snapshot":
        tree = hash_tree(root)
        with open(SNAPSHOT_PATH, "w", encoding="utf-8") as f:
            json.dump(tree, f)
        print(f"스냅샷 저장: {len(tree)}개 파일 -> {SNAPSHOT_PATH}")
        return 0

    # audit
    if not os.path.exists(SNAPSHOT_PATH):
        print("오류: 스냅샷이 없다. 웨이브 시작 전에 snapshot을 먼저 떠라.")
        return 2
    with open(SNAPSHOT_PATH, encoding="utf-8") as f:
        before = json.load(f)
    after = hash_tree(root)
    patterns = sys.argv[2:]

    changed = sorted(r for r in after if r in before and after[r] != before[r])
    added = sorted(r for r in after if r not in before)
    deleted = sorted(r for r in before if r not in after)

    violations = []
    for rel in changed:
        if not allowed(rel, patterns):
            violations.append(("수정", rel))
    for rel in added:
        if not allowed(rel, patterns):
            violations.append(("신규", rel))
    for rel in deleted:
        # 삭제는 어떤 락으로도 정당화되지 않는다(이 워크플로에서 에이전트 삭제는 항상 사전 합의)
        if not allowed(rel, patterns):
            violations.append(("삭제", rel))

    print(f"변경 {len(changed)} / 신규 {len(added)} / 삭제 {len(deleted)}"
          f" (허용 패턴 {len(patterns)}개)")
    if violations:
        print("\n★ 락 위반 또는 중단 오염 의심 ★")
        for kind, rel in violations:
            print(f"  [{kind}] {rel}")
        print("\n조치: 커밋본에서 해당 파일을 복원하거나, 락 목록이 틀렸다면 목록을 고치고 기록해라.")
        return 1
    print("깨끗함 - 허용 목록 밖 변경 없음.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
