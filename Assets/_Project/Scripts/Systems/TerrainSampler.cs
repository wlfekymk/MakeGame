using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 특정 XZ 좌표 위쪽에서 아래로 레이를 쏴서 지형(섬 메시) 표면 높이를 찾는 공용 유틸리티.
    /// 섬이 원기둥 플레이스홀더에서 굴곡 있는 절차적 지형으로 바뀌면서, 자원/위험요소/도면 등을
    /// 평평한 y=섬높이 가정으로 배치하면 지형에 파묻히거나 공중에 뜨는 문제가 생겨 이 헬퍼로 보정한다.
    /// </summary>
    public static class TerrainSampler
    {
        // 버그 수정(F9 불러오기 후 오브젝트가 하늘로 떠오르는 문제 - 방어 조치): 예전에는
        // Physics.Raycast(단일 히트, out hit)로 "무엇이든" 먼저 맞으면 그 y를 그대로 썼다. 그런데 자원
        // 노드/위험요소/사냥감/도면은 전부 GameObject.CreatePrimitive로 만들어져 콜라이더가 자동으로
        // 붙는다. WorldMapManager.RegenerateWorld의 Destroy()가 프레임 끝까지 지연 실행되는 동안에는
        // 아직 안 지워진 옛 노드가, 정상 상황에서도 같은 섬 안에서 먼저 생성된 다른 노드가 레이에 맞을
        // 수 있어(생성 순서상 나중 노드가 먼저 노드 위에 얹히는 약한 형태의 같은 버그), 지형이 아닌
        // 오브젝트의 윗면 높이로 잘못 스냅될 수 있었다. 레이가 지나가는 모든 콜라이더를 받아(RaycastAll
        // 계열) "섬 지형"으로 식별되는 히트만 사용하도록 바꿔, 지형이 아닌 오브젝트는 레이가 뚫고 지나가게
        // 한다(WorldMapManager.RegenerateWorld의 SetActive(false) 조치와 함께 근본 원인과 방어를 모두 막는다).
        // 레이어마스크 대신 이름 접두사로 판별하는 이유: 프로젝트에 지형 전용 물리 레이어가 이미 정의돼
        // 있는지 이번 스테이징(ProjectSettings 미포함)만으로는 확인할 수 없고, 새 레이어를 추가하려면
        // ProjectSettings/TagManager.asset 편집이 필요해 이번 작업 범위(.unity/.prefab 편집 금지) 밖이다.
        // 대신 WorldMapManager.SpawnPlaceholder가 절차적 지형이든 지정된 islandPlaceholderPrefab이든
        // 관계없이 항상 배치 오브젝트 이름을 "Island_{id}_{size}"로 붙인다는 기존 명명 규칙이 있어(직접
        // 확인함), 이 접두사만으로 지형 구현 방식과 무관하게 안정적으로 판별할 수 있다. 자원("Resource_"),
        // 위험요소("Hazard_"), 사냥감("Creature_"/"Fish_"), 도면("BoatBlueprint_"), 작업대("BoatWorkbench"),
        // 잔해("AircraftWreck"), 바다("Ocean")는 전부 다른 접두사/이름을 쓰므로 절대 섞이지 않는다.
        private const string TerrainNamePrefix = "Island_";

        // 매 호출마다 배열을 새로 할당(RaycastAll)하지 않도록 재사용하는 버퍼. 한 지점 바로 아래로 겹칠
        // 수 있는 콜라이더 수는 지형 하나 + 혹시 겹친 다른 오브젝트 몇 개 정도면 충분해 32개로 넉넉히 잡았다.
        private static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

        /// <summary>
        /// 지정한 위치의 XZ는 그대로 두고, 위에서 아래로 레이캐스트하여 "섬 지형"에 맞은 지점의 y로
        /// 보정한 위치를 반환한다. 지형이 아닌 다른 오브젝트(자원 노드/위험요소/사냥감 등)에 레이가
        /// 맞아도 무시하고 지나친다. 지형에 아무것도 맞지 않으면(아직 지형이 없는 경우 등) 원래 위치를
        /// 그대로 반환한다.
        /// </summary>
        public static Vector3 SnapToGround(Vector3 position, float rayStartHeight = 60f, float rayLength = 120f)
        {
            Vector3 origin = new Vector3(position.x, position.y + rayStartHeight, position.z);
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, rayLength);

            bool foundTerrain = false;
            float closestDistance = float.MaxValue;
            float groundY = position.y;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (!IsTerrainHit(hit))
                    continue;

                // 지형 히트가 여러 개 잡힐 수 있어도(예: 겹친 섬은 없지만 혹시 모를 경우 대비) 가장
                // 먼저(가장 가까이) 맞은 지형 표면을 지면으로 삼는다 - 기존 단일 Raycast와 동일한 의미.
                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    groundY = hit.point.y;
                    foundTerrain = true;
                }
            }

            return foundTerrain ? new Vector3(position.x, groundY, position.z) : position;
        }

        /// <summary>
        /// 레이가 맞은 콜라이더가 섬 지형인지 판별한다. TerrainNamePrefix 주석 참고.
        /// </summary>
        // 최근에 판정한 콜라이더 몇 개를 기억해 둔다.
        //
        // [왜 필요한가] Unity에서 gameObject.name을 읽으면 **호출마다 새 문자열이 만들어진다.**
        // 이 함수는 뗏목 자리 판정 같은 경로에서 한 프레임에 수십 번 불리는데, 그 히트는 거의
        // 전부 같은 섬 콜라이더 하나다. 링 버퍼 네 칸이면 사실상 다 걸린다.
        //
        // 파괴된 콜라이더가 칸에 남아도 해롭지 않다 - 새 콜라이더와 참조가 같을 수 없어 그냥
        // 안 맞고 밀려날 뿐이다(딕셔너리가 아니라 링이라 자라지도 않는다).
        private const int TerrainCacheSize = 4;
        private static readonly Collider[] terrainCacheKeys = new Collider[TerrainCacheSize];
        private static readonly bool[] terrainCacheValues = new bool[TerrainCacheSize];
        private static int terrainCacheNext;

        private static bool IsTerrainHit(RaycastHit hit)
        {
            Collider collider = hit.collider;
            if (collider == null)
                return false;

            for (int i = 0; i < TerrainCacheSize; i++)
            {
                if (ReferenceEquals(terrainCacheKeys[i], collider))
                    return terrainCacheValues[i];
            }

            bool isTerrain = collider.gameObject.name.StartsWith(
                TerrainNamePrefix, System.StringComparison.Ordinal);

            terrainCacheKeys[terrainCacheNext] = collider;
            terrainCacheValues[terrainCacheNext] = isTerrain;
            terrainCacheNext = (terrainCacheNext + 1) % TerrainCacheSize;

            return isTerrain;
        }
    }
}
