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
        /// 비워두지 않으면 새 씬의 GameManager가 "이미 인스턴스가 있다"고 오인해 스스로 파괴된다.
        /// </summary>
        public static void ClearInstance()
        {
            Instance = null;
        }
    }
}
