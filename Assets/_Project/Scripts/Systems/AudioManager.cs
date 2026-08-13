using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 전역 사운드(효과음/배경음)를 재생하는 싱글턴 매니저.
    /// 별도의 오디오 에셋 파일 없이, ProceduralAudioClipGenerator로 런타임에 생성한 절차적 사운드
    /// (사인파 비프음/화음/노이즈 버스트/파도 앰비언트)를 사용한다.
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

        // 매 재생마다 새로 생성하지 않도록, Awake에서 한 번만 만들어 캐시해두는 절차적 효과음 클립들.
        private AudioClip clipPickup;
        private AudioClip clipCraftSuccess;
        private AudioClip clipEat;
        private AudioClip clipDrink;
        private AudioClip clipHit;
        private AudioClip clipDamage;
        private AudioClip clipStageComplete;
        private AudioClip clipSaveOrLoad;
        private AudioClip clipOceanAmbient;

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

            BuildClips();
        }

        /// <summary>플레이 시작과 동시에 파도 배경음을 재생한다.</summary>
        private void Start()
        {
            PlayAmbientOcean();
        }

        /// <summary>
        /// 각 상황에 사용할 절차적 효과음 클립들을 미리 생성해둔다.
        /// 주파수/길이 값은 사운드 성격에 맞춰 임의로 정한 것으로, 추후 실제 오디오 에셋으로 교체할 수 있다.
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
            clipOceanAmbient = ProceduralAudioClipGenerator.CreateOceanAmbientLoop(4f); // 파도 배경음 루프
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

        /// <summary>저장 또는 불러오기가 완료됐을 때 재생한다.</summary>
        public void PlaySaveOrLoadFeedback() => PlaySfx(clipSaveOrLoad);

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
    }
}
