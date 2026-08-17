using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// BuildingSystem의 보관 상자 partial 분할 파일. 상자 조준(UpdateChestFocus)·배치 자리 판정
    /// (ResolveChestTarget)·등급 변경 겉모습 교체(OnChestTierChanged)·내용물 그릇 결속(AttachChest)을
    /// BuildingSystem.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 상자의 설치·철거 흐름 자체는 공용 경로(TryPlace/TryDemolish)라 BuildingSystem.cs에 남아 있고,
    /// 상자 저장·복원(SerializeChests 등)은 BuildingSystem.Persistence.cs에 있다.
    /// </summary>
    public partial class BuildingSystem : MonoBehaviour
    {

        /// <summary>상자 등급이 바뀌었다. 그 상자의 실물만 새 등급 형상으로 다시 만든다.</summary>
        private void OnChestTierChanged(StorageChest chest)
        {
            if (chest == null)
                return;

            PlacedPiece piece = FindPieceOf(chest.transform);
            if (piece == null || piece.type != BuildPieceType.Chest || piece.go == null)
                return;

            BuildPieceVisualBuilder.RebuildChest(piece.go, chest.Tier);
            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        /// <summary>
        /// 카메라 정면의 상자를 찾아 <see cref="StorageChest.Focused"/>를 갱신한다.
        /// 거리와 판정은 <see cref="InteractionController.TryGetLookTarget"/>과 **같은 규칙**이다
        /// (레이 하나, 상호작용 거리 4m, 가장 가까운 콜라이더 하나만). 그래야 "E가 닿는 것"과
        /// "상자 UI가 여는 것"이 어긋나지 않는다. 상자 실물의 콜라이더는 루트에 붙어 있지만,
        /// 나중에 파츠에 콜라이더가 생겨도 안전하도록 부모를 거슬러 찾는다.
        /// </summary>
        private void UpdateChestFocus()
        {
            Camera cam = GetCamera();
            if (cam == null)
            {
                StorageChest.SetFocused(null);
                return;
            }

            Transform camTransform = cam.transform;
            var ray = new Ray(camTransform.position, camTransform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, ChestFocusDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                StorageChest.SetFocused(null);
                return;
            }

            StorageChest.SetFocused(hit.collider.GetComponentInParent<StorageChest>());
        }

        /// <summary>
        /// 보관 상자의 놓을 자리를 정한다. 상자는 셀 하나를 차지하고 **딛고 선 바닥 위에** 앉는다
        /// (로컬 원점이 밑면이라 position.y가 곧 그 바닥의 윗면이다).
        ///
        /// **지지 판정을 새로 만들지 않는다.** 계단이 쓰는 것과 **완전히 같은** 바닥 조회
        /// (<see cref="FindSupportNear"/>) 하나뿐이다 - 갑판은 그 안에서 0층 바닥으로 취급되므로
        /// 갑판 위에도 바닥 조각 없이 바로 놓인다. 지면(맨땅)에는 놓을 수 없다: 바닥을 깔고 그 위에 둔다.
        ///
        /// 상자는 지지 근거가 되지 않으므로 chestByKey에만 들어가고, 여기서도 다른 부품의 자리를
        /// 빼앗지 않는다(HasCeilingAt을 건드리지 않는다 - 상자 위에 지붕이나 바닥을 얹는 것은 자유다).
        /// </summary>
        private void ResolveChestTarget(BuildSpace space, Vector3 point)
        {
            int cellX = CellIndexOf(point.x);
            int cellZ = CellIndexOf(point.z);

            SupportRef support = FindSupportNear(space, cellX, cellZ, point.y);
            if (!support.valid)
            {
                blockReason = BuildBlockReason.NoSupportingFloor;
                return;
            }

            hasTarget = true;
            targetCellX = cellX;
            targetCellZ = cellZ;
            targetLevel = support.level;
            targetAxis = NonWallAxis;
            targetYaw = GetYawFor(BuildPieceType.Chest, NonWallAxis);
            targetPosition = new Vector3(CellCenterCoord(cellX), support.y, CellCenterCoord(cellZ));

            if (space == BuildSpace.Deck && !IsDeckCellInBounds(cellX, cellZ))
            {
                blockReason = BuildBlockReason.OffDeck;
                return;
            }

            // 한 칸에 상자 하나.
            if (chestByKey.ContainsKey(PieceKey(space, cellX, cellZ, support.level, NonWallAxis)))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            // 계단이 지나가는 칸은 통행로다 - 상자를 놓으면 계단을 오르내릴 수 없게 된다.
            if (stairByKey.ContainsKey(PieceKey(space, cellX, cellZ, support.level, NonWallAxis)))
            {
                blockReason = BuildBlockReason.StairInTheWay;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>
        /// 상자 실물에 StorageChest 컴포넌트를 붙이고 내용물 그릇을 물려준다.
        /// 그릇이 없으면 빈 그릇을 만든다 - **그릇은 조각 기록이 들고 있으므로**, 실물이 다시 만들어져도
        /// 이 함수만 다시 부르면 내용물과 등급이 그대로 이어진다.
        /// </summary>
        private void AttachChest(PlacedPiece piece)
        {
            if (piece == null || piece.type != BuildPieceType.Chest || piece.go == null)
                return;

            if (piece.chestState == null)
                piece.chestState = new StorageChestState();

            StorageChest chest = piece.go.GetComponent<StorageChest>();
            if (chest == null)
                chest = piece.go.AddComponent<StorageChest>();

            chest.Bind(piece.chestState);
            piece.chest = chest;
        }
    }
}
