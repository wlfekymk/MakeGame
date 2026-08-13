using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 경비행기 수리 엔딩의 진행 상태를 관리하는 시스템.
    /// 배(뗏목) 제작과 달리 단계 구분 없이 "필요 재료를 모두 모아 한 번에 수리 완료" 하는 단일 목표 방식이다.
    /// 엔진부품처럼 희귀한 재료가 필요해, 배 제작(여러 단계를 밟는 대신 꾸준한 자원 확보가 핵심)과는
    /// 다른 방식의 대체 엔딩 경로로 설계했다. 완료되면 배 엔딩과 마찬가지로 GameManager.CompleteEnding()을 호출한다.
    /// </summary>
    public class AircraftRepairSystem : MonoBehaviour
    {
        [Tooltip("경비행기 수리가 완료됐는지 여부")]
        public bool isRepairComplete = false;

        /// <summary>재료 하나와 필요 수량을 나타낸다 (BoatConstructionSystem.MaterialRequirement와 동일한 구조).</summary>
        [System.Serializable]
        public class MaterialRequirement
        {
            public ItemData item;
            [Min(1)]
            public int quantity = 1;
        }

        [Tooltip("경비행기 수리에 필요한 재료 목록 (엔진부품/금속조각/연료 등)")]
        public List<MaterialRequirement> requiredMaterials = new List<MaterialRequirement>();

        [Tooltip("지금까지 투입(확보)한 재료 수량 목록")]
        public List<MaterialRequirement> collectedMaterials = new List<MaterialRequirement>();

        /// <summary>
        /// 인벤토리에서 재료를 실제로 소모하여 수리에 투입한다.
        /// 인벤토리에 충분한 수량이 없으면 아무것도 소모하지 않고 실패한다.
        /// </summary>
        public bool ContributeMaterial(PlayerInventory inventory, ItemData item, int quantity)
        {
            if (isRepairComplete || inventory == null || item == null || quantity <= 0)
                return false;

            if (!inventory.RemoveItems(item, quantity))
                return false;

            AddCollected(item, quantity);
            return true;
        }

        /// <summary>투입된 재료 수량을 collectedMaterials에 누적 기록한다.</summary>
        private void AddCollected(ItemData item, int quantity)
        {
            foreach (var entry in collectedMaterials)
            {
                if (entry.item == item)
                {
                    entry.quantity += quantity;
                    return;
                }
            }

            collectedMaterials.Add(new MaterialRequirement { item = item, quantity = quantity });
        }

        /// <summary>지정한 재료를 지금까지 몇 개나 확보(투입)했는지 반환한다.</summary>
        public int GetCollectedQuantity(ItemData item)
        {
            foreach (var entry in collectedMaterials)
            {
                if (entry.item == item)
                    return entry.quantity;
            }
            return 0;
        }

        /// <summary>필요한 재료를 모두 확보했는지(수리 조건을 만족하는지) 확인한다.</summary>
        public bool CanCompleteRepair()
        {
            if (isRepairComplete)
                return false;

            foreach (var required in requiredMaterials)
            {
                if (GetCollectedQuantity(required.item) < required.quantity)
                    return false;
            }
            return true;
        }

        /// <summary>조건을 만족하면 경비행기 수리를 완료 처리한다. 완료 시 true를 반환한다.</summary>
        public bool TryCompleteRepair()
        {
            if (!CanCompleteRepair())
                return false;

            isRepairComplete = true;
            return true;
        }

        /// <summary>전체 진행률(0~1)을 필요 재료 대비 확보한 재료 비율로 대략 계산한다.</summary>
        public float GetOverallProgress()
        {
            if (isRepairComplete)
                return 1f;

            int totalRequired = 0;
            int totalCollected = 0;
            foreach (var required in requiredMaterials)
            {
                totalRequired += required.quantity;
                totalCollected += Mathf.Min(required.quantity, GetCollectedQuantity(required.item));
            }

            if (totalRequired <= 0)
                return 0f;

            return (float)totalCollected / totalRequired;
        }
    }
}
