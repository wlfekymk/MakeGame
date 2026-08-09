using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 탈출선(배) 제작 엔딩의 진행 상태를 관리하는 시스템.
    /// 총 3단계로 구성되며, 각 단계는 "도면 습득 → 재료 확보 → 제작 완료" 순서로 진행된다.
    /// 1~2단계 도면은 대형(대) 섬에서, 3단계(최종) 도면은 특대 섬에서만 습득할 수 있다.
    /// </summary>
    public class BoatConstructionSystem : MonoBehaviour
    {
        /// <summary>배 제작 전체 단계 수.</summary>
        public const int TotalStages = 3;

        [Tooltip("현재 진행 중인 단계 (1~3)")]
        public int currentStage = 1;

        [Tooltip("현재 단계의 도면을 습득했는지 여부")]
        public bool hasCurrentStageBlueprint = false;

        [Tooltip("현재 단계 제작을 위해 확보한 재료 목록")]
        public List<string> collectedMaterialsForCurrentStage = new List<string>();

        [Tooltip("현재 단계 제작에 필요한 전체 재료 목록 (세부 재료 설계 확정 전까지 임시 값)")]
        public List<string> requiredMaterialsForCurrentStage = new List<string>();

        /// <summary>
        /// 지정한 규모의 섬에서 현재 단계 도면을 습득할 수 있는지 확인한다.
        /// 1~2단계는 대형 섬, 3단계(최종)는 특대 섬에서만 습득 가능하다.
        /// </summary>
        public bool CanFindBlueprintOnIsland(IslandSize islandSize)
        {
            if (currentStage <= 2)
                return islandSize == IslandSize.Large;

            return islandSize == IslandSize.ExtraLarge;
        }

        /// <summary>
        /// 도면을 습득했을 때 호출한다. 현재 단계에 맞는 섬이 아니면 무시한다.
        /// </summary>
        public void ObtainBlueprint(IslandSize islandSize)
        {
            if (CanFindBlueprintOnIsland(islandSize))
                hasCurrentStageBlueprint = true;
        }

        /// <summary>
        /// 재료를 하나 확보했을 때 호출한다. 이미 확보한 재료는 중복 추가하지 않는다.
        /// </summary>
        public void CollectMaterial(string materialId)
        {
            if (!collectedMaterialsForCurrentStage.Contains(materialId))
                collectedMaterialsForCurrentStage.Add(materialId);
        }

        /// <summary>
        /// 현재 단계를 완료하고 다음 단계로 넘어갈 수 있는지 확인한다.
        /// 도면을 보유하고, 필요한 재료를 모두 확보했어야 한다.
        /// </summary>
        public bool CanAdvanceStage()
        {
            if (!hasCurrentStageBlueprint)
                return false;

            foreach (var required in requiredMaterialsForCurrentStage)
            {
                if (!collectedMaterialsForCurrentStage.Contains(required))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 조건을 만족하면 다음 단계로 진행한다.
        /// 이미 마지막 단계(3단계)에서 조건을 만족하면 배가 100% 완성된 것으로 보고 true를 반환한다.
        /// </summary>
        public bool TryAdvanceStage()
        {
            if (!CanAdvanceStage())
                return false;

            if (currentStage >= TotalStages)
                return true; // 3단계까지 모두 완료 - 배 100% 완성

            currentStage++;
            hasCurrentStageBlueprint = false;
            collectedMaterialsForCurrentStage.Clear();
            requiredMaterialsForCurrentStage.Clear();
            return false;
        }

        /// <summary>
        /// 배 제작 전체 진행률(0~1)을 대략적으로 반환한다. 완료된 단계 수 기준이며 단계 내 세부 진행은 반영하지 않는다.
        /// </summary>
        public float GetOverallProgress()
        {
            return (float)(currentStage - 1) / TotalStages;
        }
    }
}
