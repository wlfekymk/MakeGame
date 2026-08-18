using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 카메라가 해수면(WorldMapManager.seaLevel) 아래로 내려가면 화면을 "물속"으로 보이게 하는
    /// 수중 분위기 전환. 바다가 반투명이 되면서 잠수가 시각적으로 의미 있어졌는데, 물속에 들어가도
    /// 안개/환경광이 물 밖과 똑같아 "수면 아래로 카메라만 통과한" 느낌이었다 - 그 반쪽을 채운다.
    ///
    /// ── 안개/환경광 소유권과의 충돌 회피 설계 ─────────────────────────────────────
    /// 이 프로젝트에서 RenderSettings 안개의 단독 소유자는 DayNightCycle.Update다(맑은 날 기준값
    /// ClearFogColor/ClearFogDensity를 계산하고, WeatherSystem의 비 안개까지 RainIntensity01로
    /// 보간해 매 프레임 기록한다 - DayNightCycle.UpdateAtmosphericFog 주석 참고). 환경광(Trilight
    /// 3색)도 마찬가지로 DayNightCycle.UpdateAmbientLight가 매 프레임 기록한다. 두 스크립트가 같은
    /// 전역 상태를 번갈아 덮어쓰는 형태는 이 프로젝트가 반복해서 사고를 낸 패턴이므로(WeatherSystem
    /// 클래스 주석 참고), 여기서는 그 계약을 전혀 건드리지 않는다:
    ///  · 이 컴포넌트는 **LateUpdate**에서만, **수중일 때만** RenderSettings를 덮어쓴다.
    ///    같은 프레임 안에서 DayNightCycle.Update(안개/환경광 기록) → 본 LateUpdate(수중이면
    ///    덮어쓰기) 순서가 보장되므로, 렌더링 직전의 "마지막 승자"가 항상 이 컴포넌트다.
    ///    DayNightCycle 쪽 코드는 한 줄도 바뀌지 않고, ClearFogColor/ClearFogDensity 계약도
    ///    그대로 살아 있다(수면 위로 나오는 즉시 그 값이 다시 화면을 지배한다).
    ///  · 물 밖에서는 **아무것도 하지 않는다**. 복원 코드가 아예 없으므로 "되돌리기 전에
    ///    씬 저장/재로드가 끼면 값이 굳는" 류의 사고가 원리적으로 불가능하다 - DayNightCycle이
    ///    어차피 다음 프레임 Update에서 안개/환경광을 처음부터 다시 기록한다.
    ///  · 타이틀 화면(Time.timeScale=0)에서도 LateUpdate는 돌지만, 카메라가 물 위에 있는 한
    ///    위 원칙대로 즉시 return하므로 무해하다.
    ///
    /// 씬에 미리 배치할 필요 없이 DayNightCycle/AtmospherePostFX와 같은
    /// RuntimeInitializeOnLoadMethod 패턴으로 씬 로드 시 자동 생성된다(씬 수정 없음).
    /// 성능: 프레임당 몇 줄의 산술 연산뿐이고 정상 경로 할당은 0이다(참조 탐색은 캐시 후
    /// null일 때만 저빈도로 재시도).
    /// </summary>
    public class UnderwaterAmbience : MonoBehaviour
    {
        [Header("수중 안개")]
        // [설계] 주간 만수면 기준의 깊은 청록. 열대 바다의 물속은 붉은 파장이 수 미터 안에 흡수되어
        // 청록만 남는다 - 채도를 과하게 올리지 않아도 이 색 하나로 "물속"이 즉시 읽힌다.
        [Tooltip("주간 · 수심 0m 기준의 수중 안개 색(맑은 청록). 실제 색은 현재 조명 강도와 수심에 비례해 어두워지고, 깊어질수록 deepFogColor 쪽으로 간다.")]
        public Color underwaterFogColor = new Color(0.10f, 0.34f, 0.40f);

        // [탁도 v2] 얕은 곳과 깊은 곳의 **색까지** 갈랐다. 예전에는 색이 하나뿐이라 깊이 감쇠가
        // "같은 색을 어둡게" 하는 것밖에 못 했는데, 실제 바다는 깊어질수록 청록의 녹색 성분이
        // 먼저 죽고 짙은 남청만 남는다. 색이 함께 변해야 "더 깊이 들어왔다"가 밝기와 무관하게 읽힌다.
        [Tooltip("최대 수심에서 수렴하는 수중 안개 색(짙은 남청). 얕은 곳의 underwaterFogColor에서 이 색으로 보간된다.")]
        public Color deepFogColor = new Color(0.02f, 0.09f, 0.15f);

        // [탁도 v2] 0.055 → 0.034. ExponentialSquared 0.034 기준 잔여 시야:
        //   10m 89% · 20m 63% · 30m 35% · 45m 9.6%  →  "얕은 열대 바다는 30m 앞까지 맑게 보인다".
        // 예전 0.055는 수심 0m에서도 15~20m에서 시야가 막혀, 카우스틱/갓레이 같은 새 연출이
        // 보이기도 전에 물빛에 잠겼다. 대신 깊이 배율을 1.6 → 2.8로 크게 올려(아래) 깊은 곳의
        // 답답함은 오히려 강해진다 - "얕으면 맑고 깊으면 짙다"의 대비를 밀도로 만든 것이다.
        // DayNightCycle의 clearFogDensity(0.0016)와는 자릿수가 다른 별개 값이라 헷갈릴 일이 없다.
        [Tooltip("수심 0m 기준의 수중 안개 밀도(ExponentialSquared). 얕은 곳 시야 약 30m.")]
        public float underwaterFogDensity = 0.034f;

        [Header("수중 환경광 (Trilight 3색)")]
        // DayNightCycle이 Trilight로 몰고 있으므로 같은 모드의 3색을 전부 덮는다(하나라도 남기면
        // 물 밖의 하늘/노을 반사광이 물속 오브젝트에 그대로 얹혀 어색하다). 물속의 빛은 전부
        // 수면에서 내려오므로 하늘>수평선>지면 순으로 급격히 어두워지는 것이 실제 모습이다.
        // [탁도 v2] 얕은 물의 밝기를 조금 올렸다(0.16 → 0.19 등). 얕은 산호밭이 "맑은 청록"으로
        // 읽히려면 안개 밀도만 낮춰서는 부족하고, 물체에 닿는 환경광 자체가 밝아야 한다.
        [Tooltip("수중 환경광 '하늘' 색(수면 쪽에서 내려오는 빛). 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientSky = new Color(0.19f, 0.44f, 0.52f);

        [Tooltip("수중 환경광 '수평선' 색. 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientEquator = new Color(0.08f, 0.22f, 0.29f);

        [Tooltip("수중 환경광 '지면' 색(아래는 빛이 거의 닿지 않는다). 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientGround = new Color(0.02f, 0.07f, 0.10f);

        [Header("깊이 감쇠")]
        // [탁도 v2] 20 → 28m. 스커트 외곽 수심이 -18m이고 그 너머 골이 더 깊으므로, 20m에서
        // 이미 최대 감쇠에 닿으면 "해저에 도착한 순간부터 계속 최악의 시야"가 된다.
        // 28m로 늘리면 산호밭(수심 3~15m)이 감쇠 곡선의 앞쪽 절반에 들어와 밝게 남는다.
        [Tooltip("깊이 감쇠가 최대치에 도달하는 수심(m).")]
        public float maxAttenuationDepth = 28f;

        // [탁도 v2] 1.6 → 2.8. 최대 수심에서 밀도 0.0952 → 잔여 시야 10m 40% · 15m 13% · 20m 2.7%.
        // 얕은 곳 30m / 깊은 곳 12m로 시야거리 자체가 깊이에 따라 크게 줄어든다.
        [Tooltip("최대 수심에서 안개 밀도에 곱할 배율(깊을수록 시야가 좁아진다).")]
        public float depthDensityMultiplier = 2.8f;

        // [탁도 v2] 0.4 → 0.45. 밀도 쪽을 크게 올렸으므로 밝기까지 세게 낮추면 "깊은 곳은
        // 그냥 검은 화면"이 된다(밤 잠수가 이미 어둡다는 피드백). 어두워지는 연출은 색 보간
        // (deepFogColor)과 밀도가 담당하고, 밝기는 물체 식별이 가능한 선에서 멈춘다.
        [Tooltip("최대 수심에서 밝기(안개색/환경광)에 곱할 배율(깊을수록 어두워진다).")]
        public float depthBrightnessMultiplier = 0.45f;

        /// <summary>
        /// 이번 프레임 메인 카메라가 해수면 아래에 있는지. 다음 웨이브의 해저 연출/수중 사운드
        /// 작업이 읽는 공개 계약이다. 카메라나 WorldMapManager를 못 찾으면 항상 false.
        /// </summary>
        public static bool IsUnderwater { get; private set; }

        private Camera targetCamera;
        private WorldMapManager worldMap;
        private DayNightCycle dayNight;
        private Light sunLight;

        /// <summary>
        /// 잠수 기포 인스턴스(카메라 자식). 수중 최초 진입 때 EffectBuilder로 1회 생성한 뒤
        /// 계속 재사용한다 — 수중/물 밖 전환마다 파괴/재생성하면 그때마다 GameObject+머티리얼
        /// 바인딩 비용이 들고 오브젝트 누수 사고의 온상이 되므로 Play/Stop만 오간다.
        /// 카메라 파괴·씬 전환 시 자식이라 함께 파괴되는데, 그때는 아래 null 프로브가 잡아
        /// 다음 수중 진입에서 새로 만든다(파괴된 UnityEngine.Object == null 규칙).
        /// </summary>
        private ParticleSystem diveBubbles;

        /// <summary>
        /// 직전 프레임의 수중 여부(사운드 전환 감지용). AudioManager 호출은 상태가 바뀐 그 프레임에만
        /// 한 번 이뤄져야 한다 - 매 프레임 Start/Stop을 부르면 페이드가 계속 리셋되고 낭비다.
        /// </summary>
        private bool wasUnderwaterForAudio;

        /// <summary>
        /// DayNightCycle.Bootstrap과 같은 패턴: AfterSceneLoad는 "플레이 시작 후 첫 씬 로드"에만
        /// 호출되고 재시작(SceneManager.LoadScene)에는 다시 호출되지 않아, 재시작한 게임에서
        /// 수중 연출이 조용히 사라진다(DayNightCycle.Bootstrap 주석에서 라이브 테스트로 확인된
        /// 사실). SubsystemRegistration에서 sceneLoaded를 한 번만 구독해 씬이 로드될 때마다 새로
        /// 만들고, 이미 살아 있는 인스턴스가 있으면(중복 생성 가드, AtmospherePostFX와 동일) 건너뛴다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<UnderwaterAmbience>() != null)
                    return;

                var go = new GameObject("UnderwaterAmbience");
                go.AddComponent<UnderwaterAmbience>();
            };
        }

        /// <summary>
        /// 참조를 한 번 캐시한다. WorldMapManager/DayNightCycle은 같은 sceneLoaded 콜백에서
        /// 생성/초기화되는 순서에 따라 Start 시점에 아직 없을 수 있으므로, 못 찾은 것은
        /// LateUpdate에서 저빈도로 재시도한다(DayNightCycle이 WeatherSystem을 찾는 것과 같은 규칙).
        /// </summary>
        private void Start()
        {
            targetCamera = Camera.main;
            worldMap = FindAnyObjectByType<WorldMapManager>();
            dayNight = FindAnyObjectByType<DayNightCycle>();
            sunLight = DayNightCycle.FindDirectionalLight();
        }

        /// <summary>
        /// 씬 재로드로 파괴될 때 정적 상태를 정리한다. 다음 씬에서 카메라가 물 위에서 시작하는데
        /// IsUnderwater가 true로 굳어 있으면 다음 웨이브의 수중 사운드가 한 프레임 잘못 판정한다.
        /// </summary>
        private void OnDestroy()
        {
            IsUnderwater = false;

            // 수중에서 씬이 재로드되면 이 인스턴스는 파괴되지만 AudioManager는 DontDestroyOnLoad로
            // 살아남아 수중 앰비언스가 물 밖(새 씬)에서 계속 울릴 수 있다 - 여기서 페이드 아웃을 걸어둔다.
            if (wasUnderwaterForAudio)
            {
                wasUnderwaterForAudio = false;
                var audio = AudioManager.Instance;
                if (audio != null)
                    audio.StopUnderwaterAmbient();
            }
        }

        // 씬의 Directional Light 탐색은 DayNightCycle.FindDirectionalLight(정본)를 그대로 쓴다.

        /// <summary>
        /// 매 프레임 수중 판정 후, 수중일 때만 안개/환경광을 덮어쓴다. LateUpdate인 이유는
        /// 클래스 주석의 소유권 설계 참고 - DayNightCycle.Update가 기록을 끝낸 뒤의 "마지막 승자"
        /// 자리를 차지하기 위해서다. 물 밖에서는 즉시 return하므로 타이틀 화면(timeScale=0)에서도 무해하다.
        /// </summary>
        private void LateUpdate()
        {
            // 카메라는 파괴/재생성될 수 있으므로(파괴된 오브젝트 == null 규칙) null이면 다시 집는다.
            if (targetCamera == null)
                targetCamera = Camera.main;

            // WorldMapManager는 씬 로드 직후 생성 순서에 따라 늦게 나타날 수 있다. 못 찾았을 때만
            // 가끔 재시도한다(DayNightCycle이 WeatherSystem을 60프레임마다 재탐색하는 것과 같은 규칙 -
            // 정상 경로에서는 탐색 비용/할당이 0이다).
            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (targetCamera == null || worldMap == null)
            {
                IsUnderwater = false;
                SyncAmbienceAudio(); // 수중에서 카메라가 파괴된 경우에도 사운드는 페이드 아웃돼야 한다.
                return;
            }

            float depth = worldMap.seaLevel - targetCamera.transform.position.y;
            IsUnderwater = depth > 0f;

            // 기포는 안개와 달리 "물 밖에서 아무것도 안 하기"가 불가능하다(방출을 멈추는 것
            // 자체가 행동이다). 그래서 물 밖 조기 return보다 먼저 구동한다.
            UpdateDiveBubbles();

            // 수중 사운드도 같은 이유로 조기 return보다 먼저: 이탈 순간의 페이드 아웃 호출이 곧 행동이다.
            SyncAmbienceAudio();

            // 물 밖: 아무것도 하지 않는다. DayNightCycle이 이번 프레임 Update에서 이미 안개/환경광을
            // 기록했고 다음 프레임에도 계속 기록하므로, 복원은 자연히 그쪽 몫이다(클래스 주석 참고).
            if (!IsUnderwater)
                return;

            // 조명/시간대 참조도 같은 저빈도 재시도 규칙을 따른다(수중에서만 필요하므로 여기서 재시도).
            if (dayNight == null && Time.frameCount % 60 == 0)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (sunLight == null && Time.frameCount % 60 == 0)
                sunLight = DayNightCycle.FindDirectionalLight();

            // 현재 조명 강도(0~1). DayNightCycle.Update가 이번 프레임 sunLight.intensity에 기록한
            // 값을 그대로 읽어 dayIntensity로 정규화한다 - 밤/비까지 이미 반영된 최종값이라
            // 별도 시간 계산 없이 "밤 잠수는 거의 암흑, 비 오는 낮은 침침"이 공짜로 성립한다.
            float lightFactor = 1f;
            if (sunLight != null && dayNight != null && dayNight.dayIntensity > 0f)
                lightFactor = Mathf.Clamp01(sunLight.intensity / dayNight.dayIntensity);

            // 깊이 감쇠: 수심 0m → maxAttenuationDepth(28m)에서 밀도 2.8배 · 밝기 0.45배 ·
            // 색은 underwaterFogColor → deepFogColor.
            // [탁도 v2] 선형이 아니라 **smoothstep**을 쓴다. 선형이면 수면 바로 아래(수심 1~3m,
            // 물놀이/얕은 산호밭)에서부터 이미 탁해지기 시작해 "맑은 얕은 물"이 성립하지 않는다.
            // smoothstep은 양 끝이 평평해서 얕은 구간을 넓게 맑게 남기고, 중간 수심(10~20m)에서
            // 빠르게 짙어졌다가, 깊은 구간에서 다시 완만해진다 - 잠수하면서 "어느 순간 깊은
            // 물로 들어왔다"가 분명히 느껴지는 곡선이다.
            float depthT = Mathf.Clamp01(depth / Mathf.Max(1f, maxAttenuationDepth));
            float depth01 = depthT * depthT * (3f - 2f * depthT);
            float density = Mathf.Max(0f, underwaterFogDensity)
                * Mathf.Lerp(1f, depthDensityMultiplier, depth01);
            float brightness = lightFactor * Mathf.Lerp(1f, depthBrightnessMultiplier, depth01);

            // 수중 안개. ExponentialSquared는 근거리 감쇠가 거의 없어 손/장비는 선명하고 먼 곳만
            // 물빛에 잠긴다(DayNightCycle이 같은 모드를 쓰는 이유와 동일).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = ScaleRgb(
                Color.Lerp(underwaterFogColor, deepFogColor, depth01), brightness);
            RenderSettings.fogDensity = density;

            // 수중 환경광. DayNightCycle과 같은 Trilight 모드로 3색 전부 덮는다(필드 주석 참고).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ScaleRgb(underwaterAmbientSky, brightness);
            RenderSettings.ambientEquatorColor = ScaleRgb(underwaterAmbientEquator, brightness);
            RenderSettings.ambientGroundColor = ScaleRgb(underwaterAmbientGround, brightness);
        }

        /// <summary>
        /// 잠수 기포 구동. 수중 최초 진입 때 메인 카메라 앞 0.6m·아래 0.3m에 1회 생성해 붙이고,
        /// 이후에는 인스턴스를 재사용해 수중이면 Play, 물 밖이면 Stop만 한다. Stop은
        /// StopEmitting(Clear 아님)이라 수면 위로 나오는 순간에도 이미 뱉은 기포는 남은 수명
        /// 동안 자연스럽게 떠오르다 사라진다. isEmitting으로 판정하는 이유: Stop(StopEmitting)
        /// 후에도 잔여 입자가 살아 있는 동안 isPlaying은 true라, isPlaying으로 걸면 재진입 시
        /// Play가 씹히는 프레임이 생긴다.
        /// 호출 시점상 targetCamera는 null이 아님이 보장된다(LateUpdate 상단에서 걸러진다).
        /// </summary>
        private void UpdateDiveBubbles()
        {
            if (IsUnderwater)
            {
                if (diveBubbles == null)
                {
                    diveBubbles = EffectBuilder.CreateDiveBubbles(targetCamera.transform);
                    diveBubbles.transform.localPosition = new Vector3(0f, -0.3f, 0.6f);
                }

                if (!diveBubbles.isEmitting)
                    diveBubbles.Play();

                // 방출 콘이 항상 **월드 위**를 향하게 매 프레임 월드 회전을 고정한다.
                // CreateDiveBubbles(pointUpward)는 부모가 회전 없다는 전제로 X-90도를 주는데,
                // 부모가 카메라라 고개를 숙이면 새 기포가 수평/뒤로 발사됐다(야간 검증 지적).
                // 위치는 카메라를 따르고 회전만 고정 - 트랜스폼 대입 1회/프레임 수준이라 비용 없음.
                diveBubbles.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            else if (diveBubbles != null && diveBubbles.isEmitting)
            {
                diveBubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// 수중 앰비언스 사운드 구동. IsUnderwater가 직전 프레임과 달라진 그 프레임에만 AudioManager를
        /// 호출한다(진입 → 0.5초 페이드 인, 이탈 → 0.5초 페이드 아웃 - 페이드 자체는 AudioManager 몫).
        /// AudioManager가 씬에 없으면 조용히 아무것도 하지 않는다(null 안전). 타이틀 화면에서는
        /// 수중이 아니므로 상태 변화가 없어 자연히 무동작이고, 정상 경로 할당은 0이다.
        /// </summary>
        private void SyncAmbienceAudio()
        {
            if (IsUnderwater == wasUnderwaterForAudio)
                return;

            var audio = AudioManager.Instance;
            if (audio == null)
                return; // 다음 프레임에 다시 시도하도록 wasUnderwaterForAudio를 갱신하지 않는다.

            if (IsUnderwater)
                audio.StartUnderwaterAmbient();
            else
                audio.StopUnderwaterAmbient();

            wasUnderwaterForAudio = IsUnderwater;
        }

        /// <summary>RGB에만 배율을 곱한다. Color * float는 알파까지 곱하므로 알파는 1로 고정한다(DayNightCycle과 동일).</summary>
        private static Color ScaleRgb(Color c, float multiplier)
        {
            return new Color(c.r * multiplier, c.g * multiplier, c.b * multiplier, 1f);
        }
    }
}
