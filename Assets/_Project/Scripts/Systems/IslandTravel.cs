using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬과 섬 사이를 뗏목/보트로 이동하는 시스템.
    /// 고무보트(무제한 사용, 단 특대 섬은 해류 제약으로 못 감)를 이용해 섬으로 이동한다.
    /// 대형 섬까지는 고무보트만으로 처음부터 갈 수 있고, 특대 섬만 **해안에 지은 뗏목이 대양 규격이고
    /// 모터까지 달려 있어야**(RaftStructure.IsOceanReady + HasPart(RaftPart.Motor)) 해류를 뚫고 갈 수 있다
    /// (PlayerInventory.CanCarryToIsland 참고 - 예전에는 대형 섬까지 막아서 배 엔딩이 영원히
    /// 잠겨 있는 순환 잠금 버그가 있었다).
    ///
    /// [뗏목 계약 교체] 예전에는 삭제된 BoatConstructionSystem.HasCompletedStage(1)이 이 판정을 했다.
    /// 그 허브를 지우기 전에 이 조건부터 새 계약으로 옮겨야 특대 섬이 영구 잠기지 않는다(경비행기
    /// 엔딩의 재료가 특대 섬에만 있으므로 그쪽까지 함께 죽는다).
    /// **뗏목이 아예 없어도 고무보트만으로 일반 섬(소/중/대) 이동은 종전과 똑같이 가능하다** -
    /// 아래 판정은 CanCarryToIsland가 막는 경우를 뚫어 주는 우회 조건일 뿐이다.
    ///
    /// [순환 잠금이 아닌 이유] 모터에 드는 엔진부품은 특대 섬 자원 노드 말고도 시작 섬 여객기 잔해
    /// 2개(불러오기로 다시 채워진다)와 모든 섬의 난파선(수거 지점 3·4번째)·대형+ 침몰 화물에서 나온다.
    /// 특대 섬에 한 번도 못 가도 모터는 만들 수 있어야 한다는 것이 이 조건의 전제다.
    /// </summary>
    public class IslandTravel : MonoBehaviour
    {
        [Tooltip("섬 목록/배치 정보를 가진 월드맵 매니저")]
        public WorldMapManager worldMapManager;

        [Tooltip("이동 수단으로 사용하는 고무보트 아이템 (해류 제약 판정에 사용)")]
        public ItemData rubberBoatItem;

        [Tooltip("현재 플레이어가 위치한 섬 번호 (0번은 항상 불시착한 시작 섬)")]
        public int currentIslandId = 0;

        /// <summary>특대 섬의 해류를 뚫는 데 요구할 뗏목 등급.</summary>
        public enum CurrentBypass
        {
            /// <summary>바닥판 4칸 + 추진 하나(노만 있어도 된다). 예전 기준.</summary>
            Seaworthy = 0,
            /// <summary>바닥판 6칸 + 돛·키 또는 모터. 노만으로는 안 된다.</summary>
            OceanReady = 1,
            /// <summary>대양 규격 + **모터**까지. 가장 엄격.</summary>
            OceanReadyWithMotor = 2,
        }

        [Tooltip("특대 섬 해류를 뚫는 데 필요한 뗏목 등급.\n" +
            "Seaworthy = 바닥판 4칸 + 추진 하나 / OceanReady = 바닥판 6칸 + 돛·키(또는 모터) /\n" +
            "OceanReadyWithMotor = 거기에 모터까지(엔진부품 1 + 금속조각 4 + 노끈 1)")]
        public CurrentBypass currentBypassRequirement = CurrentBypass.OceanReadyWithMotor;

        // ── 디버그 전체 지도 / 자유 이동 ─────────────────────────────────────────────────
        // 감독 요청: "디버그 모드에서는 전체 지도가 다 보이고, 세계지도에서 모두 갈 수 있게".
        // 격리 방식은 DebugHud의 결말 미리보기(F6~F8)/재료 지급(F4)과 동일한 이중 가드다:
        //   1) 플래그·판정·우회 분기가 전부 #if UNITY_EDITOR || DEVELOPMENT_BUILD 안 - 출시 빌드에는
        //      컴파일조차 되지 않고, 소비처(MinimapUI.IsRevealed / 아래 TryTravelTo)의 참조도 같은
        //      #if 안에 있어 함께 빠진다.
        //   2) 그 안에서도 Debug.isDebugBuild를 한 번 더 확인한다(DebugRevealAllActive).
        // 플래그를 UI(DebugHud)가 아니라 여기(Systems)에 두는 이유: 소비자가 MinimapUI(UI)와
        // IslandTravel(Systems) 둘인데, Systems→UI 방향 참조를 만들지 않으려면 Systems 쪽이 들고
        // 있어야 한다(DebugHud는 이미 MakeGame.Systems를 참조한다).
        // **static이라 씬/세이브 어디에도 직렬화되지 않는다** - 세이브에 새어들 경로가 없다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 디버그 전체 지도 + 자유 이동 토글의 원본 값. DebugHud가 F10으로 뒤집는다.
        /// 기본 ON(디버그 빌드에서 켜진 채 시작) - 끄면 일반 모드 규칙 그대로를 테스트할 수 있다.
        /// </summary>
        public static bool debugRevealAllIslands = true;

        /// <summary>디버그 우회가 실제로 발동 중인지. 토글 값에 Debug.isDebugBuild를 한 번 더 곱한다.</summary>
        public static bool DebugRevealAllActive => Debug.isDebugBuild && debugRevealAllIslands;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 토글이 이전 실행의 값을 들고 시작하지 않게
        /// 초기값(기본 ON)으로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            debugRevealAllIslands = true;
        }
