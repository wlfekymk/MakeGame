using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// BuildingSystem의 저장/복원 partial 분할 파일. 건축 조각 JSON(SerializeToJson/RestoreFromJson/
    /// CreatePieceFromEntry)과 보관 상자 목록(SerializeChests/RestoreChests와 그 보조들)을
    /// BuildingSystem.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 저장 항목 클래스(BuildPieceSaveEntry/BuildStructureSaveData)는 BuildingSystem.cs 하단에,
    /// ChestSaveEntry/ChestItemSaveEntry는 SaveData.cs에 그대로 있다.
    /// </summary>
    public partial class BuildingSystem : MonoBehaviour
    {

        // ────────────────────────────────────────────────────────────────────────
        // 저장 / 복원 (SaveLoadController가 buildStructureJson 한 칸에 배선했다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 놓여 있는 조각 전부를 JSON으로 만든다. **빈 상태면 ""를 돌려준다** - 세이브 파일에
        /// 의미 없는 빈 객체가 들어가지 않게 하고, 호출부가 "건축 기록 없음"을 문자열 하나로 판정한다.
        /// 갑판 위 조각은 좌표가 전부 **뗏목 로컬**이라 뗏목이 어디로 떠내려간 뒤에 불러와도 어긋나지 않는다.
        /// </summary>
        public string SerializeToJson()
        {
            // 상자만 있고 다른 조각이 하나도 없으면 ""가 나가는데, 그것이 맞다 - 상자는 별도 목록으로 저장된다.
            if (pieces.Count == 0 && pendingDeckEntries.Count == 0)
                return "";

            var data = new BuildStructureSaveData();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedPiece piece = pieces[i];

                // 보관 상자는 이 목록에 넣지 않는다. 등급과 내용물까지 실어야 해서 SaveData.storageChests
                // 라는 별도 목록으로 나가며(SerializeChests), 양쪽에 다 쓰면 불러올 때 상자가 둘이 된다.
                if (piece.type == BuildPieceType.Chest)
                    continue;

                data.pieces.Add(new BuildPieceSaveEntry
                {
                    type = (int)piece.type,
                    space = (int)piece.space,
                    cellX = piece.cellX,
                    cellZ = piece.cellZ,
                    level = piece.level,
                    axis = piece.axis,
                    posX = piece.position.x,
                    posY = piece.position.y,
                    posZ = piece.position.z,
                    yaw = piece.yaw,
                    // [건축 4티어] 1~4로 잘라 저장한다(0은 "옛 세이브"의 표식이므로 새 세이브에 쓰지 않는다).
                    tier = BuildPieceCatalog.ClampPieceTier(piece.tier),
                });
            }

            // 아직 갑판이 없어 세우지 못한 조각도 그대로 다시 저장한다(불러오기 두 번에 사라지면 안 된다).
            for (int i = 0; i < pendingDeckEntries.Count; i++)
                data.pieces.Add(pendingDeckEntries[i]);

            return JsonUtility.ToJson(data);
        }

        /// <summary>
        /// 저장된 조각을 그대로 되살린다. json이 ""/null이면 **아무것도 하지 않는다**(건축 기능이
        /// 없던 시절의 옛 세이브 호환 - 지금 지어 둔 것을 지우지도 않는다).
        /// 옛 세이브에는 space 필드가 없어 0(Ground)으로 읽히는데, 그게 정확히 옛 동작이다.
        /// </summary>
        public void RestoreFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            BuildStructureSaveData data;
            try
            {
                data = JsonUtility.FromJson<BuildStructureSaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildingSystem] 건축 저장 데이터를 읽지 못했다: {e.Message}");
                return;
            }

            if (data == null || data.pieces == null)
                return;

            ClearAllPieces();

            // 뗏목이 이미 서 있으면 이 자리에서 갑판 조각까지 세운다. 아직이면(불러오기 순서상 뗏목이
            // 나중에 만들어지는 경우) 대기열에 넣고 SyncRaftBinding이 갑판을 잡는 순간 세운다.
            SyncRaftBinding();

            for (int i = 0; i < data.pieces.Count; i++)
                CreatePieceFromEntry(data.pieces[i]);

            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        /// <summary>저장 항목 하나를 실제 조각으로 세운다. 갑판이 아직 없으면 대기열에 넣는다.</summary>
        private void CreatePieceFromEntry(BuildPieceSaveEntry entry)
        {
            if (entry == null)
                return;

            // 상한은 **지붕(5)** 이다. 옛 세이브(0~4)는 그대로 통과하고, 지붕만 더 읽힌다.
            // 상자(6)는 이 목록에 저장되지 않으므로(SerializeToJson) 여기 들어오면 잘못 만들어진
            // 세이브다 - 조용히 넘기지 않고 사유를 남긴다.
            if (entry.type == (int)BuildPieceType.Chest)
            {
                Debug.LogWarning("[BuildingSystem] 건축 조각 목록에 보관 상자가 들어 있어 건너뛴다" +
                    " (상자는 SaveData.storageChests로 복원된다).");
                return;
            }

            if (entry.type < (int)BuildPieceType.Floor || entry.type > (int)BuildPieceType.Roof)
            {
                Debug.LogWarning($"[BuildingSystem] 알 수 없는 부품 종류 {entry.type} 를 건너뛴다.");
                return;
            }

            var type = (BuildPieceType)entry.type;
            var space = entry.space == (int)BuildSpace.Deck ? BuildSpace.Deck : BuildSpace.Ground;

            if (space == BuildSpace.Deck && !IsDeckReady)
            {
                pendingDeckEntries.Add(entry);
                return;
            }

            bool occupied;
            switch (type)
            {
                case BuildPieceType.Floor:
                    occupied = floorByKey.ContainsKey(PieceKey(space, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                case BuildPieceType.Stair:
                    occupied = stairByKey.ContainsKey(PieceKey(space, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                case BuildPieceType.Roof:
                    occupied = roofByKey.ContainsKey(PieceKey(space, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                default:
                    occupied = wallByKey.ContainsKey(PieceKey(space, entry.cellX, entry.cellZ, entry.level, entry.axis));
                    break;
            }

            if (occupied)
            {
                Debug.LogWarning("[BuildingSystem] 같은 자리에 조각이 둘 저장돼 있어 뒤엣것을 건너뛴다.");
                return;
            }

            // [건축 4티어] tier 필드가 없던 옛 세이브는 0으로 읽히고, ClampPieceTier가 0을 1(나무)로
            // 해석한다 - 옛 세이브의 부품은 전부 1티어로 그대로 복원된다(하위호환의 핵심).
            int tier = BuildPieceCatalog.ClampPieceTier(entry.tier);

            Transform parent = space == BuildSpace.Deck ? deckContainer : piecesRoot;
            GameObject go = CreatePieceObject(type, parent, tier);
            if (go == null)
                return;


            var position = new Vector3(entry.posX, entry.posY, entry.posZ);
            ApplyPieceTransform(go.transform, space, position, entry.yaw);

            PlacedPiece piece = RegisterPiece(type, space, go, entry.cellX, entry.cellZ, entry.level, entry.axis,
                position, entry.yaw);
            piece.tier = tier;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 보관 상자 저장 / 복원 (SaveData.storageChests 한 칸에 배선한다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 놓여 있는 보관 상자 전부를 저장 항목으로 옮겨 담는다(buffer는 내부에서 Clear된다).
        /// 상자는 건축 조각 JSON에 넣지 않는다 - 등급과 내용물까지 실어야 해서 별도 목록으로 나간다.
        /// 갑판 위 상자의 좌표는 조각과 마찬가지로 **뗏목 로컬**이라 뗏목이 떠내려간 뒤에 불러와도 맞다.
        /// </summary>
        public void SerializeChests(List<ChestSaveEntry> buffer)
        {
            if (buffer == null)
                return;

            buffer.Clear();

            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedPiece piece = pieces[i];
                if (piece.type != BuildPieceType.Chest)
                    continue;

                var entry = new ChestSaveEntry
                {
                    space = (int)piece.space,
                    cellX = piece.cellX,
                    cellZ = piece.cellZ,
                    level = piece.level,
                    posX = piece.position.x,
                    posY = piece.position.y,
                    posZ = piece.position.z,
                    yaw = piece.yaw,
                    tier = piece.chestState != null ? BuildPieceCatalog.ClampChestTier(piece.chestState.tier) : 0,
                };

                if (piece.chestState != null)
                    AppendChestItems(piece.chestState, entry.items);

                buffer.Add(entry);
            }

            // 갑판이 없어 아직 세우지 못한 상자도 그대로 다시 저장한다(불러오기 두 번에 사라지면 안 된다).
            for (int i = 0; i < pendingDeckChests.Count; i++)
                buffer.Add(pendingDeckChests[i]);
        }

        /// <summary>
        /// 상자의 평면 목록(1개 = 1항목)을 이름 + 개수 + 남은 사용 횟수로 접어 담는다.
        /// **남은 사용 횟수가 다르면 접지 않는다** - 반쯤 닳은 손도끼와 새 손도끼를 한 줄로 합치면
        /// 복원할 때 내구도가 한 값으로 뭉개진다(인벤토리 세이브가 같은 이유로 항목을 나눠 둔다).
        /// </summary>
        private static void AppendChestItems(StorageChestState state, List<ChestItemSaveEntry> target)
        {
            List<InventoryItem> items = state.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data == null || string.IsNullOrEmpty(item.data.itemName))
                    continue;

                bool merged = false;
                for (int k = 0; k < target.Count; k++)
                {
                    if (target[k].itemName != item.data.itemName || target[k].remainingUses != item.remainingUses)
                        continue;

                    target[k].count++;
                    merged = true;
                    break;
                }

                if (!merged)
                {
                    target.Add(new ChestItemSaveEntry
                    {
                        itemName = item.data.itemName,
                        count = 1,
                        remainingUses = item.remainingUses,
                    });
                }
            }
        }

        /// <summary>
        /// 저장된 보관 상자를 되살린다. 목록이 비어 있으면(= 상자 기능이 없던 옛 세이브) **아무것도 하지
        /// 않는다** - 지금 지어 둔 상자를 지우지도 않는다. 건축 조각 복원(RestoreFromJson)이 ""를 만났을
        /// 때와 완전히 같은 규칙이다.
        ///
        /// 반드시 RestoreFromJson **뒤에** 불러야 한다: 그쪽이 ClearAllPieces로 격자 표를 비우기 때문에,
        /// 순서가 뒤집히면 방금 세운 상자가 그대로 지워진다.
        /// </summary>
        public void RestoreChests(List<ChestSaveEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            // 건축 조각이 하나도 없는 세이브(buildStructureJson == "")는 RestoreFromJson이 일찍 돌아가
            // 표를 비우지 않는다. 그 경우에도 상자가 겹쳐 서지 않도록 여기서 상자만 따로 걷어낸다.
            RemoveAllChests();

            SyncRaftBinding();

            for (int i = 0; i < entries.Count; i++)
                CreateChestFromEntry(entries[i]);

            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        /// <summary>놓여 있는 상자를 전부 걷어낸다(복원 직전 정리 전용 - 내용물 검사를 하지 않는다).</summary>
        private void RemoveAllChests()
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                PlacedPiece piece = pieces[i];
                if (piece.type != BuildPieceType.Chest)
                    continue;

                UnregisterPiece(piece);

                if (piece.go != null)
                {
                    piece.go.SetActive(false);
                    Destroy(piece.go);
                }
            }

            pendingDeckChests.Clear();
            StorageChest.SetFocused(null);
        }

        /// <summary>저장 항목 하나를 실제 상자로 세운다. 갑판이 아직 없으면 대기열에 넣는다.</summary>
        private void CreateChestFromEntry(ChestSaveEntry entry)
        {
            if (entry == null)
                return;

            var space = entry.space == (int)BuildSpace.Deck ? BuildSpace.Deck : BuildSpace.Ground;

            if (space == BuildSpace.Deck && !IsDeckReady)
            {
                pendingDeckChests.Add(entry);
                return;
            }

            if (chestByKey.ContainsKey(PieceKey(space, entry.cellX, entry.cellZ, entry.level, NonWallAxis)))
            {
                Debug.LogWarning("[BuildingSystem] 같은 자리에 상자가 둘 저장돼 있어 뒤엣것을 건너뛴다.");
                return;
            }

            int tier = BuildPieceCatalog.ClampChestTier(entry.tier);
            Transform parent = space == BuildSpace.Deck ? deckContainer : piecesRoot;
            GameObject go = CreatePieceObject(BuildPieceType.Chest, parent, tier);
            if (go == null)
            {
                Debug.LogWarning("[BuildingSystem] 상자 실물을 만들지 못해 복원을 건너뛴다.");
                return;
            }

            var position = new Vector3(entry.posX, entry.posY, entry.posZ);
            ApplyPieceTransform(go.transform, space, position, entry.yaw);

            PlacedPiece piece = RegisterPiece(BuildPieceType.Chest, space, go, entry.cellX, entry.cellZ,
                entry.level, NonWallAxis, position, entry.yaw);

            piece.chestState = new StorageChestState { tier = tier };
            AttachChest(piece);

            FillChestFromEntry(piece, entry);
        }

        /// <summary>
        /// 저장된 내용물을 상자에 채운다. **이름으로 ItemData를 찾지 못한 항목은 조용히 버리지 않는다** -
        /// 몇 개를 못 살렸는지 이름과 함께 경고로 남긴다(ItemDataRegistry 등록 누락을 눈으로 잡기 위해서다).
        /// 용량 검사는 하지 않는다: 상한을 넘긴 기록에서 넘치는 만큼을 버리면 플레이어의 물건이 사라진다.
        /// </summary>
        private void FillChestFromEntry(PlacedPiece piece, ChestSaveEntry entry)
        {
            if (piece.chest == null || entry.items == null)
                return;

            int lost = 0;
            string lostNames = null;

            for (int i = 0; i < entry.items.Count; i++)
            {
                ChestItemSaveEntry saved = entry.items[i];
                if (saved == null || string.IsNullOrEmpty(saved.itemName))
                    continue;

                int count = saved.Count;
                ItemData data = ResolveItem(saved.itemName);
                if (data == null)
                {
                    lost += count;
                    lostNames = lostNames == null ? saved.itemName : lostNames + ", " + saved.itemName;
                    continue;
                }

                for (int k = 0; k < count; k++)
                    piece.chest.AddItemIgnoringCapacity(data, saved.remainingUses);
            }

            piece.chest.NotifyChanged();

            if (lost > 0)
            {
                Debug.LogWarning($"[BuildingSystem] 상자 복원 중 이름으로 ItemData를 찾지 못해 {lost}개를 " +
                    $"되살리지 못했다(종류: {lostNames}). ItemDataRegistry 등록을 확인하라.");
            }

            if (piece.chest.UsedSlots > piece.chest.SlotCapacity)
            {
                Debug.LogWarning($"[BuildingSystem] 복원된 상자가 칸 상한을 넘었다" +
                    $" ({piece.chest.UsedSlots}/{piece.chest.SlotCapacity}칸). 물건은 그대로 두고 새로 넣는 것만 막힌다.");
            }
        }
    }
}
