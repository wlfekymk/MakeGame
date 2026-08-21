using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MakeGame.Systems;
using UnityEditor;
using UnityEngine;

namespace MakeGame.EditorTools
{
    /// <summary>
    /// 뗏목 다중 배치 검증 하네스(에디터 전용).
    ///
    /// 0.2.63에서 뗏목이 싱글턴에서 명부(registry) 구조로 바뀌었지만, 건축 메뉴 연결(4단계)이
    /// 아직 없어 게임 안에서 두 번째 뗏목을 세울 방법이 없다. 이 하네스가 그 자리를 대신해
    /// Create / PlaceAt / IsValidSite / 저장-불러오기 왕복을 실제 플레이 중에 직접 두들긴다.
    ///
    /// 사용법: 플레이 모드로 진입해 **새 게임을 시작한 뒤**(월드와 첫 뗏목이 서 있어야 한다)
    /// 메뉴 [MakeGame/뗏목 다중 배치 테스트]를 누른다. 결과는 프로젝트 루트
    /// raft_multi_test.json에 적힌다.
    ///
    /// 도메인 리로드를 타지 않는다(플레이 진입/이탈을 하지 않으므로). 그래서 SmokeTest처럼
    /// SessionState로 상태를 보존할 필요가 없고, 평범한 static 상태 기계로 충분하다.
    ///
    /// 세이브 슬롯: 테스트는 마지막 슬롯을 쓴다. 원본 파일이 있으면 먼저 옆으로 복사해 두고
    /// 끝난 뒤 되돌린다(테스트가 사용자의 저장을 먹지 않게).
    /// </summary>
    [InitializeOnLoad]
    public static class RaftMultiTest
    {
        private const string ResultFileName = "raft_multi_test.json";

        /// <summary>
        /// 방아쇠 파일. 프로젝트 루트에 이 이름의 파일이 생기면 테스트가 시작된다.
        ///
        /// [왜 메뉴만으로는 부족한가] 플레이 중에는 게임 뷰가 커서를 잡아 상단 메뉴 클릭이 먹지 않는다
        /// (이 프로젝트에서 반복해서 겪은 일이다). 파일 하나로 시작할 수 있으면 화면을 만지지 않고도
        /// 원격에서 테스트를 돌릴 수 있다.
        /// </summary>
        private const string TriggerFileName = "raft_multi_test.trigger";

        private const int TestSlot = 3;

        private static double nextPollAt;

        static RaftMultiTest()
        {
            // 도메인 리로드마다 다시 건다. 1회성 플래그를 쓰지 않는 이유는 RaftStructure에서 겪은
            // 그대로다 - 플래그는 살아남는데 구독은 끊겨서 두 번째부터 아무 일도 안 일어난다.
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        /// <summary>
        /// 방아쇠 파일을 0.5초마다 살핀다. **월드가 실제로 준비된 뒤에만** 발동한다 -
        /// 플레이 진입 직후 프레임에는 뗏목도 플레이어도 아직 없기 때문이다.
        /// 그래서 방아쇠를 미리 놓아 두고 새 게임을 시작해도 알아서 제때 돈다.
        /// </summary>
        private static void Poll()
        {
            if (running)
                return;

            if (EditorApplication.timeSinceStartup < nextPollAt)
                return;

            nextPollAt = EditorApplication.timeSinceStartup + 0.5;

            string trigger = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, TriggerFileName);

            if (!File.Exists(trigger))
                return;

            // 타이틀 화면은 살아 있는 씬 위에 얹힌 오버레이라 Player도 뗏목도 이미 존재한다.
            // 그 상태로 발동하면 "게임 안"이 아닌 곳에서 저장/불러오기를 돌리게 되므로 기다린다.
            //
            // 판단 기준은 MainMenuController.isMenuOpen이다. 캔버스 오브젝트의 활성 여부로 보려다
            // 실패했다 - MainMenuCanvas는 게임에 들어가도 활성인 채로 남는다(안쪽 패널만 접힌다).
            if (!Application.isPlaying
                || RaftStructure.Count < 1
                || GameObject.Find("Player") == null)
                return; // 아직 준비 전. 방아쇠는 그대로 두고 다음 기회를 본다.

            var menu = UnityEngine.Object.FindAnyObjectByType<MakeGame.UI.MainMenuController>();
            if (menu != null && menu.isMenuOpen)
                return; // 아직 타이틀 화면이다.

            try
            {
                File.Delete(trigger);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RaftMultiTest] 방아쇠 파일을 지우지 못해 중단합니다: " + e.Message);
                return;
            }

