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
        Large,      // 대형 섬 - 기본 등장 확률 15%. 희귀 재료(금속조각/부력통/대리석)와 수중 동굴이 여기부터 나온다.
                    // 시작 섬에서 가장 가까운 대형 섬도 11.8 km다(MaldivesLayout).
                    // 고무보트로 처음부터 갈 수 있다 (예전엔 여기도 막혀 있어 배/경비행기 엔딩이 전부 소프트락이었음 - islandTravelSoftlockFix/boatEndingSoftlockFix 참고)
        ExtraLarge  // 특대 섬 - 기본 등장 확률 5%. 엔진부품 자원 노드와 경비행기 잔해가 여기에만 있다.
                    // 시작 섬에서 19.0 km라 해류가 강하고, 뗏목이 대양 규격 + 모터여야 뚫린다
                    // (IslandTravel.CurrentBypass.OceanReadyWithMotor). 고무보트만으로는 못 간다.
    }
}
