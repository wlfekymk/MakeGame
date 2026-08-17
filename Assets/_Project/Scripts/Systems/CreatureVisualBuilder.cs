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
    public static partial class CreatureVisualBuilder
    {
        // ── 스포너가 쓰는 몸통 localScale (jitter 이전 원본값) ────────────────────────────
        // HazardSpawner.GetVisualConfig / CreatureSpawner.SpawnSingleCreature와 같은 값이어야 한다.
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
            ApplySharedMaterial(go, Shared(color, textureName));
        }

        /// <summary>
        /// [B36] 위 메서드의 공통부. 이미 만들어 둔 공유 머티리얼을 그대로 물릴 때 쓴다
        /// (곰 모델처럼 색+텍스처 조합이 아니라 텍스처 세트 전체가 하나로 묶인 경우).
        /// 예전 머티리얼 파괴 판정은 위와 **완전히 같은 가드**를 쓴다 - 아래 주석 참고.
        /// </summary>
        private static void ApplySharedMaterial(GameObject go, Material next)
        {
            if (go == null)
                return;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

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
}
