namespace MakeGame.Data
{
    /// <summary>
    /// 섬의 규모를 나타내는 열거형.
    /// 소/중/대/특대 순으로 크기가 커지며, 등장 확률과 배 도면 습득 규칙이 다르게 적용된다.
    /// </summary>
    public enum IslandSize
    {
        Small,      // 소형 섬 - 기본 등장 확률 50%
        Medium,     // 중형 섬 - 기본 등장 확률 30%
        Large,      // 대형 섬 - 기본 등장 확률 15%. 배 도면 1~2단계 및 희귀 재료(금속조각/부력통/엔진부품) 습득 가능.
                    // 고무보트로 처음부터 갈 수 있다 (예전엔 여기도 막혀 있어 배/경비행기 엔딩이 전부 소프트락이었음 - islandTravelSoftlockFix/boatEndingSoftlockFix 참고)
        ExtraLarge  // 특대 섬 - 기본 등장 확률 5%. 배 도면 3단계(최종) 습득 가능. 고무보트는 해류가 강해 이 섬만은
                    // 배 1단계를 완성해야(stageRequiredToBypassCurrent) 갈 수 있음
    }
}
