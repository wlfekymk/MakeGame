using UnityEngine;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 음량(효과음/배경음) 설정을 조절하는 화면.
    /// 타이틀 화면(MainMenuController)의 "설정" 버튼으로 열 수도 있고, 플레이 중에는 toggleKey(기본 Esc)로
    /// 직접 열고 닫아 간이 일시정지 메뉴처럼 쓸 수도 있다.
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
        }

        /// <summary>
        /// 플레이 중 toggleKey로 직접 열렸을 때만 이 컴포넌트가 스스로 배경+패널을 그린다.
        /// 타이틀 화면을 거쳐 열린 경우(openedStandalone == false)는 MainMenuController가 배경을 그린 뒤
        /// DrawSettingsPanel()을 직접 호출하므로, 여기서 또 그리면 배경이 겹쳐 그려지는 것을 막기 위해 건너뛴다.
        /// </summary>
        private void OnGUI()
        {
            if (!isOpen || !openedStandalone)
                return;

            GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawSettingsPanel();
        }

        /// <summary>
        /// 설정 패널(제목 + 효과음/배경음 볼륨 슬라이더 + 닫기 버튼)을 그린다.
        /// AudioManager.Instance의 SetSfxVolume/SetBgmVolume을 호출해 슬라이더 값을 실시간으로 반영한다.
        /// MainMenuController에서도 이 메서드를 직접 호출해 같은 패널을 그린다.
        /// </summary>
        public void DrawSettingsPanel()
        {
            float panelWidth = 420f;
            float panelHeight = 260f;
            float panelX = (Screen.width - panelWidth) / 2f;
            float panelY = (Screen.height - panelHeight) / 2f;

            GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), string.Empty);

            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(panelX, panelY + 10, panelWidth, 40), "설정", titleStyle);

            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            labelStyle.normal.textColor = Color.white;

            float sliderX = panelX + 30f;
            float sliderWidth = panelWidth - 60f;

            var audio = AudioManager.Instance;
            float currentSfx = audio != null ? audio.sfxVolume : 0f;
            float currentBgm = audio != null ? audio.bgmVolume : 0f;

            GUI.Label(new Rect(sliderX, panelY + 70, sliderWidth, 24), $"효과음 볼륨: {currentSfx:P0}", labelStyle);
            float sfx = GUI.HorizontalSlider(new Rect(sliderX, panelY + 96, sliderWidth, 24), currentSfx, 0f, 1f);
            if (audio != null && !Mathf.Approximately(sfx, currentSfx))
                audio.SetSfxVolume(sfx);

            GUI.Label(new Rect(sliderX, panelY + 130, sliderWidth, 24), $"배경음 볼륨: {currentBgm:P0}", labelStyle);
            float bgm = GUI.HorizontalSlider(new Rect(sliderX, panelY + 156, sliderWidth, 24), currentBgm, 0f, 1f);
            if (audio != null && !Mathf.Approximately(bgm, currentBgm))
                audio.SetBgmVolume(bgm);

            var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            if (GUI.Button(new Rect(panelX + panelWidth / 2f - 80f, panelY + panelHeight - 50f, 160f, 36f), "닫기", buttonStyle))
                Close();
        }
    }
}
