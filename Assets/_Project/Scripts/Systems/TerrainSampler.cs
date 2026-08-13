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
        /// <summary>
        /// 지정한 위치의 XZ는 그대로 두고, 위에서 아래로 레이캐스트하여 맞은 지점의 y로 보정한 위치를 반환한다.
        /// 아무것도 맞지 않으면(아직 지형이 없는 경우 등) 원래 위치를 그대로 반환한다.
        /// </summary>
        public static Vector3 SnapToGround(Vector3 position, float rayStartHeight = 60f, float rayLength = 120f)
        {
            Vector3 origin = new Vector3(position.x, position.y + rayStartHeight, position.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLength))
                return new Vector3(position.x, hit.point.y, position.z);

            return position;
        }
    }
}
