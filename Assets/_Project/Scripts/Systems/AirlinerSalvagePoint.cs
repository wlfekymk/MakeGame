using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 여객기 잔해(AirlinerWreck) 객실 내부/주변의 1회 한정 부품 수거 지점.
    /// AirlinerWreck.BuildSalvagePoints가 AddComponent로 붙이고 지급표를 코드로 채운다
    /// (인스펙터 배선 없음). 상호작용은 InteractionController가 TryCollect로,
    /// 프롬프트는 InteractionPromptUI가 HasLoot/displayName으로 처리한다.
    ///
    /// 아이템 지급은 AirlinerWreck.TrySearch와 **완전히 같은 패턴**을 쓴다: 레지스트리에서
    /// 이름으로 ItemData를 찾아 TryAddItem으로 넣고(용량 존중 - 아이템이 조용히 사라진 사고가
    /// 4번 있었던 프로젝트다), 가방이 차서 못 넣은 것은 pendingLoot에 남겨 다음 수거에서
    /// 이어받는다. 그 공용 로직(BuildLootList/GrantPending)은 이 클래스의 static으로 두고
    /// AirlinerWreck.TrySearch도 이것을 호출한다 - 지급 규칙이 두 벌로 갈라지지 않게.
    ///
    /// [한계] 수거 여부는 세이브에 저장하지 않는다 - 잔해는 월드 재생성마다 새로 만들어지는
    /// 배경 오브젝트라 로드할 때마다 부품이 리셋된다(AirlinerWreck.pendingSalvage와 같은 한계.
    /// 잔해 해체/수거 세이브 연동을 확장할 때 함께 넣을 예정이다).
    /// </summary>
    public class AirlinerSalvagePoint : MonoBehaviour
    {
        /// <summary>지급표 한 줄: 레지스트리 itemName + 개수. rng 없음 - 전부 고정 표다.</summary>
        [System.Serializable]
        public struct LootEntry
        {
            public string itemName;
            public int count;

            public LootEntry(string itemName, int count)
            {
                this.itemName = itemName;
                this.count = count;
            }
        }

        [Tooltip("프롬프트에 보여줄 지점 이름 (예: 조종석 계기판)")]
        public string displayName = "부품 더미";

        [Tooltip("수거 시 지급할 아이템 이름+수량 목록. 이름은 ItemDataRegistry의 itemName과 일치해야 한다.")]
        public LootEntry[] loot;

        /// <summary>
        /// 아직 지급하지 않은 부품(아이템 1개당 항목 1개). null = 아직 수거 안 함.
        /// 인벤토리가 꽉 차 일부만 들어가면 넘친 아이템이 여기 남아 다음 수거에서 이어진다.
        /// </summary>
        private List<ItemData> pendingLoot;

        /// <summary>아직 수거로 얻을 부품이 남아 있는가(수거 전이면 true). InteractionPromptUI가 쓴다.</summary>
        public bool HasLoot => pendingLoot == null || pendingLoot.Count > 0;

        /// <summary>
        /// 이 지점의 부품을 수거한다(1회 한정). InteractionController가 부른다.
        /// AirlinerWreck.TrySearch와 같은 규칙: 레지스트리 로드 실패면 수거를 소모하지 않고 false,
        /// 일부만 들어가도 true(나머지는 pendingLoot에 남는다), 하나도 못 넣으면 false
        /// (TryAddItem이 실패음/경고를 스스로 낸다).
        /// </summary>
        /// <returns>아이템을 하나라도 지급했으면 true.</returns>
        public bool TryCollect(MakeGame.Player.PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            if (pendingLoot == null)
            {
                var built = BuildLootList(loot, "[AirlinerSalvagePoint:" + displayName + "]");
                if (built == null)
                    return false; // 레지스트리 로드 실패 - 수거 소모 없이 다음 시도에서 재시도한다.
                pendingLoot = built;
            }

            if (pendingLoot.Count == 0)
                return false; // 이미 다 털었다(프롬프트는 HasLoot로 이 상태를 미리 보여준다).

            int granted = GrantPending(pendingLoot, inventory);
            if (granted > 0)
            {
                Debug.Log("[AirlinerSalvagePoint] '" + displayName + "' 수거: 부품 " + granted + "개 지급"
                    + (pendingLoot.Count > 0 ? " (가방이 차서 " + pendingLoot.Count + "개는 남음)" : " 완료"));
            }
            return granted > 0;
        }

        // ---------------------------------------------------------------------------------------
        // 공용 지급 로직 - AirlinerWreck.TrySearch(외부 1회 수색)와 이 컴포넌트(내부 수거 지점)가
        // 같은 코드를 쓴다. DebugHud.GrantDevelopmentMaterials와 같은 "레지스트리 이름 조회 +
        // TryAddItem" 패턴의 단일 소스.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// 지급표를 실제 ItemData 목록(아이템 1개당 항목 1개)으로 편다. 레지스트리 자체가 없으면
        /// null(재시도 가능), 개별 이름이 없으면 그 항목만 빼고 경고를 남긴다. rng는 소비하지 않는다.
        /// </summary>
        public static List<ItemData> BuildLootList(LootEntry[] entries, string logTag)
        {
            var registry = ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
            {
                Debug.LogWarning(logTag + " ItemDataRegistry를 불러오지 못해 지급 목록을 만들지 못했다.");
                return null;
            }

            var list = new List<ItemData>();
            if (entries == null)
                return list;

            for (int n = 0; n < entries.Length; n++)
            {
                ItemData found = null;
                for (int i = 0; i < registry.allItems.Count; i++)
                {
                    var candidate = registry.allItems[i];
                    if (candidate != null && candidate.itemName == entries[n].itemName)
                    {
                        found = candidate;
                        break;
                    }
                }

                if (found == null)
                {
                    Debug.LogWarning(logTag + " 지급표의 '" + entries[n].itemName
                        + "'을(를) 레지스트리에서 찾지 못해 지급 목록에서 뺐다.");
                    continue;
                }

                for (int c = 0; c < entries[n].count; c++)
                    list.Add(found);
            }
            return list;
        }

        /// <summary>
        /// pending 목록 앞에서부터 TryAddItem으로 지급하고, 들어간 항목만 목록에서 지운다.
        /// 실패 시 TryAddItem이 실패음 + AddRejected + 경고를 스스로 내므로 추가 알림은 없다.
        /// </summary>
        /// <returns>실제로 지급한 아이템 개수.</returns>
        public static int GrantPending(List<ItemData> pending, MakeGame.Player.PlayerInventory inventory)
        {
            int granted = 0;
            while (pending.Count > 0)
            {
                if (!inventory.TryAddItem(pending[0]))
                    break;
                pending.RemoveAt(0);
                granted++;
            }
            return granted;
        }
    }
}
