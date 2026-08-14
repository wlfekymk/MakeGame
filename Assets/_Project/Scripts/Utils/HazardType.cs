namespace MakeGame.Data
{
    /// <summary>
    /// 섬(또는 바다)에 존재할 수 있는 위험 요소 종류.
    /// 섬 규모/지역별 구체적인 분포 규칙은 추후 결정 예정 (Docs/Story/story_dictionary.json의 openQuestions 참고).
    /// Shark만 예외적으로 섬이 아니라 섬 사이의 깊은 바다에 배치된다 (SharkSpawner 참고).
    /// </summary>
    public enum HazardType
    {
        FoodShortage,   // 음식 부족
        VenomousSnake,  // 독사
        Scorpion,       // 전갈
        Bear,           // 곰
        BeeSwarm,       // 벌떼
        Trap,           // 함정
        Cannibal,       // 식인종
        Dehydration,    // 탈수
        Shark           // 상어 (수영/잠수 중에만 만나는 바다 위험요소)
    }
}
