namespace MakeGame.Data
{
    /// <summary>
    /// 섬(또는 바다)에 존재할 수 있는 위험 요소 종류.
    /// 섬 규모/지역별 구체적인 분포 규칙은 추후 결정 예정 (Docs/Story/story_dictionary.json의 openQuestions 참고).
    /// Shark만 예외적으로 섬이 아니라 섬 사이의 깊은 바다에 배치된다 (SharkSpawner 참고).
    /// </summary>
    public enum HazardType
    {
        // 주석 보강(B2-7, Spec_16 - 실측 검증: HazardSpawner.hazardEntries에는 현재 이 두 값이 없다):
        // FoodShortage/Dehydration은 SurvivalStats의 허기/갈증 감소 로직(UpdateHungerAndThirst)이
        // 이미 그 효과를 담당하고 있어, HazardSource.ApplyHazardEffect에도 이 두 케이스는 "별도 효과
        // 없음"으로 의도적으로 비워져 있다(HazardSource.cs 참고). 즉 스포너가 개별 오브젝트로 배치해
        // 접촉 판정을 만들 대상이 아니라 상시 시스템으로 이미 구현된 "개념적" 위험 요소다.
        // HazardSpawner.hazardEntries 목록에 절대 추가하지 말 것 - 넣어도 접촉 시 아무 효과가 없고
        // (죽은 콘텐츠), 존재 이유는 게임 오버 사망 원인 등 다른 시스템에서 개념적으로 참조하기 위함이다.
        FoodShortage,   // 음식 부족 (효과는 SurvivalStats 허기 감소 로직이 담당 - 스포너 목록에 넣지 말 것)
        VenomousSnake,  // 독사
        Scorpion,       // 전갈
        Bear,           // 곰
        BeeSwarm,       // 벌떼
        Trap,           // 함정
        Cannibal,       // 식인종
        Dehydration,    // 탈수 (효과는 SurvivalStats 갈증 감소 로직이 담당 - 스포너 목록에 넣지 말 것)
        Shark           // 상어 (수영/잠수 중에만 만나는 바다 위험요소)
    }
}