#endif

        /// <summary>
        /// 지정한 섬으로 이동을 시도한다.
        /// 목적지가 존재하고, 플레이어가 고무보트를 보유하고 있어야 한다.
        /// 해안의 뗏목이 currentBypassRequirement 등급(기본: 대양 규격 + 모터)에 못 미치면,
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [디버그 자유 이동] 항해 요건(고무보트 보유·해류 제약)만 건너뛰고, 성공 시 처리(발견 표시·
            // 현재 위치 갱신·텔레포트)는 일반 성공 경로와 **똑같이** 수행한다. DiscoverIsland 호출은
            // 디버그 상태 누출이 아니다 - 플레이어가 실제로 그 섬에 도착했으므로, 일반 모드로 항해해
            // 도착한 것과 동일한 "진짜 발견"이다(표시만 밝히는 MinimapUI 쪽 우회와는 성격이 다르다).
            // 아래의 일반 모드 경로는 한 글자도 바뀌지 않았다 - 이 분기는 위에서 return으로 빠질 뿐이다.
            if (DebugRevealAllActive)
            {
                worldMapManager.DiscoverIsland(destinationIslandId);
                currentIslandId = destinationIslandId;
                TeleportPlayerToIsland(inventory.transform, destination);
                return true;
            }
#endif

            if (rubberBoatItem != null)
            {
                if (inventory.GetItemCount(rubberBoatItem) <= 0)
                    return false;

                bool raftOvercomesCurrent = RaftOvercomesCurrent();

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
        /// 해안에 지은 뗏목이 고무보트의 해류 제약을 뚫을 만큼 자랐는지 판정한다.
        /// 뗏목 참조는 씬에 배선하지 않는다 - RaftStructure는 씬 로드마다 스스로 만들어지는
        /// 런타임 오브젝트라(RaftStructure.Bootstrap) 인스펙터에 넣어 둘 대상이 없기 때문이다.
        /// 뗏목이 아직 없으면 false이고, 그때도 일반 섬 이동은 그대로 된다.
        /// </summary>
        private bool RaftOvercomesCurrent()
        {
            var raft = RaftStructure.Active;
            if (raft == null)
                return false;

            switch (currentBypassRequirement)
            {
                // 모터까지 요구하는 이유(디렉터 결정 2026-08-19, 맵 확장과 한 묶음):
                // 새 배치에서 특대 섬은 시작 섬에서 19 km다. 노(1.2 m/s)로 가면 4시간 반이고
                // 돛(4.0)으로도 80분이다. 모터(6.0)라야 53분 — 실제로 몰고 갈 수 있는 유일한 수단이다.
                // 잠금이 아니라 "그 거리를 갈 배를 만들어라"는 요구다.
                case CurrentBypass.OceanReadyWithMotor:
                    return raft.IsOceanReady && raft.HasPart(RaftPart.Motor);

                case CurrentBypass.OceanReady:
                    return raft.IsOceanReady;

                default:
                    return raft.IsSeaworthy;
            }
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
