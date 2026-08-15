namespace MakeGame.Data
{
    /// <summary>
    /// 아이템의 재질 계열. game-designer가 정리한 스펙(Docs/Spec_B2_11_MaterialFamilyField.md)에 맞춰,
    /// 지금까지 IslandResourceSpawner(표면 텍스처 결정)와 UIBuilder(카테고리 색상 결정)가 각자
    /// itemName 문자열을 개별적으로 판별해오던 것을, 하나의 명시적 필드(ItemData.materialFamily)로
    /// 대체하기 위한 열거형이다.
    /// [주의] 이름/순서를 절대 바꾸지 말 것 - ui-engineer가 UIBuilder에서 동시에 이 정확한 이름들을
    /// 참조하는 코드를 작성 중이다.
    /// </summary>
    public enum MaterialFamily
    {
        None,
        Wood,
        Stone,
        Metal,
        Fiber,
        Fruit,
        Supply,
    }
}
