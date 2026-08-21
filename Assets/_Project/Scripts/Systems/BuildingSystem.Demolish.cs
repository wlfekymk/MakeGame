using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// BuildingSystem의 철거/환급 partial 분할 파일. 철거 진입점(TryDemolish)과 그 보조들
    /// (IsWallType/HasRoofOnWall/HasLoadAbove/CollectRefundForPiece/CanAcceptRefund/GiveRefund/ResolveItem),
    /// 전용 버퍼 필드(refundBuffer/chestCostBuffer/itemByName)를 BuildingSystem.cs에서
    /// **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링). ResolveItem은 상자 복원
    /// (BuildingSystem.Persistence.cs)에서도 부른다.
    /// </summary>
    public partial class BuildingSystem : MonoBehaviour
    {
        private readonly List<BuildPieceCost> refundBuffer = new List<BuildPieceCost>();

        /// <summary>상자·티어 부품의 철거 반환액을 합산할 때만 쓰는 버퍼(매 프레임 도는 placementCostBuffer와 섞지 않는다).</summary>
        private readonly List<BuildPieceCost> chestCostBuffer = new List<BuildPieceCost>();

        private Dictionary<string, ItemData> itemByName;

        // ────────────────────────────────────────────────────────────────────────
        // 철거
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조준한 조각을 부수고 재료의 **절반(내림)** 을 돌려준다.
        /// · 인벤토리가 가득 차 돌려줄 수 없으면 철거 자체를 취소한다(아이템 증발 금지).
        /// · 위에 다른 조각이 얹혀 있는 바닥은 부술 수 없다(공중에 뜬 벽·계단이 생긴다).
        /// </summary>
        public void TryDemolish()
        {
            Camera cam = GetCamera();
            if (cam == null)
                return;

            Transform camTransform = cam.transform;
            Ray ray = new Ray(camTransform.position, camTransform.forward);
            // 조준한 실물이 있어야만 부순다. 히트가 없을 때 CastBuildRay가 만들어 주는 가상 조준점은
            // 조각 참조가 null이라 여기서 그대로 걸러진다(허공을 우클릭해도 아무 일도 없다).
            if (!CastBuildRay(ray, out _, out _, out PlacedPiece piece, out _, out _, out _) || piece == null)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 내용물이 남은 상자는 부술 수 없다. 부수면서 안의 물건을 쏟아 주는 방법도 있지만, 인벤토리가
            // 가득 차면 결국 어딘가에서 아이템이 사라진다 - "비워야 부술 수 있다"가 유일하게 잃지 않는 규칙이다.
            if (piece.type == BuildPieceType.Chest && piece.chestState != null && piece.chestState.items.Count > 0)
            {
                Debug.LogWarning("[BuildingSystem] 상자에 물건이 남아 있어 부술 수 없다. 상자를 비워야 부술 수 있다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (piece.type == BuildPieceType.Floor && HasLoadAbove(piece))
            {
                Debug.LogWarning("[BuildingSystem] 이 바닥 위에 얹힌 조각이 있어 부술 수 없다. 위쪽부터 철거하라.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 벽 위에 벽을 쌓을 수 있게 됐으니(배치 37) 반대쪽 대칭도 맞춘다 - 아래 벽을 먼저 부수면
            // 위 벽이 허공에 뜬다. 바닥에 이미 적용하던 "위에 얹힌 것이 있으면 못 부순다"와 같은 원칙이다.
            if (IsWallType(piece.type)
                && wallByKey.ContainsKey(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level + 1, piece.axis)))
            {
                Debug.LogWarning("[BuildingSystem] 이 벽 위에 다른 벽이 얹혀 있어 부술 수 없다. 위쪽부터 철거하라.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 지붕도 벽이 받치는 물건이다(배치 38). 같은 원칙 - 아래를 먼저 부수면 지붕이 허공에 뜬다.
            if (IsWallType(piece.type) && HasRoofOnWall(piece))
            {
                Debug.LogWarning("[BuildingSystem] 이 벽 위에 지붕이 얹혀 있어 부술 수 없다. 지붕부터 철거하라.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (!CollectRefundForPiece(piece, refundBuffer))
            {
                Debug.LogWarning("[BuildingSystem] 돌려줄 재료의 ItemData를 찾지 못해 철거를 취소했다" +
                    " (ItemDataRegistry 미배치 가능성).");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (!CanAcceptRefund(refundBuffer))
            {
                Debug.LogWarning("[BuildingSystem] 인벤토리가 가득 차 철거를 취소했다. 반환 재료가 사라지지 않게 하는 조치다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            UnregisterPiece(piece);

            if (piece.go != null)
            {
                // 먼저 끄고 파괴한다 - Destroy는 프레임 끝까지 지연되므로, 그 사이 같은 프레임에
                // 다시 조준/배치하면 이미 없어진 조각의 콜라이더에 레이가 맞는다.
                piece.go.SetActive(false);
                Destroy(piece.go);
            }

            Physics.SyncTransforms();
            GiveRefund(refundBuffer);

            AudioManager.Instance?.PlayBreak();
            Changed?.Invoke();
        }

        /// <summary>벽/문/창문처럼 셀 모서리에 서는 부품인지.</summary>
        private static bool IsWallType(BuildPieceType type)
        {
            return type == BuildPieceType.Wall || type == BuildPieceType.Doorway || type == BuildPieceType.Window;
        }

        /// <summary>
        /// 이 벽 위에 지붕이 얹혀 있는지. 모서리 (ex,ez,axis)는 셀 두 개가 나눠 쓰므로 양쪽 다 본다
        /// (GetEdgeOfCell의 역함수 - axis 0이면 (ex,ez)와 (ex,ez-1), axis 1이면 (ex,ez)와 (ex-1,ez)).
        /// </summary>
        private bool HasRoofOnWall(PlacedPiece wall)
        {
            int roofLevel = wall.level + 1;
            if (HasRoofAt(wall, wall.cellX, wall.cellZ, roofLevel))
                return true;

            int nx = wall.axis == 0 ? wall.cellX : wall.cellX - 1;
            int nz = wall.axis == 0 ? wall.cellZ - 1 : wall.cellZ;
            return HasRoofAt(wall, nx, nz, roofLevel);
        }

        /// <summary>이 바닥이 사라지면 공중에 뜨는 조각이 있는지 확인한다.</summary>
        private bool HasLoadAbove(PlacedPiece floor)
        {
            // 바로 위층 바닥.
            if (floorByKey.ContainsKey(PieceKey(floor.space, floor.keySlot, floor.cellX, floor.cellZ, floor.level + 1, NonWallAxis)))
                return true;

            // 이 바닥만 보고 얹은 지붕(벽 없는 정자 형태). 벽이 함께 받치고 있어도 순서를 지키게 한다.
            if (HasRoofAt(floor, floor.cellX, floor.cellZ, floor.level + 1))
                return true;

            // 이 바닥을 딛고 선 계단.
            if (stairByKey.ContainsKey(PieceKey(floor.space, floor.keySlot, floor.cellX, floor.cellZ, floor.level, NonWallAxis)))
                return true;

            // 이 바닥 위에 앉은 상자. 바닥을 먼저 부수면 상자가 허공에 뜬다 - 계단과 같은 원칙이다.
            // (상자는 비우기 전에는 부술 수도 없으므로, 순서는 "비우기 → 상자 → 바닥"이 된다.)
            if (chestByKey.ContainsKey(PieceKey(floor.space, floor.keySlot, floor.cellX, floor.cellZ, floor.level, NonWallAxis)))
                return true;

            // 이 바닥 위에 선 벽류. 단, 모서리를 함께 쓰는 옆 칸 바닥이 아직 있으면 그쪽이 받쳐 준다.
            for (int side = 0; side < 4; side++)
            {
                GetEdgeOfCell(floor.cellX, floor.cellZ, side, out int ex, out int ez, out int axis);
                if (!wallByKey.ContainsKey(PieceKey(floor.space, floor.keySlot, ex, ez, floor.level, axis)))
                    continue;

                GetNeighborCell(floor.cellX, floor.cellZ, side, out int nx, out int nz);
                if (!HasFloorAt(floor, nx, nz, floor.level))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 조각 하나를 부술 때 돌려줄 재료를 채운다. 등급/티어가 있는 부품은 지금까지 **부어 넣은 전부**
        /// (설치비 + 여기까지 올라온 승급비 전부)를 합산한 뒤 절반을 돌려준다. 그러지 않으면
        /// 특대까지 올린 상자나 대리석까지 올린 벽을 부술 때 승급에 쓴 재료가 통째로 증발한다.
        /// </summary>
        private bool CollectRefundForPiece(PlacedPiece piece, List<BuildPieceCost> buffer)
        {
            chestCostBuffer.Clear();

            if (piece.type == BuildPieceType.Chest)
            {
                int chestTier = piece.chestState != null ? BuildPieceCatalog.ClampChestTier(piece.chestState.tier) : 0;

                // 먼저 원가를 전부 합산한다(줄마다 절반을 먼저 내리면 홀수 줄에서 손해가 누적된다).
                AccumulateCost(chestCostBuffer, BuildPieceCatalog.GetCost(BuildPieceType.Chest));
                for (int t = 0; t < chestTier; t++)
                    AccumulateCost(chestCostBuffer, BuildPieceCatalog.GetChestUpgradeCost(t));
            }
            else
            {
                // [건축 4티어] 1티어 설치비 + 지금 티어까지의 승급비 전부(상자와 완전히 같은 원칙).
                int tier = BuildPieceCatalog.ClampPieceTier(piece.tier);
                AccumulateCost(chestCostBuffer, BuildPieceCatalog.GetCost(piece.type));
                for (int t = 1; t < tier; t++)
                    AccumulateCost(chestCostBuffer, BuildPieceCatalog.GetPieceUpgradeCost(piece.type, t));
            }

            buffer.Clear();
            for (int i = 0; i < chestCostBuffer.Count; i++)
            {
                BuildPieceCost entry = chestCostBuffer[i];
                int back = entry.count / 2;
                if (back <= 0)
                    continue;

                if (ResolveItem(entry.itemName) == null)
                    return false;

                buffer.Add(new BuildPieceCost(entry.itemName, back));
            }

            return true;
        }

        /// <summary>
        /// 반환 재료를 전부 받을 자리가 있는지 확인한다. PlayerInventory.CanAccept는 한 종류씩만
        /// 보므로, 종류가 둘 이상일 때는 필요한 칸을 직접 합산해야 정확하다.
        /// </summary>
        private bool CanAcceptRefund(List<BuildPieceCost> refund)
        {
            if (refund.Count == 0)
                return true;

            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return false;

            int needed = 0;
            for (int i = 0; i < refund.Count; i++)
            {
                ItemData data = ResolveItem(refund[i].itemName);
                if (data == null)
                    return false;

                int max = data.MaxStackSize;
                int have = CountOwned(refund[i].itemName);
                needed += PlayerInventory.SlotsFor(have + refund[i].count, max) - PlayerInventory.SlotsFor(have, max);
            }

            return needed <= inventory.FreeSlots;
        }

        private void GiveRefund(List<BuildPieceCost> refund)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return;

            for (int i = 0; i < refund.Count; i++)
            {
                ItemData data = ResolveItem(refund[i].itemName);
                if (data == null)
                    continue;

                for (int k = 0; k < refund[i].count; k++)
                    inventory.TryAddItem(data);
            }
        }

        /// <summary>
        /// 이름으로 ItemData를 찾는다. 우선순위는 (1) 이미 만든 캐시 (2) 플레이어가 실제로 들고 있는
        /// 아이템 (3) ItemDataRegistry (4) 메모리에 올라온 전수 조회다. 철거할 때만 부르므로
        /// 매 프레임 비용이 아니다.
        /// </summary>
        private ItemData ResolveItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (itemByName == null)
                itemByName = new Dictionary<string, ItemData>();

            if (itemByName.TryGetValue(itemName, out ItemData cached) && cached != null)
                return cached;

            PlayerInventory inventory = Inventory;
            if (inventory != null && inventory.items != null)
            {
                for (int i = 0; i < inventory.items.Count; i++)
                {
                    InventoryItem item = inventory.items[i];
                    if (item == null || item.data == null || item.data.itemName != itemName)
                        continue;

                    itemByName[itemName] = item.data;
                    return item.data;
                }
            }

            ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
            if (registry != null && registry.allItems != null)
            {
                for (int i = 0; i < registry.allItems.Count; i++)
                {
                    ItemData data = registry.allItems[i];
                    if (data == null || data.itemName != itemName)
                        continue;

                    itemByName[itemName] = data;
                    return data;
                }
            }

            var all = Resources.FindObjectsOfTypeAll<ItemData>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].itemName != itemName)
                    continue;

                itemByName[itemName] = all[i];
                return all[i];
            }

            return null;
        }
    }
}
