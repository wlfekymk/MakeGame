using System.Collections.Generic;

namespace MakeGame.Systems
{
    /// <summary>
    /// 자유 건축 부품의 종류. 값을 **명시적으로 고정**해 둔다 - 세이브/UI가 이 정수를 그대로 쓰기 때문에
    /// 중간에 끼워 넣거나 순서를 바꾸면 기존 데이터의 의미가 통째로 밀린다(AGENT_BRIEF 3장 resourceEntries와 같은 함정).
    /// 추가는 반드시 맨 끝에.
    /// </summary>
    public enum BuildPieceType
    {
        Floor = 0,
        Wall = 1,
        Doorway = 2,
        Window = 3,
        Stair = 4,

        /// <summary>
        /// [배치 38] 한쪽으로 기운 지붕(shed roof). 셀 하나를 덮고 로컬 원점이 **처마 밑면**이라
        /// position.y가 곧 그 층의 천장 높이다(= 벽 꼭대기 / 위층 바닥 윗면과 같은 평면).
        /// 회전이 곧 경사 방향이며, 기울기는 메시 정점에 구워져 있다(회전으로 기울이지 않는다).
        /// **값 5는 맨 끝이다 - 새 부품은 반드시 이 아래에만 붙인다.**
        /// </summary>
        Roof = 5,

        /// <summary>
        /// [배치 39] 보관 상자. 셀 하나를 차지하고 **바닥(또는 갑판) 위에만** 놓이며, 로컬 원점이
        /// 밑면이라 position.y가 곧 딛고 선 바닥의 윗면이다. 회전은 상자가 바라보는 방향이다.
        ///
        /// **구조물이 아니다.** 지붕이 roofByKey로 분리돼 바닥 조회에 섞이지 않는 것과 같은 이유로
        /// 상자도 BuildingSystem의 chestByKey에만 들어가고, 딛는 면 조회(TryGetFloorTopY)와
        /// 천장 조회(HasCeilingAt), 실내 판정에는 절대 들어가지 않는다 - 상자 위에 벽·지붕이 서면 안 된다.
        ///
        /// 등급(소·중·대·특대)은 이 열거형이 아니라 <see cref="MakeGame.Systems.StorageChest"/>의 Tier가
        /// 들고 있다. 등급마다 값을 만들면 세이브에 박힌 정수가 등급 개수만큼 늘어나고, 업그레이드가
        /// "부품 종류를 바꾸는 일"이 되어 격자 표를 다시 써야 한다.
        /// **값 6은 맨 끝이다 - 새 부품은 반드시 이 아래에만 붙인다.**
        /// </summary>
        Chest = 6,
    }

    /// <summary>부품 하나를 짓는 데 필요한 재료 한 줄. itemName은 ItemData.itemName과 문자 그대로 대조된다.</summary>
    [System.Serializable]
    public struct BuildPieceCost
    {
        public string itemName;
        public int count;

        public BuildPieceCost(string itemName, int count)
        {
            this.itemName = itemName;
            this.count = count;
        }
    }

    /// <summary>
    /// 건축 부품의 격자 규격 · 표시 이름 · 재료표를 들고 있는 단일 소스.
    ///
    /// **재료 이름은 전부 Assets/_Project/ScriptableObjects/Item_*.asset 에 실제로 존재하는 itemName이다**
    /// (나뭇가지 / 노끈 / 대나무). 인벤토리 대조는 Shelter.CountByName과 동일하게 문자열 비교라,
    /// 오타 하나가 "영원히 못 짓는 부품"이 된다. 이름을 고칠 일이 있으면 에셋을 먼저 확인해라.
    ///
    /// 난이도 감각:
    /// - 바닥이 가장 싸고 **노끈을 요구하지 않는다** - 첫 바닥은 나뭇가지만으로 놓을 수 있어야 한다
    ///   (노끈 1개 = 야자잎 3개 + 제작 스킬 1, Recipe_노끈.asset). 집짓기 시작에 제작대를 요구하지 않는다.
    /// - 벽부터 결속(노끈)이 붙고, 문·창문은 벽보다 한 단계 비싸다(문틀/창틀은 부재가 더 든다).
    /// - 비교 기준: 쉼터 Lv2 승급이 나뭇가지6+야자잎4+노끈3+천조각2 (Shelter.cs:118). 부품은 수십 개를
    ///   놓는 물건이므로 그보다 한참 싸야 한다.
    /// </summary>
    public static class BuildPieceCatalog
    {
        /// <summary>바닥 한 칸의 한 변(m). 격자 원점은 월드 (0,0,0).</summary>
        public const float CellSize = 2f;

