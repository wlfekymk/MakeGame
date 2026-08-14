using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 맑음/비를 오가는 간단한 날씨 연출을 담당한다. 비가 오는 동안은 플레이어 머리 위에서 비 파티클이
    /// 내리고, 빗소리 배경음이 재생되며, DayNightCycle이 계산하는 태양광 밝기를 이 클래스의
    /// IsRaining/rainDimFactor 값을 참고해 추가로 어둡게 만든다(직접 sunLight.intensity를 건드리지
    /// 않는 이유는 DayNightCycle.Update()와 같은 필드를 서로 다른 스크립트가 매 프레임 덮어쓰면
    /// 실행 순서에 따라 값이 튀거나 서로 상쇄될 수 있기 때문 - 대신 DayNightCycle이 "비가 오는지"를
    /// 읽어서 자기 계산 결과에 곱하는 단방향 의존 구조로 설계했다).
    /// DayNightCycle과 동일하게 SubsystemRegistration + SceneManager.sceneLoaded 패턴으로
    /// 재시작 시에도 매번 새로 생성된다.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        [Tooltip("맑은 날씨가 지속되는 최소 시간(초, 실시간)")]
        public float minClearSeconds = 90f;

        [Tooltip("맑은 날씨가 지속되는 최대 시간(초, 실시간)")]
        public float maxClearSeconds = 240f;

        [Tooltip("비가 지속되는 최소 시간(초, 실시간)")]
        public float minRainSeconds = 40f;

        [Tooltip("비가 지속되는 최대 시간(초, 실시간)")]
        public float maxRainSeconds = 100f;

        [Tooltip("비가 올 때 태양광 밝기에 곱할 배율(0~1). 낮을수록 더 어두워진다")]
        [Range(0f, 1f)]
        public float rainDimFactor = 0.55f;

        [Tooltip("비 파티클을 플레이어(카메라) 머리 위 얼마나 높은 곳에 띄울지")]
        public float rainHeightAboveTarget = 15f;

        /// <summary>현재 비가 오고 있는지 여부. DayNightCycle이 태양광 밝기 계산에 참고한다.</summary>
        public bool IsRaining { get; private set; }

        private float phaseTimer;
        private float phaseDuration;
        private ParticleSystem rainParticles;
        private Transform followTarget;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 WeatherSystem을 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("WeatherSystem");
                go.AddComponent<WeatherSystem>();
            };
        }

        /// <summary>비 파티클을 만들고, 항상 맑은 날씨로 시작한다(플레이어가 스폰되자마자 비를 맞지 않도록).</summary>
        private void Start()
        {
            var cam = Camera.main;
            followTarget = cam != null ? cam.transform : null;

            BuildRainParticles();
            StartClearPhase();
        }

        /// <summary>
        /// 코드로 비 파티클 시스템을 만든다. gravityModifier로 월드 아래 방향으로 자연스럽게
        /// 떨어지게 한다(에미터 회전과 무관하게 항상 아래로 떨어짐).
        /// 버그 수정: 처음에는 셰이더를 지정하지 않고 파티클 시스템 기본(내장 렌더 파이프라인용)
        /// 머티리얼을 그대로 뒀는데, 실제 라이브 플레이테스트에서 비가 올 때 화면에 마젠타색
        /// 점들이 흩뿌려지는 것을 확인했다 - URP 프로젝트에서는 내장 렌더 파이프라인 전용 셰이더를
        /// 인식하지 못해 "셰이더 없음"을 뜻하는 마젠타로 표시된 것. Universal Render
        /// Pipeline/Particles/Unlit 셰이더를 명시적으로 찾아 머티리얼에 지정해 해결했다
        /// (혹시 그 셰이더도 없는 환경을 대비해 URP에서도 안전하게 동작하는 Sprites/Default를
        /// 대체 셰이더로 둔다).
        /// </summary>
        private void BuildRainParticles()
        {
            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            rainParticles = go.AddComponent<ParticleSystem>();

            var main = rainParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.5f;
            main.startSpeed = 2f;
            main.gravityModifier = 3f;
            main.startSize = 0.05f;
            main.startColor = new Color(0.75f, 0.82f, 0.92f, 0.55f);
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = rainParticles.emission;
            emission.rateOverTime = 500f;

            var shape = rainParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 0.5f, 24f);

            var renderer = rainParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default"); // URP에서도 안전하게 동작하는 대체 셰이더
                if (shader != null)
                    renderer.material = new Material(shader);
            }

            rainParticles.Stop();
        }

        /// <summary>매 프레임 날씨 단계(맑음/비) 타이머를 진행시키고, 비가 올 때 파티클을 따라오게 한다.</summary>
        private void Update()
        {
            phaseTimer += Time.deltaTime;
            if (phaseTimer >= phaseDuration)
            {
                if (IsRaining)
                    StartClearPhase();
                else
                    StartRainPhase();
            }

            if (IsRaining && followTarget != null)
            {
                transform.position = new Vector3(
                    followTarget.position.x,
                    followTarget.position.y + rainHeightAboveTarget,
                    followTarget.position.z);
            }
        }

        /// <summary>맑은 날씨로 전환한다. 비 파티클과 빗소리를 멈춘다.</summary>
        private void StartClearPhase()
        {
            IsRaining = false;
            phaseTimer = 0f;
            phaseDuration = Random.Range(minClearSeconds, maxClearSeconds);

            if (rainParticles != null)
                rainParticles.Stop();

            AudioManager.Instance?.StopRainAmbient();
        }

        /// <summary>비 오는 날씨로 전환한다. 비 파티클과 빗소리를 시작한다.</summary>
        private void StartRainPhase()
        {
            IsRaining = true;
            phaseTimer = 0f;
            phaseDuration = Random.Range(minRainSeconds, maxRainSeconds);

            if (followTarget != null)
                transform.position = followTarget.position + Vector3.up * rainHeightAboveTarget;

            if (rainParticles != null)
                rainParticles.Play();

            AudioManager.Instance?.StartRainAmbient();
        }
    }
}
