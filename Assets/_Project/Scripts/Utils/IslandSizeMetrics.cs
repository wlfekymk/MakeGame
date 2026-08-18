namespace MakeGame.Data
{
    /// <summary>
    /// 섬 규모(IslandSize)에 대응하는 공용 수치(지형 반지름, 콘텐츠 산포 반경, 밀도 배율)의 "안전한 기본값"을
    /// 한 곳에서 관리하는 정적 유틸리티.
    /// [역할 - 긴급 정정 후 재정의] 최초 버전에서는 이 클래스가 IslandResourceSpawner/HazardSpawner/
    /// CreatureSpawner/BoatBlueprintSpawner의 배율·반경 필드를 완전히 대체하려 했으나, 실제 배포된
    /// SampleScene.unity에 그 필드들이 이미 배치되어 있고 코드 기본값과 다른 값(디자이너가 조정한 실제
    /// 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되어 필드를 전부 복원했다("스테이징 범위에 씬
    /// 파일이 없다"가 "프로젝트에 씬 파일이 없다"를 뜻하지 않는다는 교훈).
    /// 그래서 이 클래스는 이제 "정답"이 아니라 "폴백"이다: 각 스포너는 인스펙터(씬 직렬화) 필드 값을
    /// 항상 우선 사용하고, 그 필드가 0 이하로 남아있어 의미 있게 설정되지 않은 경우에만(예: 새로 추가된
    /// 컴포넌트가 아직 씬에서 값을 받지 못한 경우) 이 클래스의 값으로 대체한다. 그래도 지형 반지름
    /// (GetTerrainRadius, WorldMapManager.GetSizeScale이 위임)만은 애초에 public 필드가 아니라 코드
    /// 내부 switch였으므로 씬 값과 무관하며 계속 이 클래스가 단일 소스다.
    /// </summary>
    public static class IslandSizeMetrics
    {
        /// <summary>
        /// [B4] 섬 규모에 대응하는 값을 네 후보 중에서 고르는 공용 선택기.
        /// 각 스포너(HazardSpawner/CreatureSpawner/IslandResourceSpawner)의 GetMultiplier/GetScatterRadius가
        /// 필드 이름만 다르게 복붙하던 4-case switch의 단일 구현이다. 알 수 없는 enum 값(default)은
        /// 세 호출부 모두와 동일하게 Small 후보로 폴백한다 - 순수 선택 함수라 rng를 소비하지 않으며,
        /// 호출부가 "필드>0이면 필드, 아니면 IslandSizeMetrics 폴백" 판정을 그대로 이어서 하므로
        /// 수치·평가 순서가 종전과 1비트도 다르지 않다.
        /// </summary>
        public static float SelectBySize(IslandSize size, float small, float medium, float large, float extraLarge)
        {
            switch (size)
            {
                case IslandSize.Small: return small;
                case IslandSize.Medium: return medium;
                case IslandSize.Large: return large;
                case IslandSize.ExtraLarge: return extraLarge;
                default: return small;
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 절차적 지형(섬 메시)의 반지름(미터)을 반환한다.
        /// WorldMapManager.GetSizeScale이 위임하는 유일한 소스다(public 필드가 아니므로 씬 값과 무관,
        /// 폴백이 아니라 정답). 값: Small=50, Medium=90, Large=140, ExtraLarge=200.
        /// </summary>
        public static float GetTerrainRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 50f;
                case IslandSize.Medium: return 90f;
                case IslandSize.Large: return 140f;
                case IslandSize.ExtraLarge: return 200f;
                default: return 50f;
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 콘텐츠(자원/위험요소/사냥감/도면) 산포 반경의 "폴백" 값을 반환한다.
        /// [주의] 각 스포너의 scatterRadius류 인스펙터 필드(씬 직렬화 값)가 항상 우선이며, 이 메서드는
        /// 그 필드가 0 이하로 비어 있을 때만 쓰이는 안전망이다 - 실제 게임플레이에 쓰이는 값이 아니다.
        /// 값: Small=40, Medium=72, Large=112, ExtraLarge=160 (지형 반지름의 80%).
        /// </summary>
        public static float GetScatterRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 40f;
                case IslandSize.Medium: return 72f;
                case IslandSize.Large: return 112f;
                case IslandSize.ExtraLarge: return 160f;
                default: return 40f;
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 "면적 비례" 밀도 배율의 "폴백" 값을 반환한다(IslandResourceSpawner용).
        /// [주의] IslandResourceSpawner의 multiplier류 인스펙터 필드(씬 직렬화 값)가 항상 우선이며,
        /// 이 메서드는 그 필드가 0 이하로 비어 있을 때만 쓰이는 안전망이다.
        /// 값: Small=1, Medium=3.24, Large=7.84, ExtraLarge=16.
        /// </summary>
        public static float GetAreaProportionalMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 1f;
                case IslandSize.Medium: return 3.24f;
                case IslandSize.Large: return 7.84f;
                case IslandSize.ExtraLarge: return 16f;
                default: return 1f;
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 "선형" 밀도 배율의 "폴백" 값을 반환한다(HazardSpawner/CreatureSpawner용).
        /// [주의] 각 스포너의 multiplier류 인스펙터 필드(씬 직렬화 값)가 항상 우선이며, 이 메서드는
        /// 그 필드가 0 이하로 비어 있을 때만 쓰이는 안전망이다.
        /// 값: Small=1, Medium=1.5, Large=2, ExtraLarge=2.5.
        /// </summary>
        public static float GetLinearDensityMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 1f;
                case IslandSize.Medium: return 1.5f;
                case IslandSize.Large: return 2f;
                case IslandSize.ExtraLarge: return 2.5f;
                default: return 1f;
            }
        }
    }
}
