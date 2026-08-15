# B2-11 ItemData 재질 계열 필드 설계 (MaterialFamily)

## 목적
`UIBuilder.GetMaterialSubCategoryColor`(UIBuilder.cs:233-257)와
`IslandResourceSpawner.GetSurfaceTextureName`(IslandResourceSpawner.cs:417-435)가 각각 독립적으로
**아이템 이름 문자열(`itemName.Contains(...)`)**을 파싱해 재질 계열(목재/석재/금속/식물 등)을
추론한다. 이름이 바뀌거나 새 아이템이 추가되면 두 곳 중 한쪽만 갱신되고 다른 쪽은 조용히
깨질 수 있다. `ItemData`에 `MaterialFamily` enum 필드를 추가해 단일 소스로 통합하는 설계를
제시한다. **이번 배치는 설계만 — `.cs`/`.asset` 실제 반영은 다음 배치.**

## 스키마 설계

```csharp
namespace MakeGame.Data
{
    /// <summary>
    /// 아이템의 재질 계열. UIBuilder(인벤토리 아이콘 색)와 IslandResourceSpawner(월드 표면 텍스처)가
    /// 지금까지 각자 itemName 문자열을 파싱해 추론하던 것을 단일 필드로 통합하기 위한 분류.
    /// 원재료가 아닌 완제품/도구/음식/음료/설치키트 등은 None으로 둔다(재질 텍스처 매핑 대상이 아님).
    /// </summary>
    public enum MaterialFamily
    {
        None = 0,   // 해당 없음 (완제품/도구/음식/음료/설치키트/탈것 등)
        Wood,       // 목재
        Stone,      // 석재
        Metal,      // 금속/기계
        Fiber,      // 식물섬유/천
        Fruit,      // 열매
        Supply,     // 표류 보급품(비상식량/연료/부력통류)
    }
}
```

`ItemData.cs`에 추가할 필드: `[Header("재질 계열")] public MaterialFamily materialFamily = MaterialFamily.None;`

## 31개 아이템 전체 할당표

| 아이템 | 현재 ItemCategory(UIBuilder) | MaterialFamily(신규) | 비고 |
|---|---|---|---|
| 고무보트 | Vehicle | None | 완제품 |
| 구운고기 | Food | None | 조리 결과물 |
| 구운생선 | Food | None | 조리 결과물 |
| 금속조각 | Material | **Metal** | |
| 나뭇가지 | Material | **Wood** | |
| 노끈 | Material | **Fiber** | ⚠ 아래 "발견 1" 참고 |
| 대나무 | Material | **Wood** | |
| 돌조각 | Material | **Stone** | |
| 라이터 | Material | None | 완제품 도구 |
| 모닥불키트 | Placeable | None | 완제품 키트 |
| 물증류기키트 | Placeable | None | 완제품 키트 |
| 물통 | Material | None | 완제품 도구 |
| 부력통 | Material | **Supply** | |
| 부목 | Cure | None | 완제품 치료 아이템 |
| 부싯돌 | Material | **Stone** | |
| 붕대 | Cure | None | 완제품 치료 아이템 |
| 비상식량 | Food | **Supply** | ⚠ 아래 "발견 2" 참고 |
| 생고기 | Food | None | 원재료지만 재질 계열 무관(음식) |
| 생선 | Food | None | 〃 |
| 생수 | Drink | None | |
| 손도끼 | Weapon | None | 완제품 도구 |
| 쉼터키트 | Placeable | None | 완제품 키트 |
| 야자잎 | Material | **Fiber** | |
| 엔진부품 | Material | **Metal** | ⚠ 아래 "발견 3" 참고 |
| 연료 | Material | **Supply** | |
| 창 | Weapon | None | 완제품 무기 |
| 천조각 | Material | **Fiber** | ⚠ 아래 "발견 4" 참고 |
| 칼 | Weapon | None | 완제품 무기 |
| 코코넛 | Drink | **Fruit** | ⚠ 아래 "발견 5" 참고 |
| 파이어스타터 | Material | None | 완제품 도구 |
| 해독제 | Cure | None | 완제품 치료 아이템 |

## 이번 조사로 드러난 기존 버그(문자열 추론의 실제 부작용)

기존 `UIBuilder.GetItemCategory`는 `ItemCategory`(Weapon/Cure/Food/Drink/Placeable/Vehicle/
Material)를 **재질 계열보다 먼저** 판정하고, `GetMaterialSubCategoryColor`는 그중 `Material`로
떨어진 아이템에만 호출된다. 그런데 `GetMaterialSubCategoryColor` 안에는 이미 다른 카테고리로
먼저 걸러져 **절대 도달할 수 없는 죽은 분기**가 섞여 있었다:

1. **`노끈`/`라이터`/`물통`/`파이어스타터`**: `Material` 카테고리로 떨어지지만
   `GetMaterialSubCategoryColor`의 어떤 `Contains` 키워드에도 안 걸려 전부 "그 외 미분류
   기본 갈색"으로 뭉뚱그려진다. 새 필드에서는 노끈만 `Fiber`(야자잎을 꼬아 만든 것이므로)로
   명확히 분류하고, 나머지 3개(라이터/물통/파이어스타터)는 완제품 도구이므로 `None`으로 둔다 —
   더 이상 재질로 뭉뚱그리지 않는다.