        /// <summary>한 층의 높이(m) = 벽 높이. 바닥 윗면에서 다음 층 바닥 윗면까지의 거리다.</summary>
        public const float LevelHeight = 2.5f;

        // ── 재료표 ────────────────────────────────────────────────────────────────
        // UI가 매 프레임 폴링하므로 호출마다 new 하지 않는다. 정적 배열을 한 번만 만들어 그대로 돌려준다.
        // (배열이지만 IReadOnlyList로만 노출하므로 호출부가 실수로 갈아끼울 수 없다.)

        private static readonly BuildPieceCost[] FloorCost =
        {
            new BuildPieceCost("나뭇가지", 4),
        };

        private static readonly BuildPieceCost[] WallCost =
        {
            new BuildPieceCost("나뭇가지", 4),
            new BuildPieceCost("노끈", 1),
        };

        private static readonly BuildPieceCost[] DoorwayCost =
        {
            new BuildPieceCost("나뭇가지", 5),
            new BuildPieceCost("노끈", 2),
        };

        private static readonly BuildPieceCost[] WindowCost =
        {
            new BuildPieceCost("나뭇가지", 4),
            new BuildPieceCost("대나무", 2),
            new BuildPieceCost("노끈", 1),
        };

        private static readonly BuildPieceCost[] StairCost =
        {
            new BuildPieceCost("나뭇가지", 6),
            new BuildPieceCost("대나무", 2),
            new BuildPieceCost("노끈", 2),
        };

        // 지붕은 **벽보다 조금 싸다**(나뭇가지 4 + 노끈 1 → 3 + 1). 대신 이엉으로 덮으므로 야자잎이
        // 들어간다 - 야자잎은 야자나무에서 도구 없이 얻는 가장 흔한 재료라 실질 난이도는 벽 이하다.
        // 집 한 채에 지붕이 여러 장 필요한 물건이라, 여기서 비싸지면 지붕을 아예 안 올리게 된다.
        private static readonly BuildPieceCost[] RoofCost =
        {
            new BuildPieceCost("나뭇가지", 3),
            new BuildPieceCost("야자잎", 3),
            new BuildPieceCost("노끈", 1),
        };

        // ── 보관 상자 (배치 39) ──────────────────────────────────────────────────
        // 지을 수 있는 것은 **소형뿐이다.** 중·대·특대는 이미 놓인 상자를 업그레이드해야만 도달한다
        // (감독이 사용자와 확정). 그래서 GetCost(Chest)는 언제나 소형 설치비를 돌려주고, 승급비는
        // 별도의 GetChestUpgradeCost로만 나간다 - 건축 메뉴가 상자 슬롯에 띄우는 값은 소형 설치비다.
        //
        // 난이도 감각(비교 기준): 벽 한 장 = 나뭇가지4+노끈1, 쉼터 Lv2 승급 = 나뭇가지6+야자잎4+노끈3+천조각2.
        // · 소형(50칸)은 쉼터 Lv2와 비슷한 값이다. 인벤토리가 30칸이므로 첫 상자 하나로 숨통이 트여야 한다.
        // · 승급마다 재료가 한 단계씩 상위 계열로 옮겨 간다(나뭇가지 → 대나무 → 금속조각). 금속조각은
        //   경비행기 잔해에서만 나오는 희귀재라, 대형 이상은 확실히 중반 이후 목표가 된다.
        private static readonly BuildPieceCost[] ChestCost =
        {
            new BuildPieceCost("나뭇가지", 8),
            new BuildPieceCost("노끈", 3),
            new BuildPieceCost("야자잎", 4),
        };

        private static readonly BuildPieceCost[] ChestUpgradeToMedium =
        {
            new BuildPieceCost("나뭇가지", 10),
            new BuildPieceCost("대나무", 6),
            new BuildPieceCost("노끈", 4),
        };

        private static readonly BuildPieceCost[] ChestUpgradeToLarge =
        {
            new BuildPieceCost("대나무", 12),
            new BuildPieceCost("금속조각", 4),
            new BuildPieceCost("노끈", 6),
            new BuildPieceCost("천조각", 3),
        };

        private static readonly BuildPieceCost[] ChestUpgradeToHuge =
        {
            new BuildPieceCost("대나무", 16),
            new BuildPieceCost("금속조각", 10),
            new BuildPieceCost("노끈", 8),
            new BuildPieceCost("천조각", 6),
        };

