using UnityEngine;

namespace MakeGame.Player
{
    /// <summary>
    /// 매 프레임 SurvivalStats.Tick을 호출해 허기/갈증/일사병 등 시간에 따른 생존 수치 변화를 실제로 진행시킨다.
    /// 이 컴포넌트가 없으면 SurvivalStats는 값을 들고만 있을 뿐 시간이 지나도 아무 변화가 없다.
    /// </summary>
    public class SurvivalTickDriver : MonoBehaviour
    {
        [Tooltip("매 프레임 갱신할 생존 수치 컴포넌트")]
        public SurvivalStats survivalStats;

        [Tooltip("그늘 판정에 사용할 레이어. 머리 위로 쏜 레이가 이 레이어에 맞으면 그늘로 판정한다.")]
        public LayerMask shadeLayerMask;

        [Tooltip("그늘 판정용 레이캐스트 최대 거리")]
        public float shadeCheckDistance = 20f;

        [Tooltip("해수면 높이. PlayerController.waterLevel과 같은 값을 사용해야 한다 (잠수/산소 판정 기준).")]
        public float waterLevel = 0f;

        [Tooltip("발 위치(transform.position) 기준 머리/카메라까지의 높이. 산소 판정에 사용한다 " +
            "(카메라 로컬 Y와 같은 값, 기본 1.6). 이 값이 없으면 발만 살짝 잠겨도(수면 위에서 헤엄치는 " +
            "정상적인 상태) 머리는 물 밖에 있는데도 산소가 계속 줄어드는 문제가 생긴다.")]
        public float headHeightOffset = 1.6f;

        /// <summary>
        /// 매 프레임 그늘 여부와 잠수 여부를 판정하고 SurvivalStats.Tick을 호출해 생존 수치를 갱신한다.
        /// </summary>
        private void Update()
        {
            if (survivalStats == null)
                return;

            survivalStats.Tick(Time.deltaTime, IsCurrentlyInShade(), IsCurrentlyUnderwater());
        }

        /// <summary>
        /// 산소 판정 전용: 머리(발 위치 + headHeightOffset)가 해수면보다 낮을 때만 "잠수 중"으로 판정한다.
        /// 버그 수정: 예전에는 발 위치(transform.position.y)만으로 판정해서, PlayerController의 수영
        /// 모드 전환 기준(발이 수면 아래)과 똑같이 취급했다. 그런데 수영 모드는 부력/중력이 발 위치를
        /// waterLevel 근처에서 계속 오르내리게 만들기 때문에, 머리는 물 위에 떠 있는 정상적인 수영
        /// 상태에서도 발이 수면 아래로 자주 내려가 산소가 거의 항상 줄어들고 있었다. 실제로 잠수
        /// 키(diveKey)를 눌러 머리까지 물에 담가야만 산소가 줄어들도록 머리 높이를 기준으로 고쳤다.
        /// </summary>
        private bool IsCurrentlyUnderwater()
        {
            return (transform.position.y + headHeightOffset) < waterLevel;
        }

        /// <summary>
        /// 현재 위치에서 하늘 방향으로 레이를 쏴서 그늘(지붕, 큰 나무 등) 아래에 있는지 판정한다.
        /// 아무것도 맞지 않으면 햇빛에 그대로 노출된 것으로 간주한다.
        /// </summary>
        private bool IsCurrentlyInShade()
        {
            Ray ray = new Ray(transform.position, Vector3.up);
            return Physics.Raycast(ray, shadeCheckDistance, shadeLayerMask);
        }
    }
}
