using System.Collections.Generic;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 뗏목 제작에 드는 재료 한 줄. itemName은 <c>ItemData.itemName</c>과 **문자 그대로** 대조된다
    /// (BuildPieceCost와 같은 규약 - 이 프로젝트의 재료 대조는 전부 이름 문자열이다).
    /// </summary>
    public struct RaftBuildCost
    {
        public readonly string itemName;
        public readonly int count;

        public RaftBuildCost(string itemName, int count)
        {
            this.itemName = itemName;
            this.count = count;
        }
    }

    /// <summary>
    /// 제작 목록의 항목 하나. 바닥판 3종 · 갑판 바닥재 · 부품 5종으로 **딱 9개**다.
    /// 순서를 바꾸지 말 것 - 제작 UI의 숫자키(1~9)와 줄 순서가 이 열거 순서 하나로 정해진다.
    /// </summary>
    public enum RaftBuildEntry
    {
        BaseWood = 0,
        BaseBuoy,
        BaseBarrel,
        Floor,
        Oar,
        Sail,
        Rudder,
        Anchor,
        Motor,
    }

    /// <summary>
    /// 뗏목 제작표 - **무엇을 얼마에 만들 수 있는가**의 단일 출처.
    ///
    /// [왜 UI가 아니라 여기 있나] 이 프로젝트에서 여러 번 반복된 사고가 "UI가 판정을 다시 구현해서
    /// 화면과 실제 동작이 갈라지는" 것이다(InteractionPromptUI 클래스 주석). 그래서 제작 UI
    /// (RaftBuildUI)는 이 클래스가 돌려주는 값을 **그리기만** 하고, 재료 대조·소모·설치는 전부
    /// TryBuild 한 곳에서 일어난다. 다음 웨이브(항해)나 퀘스트가 같은 표를 읽어야 할 때도 여기다.
    ///
    /// [재료 이름] 전부 Assets/_Project/ScriptableObjects/Item_*.asset 에 **실제로 존재하는**
    /// itemName이다(나뭇가지 · 노끈 · 부력통 · 금속조각 · 천조각 · 대나무 · 석재 · 엔진부품).
    /// 새 아이템 에셋은 하나도 만들지 않았다.
    ///
    /// [밸런스 근거] 기준선은 이미 게임에 있는 건축 카탈로그(BuildPieceCatalog)다 - 바닥 조각이
    /// 나뭇가지 2, 문이 나뭇가지 3 + 노끈 1, 소형 상자가 나뭇가지 8 + 노끈 3 + 야자잎 4다.
    /// 뗏목 바닥판 한 칸(나뭇가지 4 + 노끈 2)은 그 사이, 즉 "집 바닥 두 장 값"에 놓았다.
    /// 항해 가능(4칸)까지 나뭇가지 16 + 노끈 8, 대양 규격(6칸 + 돛 + 키)까지 대략 나뭇가지 27 +
    /// 노끈 15 + 천조각 3 + 대나무 2 - 상자 하나를 짓고 한두 번 업그레이드하는 정도의 채집량이다.
    /// 노(나뭇가지 2 + 노끈 1)를 가장 싸게 둔 것이 이 표의 핵심이다: 첫 항해까지 걸리는 시간을
    /// 정하는 것은 부품이 아니라 바닥판 4칸이어야 하고, 추진 수단이 비싸면 "칸은 다 깔았는데 못 나간다"
    /// 는 막힘이 생긴다. 모터(엔진부품 1 + 금속조각 4 + 노끈 1)는 이제 후반 보상이 아니라 특대 섬 항로의
    /// 열쇠다(IslandTravel.CurrentBypass.OceanReadyWithMotor). 그래서 엔진부품이 특대 섬 자원 노드에만
    /// 있으면 "모터가 있어야 갈 수 있는 섬에만 모터 재료가 있는" 순환 잠금이 된다 - 시작 섬 여객기 2개와
    /// 모든 섬의 난파선이 그 밖의 공급원이라는 전제 위에서만 이 값이 성립한다.
    /// </summary>
    public static class RaftBuildCatalog
    {
        /// <summary>제작 UI가 그리는 순서(= 숫자키 1~9). 열거 순서와 같다.</summary>
        public static readonly RaftBuildEntry[] Order =
        {
            RaftBuildEntry.BaseWood,
            RaftBuildEntry.BaseBuoy,
            RaftBuildEntry.BaseBarrel,
            RaftBuildEntry.Floor,
            RaftBuildEntry.Oar,
            RaftBuildEntry.Sail,
            RaftBuildEntry.Rudder,
            RaftBuildEntry.Anchor,
            RaftBuildEntry.Motor,
        };

        // ── 재료표 (정적 배열 - 매 프레임 갱신되는 UI가 읽어도 할당이 0이다) ──────────
        private static readonly RaftBuildCost[] CostBaseWood =
        {
            new RaftBuildCost("나뭇가지", 4), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostBaseBuoy =
        {
            new RaftBuildCost("부력통", 1), new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostBaseBarrel =
        {
            new RaftBuildCost("금속조각", 4), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostFloor =
        {
            new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostOar =
        {
            new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostSail =
        {
            new RaftBuildCost("천조각", 3), new RaftBuildCost("노끈", 2), new RaftBuildCost("대나무", 2),
        };

        private static readonly RaftBuildCost[] CostRudder =
        {
            new RaftBuildCost("나뭇가지", 3), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostAnchor =
        {
            new RaftBuildCost("석재", 2), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostMotor =
        {
            new RaftBuildCost("엔진부품", 1), new RaftBuildCost("금속조각", 4), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] EmptyCost = new RaftBuildCost[0];

        /// <summary>항목의 재료 목록. 절대 null이 아니다(없으면 빈 목록).</summary>
        public static IReadOnlyList<RaftBuildCost> GetCost(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return CostBaseWood;
                case RaftBuildEntry.BaseBuoy: return CostBaseBuoy;
                case RaftBuildEntry.BaseBarrel: return CostBaseBarrel;
                case RaftBuildEntry.Floor: return CostFloor;
                case RaftBuildEntry.Oar: return CostOar;
                case RaftBuildEntry.Sail: return CostSail;
                case RaftBuildEntry.Rudder: return CostRudder;
                case RaftBuildEntry.Anchor: return CostAnchor;
                case RaftBuildEntry.Motor: return CostMotor;
                default: return EmptyCost;
            }
        }

        /// <summary>항목 이름(한국어). 부품 이름은 RaftStructure의 단일 출처를 그대로 쓴다.</summary>
        public static string GetDisplayName(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Wood);
                case RaftBuildEntry.BaseBuoy: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Buoy);
                case RaftBuildEntry.BaseBarrel: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Barrel);
                case RaftBuildEntry.Floor: return "갑판 바닥재";
                default: return RaftStructure.GetPartName(GetPart(entry));
            }
        }

        /// <summary>한 줄 설명(그 항목이 무엇을 바꾸는지). 제작 UI의 보조 문구다.</summary>
        public static string GetDescription(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return "부력 1.0 · 가장 싼 한 칸";
                case RaftBuildEntry.BaseBuoy: return "부력 1.6 · 주운 부력통을 끼운다";
                case RaftBuildEntry.BaseBarrel: return "부력 2.0 · 가장 튼튼하다";
                case RaftBuildEntry.Floor: return "칸 위에 깔아 걸어다닐 면을 만든다";
                case RaftBuildEntry.Oar: return "가장 싼 추진 · 근해까지";
                case RaftBuildEntry.Sail: return "키와 함께 있어야 대양에 나간다";
                case RaftBuildEntry.Rudder: return "방향을 잡는다";
                case RaftBuildEntry.Anchor: return "정박용 · 항해에는 필요 없다";
                case RaftBuildEntry.Motor: return "가장 빠르다 · 돛+키를 대체한다";
                default: return string.Empty;
            }
        }

        /// <summary>이 항목이 놓는 바닥판 종류(부품이면 None).</summary>
        public static RaftBaseTileKind GetBaseTileKind(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return RaftBaseTileKind.Wood;
                case RaftBuildEntry.BaseBuoy: return RaftBaseTileKind.Buoy;
                case RaftBuildEntry.BaseBarrel: return RaftBaseTileKind.Barrel;
                default: return RaftBaseTileKind.None;
            }
        }

        /// <summary>이 항목이 장착하는 부품(바닥판/바닥재면 None).</summary>
        public static RaftPart GetPart(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.Oar: return RaftPart.Oar;
                case RaftBuildEntry.Sail: return RaftPart.Sail;
                case RaftBuildEntry.Rudder: return RaftPart.Rudder;
                case RaftBuildEntry.Anchor: return RaftPart.Anchor;
                case RaftBuildEntry.Motor: return RaftPart.Motor;
                default: return RaftPart.None;
            }
        }

        /// <summary>
        /// 인벤토리에 있는 그 이름의 아이템 개수. BuildingSystem.CountOwned와 **같은 규칙**이다
        /// (한 칸 = 한 개, 이름 문자 그대로 대조) - 두 시스템이 같은 재료를 다르게 세면 안 된다.
        /// </summary>
        public static int CountOwned(PlayerInventory inventory, string itemName)
        {
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            for (int i = 0; i < inventory.items.Count; i++)
            {
                InventoryItem item = inventory.items[i];
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }

        /// <summary>재료를 전부 들고 있는지(소모하지 않는다).</summary>
        public static bool HasMaterials(PlayerInventory inventory, RaftBuildEntry entry)
        {
            IReadOnlyList<RaftBuildCost> cost = GetCost(entry);
            for (int i = 0; i < cost.Count; i++)
            {
                if (cost[i].count > 0 && CountOwned(inventory, cost[i].itemName) < cost[i].count)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 지금 이 항목을 만들 수 있는지. 만들 수 없으면 사유를 돌려준다(재료 부족은 여기서 보지
        /// 않는다 - UI가 재료 줄에 수량으로 이미 보여주므로 사유를 두 번 적지 않기 위해서다).
        /// </summary>
        public static bool IsAvailable(RaftStructure raft, RaftBuildEntry entry, out string blockedReason)
        {
            blockedReason = string.Empty;
            if (raft == null)
            {
                blockedReason = "뗏목 자리 없음";
                return false;
            }

            RaftBaseTileKind kind = GetBaseTileKind(entry);
            if (kind != RaftBaseTileKind.None)
            {
                if (raft.BaseTileCount >= RaftStructure.MaxBaseTiles)
                {
                    blockedReason = "가득 참";
                    return false;
                }
                return true;
            }

            if (entry == RaftBuildEntry.Floor)
            {
                if (raft.BaseTileCount <= 0)
                {
                    blockedReason = "바닥판 먼저";
                    return false;
                }
                if (raft.NextFloorlessTileIndex < 0)
                {
                    blockedReason = "전부 깔림";
                    return false;
                }
                return true;
            }

            RaftPart part = GetPart(entry);
            if (part == RaftPart.None)
            {
                blockedReason = "알 수 없는 항목";
                return false;
            }

            if (raft.HasPart(part))
            {
                blockedReason = "장착됨";
                return false;
            }

            // 부품은 딛고 설 바닥이 있어야 단다. 바닥판 0칸짜리 물 위에 돛대를 세울 수는 없다.
            if (raft.BaseTileCount <= 0)
            {
                blockedReason = "바닥판 먼저";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 실제 제작. 재료를 확인하고 → 소모하고 → 뗏목에 반영한다. **이 순서와 판정이 유일하다**
        /// (UI가 같은 검사를 다시 하지 않는다). 실패하면 아무것도 소모하지 않고 사유를 돌려준다.
        ///
        /// 소모 방식은 BuildingSystem.ConsumeCostList와 같다 - 뒤에서부터 같은 이름의 항목을 지우고
        /// 마지막에 NotifyInventoryChanged를 한 번만 부른다(칸마다 이벤트를 쏘면 UI가 n번 다시 그린다).
        /// </summary>
        public static bool TryBuild(RaftStructure raft, PlayerInventory inventory, RaftBuildEntry entry,
            out string failureReason)
        {
            if (!IsAvailable(raft, entry, out failureReason))
                return false;

            if (inventory == null || inventory.items == null)
            {
                failureReason = "소지품을 찾을 수 없다";
                return false;
            }

            IReadOnlyList<RaftBuildCost> cost = GetCost(entry);
            for (int i = 0; i < cost.Count; i++)
            {
                if (cost[i].count > 0 && CountOwned(inventory, cost[i].itemName) < cost[i].count)
                {
                    failureReason = $"재료 부족 - {cost[i].itemName} {cost[i].count}개 필요";
                    return false;
                }
            }

            // 여기서부터는 반드시 성공한다(위에서 전부 확인했다) - 재료만 사라지는 상태가 없다.
            for (int i = 0; i < cost.Count; i++)
            {
                RaftBuildCost line = cost[i];
                if (string.IsNullOrEmpty(line.itemName) || line.count <= 0)
                    continue;

                int remaining = line.count;
                for (int k = inventory.items.Count - 1; k >= 0 && remaining > 0; k--)
                {
                    InventoryItem item = inventory.items[k];
                    if (item == null || item.data == null || item.data.itemName != line.itemName)
                        continue;

                    inventory.items.RemoveAt(k);
                    remaining--;
                }
            }

            inventory.NotifyInventoryChanged();

            RaftBaseTileKind kind = GetBaseTileKind(entry);
            if (kind != RaftBaseTileKind.None)
                return raft.AddBaseTile(kind);

            if (entry == RaftBuildEntry.Floor)
                return raft.AddFloorTile();

            return raft.InstallPart(GetPart(entry));
        }
    }
}
