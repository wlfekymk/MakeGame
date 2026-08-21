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

                    // [뗏목 v4] 갑판 조각의 소속. 지면 조각은 ""라 불러올 때 그대로 지면으로 간다.
                    raftId = piece.raftId,
                });
            }

            // 아직 갑판이 없어 세우지 못한 조각도 그대로 다시 저장한다(불러오기 두 번에 사라지면 안 된다).
            for (int i = 0; i < pendingDeckEntries.Count; i++)
                data.pieces.Add(pendingDeckEntries[i]);

            return JsonUtility.ToJson(data);
        }

        /// <summary>
        /// 저장된 조각을 그대로 되살린다. **어느 경로로 나가든 먼저 표를 비운다** - json이 비었든
        /// 깨졌든, 불러오기는 언제나 "이전 판을 지우고 이 세이브를 세우는" 일이기 때문이다.
        /// (예전에는 빈 json에서 그냥 돌아가 이전 판의 조각 기록이 표에 남았고, 그 기록이 나중에
        /// 같은 RaftId의 뗏목이 서는 순간 유령 구조물로 되살아났다.)
        /// 옛 세이브에는 space 필드가 없어 0(Ground)으로 읽히는데, 그게 정확히 옛 동작이다.
        /// </summary>
        public void RestoreFromJson(string json)
        {
            // ★ 빈 세이브라도 **먼저 비운다.** 예전에는 여기서 그냥 돌아가 이전 판의 조각 기록이
            //   표에 남았다. 실물은 파괴된 뗏목과 함께 사라져 go == null이 되는데, 같은 세이브를
            //   다시 열어 같은 RaftId의 뗏목이 서면 소속 비교가 통과해 유령 구조물이 되살아난다.
            if (string.IsNullOrEmpty(json))
            {
                ClearAllPieces();
                Changed?.Invoke();
                return;
            }

            BuildStructureSaveData data;
            try
            {
                data = JsonUtility.FromJson<BuildStructureSaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildingSystem] 건축 저장 데이터를 읽지 못했다: {e.Message}");
                ClearAllPieces();
                Changed?.Invoke();
                return;
            }

            if (data == null || data.pieces == null)
            {
                ClearAllPieces();
                Changed?.Invoke();
                return;
            }

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

            // [뗏목 v4] 갑판 조각은 **적힌 뗏목**에 되세운다. 결속된 뗏목이 아니다 - 뭍에 서서
            // 불러오면 결속이 없거나 엉뚱한 뗏목이라, 그걸 기준으로 삼으면 집이 남의 배로 간다.
            RaftStructure owner = null;
            if (space == BuildSpace.Deck)
            {
                owner = ResolveSavedRaft(entry.raftId);
                if (owner == null || !owner.HasDeck)
                {
                    pendingDeckEntries.Add(entry);
                    return;
                }
            }

            int ownerSlot = owner != null ? owner.KeySlot : 0;

            bool occupied;
            switch (type)
            {
                case BuildPieceType.Floor:
                    occupied = floorByKey.ContainsKey(PieceKey(space, ownerSlot, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                case BuildPieceType.Stair:
                    occupied = stairByKey.ContainsKey(PieceKey(space, ownerSlot, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                case BuildPieceType.Roof:
                    occupied = roofByKey.ContainsKey(PieceKey(space, ownerSlot, entry.cellX, entry.cellZ, entry.level, NonWallAxis));
                    break;
                default:
                    occupied = wallByKey.ContainsKey(PieceKey(space, ownerSlot, entry.cellX, entry.cellZ, entry.level, entry.axis));
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

            Transform parent = space == BuildSpace.Deck
                ? EnsureDeckContainer(owner, out _)
                : piecesRoot;

            if (parent == null)
            {
                Debug.LogWarning("[BuildingSystem] 조각을 담을 컨테이너를 못 찾아 복원을 건너뛴다.");
                return;
            }

            GameObject go = CreatePieceObject(type, parent, tier);
            if (go == null)
                return;

            // 좌표는 부모의 로컬이다(ApplyPieceTransform이 Deck이면 로컬로 대입한다).
            // 부모를 owner의 컨테이너로 잡아 두었으므로 그대로 제자리에 선다.
            var position = new Vector3(entry.posX, entry.posY, entry.posZ);
            ApplyPieceTransform(go.transform, space, position, entry.yaw);

            PlacedPiece piece = RegisterPiece(type, space, go, entry.cellX, entry.cellZ, entry.level, entry.axis,
                position, entry.yaw, owner);
            piece.tier = tier;
        }

        /// <summary>
        /// 세이브에 적힌 소속 뗏목을 찾는다.
        ///
        /// raftId가 비어 있으면(뗏목 소속을 적기 전 세이브) **대표 뗏목**에 귀속시킨다 - 그 시절에는
        /// 뗏목이 한 대뿐이었으므로 그게 정확히 옛 동작이다.
        ///
        /// raftId가 적혀 있는데 그 뗏목이 사라졌을 때도 **대표 뗏목에 얹는다.** 안 세우고 대기열에
        /// 남기는 편이 얼핏 안전해 보이지만, 그 항목은 영영 해소되지 않는다(자세한 이유는 본문 주석).
        /// </summary>
        private static RaftStructure ResolveSavedRaft(string raftId)
        {
            if (string.IsNullOrEmpty(raftId))
                return RaftStructure.Best;

            RaftStructure raft = RaftStructure.FindById(raftId);
            if (raft != null)
                return raft;

            // 적힌 뗏목이 사라졌다. **대표 뗏목에 얹는다.**
            //
            // null을 돌려 대기열에 남기면 그 항목은 영영 해소되지 않고, 대기열이 비지 않는 한
            // SyncRaftBinding이 매 프레임 flush를 돌린다(라이브락). 실제로 그럴 수 있는 경로가 있다:
            // 저장은 아직 자리를 못 잡은 뗏목을 건너뛰므로(SaveLoadController), 그 뗏목 위에 지어 둔
            // 조각의 raftId는 불러오기 뒤 주인이 없다. 집이 옆 배로 옮겨 앉는 편이 영영 안 나타나거나
            // 프레임마다 재시도하는 것보다 낫다 - 대신 무슨 일이 있었는지 한 줄 남긴다.
            RaftStructure fallback = RaftStructure.Best;
            if (fallback != null)
            {
                Debug.LogWarning("[BuildingSystem] 갑판 조각이 적어 둔 뗏목을 못 찾아" +
                    " 대표 뗏목에 되세운다(그 뗏목이 저장되지 않았을 수 있다).");
            }

            return fallback;
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
                    raftId = piece.raftId,
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
        /// 저장된 보관 상자를 되살린다. 목록이 비어 있으면(= 상자 기능이 없던 옛 세이브) 아무것도
        /// 하지 않는다 - 이 경우 상자를 걷어내는 일은 앞선 RestoreFromJson의 ClearAllPieces가 이미 했다.
        ///
        /// 반드시 RestoreFromJson **뒤에** 불러야 한다: 그쪽이 ClearAllPieces로 격자 표를 비우기 때문에,
        /// 순서가 뒤집히면 방금 세운 상자가 그대로 지워진다.
        /// </summary>
        public void RestoreChests(List<ChestSaveEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            // RestoreFromJson이 이미 표를 통째로 비웠으므로 보통은 할 일이 없다. 그래도 남겨 둔다 -
            // 이 함수를 불러오기 밖에서(예: 상자만 되돌리는 도구) 부르게 되는 날, 상자가 겹쳐 서는
            // 사고를 막는 유일한 방어선이 이 한 줄이다. 비용은 목록 한 번 훑기다.
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

            // [뗏목 v4] 조각과 같은 규칙이다(CreatePieceFromEntry 주석 참고).
            RaftStructure owner = null;
            if (space == BuildSpace.Deck)
            {
                owner = ResolveSavedRaft(entry.raftId);
                if (owner == null || !owner.HasDeck)
                {
                    pendingDeckChests.Add(entry);
                    return;
                }
            }

            int ownerSlot = owner != null ? owner.KeySlot : 0;

            if (chestByKey.ContainsKey(PieceKey(space, ownerSlot, entry.cellX, entry.cellZ, entry.level, NonWallAxis)))
            {
                Debug.LogWarning("[BuildingSystem] 같은 자리에 상자가 둘 저장돼 있어 뒤엣것을 건너뛴다.");
                return;
            }

            int tier = BuildPieceCatalog.ClampChestTier(entry.tier);
            Transform parent = space == BuildSpace.Deck
                ? EnsureDeckContainer(owner, out _)
                : piecesRoot;

            if (parent == null)
            {
                Debug.LogWarning("[BuildingSystem] 상자를 담을 컨테이너를 못 찾아 복원을 건너뛴다.");
                return;
            }

            GameObject go = CreatePieceObject(BuildPieceType.Chest, parent, tier);
            if (go == null)
            {
                Debug.LogWarning("[BuildingSystem] 상자 실물을 만들지 못해 복원을 건너뛴다.");
                return;
            }

            var position = new Vector3(entry.posX, entry.posY, entry.posZ);
            ApplyPieceTransform(go.transform, space, position, entry.yaw);

            PlacedPiece piece = RegisterPiece(BuildPieceType.Chest, space, go, entry.cellX, entry.cellZ,
                entry.level, NonWallAxis, position, entry.yaw, owner);

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
