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
    }
}
