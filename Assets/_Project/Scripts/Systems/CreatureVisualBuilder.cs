using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 위험 요소(HazardSource)/사냥감(HuntableCreature)처럼 "살아 있는 것"으로 읽혀야 하는 오브젝트의
    /// 외형을 절차적으로 만드는 공용 유틸리티.
    ///
    /// ── [B29] 방식 전환: 파츠를 덧붙이지 않고 메시에 구워 넣는다 ─────────────────────────────
    /// B28에서 자원 노드(대나무·나뭇가지·야자잎·돌조각)에 적용해 검증된 방식을 그대로 가져온다
    /// (IslandResourceSpawner.ResourceVisualLibrary 주석 참고). 예전 이 클래스는 몸통 프리미티브 하나에
    /// 구체/박스 파츠를 얹어 귀·주둥이·집게·가시를 흉내 냈는데, 그 방식은 세 가지가 동시에 나빴다:
    ///   (1) 형태 - 프리미티브 몇 개를 겹친 실루엣은 어느 각도에서 봐도 "덩어리 몇 개"였다.
    ///       곰은 서 있는 캡슐에 귀 두 개가 붙은 것이라 옆에서 봐도 곰으로 읽히지 않았다.
    ///   (2) 파츠 수 - 함정은 원판 1 + 가시 8 = 9개, 육상 사냥감은 8개였다. 섬 하나에 위험 요소가
    ///       최대 16마리 + 사냥감이 10마리 깔리므로 곧바로 드로우콜 수백 개다.
    ///   (3) 머티리얼 - 파츠 하나당 StructureVisualBuilder.CreateColorMaterial이 new Material을 하나씩
    ///       만들어서, 특대 섬 한 곳의 위험 요소/사냥감만으로 머티리얼이 약 180개 생겼다
    ///       (AGENT_BRIEF 4장 "섬당 400개면 SRP 배처가 죽는다").
    /// 지금은 몸통 자체를 절차 메시로 갈아 끼운다(CreatureMeshLibrary). 곰의 어깨 혹은 파츠가 아니라
    /// 등줄기 반지름이 부풀어 오른 것이고, 전갈 다리 8개·집게·꼬리는 전부 몸통 메시 안에 있다.
    /// 메시와 머티리얼은 월드 전체가 공유한다(정적 캐시) - 개체가 몇 마리든 메시는 종류당 1장이다.
    ///
    /// ── 좌표계 규칙(이 파일에서 사고가 나는 유일한 지점) ─────────────────────────────────
    /// 스포너가 만드는 몸통 프리미티브는 **비균일 localScale**을 갖고, 일부는 눕혀져 있다
    /// (독사/전갈/상어는 rotationEuler(0,0,90) → 로컬 +X = 월드 위, +Y = 몸통 진행 방향, +Z = 좌우).
    /// 그래서 이 파일의 모든 절차 메시는 **미터로 작성한 뒤 마지막에 NominalScale로 나눈다**
    /// (Builder.ScaleVertices). 결과적으로 메시 좌표는 프리미티브 로컬 규격 안에 들어가고,
    /// 월드 크기는 정확히 작성한 미터값 × sizeJitter가 된다.
    /// 자식 파츠도 같은 규칙이다: localScale = 1/NominalScale 로 두면 그 자식의 로컬 1단위가 1미터가
    /// 되고(=미터 공간), 개체별 sizeJitter는 그대로 살아 있다. 자식에 회전을 주지 않으므로
    /// 비균일 부모 × 자식 회전 = 전단(shear) 사고가 구조적으로 발생할 수 없다.
    ///
    /// ⚠️ NominalScale 상수는 HazardSpawner.GetVisualConfig / CreatureSpawner.SpawnSingleCreature의
    /// localScale과 **반드시 같아야 한다**. 한쪽만 바뀌면 형태가 조용히 늘어나거나 눌린다.
    /// 콜라이더(=전투/접촉 판정)는 어느 경로에서도 건드리지 않는다 - 메시 교체는 MeshFilter만 바꾸고,
    /// 프리미티브 콜라이더는 파라메트릭이라 메시와 무관하다. 파츠에는 콜라이더가 없다.
    /// </summary>
    public static class CreatureVisualBuilder
    {
        // ── 스포너가 쓰는 몸통 localScale (jitter 이전 원본값) ────────────────────────────
        // HazardSpawner.GetVisualConfig / CreatureSpawner.SpawnSingleCreature와 같은 값이어야 한다.
        private static readonly Vector3 BearScale = new Vector3(0.9f, 1.1f, 0.9f);
        private static readonly Vector3 CannibalScale = new Vector3(0.55f, 0.9f, 0.55f);
        private static readonly Vector3 SnakeScale = new Vector3(0.18f, 0.6f, 0.18f);
        private static readonly Vector3 ScorpionScale = new Vector3(0.16f, 0.3f, 0.16f);
        private static readonly Vector3 SharkScale = new Vector3(0.45f, 1.4f, 0.45f);
        private static readonly Vector3 TrapScale = new Vector3(0.6f, 0.04f, 0.6f);
        private static readonly Vector3 BeeSwarmScale = new Vector3(0.5f, 0.5f, 0.5f);
        private static readonly Vector3 HuntLandScale = new Vector3(0.45f, 0.6f, 0.45f);
        private static readonly Vector3 HuntFishScale = new Vector3(0.35f, 0.2f, 0.5f);

        // ── [B30] 게(대왕 크랩 / 소형 크랩)가 쓰는 몸통 규격 ──────────────────────────────
        // 위의 다른 규격들과 달리 **public**이다. 대왕 크랩은 HazardSpawner가, 소형 크랩은
        // CreatureSpawner가 각각 몸통 프리미티브를 만드는데, 두 스포너가 서로 다른 파일이라
        // 숫자를 각자 적어 두면 한쪽만 바뀌었을 때 게가 조용히 늘어나거나 눌린다(이 파일 상단
        // ⚠️ 주석의 사고 유형 그대로다). 스포너가 이 상수를 **직접 참조**하면 그 사고가 원천 봉쇄된다.
        //
        //   대왕: GameObject.CreatePrimitive(PrimitiveType.Cube)
        //         localScale = CrabGiantBodyScale * sizeJitter
        //         position   = 지면 + Vector3.up * CrabGiantGroundOffset  (HazardSpawner의 기존 규칙 그대로.
        //                      jitter 보정은 BuildCrabBody가 안에서 처리한다)
        //   소형: GameObject.CreatePrimitive(PrimitiveType.Sphere)
        //         localScale = CrabSmallBodyScale * sizeJitter
        //         position   = 지면 + Vector3.up * (CrabSmallGroundOffset * sizeJitter)
        //                      (물고기/육상 사냥감과 같은 접지 규칙 - CreatureSpawner.cs:144·151)
        /// <summary>대왕 크랩 몸통 프리미티브(큐브)의 localScale. HazardSpawner.GetVisualConfig와 같은 값이어야 한다.</summary>
        public static readonly Vector3 CrabGiantBodyScale = new Vector3(1.6f, 0.9f, 1.4f);

        /// <summary>소형 크랩 몸통 프리미티브(구)의 localScale. 사냥감 스포너가 이 값을 그대로 써야 한다.</summary>
        public static readonly Vector3 CrabSmallBodyScale = new Vector3(0.30f, 0.18f, 0.30f);

        /// <summary>대왕 크랩 피벗을 지면에서 띄우는 높이(m). 큐브 높이(0.9)의 절반 = 콜라이더 바닥이 지면.</summary>
        public const float CrabGiantGroundOffset = 0.45f;

        /// <summary>소형 크랩 피벗을 지면에서 띄우는 높이(m). 등딱지 아랫면과 발끝 사이가 6.2cm다.</summary>
        public const float CrabSmallGroundOffset = 0.062f;

        /// <summary>
        /// 이 클래스가 나눠 준 공유 머티리얼 목록. ApplySharedMaterial이 예전 머티리얼을 파괴할 때
        /// "이건 공유본이라 절대 파괴하면 안 된다"를 판정하는 데 쓴다(공유본을 파괴하면 그 색을 쓰는
        /// 월드의 모든 오브젝트가 한꺼번에 분홍색이 된다).
        /// </summary>
        private static readonly HashSet<Material> sharedMaterials = new HashSet<Material>();

        /// <summary>눈·발톱처럼 어디에나 쓰이는 검정에 가까운 색.</summary>
        private static readonly Color EyeBlack = new Color(0.05f, 0.05f, 0.05f);

        // ── 공용 유틸 (기존 공개 API - 시그니처를 바꾸지 않는다) ──────────────────────────
        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 구체 보조 파츠를 만들어 붙인다.
        /// worldRadius는 부모가 어떻게 눌리거나 늘어나 있어도 항상 같은 월드 크기의 둥근 구체로
        /// 보이게 하기 위한 목표 반지름(미터)이다.
        /// </summary>
        public static GameObject AddCompensatedSphere(Transform parent, Vector3 localPos, float worldRadius, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.x),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.y),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Sphere, localPos, compScale, color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 박스 보조 파츠(지느러미, 가시 등 각진 형태)를 만들어 붙인다.
        /// worldSize는 목표 월드 크기(가로/높이/깊이, 미터)다.
        /// </summary>
        public static GameObject AddCompensatedBox(Transform parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name, Quaternion? localRotation = null)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Cube, localPos, compScale, color, localRotation);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 캡슐 보조 파츠(다리 등 길쭉한 형태)를 만들어 붙인다.
        /// </summary>
        public static GameObject AddCompensatedCapsule(Transform parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name, Quaternion? localRotation = null)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Capsule, localPos, compScale, color, localRotation);
        }

        /// <summary>
        /// 눕혀서 배치된 몸통(rotationEuler(0,0,90))에 붙일 "일어선" 좌표계 피벗을 만든다.
        /// [B29] 몸통 형태가 전부 절차 메시로 옮겨가면서 이 파일 안에서는 더 이상 쓰이지 않지만,
        /// 눕힌 몸통에 파츠를 붙여야 하는 다른 작업이 다시 생길 때를 위한 공개 API로 남겨 둔다.
        /// 계산 근거: 자식의 월드 변환은 (부모회전 R)·(부모스케일 S)·(자식회전 r)·(자식스케일 s) 순이라,
        /// r = Euler(0,0,-90) · s = (1/S.y, 1/S.x, 1/S.z)로 두면 피벗 내부가 무회전·단위스케일이 된다.
        /// </summary>
        public static Transform CreateUprightPivot(GameObject body, Vector3 appliedScale, string name)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(body.transform, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            pivot.transform.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, appliedScale.y),
                1f / Mathf.Max(0.0001f, appliedScale.x),
                1f / Mathf.Max(0.0001f, appliedScale.z));
            return pivot.transform;
        }

        /// <summary>
        /// [B29 이전 방식 - 현재 호출부 없음] 몸통 캡슐 아래에 짧은 다리 4개를 파츠로 붙인다.
        /// 사족보행 실루엣은 이제 CreatureMeshLibrary.HuntLandBodyUnit이 메시로 굽는다(파츠 4개 절약).
        /// 공개 API라 남겨 두지만 새 코드에서 쓰지 말 것.
        /// </summary>
        public static void AddQuadrupedLegs(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color legColor = bodyColor * 0.7f;
            Vector3[] legLocalPositions =
            {
                new Vector3(0.356f, -0.65f, 0.356f),
                new Vector3(-0.356f, -0.65f, 0.356f),
                new Vector3(0.356f, -0.65f, -0.356f),
                new Vector3(-0.356f, -0.65f, -0.356f),
            };

            for (int i = 0; i < legLocalPositions.Length; i++)
                AddCompensatedCapsule(body.transform, legLocalPositions[i], new Vector3(0.09f, 0.21f, 0.09f), appliedScale, legColor, $"Leg{i}");
        }

        // ── 공유 머티리얼 / 메시 적용 ────────────────────────────────────────────────────
        /// <summary>
        /// (색 + 텍스처)당 하나뿐인 공유 머티리얼을 얻는다. 캐시는 자원 노드와 같은 보관소
        /// (ResourceVisualLibrary)를 쓴다 - 월드 전체가 한 벌의 머티리얼을 나눠 쓰게 하기 위함이다.
        /// </summary>
        private static Material Shared(Color color, string textureName)
        {
            Material material = ResourceVisualLibrary.GetMaterial(color, textureName);
            if (material != null)
                sharedMaterials.Add(material);
            return material;
        }

        /// <summary>
        /// 오브젝트의 머티리얼을 공유본으로 갈아 끼우고, 스포너가 개체마다 새로 만들었던 1회용
        /// 머티리얼은 파괴한다. 파괴하지 않으면 참조만 끊긴 채 세션 내내 메모리에 남는다
        /// (Unity는 런타임 생성 Object를 Resources.UnloadUnusedAssets 전까지 회수하지 않는다).
        /// 공유본은 절대 파괴하지 않는다(sharedMaterials 주석 참고).
        /// </summary>
        private static void ApplySharedMaterial(GameObject go, Color color, string textureName)
        {
            if (go == null)
                return;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            Material next = Shared(color, textureName);
            if (next == null || renderer.sharedMaterial == next)
                return;

            Material previous = renderer.sharedMaterial;
            renderer.sharedMaterial = next;

            // [B29 감독] 예전 조건은 "공유본이 아니면 파괴"였다. 그런데 프리미티브가 처음 달고 오는
            // 머티리얼은 **내장 에셋(Default-Material)** 이라 공유본 집합에도 없다 - 그래서 스폰될
            // 때마다 에셋 파괴를 시도했고, 콘솔에
            // "Destroying assets is not permitted to avoid data loss"가 54번 찍혔다(실기에서 확인).
            // 이제 우리가 런타임에 만든 것(StructureVisualBuilder가 이름에 새긴 접두어)만 파괴한다.
            if (previous != null
                && !sharedMaterials.Contains(previous)
                && previous.name != null
                && previous.name.StartsWith(StructureVisualBuilder.RuntimeMaterialPrefix))
            {
                Object.Destroy(previous);
            }
        }

        /// <summary>
        /// 몸통 프리미티브의 메시를 절차 메시로 갈아 끼운다. 콜라이더는 건드리지 않는다 -
        /// 프리미티브 콜라이더(Capsule/Sphere/Box)는 파라메트릭이라 MeshFilter와 완전히 독립이고,
        /// 그래서 접촉/전투 판정 범위는 1mm도 변하지 않는다.
        /// </summary>
        private static void ApplyBodyMesh(GameObject body, Mesh mesh)
        {
            if (body == null || mesh == null)
                return;

            var filter = body.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = mesh;
        }

        /// <summary>
        /// 이름이 name인 자식을 찾아 **미터 공간**으로 다시 세팅하고(로컬 1단위 = 1미터 × sizeJitter),
        /// 지정한 절차 메시를 씌운다. 자식이 없으면 새로 만든다(스포너 쪽 파츠 구성이 바뀌어도
        /// NullReference가 나지 않게 하는 방어).
        ///
        /// localScale = 1/nominal 인 이유: 실제 부모 스케일은 nominal × sizeJitter이므로
        /// 자식의 월드 스케일 = nominal·jitter · (1/nominal) = jitter 가 되어, 미터로 적은 값이
        /// 그대로 월드 미터가 되면서 개체별 크기 편차(jitter)는 유지된다.
        /// 회전은 주지 않는다 - 비균일 부모 밑에서 회전한 자식은 전단으로 찌그러진다.
        /// </summary>
        private static GameObject MeterSpacePart(Transform parent, string name, Vector3 nominal, Mesh mesh,
            Color color, string textureName)
        {
            Vector3 inverse = new Vector3(1f / nominal.x, 1f / nominal.y, 1f / nominal.z);
            Transform existing = parent.Find(name);
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = inverse;
            }
            else
            {
                go = StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Cube,
                    Vector3.zero, inverse, Shared(color, textureName));
            }

            ApplySharedMaterial(go, color, textureName);
            ApplyBodyMesh(go, mesh);
            return go;
        }

        /// <summary>
        /// 이름이 name인 구체 자식(스포너가 이미 만들어 둔 눈 등)을 미터 단위로 다시 배치/크기 조정한다.
        /// 없으면 새로 만든다. 파츠를 새로 만드는 대신 있는 것을 옮기므로 파츠 수가 늘지 않는다.
        /// </summary>
        private static GameObject ReshapeSphere(Transform parent, string name, Vector3 nominal,
            Vector3 posMeters, float diameterMeters, Color color, string textureName)
        {
            Vector3 localPos = new Vector3(posMeters.x / nominal.x, posMeters.y / nominal.y, posMeters.z / nominal.z);
            Vector3 localScale = new Vector3(diameterMeters / nominal.x, diameterMeters / nominal.y, diameterMeters / nominal.z);

            Transform existing = parent.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = localScale;
            }
            else
            {
                go = StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Sphere,
                    localPos, localScale, Shared(color, textureName));
            }

            ApplySharedMaterial(go, color, textureName);
            return go;
        }

        /// <summary>
        /// 이름이 name인 자식의 크기만 미터 단위로 다시 잡는다(위치는 스포너가 정한 그대로 둔다).
        /// 벌떼처럼 "스포너가 난수로 흩뿌린 위치"에 의미가 있는 파츠 전용이다.
        /// </summary>
        private static void ResizeChild(Transform parent, string name, Vector3 nominal, float diameterMeters,
            Color color, string textureName)
        {
            Transform child = parent.Find(name);
            if (child == null)
                return;

            child.localScale = new Vector3(diameterMeters / nominal.x, diameterMeters / nominal.y, diameterMeters / nominal.z);
            ApplySharedMaterial(child.gameObject, color, textureName);
        }

        // ── 위험 요소 진입점 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 지정한 위험 요소 종류에 맞는 외형을 완성한다. HazardSpawner.AddDetailParts가 눈/등지느러미/
        /// 벌 파츠를 만든 **직후** 종류 구분 없이 항상 호출되므로, 이 메서드는 그 파츠들이 이미 존재한다는
        /// 전제로 동작한다 - 새로 만드는 대신 찾아서 제자리로 옮기고 크기를 다시 잡는다.
        /// (기존 이름: EyeL / EyeR / Fin / Bee0~Bee4. 이름이 바뀌면 여기서도 함께 고칠 것.)
        ///
        /// [B29] 모든 종류가 (a) 몸통 메시 교체 (b) 공유 머티리얼 적용 (c) 남은 파츠 재배치 세 단계를 거친다.
        /// 벌떼도 더 이상 예외가 아니다(예전에는 "이미 충분하다"고 판단해 건드리지 않았다).
        /// </summary>
        public static void AddHazardDetailsIfMissing(GameObject body, HazardType type, Vector3 appliedScale, Color bodyColor)
        {
            if (body == null)
                return;

            switch (type)
            {
                case HazardType.VenomousSnake:
                    AddSnakeDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Scorpion:
                    AddScorpionDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Trap:
                    AddTrapDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Bear:
                    AddBearDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Cannibal:
                    AddCannibalDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Shark:
                    AddSharkTailDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.BeeSwarm:
                    AddBeeSwarmDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.GiantCrab:
                    // [B30] 대왕 크랩은 사냥감 소형 크랩과 **같은 메시 제작기**를 쓴다(giant 플래그만 다르다).
                    // 파츠 이름은 EyeL/EyeR 두 개뿐이라 위 다른 종류들과 겹치지 않는다.
                    BuildCrabBody(body, bodyColor, true);
                    break;
            }
        }

        /// <summary>
        /// 곰(HazardType.Bear).
        ///
        /// [B29 - 감독이 이름을 콕 집은 항목] 예전에는 세워 놓은 캡슐 + 귀 구체 2개 + 주둥이 박스라
        /// 옆에서 봐도 식인종과 같은 "서 있는 알약"이었다. 이제 몸통 메시 자체가 네 발로 선 곰이다.
        /// 실루엣을 만드는 것은 네 가지이고 전부 메시 안에 있다:
        ///   1. 어깨 혹 - 등줄기 반지름이 앞다리 위에서 0.29 → 0.37m로 부풀었다가 다시 줄어든다.
        ///      등선 꼭대기 1.12m 대비 혹 꼭대기 1.24m. 곰과 다른 사족보행 동물을 가르는 결정적 신호다.
        ///   2. 짧고 굵은 다리 - 배 아래 0.50m 공간에 지름 0.24m 다리가 들어간다(길이/굵기 = 2.1).
        ///   3. 처진 머리 - 목이 어깨(0.83m)에서 머리(0.61m)로 내려간다. 곰은 머리를 어깨보다 낮게 든다.
        ///   4. 주둥이 - 머리 앞으로 0.2m 뻗은 원뿔형 코. 끝에 어두운 코 파츠 하나만 얹는다.
        /// 크기: 코끝~엉덩이 1.68m · 혹 높이 1.24m(길이/높이 1.35 = 실제 불곰 비율).
        ///
        /// 파츠: 몸통(1) + 눈 2(스포너가 만든 것을 옮겨 씀) + 코(1) = 4개. 예전 6개.
        /// 판정 불변: 몸통 캡슐 콜라이더(지름 0.9m · 높이 2.2m 수직)는 그대로다. 시각이 콜라이더보다
        /// 앞뒤로 길어지지만, 조준/접촉이 잡히는 곳은 몸통 한가운데(혹과 가슴)라 실사용에 지장이 없다.
        /// </summary>
        public static void AddBearDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.BearBodyUnit());
            ApplySharedMaterial(body, bodyColor, "bark");
            SnapPivotForJitter(body, appliedScale, BearScale, 1.1f);

            Color muzzle = ResourceVisualLibrary.Shade(bodyColor, 0.55f);
            // 눈 위치 검산(머리 단면은 z 0.755에서 가로 반경 0.1175m · 세로 반경 0.1335m의 타원):
            // (0.088/0.1175)² + (0.0844/0.1335)² = 0.96 < 1 → 중심이 표면 바로 안쪽이고,
            // 눈 반지름 0.045m가 더해져 확실히 밖으로 드러난다.
            ReshapeSphere(body.transform, "EyeL", BearScale, new Vector3(0.088f, -0.415f, 0.755f), 0.09f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", BearScale, new Vector3(-0.088f, -0.415f, 0.755f), 0.09f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "Snout", BearScale, new Vector3(0f, -0.525f, 0.875f), 0.13f, muzzle, "noise");

            // 예전 방식의 귀 파츠는 메시에 들어갔다. 남아 있으면 머리 위에 혹 두 개로 겹치므로 지운다.
            RemoveLegacyPart(body.transform, "EarL");
            RemoveLegacyPart(body.transform, "EarR");
        }

        /// <summary>
        /// 식인종(HazardType.Cannibal). 몸통 메시가 사람 형태 전체(다리 2 · 발 2 · 허리에 두른 천 ·
        /// 몸통 · 팔 2 · 목 · 머리)를 담는다. 위협감은 비율에서 나온다: 어깨 폭 0.5m · 키 1.8m ·
        /// 팔이 무릎 근처(0.96m)까지 내려온다.
        /// 들고 있는 창은 오른손 위치(x +0.29m)에서 위로 뻗어 머리(1.8m) 위 1.92m까지 올라가고,
        /// 끝에 돌촉(각진 마름모 날)이 달린다 - 곰(동물)과 나란히 봤을 때 "무장한 사람"으로 갈린다.
        /// 파츠: 몸통(1) + 눈 2 + 창 + 돌촉 = 5개(예전과 동일하지만 형태가 전부 바뀌었다).
        /// </summary>
        public static void AddCannibalDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.CannibalBodyUnit());
            ApplySharedMaterial(body, bodyColor, "noise");
            SnapPivotForJitter(body, appliedScale, CannibalScale, 0.9f);

            ReshapeSphere(body.transform, "EyeL", CannibalScale, new Vector3(0.042f, 0.80f, 0.078f), 0.05f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", CannibalScale, new Vector3(-0.042f, 0.80f, 0.078f), 0.05f, EyeBlack, "noise");

            MeterSpacePart(body.transform, "Spear", CannibalScale, CreatureMeshLibrary.SpearShaftMeters(),
                StructureVisualBuilder.Driftwood, "wood");
            MeterSpacePart(body.transform, "SpearHead", CannibalScale, CreatureMeshLibrary.SpearHeadMeters(),
                StructureVisualBuilder.WeatheredStone, "stone");
        }

        /// <summary>
        /// 상어(HazardType.Shark). 몸통 메시가 방추형 몸통 + 가슴지느러미 2 + 초승달 꼬리지느러미를
        /// 한 장에 담고, 물 위로 드러나는 등지느러미만 별도 파츠로 남는다(Danger Red를 유지해야 하므로).
        ///
        /// ⚠️ 수면 노출 계산은 손대지 않았다. SharkSpawner.depthBelowSeaLevel = 0.3은 "몸통 중심이
        /// 해수면 아래로 내려가는 깊이"이고, 등지느러미 꼭대기는 예전과 똑같이 몸통 중심 위 **0.39m**다
        /// → 수면 위 0.09m 노출. 꼬리 위쪽 날개 끝은 0.34m로 일부러 등지느러미보다 **낮게** 잡았다.
        /// 그래야 물 위로 보이는 것이 등지느러미 하나라는 기존 실루엣이 유지된다.
        /// 이 값을 바꾸려면 반드시 SharkSpawner.depthBelowSeaLevel과 함께 계산할 것.
        ///
        /// 눈 위치도 함께 고쳤다: 예전 값(로컬 y 0.7 → 앞쪽 0.98m 지점, 좌우 ±0.18m)은 새 몸통에서
        /// 그 지점의 반지름 0.147m보다 밖이라 눈이 몸통에서 떨어져 공중에 떠 있게 된다. ±0.125m로 당겼다.
        /// 파츠: 몸통(1) + 눈 2 + 등지느러미(1) = 4개. 예전 5개(꼬리지느러미 파츠가 메시로 들어갔다).
        /// </summary>
        public static void AddSharkTailDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.SharkBodyUnit());
            ApplySharedMaterial(body, bodyColor, "noise");

            // 눕힌 몸통이라 (x, y, z) = (월드 위, 진행 방향, 좌우) 미터다.
            ReshapeSphere(body.transform, "EyeL", SharkScale, new Vector3(0.055f, 0.98f, 0.125f), 0.07f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", SharkScale, new Vector3(0.055f, 0.98f, -0.125f), 0.07f, EyeBlack, "noise");

            MeterSpacePart(body.transform, "Fin", SharkScale, CreatureMeshLibrary.SharkDorsalMeters(),
                StructureVisualBuilder.DangerRed, "noise");
            RemoveLegacyPart(body.transform, "TailFin");
        }

        /// <summary>
        /// 독사(HazardType.VenomousSnake). 위에서 내려다볼 때 알아볼 수 있어야 한다는 것이 이번 요구라,
        /// 몸통을 **굽이치는 S자**로 굽는다(좌우 진폭 ±0.15m, 1.5파장). 지면에 놓인 초록 막대기였던
        /// 예전 형태와 달리 위에서 봐도 뱀으로 읽힌다.
        /// 비늘 마디는 파츠(어두운 띠 2개)가 아니라 반지름 물결(±7.5%)로 메시에 구웠다 - 대나무 마디를
        /// 원반 파츠에서 줄기 굵기 변화로 옮긴 B28 기법 그대로다.
        /// 머리는 몸통 끝에서 두께가 한 번 부풀었다가 주둥이로 좁아지고, 지면 위 0.20m까지 들린다
        /// (등 높이 0.16m). 붉은 혀만 파츠로 남는다.
        /// 파츠: 몸통(1) + 혀(1) = 2개. 예전 5개(머리 구체 + 혀 + 띠 2).
        /// </summary>
        public static void AddSnakeDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.SnakeBodyUnit());
            ApplySharedMaterial(body, bodyColor, "noise");

            MeterSpacePart(body.transform, "Tongue", SnakeScale, CreatureMeshLibrary.SnakeTongueMeters(),
                StructureVisualBuilder.DangerRed, "noise");
        }

        /// <summary>
        /// 전갈(HazardType.Scorpion). 역시 "위에서 내려다볼 때"가 기준이라, 위에서 보이는 것 전부를
        /// 메시에 넣었다 - 마디진 몸통 + **다리 8개**(좌우 4쌍이 뒤로 벌어진다) + 앞으로 벌린 **집게 2개**.
        /// 옆에서 보이는 신호는 뒤에서 위로 솟았다가 앞으로 말리는 **꼬리 5마디**이고, 꼬리 끝 높이는
        /// 예전과 같은 지면 위 0.435m를 유지한다(몸길이 0.6m짜리 개체의 유일한 원거리 단서라 낮추면 안 된다).
        /// 독침만 Danger Red 파츠로 남는다.
        /// 파츠: 몸통(1) + 독침(1) = 2개. 예전 5개(꼬리 2마디 + 집게 2).
        /// </summary>
        public static void AddScorpionDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.ScorpionBodyUnit());
            ApplySharedMaterial(body, bodyColor, "noise");

            MeterSpacePart(body.transform, "Stinger", ScorpionScale, CreatureMeshLibrary.ScorpionStingerMeters(),
                StructureVisualBuilder.DangerRed, "noise");
        }

        /// <summary>
        /// 함정(HazardType.Trap). "사람이 만든 물건으로 읽혀야 한다"는 요구를, 자연물에는 없는 형태
        /// 언어로 답한다(ArtDirection 2장 4번): 원형 바닥판 + 가운데 압력판 + **좌우로 갈라진 반원 턱 2개** +
        /// 턱을 따라 안쪽으로 기운 **이빨 10개** + 옆으로 뻗은 고정 말뚝. 전부 메시 한 장이고 metal
        /// 텍스처를 써서 초목/돌과 표면 질감부터 갈린다.
        /// 이빨 꼭대기는 예전 가시(지면 위 0.17m)와 같은 높이대(0.185m)라 걸려 넘어지는 체감이 유지된다.
        /// 파츠: 몸통(1)뿐. 예전 9개(원판 + 가시 8) - 이번 배치에서 파츠 절감이 가장 큰 항목이다.
        /// </summary>
        public static void AddTrapDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.TrapBodyUnit());
            ApplySharedMaterial(body, ResourceVisualLibrary.Shade(bodyColor, 0.9f), "metal");

            for (int i = 0; i < 8; i++)
                RemoveLegacyPart(body.transform, $"Spike{i}");
        }

        /// <summary>
        /// 벌떼(HazardType.BeeSwarm). 벌떼의 실루엣은 개체가 아니라 **무리**라, 몸통 구체 하나를
        /// 지름 0.68m 공간에 흩어진 벌 18마리(각각 길이 5.5cm의 작은 몸통)로 갈아 끼운다. 예전에는
        /// 지름 0.5m 노란 공 하나에 지름 0.18m 구체 5개가 박혀 있어 "덩어리"로 읽혔다.
        /// 스포너가 난수로 흩뿌린 구체 5개는 위치를 살린 채 지름 0.05m로 줄이고 어둡게 칠해,
        /// 노란 무리 안에 어두운 개체가 섞이며 밀도가 생기게 한다.
        /// 무리의 "움직임"은 HazardSource.Update가 몸통을 천천히 돌려 만든다(unscaledDeltaTime 사용).
        /// 구체 콜라이더는 회전에 불변이라 판정에 영향이 없다.
        /// 파츠: 몸통(1) + 벌 5 = 6개(예전과 동일).
        /// </summary>
        public static void AddBeeSwarmDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            ApplyBodyMesh(body, CreatureMeshLibrary.BeeSwarmUnit());
            ApplySharedMaterial(body, bodyColor, "noise");

            Color darkBee = new Color(0.20f, 0.15f, 0.05f);
            for (int i = 0; i < 5; i++)
                ResizeChild(body.transform, $"Bee{i}", BeeSwarmScale, 0.05f, darkBee, "noise");
        }

        // ── 사냥감 진입점 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 사냥감(HuntableCreature)의 몸통을 완성한다. CreatureSpawner가 프리미티브를 만든 직후 호출한다.
        ///
        /// 육상: 사족보행 동물 한 마리를 메시로 굽는다(몸통 + 목 + 머리 + 주둥이 + 귀 2 + 다리 4 + 꼬리).
        ///   예전에는 캡슐 + 눈 2 + 머리 구체 + 다리 캡슐 4 = 8파츠였는데, 다리가 몸통에 묻히고
        ///   머리 돌기가 따로 놀아 "알약에 혹이 붙은 것"으로 보였다. 지금은 파츠가 몸통 + 눈 2 = 3개다.
        /// 물고기: 좌우로 납작하고 위아래로 높은 방추형 몸통 + 등지느러미 + 갈라진 꼬리지느러미를
        ///   메시 한 장에 굽는다. 파츠는 몸통 + 눈 = 2개(예전 3개).
        ///
        /// 콜라이더는 건드리지 않는다 - 사냥 상호작용은 InteractionController의 카메라 레이캐스트가
        /// 잡는 루트 콜라이더 하나뿐이라, 몸통 중심을 조준하는 기존 조작감이 그대로 유지되도록
        /// 몸길이를 콜라이더 지름의 2배 안(0.90m)으로 묶었다.
        /// </summary>
        public static void BuildHuntableBody(GameObject body, Color bodyColor, bool isFish)
        {
            if (body == null)
                return;

            if (isFish)
            {
                ApplyBodyMesh(body, CreatureMeshLibrary.FishBodyUnit());
                ApplySharedMaterial(body, bodyColor, "noise");
                // 물고기 단면은 좌우로 눌려 있어(0.62배) 눈을 옆으로 많이 벌리면 몸통에서 떨어진다.
                // z 0.170 지점의 단면은 가로 반경 0.0259m · 세로 반경 0.0521m다.
                ReshapeSphere(body.transform, "Eye", HuntFishScale, new Vector3(0.018f, 0.030f, 0.170f), 0.026f, EyeBlack, "noise");
                return;
            }

            ApplyBodyMesh(body, CreatureMeshLibrary.HuntLandBodyUnit());
            ApplySharedMaterial(body, bodyColor, "bark");
            ReshapeSphere(body.transform, "EyeL", HuntLandScale, new Vector3(0.048f, 0.030f, 0.412f), 0.026f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", HuntLandScale, new Vector3(-0.048f, 0.030f, 0.412f), 0.026f, EyeBlack, "noise");
        }

        // ── 게 (대왕 크랩 = 위험 요소 / 소형 크랩 = 사냥감) ──────────────────────────────
        /// <summary>
        /// [B30] 게 한 마리의 외형을 완성한다. **BuildHuntableBody와 완전히 같은 사용 방식**이다 -
        /// 스포너가 프리미티브를 만들어 localScale/position/rotation을 정한 **직후** 한 번 부르면 된다.
        /// 콜라이더는 어느 경로에서도 건드리지 않는다(메시 교체는 MeshFilter만 바꾸고, 프리미티브
        /// 콜라이더는 파라메트릭이라 접촉/사냥 판정 범위가 1mm도 변하지 않는다).
        ///
        /// giant = false → 등딱지 폭 0.22m의 소형 크랩(사냥감). 몸통 규격은 CrabSmallBodyScale.
        /// giant = true  → 등딱지 폭 1.60m의 대왕 크랩(위험 요소). 몸통 규격은 CrabGiantBodyScale.
        ///
        /// 대왕은 소형의 단순 확대가 아니다(CreatureMeshLibrary.CrabBodyUnit 주석의 표 참고):
        /// 집게가 등딱지 대비 1.39배 크고, 등딱지에 돌기 8개와 긁힌 자국 3줄이 메시에 파여 있다.
        ///
        /// 파츠: 몸통(1) + 자루눈 끝의 눈알 2 = **3개**. 등딱지·집게·다리 8개·눈자루는 전부 몸통 메시
        /// 한 장 안에 있다(다리를 파츠로 만들면 개체당 8개가 늘어난다 - B29가 없앤 바로 그 비용이다).
        /// 머티리얼도 새로 만들지 않는다 - 색+텍스처 조합당 하나인 공유 캐시에서 받아 쓴다.
        ///
        /// jitter 접지 보정은 giant일 때만 한다. HazardSpawner는 groundOffset에 sizeJitter를 곱하지
        /// 않지만(SnapPivotForJitter 주석 참고) CreatureSpawner는 곱하기 때문이다 - 소형에서도 보정하면
        /// 이중 보정이 되어 오히려 발이 뜬다.
        /// </summary>
        public static void BuildCrabBody(GameObject body, Color bodyColor, bool giant)
        {
            if (body == null)
                return;

            Vector3 nominal = giant ? CrabGiantBodyScale : CrabSmallBodyScale;
            float unit = giant ? CreatureMeshLibrary.CrabGiantHalfWidth : CreatureMeshLibrary.CrabSmallHalfWidth;

            ApplyBodyMesh(body, CreatureMeshLibrary.CrabBodyUnit(giant));
            // 갑각에는 돌 텍스처가 어울린다(딱딱하고 오돌토돌한 표면). 텍스처가 없으면 CreateColorMaterial이
            // 조용히 단색으로 넘어가므로 안전하다.
            ApplySharedMaterial(body, bodyColor, "rock");

            if (giant)
                SnapPivotForJitter(body, body.transform.localScale, nominal, CrabGiantGroundOffset);

            // 자루눈: 눈자루는 메시에 있고, 그 끝의 눈알만 구체 파츠다.
            // 위치/지름은 등딱지 반폭(unit) 배수로 적어, 대왕과 소형이 정확히 같은 비율을 갖는다.
            Vector3 eye = new Vector3(0.2375f * unit, 0.660f * unit, 0.505f * unit);
            float eyeDiameter = 0.130f * unit;
            ReshapeSphere(body.transform, "EyeL", nominal, eye, eyeDiameter, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", nominal, new Vector3(-eye.x, eye.y, eye.z), eyeDiameter, EyeBlack, "noise");
        }

        /// <summary>
        /// [B29 버그 수정 - 개체 크기 편차만큼 발이 뜨거나 파묻혔다]
        /// HazardSpawner는 몸통 스케일에는 sizeJitter(0.9~1.15배)를 곱하지만 지면에서 띄우는 높이
        /// (config.groundOffset)에는 곱하지 않는다. 몸통 바닥은 스케일에 비례해 내려가므로,
        /// 곰(groundOffset 1.1)은 작은 개체가 지면 위 0.11m에 떠 있고 큰 개체는 0.165m 파묻힌다.
        /// 예전에는 매끈한 캡슐이라 티가 안 났지만, 지금은 발과 발바닥이 있어서 곧바로 보인다.
        ///
        /// 보정량 = groundOffset × (jitter − 1). 유도: 몸통 바닥의 로컬 y는 −groundOffset/nominal.y로
        /// 설계돼 있으므로 월드 바닥 = 피벗 − groundOffset·jitter이고, 피벗이 지면+groundOffset이면
        /// 오차가 정확히 groundOffset·(jitter−1)이다.
        /// 콜라이더 **크기**는 건드리지 않는다 - 몸통 전체를 최대 0.165m 수직 이동시킬 뿐이고,
        /// 이는 원래 지면에 맞아야 했던 위치로 되돌리는 것이다(파묻혀 있던 개체가 오히려 정상화된다).
        /// 상어는 SharkSpawner가 수심을 직접 계산해 넘기므로(groundOffset 0) 이 보정을 적용하지 않는다.
        /// </summary>
        private static void SnapPivotForJitter(GameObject body, Vector3 appliedScale, Vector3 nominal, float groundOffset)
        {
            float jitter = appliedScale.y / Mathf.Max(0.0001f, nominal.y);
            Vector3 position = body.transform.position;
            position.y += groundOffset * (jitter - 1f);
            body.transform.position = position;
        }

        /// <summary>
        /// 예전 방식으로 만들어져 이제는 메시에 포함된 파츠를 치운다. Destroy는 프레임 끝까지 지연되므로
        /// 먼저 SetActive(false)로 즉시 화면에서 뺀다(AGENT_BRIEF 4장).
        /// </summary>
        private static void RemoveLegacyPart(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
                return;

            child.gameObject.SetActive(false);
            Object.Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// [B29] 생물형 오브젝트가 **공유**하는 절차 메시 보관소.
    /// 설계 원칙은 자원 노드 쪽(ResourceVisualLibrary)과 같다:
    ///  1. 전부 정적 캐시다. 개체가 몇 마리든 종류당 메시 1장이고, 머티리얼까지 같아서 GPU 인스턴싱이 걸린다.
    ///  2. 좌표는 **미터로 작성하고 마지막에 NominalScale로 나눈다**(Builder.ScaleVertices). 그래서
    ///     아래 표의 숫자는 전부 실제 미터이며, 그대로 읽으면 크기 검산이 된다.
    ///     기준점은 스포너가 놓는 피벗이고, 지면은 groundOffset만큼 아래에 있다(주석에 종류별로 적었다).
    ///  3. 감김(winding)을 표로 외우지 않는다. 삼각형마다 기하 법선을 기준 방향과 비교해 뒤집는다
    ///     (왼손 좌표계에서 표준 인덱스 표를 옮겨 적다가 통째로 안쪽을 향해 컬링된 사고가 반복됐다).
    ///
    /// 비균일 스케일과 법선: 메시를 M⁻¹(=1/NominalScale)로 눌러 두면 렌더 시 M이 곱해져 원래 미터
    /// 형태로 돌아오고, 법선은 Unity가 역전치 행렬로 처리하므로 눌린 상태에서 계산해도 정확히 복원된다.
    /// </summary>
    public static class CreatureMeshLibrary
    {
        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        private static bool TryGetCached(string key, out Mesh mesh)
        {
            return meshCache.TryGetValue(key, out mesh) && mesh != null;
        }

        private static Mesh Store(string key, Mesh mesh)
        {
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 곰 ────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 네 발로 선 곰(캡슐 규격, localScale 0.9 × 1.1 × 0.9 · 피벗은 지면 위 1.1m).
        /// 미터 좌표의 y는 피벗 기준이므로 **지면 = -1.1**이다. +z가 앞(머리 방향).
        /// 등줄기 표(지면 기준 높이 / 반지름): 엉덩이 0.80/0.30 → 등 0.83/0.29 → **혹 0.87/0.37** →
        /// 가슴 0.83/0.30 → 목 0.73/0.21 → 머리 0.61/0.155 → 코 0.575/0.075.
        /// 혹 꼭대기 1.24m · 등선 1.12m · 머리 꼭대기 0.77m → 옆에서 보면 어깨가 가장 높고 머리가 처진다.
        /// </summary>
        public static Mesh BearBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("bear", out cached))
                return cached;

            var builder = new Builder();

            Vector3[] spine =
            {
                new Vector3(0f, -0.340f, -0.560f),
                new Vector3(0f, -0.300f, -0.400f),
                new Vector3(0f, -0.270f, -0.100f),
                new Vector3(0f, -0.230f,  0.160f),
                new Vector3(0f, -0.270f,  0.340f),
                new Vector3(0f, -0.370f,  0.480f),
                new Vector3(0f, -0.450f,  0.600f),
                new Vector3(0f, -0.490f,  0.720f),
                new Vector3(0f, -0.525f,  0.850f),
            };
            float[] spineRadii = { 0.19f, 0.30f, 0.29f, 0.37f, 0.30f, 0.21f, 0.19f, 0.155f, 0.075f };
            builder.AddTube(spine, spineRadii, 8, true, true, 2f, new Vector3(0.88f, 1f, 1f));

            // 다리 4개. 아래 끝 링이 정확히 지면(-1.1)에 닿는 발바닥이 되도록 y를 맞췄다.
            AddBearLeg(builder, 0.185f, 0.24f);
            AddBearLeg(builder, -0.185f, 0.24f);
            AddBearLeg(builder, 0.195f, -0.34f);
            AddBearLeg(builder, -0.195f, -0.34f);

            // 귀: 머리 위(등선 -0.26)보다 4cm 솟는다.
            AddBearEar(builder, 0.115f);
            AddBearEar(builder, -0.115f);

            builder.ScaleVertices(new Vector3(1f / 0.9f, 1f / 1.1f, 1f / 0.9f));
            return Store("bear", builder.Finish("Cre_BearBody"));
        }

        /// <summary>곰 다리 하나(기둥 + 앞뒤로 뻗은 발). 발바닥이 지면(y = -1.1)에 정확히 닿는다.</summary>
        private static void AddBearLeg(Builder builder, float x, float z)
        {
            Vector3[] leg =
            {
                new Vector3(x, -0.360f, z),
                new Vector3(x, -0.700f, z + 0.005f),
                new Vector3(x, -1.020f, z + 0.010f),
            };
            builder.AddTube(leg, new[] { 0.125f, 0.115f, 0.100f }, 6, true, true, 1f, Vector3.one);

            Vector3[] paw =
            {
                new Vector3(x, -1.025f, z - 0.080f),
                new Vector3(x, -1.025f, z + 0.080f),
            };
            builder.AddTube(paw, new[] { 0.075f, 0.070f }, 6, true, true, 1f, Vector3.one);
        }

        /// <summary>곰 귀 하나(작은 원뿔).</summary>
        private static void AddBearEar(Builder builder, float x)
        {
            Vector3[] ear =
            {
                new Vector3(x, -0.330f, 0.585f),
                new Vector3(x * 1.09f, -0.210f, 0.575f),
            };
            builder.AddTube(ear, new[] { 0.055f, 0.030f }, 5, true, true, 1f, Vector3.one);
        }

        // ── 식인종 ────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 사람 형태(캡슐 규격, localScale 0.55 × 0.9 × 0.55 · 피벗은 지면 위 0.9m → **지면 = -0.9**).
        /// 키 1.80m · 어깨 폭 0.50m · 손끝 높이 0.96m(무릎 위). 몸통 단면은 앞뒤로 0.8배 눌러
        /// 원통이 아니라 사람 가슴처럼 보이게 했다.
        /// </summary>
        public static Mesh CannibalBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("cannibal", out cached))
                return cached;

            var builder = new Builder();
            Vector3 flatten = new Vector3(1f, 1f, 0.80f);

            Vector3[] torso =
            {
                new Vector3(0f, -0.10f, 0f),
                new Vector3(0f,  0.02f, 0f),
                new Vector3(0f,  0.20f, 0.005f),
                new Vector3(0f,  0.38f, 0.010f),
                new Vector3(0f,  0.50f, 0.005f),
                new Vector3(0f,  0.58f, 0f),
                new Vector3(0f,  0.66f, 0.005f),
                new Vector3(0f,  0.74f, 0.012f),
                new Vector3(0f,  0.86f, 0.010f),
                new Vector3(0f,  0.90f, 0.005f),
            };
            float[] torsoRadii = { 0.155f, 0.142f, 0.160f, 0.175f, 0.165f, 0.075f, 0.055f, 0.105f, 0.095f, 0.050f };
            builder.AddTube(torso, torsoRadii, 8, true, true, 2f, flatten);

            // 허리에 두른 천 - 자연물에는 없는 "옷" 신호.
            builder.AddTube(
                new[] { new Vector3(0f, -0.16f, 0f), new Vector3(0f, -0.02f, 0f) },
                new[] { 0.185f, 0.168f }, 8, true, true, 1f, flatten);

            AddHumanLeg(builder, 0.11f);
            AddHumanLeg(builder, -0.11f);

            // 팔. 오른팔(+x)은 창을 쥐도록 손이 바깥(0.285)으로 나간다.
            builder.AddTube(
                new[] { new Vector3(0.175f, 0.47f, 0f), new Vector3(0.245f, 0.24f, 0.010f), new Vector3(0.285f, 0.06f, 0.030f) },
                new[] { 0.058f, 0.048f, 0.042f }, 6, true, true, 1f, Vector3.one);
            builder.AddTube(
                new[] { new Vector3(-0.175f, 0.47f, 0f), new Vector3(-0.225f, 0.24f, 0.015f), new Vector3(-0.215f, 0.03f, 0.060f) },
                new[] { 0.058f, 0.048f, 0.042f }, 6, true, true, 1f, Vector3.one);

            builder.ScaleVertices(new Vector3(1f / 0.55f, 1f / 0.9f, 1f / 0.55f));
            return Store("cannibal", builder.Finish("Cre_CannibalBody"));
        }

        /// <summary>사람 다리 하나(허벅지~발목 + 앞으로 뻗은 발). 발바닥이 지면(y = -0.9)에 닿는다.</summary>
        private static void AddHumanLeg(Builder builder, float x)
        {
            builder.AddTube(
                new[] { new Vector3(x, -0.02f, 0f), new Vector3(x, -0.44f, 0.005f), new Vector3(x, -0.80f, 0.010f) },
                new[] { 0.080f, 0.068f, 0.052f }, 6, true, true, 1f, Vector3.one);
            builder.AddTube(
                new[] { new Vector3(x, -0.855f, -0.020f), new Vector3(x, -0.855f, 0.130f) },
                new[] { 0.045f, 0.040f }, 5, true, true, 1f, Vector3.one);
        }

        // ── 상어 ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 상어 몸통(캡슐 규격, localScale 0.45 × 1.4 × 0.45 · rotationEuler(0,0,90)로 눕혀져 있다).
        /// **눕힌 몸통이라 미터 좌표의 뜻이 바뀐다: x = 월드 위쪽, y = 몸통 진행 방향(+가 머리), z = 좌우.**
        /// 몸길이 2.56m(코~꼬리자루) · 최대 둘레 반지름 0.222m(캡슐 반지름 0.225m 안).
        /// 초승달 꼬리는 위쪽 날개 0.34m / 아래쪽 0.22m로 비대칭이고, 위쪽 끝을 등지느러미 꼭대기
        /// (0.39m)보다 낮게 둬서 수면 위로 나오는 것이 등지느러미 하나로 유지된다.
        /// </summary>
        public static Mesh SharkBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("shark", out cached))
                return cached;

            var builder = new Builder();

            Vector3[] body =
            {
                new Vector3(-0.020f,  1.360f, 0f),
                new Vector3( 0.000f,  1.150f, 0f),
                new Vector3( 0.010f,  0.850f, 0f),
                new Vector3( 0.000f,  0.400f, 0f),
                new Vector3( 0.000f,  0.000f, 0f),
                new Vector3( 0.000f, -0.500f, 0f),
                new Vector3( 0.010f, -0.950f, 0f),
                new Vector3( 0.020f, -1.200f, 0f),
            };
            float[] radii = { 0.035f, 0.110f, 0.175f, 0.222f, 0.215f, 0.155f, 0.075f, 0.042f };
            builder.AddTube(body, radii, 8, true, true, 2f, new Vector3(1f, 1f, 0.92f));

            // 초승달 꼬리(진행축-수직 평면의 납작한 날). 두께는 좌우 0.05m.
            builder.AddBlade(new[]
            {
                new Vector3(0.000f, -0.980f, 0f),
                new Vector3(0.340f, -1.400f, 0f),
                new Vector3(0.100f, -1.260f, 0f),
                new Vector3(-0.140f, -1.340f, 0f),
                new Vector3(-0.220f, -1.140f, 0f),
            }, Vector3.forward, 0.05f);

            // 가슴지느러미 2개: 앞쪽 아래에서 뒤·바깥·아래로 뻗는다.
            AddSharkPectoral(builder, 1f);
            AddSharkPectoral(builder, -1f);

            builder.ScaleVertices(new Vector3(1f / 0.45f, 1f / 1.4f, 1f / 0.45f));
            return Store("shark", builder.Finish("Cre_SharkBody"));
        }

        /// <summary>상어 가슴지느러미 하나(side = +1이 왼쪽, -1이 오른쪽).</summary>
        private static void AddSharkPectoral(Builder builder, float side)
        {
            builder.AddBlade(new[]
            {
                new Vector3(-0.060f, 0.740f, side * 0.150f),
                new Vector3(-0.100f, 0.340f, side * 0.150f),
                new Vector3(-0.220f, 0.260f, side * 0.520f),
                new Vector3(-0.180f, 0.520f, side * 0.430f),
            }, Vector3.right, 0.035f);
        }

        /// <summary>
        /// 상어 등지느러미(미터 공간 자식 전용, 원점 = 몸통 중심). 꼭짓점 높이 **0.39m**는
        /// SharkSpawner.depthBelowSeaLevel(0.3)과 짝을 이루는 값이라 임의로 바꾸면 수면 노출이 사라진다.
        /// </summary>
        public static Mesh SharkDorsalMeters()
        {
            Mesh cached;
            if (TryGetCached("sharkDorsal", out cached))
                return cached;

            var builder = new Builder();
            builder.AddBlade(new[]
            {
                new Vector3(0.185f,  0.400f, 0f),
                new Vector3(0.390f,  0.100f, 0f),
                new Vector3(0.260f, -0.220f, 0f),
                new Vector3(0.190f, -0.100f, 0f),
            }, Vector3.forward, 0.05f);

            return Store("sharkDorsal", builder.Finish("Cre_SharkDorsal"));
        }

        // ── 독사 ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 굽이치는 뱀 몸통(캡슐 규격, localScale 0.18 × 0.6 × 0.18 · rotationEuler(0,0,90) ·
        /// 피벗은 지면 위 0.1m). **x = 월드 위쪽, y = 몸통 방향, z = 좌우.**
        /// 좌우 진폭 ±0.15m로 1.5파장을 그리고, 배가 지면 위 0.018m에 거의 일정하게 붙도록
        /// 굵기에 맞춰 높이를 함께 낮춘다. 반지름에 ±7.5% 물결을 넣어 마디(비늘)를 표현한다.
        /// </summary>
        public static Mesh SnakeBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("snake", out cached))
                return cached;

            float[] forward = { -0.60f, -0.50f, -0.40f, -0.28f, -0.16f, -0.04f, 0.08f, 0.20f, 0.32f, 0.42f, 0.50f, 0.56f, 0.61f, 0.66f, 0.69f };
            float[] radii = { 0.010f, 0.026f, 0.040f, 0.055f, 0.066f, 0.072f, 0.073f, 0.070f, 0.064f, 0.056f, 0.048f, 0.046f, 0.058f, 0.038f, 0.014f };
            float[] up = { -0.072f, -0.056f, -0.042f, -0.027f, -0.016f, -0.010f, -0.009f, -0.012f, -0.018f, -0.010f, 0.010f, 0.030f, 0.045f, 0.046f, 0.044f };

            var centers = new Vector3[forward.Length];
            var finalRadii = new float[forward.Length];
            for (int i = 0; i < forward.Length; i++)
            {
                float lateral = 0.15f * Mathf.Sin(7f * forward[i]);
                centers[i] = new Vector3(up[i], forward[i], lateral);

                // 마디: 머리(마지막 세 링)에는 물결을 넣지 않는다 - 머리는 매끈해야 머리로 읽힌다.
                float band = i < forward.Length - 3 ? 1f + 0.075f * Mathf.Sin(i * 2.2f) : 1f;
                finalRadii[i] = radii[i] * band;
            }

            var builder = new Builder();
            // 단면을 위아래로 0.85배 눌러 뱀처럼 납작하게 만든다(폭 0.146m · 높이 0.124m).
            builder.AddTube(centers, finalRadii, 6, true, true, 6f, new Vector3(0.85f, 1f, 1f));
            // 눌린 만큼 배가 떠올라(0.029m) 전체를 내려 지면 위 0.007m에 붙인다.
            builder.Translate(new Vector3(-0.022f, 0f, 0f));
            builder.ScaleVertices(new Vector3(1f / 0.18f, 1f / 0.6f, 1f / 0.18f));
            return Store("snake", builder.Finish("Cre_SnakeBody"));
        }

        /// <summary>뱀의 갈라진 혀(미터 공간 자식 전용). 머리 끝(y 0.69 · z -0.146) 앞으로 뻗는다.</summary>
        public static Mesh SnakeTongueMeters()
        {
            Mesh cached;
            if (TryGetCached("snakeTongue", out cached))
                return cached;

            // 높이는 몸통 메시의 Translate(-0.022)와 같은 만큼 내려 잡았다(머리 중심 up 0.045 → 0.023).
            var builder = new Builder();
            Vector3 root = new Vector3(0.022f, 0.700f, -0.150f);
            Vector3 split = new Vector3(0.022f, 0.760f, -0.162f);
            builder.AddTube(new[] { root, split }, new[] { 0.008f, 0.006f }, 4, true, true, 1f, Vector3.one);
            builder.AddTube(new[] { split, new Vector3(0.026f, 0.800f, -0.140f) }, new[] { 0.005f, 0.002f }, 4, false, true, 1f, Vector3.one);
            builder.AddTube(new[] { split, new Vector3(0.026f, 0.800f, -0.186f) }, new[] { 0.005f, 0.002f }, 4, false, true, 1f, Vector3.one);

            return Store("snakeTongue", builder.Finish("Cre_SnakeTongue"));
        }

        // ── 전갈 ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 전갈(캡슐 규격, localScale 0.16 × 0.3 × 0.16 · rotationEuler(0,0,90) · 피벗은 지면 위 0.09m).
        /// **x = 월드 위쪽, y = 몸통 방향(+가 머리), z = 좌우.** 마지막에 전체를 x로 -0.02m 내려
        /// 배가 지면 위 0.01m에 오게 한다.
        /// 위에서 본 실루엣 = 마디진 몸통 + 다리 8개 + 집게 2개. 옆에서 본 실루엣 = 들어올린 꼬리 5마디
        /// (꼭대기가 지면 위 0.435m로 몸통 등 높이의 약 3배).
        /// </summary>
        public static Mesh ScorpionBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("scorpion", out cached))
                return cached;

            var builder = new Builder();

            float[] bodyForward = { -0.28f, -0.22f, -0.14f, -0.06f, 0.02f, 0.10f, 0.18f, 0.24f };
            float[] bodyRadii = { 0.022f, 0.036f, 0.047f, 0.052f, 0.055f, 0.060f, 0.055f, 0.038f };
            var bodyCenters = new Vector3[bodyForward.Length];
            var finalRadii = new float[bodyForward.Length];
            for (int i = 0; i < bodyForward.Length; i++)
            {
                bodyCenters[i] = new Vector3(bodyForward[i] > 0.05f ? 0.005f : 0f, bodyForward[i], 0f);
                finalRadii[i] = bodyRadii[i] * (1f + 0.08f * Mathf.Sin(i * 2.4f));
            }
            builder.AddTube(bodyCenters, finalRadii, 6, true, true, 3f, new Vector3(0.72f, 1f, 1.15f));

            // 꼬리: 뒤에서 위로 솟았다가 앞으로 말린다.
            builder.AddTube(new[]
            {
                new Vector3(0.000f, -0.260f, 0f),
                new Vector3(0.094f, -0.305f, 0f),
                new Vector3(0.194f, -0.305f, 0f),
                new Vector3(0.281f, -0.265f, 0f),
                new Vector3(0.338f, -0.190f, 0f),
                new Vector3(0.350f, -0.115f, 0f),
            }, new[] { 0.030f, 0.027f, 0.024f, 0.021f, 0.018f, 0.015f }, 6, true, true, 3f, Vector3.one);

            AddScorpionPincer(builder, 1f);
            AddScorpionPincer(builder, -1f);

            float[] legForward = { 0.12f, 0.05f, -0.02f, -0.09f };
            for (int i = 0; i < legForward.Length; i++)
            {
                AddScorpionLeg(builder, legForward[i], 1f);
                AddScorpionLeg(builder, legForward[i], -1f);
            }

            builder.Translate(new Vector3(-0.02f, 0f, 0f));
            builder.ScaleVertices(new Vector3(1f / 0.16f, 1f / 0.3f, 1f / 0.16f));
            return Store("scorpion", builder.Finish("Cre_ScorpionBody"));
        }

        /// <summary>전갈 집게 하나(팔 + 납작한 집게 날).</summary>
        private static void AddScorpionPincer(Builder builder, float side)
        {
            builder.AddTube(new[]
            {
                new Vector3(-0.010f, 0.200f, side * 0.045f),
                new Vector3(-0.015f, 0.300f, side * 0.115f),
            }, new[] { 0.018f, 0.014f }, 5, true, true, 1f, Vector3.one);

            builder.AddBlade(new[]
            {
                new Vector3(-0.015f, 0.280f, side * 0.090f),
                new Vector3(-0.015f, 0.420f, side * 0.100f),
                new Vector3(-0.015f, 0.500f, side * 0.150f),
                new Vector3(-0.015f, 0.460f, side * 0.205f),
                new Vector3(-0.015f, 0.340f, side * 0.175f),
            }, Vector3.right, 0.024f);
        }

        /// <summary>전갈 다리 하나(몸통 → 무릎 → 발). 발이 지면 위 0.008m에 닿는다.</summary>
        private static void AddScorpionLeg(Builder builder, float forward, float side)
        {
            builder.AddTube(new[]
            {
                new Vector3(-0.010f, forward, side * 0.045f),
                new Vector3( 0.020f, forward - 0.030f, side * 0.115f),
                new Vector3(-0.062f, forward - 0.070f, side * 0.145f),
            }, new[] { 0.010f, 0.007f, 0.004f }, 4, true, true, 1f, Vector3.one);
        }

        /// <summary>전갈 독침(미터 공간 자식 전용). 꼬리 끝에서 앞·아래를 향한다.</summary>
        public static Mesh ScorpionStingerMeters()
        {
            Mesh cached;
            if (TryGetCached("scorpionStinger", out cached))
                return cached;

            var builder = new Builder();
            builder.AddTube(new[]
            {
                new Vector3(0.330f, -0.110f, 0f),
                new Vector3(0.310f, -0.055f, 0f),
                new Vector3(0.278f, -0.020f, 0f),
            }, new[] { 0.017f, 0.012f, 0.002f }, 5, true, true, 1f, Vector3.one);

            return Store("scorpionStinger", builder.Finish("Cre_ScorpionStinger"));
        }

        // ── 함정 ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 사람이 놓아둔 덫(실린더 규격, localScale 0.6 × 0.04 × 0.6 · 피벗은 지면 위 0.04m
        /// → **지면 = -0.04**). 몸통 y 스케일이 0.04라 1로컬 단위 = 4cm인 극단적 압축인데,
        /// 미터로 적고 마지막에 나누므로 아래 숫자는 전부 실제 미터다.
        /// 바닥판 · 압력판 · 반원 턱 2개 · 안쪽으로 기운 이빨 10개 · 고정 말뚝.
        /// </summary>
        public static Mesh TrapBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("trap", out cached))
                return cached;

            var builder = new Builder();

            // 바닥판과 압력판.
            builder.AddTube(new[] { new Vector3(0f, -0.038f, 0f), new Vector3(0f, -0.004f, 0f) },
                new[] { 0.255f, 0.250f }, 14, true, true, 2f, Vector3.one);
            builder.AddTube(new[] { new Vector3(0f, -0.004f, 0f), new Vector3(0f, 0.016f, 0f) },
                new[] { 0.105f, 0.100f }, 12, true, true, 1f, Vector3.one);

            AddTrapJaw(builder, -75f, 75f);
            AddTrapJaw(builder, 105f, 255f);

            // 고정 말뚝: 옆으로 뻗은 사슬걸이 + 땅에 박힌 짧은 말뚝.
            builder.AddTube(new[] { new Vector3(0.240f, -0.020f, 0f), new Vector3(0.420f, -0.020f, 0f) },
                new[] { 0.016f, 0.014f }, 5, true, true, 1f, Vector3.one);
            builder.AddTube(new[] { new Vector3(0.420f, -0.040f, 0f), new Vector3(0.420f, 0.070f, 0f) },
                new[] { 0.020f, 0.016f }, 5, true, true, 1f, Vector3.one);

            builder.ScaleVertices(new Vector3(1f / 0.6f, 1f / 0.04f, 1f / 0.6f));
            return Store("trap", builder.Finish("Cre_TrapBody"));
        }

        /// <summary>덫의 반원 턱 하나(호를 따라가는 막대 + 안쪽으로 기운 이빨 5개).</summary>
        private static void AddTrapJaw(Builder builder, float startDegrees, float endDegrees)
        {
            const int arcSamples = 7;
            const float arcRadius = 0.235f;

            var centers = new Vector3[arcSamples];
            var radii = new float[arcSamples];
            for (int i = 0; i < arcSamples; i++)
            {
                float angle = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, (float)i / (arcSamples - 1));
                centers[i] = new Vector3(Mathf.Cos(angle) * arcRadius, 0.018f, Mathf.Sin(angle) * arcRadius);
                radii[i] = 0.024f;
            }
            builder.AddTube(centers, radii, 5, true, true, 2f, Vector3.one);

            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.Deg2Rad * Mathf.Lerp(startDegrees + 10f, endDegrees - 10f, i / 4f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 baseCenter = outward * arcRadius + new Vector3(0f, 0.030f, 0f);
                Vector3 tip = outward * (arcRadius - 0.055f) + new Vector3(0f, 0.145f, 0f);
                builder.AddTube(new[] { baseCenter, tip }, new[] { 0.030f, 0.004f }, 4, true, true, 1f, Vector3.one);
            }
        }

        // ── 벌떼 ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 벌 18마리가 흩어진 무리(구 규격, localScale 0.5 × 0.5 × 0.5). 개체 하나는 길이 5.5cm의
        /// 작은 방추형이고, 지름 0.68m 공간에 결정적 난수(System.Random - 재현성 규칙)로 흩뿌린다.
        /// 무리 자체가 실루엣이라 개체를 크게 만들지 않는 것이 핵심이다(예전 지름 0.18m는 "덩어리"였다).
        /// </summary>
        public static Mesh BeeSwarmUnit()
        {
            Mesh cached;
            if (TryGetCached("beeSwarm", out cached))
                return cached;

            var builder = new Builder();
            var random = new System.Random(4501);

            for (int i = 0; i < 18; i++)
            {
                float u = (float)random.NextDouble() * 2f - 1f;
                float v = (float)random.NextDouble() * 2f - 1f;
                float w = (float)random.NextDouble() * 2f - 1f;
                Vector3 center = new Vector3(u * 0.34f, v * 0.26f, w * 0.34f);

                Vector3 heading = new Vector3(
                    (float)random.NextDouble() * 2f - 1f,
                    ((float)random.NextDouble() * 2f - 1f) * 0.4f,
                    (float)random.NextDouble() * 2f - 1f);
                if (heading.sqrMagnitude < 0.0001f)
                    heading = Vector3.forward;
                heading = heading.normalized * 0.0275f;

                builder.AddTube(new[] { center - heading, center, center + heading },
                    new[] { 0.007f, 0.013f, 0.006f }, 4, true, true, 1f, Vector3.one);
            }

            builder.ScaleVertices(new Vector3(2f, 2f, 2f)); // 1/0.5
            return Store("beeSwarm", builder.Finish("Cre_BeeSwarm"));
        }

        // ── 창 (식인종) ───────────────────────────────────────────────────────────────
        /// <summary>창 자루(미터 공간 자식 전용). 오른손(x 0.285) 위치에서 머리 위까지 뻗는다.</summary>
        public static Mesh SpearShaftMeters()
        {
            Mesh cached;
            if (TryGetCached("spearShaft", out cached))
                return cached;

            var builder = new Builder();
            builder.AddTube(new[]
            {
                new Vector3(0.300f, -0.720f, 0.020f),
                new Vector3(0.300f,  0.100f, 0.020f),
                new Vector3(0.300f,  0.860f, 0.020f),
            }, new[] { 0.026f, 0.023f, 0.020f }, 5, true, true, 4f, Vector3.one);

            return Store("spearShaft", builder.Finish("Cre_SpearShaft"));
        }

        /// <summary>창 끝의 돌촉(미터 공간 자식 전용). 각진 마름모 날이라 막대기가 아니라 무기로 읽힌다.</summary>
        public static Mesh SpearHeadMeters()
        {
            Mesh cached;
            if (TryGetCached("spearHead", out cached))
                return cached;

            var builder = new Builder();
            builder.AddBlade(new[]
            {
                new Vector3(0.300f, 0.840f,  0.020f),
                new Vector3(0.300f, 0.900f,  0.055f),
                new Vector3(0.300f, 1.020f,  0.020f),
                new Vector3(0.300f, 0.900f, -0.015f),
            }, Vector3.right, 0.022f);

            return Store("spearHead", builder.Finish("Cre_SpearHead"));
        }

        // ── 사냥감 ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// 육상 사냥감(캡슐 규격, localScale 0.45 × 0.6 × 0.45 · 피벗은 지면 위 0.6m → **지면 = -0.6**).
        /// 코끝~꼬리 0.90m · 어깨 높이 0.60m · 머리 꼭대기 0.68m. 몸길이를 콜라이더 지름(0.45m)의
        /// 2배 안으로 묶어, 몸통 중심을 조준하면 반드시 콜라이더에 맞도록 했다(사냥 상호작용은 레이캐스트).
        /// </summary>
        public static Mesh HuntLandBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("huntLand", out cached))
                return cached;

            var builder = new Builder();

            Vector3[] spine =
            {
                new Vector3(0f, -0.200f, -0.260f),
                new Vector3(0f, -0.170f, -0.170f),
                new Vector3(0f, -0.155f, -0.050f),
                new Vector3(0f, -0.155f,  0.090f),
                new Vector3(0f, -0.170f,  0.190f),
                new Vector3(0f, -0.110f,  0.260f),
                new Vector3(0f, -0.035f,  0.320f),
                new Vector3(0f,  0.000f,  0.380f),
                new Vector3(0f, -0.015f,  0.440f),
                new Vector3(0f, -0.035f,  0.490f),
            };
            float[] radii = { 0.100f, 0.135f, 0.150f, 0.145f, 0.120f, 0.075f, 0.065f, 0.078f, 0.062f, 0.030f };
            builder.AddTube(spine, radii, 7, true, true, 2f, new Vector3(0.92f, 1f, 1f));

            AddHuntLeg(builder, 0.085f, 0.140f);
            AddHuntLeg(builder, -0.085f, 0.140f);
            AddHuntLeg(builder, 0.085f, -0.170f);
            AddHuntLeg(builder, -0.085f, -0.170f);

            AddHuntEar(builder, 0.045f);
            AddHuntEar(builder, -0.045f);

            builder.AddTube(new[] { new Vector3(0f, -0.160f, -0.260f), new Vector3(0f, -0.235f, -0.365f) },
                new[] { 0.028f, 0.010f }, 5, true, true, 1f, Vector3.one);

            builder.ScaleVertices(new Vector3(1f / 0.45f, 1f / 0.6f, 1f / 0.45f));
            return Store("huntLand", builder.Finish("Cre_HuntLandBody"));
        }

        /// <summary>육상 사냥감 다리 하나. 발끝이 지면(y = -0.6)에 닿는다.</summary>
        private static void AddHuntLeg(Builder builder, float x, float z)
        {
            builder.AddTube(new[]
            {
                new Vector3(x, -0.200f, z),
                new Vector3(x, -0.420f, z + 0.005f),
                new Vector3(x, -0.590f, z + 0.010f),
            }, new[] { 0.042f, 0.034f, 0.030f }, 5, true, true, 1f, Vector3.one);
        }

        /// <summary>육상 사냥감 귀 하나.</summary>
        private static void AddHuntEar(Builder builder, float x)
        {
            builder.AddTube(new[]
            {
                new Vector3(x, 0.035f, 0.365f),
                new Vector3(x * 1.6f, 0.115f, 0.345f),
            }, new[] { 0.026f, 0.010f }, 4, true, true, 1f, Vector3.one);
        }

        /// <summary>
        /// 물고기(구 규격, localScale 0.35 × 0.2 × 0.5 · 피벗은 지면 위 0.15m).
        /// 좌우로 눌리고(0.62배) 위아래로 높은(1.25배) 방추형이라, 예전 "납작한 알"과 달리 물고기로 읽힌다.
        /// 몸길이 0.49m · 높이 0.18m · 폭 0.09m + 등지느러미 + 갈라진 꼬리지느러미.
        /// </summary>
        public static Mesh FishBodyUnit()
        {
            Mesh cached;
            if (TryGetCached("fish", out cached))
                return cached;

            var builder = new Builder();

            Vector3[] body =
            {
                new Vector3(0f, 0f, -0.240f),
                new Vector3(0f, 0f, -0.180f),
                new Vector3(0f, 0f, -0.100f),
                new Vector3(0f, 0f, -0.020f),
                new Vector3(0f, 0f,  0.060f),
                new Vector3(0f, 0f,  0.140f),
                new Vector3(0f, 0f,  0.210f),
                new Vector3(0f, 0f,  0.245f),
            };
            float[] radii = { 0.008f, 0.028f, 0.058f, 0.072f, 0.070f, 0.052f, 0.028f, 0.010f };
            builder.AddTube(body, radii, 7, true, true, 2f, new Vector3(0.62f, 1.25f, 1f));

            // 갈라진 꼬리지느러미(좌우로 얇은 세로 날).
            builder.AddBlade(new[]
            {
                new Vector3(0f,  0.000f, -0.200f),
                new Vector3(0f,  0.130f, -0.330f),
                new Vector3(0f,  0.000f, -0.290f),
                new Vector3(0f, -0.130f, -0.330f),
            }, Vector3.right, 0.014f);

            // 등지느러미.
            builder.AddBlade(new[]
            {
                new Vector3(0f, 0.075f,  0.080f),
                new Vector3(0f, 0.155f,  0.000f),
                new Vector3(0f, 0.070f, -0.100f),
            }, Vector3.right, 0.012f);

            builder.ScaleVertices(new Vector3(1f / 0.35f, 1f / 0.2f, 1f / 0.5f));
            return Store("fish", builder.Finish("Cre_FishBody"));
        }

        // ── 게 (대왕 크랩 = 위험 요소 / 소형 크랩 = 사냥감) ──────────────────────────────
        /// <summary>대왕 크랩 등딱지 반폭(m). 등딱지 폭 1.60m.</summary>
        public const float CrabGiantHalfWidth = 0.80f;

        /// <summary>소형 크랩 등딱지 반폭(m). 등딱지 폭 0.22m.</summary>
        public const float CrabSmallHalfWidth = 0.11f;

        // ── 등딱지 제어점 ──
        // 아래 세 표의 숫자는 전부 **등딱지 반폭(unit)의 배수**다. 그래서 대왕(unit 0.80)과
        // 소형(unit 0.11)이 같은 표 하나를 공유하고, 두 크기의 비율이 구조적으로 어긋날 수 없다.
        // z = 앞뒤 위치(+가 앞) · half = 그 위치의 반폭 · dome = 등이 볼록하게 솟는 높이.
        //
        // 위에서 본 실루엣: 뒤 0.325 → 앞 1.0으로 넓어지는 **사다리꼴**이고(앞쪽이 넓다), 맨 앞
        // 한 줄만 0.825로 깎아 모서리가 칼처럼 뾰족해지는 것을 막는다. 가로:세로 = 1.60 : 0.96 = 1.67.
        // 옆에서 본 실루엣: 최고점이 반폭의 0.375배(대왕 0.30m)뿐인 **낮고 넓은** 지붕이다.
        // 게와 거미를 가르는 첫 번째 신호가 이 "넓고 낮다"이므로 dome을 함부로 올리지 말 것.
        private static readonly float[] CrabShellZ = { -0.55f, -0.40f, -0.20f, 0f, 0.20f, 0.375f, 0.525f, 0.65f };
        private static readonly float[] CrabShellHalf = { 0.325f, 0.50f, 0.6875f, 0.825f, 0.925f, 0.9875f, 1f, 0.825f };
        private static readonly float[] CrabShellDome = { 0.125f, 0.2375f, 0.325f, 0.375f, 0.375f, 0.325f, 0.2375f, 0.125f };

        /// <summary>등딱지 아랫면의 깊이(테두리 평면 아래, unit 배수). 껍질에 두께가 있어야 옆에서 "테"가 보인다.</summary>
        private const float CrabShellUnderside = 0.2125f;

        /// <summary>
        /// 집게 어깨(몸통 부착점, unit 배수 · 오른쪽 기준). 집게 크기 배수는 **이 점을 중심으로** 곱하므로,
        /// 작은 집게(왼쪽)도 어깨만은 항상 같은 자리에 붙어 있다.
        /// </summary>
        private static readonly Vector3 CrabClawShoulder = new Vector3(0.65f, -0.025f, 0.525f);

        /// <summary>다리 4쌍의 부착 z(unit 배수)와 쌍별 길이 배수. 앞다리가 가장 길고 뒤로 갈수록 짧아진다.</summary>
        private static readonly float[] CrabLegZ = { 0.325f, 0.075f, -0.175f, -0.40f };
        private static readonly float[] CrabLegLength = { 1f, 0.95f, 0.87f, 0.78f };

        /// <summary>대왕 전용 등딱지 돌기 8개의 위치(u = 앞뒤 0~1, t = 좌우 -1~1).</summary>
        private static readonly float[] CrabBumpU = { 0.30f, 0.30f, 0.52f, 0.52f, 0.72f, 0.72f, 0.88f, 0.88f };
        private static readonly float[] CrabBumpT = { -0.60f, 0.60f, -0.80f, 0.80f, -0.88f, 0.88f, -0.52f, 0.52f };

        /// <summary>게 한 마리를 만드는 데 필요한 값 묶음(전부 미터 또는 unit 배수).</summary>
        private struct CrabShape
        {
            public float unit;         // 등딱지 반폭(m). 모든 좌표의 기준 단위.
            public float ground;       // 지면의 y(m, 피벗 기준이라 음수). 발끝이 정확히 여기 닿는다.
            public float clawScale;    // 집게 크기 배수(어깨 기준). 대왕이 크다.
            public float legThickness; // 다리 굵기 배수. 작은 개체는 다리가 실처럼 사라지지 않게 굵힌다.
            public bool battleScars;   // 등딱지 돌기 + 긁힌 자국(대왕 전용).
            public int slices;         // 등딱지 앞뒤 분할 수.
            public int lateral;        // 등딱지 좌우 분할 수.
        }

        /// <summary>
        /// 게 몸통 한 장(등딱지 + 집게 2 + 다리 8 + 눈자루 2). 파츠는 눈알 2개뿐이다.
        ///
        /// 규격: 대왕 = 큐브(localScale 1.6 × 0.9 × 1.4 · 피벗 지면 위 0.45m → **지면 = -0.45**),
        ///       소형 = 구(localScale 0.30 × 0.18 × 0.30 · 피벗 지면 위 0.062m → **지면 = -0.062**).
        /// 좌표는 미터로 작성하고 마지막에 규격으로 나눈다(이 파일의 공통 규칙).
        /// 몸통에 회전이 없어서(rotationEuler 0) 미터 좌표의 뜻이 그대로다: +y = 위, +z = 앞, +x = 오른쪽.
        ///
        /// 실루엣을 만드는 네 가지:
        ///   1. 넓고 낮은 사다리꼴 등딱지 - 위에서 보면 앞이 넓고(폭:길이 = 1.67:1), 옆에서 보면 높이가
        ///      폭의 0.19배뿐이다. 이 "낮다"가 없으면 거미로 읽힌다.
        ///   2. 크기가 다른 집게 2개 - 오른쪽이 크고 왼쪽은 그 0.62배다. 좌우 대칭이면 게로 안 읽힌다.
        ///      집게발 두 개는 끝 사이가 반폭의 0.44배만큼 **벌어져** 있다(닫힌 집게는 몽둥이로 보인다).
        ///   3. 다리 8개의 꺾인 관절 - 몸 옆에서 등딱지 위(+0.20 unit)까지 올라갔다가 바깥으로 꺾여
        ///      내려와 발끝이 지면에 닿는다. 곧은 막대 8개면 그것도 거미다.
        ///   4. 자루눈 2개 - 등딱지 앞쪽 위로 반폭의 0.39배만큼 솟는다(눈알만 별도 파츠).
        ///
        /// 대왕과 소형의 차이(단순 확대가 아니다):
        ///   | 항목        | 대왕            | 소형            |
        ///   | 등딱지 폭   | 1.60m           | 0.22m           |
        ///   | 집게 배수   | 1.00 (상대적으로 크다) | 0.72     |
        ///   | 다리 굵기   | 1.00            | 1.35 (작아도 보이게) |
        ///   | 등딱지 표면 | 돌기 8 + 긁힌 자국 3줄 | 매끈함    |
        ///   | 분할 수     | 15 × 13         | 9 × 9           |
        /// </summary>
        public static Mesh CrabBodyUnit(bool giant)
        {
            string key = giant ? "crabGiant" : "crabSmall";
            Mesh cached;
            if (TryGetCached(key, out cached))
                return cached;

            var shape = new CrabShape
            {
                unit = giant ? CrabGiantHalfWidth : CrabSmallHalfWidth,
                ground = giant ? -CreatureVisualBuilder.CrabGiantGroundOffset : -CreatureVisualBuilder.CrabSmallGroundOffset,
                clawScale = giant ? 1f : 0.72f,
                legThickness = giant ? 1f : 1.35f,
                battleScars = giant,
                slices = giant ? 15 : 9,
                lateral = giant ? 13 : 9,
            };

            var builder = new Builder();
            AddCrabShell(builder, shape);

            for (int i = 0; i < CrabLegZ.Length; i++)
            {
                AddCrabLeg(builder, shape, 1f, CrabLegZ[i], CrabLegLength[i]);
                AddCrabLeg(builder, shape, -1f, CrabLegZ[i], CrabLegLength[i]);
            }

            AddCrabClaw(builder, shape, 1f, shape.clawScale);          // 큰 집게(오른쪽)
            AddCrabClaw(builder, shape, -1f, shape.clawScale * 0.62f); // 작은 집게(왼쪽)

            AddCrabEyestalk(builder, shape, 1f);
            AddCrabEyestalk(builder, shape, -1f);

            if (shape.battleScars)
                AddCrabBumps(builder, shape);

            Vector3 nominal = giant
                ? CreatureVisualBuilder.CrabGiantBodyScale
                : CreatureVisualBuilder.CrabSmallBodyScale;
            builder.ScaleVertices(new Vector3(1f / nominal.x, 1f / nominal.y, 1f / nominal.z));
            return Store(key, builder.Finish(giant ? "Cre_GiantCrabBody" : "Cre_SmallCrabBody"));
        }

        /// <summary>등딱지 제어 표를 u(0 = 뒤, 1 = 앞)에서 선형 보간해 읽는다.</summary>
        private static void SampleCrabShell(float u, out float z, out float half, out float dome)
        {
            int last = CrabShellZ.Length - 1;
            float f = Mathf.Clamp01(u) * last;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, last - 1);
            float t = f - i;
            z = Mathf.Lerp(CrabShellZ[i], CrabShellZ[i + 1], t);
            half = Mathf.Lerp(CrabShellHalf[i], CrabShellHalf[i + 1], t);
            dome = Mathf.Lerp(CrabShellDome[i], CrabShellDome[i + 1], t);
        }

        /// <summary>지정한 z(unit 배수)에서의 등딱지 반폭(unit 배수). 다리 부착점을 껍질에 붙이는 데 쓴다.</summary>
        private static float CrabHalfWidthAtZ(float z)
        {
            int last = CrabShellZ.Length - 1;
            for (int i = 0; i < last; i++)
            {
                if (z <= CrabShellZ[i + 1])
                    return Mathf.Lerp(CrabShellHalf[i], CrabShellHalf[i + 1], Mathf.InverseLerp(CrabShellZ[i], CrabShellZ[i + 1], z));
            }
            return CrabShellHalf[last];
        }

        /// <summary>
        /// 등딱지 윗면의 점 하나(미터). u = 앞뒤(0~1), t = 좌우(-1~1).
        /// 단면은 반원이 아니라 dome × sqrt(1-t²) 곡선이라, 가운데가 부드럽게 볼록하고 가장자리(t = ±1)에서
        /// 높이가 정확히 0이 된다 - 그래서 테두리와 아랫면이 틈 없이 붙는다.
        /// </summary>
        private static Vector3 CrabShellPoint(CrabShape s, float u, float t)
        {
            float z, half, dome;
            SampleCrabShell(u, out z, out half, out dome);

            float nx = half * t;
            float y = dome * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            if (s.battleScars)
                y = Mathf.Max(0f, y - CrabScarDepth(nx, z)); // 0 아래로는 파지 않는다(테두리가 뒤집히면 껍질이 갈라진다)

            return new Vector3(nx * s.unit, y * s.unit, z * s.unit);
        }

        /// <summary>
        /// 대왕 등딱지에 파인 긁힌 자국 3줄의 깊이(unit 배수). 파츠(검은 띠)가 아니라 **등딱지 자체를 판다** -
        /// B28/B29에서 대나무 마디·뱀 비늘을 파츠에서 반지름 변화로 옮긴 것과 같은 기법이다.
        /// </summary>
        private static float CrabScarDepth(float nx, float nz)
        {
            float scar = 0f;
            scar += CrabGroove(nx, nz, -0.10f, 0.42f, 0.95f, -0.55f, 1.05f, 0.075f, 0.055f);
            scar += CrabGroove(nx, nz, 0.12f, 0.30f, 0.80f, -0.75f, 0.85f, 0.060f, 0.045f);
            scar += CrabGroove(nx, nz, -0.55f, -0.30f, 0.35f, 0.95f, 0.70f, 0.055f, 0.040f);
            return scar;
        }

        /// <summary>
        /// 선분 하나를 따라 파인 홈의 깊이. 가로 방향은 가우시안, 끝은 선형으로 잦아들어 계단이 생기지 않는다.
        /// 난수를 쓰지 않으므로(재현성 규칙) 어떤 분할 수에서도 같은 자국이 나온다.
        /// </summary>
        private static float CrabGroove(float nx, float nz, float px, float pz, float dx, float dz,
            float length, float halfWidth, float depth)
        {
            float magnitude = Mathf.Sqrt(dx * dx + dz * dz);
            if (magnitude < 0.0001f || halfWidth <= 0f)
                return 0f;

            dx /= magnitude;
            dz /= magnitude;

            float ox = nx - px;
            float oz = nz - pz;
            float along = ox * dx + oz * dz;
            float across = ox * -dz + oz * dx;

            float endFade = Mathf.Clamp01(Mathf.Min(along, length - along) / (halfWidth * 2f));
            if (endFade <= 0f)
                return 0f;

            float ratio = across / halfWidth;
            return depth * endFade * Mathf.Exp(-ratio * ratio);
        }

        /// <summary>등딱지 껍질(윗면 + 아랫면 + 테두리 4면)을 메시에 굽는다.</summary>
        private static void AddCrabShell(Builder builder, CrabShape s)
        {
            int slices = Mathf.Max(3, s.slices);
            int lateral = Mathf.Max(3, s.lateral);
            float under = CrabShellUnderside * s.unit;

            var top = new Vector3[slices, lateral];
            var bottom = new Vector3[slices, lateral];
            for (int i = 0; i < slices; i++)
            {
                float u = (float)i / (slices - 1);
                for (int j = 0; j < lateral; j++)
                {
                    float t = (float)j / (lateral - 1) * 2f - 1f;
                    Vector3 point = CrabShellPoint(s, u, t);
                    top[i, j] = point;
                    bottom[i, j] = new Vector3(point.x, -under, point.z);
                }
            }

            for (int i = 0; i + 1 < slices; i++)
            {
                for (int j = 0; j + 1 < lateral; j++)
                {
                    builder.AddQuad(top[i, j], top[i, j + 1], top[i + 1, j + 1], top[i + 1, j], Vector3.up, false);
                    builder.AddQuad(bottom[i, j], bottom[i, j + 1], bottom[i + 1, j + 1], bottom[i + 1, j], Vector3.down, false);
                }
            }

            // 좌우 테두리(윗면 가장자리 y = 0 → 아랫면). 옆에서 봤을 때 껍질 두께로 읽히는 부분이다.
            for (int i = 0; i + 1 < slices; i++)
            {
                builder.AddQuad(top[i, 0], top[i + 1, 0], bottom[i + 1, 0], bottom[i, 0], Vector3.left, false);
                builder.AddQuad(top[i, lateral - 1], top[i + 1, lateral - 1],
                    bottom[i + 1, lateral - 1], bottom[i, lateral - 1], Vector3.right, false);
            }

            // 앞뒤 끝면.
            for (int j = 0; j + 1 < lateral; j++)
            {
                builder.AddQuad(top[0, j], top[0, j + 1], bottom[0, j + 1], bottom[0, j], Vector3.back, false);
                builder.AddQuad(top[slices - 1, j], top[slices - 1, j + 1],
                    bottom[slices - 1, j + 1], bottom[slices - 1, j], Vector3.forward, false);
            }
        }

        /// <summary>
        /// 다리 하나(부착 → 무릎 → 발목 → 발끝). 무릎이 등딱지 위(+0.20 unit)까지 솟았다가 바깥으로
        /// 꺾여 내려오는 **관절**이 게 다리의 전부다. 발끝은 굵기까지 계산해 지면(s.ground)에 정확히 닿는다.
        /// </summary>
        private static void AddCrabLeg(Builder builder, CrabShape s, float side, float z, float lengthFactor)
        {
            float u = s.unit;
            float half = CrabHalfWidthAtZ(z);
            float thickness = s.legThickness;
            float footRadius = 0.020f * u * thickness;

            Vector3[] leg =
            {
                new Vector3(side * (half - 0.05f) * u, -0.125f * u, z * u),
                new Vector3(side * (half + 0.375f * lengthFactor) * u, 0.200f * u * lengthFactor, (z + 0.02f) * u),
                new Vector3(side * (half + 0.525f * lengthFactor) * u, -0.275f * u, (z - 0.03f) * u),
                new Vector3(side * (half + 0.455f * lengthFactor) * u, s.ground + footRadius, (z - 0.05f) * u),
            };
            float[] radii =
            {
                0.075f * u * thickness,
                0.058f * u * thickness,
                0.040f * u * thickness,
                footRadius,
            };
            builder.AddTube(leg, radii, 5, true, true, 1f, Vector3.one);
        }

        /// <summary>
        /// 집게 하나(위팔 → 팔꿈치 → 손목 → 납작한 손바닥 → 벌린 집게발 2개).
        /// scale은 어깨(CrabClawShoulder)를 중심으로 곱하므로, 작게 만들어도 어깨는 몸통에 붙어 있다.
        /// </summary>
        private static void AddCrabClaw(Builder builder, CrabShape s, float side, float scale)
        {
            float radiusUnit = s.unit * scale;

            builder.AddTube(new[]
            {
                CrabClawPoint(s, side, scale, new Vector3(0.650f, -0.025f, 0.525f)),
                CrabClawPoint(s, side, scale, new Vector3(1.075f, -0.125f, 0.775f)),
                CrabClawPoint(s, side, scale, new Vector3(0.980f, -0.075f, 0.960f)),
            }, new[] { 0.125f * radiusUnit, 0.113f * radiusUnit, 0.100f * radiusUnit }, 6, true, true, 1f, Vector3.one);

            // 손바닥: 위아래로 납작한 판(두께 0.225 unit). 게 집게의 "두툼함"이 여기서 나온다.
            builder.AddBlade(new[]
            {
                CrabClawPoint(s, side, scale, new Vector3(0.825f, -0.075f, 0.900f)),
                CrabClawPoint(s, side, scale, new Vector3(1.175f, -0.075f, 0.975f)),
                CrabClawPoint(s, side, scale, new Vector3(1.250f, -0.075f, 1.325f)),
                CrabClawPoint(s, side, scale, new Vector3(0.925f, -0.075f, 1.375f)),
            }, Vector3.up, 0.225f * radiusUnit);

            // 벌린 집게발 2개. 끝 사이가 0.44 unit(대왕 0.35m) 벌어져 있어 멀리서도 "집게"로 읽힌다.
            builder.AddTube(new[]
            {
                CrabClawPoint(s, side, scale, new Vector3(0.930f, -0.075f, 1.330f)),
                CrabClawPoint(s, side, scale, new Vector3(0.870f, -0.040f, 1.800f)),
            }, new[] { 0.088f * radiusUnit, 0.014f * radiusUnit }, 5, true, true, 1f, Vector3.one);
            builder.AddTube(new[]
            {
                CrabClawPoint(s, side, scale, new Vector3(1.200f, -0.075f, 1.310f)),
                CrabClawPoint(s, side, scale, new Vector3(1.310f, -0.075f, 1.750f)),
            }, new[] { 0.080f * radiusUnit, 0.014f * radiusUnit }, 5, true, true, 1f, Vector3.one);
        }

        /// <summary>집게 좌표(unit 배수, 오른쪽 기준)를 어깨 기준으로 scale배 한 뒤 미터/좌우로 옮긴다.</summary>
        private static Vector3 CrabClawPoint(CrabShape s, float side, float scale, Vector3 raw)
        {
            Vector3 scaled = CrabClawShoulder + (raw - CrabClawShoulder) * scale;
            return new Vector3(side * scaled.x * s.unit, scaled.y * s.unit, scaled.z * s.unit);
        }

        /// <summary>자루눈 하나. 등딱지 앞쪽 표면에서 솟는다(끝의 눈알은 파츠라 여기 없다).</summary>
        private static void AddCrabEyestalk(Builder builder, CrabShape s, float side)
        {
            Vector3 root = CrabShellPoint(s, 0.762f, side * 0.200f);
            root.y -= 0.030f * s.unit; // 뿌리를 껍질 속에 살짝 묻어 이음매에 틈이 보이지 않게 한다.
            Vector3 tip = new Vector3(side * 0.2375f * s.unit, 0.625f * s.unit, 0.500f * s.unit);
            builder.AddTube(new[] { root, tip }, new[] { 0.056f * s.unit, 0.044f * s.unit }, 5, true, true, 1f, Vector3.one);
        }

        /// <summary>대왕 등딱지의 돌기 8개(바깥·위를 향한 짧은 뿔). 표면 함수로 뿌리를 잡아 항상 껍질에 붙는다.</summary>
        private static void AddCrabBumps(Builder builder, CrabShape s)
        {
            for (int i = 0; i < CrabBumpU.Length; i++)
            {
                Vector3 surface = CrabShellPoint(s, CrabBumpU[i], CrabBumpT[i]);
                Vector3 outward = new Vector3(CrabBumpT[i] * 0.45f, 1f, 0f).normalized;
                Vector3 root = surface - outward * (0.030f * s.unit);
                Vector3 tip = surface + outward * (0.115f * s.unit);
                builder.AddTube(new[] { root, tip }, new[] { 0.072f * s.unit, 0.008f * s.unit }, 5, true, true, 1f, Vector3.one);
            }
        }

        // ── 메시 빌더 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 정점/UV/삼각형을 모아 메시 하나로 마무리하는 최소 빌더.
        /// ResourceVisualLibrary.MeshBuilder와 같은 계보지만 두 가지가 다르다:
        ///  - AddTube가 **링마다 프레임을 새로 계산**한다(전갈 꼬리처럼 90° 넘게 휘는 관을 위해).
        ///    시작 프레임을 다음 링으로 투영해 이어가므로 관이 꼬이지(twist) 않는다.
        ///  - AddBlade(납작한 날 = 지느러미/집게/돌촉)와 Translate/ScaleVertices가 추가됐다.
        /// 삼각형을 넣을 때마다 기하 법선을 기준 방향과 비교해 감김을 바로잡으므로, 좌표계 손잡이
        /// 방향을 착각해도 안쪽으로 뒤집히지 않는다.
        /// </summary>
        private class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            /// <summary>
            /// 중심선(centers)과 반지름(radii)을 따라가는 관을 하나 잇는다.
            /// crossScale은 단면을 축별로 눌러 타원 단면을 만든다(예: 물고기는 좌우 0.62 / 상하 1.25).
            /// </summary>
            public void AddTube(Vector3[] centers, float[] radii, int sides, bool capStart, bool capEnd,
                float uvTile, Vector3 crossScale)
            {
                if (centers == null || radii == null || centers.Length < 2 || radii.Length != centers.Length || sides < 3)
                    return;

                int start = vertices.Count;
                int stride = sides + 1; // 이음매에서 UV가 끊기도록 정점을 한 개 겹쳐 둔다
                Vector3 previousRight = Vector3.zero;
                Vector3 firstTangent = Vector3.up;
                Vector3 lastTangent = Vector3.up;

                for (int r = 0; r < centers.Length; r++)
                {
                    Vector3 tangent;
                    if (r == 0)
                        tangent = centers[1] - centers[0];
                    else if (r == centers.Length - 1)
                        tangent = centers[r] - centers[r - 1];
                    else
                        tangent = centers[r + 1] - centers[r - 1];

                    if (tangent.sqrMagnitude < 0.0000001f)
                        tangent = Vector3.up;
                    tangent = tangent.normalized;

                    if (r == 0)
                        firstTangent = tangent;
                    if (r == centers.Length - 1)
                        lastTangent = tangent;

                    Vector3 right;
                    if (r == 0)
                    {
                        Vector3 helper = Mathf.Abs(tangent.y) > 0.9f ? Vector3.forward : Vector3.up;
                        right = Vector3.Cross(helper, tangent);
                    }
                    else
                    {
                        // 이전 프레임을 현재 접선에 투영해 이어간다(회전 최소화 프레임).
                        right = previousRight - tangent * Vector3.Dot(previousRight, tangent);
                        if (right.sqrMagnitude < 0.000001f)
                        {
                            Vector3 helper = Mathf.Abs(tangent.y) > 0.9f ? Vector3.forward : Vector3.up;
                            right = Vector3.Cross(helper, tangent);
                        }
                    }

                    if (right.sqrMagnitude < 0.000001f)
                        right = Vector3.right;
                    right = right.normalized;
                    previousRight = right;
                    Vector3 forward = Vector3.Cross(tangent, right);

                    for (int s = 0; s <= sides; s++)
                    {
                        float angle = (float)s / sides * Mathf.PI * 2f;
                        Vector3 direction = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                        Vector3 offset = Vector3.Scale(direction * radii[r], crossScale);
                        vertices.Add(centers[r] + offset);
                        uvs.Add(new Vector2((float)s / sides, (float)r / (centers.Length - 1) * uvTile));
                    }
                }

                for (int r = 0; r + 1 < centers.Length; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a0 = start + r * stride + s;
                        int a1 = a0 + 1;
                        int b0 = a0 + stride;
                        int b1 = b0 + 1;
                        // 바깥 방향은 링 중심에서 정점으로 향하는 방향으로 직접 구한다(스케일된 단면에서도 정확하다).
                        Vector3 outward = (vertices[a0] - centers[r]) + (vertices[b1] - centers[r + 1]);
                        if (outward.sqrMagnitude < 0.0000001f)
                            outward = Vector3.up;
                        AddTriangle(a0, b0, b1, outward);
                        AddTriangle(a0, b1, a1, outward);
                    }
                }

                if (capStart)
                    AddCap(start, sides, centers[0], -firstTangent);
                if (capEnd)
                    AddCap(start + (centers.Length - 1) * stride, sides, centers[centers.Length - 1], lastTangent);
            }

            /// <summary>
            /// 다각형 윤곽선을 thicknessAxis 방향으로 얇게 밀어낸 "날"(지느러미/집게/돌촉)을 만든다.
            /// 윤곽선은 첫 점에서 부채꼴로 삼각분할하므로, 첫 점에서 모든 변이 보이는 모양이어야 한다.
            /// </summary>
            public void AddBlade(Vector3[] outline, Vector3 thicknessAxis, float thickness)
            {
                if (outline == null || outline.Length < 3 || thicknessAxis.sqrMagnitude < 0.0000001f)
                    return;

                Vector3 axis = thicknessAxis.normalized;
                Vector3 half = axis * (thickness * 0.5f);

                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < outline.Length; i++)
                    centroid += outline[i];
                centroid /= outline.Length;

                for (int i = 1; i + 1 < outline.Length; i++)
                {
                    AddFace(outline[0] + half, outline[i] + half, outline[i + 1] + half, axis);
                    AddFace(outline[0] - half, outline[i] - half, outline[i + 1] - half, -axis);
                }

                for (int i = 0; i < outline.Length; i++)
                {
                    Vector3 a = outline[i];
                    Vector3 b = outline[(i + 1) % outline.Length];
                    Vector3 edge = b - a;
                    Vector3 outward = Vector3.Cross(axis, edge);
                    if (outward.sqrMagnitude < 0.0000001f)
                        continue;

                    outward = outward.normalized;
                    if (Vector3.Dot(outward, (a + b) * 0.5f - centroid) < 0f)
                        outward = -outward;

                    AddQuad(a + half, b + half, b - half, a - half, outward, false);
                }
            }

            /// <summary>사각면 하나. doubleSided면 감김을 뒤집은 사본을 함께 넣어 양쪽에서 보이게 한다.</summary>
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference, bool doubleSided)
            {
                AddQuadFace(a, b, c, d, reference);
                if (doubleSided)
                    AddQuadFace(a, b, c, d, -reference);
            }

            /// <summary>평면 셰이딩용 삼각면 하나(정점을 공유하지 않아 면마다 각이 선다).</summary>
            public void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(new Vector2(a.x + 0.5f, a.z + 0.5f));
                uvs.Add(new Vector2(b.x + 0.5f, b.z + 0.5f));
                uvs.Add(new Vector2(c.x + 0.5f, c.z + 0.5f));
                AddTriangle(index, index + 1, index + 2, reference);
            }

            /// <summary>정점 전체를 축별로 눌러/늘려 프리미티브 로컬 규격으로 옮긴다(마지막에 한 번 호출).</summary>
            public void ScaleVertices(Vector3 scale)
            {
                for (int i = 0; i < vertices.Count; i++)
                    vertices[i] = Vector3.Scale(vertices[i], scale);
            }

            /// <summary>정점 전체를 평행 이동한다(접지 높이 맞춤용).</summary>
            public void Translate(Vector3 offset)
            {
                for (int i = 0; i < vertices.Count; i++)
                    vertices[i] = vertices[i] + offset;
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh();
                mesh.name = name;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private void AddQuadFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                AddTriangle(index, index + 1, index + 2, reference);
                AddTriangle(index, index + 2, index + 3, reference);
            }

            private void AddCap(int ringStart, int sides, Vector3 center, Vector3 reference)
            {
                int centerIndex = vertices.Count;
                vertices.Add(center);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s < sides; s++)
                    AddTriangle(centerIndex, ringStart + s, ringStart + s + 1, reference);
            }

            /// <summary>
            /// 삼각형 하나를 감김 방향까지 맞춰 넣는다. 기하 법선이 기준과 반대면 두 인덱스를 바꿔 넣는다.
            /// </summary>
            private void AddTriangle(int i0, int i1, int i2, Vector3 reference)
            {
                Vector3 geometric = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                if (Vector3.Dot(geometric, reference) < 0f)
                {
                    int swap = i1;
                    i1 = i2;
                    i2 = swap;
                }

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);
            }
        }
    }
}
