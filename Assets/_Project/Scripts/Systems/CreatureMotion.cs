using System.Collections;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// [B34] 리그·애니메이터·애니메이션 클립이 하나도 없는 이 프로젝트에서, 큰 짐승의 "무게"를
    /// 코드로만 표현하는 프로시저럴 모션 컴포넌트. 곰 스펙 5장(핵심 움직임과 물리 효과)과
    /// 6장(애니메이션 시퀀스 - 포효/돌진/앞발 내려치기)을 담당한다.
    ///
    /// ── 반드시 지켜야 하는 세 가지 제약(이 파일 설계의 전부다) ────────────────────────────
    /// (1) **루트를 절대 건드리지 않는다.** 위험 요소의 트리거 콜라이더가 루트에 붙어 있어서,
    ///     루트를 1cm라도 움직이거나 돌리면 전투/접촉 판정이 함께 움직인다. 이 프로젝트는 그
    ///     원칙을 반복해서 명문화해 왔다(HazardSource의 벌떼 회전/곰 호흡 주석). 그래서 여기서는
    ///     **자식 시각 파츠의 localPosition만** 쓴다 - 판정은 1mm도 변하지 않는다.
    /// (2) **어떤 자식도 회전시키지 않는다.** 곰 루트의 localScale은 (0.86, 1.80, 2.56)으로 비균등이라,
    ///     그 아래에서 회전한 자식은 월드 행렬이 S·R·S 꼴이 되어 전단(shear)으로 찌그러진다
    ///     (CreatureVisualBuilder.MeterSpacePart의 "회전은 주지 않는다" 주석과 같은 이유).
    ///     → 상하 바운스·좌우 흔들림·앞뒤 쏠림은 전부 **평행 이동**으로만 만든다. 몸을 실제로 기울이는
    ///       회전(롤/피치)은 전단 없는 별도 모션 피벗이 생기기 전까지 시도하지 않는다.
    /// (3) **Time.timeScale == 0이면 아무 것도 하지 않는다.** 타이틀/설정 화면이 timeScale = 0이고,
    ///     이 컴포넌트의 시간은 전부 Time.deltaTime이라 그 화면에서는 위상도 시퀀스도 자동으로 멈춘다.
    ///     (같은 오브젝트의 호흡/벌떼 회전은 의도적으로 unscaledDeltaTime을 쓴다 - 서로 다른 채널을
    ///      건드리므로(저쪽은 localScale, 이쪽은 localPosition) 충돌하지 않는다.)
    ///
    /// ── 좌표 단위 ────────────────────────────────────────────────────────────────────────
    /// 아래 진폭 상수는 전부 **미터**다. 자식의 localPosition은 루트 로컬 공간(= NominalScale 공간)이라
    /// 적용 직전에 metersToLocal(= 1/NominalScale)을 곱한다. 개체별 크기 편차(sizeJitter)로는 나누지
    /// 않는다 - 큰 개체가 비례해서 더 크게 출렁이는 편이 옳다(ReshapeSphere가 위치를 잡는 방식과 같다).
    ///
    /// ── ★ 관절 여유: 아래 진폭이 "왜 이 숫자인가"의 전부다. 키울 때 반드시 여기부터 읽어라 ─────
    /// 곰은 강체 조각들을 서로 파묻어 이어 붙인 몸이라, 조각끼리 어긋날 수 있는 거리가 곧 진폭의 상한이다.
    /// 세 파츠 그룹으로 나눈 이유도 이 여유가 부위마다 다르기 때문이다.
    ///   · 몸통 그룹(Coat/Hump/Underside/Snout/눈) = 오프셋 100%. 배 껍질이 몸통에서 1.4cm밖에 안 떠
    ///     있어서 이들끼리는 절대 어긋나면 안 된다.
    ///   · Limbs(다리 상부) = 오프셋 **50%**. 위(어깨/골반)는 몸통 안에 2.2cm 묻혀 있고, 아래(무릎)는
    ///     다리 하부와 6cm 겹친다. 몸통을 100% 따라가면 무릎이 벌어지고, 0%면 어깨가 벌어지므로
    ///     양쪽 여유를 반씩 나눠 쓴다.
    ///   · Claws(발톱) = 오프셋 **0%**. 뿌리가 발바닥 안에 1.5cm밖에 안 묻혀 있어서 조금만 움직여도
    ///     발에서 빠진다. 발(루트)과 함께 지면에 고정된다.
    /// 여기서 나오는 상한(50% 규칙 적용 후):
    ///   **위로 0.10m** (무릎 6cm ÷ 0.5 = 12cm, 골반 캡 노출 9cm ÷ 0.5 = 18cm 중 작은 쪽에 여유를 더 둔 값)
    ///   **수평 0.035m** (어깨 묻힘 2.2cm ÷ 0.5 = 4.4cm, 무릎 단차 2.5cm ÷ 0.5 = 5cm 중 작은 쪽)
    ///   아래로는 훨씬 넉넉하다(다리가 몸통 안으로 더 들어갈 뿐이라 0.2m까지 안전하다).
    /// 그래서 포효는 "뒷다리로 완전히 일어서기"가 아니라 상체를 8.5cm 밀어 올리는 것까지다.
    /// 진짜로 일어서려면 전단 없는 모션 피벗(회전)이나 다리 늘이기가 먼저 있어야 한다 - 다음 배치 몫이다.
    /// </summary>
    public class CreatureMotion : MonoBehaviour
    {
        /// <summary>
        /// 발이 땅을 찍거나 앞발이 내려꽂힌 순간을 바깥에 알리는 훅. (강도 0~1, 지속시간 초).
        ///
        /// 카메라 흔들림은 카메라를 소유한 쪽(PlayerController)이 붙여야 하므로 여기서는 신호만 쏜다.
        /// 사운드도 이 훅에 걸 수 있지만, 곰 자신의 소리는 위치가 있어야 해서(3D) 이 파일이 직접 낸다.
        ///
        /// 구독자는 반드시 자기 OnDestroy/OnDisable에서 해제할 것 - static 이벤트라 구독을 남기면
        /// 씬을 바꿔도 죽은 오브젝트가 계속 붙잡혀 있는다. 씬 재시작 경로에서는
        /// ClearImpactSubscribers()로 한 번에 비울 수 있다(AudioManager.ClearInstance와 같은 목적).
        /// </summary>
        public static event System.Action<float, float> OnImpact;

        /// <summary>게임 오버 후 재시작처럼 씬을 통째로 새로 여는 경로에서 구독자를 한 번에 비운다.</summary>
        public static void ClearImpactSubscribers()
        {
            OnImpact = null;
        }

        // ── 튜닝 값(미터 / 초). 곰 기준 기본값이며, 다른 생물에 붙일 때는 Bind 뒤에 덮어쓰면 된다. ──

        [Header("보행")]
        [Tooltip("전속력으로 걸을 때 몸통이 위아래로 오르내리는 최대 높이(m). 상한 0.10 - 위 '관절 여유' 참고.")]
        public float bounceMeters = 0.050f;

        [Tooltip("전속력으로 걸을 때 몸통이 좌우로 실리는 최대 폭(m). 상한 0.035 - 위 '관절 여유' 참고.")]
        public float swayMeters = 0.022f;

        [Tooltip("1m 전진할 때 보행 위상이 도는 각(라디안). 2.4면 한 걸음 주기(2π)가 약 2.6m다.")]
        public float strideRadiansPerMeter = 2.4f;

        [Tooltip("이 속도(m/s)에서 보행 진폭이 최대가 된다. 이보다 느리면 비례해 줄어든다.")]
        public float fullGaitSpeed = 2.2f;

        [Header("어깨 혹 관성")]
        [Tooltip("혹이 몸통 움직임을 따라잡는 데 걸리는 시간(초). 클수록 더 늦게, 더 무겁게 따라온다.")]
        public float humpLagSeconds = 0.13f;

        [Tooltip("혹이 몸통에서 어긋날 수 있는 최대 거리(m). 혹 아랫면은 몸통 안에 45cm 묻혀 있어 이 값의 7배까지 안전하다.")]
        public float humpMaxSlackMeters = 0.06f;

        [Header("정지")]
        [Tooltip("서 있을 때 아주 느리게 좌우로 실리는 체중 이동 폭(m). 호흡(스케일 펄스)은 HazardSource가 따로 담당한다.")]
        public float idleSwayMeters = 0.012f;

        [Tooltip("서 있을 때의 미세한 상하 흔들림(m).")]
        public float idleBobMeters = 0.006f;

        // ── 바인딩된 대상 ──────────────────────────────────────────────────────────────
        private Transform[] bodyParts;          // 오프셋 100%로 움직이는 시각 파츠(루트 제외)
        private Vector3[] bodyBasePositions;    // 각 파츠의 원래 localPosition
        private Transform legPart;              // 오프셋 50%만 따라가는 다리 상부. 없어도 된다
        private Vector3 legBasePosition;
        private Transform humpPart;             // 한 박자 늦게 따라오는 덩어리(곰의 어깨 혹). 없어도 된다
        private Vector3 humpBasePosition;
        private Vector3 metersToLocal = Vector3.one;
        private bool bound;

        /// <summary>다리 상부가 몸통 오프셋을 따라가는 비율. 어깨 여유와 무릎 여유를 반씩 나눠 쓰는 값이다.</summary>
        private const float LegFollow = 0.5f;

        // ── 보행 상태 ──────────────────────────────────────────────────────────────────
        private Vector3 lastPosition;
        private float gaitPhase;
        private int lastFootfallIndex;
        private float smoothedSpeed;
        private float idleTime;

        // ── 혹 관성 상태 ───────────────────────────────────────────────────────────────
        private Vector3 humpFollow;
        private Vector3 humpVelocity;

        // ── 시퀀스(포효/돌진/슬램) 상태 ────────────────────────────────────────────────
        private Vector3 sequenceOffset;      // 시퀀스가 몸통에 더하는 오프셋(m)
        private float sequenceGaitWeight;    // 제자리에서도 보행 진동을 강제로 살리는 가중치(돌진용)
        private float sequenceStrideBoost;   // 이동 없이도 위상을 돌리는 속도(rad/s, 돌진용)
        private Coroutine sequence;

        private AudioSource voice;

        /// <summary>지금 1회성 시퀀스(포효/돌진/슬램)가 재생 중인지 여부.</summary>
        public bool IsPlayingSequence => sequence != null;

        private const float SpeedSmoothing = 8f;         // 속도 저역통과 계수(1/초). 프레임 튐을 걸러낸다
        private const float FootfallMinWeight = 0.15f;   // 이보다 느리면 발소리/충격을 내지 않는다

        /// <summary>
        /// 이 오브젝트의 자식 시각 파츠를 잡아 모션 대상으로 등록한다. 루트는 대상에서 제외되고
        /// (콜라이더가 붙어 있다) 지면에 박힌 발 메시도 루트에 있어서 자동으로 움직이지 않는다.
        /// </summary>
        /// <param name="nominalScale">루트 localScale의 규격값(곰이면 CreatureVisualBuilder.BearBodyScale). 미터를 로컬로 바꾸는 데 쓴다.</param>
        /// <param name="phaseSeed">개체별 위상 어긋냄. 같은 섬의 개체들이 한 몸처럼 움직이지 않게 한다(UnityEngine.Random 금지 - 재현성 규칙).</param>
        /// <param name="laggingPartName">한 박자 늦게 따라올 파츠 이름(곰이면 "Hump"). 없으면 null.</param>
        /// <param name="plantedPartName">아예 움직이지 않고 지면에 고정될 파츠 이름(곰이면 "Claws"). 없으면 null.</param>
        /// <param name="legPartName">오프셋의 절반만 따라갈 파츠 이름(곰이면 "Limbs"). 없으면 null.</param>
        public void Bind(Vector3 nominalScale, float phaseSeed,
            string laggingPartName = null, string plantedPartName = null, string legPartName = null)
        {
            metersToLocal = new Vector3(
                1f / Mathf.Max(0.0001f, nominalScale.x),
                1f / Mathf.Max(0.0001f, nominalScale.y),
                1f / Mathf.Max(0.0001f, nominalScale.z));

            Transform lagging = FindPart(laggingPartName);
            Transform planted = FindPart(plantedPartName);
            Transform leg = FindPart(legPartName);

            humpPart = lagging;
            humpBasePosition = lagging != null ? lagging.localPosition : Vector3.zero;
            legPart = leg;
            legBasePosition = leg != null ? leg.localPosition : Vector3.zero;

            int childCount = transform.childCount;
            var parts = new Transform[childCount];
            var bases = new Vector3[childCount];
            int count = 0;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);

                // planted는 아예 목록에 넣지 않는다(한 번도 건드리지 않는다는 뜻이다).
                if (child == null || child == lagging || child == planted || child == leg)
                    continue;

                parts[count] = child;
                bases[count] = child.localPosition;
                count++;
            }

            bodyParts = new Transform[count];
            bodyBasePositions = new Vector3[count];
            System.Array.Copy(parts, bodyParts, count);
            System.Array.Copy(bases, bodyBasePositions, count);

            gaitPhase = Mathf.Repeat(phaseSeed, 2f * Mathf.PI);
            idleTime = Mathf.Repeat(phaseSeed * 1.7f, 100f);
            lastFootfallIndex = Mathf.FloorToInt(gaitPhase / Mathf.PI);
            lastPosition = transform.position;
            humpFollow = Vector3.zero;
            humpVelocity = Vector3.zero;
            sequenceOffset = Vector3.zero;

            EnsureVoice();
            bound = bodyParts.Length > 0 || humpPart != null || legPart != null;
        }

        /// <summary>이름이 비어 있지 않을 때만 자식을 찾는다. 없으면 null(그 역할을 쓰지 않는다는 뜻).</summary>
        private Transform FindPart(string partName)
        {
            return string.IsNullOrEmpty(partName) ? null : transform.Find(partName);
        }

        /// <summary>
        /// 곰 전용 편의 진입점. 파츠 이름/규격을 아는 곳을 한 군데로 모아 호출부가 실수하지 않게 한다.
        /// 이미 붙어 있으면 새로 만들지 않고 재사용한다(세이브 복원 등으로 두 번 불려도 안전하다).
        /// </summary>
        public static CreatureMotion AttachBear(GameObject body, float phaseSeed)
        {
            if (body == null)
                return null;

            CreatureMotion motion = body.GetComponent<CreatureMotion>();
            if (motion == null)
                motion = body.AddComponent<CreatureMotion>();

            motion.Bind(CreatureVisualBuilder.BearBodyScale, phaseSeed,
                laggingPartName: "Hump",    // 어깨 혹 - 한 박자 늦게 따라온다
                plantedPartName: "Claws",   // 발톱 - 발과 함께 지면에 고정
                legPartName: "Limbs");      // 다리 상부 - 절반만 따라간다
            return motion;
        }

        /// <summary>
        /// 3D로 들리는 곰 자신의 목소리/발소리용 AudioSource를 한 번만 만든다.
        /// AudioManager는 화면 전역 효과음(2D)만 다루고 위치가 없는 재생만 제공하므로, 위치가 중요한
        /// 짐승 소리는 이 오브젝트에 직접 붙인다. 볼륨은 AudioManager의 설정값을 그대로 따른다.
        /// </summary>
        private void EnsureVoice()
        {
            if (voice != null)
                return;

            voice = GetComponent<AudioSource>();
            if (voice == null)
                voice = gameObject.AddComponent<AudioSource>();

            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 1f;             // 완전 3D - 곰이 있는 쪽에서 들려야 한다
            voice.dopplerLevel = 0f;             // 위험요소는 제자리에 있으므로 도플러가 붙으면 어색하다
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 4f;
            voice.maxDistance = 45f;
        }

        // ── 프레임 갱신 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 매 프레임 보행 위상을 이동 거리만큼 돌리고, 걷기/정지/시퀀스 오프셋을 합쳐 자식 파츠에 적용한다.
        /// timeScale이 0이면(타이틀/설정) 즉시 빠져나가므로 그 화면에서는 마지막 자세로 완전히 멈춘다.
        /// </summary>
        private void Update()
        {
            if (!bound || Time.timeScale <= 0f)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            float gaitWeight = UpdateGait(dt);
            Vector3 offsetMeters = ComposeOffset(gaitWeight, dt);
            ApplyOffset(offsetMeters, dt);
        }

        /// <summary>
        /// 루트의 실제 이동량으로 보행 위상을 돌리고, 지금 걸음이 얼마나 "실려 있는지"(0~1)를 돌려준다.
        ///
        /// 위상을 시간이 아니라 **이동 거리**로 돌리는 것이 핵심이다. 속도가 변해도 걸음 간격이 거리에
        /// 고정되므로 발이 미끄러지는 느낌이 나지 않는다. 지금 위험 요소는 이동 코드가 없어 거리가 늘
        /// 0이지만(HazardSpawner 주석), 나중에 추격 AI가 붙으면 이 코드는 그대로 걸음이 된다.
        /// 돌진 시퀀스는 제자리에서도 진동이 보여야 하므로 sequenceStrideBoost로 위상을 따로 돌린다.
        /// </summary>
        private float UpdateGait(float dt)
        {
            Vector3 now = transform.position;
            Vector3 delta = now - lastPosition;
            delta.y = 0f;                        // 지면 굴곡을 따라 오르내리는 것은 걸음이 아니다
            lastPosition = now;

            float distance = delta.magnitude;
            float rawSpeed = distance / dt;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, 1f - Mathf.Exp(-SpeedSmoothing * dt));

            gaitPhase += distance * strideRadiansPerMeter + sequenceStrideBoost * dt;

            float speedWeight = Mathf.Clamp01(smoothedSpeed / Mathf.Max(0.01f, fullGaitSpeed));
            float weight = Mathf.Max(speedWeight, sequenceGaitWeight);

            // 발 디딤: 한 걸음 주기(2π)에 두 번(π마다) 지면을 찍는다. 아래 바운스 곡선의 최저점과 같은 위상이다.
            int footfallIndex = Mathf.FloorToInt(gaitPhase / Mathf.PI);
            if (footfallIndex != lastFootfallIndex)
            {
                lastFootfallIndex = footfallIndex;
                if (weight > FootfallMinWeight)
                    Footfall(weight);
            }

            return weight;
        }

        /// <summary>
        /// 걷기 + 정지 + 시퀀스 오프셋을 합쳐 이번 프레임에 몸통이 놓일 위치(미터)를 만든다.
        /// x = 좌우, y = 상하, z = 앞뒤이며 전부 곰이 바라보는 방향 기준(루트 로컬)이다.
        /// </summary>
        private Vector3 ComposeOffset(float gaitWeight, float dt)
        {
            // 상하: 0에서 시작해 위로만 뜬다(가라앉지 않는다). 최저점이 발 디딤과 정확히 겹친다.
            float bounce = (0.5f - 0.5f * Mathf.Cos(gaitPhase * 2f)) * bounceMeters * gaitWeight;

            // 좌우: 한 걸음 주기에 한 번 좌우로 체중이 실린다(무거운 짐승 특유의 뒤뚱거림).
            float sway = Mathf.Sin(gaitPhase) * swayMeters * gaitWeight;

            Vector3 walk = new Vector3(sway, bounce, 0f);

            // 서 있을 때: 호흡은 HazardSource의 스케일 펄스가 담당하고, 여기서는 아주 느린 체중 이동만 더한다.
            idleTime += dt;
            float idleWeight = 1f - gaitWeight;
            Vector3 idle = new Vector3(
                Mathf.Sin(idleTime * 0.62f) * idleSwayMeters,
                Mathf.Sin(idleTime * 0.41f) * idleBobMeters,
                0f) * idleWeight;

            return walk + idle + sequenceOffset;
        }

        /// <summary>
        /// 계산한 오프셋(미터)을 자식 파츠의 localPosition에 적용한다. 회전/스케일은 건드리지 않는다.
        ///
        /// 어깨 혹만 감쇠 추종(SmoothDamp)으로 **한 박자 늦게** 같은 오프셋을 따라간다. 몸통이 먼저 올라가고
        /// 혹이 뒤늦게 따라 올라오는 이 시간차가 "무거운 근육 덩어리"로 읽히는 부분이다. 어긋남은
        /// humpMaxSlackMeters로 잘라 두는데, 혹 아랫면이 몸통 안에 45cm 묻혀 있어(BearHumpMeters 주석)
        /// 이 범위에서는 어떤 위상에서도 혹과 몸통 사이에 틈이 생기지 않는다.
        /// </summary>
        private void ApplyOffset(Vector3 offsetMeters, float dt)
        {
            Vector3 local = Vector3.Scale(offsetMeters, metersToLocal);

            for (int i = 0; i < bodyParts.Length; i++)
            {
                Transform part = bodyParts[i];
                if (part == null)
                    continue;

                part.localPosition = bodyBasePositions[i] + local;
            }

            // 다리 상부는 절반만 따라간다 - 어깨(위)와 무릎(아래) 여유를 반씩 나눠 쓰는 유일한 방법이다.
            if (legPart != null)
                legPart.localPosition = legBasePosition + local * LegFollow;

            if (humpPart == null)
                return;

            humpFollow = Vector3.SmoothDamp(humpFollow, offsetMeters, ref humpVelocity,
                Mathf.Max(0.0001f, humpLagSeconds), Mathf.Infinity, dt);

            Vector3 slack = Vector3.ClampMagnitude(humpFollow - offsetMeters, humpMaxSlackMeters);
            Vector3 humpLocal = Vector3.Scale(offsetMeters + slack, metersToLocal);
            humpPart.localPosition = humpBasePosition + humpLocal;
        }

        /// <summary>
        /// 발 하나가 지면을 찍은 순간. 저역 "쿵" 소리를 내고 충격 훅을 쏜다(카메라 흔들림은 구독자 몫).
        /// </summary>
        private void Footfall(float weight)
        {
            PlayThud(weight * 0.55f);
            RaiseImpact(weight * 0.30f, 0.10f);
        }

        // ── 1회성 시퀀스 ───────────────────────────────────────────────────────────────
        /// <summary>
        /// 포효. 몸을 낮춰 반동을 모았다가 상체를 크게 들어올리고, 최고점에서 소리와 충격 훅을 함께 쏜다.
        /// 회전이 없으므로 "일어선다"는 상체 전체를 위로 밀어 올리는 것으로 표현한다(위 제약 (2) 참고).
        /// </summary>
        public void PlayRoar()
        {
            StartSequence(RoarRoutine());
        }

        /// <summary>
        /// 돌진. 짧게 웅크렸다가 앞으로 가속하고, 멈출 때 상체가 앞으로 쏠렸다가 되돌아온다.
        /// 제자리에서도 보이도록 가속 구간에서 보행 위상을 강제로 돌린다(sequenceStrideBoost).
        /// </summary>
        public void PlayCharge()
        {
            StartSequence(ChargeRoutine());
        }

        /// <summary>
        /// 앞발 내려치기. 상체를 들었다가 아주 짧게(0.09초) 내려꽂고, 그 순간 가장 강한 충격 훅을 쏜다.
        /// </summary>
        public void PlaySlam()
        {
            StartSequence(SlamRoutine());
        }

        /// <summary>
        /// 재생 중인 시퀀스를 끊고 모든 파츠를 원래 자리로 즉시 되돌린다.
        /// 물리쳐서 사라지거나 세이브에서 처치 상태를 복원할 때 부른다 - 다시 나타났을 때
        /// 마지막 자세가 남아 있으면 몸통이 어긋난 채로 등장한다.
        /// </summary>
        public void StopAndReset()
        {
            if (sequence != null)
            {
                StopCoroutine(sequence);
                sequence = null;
            }

            sequenceOffset = Vector3.zero;
            sequenceGaitWeight = 0f;
            sequenceStrideBoost = 0f;
            humpFollow = Vector3.zero;
            humpVelocity = Vector3.zero;
            smoothedSpeed = 0f;

            if (!bound)
                return;

            for (int i = 0; i < bodyParts.Length; i++)
            {
                if (bodyParts[i] != null)
                    bodyParts[i].localPosition = bodyBasePositions[i];
            }

            if (legPart != null)
                legPart.localPosition = legBasePosition;

            if (humpPart != null)
                humpPart.localPosition = humpBasePosition;
        }

        /// <summary>
        /// 시퀀스를 하나만 돌린다. 이미 재생 중이면 새 요청을 무시한다 - 접촉이 연달아 들어올 때
        /// 동작이 매 프레임 처음으로 되감기면 아무 동작으로도 안 읽힌다.
        /// </summary>
        private void StartSequence(IEnumerator routine)
        {
            if (!bound || sequence != null || !isActiveAndEnabled)
                return;

            sequence = StartCoroutine(routine);
        }

        /// <summary>포효: 준비(웅크림) → 상승(포효) → 버팀(떨림) → 복귀.</summary>
        private IEnumerator RoarRoutine()
        {
            // 반동을 모은다: 몸을 낮추고 뒤로 당긴다(아래쪽은 여유가 넉넉해 마음껏 쓴다).
            yield return Blend(new Vector3(0f, -0.055f, -0.030f), 0.22f);

            // 상체를 밀어 올린다. y 0.085는 관절 여유 상한 0.10 바로 아래이고, 아래 떨림 0.004까지
            // 더해도 0.089라 무릎/골반 이음매가 열리지 않는다(클래스 주석의 '관절 여유' 참고).
            Vector3 peak = new Vector3(0f, 0.085f, 0.030f);
            yield return Blend(peak, 0.30f);

            PlayRoarVoice();
            RaiseImpact(0.45f, 0.35f);

            float held = 0f;
            while (held < 0.80f)
            {
                held += Time.deltaTime;
                sequenceOffset = peak + new Vector3(
                    Mathf.Sin(held * 37f) * 0.006f,
                    Mathf.Sin(held * 29f) * 0.004f,
                    0f);
                yield return null;
            }

            yield return Blend(Vector3.zero, 0.45f);
            sequence = null;
        }

        /// <summary>돌진: 웅크림 → 가속(앞으로 밀림 + 강제 보행 진동) → 급정지(앞쏠림 + 착지 충격) → 복귀.</summary>
        private IEnumerator ChargeRoutine()
        {
            // 전조: 크게 웅크린다. 수직 아래는 여유가 커서 이 동작이 시퀀스 중 가장 큰 진폭이다.
            yield return Blend(new Vector3(0f, -0.080f, -0.032f), 0.28f);

            sequenceGaitWeight = 1f;
            sequenceStrideBoost = 11f;   // rad/s. 제자리에서도 초당 약 1.75걸음이 보인다
            // 가속: 앞으로 실린다. 수평은 어깨 묻힘(2.2cm)이 상한을 잡아 0.035를 넘길 수 없다.
            yield return Blend(new Vector3(0f, 0.020f, 0.032f), 0.32f);

            float held = 0f;
            while (held < 0.55f)
            {
                held += Time.deltaTime;
                yield return null;
            }

            // 감속: 몸이 먼저 서고 상체가 관성으로 앞으로 더 쏠린다.
            sequenceStrideBoost = 0f;
            sequenceGaitWeight = 0f;
            yield return Blend(new Vector3(0f, -0.045f, 0.035f), 0.18f);

            PlayThud(0.8f);
            RaiseImpact(0.6f, 0.25f);

            yield return Blend(Vector3.zero, 0.50f);
            sequence = null;
        }

        /// <summary>앞발 내려치기: 들어올림 → 내려꽂기(0.09초) → 착지 충격 → 여진 → 복귀.</summary>
        private IEnumerator SlamRoutine()
        {
            // 앞발을 들어올린다(위쪽 상한 0.10 안).
            yield return Blend(new Vector3(0f, 0.070f, -0.028f), 0.20f);
            // 내려꽂는다. 0.09초 - 이 시퀀스에서 속도가 곧 무게로 읽히는 유일한 구간이다.
            yield return Blend(new Vector3(0f, -0.095f, 0.030f), 0.09f);

            PlayThud(1f);
            RaiseImpact(1f, 0.22f);

            yield return Blend(new Vector3(0f, 0.028f, 0.012f), 0.12f);
            yield return Blend(Vector3.zero, 0.30f);
            sequence = null;
        }

        /// <summary>
        /// 현재 시퀀스 오프셋을 target까지 seconds 동안 부드럽게(SmoothStep) 옮긴다.
        /// Time.deltaTime으로만 진행하므로 timeScale이 0이면 시퀀스도 그 자리에서 멈춘다.
        /// </summary>
        private IEnumerator Blend(Vector3 target, float seconds)
        {
            Vector3 from = sequenceOffset;
            float elapsed = 0f;
            float duration = Mathf.Max(0.0001f, seconds);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                sequenceOffset = Vector3.Lerp(from, target, t);
                yield return null;
            }

            sequenceOffset = target;
        }

        // ── 사운드 / 훅 ────────────────────────────────────────────────────────────────
        // 오디오 클립 에셋이 프로젝트에 한 장도 없으므로, 이미 있는 절차적 합성기를 그대로 쓴다
        // (ProceduralAudioClipGenerator - AudioClip.Create로 PCM을 직접 채우는 방식. AudioManager의
        //  모든 효과음과 BackgroundMusicGenerator의 BGM이 같은 방식이다). 새 합성 코드를 쓰지 않고
        //  기존 공개 메서드만 조합하므로 파형 품질/클리핑은 이미 검증된 것을 그대로 따른다.
        // 클립은 static이라 곰이 몇 마리든 3장뿐이고, 첫 사용 시 한 번만 만들어진다.
        private static AudioClip thudClip;
        private static AudioClip roarLowClip;
        private static AudioClip roarNoiseClip;

        /// <summary>발 디딤/착지용 저역 "쿵"과 포효용 저음 + 노이즈 클립을 처음 쓸 때 한 번만 만든다.</summary>
        private static void EnsureClips()
        {
            if (thudClip == null)
                thudClip = ProceduralAudioClipGenerator.CreateBeep(96f, 0.16f, 42f); // 96Hz -> 42Hz로 떨어지는 저역 충격음

            if (roarLowClip == null)
                roarLowClip = ProceduralAudioClipGenerator.CreateBuzz(84f, 0.62f, 1, 0.26f); // 낮은 사각파 한 번 = 으르렁대는 성대

            if (roarNoiseClip == null)
                roarNoiseClip = ProceduralAudioClipGenerator.CreateNoiseBurst(0.5f); // 그 위에 겹치는 숨/거친 결
        }

        /// <summary>지면 충격음을 세기에 맞춰 낸다. 세기가 볼륨과 피치를 함께 바꾼다(약한 디딤은 더 가볍게 들린다).</summary>
        private void PlayThud(float strength)
        {
            if (voice == null)
                return;

            EnsureClips();
            if (thudClip == null)
                return;

            float s = Mathf.Clamp01(strength);
            voice.pitch = Mathf.Lerp(1.15f, 0.85f, s);
            voice.PlayOneShot(thudClip, SfxVolume() * Mathf.Lerp(0.35f, 1f, s));
        }

        /// <summary>포효. 저음과 노이즈를 겹쳐 한 번에 낸다(PlayOneShot은 여러 클립이 겹쳐 울린다).</summary>
        private void PlayRoarVoice()
        {
            if (voice == null)
                return;

            EnsureClips();
            voice.pitch = 1f;

            if (roarLowClip != null)
                voice.PlayOneShot(roarLowClip, SfxVolume());

            if (roarNoiseClip != null)
                voice.PlayOneShot(roarNoiseClip, SfxVolume() * 0.45f);
        }

        /// <summary>설정 화면에서 정한 효과음 볼륨을 따른다. AudioManager가 아직 없으면 그 기본값을 쓴다.</summary>
        private static float SfxVolume()
        {
            return AudioManager.Instance != null ? AudioManager.Instance.sfxVolume : 0.7f;
        }

        /// <summary>충격 훅을 쏜다. 구독자가 없으면 아무 일도 일어나지 않는다.</summary>
        private static void RaiseImpact(float strength, float duration)
        {
            System.Action<float, float> handler = OnImpact;
            if (handler == null)
                return;

            handler(Mathf.Clamp01(strength), Mathf.Max(0f, duration));
        }

        /// <summary>
        /// 이 컴포넌트가 꺼질 때(처치 등) 마지막 자세가 남지 않도록 파츠를 원래 자리로 되돌린다.
        /// Update가 멈춘 뒤 어긋난 자세로 굳어 버리는 것을 막는 유일한 지점이다.
        /// </summary>
        private void OnDisable()
        {
            StopAndReset();
        }
    }
}