        private static readonly BuildPieceCost[] EmptyCost = new BuildPieceCost[0];

        /// <summary>보관 상자의 등급 수(0=소 1=중 2=대 3=특대).</summary>
        public const int ChestTierCount = 4;

        /// <summary>등급별 칸 수. 인덱스가 곧 Tier다.</summary>
        private static readonly int[] ChestCapacities = { 50, 100, 150, 200 };

        private static readonly string[] ChestTierNames = { "소형 상자", "중형 상자", "대형 상자", "특대 상자" };

        /// <summary>등급을 0~3으로 자른다(세이브가 이상한 값을 들고 와도 표를 벗어나지 않게).</summary>
        public static int ClampChestTier(int tier)
        {
            if (tier < 0)
                return 0;
            if (tier > ChestTierCount - 1)
                return ChestTierCount - 1;
            return tier;
        }

        /// <summary>등급별 보관 칸 수(50 / 100 / 150 / 200).</summary>
        public static int GetChestCapacity(int tier)
        {
            return ChestCapacities[ClampChestTier(tier)];
        }

        /// <summary>등급별 한국어 이름("소형 상자" 등).</summary>
        public static string GetChestTierDisplayName(int tier)
        {
            return ChestTierNames[ClampChestTier(tier)];
        }

        /// <summary>
        /// tier에서 tier+1로 올리는 데 드는 재료. **최고 등급(3)이면 빈 목록**이다(null이 아니다).
        /// 매 호출 같은 배열 인스턴스를 돌려주므로 UI가 폴링해도 할당이 없다.
        /// </summary>
        public static IReadOnlyList<BuildPieceCost> GetChestUpgradeCost(int tier)
        {
            switch (tier)
            {
                case 0: return ChestUpgradeToMedium;
                case 1: return ChestUpgradeToLarge;
                case 2: return ChestUpgradeToHuge;
                default: return EmptyCost;
            }
        }

        /// <summary>메뉴/미리보기에 띄우는 한국어 이름.</summary>
        public static string GetDisplayName(BuildPieceType type)
        {
            switch (type)
            {
                case BuildPieceType.Floor: return "바닥";
                case BuildPieceType.Wall: return "벽";
                case BuildPieceType.Doorway: return "문";
                case BuildPieceType.Window: return "창문";
                case BuildPieceType.Stair: return "계단";
                case BuildPieceType.Roof: return "지붕";
                case BuildPieceType.Chest: return "보관 상자";
                default: return "건축 부품";
            }
        }

        /// <summary>
        /// 부품 하나의 재료표. **매 호출마다 같은 배열 인스턴스**를 돌려준다(할당 없음).
        /// 알 수 없는 타입이면 빈 목록이다 - null을 돌려주지 않으므로 호출부에 null 검사가 필요 없다.
        /// </summary>
        public static IReadOnlyList<BuildPieceCost> GetCost(BuildPieceType type)
        {
            switch (type)
            {
                case BuildPieceType.Floor: return FloorCost;
                case BuildPieceType.Wall: return WallCost;
                case BuildPieceType.Doorway: return DoorwayCost;
                case BuildPieceType.Window: return WindowCost;
                case BuildPieceType.Stair: return StairCost;
                case BuildPieceType.Roof: return RoofCost;
                // 상자는 소형만 지을 수 있다. 상위 등급은 GetChestUpgradeCost로만 간다.
                case BuildPieceType.Chest: return ChestCost;
                default: return EmptyCost;
            }
        }

        /// <summary>
        /// 아이콘으로 빌려 쓸 ItemData의 itemName. 이 프로젝트에는 건축 부품 전용 아이콘 에셋이 없어
        /// 부품을 대표하는 재료 아이콘을 빌린다. 전부 실재하는 이름이므로 null이 나오는 경로는 지금 없지만,
        /// 시그니처는 "없으면 null"을 유지한다(나중에 아이콘 없는 부품이 생겨도 호출부가 안 깨진다).
        /// </summary>
        public static string GetIconItemName(BuildPieceType type)
        {
            switch (type)
            {
                case BuildPieceType.Floor: return "나뭇가지";
                case BuildPieceType.Wall: return "나뭇가지";
                case BuildPieceType.Doorway: return "노끈";
                case BuildPieceType.Window: return "대나무";
                case BuildPieceType.Stair: return "나뭇가지";
                case BuildPieceType.Roof: return "야자잎";
                case BuildPieceType.Chest: return "노끈";
                default: return null;
            }
        }
    }
}
