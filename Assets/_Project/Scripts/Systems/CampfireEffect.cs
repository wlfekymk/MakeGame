using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 모닥불(Campfire)에 붙어 불꽃/연기/불빛을 켜고 끄는 시각 전용 컴포넌트 (B4-11).
    ///
    /// 왜 Campfire.cs 안에 직접 넣지 않았나: Campfire는 연료·조리·저장 같은 게임플레이 상태를 다루는
    /// 클래스다. 거기에 파티클과 조명 코드를 섞으면 이후 밸런스 수정 때마다 시각 코드를 함께 읽어야
    /// 하고, 반대로 이펙트를 손볼 때 게임플레이 로직을 건드릴 위험이 생긴다. 이 컴포넌트는
    /// Campfire.isLit을 "읽기만" 하고 아무 것도 되돌려 쓰지 않으므로, 통째로 삭제해도 게임플레이는
    /// 100% 동일하게 동작한다(시각 전용 코드의 기준선).
    ///
    /// 프리팹(.prefab)을 편집하지 않고도 붙는 이유: Campfire.Awake가 EnsureAttached를 한 번 호출한다.
    /// 씬 배치·아이템 설치·세이브 복원 어느 경로로 생성되든 Awake는 반드시 지나가므로 누락이 없다.
    /// </summary>
    public class CampfireEffect : MonoBehaviour
    {
        [Tooltip("불꽃/연기/불빛을 모닥불 원점 기준 어디에 놓을지 (장작 더미 위쪽)")]
        public Vector3 flameLocalOffset = new Vector3(0f, 0.3f, 0f);

        [Tooltip("모닥불 불빛이 닿는 반경(m). 밤에 '여기가 안전지대'라는 신호가 닿는 거리다")]
        public float lightRange = 9f;

        [Tooltip("모닥불 불빛의 기준 밝기 (실제 밝기는 이 값 주변에서 흔들린다)")]
        public float lightIntensity = 1.6f;

        private Campfire campfire;
        private ParticleSystem flame;
        private ParticleSystem smoke;
        private Light fireLight;
        private bool lastLitState;

        /// <summary>
        /// 대상 오브젝트에 이 컴포넌트가 없으면 붙이고, 이미 있으면 그것을 그대로 돌려준다.
        /// 세이브 복원처럼 같은 오브젝트에 대해 초기화가 두 번 일어날 수 있는 경로가 있어,
        /// 중복으로 불꽃이 두 겹 생기지 않도록 반드시 이 진입점을 거치게 한다.
        /// </summary>
        public static CampfireEffect EnsureAttached(GameObject target)
        {
            if (target == null)
                return null;

            var existing = target.GetComponent<CampfireEffect>();
            if (existing != null)
                return existing;

            return target.AddComponent<CampfireEffect>();
        }

        /// <summary>파티클/조명을 만들어 두고, 처음에는 꺼진 상태로 시작한다.</summary>
        private void Awake()
        {
            campfire = GetComponent<Campfire>();
            BuildVisuals();
            ApplyLitState(campfire != null && campfire.isLit, true);
        }

        /// <summary>불꽃 파티클·연기 파티클·점광원을 자식으로 만든다.</summary>
        private void BuildVisuals()
        {
            flame = EffectBuilder.CreateCampfireFlame(transform, flameLocalOffset);
            smoke = EffectBuilder.CreateCampfireSmoke(transform, flameLocalOffset + new Vector3(0f, 0.35f, 0f));

            var lightGo = new GameObject("CampfireLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = flameLocalOffset + new Vector3(0f, 0.25f, 0f);

            fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = EffectBuilder.FoodOrange; // 팔레트 Food Orange #D98C33
            fireLight.range = lightRange;
            fireLight.intensity = lightIntensity;
            fireLight.shadows = LightShadows.None; // 그림자까지 켜면 설치 개수만큼 비용이 붙는다
            fireLight.enabled = false;
        }

        /// <summary>
        /// 점화 상태가 바뀌는 순간에만 파티클을 켜고 끄고, 켜져 있는 동안에는 불빛을 미세하게 흔든다.
        /// 매 프레임 Play/Stop을 호출하면 파티클이 계속 초기화되므로 반드시 상태 변화 시에만 전환한다.
        /// </summary>
        private void Update()
        {
            bool lit = campfire != null && campfire.isLit;
            if (lit != lastLitState)
                ApplyLitState(lit, false);

            if (lit && fireLight != null)
            {
                // 일정한 밝기의 점광원은 형광등처럼 보인다. PerlinNoise로 부드럽게 흔들어 불꽃이
                // 살아 있는 것처럼 만든다(Random을 쓰면 프레임마다 튀어 오히려 거슬린다).
                float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 5.5f, 0f) * 0.3f;
                fireLight.intensity = lightIntensity * flicker;
            }
        }

        /// <summary>불이 켜졌/꺼졌을 때 파티클과 조명을 한꺼번에 전환한다.</summary>
        private void ApplyLitState(bool lit, bool immediate)
        {
            lastLitState = lit;

            if (flame != null)
            {
                if (lit)
                    flame.Play();
                else
                    flame.Stop(true, immediate
                        ? ParticleSystemStopBehavior.StopEmittingAndClear
                        : ParticleSystemStopBehavior.StopEmitting); // 이미 떠 있는 불티는 자연스럽게 사그라들게 둔다
            }

            if (smoke != null)
            {
                if (lit)
                    smoke.Play();
                else
                    smoke.Stop(true, immediate
                        ? ParticleSystemStopBehavior.StopEmittingAndClear
                        : ParticleSystemStopBehavior.StopEmitting);
            }

            if (fireLight != null)
            {
                fireLight.enabled = lit;
                fireLight.intensity = lightIntensity;
            }
        }
    }
}
