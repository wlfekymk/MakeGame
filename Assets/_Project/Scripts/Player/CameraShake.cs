using UnityEngine;
using MakeGame.Systems;

namespace MakeGame.Player
{
    /// <summary>
    /// [B34] 큰 짐승의 충격(CreatureMotion.OnImpact)을 받아 플레이어 카메라를 짧게 흔든다.
    ///
    /// ── 반드시 지켜야 하는 세 가지 제약 ────────────────────────────────────────────────
    /// (1) **회전을 절대 건드리지 않는다.** PlayerController.HandleLook이 카메라의 localEulerAngles를
    ///     매 프레임 통째로 덮어쓰므로(시야 상하 회전), 여기서 회전을 더하면 두 코드가 같은 채널을
    ///     두고 싸워 마우스 조작이 튄다. 이 컴포넌트는 **localPosition 오프셋만** 쓴다 -
    ///     회전 채널은 HandleLook, 위치 채널은 여기, 이렇게 완전히 나눠 갖는다.
    ///     (CreatureMotion이 "회전 금지, 평행 이동만"으로 곰을 움직이는 것과 같은 분리다.)
    /// (2) **Time.timeScale == 0이면 흔들지 않고, 오프셋을 0으로 되돌린다.** 타이틀·설정·엔딩·사망
    ///     화면이 timeScale = 0이다. 흔들린 상태로 그냥 멈추면 그 화면 내내 카메라가 옆으로
    ///     밀린 채 고정된다 - 정지 시에는 반드시 원위치로 되돌린 뒤 멈춘다.
    /// (3) **static 이벤트 구독은 반드시 해제한다.** OnImpact는 static이라 구독을 남기면 씬을 새로
    ///     열어도 죽은 카메라가 계속 붙잡혀 있는다(누수). OnEnable에서 걸고 OnDisable/OnDestroy에서
    ///     반드시 뗀다. 씬을 통째로 다시 여는 경로에서는 CreatureMotion.ClearImpactSubscribers()도 있다.
    ///
    /// ── 진폭 상한(멀미 방지) ───────────────────────────────────────────────────────────
    /// maxOffsetMeters는 5.5cm다. 카메라가 눈높이 1.6m에 있고 화면 폭이 60도 FOV라, 이 정도가
    /// "쿵 하고 울렸다"로 읽히면서도 시야가 흐르지 않는 폭이다. 충격이 겹쳐 들어와도 trauma를
    /// 0~1로 클램프하므로 절대 이 값을 넘지 않는다.
    ///
    /// ── 거리 감쇠 ──────────────────────────────────────────────────────────────────────
    /// OnImpact는 (강도, 지속시간) 두 float만 준다 - 발신자도 위치도 알 수 없다(RaiseImpact가
    /// private static이라 인스턴스 정보 자체가 없다). CreatureMotion은 다른 담당의 파일이라 고칠 수
    /// 없으므로, 위치 대신 아래 두 단계로 "멀리 있는 곰은 화면을 흔들지 않는다"를 만든다.
    ///   1) 강도 문턱(minImpactStrength): 발 디딤은 강도가 최대 0.30이라 통과하지 못하고,
    ///      포효(0.45)·돌진 착지(0.60)·앞발 내려치기(1.00)만 화면을 흔든다.
    ///   2) 가장 가까운 CreatureMotion까지의 거리로 감쇠: 충격을 쏜 개체가 누구인지는 알 수 없지만,
    ///      **가장 가까운 개체보다 가까울 수는 없다.** 그래서 "가장 가까운 곰도 maxShakeDistance보다
    ///      멀면 흔들지 않는다"는 항상 옳다. 곰이 여러 마리일 때 먼 쪽의 충격을 가까운 쪽 거리로
    ///      계산할 수는 있지만, 그 오차는 "덜 흔들려야 할 때 흔들린다"가 아니라 "가까운 곰이 있는
    ///      상황에서 흔들린다"라 체감상 문제가 되지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraShake : MonoBehaviour
    {
        [Tooltip("흔들 대상 Transform. 비워두면 이 컴포넌트가 붙은 오브젝트(=카메라) 자신을 쓴다.\n" +
            "회전이 아니라 localPosition만 건드리므로 PlayerController의 시야 회전과 겹치지 않는다.")]
        public Transform shakeTarget;

        [Header("진폭")]
        [Tooltip("흔들림 오프셋의 절대 상한(m). 멀미 방지용이며 충격이 겹쳐도 이 값을 넘지 않는다.")]
        public float maxOffsetMeters = 0.055f;

        [Tooltip("노이즈가 도는 속도(Hz에 준하는 값). 클수록 잘게 떨리고 작을수록 크게 출렁인다.")]
        public float noiseFrequency = 18f;

        [Header("충격 필터")]
        [Tooltip("이 강도(0~1) 미만의 충격은 무시한다. 곰의 발 디딤(최대 0.30)을 걸러내는 문턱이다.")]
        public float minImpactStrength = 0.35f;

        [Tooltip("이 거리(m) 안쪽이면 강도를 그대로 쓴다.")]
        public float fullShakeDistance = 6f;

        [Tooltip("가장 가까운 짐승이 이 거리(m)보다 멀면 아예 흔들지 않는다.")]
        public float maxShakeDistance = 25f;

        // ── 상태 ──────────────────────────────────────────────────────────────────────
        private Vector3 basePosition;          // 흔들리기 전의 원래 localPosition (씬에서는 (0, 1.6, 0))
        private bool baseCaptured;
        private bool offsetApplied;            // 지금 원위치에서 벗어나 있는지(불필요한 쓰기를 막는다)

        private float trauma;                  // 남은 흔들림 세기 0~1
        private float traumaDecayPerSecond;    // 초당 감쇠량 (= 1 / 지속시간)
        private float noiseTime;
        private float seedX;
        private float seedY;

        private bool subscribed;

        // 거리 감쇠용 캐시. 충격은 드물게 오지만(포효/돌진/슬램) 그때마다 씬을 훑지 않도록 잠깐 캐싱한다.
        private CreatureMotion[] creatureCache;
        private float creatureCacheTime = -999f;
        private const float CreatureCacheSeconds = 0.5f;

        /// <summary>흔들 대상과 원래 위치, 개체별 노이즈 시드를 잡는다.</summary>
        private void Awake()
        {
            if (shakeTarget == null)
                shakeTarget = transform;

            CaptureBase();

            // 두 축이 같은 노이즈를 타면 대각선으로만 흔들려 부자연스럽다. 축마다 다른 시드를 준다.
            // Unity 6.5에서 GetInstanceID()는 CS0619(에러)다. 시드는 고유하기만 하면 되므로
            // 프레임/시간에 의존하지 않는 이름 해시를 쓴다.
            int id = name.GetHashCode() ^ (gameObject.name.GetHashCode() << 1);
            seedX = Mathf.Repeat(id * 0.317f, 100f);
            seedY = Mathf.Repeat(id * 0.713f + 37f, 100f);
        }

        /// <summary>원래 localPosition을 한 번만 기억한다. 이후 모든 흔들림은 이 값 기준의 오프셋이다.</summary>
        private void CaptureBase()
        {
            if (baseCaptured || shakeTarget == null)
                return;

            basePosition = shakeTarget.localPosition;
            baseCaptured = true;
        }

        /// <summary>
        /// 충격 훅을 구독한다. static 이벤트라 반드시 짝이 되는 해제(OnDisable/OnDestroy)가 있어야 한다.
        /// </summary>
        private void OnEnable()
        {
            CaptureBase();

            if (!subscribed)
            {
                CreatureMotion.OnImpact += HandleImpact;
                subscribed = true;
            }
        }

        /// <summary>
        /// 구독을 해제하고 흔들림을 즉시 지운다. 꺼지는 순간 오프셋이 남아 있으면 카메라가
        /// 옆으로 밀린 채 굳어 버리므로 원위치로 되돌린 뒤 끝낸다.
        /// </summary>
        private void OnDisable()
        {
            Unsubscribe();
            trauma = 0f;
            traumaDecayPerSecond = 0f;
            ResetOffset();
        }

        /// <summary>OnDisable을 거치지 않고 파괴되는 경로(씬 언로드 등)를 위한 이중 안전장치.</summary>
        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary>구독 해제. 두 번 불려도 안전하다.</summary>
        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            CreatureMotion.OnImpact -= HandleImpact;
            subscribed = false;
        }

