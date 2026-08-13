using UnityEngine;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 임시 디버그 HUD. OnGUI로 경과 일수, 생존 수치, 상태 이상, 배 진행도, 인벤토리 개수를 화면에 표시한다.
    /// 정식 UGUI 인터페이스가 만들어지기 전까지 플레이 테스트용으로 사용한다.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [Tooltip("표시할 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("표시할 배 제작 진행 상태")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("표시할 경과 일수")]
        public SurvivalClock survivalClock;

        [Tooltip("표시할 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("조작키 안내를 함께 표시할지 여부")]
        public bool showControlsHelp = true;

        /// <summary>
        /// 매 프레임 화면 좌상단에 생존 수치와 진행 상황을 텍스트로 그린다.
        /// </summary>
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 340, 420), GUI.skin.box);

            if (survivalClock != null)
                GUILayout.Label($"경과 일수: {survivalClock.ElapsedDays}일차");

            if (survivalStats != null)
            {
                GUILayout.Label($"체력: {survivalStats.health:F0} / {survivalStats.maxHealth:F0}");
                GUILayout.Label($"허기: {survivalStats.hunger:F0}   갈증: {survivalStats.thirst:F0}");
                GUILayout.Label($"일사병: {survivalStats.sunstroke:F0}   산소: {survivalStats.oxygen:F0}");
                GUILayout.Label($"중독:{(survivalStats.isPoisoned ? "O" : "X")} 출혈:{(survivalStats.isBleeding ? "O" : "X")} 골절:{(survivalStats.hasBrokenBone ? "O" : "X")}");
            }

            if (boatConstruction != null)
            {
                GUILayout.Label($"배 제작: {boatConstruction.currentStage} / {BoatConstructionSystem.TotalStages}단계 (도면 {(boatConstruction.hasCurrentStageBlueprint ? "보유" : "없음")})");
            }

            if (inventory != null)
                GUILayout.Label($"인벤토리 아이템 수: {inventory.items.Count}");

            if (showControlsHelp)
            {
                GUILayout.Space(8);
                GUILayout.Label("[E] 상호작용/공격(무기 필요)   [R] 조리   [C] 섭취   [G] 설치");
                GUILayout.Label("[Tab] 인벤토리   [V] 제작");
                GUILayout.Label("[수영중] [Space] 위로   [Ctrl] 잠수");
            }

            GUILayout.EndArea();
        }
    }
}