2. **`비상식량`**: `hungerRestoreAmount=20`이 있어 `GetItemCategory`에서 이미 `Food`로 분류되고,
   `GetMaterialSubCategoryColor`의 "부력통·비상식량·연료" 보급품(Supply) 키워드 분기까지 절대
   도달하지 못한다 — **완전한 죽은 코드**였다. 새 필드에서는 재질 계열 자체는 `Supply`로 정확히
   부여해두되(향후 월드 자원 노드 텍스처링 등에 재사용 가능하도록), 인벤토리 아이콘 색은 여전히
   `ItemCategory.Food`가 우선하도록 유지 권장(음식이라는 정보가 재질보다 더 중요한 UX 정보이므로
   식별 우선순위는 유지, `MaterialFamily`는 부가 정보로만 사용).
3. **`엔진부품`**: `UIBuilder`의 재질 색상 분류에는 "금속조각·엔진부품"이 같은 그룹(Metal)으로
   묶여 있는데, `IslandResourceSpawner.GetSurfaceTextureName`의 실제 표면 텍스처 판정에는
   `엔진부품`이 빠져 있어(금속조각만 `"metal"` 텍스처, 엔진부품은 매칭 실패로 기본 `"noise"`
   텍스처) **색은 금속인데 질감은 금속이 아닌 불일치**가 있었다. 새 필드로 통합하면
   `MaterialFamily.Metal` 하나로 색과 텍스처가 항상 같이 움직이므로 이 불일치가 자동으로
   해소된다.
4. **`천조각`**: `UIBuilder`는 "야자잎·천조각"을 같은 색 그룹(올리브, 식물/섬유 계열)으로 묶지만,
   `IslandResourceSpawner`의 텍스처 판정에는 "야자잎"만 `"leaf"`로 매핑되고 천조각은 매칭 실패로
   `"noise"`가 적용된다 — 마찬가지로 색-질감 불일치. `MaterialFamily.Fiber`로 통합 시 천조각도
   `"leaf"` 텍스처를 쓰게 되어 현재 시각 결과가 **미세하게 바뀐다**(천조각이 지금은 무늬 없는
   `noise`, 통합 후에는 야자잎과 같은 결 무늬 `leaf`로 보임). 이 변경이 의도한 개선인지
   tech-artist 확인 필요.
5. **`코코넛`**: `thirstRestoreAmount=50`(코코넛워터 소스)이 있어 `Drink` 카테고리로 먼저
   분류되고, `GetMaterialSubCategoryColor`의 "코코넛" 키워드 분기도 **죽은 코드**였다. 재질
   계열은 `Fruit`로 부여하되(향후 월드 자원 노드용), 인벤토리 색은 `Drink`가 그대로 우선한다.

## 코드 반영 시 권장 매핑 (다음 배치, systems-engineer 참고용)
- `UIBuilder.GetItemCategoryColor`: `Material` 폴백 분기에서 `item.materialFamily`로 직접
  switch(문자열 파싱 제거). `None`이면 기존 "미분류 기본 갈색" 유지.
- `IslandResourceSpawner.GetSurfaceTextureName`: `item.materialFamily`로 switch. 매핑:
  `Wood→"wood"`, `Stone→"stone"`, `Metal→"metal"`, `Fiber→"leaf"`,
  `Fruit`/`Supply`/`None→"noise"`(현행 동작 최대한 보존, `Fruit` 전용 텍스처 신설은 tech-artist
  판단에 맡김).
- 두 함수 모두 `itemName.Contains(...)` 로직 완전 제거.

## 수정 대상 (다음 배치)
- `Assets/_Project/Scripts/Utils/MaterialFamily.cs` (신규 enum, systems-engineer)
- `Assets/_Project/Scripts/Utils/ItemData.cs`에 `materialFamily` 필드 추가 (systems-engineer)
- `Assets/_Project/ScriptableObjects/Item_*.asset` 31개에 값 채우기 (game-designer, 위 표 기준)
- `UIBuilder.cs`/`IslandResourceSpawner.cs` 문자열 매칭 제거 및 필드 참조로 교체 (systems-engineer)

## 수용 기준
- 본 문서의 31개 할당표가 최종 값으로 확정됨(완료).
- 이번 배치엔 `.cs`/`.asset` 변경 없음 — 설계 문서만 산출물.
- 다음 배치에서 필드 추가 후, 기존 인벤토리/월드 자원 색상·텍스처가 **의도치 않게** 바뀌는
  항목은 천조각(`noise`→`leaf`) 하나뿐임을 qa-reviewer가 diff로 재확인.

## 담당 제안
- 설계 및 31개 할당: game-designer (완료, 본 문서)
- enum/필드/스포너 코드 반영: systems-engineer (다음 배치)
- `.asset` 값 기입: game-designer (다음 배치, 필드 존재 이후)
- 천조각 텍스처 변경 승인: tech-artist
