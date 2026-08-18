using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// Shelter의 비주얼 생성 부분(partial). Shelter.cs에서 그대로 옮겨 왔다 - 동작/코드 내용 불변.
    /// </summary>
    public partial class Shelter
    {
        /// <summary>
        /// 설치 직후 루트(지붕)를 roofHeight만큼 들어올리고, 바닥까지 닿는 기둥 4개를 절차적으로 붙여
        /// 판자 한 장뿐이던 플레이스홀더를 실제 쉼터처럼 보이게 만든다.
        /// </summary>
        private void Awake()
        {
            transform.position += Vector3.up * roofHeight;
            BuildVisual();
        }

        /// <summary>
        /// 지붕 색을 이엉(초가) 색으로 바꾸고, 스케일이 비균일한 루트(지붕 Plane) 아래에
        /// 스케일 영향을 받지 않는 보정용 빈 오브젝트를 하나 만들어 그 밑에 기둥 4개를 붙인다.
        ///
        /// [정착 배치 1] Awake 전용이 아니라 **언제든 다시 부를 수 있는 재빌드 메서드**다
        /// (Design_Settlement 2-2: 승급은 비주얼 재빌드 분리가 전제). 여러 번 불러도 안전하도록
        /// 이전에 만든 VisualParts를 먼저 지운다. Destroy는 프레임 끝까지 지연되므로
        /// SetActive(false)로 즉시 화면/물리에서 빼고 참조를 끊는다(AGENT_BRIEF 4장).
        /// 루트 위치는 여기서 건드리지 않는다 - 위치 보정은 Awake 1회뿐이다(재빌드마다 또 올리면
        /// 지붕이 계속 떠오른다. SaveLoadController.RestoreShelter의 roofHeight 보정과 같은 함정).
        /// </summary>
        public void BuildVisual()
        {
            if (visualParts != null)
            {
                visualParts.SetActive(false);
                Destroy(visualParts);
                visualParts = null;
            }

            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                rootRenderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(RoofColorForLevel());

            // 루트가 지붕용으로 (4, 0.3, 4) 비균일 스케일되어 있어, 그 스케일을 상쇄하는 빈 부모를 만든다.
            visualParts = new GameObject("VisualParts");
            visualParts.transform.SetParent(transform, false);
            Vector3 parentScale = transform.localScale;
            visualParts.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);

            Vector3[] legOffsets =
            {
                new Vector3(1.6f, 0f, 1.6f),
                new Vector3(-1.6f, 0f, 1.6f),
                new Vector3(1.6f, 0f, -1.6f),
                new Vector3(-1.6f, 0f, -1.6f),
            };

            // [tech-artist-B 요청 - 인공물 시각 언어] 매끈한 원기둥 다리는 야자수 줄기/대나무 자원과 같은
            // 형태 언어라 "내가 지은 것"으로 읽히지 않는다(ArtDirection 2장 4번). 각진 사각 기둥 + 밧줄
            // 결속으로 바꾼다. 높이 인자가 원기둥과 다르다는 점에 주의: 원기둥 메시는 높이가 2단위라
            // scale.y에 절반(roofHeight * 0.5)을 넣어야 했지만, CreateLashedPost는 큐브라 실제 높이를
            // 그대로 받는다 - 그래서 roofHeight를 넘긴다(중심은 지붕 아래 roofHeight/2로 동일하므로
            // 기둥이 바닥~지붕을 정확히 잇는 결과도 이전과 완전히 같다).
            foreach (var offset in legOffsets)
            {
                StructureVisualBuilder.CreateLashedPost(visualParts.transform, "Leg",
                    offset + Vector3.down * (roofHeight * 0.5f), roofHeight, 0.12f, new Color(0.35f, 0.22f, 0.1f));
            }

            if (level >= 2)
                BuildWalls();

            if (level >= 3)
                BuildUpperDeck();

            if (level >= SlotUnlockLevel)
                BuildSlotVisuals();
        }

        /// <summary>승급으로 새로 만들어진 비주얼 파츠들의 부모. 재빌드 시 통째로 지운다.</summary>
        private GameObject visualParts;

        /// <summary>레벨이 올라갈수록 지붕이 더 잘 손질된 색으로 바뀐다(형태 변화는 벽/데크가 담당).</summary>
        private Color RoofColorForLevel()
        {
            if (level >= 3) return new Color(0.62f, 0.47f, 0.26f);
            if (level >= 2) return new Color(0.58f, 0.44f, 0.24f);
            return new Color(0.55f, 0.42f, 0.22f);
        }

        /// <summary>
        /// Lv2 이상의 벽 3면. 앞면(z+)은 문 슬롯 자리라 비워 두고 BuildSlotVisuals가 채운다.
        /// 콜라이더는 붙지 않는다(StructureVisualBuilder.CreateVisualPart가 제거한다) - 시각 신호만이
        /// 목적이고, 벽으로 플레이어를 가두면 "실내"가 아니라 버그가 된다.
        /// </summary>
        private void BuildWalls()
        {
            float wallHeight = roofHeight * 0.9f;
            float wallCenterY = -roofHeight * 0.55f;
            Color wallColor = new Color(0.47f, 0.36f, 0.2f);

            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Back", PrimitiveType.Cube,
                new Vector3(0f, wallCenterY, -1.6f), new Vector3(3.2f, wallHeight, 0.12f), wallColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Left", PrimitiveType.Cube,
                new Vector3(-1.6f, wallCenterY, 0f), new Vector3(0.12f, wallHeight, 3.2f), wallColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Right", PrimitiveType.Cube,
                new Vector3(1.6f, wallCenterY, 0f), new Vector3(0.12f, wallHeight, 3.2f), wallColor, null, "wood");
        }

        /// <summary>Lv3의 2층 전망 데크. 지붕 위 평상 + 난간 2개로 최소 파츠만 쓴다.</summary>
        private void BuildUpperDeck()
        {
            Color deckColor = new Color(0.52f, 0.4f, 0.22f);

            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "UpperDeck", PrimitiveType.Cube,
                new Vector3(0f, 0.35f, 0f), new Vector3(2.6f, 0.12f, 2.6f), deckColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DeckRail_Back", PrimitiveType.Cube,
                new Vector3(0f, 0.62f, -1.25f), new Vector3(2.6f, 0.42f, 0.08f), deckColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DeckRail_Left", PrimitiveType.Cube,
                new Vector3(-1.25f, 0.62f, 0f), new Vector3(0.08f, 0.42f, 2.6f), deckColor, null, "wood");
        }

        /// <summary>
        /// 슬롯 3개의 비주얼. **비어 있는 슬롯도 반드시 그린다** - 빈 슬롯이 곧 할 일 목록이라는 것이
        /// Design_Settlement 1장의 축이다(퀘스트 시스템 0줄로 "할 일이 하나 더 있다"가 성립한다).
        /// 비어 있으면 어두운 자리 표시(빈 문틀 / 빈 바닥 자국), 설치되면 실제 가구가 된다.
        /// </summary>
        private void BuildSlotVisuals()
        {
            float floorY = -roofHeight;
            Color emptyColor = new Color(0.28f, 0.24f, 0.2f);

            // 문(앞면 z+): 비었으면 문틀만, 설치되면 문짝이 채워진다.
            float doorHeight = roofHeight * 0.9f;
            float doorCenterY = -roofHeight * 0.55f;
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorFrame_L", PrimitiveType.Cube,
                new Vector3(-0.75f, doorCenterY, 1.6f), new Vector3(0.14f, doorHeight, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorFrame_R", PrimitiveType.Cube,
                new Vector3(0.75f, doorCenterY, 1.6f), new Vector3(0.14f, doorHeight, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorLintel", PrimitiveType.Cube,
                new Vector3(0f, doorCenterY + doorHeight * 0.5f, 1.6f), new Vector3(1.7f, 0.12f, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");

            if (HasSlot(SlotDoor))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Door", PrimitiveType.Cube,
                    new Vector3(0f, doorCenterY, 1.6f), new Vector3(1.4f, doorHeight * 0.92f, 0.08f),
                    new Color(0.5f, 0.38f, 0.2f), null, "wood");
            }

            // 침상(안쪽 왼편).
            if (HasSlot(SlotBed))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_Frame", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.18f, -0.85f), new Vector3(1.5f, 0.24f, 0.9f),
                    new Color(0.45f, 0.34f, 0.19f), null, "wood");
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_Mat", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.33f, -0.85f), new Vector3(1.4f, 0.1f, 0.82f),
                    StructureVisualBuilder.PalmFiber, null, "leaf");
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_EmptySlot", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.03f, -0.85f), new Vector3(1.5f, 0.05f, 0.9f),
                    emptyColor, null, "sand");
            }

            // 저장궤(안쪽 오른편).
            if (HasSlot(SlotChest))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_Body", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.28f, -0.9f), new Vector3(0.9f, 0.55f, 0.7f),
                    new Color(0.48f, 0.36f, 0.2f), null, "wood");
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_Lid", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.59f, -0.9f), new Vector3(0.95f, 0.1f, 0.75f),
                    StructureVisualBuilder.PalmFiber, null, "leaf");
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_EmptySlot", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.03f, -0.9f), new Vector3(0.9f, 0.05f, 0.7f),
                    emptyColor, null, "sand");
            }
        }
    }
}