            Run();
        }

        private static readonly List<string> steps = new List<string>();
        private static bool running;
        private static int phase;
        private static int waitFrames;
        private static bool failed;

        /// <summary>저장 직전에 찍어 두는 뗏목 한 대의 상태. 불러오기 뒤 이것과 대조한다.</summary>
        private struct Snapshot
        {
            public string name;
            public Vector3 pos;
            public float yaw;
            public int tiles;
            public int parts;
        }

        /// <summary>
        /// 왕복 검증용 기대값. **대수를 상수로 박지 않는다** - 같은 세션에서 테스트를 두 번 돌리면
        /// 앞선 회차가 세운 뗏목이 그대로 남아 3대, 4대가 되는 것이 정상이기 때문이다.
        /// (첫 판에서 "2대여야 한다"고 박아 두었다가 2회차에서 헛되이 FAIL이 났다.)
        /// </summary>
        private static readonly List<Snapshot> expected = new List<Snapshot>();

        /// <summary>이번 회차에 새로 세운 뗏목. 상태 독립성 검사의 상대다.</summary>
        private static RaftStructure freshRaft;

        private static string savedSlotBackup;

        private static string ResultPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, ResultFileName);

        [MenuItem("MakeGame/뗏목 다중 배치 테스트")]
        private static void Run()
        {
            if (running)
            {
                Debug.LogWarning("[RaftMultiTest] 이미 돌고 있습니다.");
                return;
            }

            if (!Application.isPlaying)
            {
                Debug.LogError("[RaftMultiTest] 플레이 모드에서, 새 게임을 시작한 뒤에 눌러야 합니다.");
                return;
            }

            steps.Clear();
            failed = false;
            phase = 0;
            waitFrames = 0;
            savedSlotBackup = null;
            running = true;

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[RaftMultiTest] 시작");
        }

        private static void Tick()
        {
            if (!Application.isPlaying)
            {
                Fail("플레이 모드가 도중에 끝났다");
                Finish();
                return;
            }

            if (waitFrames > 0)
            {
                waitFrames--;
                return;
            }

            try
            {
                switch (phase)
                {
                    case 0: PhasePrecondition(); break;
                    case 1: PhasePlaceSecond(); break;
                    case 2: PhaseValidSiteRules(); break;
                    case 3: PhaseDistinguish(); break;
                    case 4: PhaseSave(); break;
                    case 5: PhaseDestroyAndLoad(); break;
                    case 6: PhaseVerifyRestore(); break;
                    default: Finish(); return;
                }
            }
            catch (Exception e)
            {
                Fail("예외: " + e.GetType().Name + " " + e.Message);
                Finish();
                return;
            }

            phase++;

            if (failed)
                Finish();
        }

        // ── 0단계: 전제 확인 ────────────────────────────────────────────────
        private static void PhasePrecondition()
        {
            if (GameObject.Find("Player") == null)
            {
                Fail("Player가 없다 - 새 게임을 시작한 뒤에 실행할 것");
                return;
            }

            if (RaftStructure.Count < 1)
            {
                Fail("뗏목이 한 대도 없다(Count=" + RaftStructure.Count + ")");
                return;
            }

            RaftStructure first = RaftStructure.All[0];
            if (first == null || !first.IsPlaced)
            {
                Fail("첫 뗏목이 아직 자리를 못 잡았다");
                return;
            }

            Pass("전제", "뗏목 " + RaftStructure.Count + "대, 첫 뗏목 정박 위치 " + V(first.AnchorPosition));
        }

        // ── 1단계: 두 번째 뗏목 세우기 ──────────────────────────────────────
        private static void PhasePlaceSecond()
        {
            RaftStructure first = RaftStructure.All[0];
            Vector3 origin = first.AnchorPosition;

            float minGap = RaftStructure.FootprintRadius * 2f;
            Vector3 site = Vector3.zero;
            string lastReason = "(후보 없음)";
            int tried = 0;
            bool found = false;

            for (float r = minGap + 1f; r <= minGap + 24f && !found; r += 1.5f)
            {
                for (int a = 0; a < 36 && !found; a++)
                {
                    float ang = a * (Mathf.PI * 2f / 36f);
                    Vector3 p = origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    tried++;

                    if (RaftStructure.IsValidSite(p, null, out string reason))
                    {
                        site = p;
                        found = true;
                    }
                    else
                    {
                        lastReason = reason;
                    }
                }
            }

            if (!found)
            {
                Fail("첫 뗏목 주변에서 유효한 자리를 못 찾았다 (후보 " + tried + "개, 마지막 사유: " + lastReason + ")");
                return;
            }

            int before = RaftStructure.Count;
            RaftStructure second = RaftStructure.Create();
            if (second == null)
            {
                Fail("Create()가 null을 돌려줬다");
                return;
            }

            if (RaftStructure.Count != before + 1)
            {
                Fail("Create() 뒤 명부 수가 " + RaftStructure.Count + " (기대 " + (before + 1) + ")");
                return;
            }

            second.PlaceAt(site, Quaternion.Euler(0f, 37f, 0f));

            if (!second.IsPlaced)
            {
                Fail("PlaceAt 뒤에도 IsPlaced가 false다");
                return;
            }

            Vector3 d = second.AnchorPosition - first.AnchorPosition;
            d.y = 0f;
            if (d.magnitude < minGap * 0.95f)
            {
                Fail("두 뗏목이 겹친다 (간격 " + F(d.magnitude) + "m, 최소 " + F(minGap) + "m)");
                return;
            }

            if (second.gameObject.name == first.gameObject.name)
            {
                Fail("두 뗏목의 이름이 같다: " + second.gameObject.name);
                return;
            }

            freshRaft = second;

            Pass("뗏목 추가 배치",
                "후보 " + tried + "개 탐색 → " + V(site) + " 에 '" + second.gameObject.name +
                "' 배치. 명부 " + RaftStructure.Count + "대, 간격 " + F(d.magnitude) + "m");
        }

        // ── 2단계: IsValidSite 규칙 ────────────────────────────────────────
        private static void PhaseValidSiteRules()
        {
            RaftStructure first = RaftStructure.All[0];
            RaftStructure second = freshRaft;
            var notes = new List<string>();

            // (a) 방금 세운 자리는 이제 "다른 뗏목과 너무 가깝다"여야 한다.
            Vector3 taken = second.transform.position;
            if (RaftStructure.IsValidSite(taken, null, out string r1))
            {
                Fail("이미 뗏목이 선 자리가 여전히 유효하다고 나온다");
                return;
            }
            notes.Add("점유된 자리 거절: \"" + r1 + "\"");

            // (b) 같은 자리라도 그 뗏목 자신을 ignore로 넘기면 통과해야 한다(이동/재배치용).
            if (!RaftStructure.IsValidSite(taken, second, out string r2))
            {
                Fail("ignore로 자신을 넘겼는데도 거절당했다: " + r2);
                return;
            }
            notes.Add("ignore=자기자신 통과");

            // (c) 뭍 위는 거절되어야 한다.
            var worldMap = UnityEngine.Object.FindAnyObjectByType<WorldMapManager>();
            float seaLevel = worldMap != null ? worldMap.seaLevel : 0f;
            Vector3 land = Vector3.zero;
            bool landFound = false;
            Vector3 o = first.AnchorPosition;

            for (int dir = 0; dir < 16 && !landFound; dir++)
            {
                float ang = dir * (Mathf.PI * 2f / 16f);
                var step = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                for (float d = 1f; d <= 40f; d += 1f)
                {
                    Vector3 probe = o + step * d;
                    float y = RaftStructure.SampleTerrainHeightStatic(probe, out bool hit);
                    if (hit && y > seaLevel + 0.6f)
                    {
                        land = probe;
                        landFound = true;
                        break;
                    }
                }
            }

            if (!landFound)
            {
                notes.Add("뭍 지점을 못 찾아 (c) 건너뜀");
            }
            else if (RaftStructure.IsValidSite(land, null, out string r3))
            {
                Fail("뭍 위(" + V(land) + ")가 유효한 자리로 나온다");
                return;
            }
            else
            {
                notes.Add("뭍 거절: \"" + r3 + "\"");
            }

            // (d) 먼바다는 거절되어야 한다.
            Vector3 offshore = o + new Vector3(600f, 0f, 600f);
            if (RaftStructure.IsValidSite(offshore, null, out string r4))
            {
                Fail("먼바다(" + V(offshore) + ")가 유효한 자리로 나온다");
                return;
            }
            notes.Add("먼바다 거절: \"" + r4 + "\"");

            Pass("IsValidSite 규칙", string.Join(" · ", notes));
        }

        // ── 3단계: 두 뗏목에 서로 다른 상태 주기 ────────────────────────────
        private static void PhaseDistinguish()
        {
            RaftStructure a = RaftStructure.All[0];
            RaftStructure b = freshRaft;

            a.SetBaseTileCount(4);
            a.InstallPart(RaftPart.Sail | RaftPart.Rudder);

            b.SetBaseTileCount(7);
            b.InstallPart(RaftPart.Anchor);

            if (a.BaseTileCount != 4 || b.BaseTileCount != 7)
            {
                Fail("바닥판 칸 수가 서로 섞였다 (A=" + a.BaseTileCount + ", B=" + b.BaseTileCount + ")");
                return;
            }

            if (!a.HasPart(RaftPart.Sail) || a.HasPart(RaftPart.Anchor))
            {
                Fail("A의 부품이 섞였다");
                return;
            }

            if (!b.HasPart(RaftPart.Anchor) || b.HasPart(RaftPart.Sail))
            {
                Fail("B의 부품이 섞였다 - 부품이 뗏목 사이에서 새고 있다");
                return;
            }

            Pass("상태 독립성",
                "A(" + a.gameObject.name + ") 칸4/돛+키, B(" + b.gameObject.name + ") 칸7/닻 - 서로 안 섞임");
        }

        // ── 4단계: 저장 ────────────────────────────────────────────────────
        private static void PhaseSave()
        {
            var slc = UnityEngine.Object.FindAnyObjectByType<SaveLoadController>();
            if (slc == null)
            {
                Fail("SaveLoadController를 못 찾았다");
                return;
            }

            // 사용자의 슬롯 파일을 먼저 옆으로 치워 둔다.
            string path = SaveLoadController.SlotFilePath(TestSlot);
            if (File.Exists(path))
            {
                savedSlotBackup = path + ".raftmultitest.orig";
                File.Copy(path, savedSlotBackup, true);
            }

            // 배치된 뗏목 전부를 찍는다(저장 대상과 같은 기준: IsPlaced).
            expected.Clear();
            var live = RaftStructure.All;
            for (int i = 0; i < live.Count; i++)
            {
                RaftStructure raft = live[i];
                if (raft == null || !raft.IsPlaced)
                    continue;

                expected.Add(new Snapshot
                {
                    name = raft.gameObject.name,
                    pos = raft.AnchorPosition,
                    yaw = raft.AnchorYaw,
                    tiles = raft.BaseTileCount,
                    parts = PartsOf(raft),
                });
            }

            if (expected.Count < 2)
            {
                Fail("저장 대상 뗏목이 " + expected.Count + "대뿐이다 - 다중 배치를 검증할 수 없다");
                return;
            }

            slc.SaveToSlot(TestSlot);

            if (!File.Exists(path))
            {
                Fail("저장 뒤에도 슬롯 파일이 없다: " + path);
                return;
            }

            string json = File.ReadAllText(path);
            int raftsAt = json.IndexOf("\"rafts\"", StringComparison.Ordinal);
            if (raftsAt < 0)
            {
                Fail("세이브에 rafts 목록이 없다");
                return;
            }

            var summary = new StringBuilder();
            for (int i = 0; i < expected.Count; i++)
            {
                if (i > 0)
                    summary.Append(", ");
                summary.Append(expected[i].name).Append("=").Append(V(expected[i].pos))
                       .Append(" 칸").Append(expected[i].tiles);
            }

            Pass("저장", "슬롯 " + TestSlot + " 기록 (" + new FileInfo(path).Length + " byte), rafts 목록 존재. " +
                "기대 " + expected.Count + "대: " + summary);
        }

        // ── 5단계: 전부 없앤 뒤 불러오기 ────────────────────────────────────
        private static void PhaseDestroyAndLoad()
        {
            RaftStructure.DestroyAll();

            if (RaftStructure.Count != 0)
            {
                Fail("DestroyAll 뒤에도 명부에 " + RaftStructure.Count + "대가 남아 있다");
                return;
            }

            var slc = UnityEngine.Object.FindAnyObjectByType<SaveLoadController>();
            if (slc == null)
            {
                Fail("SaveLoadController가 사라졌다");
                return;
            }

            // 같은 프레임에 곧바로 불러온다 - 뗏목이 0대인 프레임을 만들지 않기 위해서다.
            slc.LoadFromSlot(TestSlot);
            waitFrames = 3;

            Pass("불러오기 호출", "DestroyAll로 0대 확인 → 슬롯 " + TestSlot + " 불러오기 실행");
        }

        // ── 6단계: 복원 검증 ───────────────────────────────────────────────
        private static void PhaseVerifyRestore()
        {
            if (RaftStructure.Count != expected.Count)
            {
                Fail("불러오기 뒤 뗏목이 " + RaftStructure.Count + "대다 (기대 " + expected.Count + "대)");
                return;
            }

            // 기대값 한 건마다 아직 짝이 없는 뗏목 중 가장 가까운 것을 붙인다. 1:1이 아니면 실패다
            // (같은 뗏목 하나에 두 기대값이 붙는 상황 = 나머지 한 대가 엉뚱한 곳에 섰다는 뜻).
            var live = new List<RaftStructure>(RaftStructure.All);
            var matched = new bool[live.Count];
            var notes = new List<string>();

            for (int e = 0; e < expected.Count; e++)
            {
                Snapshot want = expected[e];
                int best = -1;
                float bestDist = float.MaxValue;

                for (int i = 0; i < live.Count; i++)
                {
                    if (matched[i] || live[i] == null)
                        continue;

                    float d = Flat(live[i].AnchorPosition - want.pos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    Fail(want.name + "에 짝지을 뗏목이 남지 않았다");
                    return;
                }

                if (bestDist > 0.5f)
                {
                    Fail(want.name + "의 복원 위치가 " + F(bestDist) + "m 어긋났다 (기대 " + V(want.pos) + ")");
                    return;
                }

                matched[best] = true;
                RaftStructure got = live[best];

                if (got.BaseTileCount != want.tiles)
                {
                    Fail(want.name + "의 바닥판 칸 수가 " + got.BaseTileCount + " (기대 " + want.tiles + ")");
                    return;
                }

                if (PartsOf(got) != want.parts)
                {
                    Fail(want.name + "의 부품이 " + PartsOf(got) + " (기대 " + want.parts + ")" +
                         " - 부품이 뗏목 사이에서 새고 있다");
                    return;
                }

                float yawErr = Mathf.Abs(Mathf.DeltaAngle(got.AnchorYaw, want.yaw));
                if (yawErr > 1.5f)
                {
                    Fail(want.name + "의 방위가 " + F(yawErr) + "° 어긋났다");
                    return;
                }

                notes.Add(want.name + " 오차 " + F(bestDist) + "m/" + F(yawErr) + "°");
            }

            Pass("왕복 복원", expected.Count + "대 전부 제자리 · " + string.Join(" · ", notes));
        }

        // ── 마무리 ─────────────────────────────────────────────────────────
        private static void Finish()
        {
            EditorApplication.update -= Tick;
            running = false;

            // 사용자의 원본 슬롯 파일을 되돌린다.
            try
            {
                string bak = SaveLoadController.SlotBackupPath(TestSlot);
                if (File.Exists(bak))
                    File.Delete(bak);

                if (!string.IsNullOrEmpty(savedSlotBackup) && File.Exists(savedSlotBackup))
                {
                    File.Copy(savedSlotBackup, SaveLoadController.SlotFilePath(TestSlot), true);
                    File.Delete(savedSlotBackup);
                }
                else if (File.Exists(SaveLoadController.SlotFilePath(TestSlot)))
                {
                    // 원래 비어 있던 슬롯이면 테스트가 남긴 파일도 치운다.
                    File.Delete(SaveLoadController.SlotFilePath(TestSlot));
                }
            }
            catch (Exception e)
            {
                steps.Add("{\"step\":\"슬롯 복구\",\"ok\":false,\"note\":" + Q(e.Message) + "}");
                failed = true;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": ").Append(failed ? "\"FAIL\"" : "\"PASS\"").Append(",\n");
            sb.Append("  \"version\": ").Append(Q(MakeGame.UI.MainMenuController.DisplayVersion)).Append(",\n");
            sb.Append("  \"raftCountAtEnd\": ").Append(RaftStructure.Count).Append(",\n");
            sb.Append("  \"steps\": [\n    ");
            sb.Append(string.Join(",\n    ", steps));
            sb.Append("\n  ]\n}\n");

            try
            {
                File.WriteAllText(ResultPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[RaftMultiTest] 결과 기록 실패: " + e.Message);
            }

            if (failed)
                Debug.LogError("[RaftMultiTest] FAIL - " + ResultFileName + " 참고");
            else
                Debug.Log("[RaftMultiTest] PASS - " + ResultFileName + " 참고");
        }

        // ── 보조 ───────────────────────────────────────────────────────────
        private static void Pass(string step, string note)
        {
            steps.Add("{\"step\":" + Q(step) + ",\"ok\":true,\"note\":" + Q(note) + "}");
            Debug.Log("[RaftMultiTest] OK · " + step + " · " + note);
        }

        private static void Fail(string note)
        {
            failed = true;
            steps.Add("{\"step\":\"phase" + phase + "\",\"ok\":false,\"note\":" + Q(note) + "}");
            Debug.LogError("[RaftMultiTest] FAIL · " + note);
        }

        private static int PartsOf(RaftStructure raft)
        {
            int mask = 0;
            if (raft.HasPart(RaftPart.Sail)) mask |= 1;
            if (raft.HasPart(RaftPart.Rudder)) mask |= 2;
            if (raft.HasPart(RaftPart.Anchor)) mask |= 4;
            if (raft.HasPart(RaftPart.Oar)) mask |= 8;
            if (raft.HasPart(RaftPart.Motor)) mask |= 16;
            return mask;
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static string V(Vector3 v) =>
            "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        private static string F(float f) =>
            f.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Q(string s)
        {
            if (s == null)
                return "\"\"";

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
