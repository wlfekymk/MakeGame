using UnityEngine;

namespace MakeGame.Managers
{
    /// <summary>
    /// 게임 전역 상태(첫 엔딩 달성 여부, 멀티플레이 개방 여부 등)를 관리하는 싱글턴 매니저.
    /// 규칙: 첫 플레이는 무조건 싱글플레이이며, 엔딩을 한 번 본 이후에만 멀티플레이가 개방된다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>첫 엔딩을 한 번이라도 봤는지 여부.</summary>
        public bool HasCompletedFirstEnding { get; private set; }

        /// <summary>
        /// 싱글턴 인스턴스를 초기화하고, 씬 전환 시에도 파괴되지 않도록 설정한다.
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
        }

        /// <summary>
        /// 엔딩(배 제작 완성 등)을 달성했을 때 호출한다.
        /// 최초 1회 달성 시 멀티플레이 모드를 개방한다.
        /// </summary>
        public void CompleteEnding()
        {
            if (HasCompletedFirstEnding)
                return;

            HasCompletedFirstEnding = true;
            Debug.Log("첫 엔딩 달성! 멀티플레이가 개방되었습니다.");
        }

        /// <summary>
        /// 현재 멀티플레이 모드를 선택할 수 있는지 여부를 반환한다.
        /// 첫 엔딩 이전에는 항상 false(싱글플레이 강제)이다.
        /// </summary>
        public bool IsMultiplayerAvailable()
        {
            return HasCompletedFirstEnding;
        }
    }
}