        /// <summary>
        /// CreatureMotion이 쏜 충격을 받는다. (강도 0~1, 지속시간 초)
        /// 강도 문턱과 거리 감쇠를 통과한 것만 흔들림으로 쌓는다(클래스 주석의 '거리 감쇠' 참고).
        /// </summary>
        private void HandleImpact(float strength, float duration)
        {
            // 일시정지·타이틀·사망 화면에서 들어온 충격은 쌓아두지 않는다. 쌓아두면 게임으로
            // 돌아오는 순간 밀린 충격이 한꺼번에 터진다.
            if (Time.timeScale <= 0f || !isActiveAndEnabled || shakeTarget == null)
                return;

            if (strength < minImpactStrength)
                return;

            float scaled = Mathf.Clamp01(strength) * DistanceAttenuation();
            if (scaled <= 0.01f)
                return;

            AddTrauma(scaled, duration);
        }

        /// <summary>
        /// 흔들림을 누적한다. 겹쳐 들어와도 0~1로 클램프되므로 진폭 상한을 넘지 않고,
        /// 감쇠 속도는 둘 중 느린 쪽(= 지속시간이 긴 쪽)을 따라 꼬리가 잘리지 않게 한다.
        /// </summary>
        private void AddTrauma(float amount, float duration)
        {
            trauma = Mathf.Clamp01(trauma + amount);

            float decay = 1f / Mathf.Max(0.05f, duration);
            traumaDecayPerSecond = traumaDecayPerSecond <= 0f ? decay : Mathf.Min(traumaDecayPerSecond, decay);
        }

