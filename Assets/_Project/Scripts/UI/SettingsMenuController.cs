using UnityEngine;
using UnityEngine.UI;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 음량(효과음/배경음) 설정을 조절하는 화면.
    /// 타이틀 화면(MainMenuController)의 "설정" 버튼으로 열 수도 있고, 플레이 중에는 toggleKey(기본 Esc)로
    /// 직접 열고 닫아 간이 일시정지 메뉴처럼 쓸 수도 있다.
    /// 개선(B2-13): OnGUI(레거시 IMGUI)로 직접 그리던 것을 UIBuilder 기반 UGUI로 옮겼다. IMGUI는
    /// Screen Space Overlay Canvas보다 항상 나중에(최상단에) 그려져 다른 UGUI 화면을 가려버리는 문제가
    /// 있었기 때문에(GameOverController.OnGUI 사례 참고) OnGUI를 완전히 제거했다. UGUI로 바뀌면서
    /// "MainMenuController가 이 컴포넌트의 DrawSettingsPanel()을 대신 호출해서 그려주는" 방식이 더 이상
    /// 필요 없어졌다 - 이 컴포넌트가 스스로 자기 캔버스를 갖고 isOpen에 따라 보이고 숨는다. 설정 캔버스가
    /// 타이틀 캔버스보다 항상 위(sortOrder 높음)에 있고 화면 전체를 덮는 불투명 배경을 깔기 때문에,
    /// 열려 있는 동안은 자연히 타이틀 화면의 버튼 클릭도 가로막는다(레이캐스트가 앞쪽 그래픽에서 멈춤).
    /// </summary>
    public class SettingsMenuController : MonoBehaviour
    {
        [Tooltip("플레이 중 이 화면을 직접 열고 닫는 키. 타이틀 화면에서는 '설정' 버튼으로 연다.")]
        public KeyCode toggleKey = KeyCode.Escape;

        [Tooltip("플레이 중 toggleKey로 직접 열었을 때 시간을 멈출지 여부 (간이 일시정지 역할)")]
        public bool pauseTimeWhenOpenedDuringPlay = true;

        /// <summary>현재 설정 화면이 열려 있는지 여부.</summary>
        public bool isOpen = false;

        /// <summary>타이틀 화면을 거치지 않고 플레이 중 toggleKey로 직접 연 것인지 여부.</summary>
        private bool openedStandalone = false;

        private float timeScaleBeforeOpen = 1f;

        private GameObject panelRoot;
        private Slider sfxSlider;
        private Slider bgmSlider;
        private Text sfxValueLabel;
        private Text bgmValueLabel;

        /// <summary>
        /// 시작 시 설정 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            BuildUI();
            SetPanelActive(false);
        }

        /// <summary>
        /// 플레이 중에는 toggleKey로 직접 열고 닫을 수 있다.
        /// 타이틀/설정 화면을 통해 이미 열려 있는 경우(openedStandalone == false)에는 이 키로 닫지 않는다 -
        /// 그 경우는 각 화면의 자체 닫기 버튼/흐름을 따르게 한다.
        /// </summary>
        private void Update()
        {
            if (!Input.GetKeyDown(toggleKey))
                return;

            if (isOpen && openedStandalone)
                Close();
            else if (!isOpen)
                OpenStandalone();
        }

        /// <summary>
        /// 타이틀 화면 등 다른 화면에서 이 설정 화면을 열 때 호출한다.
        /// 시간 제어는 호출한 쪽(MainMenuController 등)이 이미 담당하므로 여기서는 건드리지 않는다.
        /// </summary>
        public void Open()
        {
            isOpen = true;
            openedStandalone = false;
            ShowPanel();
        }

        /// <summary>
        /// 플레이 도중 toggleKey로 직접 열 때 호출한다. 필요하면 시간을 멈춰 간이 일시정지 메뉴처럼 동작시킨다.
        /// </summary>
        private void OpenStandalone()
        {
            isOpen = true;
            openedStandalone = true;

            if (pauseTimeWhenOpenedDuringPlay)
            {
                timeScaleBeforeOpen = Time.timeScale;
                Time.timeScale = 0f;
            }

            ShowPanel();
        }

        /// <summary>
        /// 설정 화면을 닫는다. toggleKey로 직접 연 경우에만, 멈췄던 시간을 원래대로 되돌린다.
        /// </summary>
        public void Close()
        {
            isOpen = false;

            if (openedStandalone && pauseTimeWhenOpenedDuringPlay)
                Time.timeScale = timeScaleBeforeOpen;

            openedStandalone = false;
            SetPanelActive(false);
        }

        /// <summary>
        /// 캔버스와 반투명 전체화면 배경, 중앙의 설정 박스(제목/효과음·배경음 슬라이더/닫기 버튼)를 생성한다.
        /// Time.timeScale이 0인 상태(타이틀/일시정지)에서도 UGUI 버튼·슬라이더 클릭은 EventSystem이
        /// Time.deltaTime과 무관하게 매 프레임 처리하므로 정상 동작한다(GameOverUI에서 확인된 패턴과 동일).
        /// </summary>
        private void BuildUI()
        {
            // MainMenuCanvas(15)보다 항상 위에 그려져야 타이틀 화면을 가리는 배경 역할을 할 수 있다.
            var canvas = UIBuilder.CreateCanvas("SettingsCanvas", sortOrder: 16);

            var backdrop = UIBuilder.CreatePanel(
                canvas.transform, "SettingsBackdrop",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.05f, 0.08f, 0.12f, 0.85f));

            panelRoot = backdrop.gameObject;

            var box = UIBuilder.CreatePanel(
                backdrop, "SettingsBox",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(-210f, -130f), offsetMax: new Vector2(210f, 130f),
                color: new Color(0f, 0f, 0f, 0.85f));

            var vlg = box.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 10f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            var title = UIBuilder.CreateText(box, "Title", "설정", 26, Color.white, TextAnchor.MiddleCenter);
            title.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

            sfxValueLabel = UIBuilder.CreateText(box, "SfxLabel", "", 16, Color.white, TextAnchor.MiddleLeft);
            sfxValueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            sfxSlider = UIBuilder.CreateSlider(box, "SfxSlider", 0f, 1f, 0f,
                trackColor: new Color(1f, 1f, 1f, 0.15f),
                fillColor: new Color(0.35f, 0.65f, 0.4f, 1f),
                handleColor: Color.white);
            sfxSlider.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);

            bgmValueLabel = UIBuilder.CreateText(box, "BgmLabel", "", 16, Color.white, TextAnchor.MiddleLeft);
            bgmValueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            bgmSlider = UIBuilder.CreateSlider(box, "BgmSlider", 0f, 1f, 0f,
                trackColor: new Color(1f, 1f, 1f, 0.15f),
                fillColor: new Color(0.35f, 0.65f, 0.4f, 1f),
                handleColor: Color.white);
            bgmSlider.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            bgmSlider.onValueChanged.AddListener(OnBgmSliderChanged);

            var closeButton = UIBuilder.CreateButton(box, "CloseButton", "닫기", Close);
            var closeLayout = closeButton.gameObject.AddComponent<LayoutElement>();
            closeLayout.minHeight = 36f;
            closeLayout.preferredWidth = 160f;
        }

        /// <summary>
        /// 패널을 화면에 보이게 하고, AudioManager의 현재 볼륨 값으로 슬라이더/라벨을 동기화한다.
        /// (기존 OnGUI는 매 프레임 GUI.HorizontalSlider를 그리며 현재 값을 다시 읽었지만, 이 값은
        /// 이 UI를 통해서만 바뀌므로 패널이 열리는 시점에 한 번만 동기화해도 결과는 동일하다.)
        /// </summary>
        private void ShowPanel()
        {
            SetPanelActive(true);
            RefreshVolumeDisplay();
        }

        /// <summary>
        /// 패널을 활성/비활성화한다.
        /// </summary>
        private void SetPanelActive(bool active)
        {
            if (panelRoot != null)
                panelRoot.SetActive(active);
        }

        /// <summary>
        /// AudioManager.Instance의 현재 효과음/배경음 볼륨을 읽어 슬라이더와 라벨에 반영한다.
        /// SetValueWithoutNotify를 써서, 여기서 값을 맞추는 동작 자체가 OnSfxSliderChanged 등을 다시
        /// 불러 AudioManager.SetXxxVolume을 불필요하게 재호출하지 않게 한다.
        /// </summary>
        private void RefreshVolumeDisplay()
        {
            var audio = AudioManager.Instance;
            float currentSfx = audio != null ? audio.sfxVolume : 0f;
            float currentBgm = audio != null ? audio.bgmVolume : 0f;

            sfxSlider.SetValueWithoutNotify(currentSfx);
            bgmSlider.SetValueWithoutNotify(currentBgm);
            sfxValueLabel.text = $"효과음 볼륨: {currentSfx:P0}";
            bgmValueLabel.text = $"배경음 볼륨: {currentBgm:P0}";
        }

        /// <summary>
        /// 효과음 슬라이더를 드래그할 때마다 호출된다. AudioManager에 즉시 반영하고 라벨을 갱신한다.
        /// </summary>
        private void OnSfxSliderChanged(float value)
        {
            AudioManager.Instance?.SetSfxVolume(value);
            sfxValueLabel.text = $"효과음 볼륨: {value:P0}";
        }

        /// <summary>
        /// 배경음 슬라이더를 드래그할 때마다 호출된다. AudioManager에 즉시 반영하고 라벨을 갱신한다.
        /// </summary>
        private void OnBgmSliderChanged(float value)
        {
            AudioManager.Instance?.SetBgmVolume(value);
            bgmValueLabel.text = $"배경음 볼륨: {value:P0}";
        }
    }
}
