using System;
using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// 월드 맵에 실제로 생성된 섬 하나의 데이터.
    /// IslandSize(규모 분류)와 달리, 이 클래스는 특정 섬 인스턴스의 위치/발견 여부 등 개별 상태를 담는다.
    /// </summary>
    [Serializable]
    public class IslandInstance
    {
        [Tooltip("섬 고유 번호 (0번은 항상 불시착한 시작 섬)")]
        public int islandId;

        [Tooltip("섬 규모 (소/중/대/특대)")]
        public IslandSize size;

        [Tooltip("월드 맵 상의 위치 (X, Z 평면 좌표. Y는 해수면 기준 0)")]
        public Vector3 mapPosition;

        [Tooltip("플레이어가 이 섬을 발견(시야 확보 또는 방문)했는지 여부")]
        public bool isDiscovered;

        [Tooltip("플레이어가 불시착한 시작 섬인지 여부")]
        public bool isStartingIsland;

        [Tooltip("씬에 배치된 이 섬의 플레이스홀더 오브젝트 (아직 실제 지형 에셋 전이라 배치용 임시 오브젝트)")]
        public GameObject placeholderObject;

        // ── [B54 섬 유형(아키타입)] 왜 여기에 "저장 필드"를 만들지 않았는가 ────────────────
        //
        // 판단: **아키타입은 상태가 아니라 시드의 함수다.** (worldSeed, islandId, size) 셋만 있으면
        // IslandArchetypes.For가 언제든 같은 값을 되돌려주고, 그 셋은 이 클래스와 WorldMapManager가
        // 이미 전부 들고 있다. 그래서 직렬화 필드를 늘리지 않는다:
        //   · 세이브 포맷이 불변이다(SaveData/SaveLoadController를 건드릴 이유가 없다).
        //   · "저장된 아키타입"과 "시드에서 계산한 아키타입"이 어긋날 경로가 원리적으로 없다
        //     (지형 프로파일 shapeProfile을 저장하지 않는 것과 같은 이유 - 그쪽이 선례다).
        //   · 옛 세이브를 열어도 마이그레이션이 필요 없다.
        // 대신 **직렬화되지 않는 캐시**만 둔다. 재계산은 해시 몇 번이라 비용이 아니지만, 다음 웨이브의
        // 암석 피복 배치기가 섬 하나를 훑으며 반복 조회할 것이 예상되므로 자리를 미리 만들어 둔다.
        // 인스턴스 필드라 섬 오브젝트와 수명이 같다 - 정적 캐시가 아니므로 리셋 훅이 필요 없다
        // (RegenerateWorld는 islands 리스트를 통째로 비우고 IslandInstance를 새로 만든다).

        [NonSerialized] private IslandArchetype cachedArchetype;
        [NonSerialized] private int cachedArchetypeSeed;
        [NonSerialized] private bool hasCachedArchetype;

        /// <summary>
        /// 이 섬의 유형(아키타입). worldSeed와 이 인스턴스의 (islandId, size)만으로 결정되는
        /// **순수 해시**라 난수를 소비하지 않고, 같은 시드를 다시 열면 항상 같은 값이 나온다.
        /// worldSeed가 바뀌면(RegenerateWorld) 캐시를 자동으로 무효화한다.
        /// </summary>
        public IslandArchetype GetArchetype(int worldSeed)
        {
            if (!hasCachedArchetype || cachedArchetypeSeed != worldSeed)
            {
                cachedArchetype = IslandArchetypes.For(worldSeed, islandId, size);
                cachedArchetypeSeed = worldSeed;
                hasCachedArchetype = true;
            }
            return cachedArchetype;
        }

        /// <summary>이 섬의 아키타입 파라미터(색·배율·rockCoverage). GetArchetype의 편의 래퍼다.</summary>
        public IslandArchetypeProfile GetArchetypeProfile(int worldSeed)
        {
            return IslandArchetypes.Get(GetArchetype(worldSeed));
        }
    }
}
