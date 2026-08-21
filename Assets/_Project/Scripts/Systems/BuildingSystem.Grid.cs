using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// BuildingSystem의 격자 수학/실내 판정 partial 분할 파일. 격자 계산 static 순수 함수
    /// (CellIndexOf/CellCenterCoord/LevelOf/GetEdgeOfCell/GetNeighborCell/EdgeMidpoint/PieceKey)와
    /// 실내(집) 판정 BFS(IsInsideEnclosedStructure/IsInsideInternal/TryGetFloorUnder/ComputeEnclosed),
    /// 그 전용 필드(enclosureCache/enclosureCacheVersion/bfsCellX/bfsCellZ/bfsVisited)를
    /// BuildingSystem.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 캐시 무효화 기준인 structureVersion은 등록/해제(RegisterPiece 등)가 올리므로 본체에 남아 있다.
    /// </summary>
    public partial class BuildingSystem : MonoBehaviour
    {
        private readonly Dictionary<long, bool> enclosureCache = new Dictionary<long, bool>();
        private int enclosureCacheVersion = -1;

        private readonly List<int> bfsCellX = new List<int>();
        private readonly List<int> bfsCellZ = new List<int>();
        private readonly HashSet<long> bfsVisited = new HashSet<long>();

        // ────────────────────────────────────────────────────────────────────────
        // 실내(집) 판정
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 좌표가 "벽으로 둘러싸이고 머리 위가 덮인 실내"인지 판정한다. Shelter 등 다른 시스템이
        /// 매 프레임 여러 번 부를 수 있어, 결과를 (공간, 칸, 층) 단위로 캐시하고 조각이 바뀔 때만 버린다.
        ///
        /// 판정: 발밑 바닥 칸에서 시작해 **같은 층의 바닥을 따라 퍼져 나가며**, 벽류가 없는 모서리를
        /// 넘어갔을 때 바닥도 없으면 "바깥으로 샌다"로 보고 실외 판정한다. 벽으로 다 막힌 방이면
        /// 탐색이 방 안에서 끝난다. 1x1 오두막부터 여러 칸짜리 방까지 같은 규칙으로 잡히고,
        /// 벽 없는 데크는 첫 걸음에 새므로 즉시 실외가 된다(문·창문은 벽류로 쳐서 막힌 것으로 본다).
        ///
        /// **천장도 요구한다**: 방을 이루는 모든 칸의 바로 위층에 **바닥 또는 지붕**이 있어야 한다
        /// (배치 38 전에는 지붕 부품이 없어 위층 바닥만 천장으로 쳤다). 계단이 선 칸만 예외로 둔다 -
        /// 계단은 위층으로 뚫려 있는 것이 정상이고, 그 구멍 때문에 2층 집 전체가 실외가 되면 안 된다.
        ///
        /// **지면과 뗏목 갑판 양쪽에서 동작한다.** 지면에서 실패하면 좌표를 갑판 로컬로 바꿔 한 번 더 본다.
        ///
        /// [배치 37] 벽이 아래 벽만으로도 설 수 있게 됐지만 **실내 판정은 바뀌지 않는다.** 시작 조건이
        /// TryGetFloorUnder(발밑 바닥)이고 탐색도 바닥이 있는 칸으로만 번지므로, 바닥 없이 벽만 두른
        /// 기둥은 어떤 경우에도 실내가 되지 않는다(비를 피하는 버그가 생기지 않는다).
        ///
        /// [배치 38] 지붕이 천장으로 인정되지만 **바뀐 것은 천장 검사 한 줄뿐이고 두 안전장치는 그대로다.**
        ///  · "바닥 없이 벽만 세운 기둥": 지붕은 roofByKey에만 들어가고 바닥 조회(TryGetFloorTopY)에는
        ///    절대 섞이지 않는다. 시작점 TryGetFloorUnder와 번짐 조건이 여전히 **진짜 바닥**만 보므로,
        ///    바닥 없는 기둥은 지붕을 씌우든 말든 첫 줄에서 실외로 끝난다.
        ///  · "지붕만 덜렁 있고 벽이 없는 경우": 옆면 검사는 그대로다 - 벽류가 없는 모서리를 넘었는데
        ///    그쪽에 바닥도 없으면 곧바로 실외다. 지붕은 옆면을 막지 않으므로 벽 없는 지붕은 첫 걸음에 샌다.
        /// </summary>
        public static bool IsInsideEnclosedStructure(Vector3 worldPos)
        {
            BuildingSystem system = Instance;
            if (system == null)
                return false;

            if (system.IsInsideInternal(BuildSpace.Ground, worldPos))
                return true;

            if (!system.IsDeckReady)
                return false;

            return system.IsInsideInternal(BuildSpace.Deck, worldPos);
        }

        private bool IsInsideInternal(BuildSpace space, Vector3 worldPos)
        {
            if (floorByKey.Count == 0 && space == BuildSpace.Ground)
                return false;

            Vector3 point = WorldToSpace(space, worldPos);
            int cellX = CellIndexOf(point.x);
            int cellZ = CellIndexOf(point.z);

            if (!TryGetFloorUnder(space, cellX, cellZ, point.y, out int level))
                return false;

            if (enclosureCacheVersion != structureVersion)
            {
                enclosureCache.Clear();
                enclosureCacheVersion = structureVersion;
            }

            long key = PieceKey(space, cellX, cellZ, level, NonWallAxis);
            if (enclosureCache.TryGetValue(key, out bool cachedResult))
                return cachedResult;

            bool result = ComputeEnclosed(space, cellX, cellZ, level);
            enclosureCache[key] = result;
            return result;
        }

        /// <summary>이 좌표가 딛고 서 있는 바닥의 층(한 층 안쪽에 있는 것 중 가장 높은 것).</summary>
        private bool TryGetFloorUnder(BuildSpace space, int cellX, int cellZ, float y, out int level)
        {
            int start = LevelOf(y);
            bool found = false;
            float bestY = float.MinValue;
            level = 0;

            for (int d = -1; d <= 1; d++)
            {
                int candidate = start + d;
                if (!TryGetFloorTopY(space, cellX, cellZ, candidate, out float floorY))
                    continue;

                float delta = y - floorY;
                if (delta < -0.3f || delta > LevelHeight)
                    continue;

                if (found && floorY <= bestY)
                    continue;

                found = true;
                bestY = floorY;
                level = candidate;
            }

            return found;
        }

        private bool ComputeEnclosed(BuildSpace space, int cellX, int cellZ, int level)
        {
            bfsCellX.Clear();
            bfsCellZ.Clear();
            bfsVisited.Clear();

            bfsCellX.Add(cellX);
            bfsCellZ.Add(cellZ);
            bfsVisited.Add(PieceKey(space, cellX, cellZ, level, NonWallAxis));

            for (int head = 0; head < bfsCellX.Count; head++)
            {
                if (bfsCellX.Count > MaxEnclosureCells)
                    return false; // 방이라기엔 너무 넓다 - 야외 데크로 본다

                int x = bfsCellX[head];
                int z = bfsCellZ[head];

                // 머리 위(바로 위층의 바닥 **또는 지붕**)가 없으면 실내가 아니다. 계단이 선 칸은 예외다.
                if (!HasCeilingAt(space, x, z, level + 1)
                    && !stairByKey.ContainsKey(PieceKey(space, x, z, level, NonWallAxis)))
                    return false;

                for (int side = 0; side < 4; side++)
                {
                    GetEdgeOfCell(x, z, side, out int ex, out int ez, out int axis);
                    if (wallByKey.ContainsKey(PieceKey(space, ex, ez, level, axis)))
                        continue; // 벽·문·창문이 막고 있다

                    GetNeighborCell(x, z, side, out int nx, out int nz);
                    if (!HasFloorAt(space, nx, nz, level))
                        return false; // 벽도 바닥도 없다 → 바깥으로 샌다

                    long neighborKey = PieceKey(space, nx, nz, level, NonWallAxis);
                    if (bfsVisited.Add(neighborKey))
                    {
                        bfsCellX.Add(nx);
                        bfsCellZ.Add(nz);
                    }
                }
            }

            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 격자 계산
        // ────────────────────────────────────────────────────────────────────────

        private static int CellIndexOf(float coordinate)
        {
            return Mathf.FloorToInt(coordinate / CellSize);
        }

        private static float CellCenterCoord(int index)
        {
            return (index + 0.5f) * CellSize;
        }

        /// <summary>
        /// y를 층 번호로 양자화한다. **Mathf.RoundToInt를 쓰면 안 된다** - 그쪽은 정확히 .5일 때
        /// 짝수로 붙이는(banker's rounding) 규칙이라 1.5→2, 2.5→2 가 되어 층이 단조증가하지 않는다.
        /// 그러면 y와 y+2.5(정확히 한 층 위)가 같은 번호로 접혀 "이미 조각이 있다"로 잘못 막힌다.
        /// 항상 반올림(내림+0.5)을 쓰면 +2.5는 언제나 정확히 +1층이다.
        /// </summary>
        private static int LevelOf(float y)
        {
            return Mathf.FloorToInt(y / LevelHeight + 0.5f);
        }

        /// <summary>
        /// 셀 (x,z)의 side번째 모서리를 canonical 좌표로 돌려준다.
        /// side 0 = -Z · 1 = +Z · 2 = -X · 3 = +X.
        /// </summary>
        private static void GetEdgeOfCell(int x, int z, int side, out int edgeX, out int edgeZ, out int axis)
        {
            switch (side)
            {
                case 0: edgeX = x; edgeZ = z; axis = 0; break;
                case 1: edgeX = x; edgeZ = z + 1; axis = 0; break;
                case 2: edgeX = x; edgeZ = z; axis = 1; break;
                default: edgeX = x + 1; edgeZ = z; axis = 1; break;
            }
        }

        /// <summary>셀 (x,z)의 side번째 모서리 건너편 셀.</summary>
        private static void GetNeighborCell(int x, int z, int side, out int nx, out int nz)
        {
            switch (side)
            {
                case 0: nx = x; nz = z - 1; break;
                case 1: nx = x; nz = z + 1; break;
                case 2: nx = x - 1; nz = z; break;
                default: nx = x + 1; nz = z; break;
            }
        }

        /// <summary>모서리 (ex,ez,axis)의 중점(= 벽 밑면 중심이 놓일 자리).</summary>
        private static Vector3 EdgeMidpoint(int edgeX, int edgeZ, int axis, float y)
        {
            if (axis == 0)
                return new Vector3(CellCenterCoord(edgeX), y, edgeZ * CellSize);

            return new Vector3(edgeX * CellSize, y, CellCenterCoord(edgeZ));
        }

        /// <summary>
        /// (space, raftSlot, x, z, level, axis)를 long 하나로 접는다. x/z 각 21비트(±1,048,575 -
        /// 월드 반경 20,000m를 셀 크기 2로 나눠도 10,000이라 넉넉하다), level 12비트, axis 2비트,
        /// 공간 1비트, **뗏목 번호 6비트**로 총 63비트다(부호 비트는 쓰지 않는다).
        ///
        /// 공간 비트가 있어서 지면 (0,0,0)칸과 갑판 (0,0,0)칸이 섞이지 않고, 뗏목 번호가 있어서
        /// **뗏목 A의 갑판 (0,0)칸과 뗏목 B의 (0,0)칸이 섞이지 않는다.** 뗏목 번호가 없던 시절에는
        /// 둘이 같은 키였고, 그래서 B 갑판에 지으려 하면 A에 있는 조각 때문에 "이미 찼다"가 떴다.
        /// raftSlot은 지면 조각에서는 언제나 0이다.
        /// </summary>
        private static long PieceKey(BuildSpace space, int raftSlot, int x, int z, int level, int axis)
        {
            long kx = (long)(x + 1048576) & 0x1FFFFF;
            long kz = (long)(z + 1048576) & 0x1FFFFF;
            long kl = (long)(level + 512) & 0xFFF;
            long ka = axis & 0x3;
            long ks = (long)space & 0x1;
            long kr = (long)raftSlot & 0x3F;
            return (kr << 57) | (ks << 56) | (kx << 35) | (kz << 14) | (kl << 2) | ka;
        }

        /// <summary>
        /// **지금 조준하고 있는 맥락**의 키(= 결속된 뗏목). 조회 전용이다.
        ///
        /// ★ 등록·해제·세이브 복원처럼 "그 조각의 뗏목"이 따로 정해져 있는 자리에서는 반드시
        ///   6인자 판을 써야 한다. 뭍에 서서 불러오기를 하면 boundRaft가 null이거나 엉뚱한 뗏목이라,
        ///   여기로 키를 만들면 조각이 남의 칸에 등록된다.
        /// </summary>
        private long PieceKey(BuildSpace space, int x, int z, int level, int axis)
        {
            return PieceKey(space, ContextKeySlot(space), x, z, level, axis);
        }

        /// <summary>조준 맥락의 뗏목 번호. 지면이거나 결속된 뗏목이 없으면 0이다.</summary>
        private int ContextKeySlot(BuildSpace space)
        {
            if (space != BuildSpace.Deck)
                return 0;

            return boundRaft != null ? boundRaft.KeySlot : 0;
        }
    }
}
