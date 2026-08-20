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
    ///     ※ [B35] 루트를 움직이는 주체가 생겼다: 곰 추격 AI(HazardSource의 "곰 추격 AI" 구획)가
    ///       루트의 position/rotation을 소유한다. 그래도 이 제약은 그대로다 - 루트는 **콜라이더를
    ///       소유한 쪽**이 옮겨야 판정과 몸이 함께 가고, 이 파일은 그 위에 얹히는 오프셋만 만든다.
    ///       세 채널이 정확히 갈라져 있다: 루트 position/rotation(AI) · 자식 localPosition(여기) ·
    ///       자식 localScale(HazardSource의 호흡). 서로 덮어쓰지 않는다.
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
    ///
    /// ── [B36] 통짜 메시(실물 3D 모델) 예외 ──────────────────────────────────────────────
    /// 곰이 bear_adult.obj로 만들어지면 위 파츠(Hump/Limbs/Claws)가 **하나도 없다** - 몸이 메시 한 장이다.
    /// 이 파일은 그 상황에서 (a) 예외 없이 (b) 모델 자식 하나를 통째로 흔드는 것으로 자동 전환된다
    /// (solidBody 필드 참고). 이음매가 없으니 위 진폭 상한은 적용되지 않고 2배로 키우되, 발바닥이 곧
    /// 몸의 밑면이라 **아래로 내려가는 성분만 잘라** 위로만 뜨게 한다. 파츠 이름을 찾는 코드는 원래부터
    /// null을 허용했으므로(FindPart / ApplyOffset의 null 가드) 그 경로는 한 줄도 바뀌지 않았다.
    /// </summary>
    public class CreatureMotion : MonoBehaviour
    {
        // ── [실사감 F3] 잔디를 밟는 것들의 명부 ──────────────────────────────────────
        //
        // 왜 여기인가: 잔디를 밟아야 하는 것은 "월드를 돌아다니는 생물"이고, 그것들이 공통으로
        // 붙이고 있는 컴포넌트가 이것이다. 곰 AI(HazardSource)나 사냥감(HuntableCreature)에 각각
        // 등록 코드를 넣으면 생물이 늘 때마다 빠뜨린다.
        //
        // 리스트를 static으로 두는 대신 잔디 쪽이 매 프레임 FindObjectsByType을 부르는 방법도
        // 있지만, 그건 이 프로젝트가 반복해서 피해 온 형태다(할당 + 전수 검색).
        //
        // ★ 씬 재로드 대비: 정적 리스트라 리셋 훅이 필요하다. OnDisable이 항상 불린다는 보장이
        //   없기 때문이다(도메인 리로드를 끈 플레이 모드에서 옛 인스턴스가 남을 수 있다).
        private static readonly System.Collections.Generic.List<CreatureMotion> benders =
            new System.Collections.Generic.List<CreatureMotion>();

        /// <summary>지금 월드에 살아 있는, 잔디를 밟는 생물들. 잔디 시스템이 읽는다.</summary>
        public static System.Collections.Generic.IReadOnlyList<CreatureMotion> Benders => benders;

        [Tooltip("이 생물이 잔디를 눕히는 반경(m). 0 이하면 잔디를 밟지 않는다.")]
        public float grassBendRadius = 1.1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBenders()
        {
            benders.Clear();
        }

        private void OnEnable()
        {
            if (!benders.Contains(this))
                benders.Add(this);
        }

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

        // ── [B36] 통짜 메시 모드 ────────────────────────────────────────────────────────
        /// <summary>
        /// 호출부가 파츠 역할(혹/다리/발톱)을 요청했는데 **하나도 찾지 못한** 상태.
        /// 곰이 실물 3D 모델(bear_adult.obj)로 만들어지면 몸이 메시 한 장이라 이 파츠들이 존재하지 않는다.
        /// 이때는 몸 전체(모델 자식 하나)를 통째로 흔드는 것 말고는 표현할 방법이 없고, 반대로
        /// 클래스 주석의 '관절 여유' 상한(위 0.10m / 수평 0.035m)은 **적용되지 않는다** -
        /// 그 숫자는 강체 조각들을 파묻어 이은 절차 곰에서 조각끼리 어긋날 수 있는 거리였기 때문이다.
        /// 통짜 메시는 어긋날 이음매가 없으므로 진폭을 키워야 오히려 "미끄러지지 않고" 걷는 것으로 읽힌다.
        /// </summary>
        private bool solidBody;

        /// <summary>통짜 메시일 때 모든 오프셋에 곱하는 배수. 걷기 상하 0.050 → 0.10m, 포효 0.085 → 0.17m.</summary>
        private const float SolidBodyAmplitude = 2f;

        /// <summary>
        /// 통짜 메시일 때 허용하는 **최하점**(m). 절차 곰은 아래로 내려가도 다리가 몸통 안으로 더 들어갈 뿐이라
        /// 여유가 넉넉했지만(웅크림 -0.080, 내려치기 -0.095), 모델 곰은 발바닥이 곧 몸의 밑면이라
        /// 1cm만 내려가도 발이 지면을 파고든다. 그래서 아래 성분을 사실상 0으로 잘라 **위로만** 뜨게 한다.
        /// </summary>
        private const float SolidBodyMinRise = -0.01f;

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

            // [B36] 파츠를 **요청했는데 하나도 없다** = 몸이 메시 한 장인 모델 곰이다(위 solidBody 주석).
            // 이름을 아예 넘기지 않은 호출부(다른 생물)는 요청 자체가 없으므로 예전 동작 그대로다.
            bool requestedParts = !string.IsNullOrEmpty(laggingPartName)
                || !string.IsNullOrEmpty(plantedPartName)
                || !string.IsNullOrEmpty(legPartName);
            solidBody = requestedParts && lagging == null && planted == null && leg == null;

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
        ///
        /// [B36] 세 이름은 **모델 곰에는 없다**(몸이 메시 한 장이다). 그래도 그대로 넘긴다 - Bind가
        /// "요청했는데 하나도 없음"을 통짜 메시의 신호로 삼아 전신 모션으로 전환하기 때문이다.
        /// BearBodyScale도 모델 유무에 따라 알아서 갈리므로(CreatureVisualBuilder) 여기는 손댈 곳이 없다.
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
        /// [B37] 새끼 곰 전용 진입점. 성체와 **같은 통짜 메시 경로**를 쓰되(파츠 이름 세 개를 똑같이
        /// 넘긴다 - 모델 새끼에는 그 파츠가 없으므로 Bind가 solidBody로 전환하고, 폴백 새끼는 성체를
        /// 축소한 것이라 파츠가 그대로 있어 예전 경로가 돈다), 진폭만 몸 크기에 비례해 줄인다.
        ///
        /// 규격(BearCubBodyScale)을 넘기는 것이 핵심이다. 이 값이 미터 → 루트 로컬 변환의 분모이고,
        /// 새끼 몸의 자식은 1/BearCubBodyScale로 붙어 있으므로(모델 경로) 또는 성체 규격 × shrink로
        /// 붙어 있으므로(폴백 경로) 두 경우 모두 이 분모 하나가 맞는다.
        ///
        /// 새끼는 포효/돌진/슬램을 **하지 않는다**(HazardSource가 아예 부르지 않는다). 그래서 시퀀스
        /// 진폭(코루틴 안의 상수)은 손댈 이유가 없고, 여기서는 보행/정지 진폭만 조정한다.
        /// </summary>
        public static CreatureMotion AttachBearCub(GameObject body, float phaseSeed)
        {
            if (body == null)
                return null;

            CreatureMotion motion = body.GetComponent<CreatureMotion>();
            if (motion == null)
                motion = body.AddComponent<CreatureMotion>();

            motion.Bind(CreatureVisualBuilder.BearCubBodyScale, phaseSeed,
                laggingPartName: "Hump",
                plantedPartName: "Claws",
                legPartName: "Limbs");

            // 몸 높이 비율(모델 실측 기준 0.65 / 1.22 ≈ 0.53). 진폭은 관절 여유에 비례하므로 몸이
            // 절반이면 흔들림도 절반이어야 한다 - 성체 값을 그대로 쓰면 새끼가 통통 튀어 보인다.
            float ratio = Mathf.Clamp(
                CreatureVisualBuilder.BearCubBodyScale.y /
                Mathf.Max(0.0001f, CreatureVisualBuilder.BearBodyScale.y), 0.2f, 1f);

            motion.bounceMeters *= ratio;
            motion.swayMeters *= ratio;
            motion.idleSwayMeters *= ratio;
            motion.idleBobMeters *= ratio;
            motion.humpMaxSlackMeters *= ratio;

            // 다리가 짧으면 같은 거리를 더 많은 걸음으로 간다 → 1m당 위상이 더 많이 돈다.
            motion.strideRadiansPerMeter /= ratio;
            // 가벼운 짐승이라 더 빨리 "제 보폭"에 닿고, 관성도 덜 남는다.
            motion.fullGaitSpeed *= 0.75f;
            motion.humpLagSeconds *= ratio;

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
            voice.dopplerLevel = 0f;             // [B35] 곰이 달리기 시작해도 0으로 둔다 - 절차 합성 클립은
                                                 // 짧고 피치가 이미 세기에 따라 변해서, 도플러까지 얹으면
                                                 // 포효/발소리의 음정이 추격 중에 널뛴다.
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
        /// 고정되므로 발이 미끄러지는 느낌이 나지 않는다.
        /// [B35] 이 경로가 드디어 실제로 돈다 - 곰 추격 AI가 루트를 옮기기 시작해서, 배회(약 1.2m/s)와
        /// 추격(약 5.3m/s)의 거리 차이가 그대로 진폭과 발 디딤 간격의 차이로 나온다. 이 파일은
        /// 한 줄도 바뀌지 않았다(거리만 들어오면 되는 설계였다).
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

            Vector3 total = walk + idle + sequenceOffset;

            // [B36] 통짜 메시(모델 곰)는 이음매가 없어 진폭을 키울 수 있고, 대신 발바닥이 곧 몸의 밑면이라
            // 아래로는 내려갈 수 없다. 배수를 먼저 곱한 뒤 최하점을 자른다(위 두 상수 주석 참고).
            if (solidBody)
            {
                total *= SolidBodyAmplitude;
                total.y = Mathf.Max(total.y, SolidBodyMinRise);
            }

            return total;
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
            benders.Remove(this);
            StopAndReset();
        }

        // ── [B52] 이동 생물 공용 장애물 검사(정적 · 순수 질의) ─────────────────────────────
        //  루트를 실제로 옮기는 쪽(곰 AI의 DriveBear 등)이 이번 프레임 이동분을 적용하기 **전에**
        //  부르는 유틸. 배경: 바위 큰 덩어리/거암/절벽에 convex MeshCollider가 생겼는데
        //  (IslandMeshGenerator.Vegetation) 곰은 transform 직접 이동이라 물리 충돌을 받지 않아
        //  바위를 그대로 뚫고 걸었다.
        //
        //  ★ 이 파일의 계약(클래스 주석 (1) "루트를 절대 건드리지 않는다")은 그대로다 ★
        //  아래 메서드들은 static이고 어떤 Transform에도 쓰기를 하지 않는다 - "얼마나 갈 수 있는지"만
        //  계산해 돌려주고, 루트를 옮기는 것은 여전히 콜라이더를 소유한 쪽(HazardSource)이다.
        //  공용으로 여기에 둔 이유: 이동 코드가 생기는 다른 생물도 같은 검사를 그대로 쓰기 위해서다
        //  (사냥감 HuntableCreature는 현재 제자리 고정이라 호출부가 없다).
        //
        //  걸러내는 것(막지 않는 것):
        //   · 지형 "Island_" 콜라이더 - TerrainSampler와 같은 명명 규칙. 접지는 호출자의 지형 프로브가
        //     따로 담당하므로 여기서 지형에 걸리면 오르막마다 헛발이 난다.
        //   · Water 레이어 - 마스크(Default만)에서 이미 빠진다. 물가 진입 금지도 호출자 몫이다.
        //   · 트리거(QueryTriggerInteraction.Ignore) - 곰/벌떼 등 위험 요소의 접촉 판정 콜라이더.
        //   · 자기 자신(루트/자식) · 플레이어(CharacterController) · 리지드바디가 달린 동체 ·
        //     다른 생물(HazardSource/HuntableCreature) - 생물끼리는 서로 길을 막지 않는다.
        //  남는 것 = 기본 레이어의 정적 장애물: 바위 convex 헐/절벽 박스, 야자수 줄기, 건축물 등.

        /// <summary>스피어캐스트 히트 재사용 버퍼. 메인 스레드 전용(이 프로젝트의 물리 질의는 전부 메인 스레드다).</summary>
        private static readonly RaycastHit[] obstacleCastHits = new RaycastHit[16];

        /// <summary>Default 레이어만 캐스트한다. 지형도 Default지만 이름("Island_")으로 거르고, 물은 Water 레이어라 여기서 이미 빠진다.</summary>
        private const int ObstacleCastLayerMask = 1 << 0;

        /// <summary>이동분보다 이만큼(m) 더 내다본다 - 다음 프레임에 파고들기 직전에 미리 미끄러지기 시작한다.</summary>
        private const float ObstacleCastSkin = 0.05f;

        /// <summary>지형 콜라이더 이름 접두사(TerrainSampler.TerrainNamePrefix와 같은 규칙 - 그쪽은 private라 여기 다시 적는다).</summary>
        private const string TerrainObstaclePrefix = "Island_";

        /// <summary>
        /// 이번 프레임의 수평 이동분(motion)을 몸통 반경의 스피어로 캐스트해서, 실제로 갈 수 있는
        /// 이동분을 돌려준다. (a) 장애물에 막히면 표면 접선으로 투영해 미끄러지고(ProjectOnPlane),
        /// (b) 미끄러질 곳조차 막혀 있으면 Vector3.zero + blocked=true를 돌려준다 - 호출자는 이동을
        /// 취소하고(배회라면) 목적지를 무효화하면 된다. 어떤 Transform에도 쓰기를 하지 않는다.
        /// </summary>
        /// <param name="mover">이동 주체의 루트. 자기 콜라이더(루트/자식) 히트를 거르는 데만 쓴다.</param>
        /// <param name="castCenter">스피어 중심(월드). 보통 루트 중심 - 몸통 높이의 장애물만 본다.</param>
        /// <param name="motion">이번 프레임 수평 이동분(월드, y는 무시된다).</param>
        /// <param name="bodyRadius">몸통 반경(m). 스피어 반경으로 그대로 쓴다.</param>
        /// <param name="blocked">미끄러질 곳도 없이 완전히 막혔으면 true.</param>
        public static Vector3 ResolveObstacleMotion(Transform mover, Vector3 castCenter, Vector3 motion,
            float bodyRadius, out bool blocked)
        {
            blocked = false;
            motion.y = 0f;

            float distance = motion.magnitude;
            if (mover == null || distance <= 0.0001f || bodyRadius <= 0.01f)
                return motion;   // 서 있으면 캐스트하지 않는다(성능 규칙: 캐스트는 이동 중일 때만)

            RaycastHit obstacle;
            if (!FindBlockingObstacle(mover, castCenter, motion / distance, distance, bodyRadius, out obstacle))
                return motion;

            // (a) 표면을 따라 미끄러진다. 헐/박스 면이 기울어 있어도 수평 이동만 하므로 법선의
            //     수평 성분에 대해서만 투영한다(수직 성분을 남기면 이동에 y가 새어 들어 접지가 흔들린다).
            Vector3 wallNormal = obstacle.normal;
            wallNormal.y = 0f;
            if (wallNormal.sqrMagnitude > 0.0001f)
            {
                wallNormal.Normalize();
                Vector3 slide = Vector3.ProjectOnPlane(motion, wallNormal);
                slide.y = 0f;

                float slideDistance = slide.magnitude;
                if (slideDistance > 0.0001f
                    && !FindBlockingObstacle(mover, castCenter, slide / slideDistance, slideDistance, bodyRadius, out _))
                    return slide;   // 프레임당 캐스트 최대 2회(원래 방향 + 미끄러질 방향)
            }

            // (b) 미끄러질 방향까지 막혔다(모서리에 정면으로 박힘). 이동 취소.
            blocked = true;
            return Vector3.zero;
        }

        /// <summary>
        /// 지정 방향으로 스피어캐스트해서 "정말로 길을 막는" 가장 가까운 히트를 찾는다. 없으면 false.
        /// 시작 시점에 이미 겹쳐 있는 콜라이더(distance 0 반환)는 막지 않는다 - 스폰/복원으로 바위 속에
        /// 박힌 개체가 영원히 못 빠져나오는 것을 막는 표준 처리다(걸어 나가는 것은 허용).
        /// </summary>
        private static bool FindBlockingObstacle(Transform mover, Vector3 castCenter, Vector3 direction,
            float distance, float bodyRadius, out RaycastHit closest)
        {
            closest = default(RaycastHit);

            int count = Physics.SphereCastNonAlloc(castCenter, bodyRadius, direction,
                obstacleCastHits, distance + ObstacleCastSkin, ObstacleCastLayerMask,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = obstacleCastHits[i];
                if (hit.collider == null || hit.distance <= 0f)
                    continue;   // 시작 겹침(위 주석) 또는 무효 항목

                if (IsObstacleIgnored(mover, hit.collider))
                    continue;

                if (!found || hit.distance < closest.distance)
                {
                    closest = hit;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>이 콜라이더는 이동을 막는 장애물로 치지 않는다(클래스 하단 [B52] 구획 주석의 목록).</summary>
        private static bool IsObstacleIgnored(Transform mover, Collider hitCollider)
        {
            Transform hitTransform = hitCollider.transform;
            if (hitTransform == mover || hitTransform.IsChildOf(mover))
                return true;   // 자기 몸

            if (hitCollider.gameObject.name.StartsWith(TerrainObstaclePrefix))
                return true;   // 지형 - 접지/경사는 호출자의 지형 프로브 몫

            if (hitCollider is CharacterController)
                return true;   // 플레이어 - 접촉 판정은 트리거 콜라이더가 담당한다

            if (hitCollider.attachedRigidbody != null)
                return true;   // 움직이는 물리 동체 - 정적 장애물이 아니다

            // 다른 생물. 위험 요소는 대부분 트리거라 위에서 이미 걸러지지만, 종류가 늘어도 안전하게.
            if (hitCollider.GetComponentInParent<HazardSource>() != null)
                return true;
            if (hitCollider.GetComponentInParent<HuntableCreature>() != null)
                return true;

            return false;
        }
    }
}
