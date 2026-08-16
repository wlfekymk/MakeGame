using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 시작 시 스스로 씬에 생성되어 BGM을 루프 재생하는 컴포넌트.
    /// 우선순위: (1) Resources/Audio/Music 아래 사용자가 넣어둔 외부 트랙(ExternalTrackResourcePath)이
    /// 있으면 그것을 재생하고, (2) 없으면 절차적으로 합성한 오리지널 BGM(BackgroundMusicGenerator)으로
    /// 자동 대체(fallback)한다 — 절차적 생성 코드는 삭제하지 않고 "보류(예비)" 상태로 남겨 둔다는
    /// 사용자 지시에 따른 구조다.
    /// DayNightCycle/WeatherSystem과 동일하게 RuntimeInitializeOnLoadMethod로 자기 완결적으로 부트스트랩되며,
    /// 다른 매니저 스크립트나 씬 파일을 전혀 수정하지 않는다(동시에 다른 에이전트가 Scripts/Systems를
    /// 작업 중이어도 파일 충돌이 나지 않도록 신규 파일로만 구현).
    /// </summary>
    public class BackgroundMusicPlayer : MonoBehaviour
    {
        // AudioManager.SetBgmVolume이 저장하는 것과 동일한 PlayerPrefs 키를 그대로 읽어서 쓴다.
        // 이렇게 하면 설정 화면의 "배경음 볼륨" 슬라이더 하나로 파도 앰비언트와 이 BGM을 함께 조절할 수 있다.
        private const string BgmVolumePrefKey = "MakeGame_BgmVolume";

        // Resources.Load 경로 (확장자 제외). Assets/_Project/Resources/Audio/Music/BGM_StayHerePiano.mp3 에 대응한다.
        // 사용자가 제공한 외부 트랙 - 라이선스 확인은 사용자 책임하에 제공된 파일을 그대로 사용한다.
        private const string ExternalTrackResourcePath = "Audio/Music/BGM_StayHerePiano";

        [Tooltip("외부 트랙이 없을 때 쓸 절차적 합성 BGM 루프 전체 길이(초)")]
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
        /// 외부 트랙(Resources/Audio/Music)을 먼저 찾아 있으면 그걸로, 없으면 절차적 아일랜드 테마로
        /// 대체해 루프 재생을 시작하고, 저장된 배경음 볼륨을 적용한다.
        /// </summary>
        private void Start()
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = LoadBgmClip();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            // 임포터의 3D 플래그와 무관하게 항상 2D로 깔린다(플레이어 위치에 따라 좌우로 흐르면 안 된다).
            musicSource.spatialBlend = 0f;

            ApplyVolume();
            musicSource.Play();

            // [B23 감독 수정] 원래 InvokeRepeating(nameof(ApplyVolume), 2f, 2f)였다. InvokeRepeating은
            // **스케일드 타임**으로 돈다. 그런데 SettingsMenuController.cs:106이 설정 화면을 열 때
            // Time.timeScale = 0으로 만든다 → 정작 사용자가 "배경음 볼륨" 슬라이더를 만지는 그 순간에만
            // 폴링이 완전히 멈춰서, 슬라이더를 아무리 움직여도 소리가 안 변했다.
            // (타이틀 화면도 MainMenuController.cs:59에서 timeScale 0이다.)
            // WaitForSecondsRealtime 코루틴으로 바꿔 timeScale과 무관하게 돌게 했다.
            StartCoroutine(PollVolume());
        }

        /// <summary>
        /// 배경음 볼륨을 실시간(unscaled) 주기로 다시 읽는다. 설정 화면이 timeScale을 0으로 만들기
        /// 때문에 반드시 WaitForSecondsRealtime이어야 한다.
        /// </summary>
        private System.Collections.IEnumerator PollVolume()
        {
            var interval = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                yield return interval;
                ApplyVolume();
            }
        }

        /// <summary>
        /// ExternalTrackResourcePath에 사용자가 넣어둔 외부 BGM 파일이 있으면 그것을 로드해 반환하고,
        /// 없으면(Resources.Load가 null을 반환하면) 절차적으로 합성한 오리지널 아일랜드 테마로 대체한다.
        /// 절차적 생성 코드는 지우지 않고 이렇게 예비 대체용으로 남겨 둔다(사용자 지시).
        /// </summary>
        private AudioClip LoadBgmClip()
        {
            AudioClip externalClip = Resources.Load<AudioClip>(ExternalTrackResourcePath);
            if (externalClip != null)
                return externalClip;

            return BackgroundMusicGenerator.CreateIslandTheme(loopDurationSeconds);
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