        /// <summary>
        /// 가장 가까운 CreatureMotion까지의 거리로 0~1 감쇠 계수를 만든다.
        /// fullShakeDistance 안쪽이면 1, maxShakeDistance 바깥이면 0이다.
        /// 씬에 CreatureMotion이 하나도 잡히지 않으면(찾기 실패) 1을 돌려준다 - 충격이 실제로
        /// 왔다는 것은 어딘가에 발신자가 있다는 뜻이라, 여기서 0을 주면 연출이 통째로 사라진다.
        /// </summary>
        private float DistanceAttenuation()
        {
            RefreshCreatureCache();

            if (creatureCache == null || creatureCache.Length == 0)
                return 1f;

            Vector3 here = shakeTarget.position;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < creatureCache.Length; i++)
            {
                CreatureMotion motion = creatureCache[i];
                if (motion == null)          // 파괴된 개체(Unity의 null 비교로 걸러진다)
                    continue;

                float sqr = (motion.transform.position - here).sqrMagnitude;
                if (sqr < nearestSqr)
                    nearestSqr = sqr;
            }

            if (nearestSqr == float.MaxValue)
                return 1f;

            float distance = Mathf.Sqrt(nearestSqr);
            float far = Mathf.Max(fullShakeDistance + 0.01f, maxShakeDistance);
            return 1f - Mathf.InverseLerp(fullShakeDistance, far, distance);
        }

        /// <summary>
        /// 씬의 CreatureMotion 목록을 짧게 캐싱한다. 처치되어 시각 파츠가 꺼진 개체도 발신자가 될 수
        /// 있으므로 Include로 찾는다. FindObjectsByType은 2인자 오버로드만 쓴다(3인자는 CS0618).
        /// </summary>
        private void RefreshCreatureCache()
        {
            if (creatureCache != null && Time.unscaledTime - creatureCacheTime < CreatureCacheSeconds)
                return;

            // Unity 6.5: FindObjectsSortMode를 받는 오버로드는 CS0618이다. 1인자 형태만 쓴다.
            creatureCache = FindObjectsByType<CreatureMotion>(FindObjectsInactive.Include);
            creatureCacheTime = Time.unscaledTime;
        }

        /// <summary>
        /// 감쇠 노이즈를 매 프레임 카메라 localPosition에 얹는다. 회전은 PlayerController.HandleLook의
        /// 채널이라 여기서는 절대 건드리지 않는다. LateUpdate인 이유는 시야 회전(Update)이 끝난 뒤
        /// 카메라를 마무리하는 것이 순서상 맞기 때문이다(회전과 위치라 서로 덮어쓰지는 않는다).
        /// </summary>
        private void LateUpdate()
        {
            if (shakeTarget == null)
                return;

            // 정지 화면에서는 흔들림을 진행하지도, 남겨두지도 않는다. 여기서 되돌리지 않으면
            // 타이틀·설정·엔딩·사망 화면이 삐뚤어진 카메라로 고정된다.
            if (Time.timeScale <= 0f)
            {
                trauma = 0f;
                traumaDecayPerSecond = 0f;
                ResetOffset();
                return;
            }

            if (trauma <= 0f)
            {
                ResetOffset();
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            trauma = Mathf.Max(0f, trauma - traumaDecayPerSecond * dt);
            if (trauma <= 0f)
            {
                traumaDecayPerSecond = 0f;
                ResetOffset();
                return;
            }

            noiseTime += dt * Mathf.Max(0.01f, noiseFrequency);

            // 진폭은 trauma의 제곱. 끝날 때 뚝 끊기지 않고 부드럽게 잦아든다.
            float amplitude = Mathf.Max(0f, maxOffsetMeters) * trauma * trauma;

            float nx = (Mathf.PerlinNoise(seedX, noiseTime) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(seedY, noiseTime) - 0.5f) * 2f;

            // 좌우/상하만 쓴다. 앞뒤(z)로 밀면 시야가 줌처럼 들썩여 멀미가 훨씬 심해진다.
            Vector3 offset = new Vector3(nx * amplitude, ny * amplitude, 0f);
            offset = Vector3.ClampMagnitude(offset, Mathf.Max(0f, maxOffsetMeters));

            shakeTarget.localPosition = basePosition + offset;
            offsetApplied = true;
        }

        /// <summary>카메라를 원래 위치로 되돌린다. 이미 원위치면 아무 것도 쓰지 않는다.</summary>
        private void ResetOffset()
        {
            if (!offsetApplied || shakeTarget == null || !baseCaptured)
                return;

            shakeTarget.localPosition = basePosition;
            offsetApplied = false;
        }
    }
}
