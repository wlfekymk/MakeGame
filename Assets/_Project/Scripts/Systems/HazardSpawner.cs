using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 위험 요소(HazardSource)들을 배치하는 스포너.
    /// 섬 규모가 클수록 위험 요소 등장 확률이 높아진다 (Stranded Deep 기준: 큰 섬일수록 위험도 큼).
    /// 플레이어가 불시착하는 시작 섬(isStartingIsland)에는 안전을 위해 위험 요소를 배치하지 않는다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — smallMultiplier ← hazardSmallMultiplier,
    /// mediumMultiplier ← hazardMediumMultiplier, largeMultiplier ← hazardLargeMultiplier,
    /// extraLargeMultiplier ← hazardExtraLargeMultiplier.
    /// 산포 반경(*ScatterRadius)은 Spec_15가 의도적으로 config에서 제외한 값이라 배선하지 않았다
    /// (밸런스가 아니라 월드 지형 스케일 종속값).
    /// 폴백은 해당 필드가 0 이하(미설정)일 때만 적용되므로 씬 직렬화 값이 항상 이긴다. 즉 이 배선은
    /// GetMultiplier의 기존 3단 우선순위에 중간 단계를 하나 끼워 넣는 것과 같다:
    /// 씬/인스펙터 값 > balanceConfig > IslandSizeMetrics(최후 폴백).
    /// [주의/실측 대조] Balance_SceneSnapshot.md 기준 씬 직렬화 값은 1/1.5/2/2.5인 반면, 이 스크립트의
    /// 코드 기본값과 SurvivalBalanceConfig는 B3-7 상향안(1/1.75/2.5/3.25)을 담고 있어 서로 다르다.
    /// 씬 값이 전부 양수라 폴백이 실행되지 않으므로 이 배선 자체는 실동작을 바꾸지 않지만, 어느 쪽이
    /// 최종 밸런스인지는 기획 결정 사항이다(씬 값 갱신은 디렉터 담당).
    /// </summary>
    public class HazardSpawner : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 섬 규모별 배율이 0 이하로(미설정) 남아있는 경우에 한해 config의" +
            " hazard*Multiplier 값을 대신 쓴다. 씬에 이미 의미 있는(양수) 값이 직렬화돼 있으면" +
            " 절대 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

        [System.Serializable]
        public class HazardEntry
        {
            [Tooltip("위험 요소 종류")]
            public HazardType type;

            [Tooltip("소형 섬 기준 기본 등장 확률(0~1). 규모가 커질수록 배율이 곱해진다.")]
            [Range(0f, 1f)]
            public float baseChance = 0.2f;
        }

        [Tooltip("섬에 등장 가능한 위험 요소 종류와 기본 확률 목록")]
        public List<HazardEntry> hazardEntries = new List<HazardEntry>();

        // 긴급 정정(#2 회귀 수정): 이 필드들을 한 차례 제거하고 IslandSizeMetrics 직접 호출로 바꿨었는데,
        // 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 이 필드들에 코드 기본값과 다른
        // 값(디자이너가 조정한 실제 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되었다. 필드를
        // 제거하면 Unity가 그 직렬화 값을 잃어버리고 조용히 코드 기본값으로 되돌아간다 - "스테이징 범위에
        // 씬 파일이 없다"는 것이 "프로젝트에 씬 파일이 없다"는 뜻이 아니었다. 필드명/타입/기본값을
        // 원래(리팩터링 이전) 그대로 복원해 씬 직렬화 값이 다시 정상적으로 바인딩되도록 되돌렸다.
        // IslandSizeMetrics는 삭제하지 않고, 이 필드가 의미 있게 설정되지 않았을 때(0 이하)만 쓰는
        // "폴백 단일 소스"로 역할을 낮췄다 (GetMultiplier/GetScatterRadius 참고).
        // B3-7: 기획 결정으로 1/1.5/2/2.5 → 1/1.75/2.5/3.25로 상향한다. 자원 배율(IslandResourceSpawner,
        // 씬 실측 1/2/3/4)과 비교했을 때 기존 위험 배율은 대형 섬 기준 보상÷위험 비율이 1.5배가 되어
        // 소형/중형 섬을 굳이 찾아갈 이유가 사라진다는 문제가 있었다. 그렇다고 자원과 같은 배율(4배)로
        // 올리면 배 제작 재료를 구하러 반드시 가야 하는 후반 동선(대형/특대 섬)이 지나치게 가혹해진다.
        // 절충안으로 자원 곡선(1/2/3/4)과 기존 위험 곡선(1/1.5/2/2.5)의 산술 평균을 택했다:
        // (1+1)/2=1, (2+1.5)/2=1.75, (3+2)/2=2.5, (4+2.5)/2=3.25.
        // 씬(SampleScene.unity)에도 1/1.5/2/2.5가 직렬화돼 있어 코드 기본값만으로는 반영되지 않는다 -
        // 디렉터가 씬 값을 직접 맞춘다(코디네이터 보고 [디렉터 조치 요청] 항목 참고). 자원 배율
        // (IslandResourceSpawner의 smallMultiplier 등)은 이번 변경 대상이 아니므로 손대지 않았다.
        [Header("섬 규모별 등장 확률 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 1.75f;
        public float largeMultiplier = 2.5f;
        public float extraLargeMultiplier = 3.25f;

        [Header("섬 규모별 산포 반경")]
        // 버그 수정 (#1006 - 섬 크기별 밀도 공식 정립 연장선): 예전에는 scatterRadius가 섬 규모와 무관한
        // 값 하나(100f)뿐이었다. WorldMapManager.GetSizeScale의 지형 반지름(50/90/140/200)과 어긋나 있어서,
        // 소형 섬에서는 위험 요소가 지형 밖(바다)에 배치될 수 있었고 특대 섬에서는 중심 근처로만 몰렸다.
        // IslandResourceSpawner와 동일하게 각 섬 지형 반지름의 80%에 맞춰 규모별 반경을 따로 뒀다.
        public float smallScatterRadius = 100f;
        public float mediumScatterRadius = 100f;
        public float largeScatterRadius = 100f;
        public float extraLargeScatterRadius = 100f;

        /// <summary>
        /// 초기화 시점에 balanceConfig 폴백을 적용한다. SpawnHazardsForIsland는 월드 생성 흐름에서
        /// 호출되므로, 그 전에 배율이 확정돼 있어야 한다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 배율만 골라 config 값으로 채운다.
        /// 판정 기준(0 이하 = 미설정)은 GetMultiplier/GetScatterRadius가 이미 쓰던 것과 동일하며,
        /// 폴백이 채운 뒤에도 여전히 0 이하로 남는 배율은 기존대로 IslandSizeMetrics가 처리한다.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (smallMultiplier <= 0f) smallMultiplier = balanceConfig.hazardSmallMultiplier;
            if (mediumMultiplier <= 0f) mediumMultiplier = balanceConfig.hazardMediumMultiplier;
            if (largeMultiplier <= 0f) largeMultiplier = balanceConfig.hazardLargeMultiplier;
            if (extraLargeMultiplier <= 0f) extraLargeMultiplier = balanceConfig.hazardExtraLargeMultiplier;
        }

        /// <summary>
        /// 지정한 섬에 규모와 확률에 따라 위험 요소를 배치한다. 시작 섬에는 배치하지 않는다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림으로
        /// 등장 확률 판정·산포 위치·크기/회전 지터를 전부 뽑는다(재현성 근거는 IslandResourceSpawner
        /// 상단 주석과 동일). 실제로 등장한 위험 요소마다 (island.islandId, spawnOrder) 식별자를 부여한다.
        /// </summary>
        public List<HazardSource> SpawnHazardsForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<HazardSource>();
            if (island == null || island.isStartingIsland)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);

            foreach (var entry in hazardEntries)
            {
                float chance = Mathf.Clamp01(entry.baseChance * multiplier);
                if (rng.NextValue01() <= chance)
                {
                    Vector2 offset = rng.NextInsideUnitCircle() * radius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleHazard(entry.type, position, parent, rng, island.islandId, spawnOrder));
                    spawnOrder++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// SharkSpawner처럼 섬이 아닌 곳(바다 한가운데)에 위험 요소를 배치해야 하는 다른 스포너가
        /// 이 클래스의 시각/전투 설정 테이블(GetVisualConfig, HazardSource.ConfigureForType)을 그대로
        /// 재사용할 수 있도록 공개한 진입점. 섬 배치(SpawnHazardsForIsland)와 달리 확률/섬 규모 개념이
        /// 없고, 호출자가 이미 정한 위치에 정확히 하나를 생성한다.
        /// B3-3: 호출자(SharkSpawner)가 자신만의 독립된 결정적 rng와 spawnOrder를 넘겨야 한다 - 섬에
        /// 속하지 않는 스폰이므로 islandIndex는 호출자가 판단해 넘긴다(SharkSpawner는 -1을 쓴다).
        /// </summary>
        public HazardSource SpawnHazardAtPosition(HazardType type, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            return SpawnSingleHazard(type, position, parent, rng, islandIndex, spawnOrder);
        }

        /// <summary>
        /// 위험 요소 하나를 실제로 생성한다. 종류별로 형태/크기/색상/회전이 다른 프리미티브를 사용해
        /// 플레이어가 캡슐 하나로는 구분할 수 없던 곰/식인종/독사/전갈/벌떼/함정/상어를 한눈에 구별할 수 있게 한다.
        /// </summary>
        private HazardSource SpawnSingleHazard(HazardType type, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            HazardVisualConfig config = GetVisualConfig(type);

            GameObject go = GameObject.CreatePrimitive(config.primitiveType);
            go.transform.SetParent(parent);

            // 퀄리티 개선(#325 재점검): 자원 노드/사냥감과 같은 문제 - 같은 종류의 위험 요소가 여러 섬에
            // 걸쳐 완전히 동일한 크기/방향으로 찍히는 것을 막기 위해 개체마다 살짝 다른 크기 배율과
            // 세워진 축(Y) 기준 방향을 추가로 준다. Trap처럼 대칭적인 원판은 시각적으로 티가 안 나지만
            // 해를 끼치지도 않으므로 모든 타입에 공통 적용해 코드를 단순하게 유지한다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 호출자가 넘긴 결정적 rng를 쓴다.
            float sizeJitter = rng.NextFloat(0.9f, 1.15f);
            Quaternion yawJitter = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            go.transform.localScale = config.localScale * sizeJitter;
            go.transform.rotation = yawJitter * Quaternion.Euler(config.rotationEuler);
            go.transform.position = position + Vector3.up * config.groundOffset;
            go.name = $"Hazard_{type}";

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(config.color);

            // 퀄리티 개선: 몸통 하나짜리 프리미티브만으로는 위험 요소가 밋밋해 보여, 종류별로
            // 알아볼 수 있는 작은 보조 파츠(눈, 벌떼 무리 등)를 덧붙인다.
            // sizeJitter가 적용된 실제 스케일(go.transform.localScale)을 넘겨야 보정 계산이 실제 배치된
            // 크기와 맞아떨어진다(config.localScale은 jitter 이전 원본값이라 그대로 쓰면 이후 유지보수 시
            // 혼동의 여지가 있어 명시적으로 실제 값을 전달한다).
            AddDetailParts(go, type, config, go.transform.localScale, rng);

            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            var hazard = go.AddComponent<HazardSource>();
            hazard.hazardType = type;
            hazard.ConfigureForType(); // 종류(곰/식인종/벌떼 등)에 맞춰 전투 가능 여부와 체력을 설정한다.
            hazard.islandIndex = islandIndex;
            hazard.spawnOrder = spawnOrder;
            return hazard;
        }

        /// <summary>
        /// 종류별로 몸통 프리미티브 하나로는 표현할 수 없던 디테일(눈, 벌떼 무리)을 자식 오브젝트로 추가한다.
        /// 자식의 localScale은 부모의 비균일 localScale(config.localScale)로 나눠 보정해, 몸통이
        /// 눌리거나 늘어난 축(예: 상어의 길쭉한 몸통)에서도 눈이 타원으로 찌그러지지 않고 둥글게 보이게 한다.
        /// </summary>
        private void AddDetailParts(GameObject go, HazardType type, HazardVisualConfig config, Vector3 appliedScale, System.Random rng)
        {
            Vector3 s = appliedScale;
            Color darkEye = new Color(0.05f, 0.05f, 0.05f);

            switch (type)
            {
                case HazardType.Bear:
                case HazardType.Cannibal:
                    // 몸통 캡슐 위쪽(머리 부근)에 작은 눈 두 개를 붙인다.
                    AddCompensatedSphere(go, new Vector3(0.18f, 0.75f, 0.35f), 0.09f, s, darkEye, "EyeL");
                    AddCompensatedSphere(go, new Vector3(-0.18f, 0.75f, 0.35f), 0.09f, s, darkEye, "EyeR");
                    break;

                case HazardType.Shark:
                    // 상어는 눕혀서 배치되므로(로컬 Y가 몸통 진행 방향) 머리 쪽에 눈을 붙인다.
                    AddCompensatedSphere(go, new Vector3(0.22f, 0.7f, 0f), 0.07f, s, darkEye, "EyeL");
                    AddCompensatedSphere(go, new Vector3(-0.22f, 0.7f, 0f), 0.07f, s, darkEye, "EyeR");
                    // 등지느러미: 작은 원뿔 대신 얇은 큐브로 단순하게 표현.
                    //
                    // B4-3(축 정정 + 확대 + 색): 세 가지를 함께 고쳤다.
                    // (1) 축 — 상어 몸통은 rotationEuler(0,0,90)으로 눕혀져 있어서 "로컬 +X가 월드 위쪽,
                    //     로컬 +Y가 몸통 진행 방향, 로컬 +Z가 좌우"가 된다(Unity의 Z축 +90도 회전은
                    //     +X→+Y, +Y→-X). 기존 값은 로컬 +Z로 0.32 밀고 두께 0.06을 로컬 X(=월드 수직)에
                    //     줬기 때문에, 등지느러미가 아니라 옆구리에 붙은 납작한 판이었고 수직 높이가
                    //     사실상 0이었다. 그래서 "지느러미가 작아 안 보인다"는 지적은 크기 이전에
                    //     방향 문제였다. 이제 로컬 +X(월드 위)로 세운다.
                    // (2) 크기 — 최대 치수 0.22 → 0.42(약 1.9배), 두께 0.06 → 0.10(약 1.7배)로
                    //     지시받은 1.5~2배 범위 안에서 키웠다.
                    // (3) 색 — Danger Red #CC3333(ArtDirection 1.1). 근거는 아래 finColor 주석 참고.
                    //
                    // 주의: 이 파츠는 CreateVisualPart가 콜라이더를 제거한 순수 시각 오브젝트다.
                    // 판정은 몸통 캡슐의 트리거 콜라이더 하나뿐이라 지느러미를 키워도 공격 범위는
                    // 1mm도 변하지 않는다(시각/판정이 이미 분리되어 있음을 확인함).
                    Color finColor = new Color(0.8f, 0.2f, 0.2f); // Danger Red #CC3333
                    AddCompensatedBox(go, new Vector3(0.4f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.1f), s, finColor, "Fin");
                    break;

                case HazardType.BeeSwarm:
                    // 공 하나가 아니라 작은 벌 여러 마리가 뭉쳐 있는 것처럼 보이도록 주변에 작은 구체를 흩뿌린다.
                    // B3-3: 시드 없는 UnityEngine.Random 대신 호출자가 넘긴 결정적 rng를 쓴다.
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 offset = new Vector3(
                            rng.NextFloat(-0.8f, 0.8f),
                            rng.NextFloat(-0.8f, 0.8f),
                            rng.NextFloat(-0.8f, 0.8f));
                        AddCompensatedSphere(go, offset, 0.22f, s, config.color, $"Bee{i}");
                    }
                    break;
            }

            // 연결(A-1): tech-artist가 만든 CreatureVisualBuilder.AddHazardDetailsIfMissing을 호출해
            // 독사(VenomousSnake)/전갈(Scorpion)/함정(Trap)에 보조 디테일(혀/꼬리·집게/가시)을 추가한다.
            // 곰/식인종/상어/벌떼는 위 switch에서 이미 자체 디테일을 만들었고 CreatureVisualBuilder는
            // 그 네 종류에는 아무 것도 하지 않으므로(직접 확인함) 중복 없이 안전하게 이어붙일 수 있다.
            CreatureVisualBuilder.AddHazardDetailsIfMissing(go, type, s, config.color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 구체 파츠를 만든다(둥근 형태 유지용).
        /// </summary>
        private void AddCompensatedSphere(GameObject parent, Vector3 localPos, float worldRadius, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.x),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.y),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.z));
            StructureVisualBuilder.CreateVisualPart(parent.transform, name, PrimitiveType.Sphere, localPos, compScale, color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 박스 파츠를 만든다(지느러미 등 납작한 형태용).
        /// </summary>
        private void AddCompensatedBox(GameObject parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            StructureVisualBuilder.CreateVisualPart(parent.transform, name, PrimitiveType.Cube, localPos, compScale, color);
        }

        /// <summary>
        /// 위험 요소 시각 정보(프리미티브 종류, 크기, 회전, 색상, 지면으로부터 띄울 높이)를 담는 구조체.
        /// </summary>
        private struct HazardVisualConfig
        {
            public PrimitiveType primitiveType;
            public Vector3 localScale;
            public Vector3 rotationEuler;
            public Color color;
            public float groundOffset;
        }

        /// <summary>
        /// 위험 요소 종류별로 구분 가능한 형태/크기/색상을 반환한다.
        /// 곰=크고 진한 갈색 캡슐, 식인종=사람 크기의 적갈색 캡슐, 독사=길고 납작한 초록 캡슐(눕혀서 배치),
        /// 전갈=작고 납작한 어두운 주황 캡슐, 벌떼=작은 노란 구체, 함정=땅에 깔린 어두운 회갈색 원판.
        /// </summary>
        private HazardVisualConfig GetVisualConfig(HazardType type)
        {
            switch (type)
            {
                case HazardType.Bear:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.9f, 1.1f, 0.9f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.32f, 0.2f, 0.12f), // 진한 갈색
                        groundOffset = 1.1f
                    };

                case HazardType.Cannibal:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.55f, 0.9f, 0.55f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.6f, 0.35f, 0.25f), // 적갈색
                        groundOffset = 0.9f
                    };

                case HazardType.VenomousSnake:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.18f, 0.6f, 0.18f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 길게 배치
                        color = new Color(0.15f, 0.55f, 0.2f), // 초록
                        groundOffset = 0.1f
                    };

                case HazardType.Scorpion:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.16f, 0.3f, 0.16f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 낮고 짧게 배치
                        color = new Color(0.45f, 0.22f, 0.05f), // 어두운 주황/흙색
                        groundOffset = 0.09f
                    };

                case HazardType.BeeSwarm:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Sphere,
                        localScale = new Vector3(0.5f, 0.5f, 0.5f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.95f, 0.75f, 0.1f), // 노란색
                        groundOffset = 1.4f // 벌떼는 공중에 떠 있게
                    };

                case HazardType.Trap:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Cylinder,
                        localScale = new Vector3(0.6f, 0.04f, 0.6f), // 얇은 원판 형태로 땅에 깔아둔다
                        rotationEuler = Vector3.zero,
                        color = new Color(0.35f, 0.3f, 0.25f), // 어두운 회갈색
                        groundOffset = 0.04f
                    };

                case HazardType.Shark:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.45f, 1.4f, 0.45f), // 길쭉하게 눕혀서 상어 몸통처럼 보이게
                        rotationEuler = new Vector3(0f, 0f, 90f),
                        color = new Color(0.28f, 0.35f, 0.42f), // 어두운 청회색
                        groundOffset = 0f // SharkSpawner가 이미 해수면 아래 정확한 위치를 계산해 넘겨준다
                    };

                default:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.6f, 0.6f, 0.6f),
                        rotationEuler = Vector3.zero,
                        color = Color.gray,
                        groundOffset = 0.6f
                    };
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 위험 요소 등장 확률 배율을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0
        /// 이하로 남아있어(설정 실수/아직 배치 안 된 새 컴포넌트 등) 의미 있게 설정되지 않은 경우에만
        /// IslandSizeMetrics.GetLinearDensityMultiplier를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Medium: return mediumMultiplier > 0f ? mediumMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Large: return largeMultiplier > 0f ? largeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.ExtraLarge: return extraLargeMultiplier > 0f ? extraLargeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                default: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 위험 요소 산포 반경을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0 이하로
        /// 남아있을 때만 IslandSizeMetrics.GetScatterRadius를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetScatterRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Medium: return mediumScatterRadius > 0f ? mediumScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Large: return largeScatterRadius > 0f ? largeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.ExtraLarge: return extraLargeScatterRadius > 0f ? extraLargeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                default: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
            }
        }
    }
}
