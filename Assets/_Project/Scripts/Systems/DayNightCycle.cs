using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// SurvivalClock의 하루 진행률(TimeOfDay01)에 맞춰 Directional Light의 각도/밝기/색온도를
    /// 서서히 바꿔 밤/낮 주기를 표현한다. 예전에는 조명이 고정값이라 게임 내 시간이 아무리 흘러도
    /// 낮과 밤의 시각적 차이가 전혀 없었다(밤/낮 주기 부재 이슈).
    /// 씬에 미리 배치할 필요 없이 RuntimeInitializeOnLoadMethod로 씬 로드 시 자동 생성되며,
    /// Directional Light와 SurvivalClock을 씬에서 스스로 찾아 연결한다.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("낮 동안(정오)의 최대 조명 강도")]
        public float dayIntensity = 1.2f;

        [Tooltip("밤 동안의 최소 조명 강도 (완전한 암흑은 아니고 은은하게 남겨둔다)")]
        public float nightIntensity = 0.05f;

        [Tooltip("한낮의 조명 색상 (밝은 백색광)")]
        public Color dayColor = new Color(1f, 0.98f, 0.92f);

        [Tooltip("일출/일몰 무렵의 조명 색상 (붉은 노을빛)")]
        public Color duskDawnColor = new Color(1f, 0.6f, 0.35f);

        [Tooltip("한밤중의 조명 색상 (푸르스름한 달빛)")]
        public Color nightColor = new Color(0.4f, 0.5f, 0.7f);

        private Light sunLight;
        private SurvivalClock clock;

        /// <summary>
        /// 씬에 이 컴포넌트가 아직 없으면 스스로 생성한다. AudioManager/GameManager와 달리
        /// 씬이 바뀌면(재시작 등) 새 씬의 Directional Light를 다시 찾아야 하므로
        /// DontDestroyOnLoad를 쓰지 않고, 씬이 로드될 때마다 새로 만들어진다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("DayNightCycle");
            go.AddComponent<DayNightCycle>();
        }

        /// <summary>
        /// 씬에서 Directional Light와 SurvivalClock을 찾아 참조를 캐시해둔다.
        /// 둘 중 하나라도 없으면 이 컴포넌트는 아무 동작도 하지 않는다(방어적 설계).
        /// </summary>
        private void Start()
        {
            sunLight = FindDirectionalLight();
            clock = FindAnyObjectByType<SurvivalClock>();
        }

        /// <summary>
        /// 씬의 모든 Light 중 타입이 Directional인 첫 번째 광원을 찾는다.
        /// </summary>
        private Light FindDirectionalLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                    return light;
            }
            return null;
        }

        /// <summary>
        /// 매 프레임 하루 진행률에 맞춰 태양의 각도와 밝기/색상을 갱신한다.
        /// t=0(자정)~0.25(일출)~0.5(정오)~0.75(일몰)~1(다시 자정) 순으로 순환한다.
        /// </summary>
        private void Update()
        {
            if (sunLight == null || clock == null)
                return;

            float t = clock.TimeOfDay01;

            // 태양 각도: t=0.5(정오)일 때 머리 위(90도)에 가깝고, t=0/1(자정)일 때 지평선 아래로 내려가도록
            // 360도를 한 바퀴 회전시킨다. -90도 오프셋을 주면 t=0.25(일출)에 지평선 근처(0도)에서 시작한다.
            float sunAngle = (t * 360f) - 90f;
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);

            // 낮 강도(0~1): 정오에 1, 자정에 0이 되는 코사인 곡선. 태양이 지평선 아래일 때는 0으로 클램프.
            float dayFactor = Mathf.Clamp01(Mathf.Cos((t - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f);
            sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor);

            // 색상: 낮에는 백색광, 일출/일몰 무렵(dayFactor가 중간값)에는 노을빛, 밤에는 푸른 달빛으로 보간한다.
            Color baseColor = Color.Lerp(nightColor, dayColor, dayFactor);
            float duskDawnBlend = 1f - Mathf.Abs(dayFactor - 0.5f) * 2f; // dayFactor=0.5 부근(여명/노을)에서 1에 가까워짐
            sunLight.color = Color.Lerp(baseColor, duskDawnColor, Mathf.Clamp01(duskDawnBlend) * 0.6f);
        }
    }
}
