using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 전역 사운드(효과음/배경음)를 재생하는 싱글턴 매니저.
    /// 별도의 오디오 에셋 파일 없이, ProceduralAudioClipGenerator로 런타임에 생성한 절차적 사운드
    /// (사인파 비프음/화음/노이즈 버스트/파도 앰비언트)를 사용한다.
    /// 효과음/배경음 볼륨은 PlayerPrefs에 저장되어 게임을 다시 실행해도 유지된다
    /// (volumePersistenceFix 참고 - 예전에는 필드에만 남아 있어 매번 기본값으로 리셋됐다).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("볼륨")]
        [Range(0f, 1f)]
        [Tooltip("효과음 전체 볼륨")]
        public float sfxVolume = 0.7f;

        [Range(0f, 1f)]
        [Tooltip("배경음(파도 앰비언트) 볼륨")]
        public float bgmVolume = 0.3f;

        private AudioSource sfxSource;
        private AudioSource bgmSource;
        private AudioSource rainSource;

        // 매 재생마다 새로 생성하지 않도록, Awake에서 한 번만 만들어 캐시해두는 절차적 효과음 클립들.
        private AudioClip clipPickup;
        private AudioClip clipCraftSuccess;
        private AudioClip clipEat;
        private AudioClip clipDrink;
        private AudioClip clipHit;
        private AudioClip clipDamage;
        private AudioClip clipStageComplete;
        private AudioClip clipSaveOrLoad;
        private AudioClip clipBreak;
        private AudioClip clipOceanAmbient;
        private AudioClip clipRainAmbient;

        // 품질 개선(#18): PlayCraftSuccess() 하나가 "치료 성공"/"취침 성공"/"모닥불 점화 성공" 3곳에서
        // 그대로 재사용되고 있어 상황이 구분되지 않던 문제를 보완하는 전용 클립들. 기존 clipCraftSuccess는
        // 그대로 남겨둔다(다른 소유 파일에서 여전히 호출 중이므로 삭제/변경 금지).
        private AudioClip clipHealSuccess;
        private AudioClip clipSleepSuccess;
        private AudioClip clipCampfireLit;

        // [game-designer 최우선 요청] 행동이 조건 미달로 실패했을 때의 전용 신호음. 지금까지 실패는
        // "아무 소리도 나지 않음"이라 플레이어가 버그와 구분할 수 없었다(ResourceNode.Harvest 참고).
        // 성공음과 정반대의 음색(사각파 부저)을 쓴다 - 자세한 이유는 CreateBuzz 주석 참고.
        private AudioClip clipActionFail;

        // [ui-engineer 요청] 상태 이상(중독/출혈/일사병/익사)이 시작된 그 순간에만 울리는 짧은 경고음.
        // 지속 중 반복 재생은 금지 - PlayStatusOnset의 재발동 가드 주석 참고.
        private AudioClip clipStatusOnset;

        // [ui-engineer 요청] 엔딩 연출 전용 팡파르. 배 제작 "단계 완료"(clipStageComplete)를 그대로
        // 재사용하면 150분짜리 플레이의 마지막 소리가 중간 진행 알림과 똑같아진다 - 게임의 마지막
        // 한 번에만 쓰는 소리를 따로 둔다.
        private AudioClip clipEndingFanfare;

        /// <summary>
        /// 싱글턴 인스턴스를 초기화하고, 씬 전환에도 파괴되지 않게 한 뒤 재생용 AudioSource와
        /// 절차적 효과음 클립들을 준비한다.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;

            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;

            // 날씨(비) 배경음 전용 소스. 파도 배경음(bgmSource)과 별도 채널로 둬서, 비가 오는 동안
            // 두 소리가 동시에 자연스럽게 겹쳐 들리게 한다(파도 소리를 끄고 비 소리로 바꾸는 대신).
            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.loop = true;
            rainSource.playOnAwake = false;

            // 버그 수정: 설정 화면에서 조절한 볼륨이 AudioManager 필드에만 저장되고 디스크에는 전혀
            // 저장되지 않아, 게임을 다시 실행할 때마다 항상 기본값(효과음 0.7 / 배경음 0.3)으로
            // 리셋되던 문제. PlayerPrefs에 저장된 값이 있으면 그 값으로 덮어써 이전 설정을 이어간다.
            LoadVolumePrefs();

            BuildClips();
        }

        private const string SfxVolumePrefKey = "MakeGame_SfxVolume";
        private const string BgmVolumePrefKey = "MakeGame_BgmVolume";

        /// <summary>PlayerPrefs에 저장된 볼륨 값이 있으면 불러와 현재 값을 덮어쓴다. 저장된 적이 없으면 기본값을 유지한다.</summary>
        private void LoadVolumePrefs()
        {
            if (PlayerPrefs.HasKey(SfxVolumePrefKey))
                sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey));

            if (PlayerPrefs.HasKey(BgmVolumePrefKey))
                bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumePrefKey));
        }

        /// <summary>플레이 시작과 동시에 파도 배경음을 재생한다.</summary>
        private void Start()
        {
            PlayAmbientOcean();
        }

        /// <summary>
        /// 각 상황에 사용할 절차적 효과음 클립들을 미리 생성해둔다.
        /// 주파수/길이 값은 사운드 성격에 맞춰 임의로 정한 것으로, 추후 실제 오디오 에셋으로 교체할 수 있다.
        /// 품질 개선(#18): 파도/비 앰비언트 루프가 예전엔 4초 고정이라 반복 티가 났다. 44100Hz 기준으로
        /// 파도는 18초(약 793,800 샘플 ≈ 3.0MB), 비는 12초(약 529,200 샘플 ≈ 2.0MB)로 늘려 순환 체감을
        /// 줄였다(둘 다 float[] 임시 배열 기준 추정치이며, AudioClip 자체도 비슷한 크기의 네이티브 버퍼를
        /// 하나 더 들고 있으므로 두 클립 합쳐 런타임에 약 10MB 내외를 차지한다 - 매 프레임이 아니라
        /// Awake 시 한 번만 생성되므로 이 정도 크기/생성 시간은 무리 없는 수준이다).
        /// </summary>
        private void BuildClips()
        {
            clipPickup = ProceduralAudioClipGenerator.CreateBeep(880f, 0.08f, 1046f); // 짧고 밝은 상승음: 채집/획득
            clipCraftSuccess = ProceduralAudioClipGenerator.CreateBeep(660f, 0.18f, 990f); // 제작 성공: 완만한 상승음
            clipEat = ProceduralAudioClipGenerator.CreateBeep(300f, 0.12f); // 섭취(음식): 낮고 짧은 음
            clipDrink = ProceduralAudioClipGenerator.CreateBeep(500f, 0.1f); // 섭취(음료): 중간 음
            clipHit = ProceduralAudioClipGenerator.CreateNoiseBurst(0.08f); // 위험요소 공격 적중: 타격 노이즈
            clipDamage = ProceduralAudioClipGenerator.CreateBeep(180f, 0.2f, 90f); // 피해를 입음: 낮게 떨어지는 경고음
            clipStageComplete = ProceduralAudioClipGenerator.CreateChord(new float[] { 523f, 659f, 784f }, 0.4f); // 배 제작 단계 완료: 3화음 팡파르
            clipSaveOrLoad = ProceduralAudioClipGenerator.CreateBeep(1046f, 0.06f); // 저장/불러오기: 짧은 확인음
            clipBreak = ProceduralAudioClipGenerator.CreateBeep(420f, 0.16f, 70f); // 도구/무기 파손: 뚝 끊기듯 빠르게 떨어지는 저음
            clipOceanAmbient = ProceduralAudioClipGenerator.CreateOceanAmbientLoop(18f); // 파도 배경음 루프 (4초 -> 18초로 확장, 저주파 2중 주기로 불균일하게)
            clipRainAmbient = ProceduralAudioClipGenerator.CreateRainAmbientLoop(12f); // 비 배경음 루프 (4초 -> 12초로 확장. WeatherSystem이 비가 올 때만 재생)

            // 품질 개선(#18): PlayCraftSuccess 재사용 대신 상황별로 구분되는 전용 SFX.
            clipHealSuccess = ProceduralAudioClipGenerator.CreateChord(new float[] { 392f, 494f, 587f }, 0.3f); // 치료 성공: 배 제작 완료(523/659/784)보다 한 옥타브 낮고 차분한 3화음
            clipSleepSuccess = ProceduralAudioClipGenerator.CreateBeep(440f, 0.5f, 220f); // 취침 성공: 다른 효과음보다 훨씬 길게(0.5초) 늘여 서서히 가라앉는 저음 - "잠드는" 느낌
            clipCampfireLit = ProceduralAudioClipGenerator.CreateBeep(150f, 0.15f, 600f); // 모닥불 점화: 낮은음에서 확 타오르듯 빠르게 상승하는 스윕음

            // 실패(조건 미달): 낮은 사각파 부저 2연타. 이 게임에서 사각파를 쓰는 유일한 소리라
            // 채집 성공(880->1046 상승 사인)/제작 성공(660->990)과 음색 자체가 달라 절대 헷갈리지 않는다.
            clipActionFail = ProceduralAudioClipGenerator.CreateBuzz(140f, 0.16f, 2);

            // 상태 이상 시작: 반음 이내로 붙인 두 음의 맥놀이. 협화음인 축하음(523/659/784)·치료음
            // (392/494/587)과 같은 화음 계열이지만 불협이라 귀에는 정반대로("불길함") 읽힌다.
            clipStatusOnset = ProceduralAudioClipGenerator.CreateWarningBeat(466f, 14f, 0.34f);

            // 엔딩 팡파르: 단계 완료(523/659/784, 0.4초)와 같은 C장3화음이되 한 옥타브 위 도(1046)를
            // 얹어 4음으로 넓히고 길이를 1.2초로 늘였다. "같은 계열의 소리인데 더 크고 더 길다"가
            // 마지막이라는 신호가 된다. 새 음색을 만들지 않는 편이 게임 전체 사운드와 어긋나지 않는다.
            clipEndingFanfare = ProceduralAudioClipGenerator.CreateChord(
                new float[] { 523f, 659f, 784f, 1046f }, 1.2f);
        }

        /// <summary>자원 채집 성공 시 재생한다.</summary>
        public void PlayPickup() => PlaySfx(clipPickup);

        /// <summary>제작 성공 시 재생한다.</summary>
        public void PlayCraftSuccess() => PlaySfx(clipCraftSuccess);

        /// <summary>음식을 섭취했을 때 재생한다.</summary>
        public void PlayEat() => PlaySfx(clipEat);

        /// <summary>음료를 섭취했을 때 재생한다.</summary>
        public void PlayDrink() => PlaySfx(clipDrink);

        /// <summary>위험요소를 공격해 적중시켰을 때 재생한다.</summary>
        public void PlayHit() => PlaySfx(clipHit);

        /// <summary>플레이어가 피해를 입었을 때 재생한다.</summary>
        public void PlayDamage() => PlaySfx(clipDamage);

        /// <summary>배 제작 단계를 완료했을 때 재생한다.</summary>
        public void PlayStageComplete() => PlaySfx(clipStageComplete);

        /// <summary>
        /// [ui-engineer 요청] 엔딩 연출의 **팡파르**. 화면이 검게 덮인 뒤 배경색과 제목이 떠오르는
        /// 페이즈 2에서 UI가 직접 부른다(Design_Ending.md 3장).
        ///
        /// 예전에는 EndingChecker.TriggerEnding이 PlayStageComplete()를 즉시 불렀는데, 연출은 암전
        /// 1초로 시작하므로 화면에 아무것도 뜨기 전에 소리부터 나서 그림과 소리가 어긋났다. 그 호출은
        /// 제거했고, 타이밍은 이제 전적으로 UI가 정한다.
        ///
        /// Time.timeScale = 0 에서도 정상 재생된다 - Unity의 오디오 재생은 timeScale의 영향을 받지 않는다
        /// (엔딩 화면은 항상 timeScale 0이므로 이 점이 중요하다).
        /// 엔딩 1회당 1번만 부를 것(1.2초짜리라 겹쳐 울리면 소리가 뭉개진다).
        /// </summary>
        public void PlayEndingFanfare() => PlaySfx(clipEndingFanfare);

        /// <summary>저장 또는 불러오기가 완료됐을 때 재생한다.</summary>
        public void PlaySaveOrLoadFeedback() => PlaySfx(clipSaveOrLoad);

        /// <summary>
        /// 버그 수정: 무기/도구가 내구도(remainingUses) 소진으로 인벤토리에서 조용히 사라져도 아무 피드백이
        /// 없어, 전투 중 손도끼가 갑자기 없어진 이유를 플레이어가 알아채기 어려웠다. 파손 시 재생한다.
        /// </summary>
        public void PlayBreak() => PlaySfx(clipBreak);

        /// <summary>
        /// 품질 개선(#18): 치료 아이템(붕대/해독제/부목) 사용 성공 시 재생한다. 예전에는 PlayCraftSuccess를
        /// 그대로 재사용해 "제작"과 "치료"가 같은 소리로 구분이 안 됐다. 호출부 교체는
        /// ConsumptionSystem.cs(systems-engineer 소유) 쪽에서 이뤄져야 한다.
        /// </summary>
        public void PlayHealSuccess() => PlaySfx(clipHealSuccess);

        /// <summary>
        /// 품질 개선(#18): 쉼터에서 취침(밤 건너뛰기) 성공 시 재생한다. 예전에는 PlayCraftSuccess를
        /// 그대로 재사용했다. 호출부 교체는 Shelter.cs(systems-engineer 소유) 쪽에서 이뤄져야 한다.
        /// </summary>
        public void PlaySleepSuccess() => PlaySfx(clipSleepSuccess);

        /// <summary>
        /// 품질 개선(#18): 모닥불 점화 성공 시 재생한다. 예전에는 PlayCraftSuccess를 그대로 재사용했다.
        /// 호출부 교체는 Campfire.cs(systems-engineer 소유) 쪽에서 이뤄져야 한다.
        /// </summary>
        public void PlayCampfireLit() => PlaySfx(clipCampfireLit);

        /// <summary>
        /// [game-designer 최우선 요청] 플레이어의 행동이 "조건 미달"로 실패했을 때 재생한다
        /// (예: 손도끼 없이 금속조각 채집 시도, 이미 고갈된 노드 재채집).
        ///
        /// 이 신호가 없던 동안 실패는 화면·소리 어느 쪽에서도 아무 일이 일어나지 않는 것과 구별되지
        /// 않았고, 플레이어는 그것을 버그로 읽고 해당 노드를 영구히 포기했다. 화면 문구는
        /// InteractionPromptUI(조준 시 회색 사유 표시)가 담당하므로, 여기서는 "지금 누른 그 입력이
        /// 확실히 처리됐고 다만 거부됐다"는 사실만 소리로 확인해 준다.
        ///
        /// 남용 금지: 조건 미달로 거부된 그 순간에만 부른다. 상시로 반복되는 상태(굶주림 등)에는 쓰지 않는다.
        /// </summary>
        public void PlayActionFail() => PlaySfx(clipActionFail);

        /// <summary>
        /// [ui-engineer 요청] 상태 이상(중독/출혈/일사병/익사)이 **시작된 그 순간**에만 재생하는 경고음.
        /// CombatFeedbackUI.TriggerStatusOnset(Color)의 청각 짝이며, 같은 지점에서 함께 부르면 된다.
        ///
        /// 반드시 1회성으로 부를 것: 상태 이상 플래그가 false -> true로 바뀐 그 프레임에만 호출한다
        /// (StatusEffectWarningUI가 이미 그 방식으로 CombatFeedbackUI를 호출하고 있다). 지속되는 동안
        /// 매 프레임 부르면 경고음이 끊임없이 겹쳐 울려 소리가 뭉개지고 정보 가치가 사라진다.
        ///
        /// 호출부 실수에 대비한 최소한의 방어로 재발동 간격 가드를 하나 둔다. 이것은 "매 프레임 호출해도
        /// 된다"는 뜻이 절대 아니다 - 가드는 같은 접촉으로 중독과 출혈이 동시에 시작되는 경우(곰/상어)처럼
        /// 같은 순간의 중복만 하나로 합쳐 주며, 시간 간격을 둔 정상적인 재발동은 그대로 통과시킨다.
        /// </summary>
        public void PlayStatusOnset()
        {
            // Time.timeScale = 0인 화면(게임오버/설정)에서도 정상 동작하도록 unscaledTime을 쓴다.
            if (Time.unscaledTime - lastStatusOnsetTime < StatusOnsetMinInterval)
                return;

            lastStatusOnsetTime = Time.unscaledTime;
            PlaySfx(clipStatusOnset);
        }

        /// <summary>같은 순간에 겹쳐 시작된 상태 이상들을 한 번의 경고음으로 합치는 최소 간격(초).</summary>
        private const float StatusOnsetMinInterval = 0.3f;

        private float lastStatusOnsetTime = -999f;

        /// <summary>지정한 효과음 클립을 sfxVolume 크기로 한 번 재생한다. 클립이 없으면 아무 것도 하지 않는다.</summary>
        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || sfxSource == null)
                return;

            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>파도 배경음 루프 재생을 시작한다.</summary>
        private void PlayAmbientOcean()
        {
            if (clipOceanAmbient == null || bgmSource == null)
                return;

            bgmSource.clip = clipOceanAmbient;
            bgmSource.volume = bgmVolume;
            bgmSource.Play();
        }

        /// <summary>WeatherSystem이 비가 내리기 시작할 때 호출한다. 이미 재생 중이면 아무 것도 하지 않는다.</summary>
        public void StartRainAmbient()
        {
            if (clipRainAmbient == null || rainSource == null || rainSource.isPlaying)
                return;

            rainSource.clip = clipRainAmbient;
            rainSource.volume = bgmVolume;
            rainSource.Play();
        }

        /// <summary>WeatherSystem이 비가 그칠 때 호출한다.</summary>
        public void StopRainAmbient()
        {
            if (rainSource != null)
                rainSource.Stop();
        }

        /// <summary>
        /// 이 인스턴스가 파괴될 때 정적 참조가 죽은 오브젝트를 계속 가리키지 않도록 정리한다.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 게임 오버 후 재시작처럼, DontDestroyOnLoad로 살아남은 이 싱글턴을 의도적으로 파괴하고
        /// 새 씬에서 새 인스턴스를 만들어야 할 때 미리 정적 참조를 비워둔다.
        /// 비워두지 않으면 새 씬의 AudioManager가 "이미 인스턴스가 있다"고 오인해 스스로 파괴된다.
        /// </summary>
        public static void ClearInstance()
        {
            Instance = null;
        }

        /// <summary>
        /// 설정 화면에서 배경음 볼륨 슬라이더를 조작할 때 호출한다. sfxVolume과 달리 bgmSource는
        /// Start()에서 한 번만 volume을 설정하고 계속 재생 중이므로, 필드만 바꿔서는 이미 재생 중인
        /// 소리 크기가 즉시 반영되지 않는다. 그래서 필드와 실제 AudioSource.volume을 함께 갱신한다.
        /// </summary>
        public void SetBgmVolume(float value)
        {
            bgmVolume = Mathf.Clamp01(value);
            if (bgmSource != null)
                bgmSource.volume = bgmVolume;
            if (rainSource != null)
                rainSource.volume = bgmVolume;

            // 버그 수정: 값을 바꿀 때마다 PlayerPrefs에도 저장해, 다음 실행 시 LoadVolumePrefs()가
            // 이 값을 다시 불러올 수 있게 한다.
            PlayerPrefs.SetFloat(BgmVolumePrefKey, bgmVolume);
        }

        /// <summary>
        /// 설정 화면에서 효과음 볼륨 슬라이더를 조작할 때 호출한다. PlaySfx가 매 재생마다 sfxVolume을
        /// 즉시 읽어 쓰므로 필드만 갱신해도 다음 효과음부터 바로 반영되지만, bgm과 대칭되는 진입점을
        /// 두어 설정 화면 쪽 코드를 일관되게 만든다.
        /// </summary>
        public void SetSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);

            // 버그 수정: 값을 바꿀 때마다 PlayerPrefs에도 저장해, 다음 실행 시 LoadVolumePrefs()가
            // 이 값을 다시 불러올 수 있게 한다.
            PlayerPrefs.SetFloat(SfxVolumePrefKey, sfxVolume);
        }
    }
}
