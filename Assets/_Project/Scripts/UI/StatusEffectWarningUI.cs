using UnityEngine;
using MakeGame.Player;

namespace MakeGame.UI
{
    /// <summary>
    /// 출혈/중독/골절처럼 즉각적인 대응이 필요한 상태 이상이 발생했을 때
    /// 화면 상단 중앙에 눈에 띄는 경고 배너를 띄운다.
    /// 기존 DebugHud의 작은 O/X 표시만으로는 상태 이상 발생을 놓치기 쉬워서
    /// 별도의 큰 경고 UI로 보완한다.
    /// </summary>
    public class StatusEffectWarningUI : MonoBehaviour
    {
        [Tooltip("경고 상태를 판단할 대상 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("경고 배너 배경색 (반투명 빨강)")]
        public Color warningColor = new Color(0.6f, 0f, 0f, 0.75f);

        [Tooltip("경고 텍스트가 깜빡이는 속도")]
        public float pulseSpeed = 2.5f;

        private GUIStyle bannerStyle;
        private GUIStyle textStyle;

        /// <summary>
        /// 매 프레임 현재 활성화된 상태 이상을 확인해 화면 상단에 경고 배너를 그린다.
        /// 상태 이상이 없으면 아무것도 그리지 않는다.
        /// </summary>
        private void OnGUI()
        {
            if (survivalStats == null)
                return;

            bool anyWarning = survivalStats.isPoisoned || survivalStats.isBleeding || survivalStats.hasBrokenBone;
            if (!anyWarning)
                return;

            EnsureStyles();

            string message = BuildWarningMessage();

            // 시간에 따라 투명도를 진동시켜(pulse) 시선을 끄는 효과를 준다.
            float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            float alpha = Mathf.Lerp(0.55f, 1f, pulse);

            // 상태 이상이 여러 개 겹치면 문구가 길어져 520px 고정폭에서는 줄바꿈으로 잘려 보이던 문제가 있었다.
            // 화면 폭에 맞춰 최대 900px까지 넓히고, 화면이 더 좁으면 여백 40px만 남기고 줄인다.
            float bannerWidth = Mathf.Min(900f, Screen.width - 40f);
            float bannerHeight = 46f;
            Rect bannerRect = new Rect((Screen.width - bannerWidth) / 2f, 14f, bannerWidth, bannerHeight);

            Color prevColor = GUI.color;
            GUI.color = new Color(warningColor.r, warningColor.g, warningColor.b, warningColor.a * alpha);
            GUI.DrawTexture(bannerRect, Texture2D.whiteTexture);

            // 밝은 배경 위에서도 잘 읽히도록 그림자를 한 겹 먼저 그린 뒤 흰 글자를 덧그린다.
            Rect shadowRect = new Rect(bannerRect.x + 1.5f, bannerRect.y + 1.5f, bannerRect.width, bannerRect.height);
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.6f);
            GUI.Label(shadowRect, message, textStyle);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(bannerRect, message, textStyle);
            GUI.color = prevColor;
        }

        /// <summary>
        /// 현재 활성화된 상태 이상들을 조합해 하나의 경고 문구로 만든다.
        /// 붕대/해독제/부목처럼 어떤 아이템으로 치료 가능한지도 함께 안내한다.
        /// </summary>
        private string BuildWarningMessage()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (survivalStats.isBleeding)
                parts.Add("⚠ 출혈 중! 붕대로 지혈하세요 (C)");
            if (survivalStats.isPoisoned)
                parts.Add("⚠ 중독 상태! 해독제가 필요합니다 (C)");
            if (survivalStats.hasBrokenBone)
                parts.Add("⚠ 골절 상태! 부목으로 치료하세요 (C)");

            return string.Join("   /   ", parts);
        }

        /// <summary>
        /// GUIStyle은 OnGUI 컨텍스트 안에서만 새로 만들 수 있으므로, 최초 호출 시점에 지연 생성한다.
        /// </summary>
        private void EnsureStyles()
        {
            if (textStyle != null)
                return;

            textStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true // 화면이 좁아 배너 폭이 줄어들 때도 글자가 잘리지 않고 줄바꿈되게 한다.
            };
            textStyle.normal.textColor = Color.white;
        }
    }
}
