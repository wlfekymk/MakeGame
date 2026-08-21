namespace MakeGame.Systems
{
    /// <summary>
    /// 뗏목에 실리는 물건의 무게표 - **무엇이 얼마나 무거운가**의 단일 출처.
    ///
    /// [왜 ItemData에 무게 필드를 넣지 않았나] 이 프로젝트의 ItemData에는 무게가 없고,
    /// 그나마 무게를 유추할 수 있는 materialFamily는 기존 .asset 대부분이 None이라(ItemData 주석)
    /// 지금 상태로는 쓸 수 없다. 필드를 새로 넣으면 31개 에셋을 전부 손으로 채워야 하고, 그건
    /// 이 작업 범위(.asset 편집 금지) 밖이다. 그래서 **이름 문자열 대조**로 간다 - 재료 대조가
    /// 전부 이름인 이 프로젝트의 기존 관례 그대로다(RaftBuildCatalog.CountOwned 참고).
    ///
    /// [단위] 바닥판 한 칸(통나무)의 부력이 1.0이다. 그 위에서 읽으면 된다 -
    /// 금속조각 하나가 0.14면 통나무 바닥판 한 칸이 금속조각 일곱 개를 겨우 든다는 뜻이다.
    ///
    /// [표에 없는 아이템] Default(0.06)로 본다. 이 값은 무게 개념이 들어오기 전에 상자 내용물
    /// 한 칸에 매기던 값 그대로라, 표에 없는 물건의 무게는 예전과 정확히 같다.
    /// </summary>
    public static class RaftLoadCatalog
    {
        /// <summary>표에 없는 아이템 하나의 무게(부력 단위).</summary>
        public const float DefaultItemUnits = 0.06f;

        /// <summary>돌·금속처럼 무거운 것.</summary>
        private const float Heavy = 0.16f;

        /// <summary>나무처럼 부피는 있지만 물에 뜨는 것.</summary>
        private const float Medium = 0.08f;

        /// <summary>천·끈·잎처럼 가벼운 것.</summary>
        private const float Light = 0.02f;

        /// <summary>
        /// 이름 → 무게. **실제로 존재하는 itemName만 적는다** - 오타는 조용히 Default로 떨어져
        /// "무게를 매겼는데 안 먹는" 상태가 되므로, 새 줄을 넣을 때는 반드시 에셋 이름을 확인할 것.
        /// </summary>
        private static readonly (string itemName, float units)[] Table =
        {
            // 무거운 것 - 이것들을 쌓으면 뗏목이 눈에 띄게 잠긴다.
            ("금속조각", Heavy),
            ("석재", Heavy),
            ("돌조각", Heavy),
            ("대리석", Heavy),
            ("강철", Heavy),
            ("엔진부품", Heavy),
            ("산소통", Heavy),
            ("용광로키트", Heavy),
            ("제작대키트", Heavy),
            ("물증류기키트", Heavy),
            ("훈연기키트", Heavy),
            ("베틀키트", Heavy),

            // 중간 - 목재류와 부피 있는 도구.
            ("나뭇가지", Medium),
            ("대나무", Medium),
            ("부목", Medium),
            ("낚싯대", Medium),
            ("창", Medium),
            ("정제 창", Medium),
            ("손도끼", Medium),
            ("정제 손도끼", Medium),
            ("모닥불키트", Medium),
            ("쉼터키트", Medium),
            ("밭키트", Medium),
            ("물통", Medium),
            ("연료", Medium),

            // 부력통은 그 자체가 뜨는 물건이라 짐으로서는 거의 무게가 없다.
            ("부력통", Light),

            // 가벼운 것.
            ("노끈", Light),
            ("천조각", Light),
            ("야자잎", Light),
            ("약초", Light),
            ("해조류", Light),
            ("붕대", Light),
            ("미끼", Light),
            ("부싯돌", Light),
            ("진주", Light),
            ("상어 이빨", Light),
            ("곰치 턱뼈", Light),
            ("촉수 표본", Light),
            ("야자씨앗", Light),
            ("약초씨앗", Light),
            ("해조류씨앗", Light),
        };

        /// <summary>아이템 하나의 무게(부력 단위). 이름이 표에 없으면 Default.</summary>
        public static float GetItemUnits(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return DefaultItemUnits;

            for (int i = 0; i < Table.Length; i++)
            {
                if (Table[i].itemName == itemName)
                    return Table[i].units;
            }

            return DefaultItemUnits;
        }
    }
}
