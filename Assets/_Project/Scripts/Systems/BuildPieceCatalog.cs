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

        private static readonly BuildPieceCost[] EmptyCost = new BuildPieceCost[0];

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
                default: return null;
            }
        }
    }
}
