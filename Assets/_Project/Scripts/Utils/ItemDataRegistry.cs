using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// B2-16: SaveLoadController.FindItemDataByName의 근본 한계(Resources.FindObjectsOfTypeAll은
    /// "지금 메모리에 로드된" ItemData만 찾을 수 있어, 씬의 어떤 컴포넌트도 참조하지 않는 ItemData는
    /// 영영 못 찾는다)를 해결하기 위한 전수 목록 ScriptableObject.
    ///
    /// 해결 방식: 모든 ItemData 에셋을 Resources/ 아래로 옮기는 대신(GUID/참조 무결성 위험이 커서
    /// 이번 배치에서는 채택하지 않음 - Spec_15와 동일한 이유로 안전 전환을 우선함), 이 레지스트리
    /// 하나만 Resources/ 아래에 두고 모든 ItemData를 필드로 직접 참조한다. Unity는 레지스트리
    /// 에셋을 로드하는 순간 이 레지스트리가 참조하는 모든 ItemData를 함께 로드하므로, 씬의 다른
    /// 어떤 컴포넌트도 참조하지 않는 ItemData라도 Resources.Load(레지스트리)만 하면 메모리에
    /// 올라와 찾을 수 있게 된다.
    ///
    /// 이번 배치 범위: 이 클래스와 SaveLoadController의 로딩 경로만 만든다. 실제 `.asset` 인스턴스
    /// 생성 및 31개 ItemData 등록은 하지 않았다(에셋 배치는 game-designer 담당 - 코디네이터 보고서의
    /// [요청] 항목 참고). 에셋이 아직 없을 때는 LoadFromResources()가 null을 반환하고,
    /// SaveLoadController는 기존 FindObjectsOfTypeAll 방식으로 안전하게 폴백한다(정상 동작, 근본
    /// 한계만 남아 있음).
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDataRegistry", menuName = "MakeGame/Item Data Registry")]
    public class ItemDataRegistry : ScriptableObject
    {
        [Tooltip("게임에 존재하는 모든 ItemData 에셋. 여기 등록된 항목은 씬의 다른 컴포넌트가 참조하지" +
            " 않아도 이 레지스트리를 로드하는 순간 함께 로드되어 이름으로 찾을 수 있다.\n" +
            "[요청] game-designer: Assets/_Project/Resources/ItemDataRegistry.asset 이름으로 이 " +
            "ScriptableObject의 인스턴스를 만들고(메뉴: Assets > Create > MakeGame > Item Data Registry)," +
            " GUID 매핑표(Docs/Balance_SceneSnapshot.md 1장, 31개 전수)에 있는 모든 ItemData 에셋을 이" +
            " 리스트에 채워 넣어 주세요. 폴더가 정확히 'Resources'여야 Resources.Load가 찾을 수 있습니다.")]
        public List<ItemData> allItems = new List<ItemData>();

        /// <summary>
        /// Resources 폴더 안에서 이 레지스트리를 찾아 로드한다.
        /// 파일 이름이 정확히 "ItemDataRegistry"이고 어떤 Resources 폴더 아래에든 있으면 찾는다
        /// (예: Assets/_Project/Resources/ItemDataRegistry.asset).
        /// 아직 에셋이 만들어지지 않았으면(1단계 현재 상태) null을 반환한다 - 호출부는 반드시
        /// null을 정상적인 "아직 없음" 상태로 처리하고 기존 폴백 경로를 써야 한다.
        /// </summary>
        public static ItemDataRegistry LoadFromResources()
        {
            return Resources.Load<ItemDataRegistry>("ItemDataRegistry");
        }
    }
}
