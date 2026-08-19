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
    /// 생성 및 ItemData 등록은 처음엔 하지 않았다(현재는 Resources/ItemDataRegistry.asset에 57종 등록됨 - 낡으면 개수보다 에셋을 믿어라. 코디네이터 보고서의
    /// [요청] 항목 참고). 에셋이 아직 없을 때는 LoadFromResources()가 null을 반환하고,
    /// SaveLoadController는 기존 FindObjectsOfTypeAll 방식으로 안전하게 폴백한다(정상 동작, 근본
    /// 한계만 남아 있음).
    ///
    /// B4-5(등록 누락 감지): 이 레지스트리는 "전수 목록"이라는 전제 위에서만 제 역할을 하는데, 목록에
    /// 빠지거나 잘못 등록된 항목이 있어도 지금까지는 아무 신호 없이 조용히 실패했다(빠진 아이템은
    /// 세이브 로드 시 이름으로 못 찾아 사라지고, 이름이 중복되면 SaveLoadController가 먼저 찾은
    /// 항목으로 덮어써 엉뚱한 아이템이 복원된다). 그래서 런타임 로드 시점(LoadFromResources)에 1회
    /// 자체 검증을 돌려 문제 인덱스를 Debug.LogWarning으로 남기고, 에디터에서는 OnValidate가
    /// AssetDatabase 전수 조회로 "프로젝트에는 있는데 목록에 없는" 진짜 누락까지 잡아낸다.
    /// 검증은 경고만 남기며 목록을 자동으로 고치지 않는다 - `.asset` 수정은 game-designer 담당이다.
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
            var registry = Resources.Load<ItemDataRegistry>("ItemDataRegistry");

            if (registry != null)
                registry.ValidateEntriesOnce();

            return registry;
        }

        /// <summary>
        /// 이미 검증을 한 번 돌렸는지 여부. Resources.Load는 같은 에셋 인스턴스를 캐시해 돌려주므로,
        /// 이 플래그 하나로 호출부가 몇 번 로드하든 경고가 중복 출력되지 않는다.
        /// [System.NonSerialized]: 이 값은 에셋에 저장되면 안 된다(저장되면 다음 실행에서 검증이 통째로 생략된다).
        /// </summary>
        [System.NonSerialized] private bool validated;

        /// <summary>
        /// 런타임 최초 로드 시 1회만 ValidateEntries를 실행한다(매 프레임/매 호출 로그 스팸 방지).
        /// </summary>
        private void ValidateEntriesOnce()
        {
            if (validated)
                return;

            validated = true;
            ValidateEntries("런타임");
        }

        /// <summary>
        /// allItems 목록의 정합성을 검사하고 문제를 Debug.LogWarning으로 보고한다. 목록을 고치지는 않는다.
        /// 검사 항목:
        /// 1) 목록이 비어 있음 (레지스트리 에셋만 만들고 아이템을 채우지 않은 상태)
        /// 2) null 항목 (에셋이 삭제됐거나 슬롯만 추가하고 비워둔 경우) - 인덱스를 찍어준다
        /// 3) itemName이 비어 있음 - SaveLoadController가 이름을 키로 쓰므로 절대 찾을 수 없는 죽은 등록이다
        ///    (ItemData에는 별도의 id/displayName 필드가 없고 itemName 하나가 그 역할을 겸한다)
        /// 4) 같은 ItemData 에셋이 두 번 등록됨 - 런타임에서 GUID를 볼 수 없으므로 참조 동일성으로 판정한다
        ///    (에셋이 같으면 GUID도 같다. 서로 다른 에셋인데 GUID가 겹치는 경우는 에디터 검사에서 잡는다)
        /// 5) 서로 다른 에셋인데 itemName이 겹침 - 이름 기반 조회가 엉뚱한 아이템을 돌려주는 원인이 된다
        /// </summary>
        /// <param name="context">경고 메시지에 표시할 검사 시점(런타임/에디터)</param>
        public void ValidateEntries(string context)
        {
            if (allItems == null || allItems.Count == 0)
            {
                Debug.LogWarning($"[ItemDataRegistry/{context}] allItems가 비어 있습니다. 레지스트리에 아무 " +
                    "ItemData도 등록돼 있지 않아, 이름으로 아이템을 찾는 경로(세이브 로드 등)가 전부 실패합니다.", this);
                return;
            }

            var seenAssets = new Dictionary<ItemData, int>();
            var seenNames = new Dictionary<string, int>();

            for (int i = 0; i < allItems.Count; i++)
            {
                ItemData item = allItems[i];

                if (item == null)
                {
                    Debug.LogWarning($"[ItemDataRegistry/{context}] allItems[{i}] 가 비어 있습니다(null). " +
                        "삭제된 에셋이거나 채우지 않은 슬롯입니다.", this);
                    continue;
                }

                if (seenAssets.TryGetValue(item, out int firstAssetIndex))
                {
                    Debug.LogWarning($"[ItemDataRegistry/{context}] allItems[{i}] 는 allItems[{firstAssetIndex}] " +
                        $"와 같은 에셋(같은 GUID)입니다: '{item.name}'. 중복 등록을 제거하세요.", this);
                }
                else
                {
                    seenAssets.Add(item, i);
                }

                if (string.IsNullOrWhiteSpace(item.itemName))
                {
                    Debug.LogWarning($"[ItemDataRegistry/{context}] allItems[{i}] ('{item.name}') 의 itemName이 " +
                        "비어 있습니다. 이름으로 조회하는 경로에서 절대 찾을 수 없습니다.", this);
                    continue;
                }

                if (seenNames.TryGetValue(item.itemName, out int firstNameIndex))
                {
                    Debug.LogWarning($"[ItemDataRegistry/{context}] itemName '{item.itemName}' 이 중복됩니다: " +
                        $"allItems[{firstNameIndex}] 와 allItems[{i}]. 이름 조회 시 먼저 등록된 쪽만 쓰이므로 " +
                        "다른 하나는 영영 복원되지 않습니다.", this);
                }
                else
                {
                    seenNames.Add(item.itemName, i);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 인스펙터 값이 바뀔 때마다 같은 검증을 돌린다. 런타임 검사에 더해, 프로젝트에
        /// 존재하는 모든 ItemData 에셋(AssetDatabase 전수 조회)과 대조해 **목록에서 빠진 아이템**을
        /// 이름과 경로까지 찍어 보고한다 - 이것이 B4-5가 잡으려던 "등록 누락"의 본체다.
        /// 에디터 전용이므로 빌드에는 포함되지 않으며, 어떤 경우에도 목록을 자동 수정하지 않는다.
        /// </summary>
        private void OnValidate()
        {
            ValidateEntries("에디터");

            // AssetDatabase 조회는 OnValidate(직렬화 중) 안에서 직접 호출하면 Unity가 거부/경고할 수
            // 있으므로, 직렬화가 끝난 다음 프레임으로 미룬다. 그 사이 에셋이 사라졌을 수 있어 null 검사를 둔다.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    ReportUnregisteredAssets();
            };
        }

        /// <summary>
        /// 프로젝트의 모든 ItemData 에셋 중 allItems에 등록되지 않은 것을 찾아 경고한다.
        /// </summary>
        private void ReportUnregisteredAssets()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemData");
            if (guids == null || guids.Length == 0)
                return;

            var registered = new HashSet<ItemData>();
            if (allItems != null)
            {
                foreach (var item in allItems)
                {
                    if (item != null)
                        registered.Add(item);
                }
            }

            var missing = new List<string>();
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (asset != null && !registered.Contains(asset))
                    missing.Add($"{asset.itemName} ({path})");
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[ItemDataRegistry/에디터] 프로젝트에 있지만 allItems에 등록되지 않은 " +
                    $"ItemData가 {missing.Count}개 있습니다:\n - " + string.Join("\n - ", missing), this);
            }
        }
#endif
    }
}
