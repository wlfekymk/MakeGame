using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 낙수(물방울 떨어짐). 지붕 처마·쉼터·야자 잎끝에서 물방울이 하나씩 떨어져 지면에서 튄다.
    ///
    /// ── 왜 이것이 사실감의 핵심인가 ────────────────────────────────────────────
    /// 지금까지 이 게임의 비는 **켜지고 꺼지는 스위치**였다. 비가 그치는 순간 빗줄기·물튀김·빗소리가
    /// 한꺼번에 사라지고, 방금까지 폭우가 쏟아지던 섬이 1초 만에 맑은 날과 완전히 같아진다.
    /// 실제로 비가 그친 뒤 한동안 세상을 채우는 것은 비가 아니라 **비의 잔여물**이다 - 처마 끝과
    /// 잎끝에 맺혔던 물이 한참 동안 뚝, 뚝 떨어진다. 이 드라이버가 담당하는 것이 정확히 그 구간이며,
    /// 그래서 비가 그친 뒤에도 **48초에 걸쳐 천천히 잦아든다**(아래 활성도 곡선).
    ///
    /// ── 규약 ───────────────────────────────────────────────────────────────────
    /// · 파티클 시스템은 **월드 전체에 2개**(물방울 1 + 파문 1)뿐이고, 대상 지점마다
    ///   Emit(EmitParams)로 위치를 지정해 뿌린다. 처마·잎끝마다 시스템을 만들면 수십 개가 된다 -
    ///   ShorelineRibbon의 물보라(ShoreSprayFX)가 이미 같은 이유로 쓰는 규약을 그대로 따랐다.
    /// · 대상 탐색은 2초 주기로 물리 질의 1회(+ 새로 찾은 지점의 착지 높이 레이) — **프레임당
    ///   물리 질의 0**. 매 프레임 하는 일은 앵커 ≤24개의 타이머 비교뿐이라 힙 할당도 0이다.
    /// · rng는 이 클래스 전용 System.Random 하나다. 월드 생성 스트림은 한 톨도 건드리지 않는다.
    /// · 세이브 포맷과 무관하다(순수 연출).
    /// </summary>
    public class RainDrips : MonoBehaviour
    {
        // ── 활성도 곡선 ──────────────────────────────────────────────────────────

        /// <summary>비가 오기 시작할 때 활성도가 차오르는 시간 상수(초). 0 → 0.95까지 약 9초.</summary>
        private const float DripRiseTau = 3f;

        /// <summary>
        /// 비가 그친 뒤 활성도가 빠지는 시간 상수(초). 아래 DripFloorSeconds와 짝이다.
        /// RainWetness.StepWetness(젖음 곡선의 정본)를 그대로 빌려 쓴다 - 낙수와 젖음은 "빨리 생기고
        /// 아주 천천히 마른다"는 같은 물리이므로 곡선을 따로 만들 이유가 없고, 두 연출이 어긋나지도 않는다.
        /// 셰이더 전역 _MG_Wetness를 C#에서 되읽지 않고 같은 함수로 **자체 적분**한다(RainWetness가
        /// 씬에 없어도 낙수는 그대로 동작해야 하므로).
        /// 실측(dt 1/60·1/30 양쪽 동일): 1 → 0 까지 **48.3초** · 절반 15.3초 · 10% 38.2초.
        /// 요구 구간(30~60초)의 한가운데이고, 프레임률이 바뀌어도 곡선이 같다(dt 지수식이라서).
        /// </summary>
        private const float DripDryTau = 30f;

        /// <summary>지수 꼬리를 잘라 유한 시간에 정확히 0에 닿게 하는 선형 바닥항(초). 위 주석 참고.</summary>
        private const float DripFloorSeconds = 120f;

        // ── 대상 탐색 ────────────────────────────────────────────────────────────

        /// <summary>대상을 다시 훑는 주기(초). 건축은 플레이 중에 늘어나므로 1회가 아니라 저빈도 반복이다.</summary>
        private const float RescanInterval = 2f;

        /// <summary>탐색 반경(m) = 컬링 거리. 이보다 먼 낙수는 보이지도 들리지도 않으므로 아예 만들지 않는다.</summary>
        private const float ScanRadius = 40f;

        /// <summary>탐색용 콜라이더 버퍼(사전 할당). 40m 안의 콜라이더가 이보다 많으면 앞의 128개만 본다.</summary>
        private const int ScanBufferSize = 128;

        /// <summary>
        /// 동시에 유지하는 낙수 지점 상한. 파티클 예산의 뿌리다 —
        /// 24개 × (최소 간격 1.4초 × 지터 평균 1.1) ≈ 초당 15.6방울,
        /// 물방울 수명 2.0초 → 살아 있는 물방울 약 31개(상한 48 · 여유 55%),
        /// 파문 수명 평균 0.36초 → 약 5.6개(상한 24 · 여유 4배).
        /// 이것이 이 연출의 **최대치**다(활성도 1 = 폭우 중이고 24지점이 전부 40m 안일 때).
        /// </summary>
        private const int MaxAnchors = 24;

        /// <summary>낙수 판정에 쓰는 물리 레이어(Default=0만). WeatherSystem.RainCollisionMask와 같은 근거.</summary>
        private const int DripLayerMask = 1 << 0;

        // ── 방울 타이밍 ──────────────────────────────────────────────────────────

        /// <summary>활성도 1(폭우 중)일 때 한 지점의 방울 간격(초).</summary>
        private const float MinDripInterval = 1.4f;

        /// <summary>활성도 0에 가까울 때 한 지점의 방울 간격(초). 다 마르기 직전엔 아주 드물게 떨어진다.</summary>
        private const float MaxDripInterval = 9f;

        /// <summary>간격에 곱하는 지터 범위. 지점끼리 박자가 맞아 "합주"하지 않게 한다.</summary>
        private const float IntervalJitterMin = 0.6f;
        private const float IntervalJitterMax = 1.6f;

        /// <summary>착지 파문 예약 슬롯 수(사전 할당 링 버퍼). 낙하 시간 최대 약 1.2초 × 초당 15.6방울 = 19개.</summary>
        private const int PendingSplashSlots = 32;

        /// <summary>중력 가속도(m/s²). 물방울 파티클의 gravityModifier가 1이므로 그대로 쓴다.</summary>
        private const float Gravity = 9.81f;

        // ── 물방울 소리 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 물방울 소리를 들려줄 최대 거리(m). 이보다 멀면 파티클만 뿌리고 소리는 내지 않는다.
        /// 40m 밖까지 전부 소리를 내면 초당 15번 "톡"이 울려 빗소리가 아니라 자판 소리가 된다.
        /// </summary>
        private const float DripSoundDistance = 8f;

        /// <summary>물방울 소리 사이의 최소 간격(초). 위와 함께 소리를 초당 2번 이하로 묶는 두 번째 안전장치.</summary>
        private const float DripSoundMinInterval = 0.45f;

        /// <summary>
        /// 이 세기보다 비가 강하면 물방울 소리를 내지 않는다(실외에 한해). 폭우 한가운데의 "톡"은
        /// 어차피 빗소리에 묻히고, 묻히지 않을 만큼 키우면 거슬린다. 비가 잦아든 뒤에야 들린다.
        /// </summary>
        private const float DripSoundIntensityGate = 0.35f;

        /// <summary>물방울 소리의 기준 볼륨(sfxVolume에 곱해진다). 아주 작게 - 있는 줄 모르게 있어야 한다.</summary>
        private const float DripSoundVolume = 0.10f;

        // ── 정적 클립 캐시 ───────────────────────────────────────────────────────

        /// <summary>
        /// 물방울 효과음 3종(음높이만 다르다 - 같은 소리가 반복되면 즉시 기계로 들린다).
        /// RainAudio의 루프 클립과 같은 규약: HideAndDontSave로 씬 언로드를 견디고, **R1 리셋 훅에서
        /// 비우지 않는다**(비우면 살아 있는 클립을 놓쳐 순수 누수가 된다 - RainAudio 필드 주석 참고).
        /// 3장 합쳐 0.6초 × 44100Hz ≈ 26,000샘플 ≈ 105KB로 무시할 만한 크기다.
        /// </summary>
        private static AudioClip[] dripClips;

        /// <summary>씬에 살아 있는 드라이버(없으면 null).</summary>
        public static RainDrips Active { get; private set; }

        // ── 인스턴스 상태 ────────────────────────────────────────────────────────

        /// <summary>낙수 지점 하나. 구조체 배열로 사전 할당해 프레임당 힙 할당을 0으로 만든다.</summary>
        private struct Anchor
        {
            public Vector3 position;   // 방울이 맺혀 떨어지는 월드 좌표(처마 끝 / 잎끝)
            public float groundY;      // 그 바로 아래에서 찾은 착지 높이(못 찾았으면 float.NaN)
            public float nextDripTime; // 다음 방울을 뿌릴 시각(unscaledTime)
        }

        /// <summary>착지 파문 예약 하나(방울을 뿌린 순간 낙하 시간을 계산해 넣어 둔다).</summary>
        private struct PendingSplash
        {
            public Vector3 position;
            public float dueTime;
            public bool active;
        }

        private readonly Anchor[] anchors = new Anchor[MaxAnchors];
        private int anchorCount;

        private readonly PendingSplash[] pendingSplashes = new PendingSplash[PendingSplashSlots];
        private int splashCursor;

        private readonly Collider[] scanBuffer = new Collider[ScanBufferSize];

        /// <summary>
        /// 콜라이더 → 낙수 대상 종류 캐시. Transform.name은 호출마다 문자열을 새로 만드는
        /// 네이티브 마샬링이라, 자리에서 안 움직이는 야자·건축 조각은 처음 한 번만 읽는다
        /// (RainAudio.materialKindCache와 같은 이유·같은 상한 규칙).
        /// </summary>
        private readonly Dictionary<Transform, byte> targetKindCache = new Dictionary<Transform, byte>(128);

        private const int TargetKindCacheLimit = 256;

        private const byte KindNone = 0;
        private const byte KindPalm = 1;
        private const byte KindRoof = 2;

        private ParticleSystem dropFx;
        private ParticleSystem splashFx;
        private AudioSource dripSource;

        private WeatherSystem weather;
        private Transform listener;

        private float dripLevel;
        private float rescanTimer;
        private float lastDripSoundTime = -99f;
        private int dripClipCursor;

        /// <summary>
        /// 이 클래스 전용 난수. **월드 생성 스트림(SeededRandomExtensions)과 완전히 분리돼 있다** -
        /// 여기서 몇 개를 뽑든 섬 배치·자원 배치는 한 톨도 달라지지 않는다(AGENT_BRIEF 2장 6번).
        /// 고정 시드라 같은 세션에서 늘 같은 순서로 나오지만, 낙수는 세이브에 남지 않으므로
        /// 재현성이 요구되는 값이 아니다.
        /// </summary>
        private readonly System.Random rng = new System.Random(778899);

        /// <summary>현재 낙수 활성도 0~1(1 = 폭우 중, 0 = 완전히 말랐다). 디버그/다른 연출용 읽기값.</summary>
        public static float Activity01 => Active != null ? Active.dripLevel : 0f;

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 드라이버를 하나 만든다(중복 가드 포함). AfterSceneLoad가 재시작에
        /// 다시 불리지 않는 문제는 UnderwaterAmbience.Bootstrap 주석에 라이브 테스트로 기록돼 있다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            Active = null; // R1 리셋 훅(정적 클립 캐시는 일부러 건드리지 않는다 - 필드 주석 참고)

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<RainDrips>() != null)
                    return;

                var go = new GameObject("RainDrips");
                go.AddComponent<RainDrips>();
            };
        }

        private void Awake()
        {
            Active = this;
            EnsureClips();

            dripSource = gameObject.AddComponent<AudioSource>();
            dripSource.playOnAwake = false;
            dripSource.loop = false;
            dripSource.spatialBlend = 0f; // 2D. 거리 감쇠는 아래에서 볼륨으로 직접 계산한다
            dripSource.dopplerLevel = 0f;
        }

        private void Start()
        {
            weather = WeatherSystem.Active;
            var cam = Camera.main;
            listener = cam != null ? cam.transform : null;
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            targetKindCache.Clear();
        }

        /// <summary>물방울 효과음 3종을 세션당 1회만 굽는다(필드 주석 참고).</summary>
        private static void EnsureClips()
        {
            if (dripClips != null && dripClips.Length == 3 && dripClips[0] != null)
                return;

            dripClips = new[]
            {
                ProceduralAudioClipGenerator.CreateWaterDrip(520f, 2.1f, 0.20f),
                ProceduralAudioClipGenerator.CreateWaterDrip(700f, 1.9f, 0.18f),
                ProceduralAudioClipGenerator.CreateWaterDrip(900f, 1.7f, 0.16f),
            };

            for (int i = 0; i < dripClips.Length; i++)
                dripClips[i].hideFlags = HideFlags.HideAndDontSave;
        }

        // ── 구동 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 활성도를 적분하고, 저빈도로 대상을 갱신하고, 시간이 된 지점에 방울을 하나씩 뿌린다.
        /// 활성도가 0이면 예약된 파문만 마저 처리하고 즉시 빠져나간다(마른 날의 비용은 조건문 몇 개).
        /// timeScale이 0이어도 얼어붙지 않게 unscaled 시간을 쓴다(파티클도 useUnscaledTime).
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            if (weather == null)
                weather = WeatherSystem.Active;
            if (listener == null && Time.frameCount % 60 == 0)
            {
                var cam = Camera.main;
                listener = cam != null ? cam.transform : null;
            }

            float target = weather != null ? Mathf.Clamp01(weather.RainIntensity01) : 0f;
            dripLevel = RainWetness.StepWetness(dripLevel, target, dt,
                DripRiseTau, DripDryTau, DripFloorSeconds);

            // 이미 떨어진 방울의 착지 파문은 활성도와 무관하게 끝까지 처리해야 한다(공중에서 사라지면 안 된다).
            FlushPendingSplashes(now);

            if (dripLevel <= 0.001f || listener == null)
            {
                anchorCount = 0;   // 다 마르면 대상을 놓아준다
                rescanTimer = 0f;  // 다음 비가 시작되는 그 프레임에 곧바로 다시 훑게 한다
                return;
            }

            rescanTimer -= dt;
            if (rescanTimer <= 0f)
            {
                rescanTimer = RescanInterval;
                RescanAnchors(now);
            }

            EmitDrips(now);
        }

        /// <summary>
        /// 낙수 지점을 다시 훑는다(2초 주기). 물리 질의는 **OverlapSphere 1회**이고, 새로 잡힌
        /// 지점마다 착지 높이를 재는 아래 방향 레이가 최대 24회 더 붙는다(같은 프레임에 한 번뿐이며
        /// 2초에 한 번이므로 평균 초당 12회 미만이다).
        ///
        /// 대상 3종:
        ///  · 야자수("Veg_Palm") — 줄기 차단 캡슐의 height가 나무 높이의 60%라는 규약
        ///    (IslandMeshGenerator.Vegetation의 trunkBlocker)을 역산해 실제 높이를 얻고,
        ///    그 86% 높이의 잎끝(수평 1.6m)에 지점을 잡는다. 방향은 위치 해시라 나무마다 다르고
        ///    프레임마다 흔들리지 않는다.
        ///  · 건축 지붕("BuildPiece_Roof") — 콜라이더 bounds의 아랫면 모서리 중점이 곧 처마다.
        ///    바닥/벽은 제외한다: Roof는 정의상 머리 위에 있지만(로컬 원점이 처마 밑면),
        ///    1층 바닥은 지면에 붙어 있어 "떨어질 높이"가 없어 물방울이 제자리에서 사라진다.
        ///  · 쉼터(Shelter.ActiveShelters) — 정적 목록이라 물리 질의가 아예 필요 없다.
        ///    지붕 높이(roofHeight)와 그늘 반경(shadeRadius)이 곧 처마 위치다. 지점 2개씩.
        /// 상한 24개에 도달하면 즉시 멈춘다(가까운 것부터 잡히도록 쉼터 → 물리 순서다).
        /// </summary>
        private void RescanAnchors(float now)
        {
            Vector3 origin = listener.position;
            anchorCount = 0;

            // ── 쉼터: 정적 목록(물리 질의 0) ─────────────────────────────────────
            var shelters = Shelter.ActiveShelters;
            if (shelters != null)
            {
                for (int i = 0; i < shelters.Count && anchorCount < MaxAnchors; i++)
                {
                    Shelter shelter = shelters[i];
                    if (shelter == null)
                        continue;

                    Vector3 basePos = shelter.transform.position;
                    if ((basePos - origin).sqrMagnitude > ScanRadius * ScanRadius)
                        continue;

                    float edge = Mathf.Max(0.6f, shelter.shadeRadius * 0.85f);
                    float roofY = basePos.y + Mathf.Max(1f, shelter.roofHeight);
                    AddAnchor(new Vector3(basePos.x + edge, roofY, basePos.z), now);
                    if (anchorCount < MaxAnchors)
                        AddAnchor(new Vector3(basePos.x - edge, roofY, basePos.z + edge * 0.5f), now);
                }
            }

            // ── 야자수 / 건축 지붕: 물리 질의 1회 ────────────────────────────────
            int hits = Physics.OverlapSphereNonAlloc(origin, ScanRadius, scanBuffer,
                DripLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits && anchorCount < MaxAnchors; i++)
            {
                Collider col = scanBuffer[i];
                if (col == null)
                    continue;

                byte kind = ClassifyTarget(col.transform);
                if (kind == KindNone)
                    continue;

                if (kind == KindPalm)
                {
                    var capsule = col as CapsuleCollider;
                    if (capsule == null)
                        continue;

                    // 줄기 캡슐 height = 나무 높이 × 0.6 (Vegetation의 trunkBlocker 규약을 역산).
                    float palmHeight = capsule.height / 0.6f;
                    if (palmHeight < 2f)
                        continue;

                    Vector3 root = col.transform.position;
                    float angle = HashAngle(root);
                    Vector3 tip = root
                        + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.6f
                        + Vector3.up * (palmHeight * 0.86f);
                    AddAnchor(tip, now);
                }
                else // KindRoof
                {
                    Bounds b = col.bounds;
                    // 아랫면(= 처마 밑) 네 모서리 중점 중 하나를 위치 해시로 고른다. 이웃한 지붕
                    // 조각들이 전부 같은 변에서 떨어지면 일직선으로 늘어서 인공적으로 보인다.
                    float angle = HashAngle(b.center);
                    Vector3 eave = new Vector3(
                        b.center.x + Mathf.Cos(angle) * b.extents.x * 0.9f,
                        b.min.y,
                        b.center.z + Mathf.Sin(angle) * b.extents.z * 0.9f);
                    AddAnchor(eave, now);
                }
            }
        }

        /// <summary>
        /// 지점을 표에 넣으면서 착지 높이를 한 번 잰다. 아래로 30m까지 훑어 자기 자신보다 아래에
        /// 있는 첫 표면을 찾고, 못 찾으면 파문 없이 방울만 떨어뜨린다(바다 위 처마 등).
        /// 첫 방울 시각은 즉시가 아니라 무작위로 흩뿌려, 재탐색 직후 모든 지점이 한꺼번에
        /// 떨어지는 "우수수"를 막는다.
        /// </summary>
        private void AddAnchor(Vector3 position, float now)
        {
            if (anchorCount >= MaxAnchors)
                return;

            float groundY = float.NaN;
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.down * 0.05f, Vector3.down, out hit, 30f,
                    DripLayerMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
            }

            anchors[anchorCount] = new Anchor
            {
                position = position,
                groundY = groundY,
                nextDripTime = now + (float)rng.NextDouble() * MaxDripInterval,
            };
            anchorCount++;
        }

        /// <summary>
        /// 시간이 된 지점에 방울을 하나씩 뿌린다. 매 프레임 하는 일은 앵커 ≤24개의 float 비교이고,
        /// 실제 Emit은 초당 십수 회뿐이다(EmitParams는 구조체라 호출당 힙 할당 0).
        /// </summary>
        private void EmitDrips(float now)
        {
            if (dropFx == null && !EnsureParticles())
                return;

            Vector3 camPos = listener.position;
            float interval = Mathf.Lerp(MaxDripInterval, MinDripInterval, dripLevel);

            for (int i = 0; i < anchorCount; i++)
            {
                if (now < anchors[i].nextDripTime)
                    continue;

                float jitter = Mathf.Lerp(IntervalJitterMin, IntervalJitterMax, (float)rng.NextDouble());
                anchors[i].nextDripTime = now + interval * jitter;

                Vector3 pos = anchors[i].position;
                float sqrDistance = (pos - camPos).sqrMagnitude;
                if (sqrDistance > ScanRadius * ScanRadius)
                    continue; // 걸어서 멀어진 지점: 다음 재탐색에서 표에서 빠진다

                // 맺혔다 떨어지는 물방울이라 초기 속도는 거의 0이다(중력이 전부 만든다).
                var emit = new ParticleSystem.EmitParams
                {
                    position = pos,
                    velocity = new Vector3(0f, -0.15f, 0f),
                };
                dropFx.Emit(emit, 1);

                SchedulePlash(anchors[i], now);
                TryPlayDripSound(sqrDistance);
            }
        }

        /// <summary>
        /// 방울이 착지할 시각과 위치를 링 버퍼에 예약한다. 자유 낙하이므로 t = √(2h/g)이고,
        /// 파티클의 gravityModifier가 1이라 실제 낙하와 정확히 같은 시각에 파문이 뜬다.
        /// 착지 높이를 못 찾았거나 낙차가 5cm 미만이면 파문을 만들지 않는다(제자리에서 튀면 어색하다).
        /// </summary>
        private void SchedulePlash(Anchor anchor, float now)
        {
            if (float.IsNaN(anchor.groundY))
                return;

            float fall = anchor.position.y - anchor.groundY;
            if (fall < 0.05f)
                return;

            float fallTime = Mathf.Sqrt(2f * fall / Gravity);

            pendingSplashes[splashCursor] = new PendingSplash
            {
                position = new Vector3(anchor.position.x, anchor.groundY + 0.02f, anchor.position.z),
                dueTime = now + fallTime,
                active = true,
            };
            splashCursor = (splashCursor + 1) % PendingSplashSlots;
        }

        /// <summary>예약 시각이 지난 파문을 띄운다(슬롯 32개 순회 - 프레임당 비용이 상수다).</summary>
        private void FlushPendingSplashes(float now)
        {
            if (splashFx == null)
                return;

            for (int i = 0; i < PendingSplashSlots; i++)
            {
                if (!pendingSplashes[i].active || now < pendingSplashes[i].dueTime)
                    continue;

                var emit = new ParticleSystem.EmitParams
                {
                    position = pendingSplashes[i].position,
                    velocity = Vector3.zero,
                };
                splashFx.Emit(emit, 1);
                pendingSplashes[i].active = false;
            }
        }

        /// <summary>
        /// 물방울 소리를 낼지 판단해 아주 작게 재생한다.
        ///
        /// **판단: 넣는다. 단 세 겹의 문을 통과한 것만.** 근거 - 낙수는 초당 15방울 규모라 전부
        /// 소리를 내면 "톡톡톡톡" 자판 소리가 되어 즉시 거슬린다. 반대로 소리를 아예 빼면,
        /// 비가 그친 뒤 처마 밑에서 물방울이 떨어지는 그 조용한 순간이 화면에만 있고 귀에는 없다 -
        /// 이 연출의 값어치가 절반이 된다. 그래서 "들으면 좋은 그 한 방울"만 남기는 문을 세운다.
        ///  1) 거리 8m 이내 — 내가 바라보고 있는 그 처마의 방울만.
        ///  2) 최소 간격 0.45초 — 초당 2번을 넘지 않는다.
        ///  3) 세기 게이트 — 폭우 중(세기 0.35 초과)에는 실외에서 소리를 내지 않는다. 어차피
        ///     빗소리에 묻히고, 묻히지 않을 만큼 키우면 거슬린다. 단 **지붕 아래에 있으면 예외**다 -
        ///     빗소리가 로우패스로 먹먹해진 실내에서는 처마 물방울이 오히려 또렷하게 들려야 하고,
        ///     그 대비가 "비를 피했다"는 감각을 완성한다(RainAudio 층 3과 같은 목적).
        /// 볼륨은 sfxVolume × 0.10 × 거리 감쇠로, 다른 어떤 효과음보다도 작다.
        /// </summary>
        private void TryPlayDripSound(float sqrDistance)
        {
            if (dripSource == null || dripClips == null)
                return;

            if (sqrDistance > DripSoundDistance * DripSoundDistance)
                return;

            float now = Time.unscaledTime;
            if (now - lastDripSoundTime < DripSoundMinInterval)
                return;

            bool indoors = weather != null && weather.IsIndoors;
            float intensity = weather != null ? weather.RainIntensity01 : 0f;
            if (!indoors && intensity > DripSoundIntensityGate)
                return;

            var audio = AudioManager.Instance;
            float sfx = audio != null ? audio.sfxVolume : 0.7f;
            float distance01 = Mathf.Sqrt(sqrDistance) / DripSoundDistance;
            float volume = sfx * DripSoundVolume * Mathf.Lerp(1f, 0.35f, distance01);
            if (volume <= 0.0005f)
                return;

            lastDripSoundTime = now;
            dripSource.PlayOneShot(dripClips[dripClipCursor], volume);
            dripClipCursor = (dripClipCursor + 1) % dripClips.Length;
        }

        /// <summary>
        /// 파티클 시스템 2개를 처음 필요할 때 한 번 만든다(EffectBuilder 규약 - 생성 순서의
        /// main.duration 함정은 그쪽 CreateSystem이 이미 처리한다). 실패하면 다시 시도하지 않고
        /// 조용히 파티클 없이 동작한다(소리는 그대로 난다 - 우아한 열화).
        /// </summary>
        private bool EnsureParticles()
        {
            if (particleBuildFailed)
                return false;

            dropFx = EffectBuilder.CreateRainDripDrops(transform);
            splashFx = EffectBuilder.CreateRainDripSplashes(transform);

            if (dropFx == null || splashFx == null)
            {
                particleBuildFailed = true;
                return false;
            }

            dropFx.Play();
            splashFx.Play();
            return true;
        }

        private bool particleBuildFailed;

        /// <summary>
        /// 콜라이더 소유 Transform이 낙수 대상인지 판정한다(결과는 캐시 - 필드 주석 참고).
        /// 이름 규약: 야자 = "Veg_Palm"(IslandMeshGenerator.Vegetation),
        /// 건축 지붕 = "BuildPiece_Roof"(BuildPieceVisualBuilder.CreateSolid의 $"BuildPiece_{type}").
        /// </summary>
        private byte ClassifyTarget(Transform t)
        {
            if (t == null)
                return KindNone;

            byte cached;
            if (targetKindCache.TryGetValue(t, out cached))
                return cached;

            if (targetKindCache.Count >= TargetKindCacheLimit)
                targetKindCache.Clear();

            string name = t.name;
            byte kind = KindNone;
            if (name == "Veg_Palm")
                kind = KindPalm;
            else if (name == "BuildPiece_Roof")
                kind = KindRoof;

            targetKindCache[t] = kind;
            return kind;
        }

        /// <summary>
        /// 월드 좌표를 0~2π 각도로 바꾸는 결정적 해시. UnityEngine.Random도 System.Random도 쓰지
        /// 않으므로 같은 나무/지붕은 재탐색 때마다 **같은 방향**의 지점을 내놓는다(2초마다 물방울이
        /// 나무 반대편으로 순간이동하지 않게 하는 것이 목적이다).
        /// </summary>
        private static float HashAngle(Vector3 worldPosition)
        {
            int hx = Mathf.RoundToInt(worldPosition.x * 7f);
            int hz = Mathf.RoundToInt(worldPosition.z * 7f);
            int h = hx * 73856093 ^ hz * 19349663;
            return (h & 0xFFFF) / 65535f * (Mathf.PI * 2f);
        }
    }
}
