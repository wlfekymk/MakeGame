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
        /// 현재 발 위치(transform.position.y)가 해수면보다 낮으면 잠수 중인 것으로 판정한다.
        /// 섬 지형은 항상 y=0 이상이므로, 이 값보다 낮다는 것은 섬을 벗어나 바다에 있다는 뜻이다.
        /// </summary>
        private bool IsCurrentlyUnderwater()
        {
            return transform.position.y < waterLevel;
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
