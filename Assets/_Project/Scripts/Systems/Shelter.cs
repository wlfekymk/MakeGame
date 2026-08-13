using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어가 제작해 설치한 쉼터(Shelter). Stranded Deep 기준: 비/햇빛을 막아주고 휴식 지점이 된다.
    /// 별도의 능동적 동작은 없으며, 지붕 콜라이더가 "Shade" 레이어에 있어
    /// SurvivalTickDriver의 그늘 판정 레이캐스트에 자동으로 걸리도록 하는 것이 핵심 역할이다.
    /// </summary>
    public class Shelter : MonoBehaviour
    {
        [Tooltip("이 쉼터가 제공하는 그늘 판정 반경(참고용 수치, 실제 판정은 레이캐스트로 이뤄진다)")]
        public float shadeRadius = 3f;
    }
}
