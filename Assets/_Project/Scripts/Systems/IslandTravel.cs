using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬과 섬 사이를 뗏목/보트로 이동하는 시스템.
    /// 고무보트(무제한 사용, 단 특대 섬은 해류 제약으로 못 감)를 이용해 섬으로 이동한다.
    /// 대형 섬은 배 도면(1~2단계)을 구할 수 있는 유일한 장소라 처음부터 갈 수 있고,
    /// 특대 섬(최종 도면)만 뗏목 진행도가 stageRequiredToBypassCurrent 이상이어야 갈 수 있다
    /// (PlayerInventory.CanCarryToIsland 참고 - 예전에는 대형 섬까지 막아서 배 엔딩이 영원히
    /// 잠겨 있는 순환 잠금 버그가 있었다).
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

        [Tooltip("이 단계까지 배(뗏목)를 완성하면 고무보트의 해류 제약(특대 섬 접근 불가)을 무시하고 갈 수 있다.")]
        public int stageRequiredToBypassCurrent = 1;

        /// <summary>
        /// 지정한 섬으로 이동을 시도한다.
        /// 목적지가 존재하고, 플레이어가 고무보트를 보유하고 있어야 한다.
        /// 뗏목(배) 제작이 stageRequiredToBypassCurrent 단계 이상 진행되지 않았다면,
        /// 고무보트만으로는 특대 섬까지 해류를 뚫고 갈 수 없다 (대형 섬은 처음부터 갈 수 있다).
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
                    return false; // 고무보트만으로는 특대 섬까지 해류를 뚫고 갈 수 없음
            }

            worldMapManager.DiscoverIsland(destinationIslandId);
            currentIslandId = destinationIslandId;

            // 치명적 버그 수정 (1/2): 이동 성공 처리(발견 상태 갱신, currentIslandId 갱신)만 하고 정작
            // 플레이어 캐릭터의 실제 Transform은 한 번도 옮기지 않고 있었다. 그래서 미니맵에서 "이동"에
            // 성공해도 화면상 플레이어는 원래 있던 자리(대부분 시작 섬 근처)에 그대로 남아, 목적지 섬의
            // 도면/자원/위험요소에 실제로는 절대 다가갈 수 없었다 - 버튼은 눌리는데 게임플레이 상으로는
            // 아무 데도 못 가는 또 다른 형태의 소프트락이었다.
            TeleportPlayerToIsland(inventory.transform, destination);

            return true;
        }

        /// <summary>
        /// 플레이어를 목적지 섬 중심에서 약간 벗어난 위치(해안 근처)로 텔레포트시키고, 지형 위에 놓이도록
        /// TerrainSampler로 높이를 스냅한다. 섬 정중앙에 떨어뜨리면 자원 노드/도면 등과 겹쳐 어색하므로
        /// 살짝 오프셋을 둔다.
        /// 치명적 버그 수정 (2/2): 처음에는 CharacterController를 켜 둔 채로 transform.position만 바꿨는데,
        /// 디버그 로그로 확인해보니 대입 직후에는 값이 바뀌었다가 다음 프레임에 원래 위치로 되돌아가 버렸다.
        /// CharacterController가 매 프레임 내부적으로 캐시해 둔 캡슐 위치를 기준으로 다시 보정하기 때문에,
        /// 먼 거리 순간이동처럼 큰 폭의 Transform 변경은 CharacterController가 활성화된 상태에서는 반영되지
        /// 않고 도로 취소되는 Unity의 잘 알려진 함정이다. 그래서 텔레포트 직전에 CharacterController를
        /// 잠깐 꺼서 위치 대입이 방해받지 않게 한 뒤 다시 켠다.
        /// </summary>
        private void TeleportPlayerToIsland(Transform player, IslandInstance destination)
        {
            if (player == null)
                return;

            Vector3 landingSpot = destination.mapPosition + new Vector3(2f, 0f, 2f);
            Vector3 groundedSpot = TerrainSampler.SnapToGround(landingSpot);

            var characterController = player.GetComponent<CharacterController>();
            if (characterController != null)
                characterController.enabled = false;

            player.position = groundedSpot;

            if (characterController != null)
                characterController.enabled = true;
        }
    }
}
