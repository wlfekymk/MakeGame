using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>난이도. 정수로 PlayerPrefs에 저장되므로 **값을 재배치하지 말고 끝에만 추가**한다.</summary>
    public enum GameDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>
    /// PlayerPrefs 기반 전역 설정 저장소(정적).
    ///
    /// [왜 새 파일인가 - 볼륨과의 관계] 효과음/배경음 볼륨은 이미
    /// <see cref="AudioManager"/>가 <c>MakeGame_SfxVolume</c> / <c>MakeGame_BgmVolume</c> 키로
    /// PlayerPrefs에 직접 읽고 쓴다(AudioManager.LoadVolumePrefs / SetSfxVolume / SetBgmVolume,
    /// BackgroundMusicPlayer도 같은 키를 읽는다). 그 두 값을 여기로 **옮기지 않는다**:
    ///  · AudioManager는 이 작업의 락 밖이라 한 글자도 고칠 수 없고,
    ///  · 옮기면 키가 둘로 갈라져 "설정에서 줄인 볼륨이 다음 실행에 안 먹는" 사고가 난다.
    /// 그래서 **저장 방식(PlayerPrefs + "MakeGame_" 접두 키)만 똑같이 따르고 값은 공존**시킨다.
    /// 볼륨의 주인은 계속 AudioManager, 그 외 설정(감도·Y축 반전·난이도·세이브 슬롯)의 주인은 여기다.
    ///
    /// [비용] 값은 정적 필드에 캐시되고 PlayerPrefs는 최초 1회만 읽는다. 게터는 필드 반환이라
    /// 매 프레임 호출해도(PlayerController.HandleLook) 할당도 디스크 접근도 0이다.
    /// 세터는 PlayerPrefs.Set*(메모리 기록)만 하고, 디스크 flush(PlayerPrefs.Save)는 슬라이더처럼
    /// 연속으로 바뀌는 값에서는 **일부러 부르지 않는다**(드래그 한 번에 디스크를 수십 번 긁지 않도록).
    /// 버튼 한 번으로 끝나는 이산 설정(반전·난이도·슬롯)만 즉시 flush한다.
    ///
    /// [R1 규약] 도메인 리로드를 끈 상태에서 정적 상태가 이전 플레이 세션에서 새지 않도록
    /// SubsystemRegistration 훅에서 캐시와 구독자를 전부 비운다(IslandArchetype.ResetStaticCache 선례).
    ///
    /// **난수를 소비하지 않는다.**
    /// </summary>
    public static class GameSettings
    {
        // ── PlayerPrefs 키. AudioManager가 쓰는 "MakeGame_" 접두 규약을 그대로 따른다. ──────────
        private const string MouseSensitivityKey = "MakeGame_MouseSensitivity";
        private const string InvertLookYKey = "MakeGame_InvertLookY";
        private const string DifficultyKey = "MakeGame_Difficulty";
        private const string SaveSlotKey = "MakeGame_SaveSlot";

        /// <summary>감도 슬라이더의 하한. 0에 닿으면 시야가 아예 안 돌아 "고장"으로 보이므로 0으로 두지 않는다.</summary>
        public const float MinMouseSensitivity = 0.3f;

        /// <summary>감도 슬라이더의 상한.</summary>
        public const float MaxMouseSensitivity = 3f;

        /// <summary>기본 감도. 1 = 씬에 직렬화된 PlayerController.lookSensitivity 그대로(기존 동작 100% 보존).</summary>
        public const float DefaultMouseSensitivity = 1f;

        /// <summary>세이브 슬롯 개수. 슬롯 번호는 1부터 이 값까지다(SaveLoadController가 이 상수를 쓴다).</summary>
        public const int SaveSlotCount = 3;

        /// <summary>
        /// 이산 설정(Y축 반전·난이도·세이브 슬롯)이 바뀌면 발행된다. 연속 값(감도 슬라이더)은 발행하지
        /// 않는다 - 이유는 MouseSensitivity 세터 주석 참고.
        /// 설정 창이 닫혀 있는 동안에는 아무도 발행하지 않으므로 비용 0이다.
        /// 구독자는 반드시 OnDestroy에서 구독을 푼다(PlayerController 참고).
        /// </summary>
        public static event System.Action Changed;

        private static bool loaded;
        private static float mouseSensitivity = DefaultMouseSensitivity;
        private static bool invertLookY;
        private static GameDifficulty difficulty = GameDifficulty.Normal;
        private static int saveSlot = 1;

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 에디터에서 이전 플레이 세션의 캐시/구독자가 남지 않게 리셋한다.
        /// 값 자체는 PlayerPrefs에 있으므로 여기서 지워도 다음 접근 때 다시 읽힌다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            loaded = false;
            mouseSensitivity = DefaultMouseSensitivity;
            invertLookY = false;
            difficulty = GameDifficulty.Normal;
            saveSlot = 1;
            Changed = null;
        }

        /// <summary>
        /// 최초 접근 시 한 번만 PlayerPrefs에서 값을 읽어 캐시한다. 저장된 적이 없는 항목은 기본값을
        /// 그대로 둔다(= 설정을 한 번도 만지지 않은 사용자에게 기존 동작이 그대로 유지된다).
        /// </summary>
        private static void EnsureLoaded()
        {
            if (loaded)
                return;

            loaded = true;

            if (PlayerPrefs.HasKey(MouseSensitivityKey))
                mouseSensitivity = ClampSensitivity(PlayerPrefs.GetFloat(MouseSensitivityKey));

            if (PlayerPrefs.HasKey(InvertLookYKey))
                invertLookY = PlayerPrefs.GetInt(InvertLookYKey) != 0;

            if (PlayerPrefs.HasKey(DifficultyKey))
                difficulty = ClampDifficulty(PlayerPrefs.GetInt(DifficultyKey));

            if (PlayerPrefs.HasKey(SaveSlotKey))
                saveSlot = ClampSlot(PlayerPrefs.GetInt(SaveSlotKey));
        }

        /// <summary>
        /// 마우스 시점 감도 배율(0.3~3.0, 기본 1.0). PlayerController.lookSensitivity에 **곱해지는** 값이다 -
        /// 씬에 직렬화된 감도를 갈아치우지 않고 배율만 얹으므로, 1.0이면 예전과 완전히 같은 회전량이 된다.
        /// </summary>
        public static float MouseSensitivity
        {
            get
            {
                EnsureLoaded();
                return mouseSensitivity;
            }
            set
            {
                EnsureLoaded();
                float clamped = ClampSensitivity(value);
                if (Mathf.Approximately(clamped, mouseSensitivity))
                    return;

                mouseSensitivity = clamped;
                PlayerPrefs.SetFloat(MouseSensitivityKey, clamped);
                // 슬라이더 드래그 중 매 프레임 들어오는 값이라 여기서는 디스크로 flush하지 않는다.
                // PlayerPrefs는 애플리케이션 종료(OnApplicationQuit) 시 자동으로 기록된다.
                //
                // **Changed도 일부러 발행하지 않는다.** 감도를 쓰는 곳(PlayerController.HandleLook)은
                // 매 프레임 이 값을 직접 읽으므로 통지가 필요 없고, 드래그 한 번에 이벤트가 수십 번
                // 발행되면 세이브 슬롯 요약을 다시 읽는 구독자(MainMenuController)가 프레임마다
                // 파일을 3개씩 여는 사고가 난다. Changed는 버튼 한 번으로 끝나는 이산 설정 전용이다.
            }
        }

        /// <summary>상하 시점 반전(비행 시뮬 방식). 켜면 마우스를 위로 밀 때 시선이 아래로 내려간다.</summary>
        public static bool InvertLookY
        {
            get
            {
                EnsureLoaded();
                return invertLookY;
            }
            set
            {
                EnsureLoaded();
                if (invertLookY == value)
                    return;

                invertLookY = value;
                PlayerPrefs.SetInt(InvertLookYKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>난이도. 허기/갈증 감소 속도에 배율로 걸린다(아래 배율 표 참고).</summary>
        public static GameDifficulty Difficulty
        {
            get
            {
                EnsureLoaded();
                return difficulty;
            }
            set
            {
                EnsureLoaded();
                if (difficulty == value)
                    return;

                difficulty = ClampDifficulty((int)value);
                PlayerPrefs.SetInt(DifficultyKey, (int)difficulty);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// 현재 세이브 슬롯(1~<see cref="SaveSlotCount"/>). F5/F9와 타이틀 "이어하기"가 모두 이 슬롯을 쓴다.
        /// **슬롯 1은 기존 단일 세이브 파일 그 자체다**(SaveLoadController.SlotFilePath 주석 참고).
        /// </summary>
        public static int SaveSlot
        {
            get
            {
                EnsureLoaded();
                return saveSlot;
            }
            set
            {
                EnsureLoaded();
                int clamped = ClampSlot(value);
                if (clamped == saveSlot)
                    return;

                saveSlot = clamped;
                PlayerPrefs.SetInt(SaveSlotKey, clamped);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>다음 난이도로 순환시킨다(쉬움 → 보통 → 어려움 → 쉬움). 설정 화면의 난이도 버튼용.</summary>
        public static void CycleDifficulty()
        {
            int next = ((int)Difficulty + 1) % 3;
            Difficulty = (GameDifficulty)next;
        }

        /// <summary>
        /// 허기 감소 속도 배율. SurvivalStats.hungerDecayPerSecond에 곱해 적용된다
        /// (적용 지점은 PlayerController.ApplyDifficultyToSurvival - 그 파일이 survivalStats의 주인이다).
        /// </summary>
        public static float HungerDrainMultiplier => DrainMultiplier(Difficulty);

        /// <summary>갈증 감소 속도 배율. 허기와 같은 값·같은 적용 지점을 쓴다.</summary>
        public static float ThirstDrainMultiplier => DrainMultiplier(Difficulty);

        /// <summary>
        /// 위협(곰/상어 등)이 주는 피해 배율.
        ///
        /// **0.2.51에서 배선됨 — SurvivalStats.TakeDamage가 전투 원인(Predator/SharkAttack)에만 곱한다.** 원래 계획했던 피해 진입점인
        /// SurvivalStats.TakeDamage(또는 CombatSystem/HazardSource의 피해 계산)를 고쳐야 하는데
        /// 그 파일들은 이 작업의 락 밖이다. 값만 여기 두고 배선은 그 파일의 소유자에게 넘긴다 -
        /// <c>survivalStats.TakeDamage(amount * GameSettings.ThreatDamageMultiplier, cause)</c> 한 줄이면 끝난다.
        /// (난이도 설정 자체는 허기/갈증 배율이 실제로 걸리므로 죽은 스위치가 아니다.)
        /// </summary>
        public static float ThreatDamageMultiplier
        {
            get
            {
                switch (Difficulty)
                {
                    case GameDifficulty.Easy: return 0.7f;
                    case GameDifficulty.Hard: return 1.4f;
                    default: return 1f;
                }
            }
        }

        /// <summary>
        /// 난이도별 허기/갈증 감소 배율. 보통(1.0)이 기존 밸런스 그대로이고, 쉬움/어려움만 위아래로 벌린다 -
        /// 기본값을 건드리지 않아야 "설정을 만지지 않은 사용자의 게임이 그대로"라는 호환 규칙이 지켜진다.
        /// </summary>
        private static float DrainMultiplier(GameDifficulty value)
        {
            switch (value)
            {
                case GameDifficulty.Easy: return 0.7f;
                case GameDifficulty.Hard: return 1.35f;
                default: return 1f;
            }
        }

        /// <summary>난이도의 한글 표기(UI 라벨용).</summary>
        public static string DifficultyLabel(GameDifficulty value)
        {
            switch (value)
            {
                case GameDifficulty.Easy: return "쉬움";
                case GameDifficulty.Hard: return "어려움";
                default: return "보통";
            }
        }

        /// <summary>감도 값을 허용 범위로 자른다.</summary>
        public static float ClampSensitivity(float value)
        {
            return Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
        }

        /// <summary>슬롯 번호를 1~SaveSlotCount로 자른다. 범위 밖 값(옛 prefs·잘못된 호출)은 1로 수렴한다.</summary>
        public static int ClampSlot(int slot)
        {
            return Mathf.Clamp(slot, 1, SaveSlotCount);
        }

        /// <summary>저장된 정수를 난이도로 되돌린다. 모르는 값은 보통으로 취급한다(기존 밸런스).</summary>
        private static GameDifficulty ClampDifficulty(int value)
        {
            if (value == (int)GameDifficulty.Easy)
                return GameDifficulty.Easy;
            if (value == (int)GameDifficulty.Hard)
                return GameDifficulty.Hard;

            return GameDifficulty.Normal;
        }
    }
}
