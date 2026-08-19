using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.UI;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 중에는 마우스 커서를 화면 한가운데에 잠그고(<see cref="CursorLockMode.Locked"/>), 커서가
    /// 실제로 필요한 순간에만 풀어 주는 단일 판정처.
    ///
    /// **왜 필요한가.** 이 프로젝트는 여태 <c>Cursor.lockState</c>를 한 번도 건드리지 않았다
    /// (InventoryUI.cs:634 주석이 그 전제 위에 쓰여 있다). 커서가 자유롭게 떠다니므로 화면 가장자리에
    /// 닿거나 창 밖으로 나가는 순간 <c>Input.GetAxis("Mouse X")</c>가 0이 되고, 그게 "마우스는
    /// 움직이는데 시야는 안 돈다"는 증상이었다. 커서를 잠그면 마우스 델타가 화면 경계와 무관해진다.
    ///
    /// **커서를 푸는 조건**(하나라도 성립하면 푼다):
    ///   1. <c>Time.timeScale &lt;= 0</c> — 타이틀·설정(Esc)·엔딩·사망 화면이 전부 여기 걸린다.
    ///   2. 타이틀 화면(<see cref="MainMenuController.isMenuOpen"/>) 또는 설정 화면
    ///      (<see cref="SettingsMenuController.isOpen"/>)이 열려 있다 — timeScale과 독립적인 이중 안전장치다
    ///      (타이틀은 MainMenuController.Start가 돌기 전 몇 프레임 동안 timeScale이 아직 1이다).
    ///   3. 드래그 가능한 창이 하나라도 열려 있다(아래 참고).
    ///   4. <see cref="releaseCursorKey"/>(기본 LeftShift)를 누르고 있다.
    ///
    /// **창 열림 판정을 UIDragHandle로 하는 이유.** 인벤토리(Tab)·제작(V)·퀘스트(J)·전체 지도(M)·
    /// 건축(B) 다섯 창은 전부 제목 표시줄에 <see cref="UIDragHandle"/>을 붙이고, 닫을 때 창 루트를
    /// <c>SetActive(false)</c>한다(InventoryUI.SetOpen / CraftingUI.SetOpen / QuestUI.SetOpen /
    /// MinimapUI.SetMapOpen / BuildMenuUI.SetOpen). 즉 "활성 상태인 UIDragHandle이 있는가"가 곧
    /// "창이 열려 있는가"다. UI 파일 다섯 개에 각각 손대지 않고 한 곳에서 판정할 수 있다.
    ///
    /// **건축 핫바(BuildMenuUI)만 예외로 뺀다(판단).** 그 창은 플레이어가 여는 창이 아니라 건축 모드가
    /// 켜져 있는 동안 계속 떠 있는 핫바이고, 정작 배치 조준은 커서가 아니라 카메라 정면 레이다
    /// (BuildingSystem.ResolveTarget / TryDemolish는 <c>cam.transform.forward</c>로 쏜다). 이 창을
    /// "열린 창"으로 세면 건축 모드 내내 시야가 얼어붙어 아무것도 지을 수 없다. 대신 부품 선택은
    /// 숫자키 1~5(BuildMenuUI.SelectKeys)로 가능하고, 굳이 칸을 클릭하고 싶으면 Shift를 눌러 커서를
    /// 잠깐 풀면 된다.
    ///
    /// 씬을 수정할 수 없으므로 <see cref="DayNightCycle"/>·<see cref="MakeGame.UI.QuestUI"/>와 같은
    /// RuntimeInitializeOnLoadMethod 자기 완결 부트스트랩으로 씬 로드마다 스스로 생성된다.
    /// **씬에 인스턴스가 없다 → 이 코드의 기본값이 유일한 진실이다**(AGENT_BRIEF 3장).
    /// </summary>
    public class CursorLockController : MonoBehaviour
    {
        /// <summary>이 씬의 커서 잠금 판정처(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static CursorLockController Instance { get; private set; }

        [Tooltip("누르고 있는 동안 커서만 풀리는 키(시야는 그대로 멈춘다). None으로 두면 이 기능이 꺼진다.\n" +
            "기본 LeftShift. 이 키는 '상태'만 읽으므로 InventoryUI.dropWholeStackModifier(Shift+우클릭=한 칸 전부)와 " +
            "WaterStill.bottleModifierKey(Shift+E=물통에 담기)를 방해하지 않는다.")]
        public KeyCode releaseCursorKey = KeyCode.LeftShift;

        [Tooltip("releaseCursorKey가 LeftShift일 때 RightShift도 같은 뜻으로 인정할지 여부.\n" +
            "InventoryUI.cs:1104가 버리기 수식 키에 대해 이미 같은 규칙을 쓰고 있어 그쪽에 맞춘다.")]
        public bool acceptRightShiftToo = true;

        [Tooltip("열린 창 목록을 다시 훑는 주기(초, unscaled). 매 프레임 FindObjectsByType을 돌리지 않기 위한 값이다.\n" +
            "훑기는 '존재하는 손잡이 목록'만 갱신하고, 열림/닫힘 자체는 매 프레임 activeInHierarchy로 본다(지연 0).")]
        public float rescanInterval = 0.2f;

        [Tooltip("커서 잠금 기능 전체를 끄는 안전 스위치. 끄면 예전처럼 커서가 항상 자유롭게 떠다닌다.")]
        public bool enableCursorLock = true;

        /// <summary>지금 커서가 잠겨 있는지. PlayerController가 시야 회전 여부를 판정할 때 쓰는 것과 같은 값이다.</summary>
        public static bool IsCursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>지금 커서가 풀려 있다면 그 이유(빈 문자열이면 잠긴 상태). 디버그/보고용 읽기 전용.</summary>
        public string ReleaseReason { get; private set; } = "초기화 전";

        /// <summary>Shift(해제 키)를 눌러 일시적으로 풀려 있는 상태인지. 읽기 전용.</summary>
        public bool IsReleaseKeyHeld { get; private set; }

        // 존재하는 드래그 손잡이 목록. 열림/닫힘은 매 프레임 activeInHierarchy로 보고, 이 목록 자체만
        // rescanInterval마다 갱신한다(창들은 런타임에 생성되므로 한 번 훑고 끝낼 수 없다).
        private readonly List<UIDragHandle> windowHandles = new List<UIDragHandle>();
        private UIDragHandle buildMenuHandle;
        private MainMenuController mainMenu;
        private SettingsMenuController settingsMenu;

        private float rescanTimer;

        /// <summary>씬이 로드될 때마다 새 CursorLockController를 만든다(DayNightCycle과 같은 패턴).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                // 중복 생성 방지: 커서 상태를 두 인스턴스가 서로 다르게 밀면 매 프레임 깜빡인다.
                if (FindAnyObjectByType<CursorLockController>() != null)
                    return;

                var go = new GameObject("CursorLockController");
                go.AddComponent<CursorLockController>();
            };
        }

        private void Awake()
        {
            Instance = this;
            Rescan();
        }

        private void Update()
        {
            // AGENT_BRIEF 4장: timeScale 0 구간이 존재하므로 타이머는 unscaled로 센다.
            rescanTimer -= Time.unscaledDeltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = Mathf.Max(0.05f, rescanInterval);
                Rescan();
            }

            ApplyCursorState(ShouldReleaseCursor());
        }

        // ────────────────────────────────────────────────────────────────────────
        // 판정
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>지금 커서를 풀어야 하는지 판정하고, 그 이유를 <see cref="ReleaseReason"/>에 남긴다.</summary>
        private bool ShouldReleaseCursor()
        {
            IsReleaseKeyHeld = CheckReleaseKey();

            if (!enableCursorLock)
            {
                ReleaseReason = "잠금 기능 꺼짐";
                return true;
            }

            if (IsReleaseKeyHeld)
            {
                ReleaseReason = "해제 키";
                return true;
            }

            // 타이틀·설정·엔딩·사망 화면은 전부 timeScale 0을 건다(AGENT_BRIEF 4장).
            if (Time.timeScale <= 0f)
            {
                ReleaseReason = "일시정지(timeScale 0)";
                return true;
            }

            if (mainMenu != null && mainMenu.isMenuOpen)
            {
                ReleaseReason = "타이틀 화면";
                return true;
            }

            if (settingsMenu != null && settingsMenu.isOpen)
            {
                ReleaseReason = "설정 화면";
                return true;
            }

            if (IsAnyWindowOpen())
            {
                ReleaseReason = "창 열림";
                return true;
            }

            ReleaseReason = string.Empty;
            return false;
        }

        /// <summary>
        /// 해제 키를 누르고 있는지. <c>Input.GetKey</c>(눌린 '상태')만 읽으므로, 같은 Shift를 수식 키로
        /// 쓰는 InventoryUI(한 칸 전부 버리기)·WaterStill(물통에 담기)과 입력을 다투지 않는다.
        /// 두 곳 모두 GetKey로 상태를 읽는 방식이고, 이쪽도 키를 소비하지 않는다.
        /// </summary>
        private bool CheckReleaseKey()
        {
            if (releaseCursorKey == KeyCode.None)
                return false;

            if (Input.GetKey(releaseCursorKey))
                return true;

            // InventoryUI.cs:1104와 같은 규칙: 왼쪽 Shift가 기본일 때 오른쪽 Shift도 같은 뜻으로 본다.
            return acceptRightShiftToo
                && releaseCursorKey == KeyCode.LeftShift
                && Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>창(인벤토리·제작·퀘스트·전체 지도)이 하나라도 열려 있는지.</summary>
        private bool IsAnyWindowOpen()
        {
            for (int i = 0; i < windowHandles.Count; i++)
            {
                UIDragHandle handle = windowHandles[i];
                if (handle == null)
                    continue;

                // 건축 핫바는 조준(카메라 정면 레이)을 막으므로 창으로 세지 않는다. 클래스 주석 참고.
                if (buildMenuHandle != null && handle == buildMenuHandle)
                    continue;

                if (handle.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }

        /// <summary>존재하는 드래그 손잡이와 메뉴 컨트롤러 참조를 다시 모은다(창들은 런타임에 생긴다).</summary>
        private void Rescan()
        {
            windowHandles.Clear();

            // 닫힌 창의 손잡이도 목록에는 담아 둔다(열림 판정은 매 프레임 activeInHierarchy로 한다).
            // [B35 감독] 3인자 오버로드는 Unity 6에서 obsolete다(CS0618). 이 프로젝트에서 이미 한 번
            // 걸렸던 경고다(AGENT_BRIEF의 CS0618 항목). 비활성 포함 + 정렬 없음은 1인자 형태로 얻는다.
            UIDragHandle[] found = FindObjectsByType<UIDragHandle>(FindObjectsInactive.Include);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    windowHandles.Add(found[i]);
            }

            buildMenuHandle = BuildMenuUI.Instance != null ? BuildMenuUI.Instance.WindowDragHandle : null;

            if (mainMenu == null)
                mainMenu = FindAnyObjectByType<MainMenuController>();

            if (settingsMenu == null)
                settingsMenu = FindAnyObjectByType<SettingsMenuController>();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 적용 / 해제
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 원하는 상태와 실제 상태가 다를 때만 쓴다. 매 프레임 실제 <c>Cursor.lockState</c>와 비교하므로,
        /// 에디터가 밖에서 잠금을 풀어 버린 경우(게임 뷰 포커스 상실 등)에도 다음 프레임에 스스로 복구한다.
        /// </summary>
        private void ApplyCursorState(bool release)
        {
            if (release)
            {
                if (Cursor.lockState != CursorLockMode.None)
                    Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible)
                    Cursor.visible = true;
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible)
                Cursor.visible = false;
        }

        /// <summary>커서를 무조건 풀어 놓는다. 에디터에서 플레이를 멈췄을 때 마우스를 잃지 않기 위한 안전장치다.</summary>
        public static void ForceReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 에디터에서 플레이를 멈추면 OnDisable → OnDestroy 순으로 불린다. 둘 다에서 풀어 두면
        // "플레이를 멈췄는데 마우스가 잠긴 채 남는" 사고가 나지 않는다.
        private void OnDisable()
        {
            ForceReleaseCursor();
        }

        private void OnDestroy()
        {
            ForceReleaseCursor();

            if (Instance == this)
                Instance = null;
        }

        private void OnApplicationQuit()
        {
            ForceReleaseCursor();
        }

        /// <summary>창 포커스를 잃으면 즉시 푼다(포커스가 돌아오면 Update가 조건을 다시 보고 잠근다).</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                ForceReleaseCursor();
        }
    }
}
