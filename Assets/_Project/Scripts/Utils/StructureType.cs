namespace MakeGame.Data
{
    /// <summary>
    /// B2-15: 플레이어가 설치(빌드)한 구조물의 종류. 세이브 파일에서 구조물 하나가 어떤 컴포넌트로
    /// 복원돼야 하는지 구분하는 용도로 쓰인다(Systems.SaveData.StructureSaveEntry.type 참고).
    /// </summary>
    public enum StructureType
    {
        Campfire,   // 모닥불
        Shelter,    // 쉼터
        WaterStill  // 물 증류기
    }
}
