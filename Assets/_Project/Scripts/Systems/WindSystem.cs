using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// **게임에 바람은 하나뿐이다.** 잔디·해초·바다·비가 각자 바람을 흉내 내던 것을 여기로 모았다.
    ///
    /// 왜 합쳤나 — 조사에서 세 영역(잔디/바다/날씨)이 전부 같은 말을 했다: 시스템마다 바람이 따로
    /// 놀면 오히려 "따로 논다"는 것이 눈에 띈다. 폭풍이 몰아치는데 잔디는 잔잔하고, 해초는 북서로
    /// 눕는데 빗줄기는 남동으로 날리면, 각각을 아무리 잘 만들어도 장면 전체가 가짜로 읽힌다.
    /// 합치기 전 이 프로젝트의 상태가 정확히 그랬다(잔디 셰이더 상수 / OceanWaves 방향 배열 /
    /// WeatherSystem.rainWind / MGKelpSway 셰이더 상수 - 네 벌이 서로를 몰랐다).
    ///
    /// 이 클래스가 만드는 값은 세 개다.
    ///  · **방위(bearing)** - 아주 천천히 돈다. 급하게 돌리면 안 되는 이유가 있다: 바다 파도 방향이
    ///    이 값을 따라가고, 뗏목 부력이 그 파도를 딛고 서 있다. 방향이 홱 돌면 뗏목이 이유 없이
    ///    출렁인다. 그래서 기본값이 초당 0.35도(한 바퀴에 17분)다.
    ///  · **세기(strength)** - 앰비언트(날씨가 정하는 기본 세기) + **돌풍(gust)**.
    ///  · **위상(phase)** - 셰이더가 스크롤에 쓸 누적값.
    ///
    /// ★ 위상을 누적하는 이유(중요). 셰이더가 흔히 하듯 `_Time.y * speed`로 스크롤하면, 세기가
    ///   바뀌는 순간 곱셈 결과가 통째로 달라져 **패턴이 순간이동한다**(잔잔→돌풍 전환마다 잔디밭이
    ///   한 번씩 튄다). C#에서 `phase += strength * dt`로 적분해 넘기면 세기는 "지금부터의 속도"만
    ///   바꾸므로 이음매가 없다.
    ///
    /// ★ 돌풍 모델은 SpeedTree의 게임용 바람 모델을 따랐다: 일정한 앰비언트 위에 변동이 **얹히고**,
    ///   세기 변화가 즉각적이지 않고 response time을 두고 서서히 적용된다. 계단처럼 켜졌다 꺼지는
    ///   돌풍은 실제 바람으로 읽히지 않는다.
    ///
    /// 시간은 **스케일된 Time.deltaTime**을 쓴다. 프로젝트의 다른 곳(OceanWaves의 거칠기 보간 등)이
    /// unscaled를 쓰는 것과 반대인데, 이유가 다르다: 저쪽은 "화면이 멈춰도 진행돼야 하는 상태 보간"이고
    /// 이쪽은 "화면에 보이는 움직임"이다. 잔디·해초 셰이더가 원래 Time.time을 받아 timeScale 0에서
    /// 멈추도록 만들어져 있었고(타이틀 화면에서 해초가 멈춘다는 MGKelpSway 주석), 그 약속을 지킨다.
    /// </summary>
    public class WindSystem : MonoBehaviour
    {
        // ── 셰이더 전역 ─────────────────────────────────────────
        //
        // _MG_Wind = (방향x, 방향z, 세기, 누적위상)
        //
        // 소비자 셰이더는 이 값을 **CBUFFER(UnityPerMaterial) 밖에** 선언해야 한다. 그 안은
        // 머티리얼이 소유하는 값만 들어가는 자리이고, 안에 넣으면 SetGlobalVector가 먹지 않는다.
        private static readonly int WindProperty = Shader.PropertyToID("_MG_Wind");

        /// <summary>세기 1.0이 뜻하는 것: 산들바람. 잔디 끝 진폭 0.12m 기준(MGGrass의 MG_WIND_AMP).</summary>
        public const float ReferenceStrength = 1f;

        [Header("세기")]
        [Tooltip("잔잔할 때의 앰비언트 세기")]
        public float calmStrength = 0.70f;

        [Tooltip("폭풍일 때의 앰비언트 세기")]
        public float stormStrength = 2.10f;

        [Tooltip("돌풍이 앰비언트 위에 얹는 최대 가산 비율(0.5 = 최대 1.5배)")]
        public float gustAmount = 0.55f;

        [Tooltip("돌풍 세기가 목표값을 따라잡는 데 걸리는 시간(초). SpeedTree의 response time에 해당한다.")]
        public float gustResponseSeconds = 2.2f;

        [Header("방향")]
        [Tooltip("시작 방위(도). 0 = +X, 90 = +Z")]
        public float bearingDegrees = 30f;

        [Tooltip("방위가 도는 속도(초당 도). 파도 방향이 이 값을 따라가므로 크게 잡으면 뗏목이 흔들린다.")]
        public float bearingDriftDegreesPerSecond = 0.35f;

        [Tooltip("폭풍일 때 방위가 도는 속도 배율")]
        public float stormBearingDriftScale = 2.5f;

        /// <summary>지금 살아 있는 인스턴스. 씬마다 하나다.</summary>
        public static WindSystem Active { get; private set; }

        // ── 지금 값(정적 - 셰이더를 못 쓰는 C# 쪽 소비자용) ─────
        private static Vector2 direction = new Vector2(0.866f, 0.5f);
        private static float strength = 1f;
        private static float gust01;
        private static float phase;
        private static float bearing = 30f;

        /// <summary>지금 바람 방향(정규화된 월드 XZ). 시스템이 없어도 기본값이 나온다.</summary>
        public static Vector2 Direction => direction;

        /// <summary>지금 바람 세기(1 = 산들바람). 앰비언트 + 돌풍이 이미 합쳐진 값이다.</summary>
        public static float Strength => strength;

        /// <summary>돌풍 성분만(0~1). "지금 한 줄기 지나가는 중인가"를 보고 싶은 쪽이 쓴다.</summary>
        public static float Gust01 => gust01;

        /// <summary>누적 위상. 셰이더 스크롤과 같은 값을 C#에서 쓰고 싶을 때.</summary>
        public static float Phase => phase;

        /// <summary>지금 방위(도). OceanWaves가 파도 방향을 이쪽으로 돌린다.</summary>
        public static float BearingDegrees => bearing;

        /// <summary>바람이 만드는 수평 속도(m/s 근사). 비의 기울기·연기 흐름 같은 곳에 쓴다.</summary>
        public static Vector2 Velocity => direction * (strength * 3.6f);

        private float gustPhase;
        private float gustVelocity;
        private float driftPhase;

        // ── 초목 흔들기 ─────────────────────────────────────────
        //
        // 나무는 그루마다 Update를 돌리지 않고 여기서 **카메라 근처 것만** 골라 흔든다.
        // 섬 50개에 야자수가 수백~수천 그루라 그루마다 Update를 두면 그 자체가 프레임 비용이고,
        // 멀리 있는 나무는 몇 픽셀이라 흔들려도 보이지 않는다.
        private const int MaxSwayed = 48;
        private const float SwayRadius = 110f;
        private const int SwayRescanFrames = 20;

        private static readonly System.Collections.Generic.List<FoliageSway> swayActive =
            new System.Collections.Generic.List<FoliageSway>();

        private Camera swayCamera;
        private int swayRescanCountdown;

        /// <summary>
        /// 씬이 로드될 때마다 스스로 생긴다(WeatherSystem·CompassUI와 같은 자기 완결 패턴).
        /// 씬에 미리 배치할 것이 없으니 프리팹 실수로 빠질 일도 없다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            ResetStatics();

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("WindSystem");
                go.AddComponent<WindSystem>();
            };
        }

        /// <summary>
        /// 도메인 리로드가 정적 필드를 지우고 다시 채우지 않는 경우를 막는다. 이 값들이 0으로
        /// 남으면 방향이 (0,0)이 되어 잔디가 통째로 멈춘 것처럼 보인다.
        /// </summary>
        private static void ResetStatics()
        {
            direction = new Vector2(0.866f, 0.5f);
            strength = 1f;
            gust01 = 0f;
            phase = 0f;
            bearing = 30f;
            swayActive.Clear();
        }

        private void Awake()
        {
            Active = this;
            bearing = bearingDegrees;
            ApplyBearing();
            PushToShader();
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            float storm01 = ReadStorm01();

            // ── 앰비언트: 날씨가 정한다 ───────────────────────
            float ambient = Mathf.Lerp(calmStrength, stormStrength, storm01);

            // ── 돌풍: 주기가 서로 나누어떨어지지 않는 사인 두 겹 ──
            // 두 겹이면 합성 주기가 길어져 "같은 돌풍이 반복된다"는 느낌이 사라진다.
            // pow로 봉우리를 뾰족하게 만드는 이유: 실제 돌풍은 대부분의 시간 잠잠하다가
            // 가끔 한 줄기 지나간다. 사인을 그대로 쓰면 항상 반쯤 불고 있는 바람이 된다.
            gustPhase += dt * (0.12f + 0.20f * storm01);
            float raw = 0.5f + 0.5f * (0.62f * Mathf.Sin(gustPhase * 2.0f)
                                     + 0.38f * Mathf.Sin(gustPhase * 3.7f + 1.3f));
            float gustTarget = Mathf.Pow(Mathf.Clamp01(raw), 2.2f);

            // response time을 두고 따라간다(계단처럼 켜지지 않게).
            gust01 = Mathf.SmoothDamp(gust01, gustTarget, ref gustVelocity,
                Mathf.Max(0.05f, gustResponseSeconds), Mathf.Infinity, dt);

            strength = ambient * (1f + gustAmount * gust01);

            // ── 방위: 한 방향으로 영영 도는 대신 아주 느리게 오간다 ──
            // 계속 같은 방향으로 돌면 몇 분마다 바람이 한 바퀴를 돌아 "회전목마"가 된다.
            // 진동 주기 2π/0.021 ≈ 300초. 기본 속도 0.35°/s를 적분하면 진폭 ±17°가 된다 -
            // "바람이 조금씩 방향을 튼다"는 느낌은 나되 뗏목이 휘둘리지는 않는 폭이다.
            driftPhase += dt * 0.021f;
            float driftScale = Mathf.Lerp(1f, Mathf.Max(1f, stormBearingDriftScale), storm01);
            bearing += Mathf.Sin(driftPhase) * bearingDriftDegreesPerSecond * driftScale * dt;
            ApplyBearing();

            // ── 위상: 세기를 적분한다(클래스 주석의 ★ 참고) ──
            phase += strength * dt;

            PushToShader();
            DriveFoliage();
        }

        /// <summary>날씨에서 폭풍 강도(0~1)를 읽는다. 날씨 시스템이 없으면 잔잔으로 본다.</summary>
        private static float ReadStorm01()
        {
            var weather = WeatherSystem.Active;
            if (weather == null)
                return 0f;

            // 바다 거칠기와 비 세기 중 큰 쪽. 둘은 서로 다른 시간상수로 움직이는데(바다가 더 느리게
            // 일어나고 더 느리게 가라앉는다), 바람은 "지금 사나운가"를 따라야 하므로 max가 맞다.
            return Mathf.Clamp01(Mathf.Max(weather.SeaRoughness01, weather.RainIntensity01));
        }

        /// <summary>
        /// 카메라 근처의 나무를 흔든다. 목록은 SwayRescanFrames 프레임마다 다시 고른다 -
        /// 매 프레임 전체 명부를 훑을 이유가 없고(나무는 걸어 다니지 않는다), 20프레임이면
        /// 플레이어가 최대 몇 미터 움직일 뿐이라 SwayRadius 안쪽에서 놓치는 나무가 없다.
        /// </summary>
        private void DriveFoliage()
        {
            if (swayCamera == null)
            {
                swayCamera = Camera.main;
                if (swayCamera == null)
                    return;
            }

            if (--swayRescanCountdown <= 0)
            {
                swayRescanCountdown = SwayRescanFrames;
                RebuildSwayList(swayCamera.transform.position);
            }

            for (int i = swayActive.Count - 1; i >= 0; i--)
            {
                FoliageSway tree = swayActive[i];

                // 섬이 꺼지거나 파괴될 수 있다(RegenerateWorld). 그러면 목록에서 빼고 넘어간다 -
                // 꺼진 나무를 계속 흔들면 다시 켰을 때 기운 자세가 남는다.
                if (tree == null || !tree.isActiveAndEnabled)
                {
                    swayActive.RemoveAt(i);
                    continue;
                }

                tree.ApplySway(direction, strength, phase);
            }
        }

        private static void RebuildSwayList(Vector3 cameraPosition)
        {
            swayActive.Clear();

            var all = FoliageSway.All;
            float radiusSqr = SwayRadius * SwayRadius;

            for (int i = 0; i < all.Count && swayActive.Count < MaxSwayed; i++)
            {
                FoliageSway tree = all[i];
                if (tree == null || !tree.isActiveAndEnabled)
                    continue;

                if ((tree.transform.position - cameraPosition).sqrMagnitude > radiusSqr)
                    continue;

                swayActive.Add(tree);
            }
        }

        private static void ApplyBearing()
        {
            float rad = bearing * Mathf.Deg2Rad;
            direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        private static void PushToShader()
        {
            Shader.SetGlobalVector(WindProperty,
                new Vector4(direction.x, direction.y, strength, phase));
        }
    }
}
