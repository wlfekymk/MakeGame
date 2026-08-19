using UnityEngine;
using MakeGame.Data;
using MakeGame.Player; // ProgressionTracker가 PlayerInventory/SurvivalStats를 읽는다(읽기 전용).

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어의 현재 진행 단계를 판정하는 읽기 전용 유틸리티(Docs/Design_Progression.md 6장 요청).
    ///
    /// 설계 원칙 세 가지:
    /// 1. **상태를 만들지 않는다.** 판정 입력이 전부 이미 public이라, 매 호출마다 그 값만 보고 다시 계산한다.
    ///    새 필드도, 새 컴포넌트도, 세이브 포맷 변경도 없다(game-designer 명시 조건). 따라서 F9 불러오기나
    ///    씬 재진입 후에도 "복원해야 할 진행 단계" 같은 것이 애초에 존재하지 않는다.
    /// 2. **위 단계부터 아래로 판정한다.** 높은 단계 조건이 하나라도 성립하면 즉시 그 단계를 반환하므로,
    ///    "탈출 준비 중인데 배가 고파져서 1단계로 되돌아가는" 역행이 구조적으로 불가능하다.
    /// 3. **아이템은 이름으로 조회한다.** 이 유틸리티는 static이라 인스펙터 참조를 가질 수 없다. 이 프로젝트가
    ///    이미 여러 곳에서 쓰는 한국어 itemName 스위치와 같은 관례다(IslandResourceSpawner.GetNodeShape 등).
    ///
    /// 이 파일에 있는 이유: 파일 소유권 규칙상 systems-engineer-B가 편집할 수 있는 파일 안에 둬야 했고,
    /// 그 중 "월드 전체 상태를 다루는" 성격에 가장 가까운 것이 이 파일이다. WorldMapManager 자체와는
    /// 의존 관계가 전혀 없으므로(참조도 호출도 하지 않는다) 나중에 독립 파일로 그대로 옮길 수 있다.
    /// </summary>
    public static class ProgressionTracker
    {
        /// <summary>대형 섬 금속조각을 여는 유일한 열쇠. 2단계 → 3단계 전환 신호(Design_Progression 2장).</summary>
        public const string HatchetItemName = "손도끼";

        /// <summary>대형 섬 이상에서만 나오는 재료. 최초 획득이 3단계 → 4단계 전환 신호.</summary>
        public const string MetalScrapItemName = "금속조각";

        /// <summary>손도끼 레시피 재료(나뭇가지 1 + 돌조각 2). 채집 루프를 이해했다는 증거로 쓴다.</summary>
        public const string StickItemName = "나뭇가지";

        /// <summary>손도끼 레시피 재료.</summary>
        public const string StoneItemName = "돌조각";

        /// <summary>
        /// 1단계 → 2단계 전환 임계 비율. 문서의 전환 신호는 "허기·갈증이 70% 아래로 떨어졌다가 회복"인데,
        /// "떨어진 적이 있다"는 이력이라 상태 저장 없이는 볼 수 없다. 상태를 만들지 않기 위해 두 가지
        /// 관측 가능한 대체 신호를 OR로 쓴다 - (a) 지금 70% 아래이거나, (b) 이미 기초 재료를 채집했거나.
        /// (b)가 있어서 밥을 먹어 허기가 100%로 돌아와도 1단계로 되돌아가지 않는다(재료는 남아 있으므로).
        /// 갈증은 0.08/초로 줄어 약 6분이면 70%에 닿으므로, 실제로도 문서의 "0~10분" 구간과 잘 맞는다.
        /// </summary>
        public const float SurvivalPressureRatio = 0.7f;

        /// <summary>
        /// 현재 진행 단계를 판정한다. 인자는 전부 null을 허용한다(연결되지 않은 참조가 있어도 NRE 없이
        /// 판정 가능한 범위까지만 계산한다 - 이 프로젝트에서 씬 참조 누락은 실제로 반복된 사고다).
        /// 매 프레임 호출해도 되는 비용(인벤토리 1회 순회 × 최대 4번)이며, 부작용이 전혀 없다.
        /// </summary>
        public static ProgressionStage Evaluate(PlayerInventory inventory, SurvivalStats stats,
            RaftStructure raft, AircraftRepairSystem aircraft, IslandTravel travel)
        {
            // 5단계 - 한쪽 경로가 100%에 도달했다.
            // 뗏목 쪽 기준은 "대양에 나갈 준비가 끝났는가"(IsOceanReady)다 - 엔딩 판정과 같은 값을 봐야
            // HUD가 "탈출 준비 완료"라고 말한 뒤에도 엔딩이 안 나는 어긋남이 생기지 않는다.
            if ((raft != null && raft.IsOceanReady) || (aircraft != null && aircraft.isRepairComplete))
                return ProgressionStage.Escape;

            // 4단계 - 탈출 경로에 실제로 착수했다. 뗏목 바닥판을 한 칸이라도 놓았거나, 경비행기 재료를
            // 투입했거나, 금속조각을 처음 얻었거나.
            bool raftStarted = raft != null && raft.Exists;
            bool aircraftStarted = aircraft != null && aircraft.GetOverallProgress() > 0f;
            if (raftStarted || aircraftStarted || CountByName(inventory, MetalScrapItemName) > 0)
                return ProgressionStage.EscapePreparation;

            // 3단계 - 손도끼를 얻었거나(문서의 전환 신호), 이미 시작 섬을 떠나 탐험을 시작했다.
            bool leftStartingIsland = travel != null && travel.currentIslandId != 0;
            if (leftStartingIsland || CountByName(inventory, HatchetItemName) > 0)
                return ProgressionStage.Exploration;

            // 2단계 - 생존 압박을 한 번 겪었거나, 이미 기초 재료를 채집했다(위 SurvivalPressureRatio 주석).
            float pressureThreshold = SurvivalStats.MaxStatValue * SurvivalPressureRatio;
            bool feltSurvivalPressure = stats != null &&
                (stats.hunger <= pressureThreshold || stats.thirst <= pressureThreshold);
            bool hasGatheredBasics = CountByName(inventory, StickItemName) > 0 || CountByName(inventory, StoneItemName) > 0;
            if (feltSurvivalPressure || hasGatheredBasics)
                return ProgressionStage.Tools;

            return ProgressionStage.Survival;
        }

        /// <summary>
        /// 단계에 대응하는 HUD 목표 문구를 반환한다. 문구는 Docs/Design_Progression.md 3장에 확정된 것을
        /// 그대로 옮긴 것이다(임의로 바꾸지 말 것 - 키 안내가 문구의 핵심이다).
        /// 4단계만 두 줄('\n' 구분)이며, 이는 "두 갈래가 처음 명시되는 유일한 지점"이라는 설계 의도다.
        /// raft/aircraft를 넘기면 4단계 문구에 실제 진행도가 채워지고, 넘기지 않으면 진행도 없이 나온다.
        /// </summary>
        public static string GetObjectiveText(ProgressionStage stage,
            RaftStructure raft = null, AircraftRepairSystem aircraft = null)
        {
            switch (stage)
            {
                case ProgressionStage.Tools:
                    return "제작(V)으로 도구를 만드세요 — 손도끼 · 창";

                case ProgressionStage.Exploration:
                    return "지도(M)를 열어 큰 섬으로 이동하세요";

                case ProgressionStage.EscapePreparation:
                {
                    // 뗏목은 "바닥판을 몇 칸 깔았는가", 경비행기는 "재료를 몇 % 모았는가"가 각각
                    // 자연스러운 단위다(뗏목은 격자 누적제, AircraftRepairSystem은 재료 누적제).
                    int raftTiles = raft != null ? raft.BaseTileCount : 0;
                    int aircraftPercent = aircraft != null
                        ? Mathf.RoundToInt(Mathf.Clamp01(aircraft.GetOverallProgress()) * 100f)
                        : 0;

                    // 뗏목(A)을 먼저 쓴다 - Design_Progression 4장의 결정(기본 안내 경로를 배로 둔다).
                    // 작업대가 사라졌으므로 위치 표기는 "시작 섬 해안"으로 바꿨다 - 틀린 길 안내가 더 나쁘다.
                    return $"탈출 경로 A — 뗏목 건조 (시작 섬 해안)  [바닥판 {raftTiles}/{RaftStructure.MaxBaseTiles}칸]\n" +
                           $"탈출 경로 B — 경비행기 수리 (잔해: 특대 섬)  [재료 {aircraftPercent}%]";
                }

                case ProgressionStage.Escape:
                    return "탈출 준비 완료 — 시작 섬에서 탈출하세요";

                case ProgressionStage.Survival:
                default:
                    return "물과 음식을 확보하세요";
            }
        }

        /// <summary>
        /// 판정과 문구 생성을 한 번에 한다(UI가 가장 자주 쓸 형태). Evaluate와 동일하게 부작용이 없다.
        /// </summary>
        public static string GetObjectiveText(PlayerInventory inventory, SurvivalStats stats,
            RaftStructure raft, AircraftRepairSystem aircraft, IslandTravel travel)
        {
            return GetObjectiveText(Evaluate(inventory, stats, raft, aircraft, travel), raft, aircraft);
        }

        /// <summary>
        /// 인벤토리에서 지정한 이름의 아이템 개수를 센다(PlayerInventory.GetItemCount와 같은 셈법 -
        /// InventoryItem 하나가 1개다). ItemData 참조 없이 이름만으로 셀 수 있어야 이 유틸리티가
        /// 인스펙터 배선 없이 동작한다. 인벤토리가 null이거나 비어 있으면 0.
        /// </summary>
        public static int CountByName(PlayerInventory inventory, string itemName)
        {
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            foreach (var item in inventory.items)
            {
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }
    }
}
