using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 제작(크래프팅) 시스템. 재료 확인, 재료 소모, 결과물 지급, 스킬 경험치 지급을 처리한다.
    /// Stranded Deep처럼 쉼터/도구/뗏목 등 대부분의 진행이 이 제작 시스템을 통해 이루어진다.
    /// </summary>
    public class CraftingSystem : MonoBehaviour
    {
        [Tooltip("제작 재료를 확인/소모할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("제작 성공 시 경험치를 지급할 대상 스킬 목록")]
        public PlayerSkills skills;

        /// <summary>
        /// [ui-engineer 요청 - Design_Ending.md 4장] 이번 판에 한 번이라도 제작에 성공한 제작법의 집합.
        /// 엔딩/사망 화면의 "제작한 물건 종류" 통계가 읽는 유일한 소스다.
        ///
        /// 같은 제작법을 100번 만들어도 1로 센다(개수가 아니라 "종류"). 세이브에 넣지 않는 것은 설계
        /// 의도다 - 통계를 보여주는 세 화면(배 엔딩/비행기 엔딩/사망)이 전부 한 세션 안에서 완결되고,
        /// 세이브 스키마(SaveData)를 건드리면 기존 세이브 파일과의 호환을 따져야 해서 비용이 튄다.
        /// 불러오기로 이어서 하면 이 값은 0부터 다시 센다.
        ///
        /// private + 읽기 전용 접근자로 노출하는 이유: 외부에서 Add/Clear가 가능하면 "제작했다"는
        /// 사실의 정의가 이 클래스 밖으로 새어 나간다. 넣는 곳은 TryCraft의 성공 분기 하나뿐이다.
        /// </summary>
        private readonly HashSet<CraftingRecipe> craftedRecipes = new HashSet<CraftingRecipe>();

        /// <summary>
        /// 이번 판에 제작에 성공한 제작법의 "종류" 수. 엔딩/사망 화면 통계용 읽기 전용 값이다.
        /// </summary>
        public int CraftedRecipeCount => craftedRecipes.Count;

        /// <summary>
        /// 지정한 제작법을 이번 판에 한 번이라도 만든 적이 있는지. null이면 항상 false다.
        /// (튜토리얼 힌트처럼 "아직 안 만들어 본 것"을 고를 때 쓰라고 함께 노출한다.)
        /// </summary>
        public bool HasCrafted(CraftingRecipe recipe)
        {
            return recipe != null && craftedRecipes.Contains(recipe);
        }

        // ── 제작대 요구 ──────────────────────────────────────────────────────────
        //
        // [왜 표가 여기 있는가]
        // "이 제작법은 제작대가 필요하다"를 CraftingRecipe(ScriptableObject)의 새 필드로 두면 기존
        // 제작법 에셋 14개의 직렬화가 전부 어긋난다(그 파일은 이 작업의 락 밖이라 에셋을 다시 저장할
        // 수도 없다). 그래서 코드 안의 표 하나로만 정하고, 에셋은 한 글자도 건드리지 않는다.
        //
        // [왜 recipeName으로만 찾는가 - 결과 아이템 이름으로 찾으면 안 되는 이유]
        // 실측한 에셋 값이 그렇게 하지 못하게 막는다.
        //  · Recipe_강철주괴.resultItem == Recipe_강철.resultItem == Item_강철  → 결과물 이름("강철")으로
        //    찾으면 **맨손으로 남겨야 하는 기존 Recipe_강철까지** 용광로에 잠긴다(초반 진행이 끊긴다).
        //  · Recipe_노끈다발.resultItem == Recipe_노끈.resultItem == Item_노끈  → 같은 이유로 기존
        //    Recipe_노끈(맨손, Lv1)이 베틀에 잠긴다.
        // recipeName은 신규/기존이 서로 다르다("강철 주괴" ≠ "강철", "노끈 다발" ≠ "노끈")므로 이 표는
        // 신규 고급 제작법 6개에만 정확히 걸린다. 아래 문자열은 전부 에셋의 recipeName 실측값이다
        // (공백 위치까지 그대로 - "정제 칼"에는 공백이 있고 "천조각"에는 없다).
        //
        // 표에 없는 제작법은 예전과 100% 같다(맨손). 기존 세이브에도 영향이 없다 - 저장되는 것은
        // 인벤토리와 스킬뿐이고, 제작대 요구는 매번 현재 위치로 다시 판정하는 휘발성 조건이다.

        /// <summary>
        /// 제작대를 요구하는 제작법의 이름 → 필요한 시설 종류. 여기 없는 이름은 맨손 제작이다.
        /// 순서 비교가 필요 없는 정확 일치 조회라 Dictionary를 쓴다(비교는 서수 기준 - 문화권 설정에
        /// 따라 한국어 문자열 비교 결과가 달라지는 일이 없게 한다).
        /// </summary>
        private static readonly Dictionary<string, CraftStationKind> StationByRecipeName =
            new Dictionary<string, CraftStationKind>(System.StringComparer.Ordinal)
            {
                { "정제 칼", CraftStationKind.Workbench },
                { "정제 손도끼", CraftStationKind.Workbench },
                { "정제 창", CraftStationKind.Workbench },
                { "강철 주괴", CraftStationKind.Furnace },
                { "노끈 다발", CraftStationKind.Loom },
                { "천조각", CraftStationKind.Loom },
            };

        /// <summary>
        /// 이 제작법이 설치형 제작 시설을 요구하는지, 요구한다면 어떤 종류인지 알려준다.
        /// 요구하지 않으면 false(= 맨손 제작). 상태를 읽지 않는 순수 조회라 UI가 매 갱신마다 불러도 된다.
        /// </summary>
        public static bool TryGetRequiredStation(CraftingRecipe recipe, out CraftStationKind kind)
        {
            kind = CraftStationKind.Workbench;
            if (recipe == null || string.IsNullOrEmpty(recipe.recipeName))
                return false;

            return StationByRecipeName.TryGetValue(recipe.recipeName, out kind);
        }

        /// <summary>
        /// 제작대 요구를 판정할 때 쓸 플레이어 위치.
        ///
        /// inventory의 위치를 먼저 보는 이유: 이 컴포넌트는 씬에서 Player 오브젝트에 붙어 있어
        /// (SampleScene의 Player가 PlayerInventory/PlayerSkills/CraftingSystem을 함께 들고 있다)
        /// 두 값이 같지만, 나중에 이 시스템만 매니저 오브젝트로 옮기면 transform은 원점에 고정되고
        /// 판정이 조용히 무너진다. "재료를 들고 있는 쪽"이 곧 플레이어라는 사실은 그때도 변하지 않는다.
        /// </summary>
        private Vector3 CraftPosition =>
            inventory != null ? inventory.transform.position : transform.position;

        /// <summary>
        /// 이 제작법에 필요한 제작 시설이 지금 손 닿는 곳에 있는지. 시설이 필요 없는 제작법은 언제나 true다.
        /// 반경 판정은 <see cref="CraftStation.IsNear"/> 하나만 쓴다 - UI가 같은 판정을 따로 구현하면
        /// 화면에 보이는 것과 실제 제작 결과가 갈라진다.
        /// </summary>
        public bool HasRequiredStation(CraftingRecipe recipe)
        {
            if (!TryGetRequiredStation(recipe, out CraftStationKind kind))
                return true;

            return CraftStation.IsNear(CraftPosition, kind);
        }

        /// <summary>
        /// 제작이 막힌 이유가 "제작대가 없어서"인지 알려준다(막혔으면 필요한 시설 종류를 함께 준다).
        /// 실패 사유를 열거형/예외로 돌려주는 구조가 이 시스템에는 없다 - CanCraft는 bool 하나이고,
        /// 제작 창은 스킬 부족/재료 부족을 각각의 공개 조회로 직접 판정해 문구를 만든다(CraftingUI.RefreshAll).
        /// 그 방식을 그대로 따라, 제작대 부족도 같은 모양의 조회 하나로 노출한다.
        /// </summary>
        public bool IsMissingRequiredStation(CraftingRecipe recipe, out CraftStationKind kind)
        {
            if (!TryGetRequiredStation(recipe, out kind))
                return false;

            return !CraftStation.IsNear(CraftPosition, kind);
        }

        /// <summary>
        /// 지정한 제작법을 지금 제작할 수 있는지 확인한다.
        /// 필요 스킬 레벨을 만족하고, 필요한 재료를 모두 충분히 보유하고 있어야 하며,
        /// 제작대를 요구하는 제작법이라면 해당 시설 반경 안에 서 있어야 한다.
        /// </summary>
        public bool CanCraft(CraftingRecipe recipe)
        {
            if (recipe == null || inventory == null)
                return false;

            if (skills != null && skills.GetLevel(recipe.requiredSkill) < recipe.requiredSkillLevel)
                return false;

            // 제작대 판정은 재료 검사보다 앞에 둔다. 둘 다 실패를 뜻하므로 결과는 같지만, 재료를 세는
            // 반복문을 도는 것보다 표 조회 한 번이 싸고 - 표에 없는 제작법(= 기존 전부)은 여기서
            // 곧바로 통과하므로 예전 경로에 추가 비용이 사실상 없다.
            if (!HasRequiredStation(recipe))
                return false;

            foreach (var requirement in recipe.requiredMaterials)
            {
                if (inventory.GetItemCount(requirement.item) < requirement.quantity)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 제작을 시도한다. 제작 가능하면 재료를 소모하고 결과물을 지급하며, 관련 스킬에 경험치를 준다.
        /// 조건을 만족하지 못하면 아무 변화 없이 false를 반환한다.
        /// </summary>
        public bool TryCraft(CraftingRecipe recipe)
        {
            if (!CanCraft(recipe))
                return false;

            foreach (var requirement in recipe.requiredMaterials)
            {
                inventory.RemoveItems(requirement.item, requirement.quantity);
            }

            for (int i = 0; i < recipe.resultQuantity; i++)
            {
                inventory.AddItem(recipe.resultItem);
            }

            if (skills != null)
                skills.AddExperience(recipe.requiredSkill, recipe.experienceReward);

            // [ui-engineer 요청 - Design_Ending.md 4장] 성공 분기에서만 기록한다. CanCraft가 false면
            // 위에서 이미 return했으므로 "시도했지만 재료가 없었다"는 여기까지 오지 않는다.
            craftedRecipes.Add(recipe);

            AudioManager.Instance?.PlayCraftSuccess(); // 제작 성공 효과음
            return true;
        }
    }
}
