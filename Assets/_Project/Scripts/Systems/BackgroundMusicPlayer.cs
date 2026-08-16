using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 시작 시 스스로 씬에 생성되어, 절차적으로 합성한 오리지널 BGM(BackgroundMusicGenerator)을
    /// 루프 재생하는 컴포넌트. 외부 음원 파일을 전혀 쓰지 않으므로 저작권/라이선스 문제가 없다.
    /// DayNightCycle/WeatherSystem과 동일하게 RuntimeInitializeOnLoadMethod로 자기 완결적으로 부트스트랩되며,
    /// 다른 매니저 스크립트나 씬 파일을 전혀 수정하지 않는다(동시에 다른 에이전트가 Scripts/Systems를
    /// 작업 중이어도 파일 충돌이 나지 않도록 신규 파일로만 구현).
    /// </summary>
    public class BackgroundMusicPlayer : MonoBehaviour
    {
        // AudioManager.SetBgmVolume이 저장하는 것과 동일한 PlayerPrefs 키를 그대로 읽어서 쓴다.
        // 이렇게 하면 설정 화면의 "배경음 볼륨" 슬라이더 하나로 파도 앰비언트와 이 BGM을 함께 조절할 수 있다.
        private const string BgmVolumePrefKey = "MakeGame_BgmVolume";

        [Tooltip("BGM 루프 전체 길이(초)")]
        public float loopDurationSeconds = 48f;

        [Tooltip("배경음 볼륨 슬라이더 값 대비 이 음악의 상대 볼륨 배율. 파도 앰비언트보다 은은하게 깔리도록 1보다 낮게 잡는다.")]
        [Range(0f, 1f)]
        public float musicVolumeScale = 0.55f;

        private AudioSource musicSource;

        /// <summary>
        /// 씬이 로드된 직후 자동으로 호출되어 BGM 재생 전용 GameObject를 만들고 이 컴포넌트를 붙인다.
        /// 이미 존재하면(씬 재로드 등) 중복 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<BackgroundMusicPlayer>() != null)
                return;

            var go = new GameObject("BackgroundMusicPlayer");
            go.AddComponent<BackgroundMusicPlayer>();
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// 오리지널 아일랜드 테마를 생성해 루프 재생을 시작하고, 저장된 배경음 볼륨을 적용한다.
        /// </summary>
        private void Start()
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = BackgroundMusicGenerator.CreateIslandTheme(loopDurationSeconds);
            musicSource.loop = true;
            musicSource.playOnAwake = false;

            ApplyVolume();
            musicSource.Play();

            // 설정 화면에서 볼륨 슬라이더를 바꾸면 PlayerPrefs 값이 갱신되므로, 2초마다 다시 읽어 반영한다.
            // AudioManager와 별도 컴포넌트라 이벤트 구독 대신 가벼운 폴링 방식을 쓴다(비용 미미, 매 프레임이 아님).
            InvokeRepeating(nameof(ApplyVolume), 2f, 2f);
        }

        /// <summary>
        /// PlayerPrefs에 저장된 배경음 볼륨(없으면 기본값 0.3)을 읽어 musicVolumeScale을 곱한 뒤 음악 소스 볼륨에 반영한다.
        /// </summary>
        private void ApplyVolume()
        {
            if (musicSource == null)
                return;

            float bgmVolume = PlayerPrefs.HasKey(BgmVolumePrefKey)
                ? Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumePrefKey))
                : 0.3f;

            musicSource.volume = bgmVolume * musicVolumeScale;
        }
    }
}
