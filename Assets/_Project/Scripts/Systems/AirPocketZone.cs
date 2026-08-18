using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 수중 동굴 천장의 "에어포켓"(공기 주머니) 볼륨. 플레이어의 머리(카메라 위치)가 이 볼륨 안에
    /// 들어오면 잠수 중에도 산소가 회복된다 (판정·회복 배선은 PlayerController → SurvivalStats,
    /// 산소통 oxygenDrainMultiplier와 같은 "컨트롤러가 파생 상태를 밀어주는" 패턴).
    ///
    /// 왜 트리거 콜라이더가 아니라 순수 수학 판정인가: 플레이어가 CharacterController라
    /// OnTriggerEnter/Exit의 신뢰성이 낮다(특히 컨트롤러가 움직이지 않는 프레임). Shelter.IsInsideHome이
    /// 확립한 것과 같은 "static 등록부 + 거리 비교" 방식을 그대로 따른다 — 존은 월드에 몇 개뿐이라
    /// O(n) 순회는 무시 가능한 비용이고, 판정은 PlayerController가 프레임당 최대 1회만 호출한다.
    ///
    /// 씬 수정 없음: 이 컴포넌트는 동굴 스포너가 코드로 붙인다(다음 웨이브). 세이브 대상 상태가 없다.
    /// </summary>
    public class AirPocketZone : MonoBehaviour
    {
        [Tooltip("구형 존의 반지름(m). 머리(카메라)가 이 반경 안이면 에어포켓 안으로 판정한다.")]
        public float radius = 1.2f;

        [Tooltip("켜면 구 대신 박스 볼륨으로 판정한다 (transform의 회전/위치 기준 로컬 박스).\n" +
            "동굴 천장의 납작하고 긴 공기층을 표현할 때 쓴다.")]
        public bool useBoxVolume = false;

        [Tooltip("박스 볼륨의 반너비(m). useBoxVolume이 켜져 있을 때만 사용한다.")]
        public Vector3 boxHalfExtents = new Vector3(1.5f, 0.6f, 1.5f);

        [Tooltip("존 위치에 은은한 상승 기포 쉼머 파티클을 자동 생성할지 여부.\n" +
            "\"저기 공기가 있다\"는 원거리 신호다. 스포너가 시각 효과를 따로 책임지면 꺼도 된다.")]
        public bool spawnBubbleShimmer = true;

        // ── static 등록부 (Shelter.activeShelters와 동일 패턴) ─────────────────────────
        private static readonly List<AirPocketZone> activeZones = new List<AirPocketZone>();

        /// <summary>현재 씬에 살아 있는 에어포켓 존 목록(읽기 전용).</summary>
        public static IReadOnlyList<AirPocketZone> ActiveZones => activeZones;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 리스트가 이전 실행의 파괴된 참조를 들고
        /// 시작하지 않게 초기 상태로 되돌린다 (R1 규약 - Shelter.ResetStaticCache와 동일).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticRegistry()
        {
            activeZones.Clear();
        }

        // 기포 쉼머 인스턴스. OnEnable 최초 1회만 생성하고, 이후에는 켜고/끄기만 한다.
        private ParticleSystem bubbleShimmer;

        private void OnEnable()
        {
            if (!activeZones.Contains(this))
                activeZones.Add(this);

            EnsureBubbleShimmer();
            if (bubbleShimmer != null && !bubbleShimmer.isEmitting)
                bubbleShimmer.Play();
        }

        private void OnDisable()
        {
            activeZones.Remove(this);

            // Clear가 아니라 StopEmitting: 이미 뱉은 기포는 남은 수명 동안 자연스럽게 사라진다
            // (UnderwaterAmbience.UpdateDiveBubbles와 같은 선택).
            if (bubbleShimmer != null && bubbleShimmer.isEmitting)
                bubbleShimmer.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// 월드 좌표가 등록된 에어포켓 존 중 하나라도의 볼륨 안에 있는지 판정한다.
        /// 순수 수학 판정(구 거리/로컬 박스 비교)이라 물리 이벤트에 의존하지 않고, 할당도 없다.
        /// 호출 규약: 프레임당 1회 이하 (PlayerController가 잠수 중일 때만 호출한다).
        /// </summary>
        public static bool IsInsideAny(Vector3 worldPos)
        {
            for (int i = 0; i < activeZones.Count; i++)
            {
                AirPocketZone zone = activeZones[i];
                if (zone != null && zone.Contains(worldPos))
                    return true;
            }
            return false;
        }

        /// <summary>이 존 하나의 볼륨 판정. 구형은 제곱 거리 비교, 박스는 로컬 좌표 절댓값 비교.</summary>
        public bool Contains(Vector3 worldPos)
        {
            if (useBoxVolume)
            {
                Vector3 local = transform.InverseTransformPoint(worldPos);
                return Mathf.Abs(local.x) <= boxHalfExtents.x
                    && Mathf.Abs(local.y) <= boxHalfExtents.y
                    && Mathf.Abs(local.z) <= boxHalfExtents.z;
            }

            float r = radius > 0f ? radius : 1.2f; // 0 이하는 미설정으로 보고 기본값(1.2m) 취급
            return (worldPos - transform.position).sqrMagnitude <= r * r;
        }

        /// <summary>
        /// 존 위치에 작고 성긴 상승 기포 쉼머를 1회 생성한다. EffectBuilder.CreateDiveBubbles를
        /// 재사용해 머티리얼/셰이더 폴백/월드 시뮬레이션 공간 규약을 그대로 물려받고, 여기서는
        /// "입에서 뿜는 기포"를 "볼륨 전체에서 드문드문 피어오르는 쉼머"로 바꾸는 튜닝만 한다.
        /// 텍스처/에셋 신규 생성 없음 (전부 절차 생성).
        /// </summary>
        private void EnsureBubbleShimmer()
        {
            if (!spawnBubbleShimmer || bubbleShimmer != null)
                return;

            bubbleShimmer = EffectBuilder.CreateDiveBubbles(transform);

            // 기포는 볼륨 아래쪽에서 피어올라 포켓(중심) 쪽으로 모인다.
            float rise = useBoxVolume ? boxHalfExtents.y : Mathf.Max(0.1f, radius) * 0.5f;
            bubbleShimmer.transform.localPosition = new Vector3(0f, -rise, 0f);

            // UnderwaterAmbience 기포 규약: 방출 콘이 항상 **월드 위**를 향하게 월드 회전을 고정한다.
            // 잠수 기포는 부모가 카메라라 매 프레임 고정해야 했지만, 존은 정적이라 생성 시 1회면 충분하다.
            bubbleShimmer.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            // 쉼머 튜닝: 잠수 기포(입 앞 좁은 콘, 4~7개/초)보다 작고 성기게, 볼륨 너비만큼 퍼뜨린다.
            var main = bubbleShimmer.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);

            var emission = bubbleShimmer.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(1.5f, 3f);

            var shape = bubbleShimmer.shape;
            shape.radius = useBoxVolume
                ? Mathf.Max(0.1f, Mathf.Min(boxHalfExtents.x, boxHalfExtents.z) * 0.8f)
                : Mathf.Max(0.1f, radius) * 0.6f;
        }
    }
}
