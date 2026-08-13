using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬과 섬 사이를 뗏목/보트로 이동하는 시스템.
    /// 고무보트(무제한 사용, 단 대형 섬부터는 해류 제약으로 못 감)를 이용해 발견된 섬으로 이동한다.
    /// 뗏목/배가 완성되면 더 먼 섬까지 갈 수 있도록 추후 확장할 수 있다.
    /// </summary>
    public class IslandTravel : MonoBehaviour
    {
        [Tooltip("섬 목록/배치 정보를 가진 월드맵 매니저")]
        public WorldMapManager worldMapManager;

        [Tooltip("이동 수단으로 사용하는 고무보트 아이템 (해류 제약 판정에 사용)")]
        public ItemData rubberBoatItem;

        [Tooltip("현재 플레이어가 위치한 섬 번호 (0번은 항상 불시착한 시작 섬)")]
        public int currentIslandId = 0;

        [Tooltip("뗏목(배) 제작 진행도를 확인할 시스템. 비워두면 뗏목 진행도에 의한 이동 범위 확장은 적용되지 않는다.")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("이 단계까지 배(뗏목)를 완성하면 고무보트의 해류 제약(대형/특대 섬 접근 불가)을 무시하고 갈 수 있다.")]
        public int stageRequiredToBypassCurrent = 1;

        /// <summary>
        /// 지정한 섬으로 이동을 시도한다.
        /// 목적지가 존재하고, 플레이어가 고무보트를 보유하고 있어야 한다.
        /// 뗏목(배) 제작이 stageRequiredToBypassCurrent 단계 이상 진행되지 않았다면,
        /// 고무보트만으로는 대형/특대 섬까지 해류를 뚫고 갈 수 없다.
        /// 성공 시 목적지 섬을 발견 상태로 표시하고 현재 위치를 갱신한다.
        /// </summary>
        public bool TryTravelTo(int destinationIslandId, PlayerInventory inventory)
        {
            if (worldMapManager == null || inventory == null)
                return false;

            var destination = worldMapManager.GetIsland(destinationIslandId);
            if (destination == null)
                return false;

            if (rubberBoatItem != null)
            {
                if (inventory.GetItemCount(rubberBoatItem) <= 0)
                    return false;

                bool raftOvercomesCurrent = boatConstruction != null && boatConstruction.HasCompletedStage(stageRequiredToBypassCurrent);

                if (!raftOvercomesCurrent && !inventory.CanCarryToIsland(rubberBoatItem, destination.size))
                    return false; // 고무보트만으로는 대형/특대 섬까지 해류를 뚫고 갈 수 없음
            }

            worldMapManager.DiscoverIsland(destinationIslandId);
            currentIslandId = destinationIslandId;
            return true;
        }
    }
}
