using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬에 배치되는 배 도면 습득 지점.
    /// 상호작용 시 이 오브젝트가 놓인 섬의 규모에 맞는 단계 도면을 지급한다.
    /// 1~2단계 도면은 대형 섬, 3단계 도면은 특대 섬에서만 실제로 습득에 성공한다 (BoatConstructionSystem 규칙에 따름).
    /// </summary>
    public class BoatBlueprintPickup : MonoBehaviour
    {
        [Tooltip("이 도면이 놓인 섬의 규모")]
        public IslandSize islandSize;

        [Tooltip("도면을 지급할 대상 배 제작 시스템")]
        public BoatConstructionSystem boatConstruction;

        /// <summary>
        /// 도면 습득을 시도한다. 현재 진행 단계와 섬 규모가 맞지 않으면 습득에 실패한다.
        /// </summary>
        public bool TryObtain()
        {
            if (boatConstruction == null)
                return false;

            bool canObtain = boatConstruction.CanFindBlueprintOnIsland(islandSize);
            boatConstruction.ObtainBlueprint(islandSize);
            return canObtain;
        }
    }
}
