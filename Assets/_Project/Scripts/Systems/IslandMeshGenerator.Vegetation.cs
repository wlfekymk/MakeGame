using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    public static partial class IslandMeshGenerator
    {
        // ─────────────────────────────────────────────────────────────────────────
        //  섬 표면(지면 구분 + 초목)  —  "민둥산" 해소
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>초목/잔디 캡을 담는 루트 자식의 이름. 중복 생성 방지 판정에도 쓴다.</summary>
        public const string SurfaceRootName = "IslandSurface";

        /// <summary>
        /// 섬 하나에 배치할 수 있는 초목 인스턴스(야자수 1그루 / 덤불 1개 / 풀포기 1개를 각각 1로 센다)의
        /// 절대 상한. 특대 섬(반지름 200m)의 면적은 12만 m²가 넘어서, 밀도만 보고 배치하면 초목이 수천
        /// 개까지 늘어나 프레임이 죽는다. 공식이 나중에 바뀌더라도 이 상한이 항상 마지막에 한 번 더 강제된다.
        ///
        /// [B9 정정 — 이 주석은 거짓이었다] 직전 값은 180이었고 "규모별 개수 공식이 커봐야 정확히 이 값에
        /// 닿도록 잡혀 있다"고 적혀 있었는데 **사실이 아니었다**. 당시 공식의 최대 요청치는
        /// palm 16 + bush 40 + tuft 78 = 134라서 상한 180에 46 모자랐고, 아래 트림 블록은 **단 한 번도
        /// 발동한 적이 없는 도달 불가 코드**였다. 이 프로젝트는 틀린 주석이 실제 사고를 만든 전력이 있어
        /// (scatterRadius / 자원 배율) 주석을 사실에 맞추는 대신 **값을 주석에 맞춘다** — 아래 상한과
        /// 규모별 상한의 합을 정확히 일치시켜, 트림 블록이 실제로 살아 있는 가드가 되게 한다.
        ///
        /// 현재 값 284 = 야자수 80 + 덤불 48 + 풀포기 156 (전부 특대 섬 R=200에서의 상한).
        /// 즉 특대 섬은 정확히 이 값에 닿고, 누군가 공식을 조금이라도 올리는 순간 트림이 발동한다.
        ///
        /// [B49 디렉터 지시 "야자수를 5배로"] 220 → **284**(+64 = 야자수 상한 16 → 80의 증가분 그대로).
        /// 상한을 함께 올리지 않으면 아래 트림 블록이 발동해 **덤불·풀이 대신 깎인다**(B10 (2)에 적어 둔
        /// 바로 그 회귀). 덤불 48 / 풀포기 156은 한 글자도 건드리지 않았고, 등식
        /// "세 상한의 합 = 이 상한"도 그대로 유지된다.
        ///
        /// 예산 근거(특대 섬 실측, B10 줄기 프리즘 교체 후):
        ///   삼각형 8,016 (야자수 3,264 + 덤불 2,880 + 풀 1,872) — B9 10,512에서 **-24%**,
        ///   저폴리 교체 전 157,824 대비 **-95%**
        ///   렌더러 508 (16×13 + 48×3 + 156×1) — 프리즘 교체로 **변하지 않았다**(줄기 파츠 수 동일).
        ///
        /// [B48 모델 교체 후] 야자수가 그루당 렌더러 13 → **2**(줄기 1 + 크라운 1), 삼각형 204 →
        /// 1,388~1,784(palm_a/b/c)가 됐다. 특대 섬 기준 렌더러 508 → **332**(16×2 + 48×3 + 156×1),
        /// 삼각형 8,016 → 약 29,600(야자수 약 25,600 + 덤불 2,880 + 풀 1,872)이다. 바위 모델이
        /// 같은 이유로 이미 40,392를 쓰고 있으므로(B45) 같은 자릿수 안이고, 야자수 1그루는
        /// AssetPipeline 2장의 "대형 구조물 8,000" 상한 안이다.
        ///
        /// [B10 그루 수를 올리지 않는 이유 — B49에서 근거가 소멸했다] 당시 근거는 두 가지였다.
        ///   (1) 16을 정한 제약은 삼각형이 아니라 **렌더러 수**다(B8, 디렉터). 그루당 렌더러 13개는
        ///       프리즘 교체로 1개도 줄지 않았으므로 16을 올릴 근거가 새로 생기지 않았다.
        ///   (2) 16 + 48 + 156 = 220 = 이 상한과 정확히 같다. 야자수만 올리면 아래 트림 블록이 발동해
        ///       **덤불·풀이 대신 깎인다** - "야자수를 늘렸더니 숲이 성겨졌다"는 조용한 회귀가 된다.
        ///       그루 수를 올리려면 이 상한과 렌더러 예산을 함께 올려야 하고, 그것은 디렉터 결정이다.
        ///
        /// [B49] (1)의 전제가 B48에서 실제로 무너졌다 - 그루당 렌더러가 **13 → 2**다. 16그루가 쓰던
        /// 렌더러 예산 208개는 지금 80그루(160개)를 넣고도 48개가 남는다. 즉 "16"은 더 이상 렌더러
        /// 예산이 강제하는 값이 아니다. (2)는 여전히 참이므로 상한을 220 → 284로 함께 올렸다.
        /// 특대 섬 초목 렌더러: 236(16×2+48+156) → **300**(80×2+48+156). 바위·표류물(약 51)까지 더해도
        /// 351로, B29에 기록된 실측 463보다 여전히 **적다** - 즉 렌더러 총량은 이번에도 회귀가 아니다.
        /// 삼각형은 특대 섬 기준 약 34,900 → 약 137,300(야자수 80×약 1,600)으로 늘어난다. 이 값이
        /// 이번 변경에서 유일하게 "예전 최악치"(B9 이전 157,824)에 근접하는 축이다 - 디렉터에게 보고됨.
        /// </summary>
        public const int MaxVegetationInstancesPerIsland = 284;

        /// <summary>
        /// [B29] 섬 하나에 놓는 바위 무리의 절대 상한(무리 1개 = 렌더러 3~4개).
        ///
        /// 초목 상한(220)과 **일부러 분리한다.** 그 상한은 "야자수 16 + 덤불 48 + 풀포기 156 = 정확히 220"
        /// 이라는 등식 위에 서 있고(위 주석), 바위를 그 안에 넣으면 트림이 발동해 **초목이 대신 깎인다** -
        /// "바위를 넣었더니 숲이 성겨졌다"는 조용한 회귀가 된다. 예산은 따로 세우고 따로 갚는다.
        ///
        /// 갚은 내역(특대 섬, 반지름 200 기준):
        ///   렌더러 — 덤불 1개가 3 → 1로 줄었다(로브 3개를 메시 한 장에 구웠다). 48개 × -2 = **-96**.
        ///            바위 12무리 × 3.5 = +42, 표류물 +9 → **순 -45**(508 → 463).
        ///   삼각형 — 덤불 2,880 → 4,416(로브가 각져지고 잎끝 8장이 생겼다),
        ///            풀포기 3,120 → 6,240(잎이 2마디로 휘었다), 바위 +1,680, 표류물 +792.
        ///            합계 9,264 → 16,392. B9 이전(157,824)의 10.4%이고, 늘어난 몫은 전부
        ///            "화면에서 실제로 보이는 형태"에 들어갔다(ArtDirection 2장 디테일 밀도 규칙).
        /// </summary>
        public const int MaxRockClustersPerIsland = 12;

        /// <summary>[B29] 섬 하나에 놓는 표류물(궤짝/통/널판)의 절대 상한. 하나당 렌더러 1개다.</summary>
        public const int MaxDriftItemsPerIsland = 9;

        /// <summary>
        /// [B51] 섬 하나에 놓는 대형 석재(거암/잔해/겹바위/절벽)의 절대 상한. 하나당 렌더러 1개다.
        /// 초목·바위무리 상한과 분리하는 이유는 MaxRockClustersPerIsland 주석 그대로다 - 합산 상한에
        /// 넣으면 트림이 기존 장식을 대신 깎는 조용한 회귀가 된다. 예산은 따로 세우고 따로 갚는다.
        /// 값 33 = 특대 섬 규모별 상한의 합(거암 8 + 잔해 16 + 겹바위 4 + 절벽 5) - B9 정정의 규칙대로
        /// "합과 상한을 정확히 일치"시켜, 누군가 규모표를 올리는 순간 아래 트림 가드가 실제로 발동한다.
        /// 삼각형 최악(특대, 전부 로드): 거암 8×5,246 + 잔해 16×2,226 + 겹바위 4×4,454 + 절벽 5×7,028
        /// = 130,540(교대 정상 동작 시 mega_b가 섞여 121,436). B49의 야자수 총량(약 137,300)과 같은
        /// 자릿수이고, 콜라이더는 전부 convex(PhysX 255면 헐 자동 단순화)라 물리 비용은 면수와 무관하다.
        /// </summary>
        public const int MaxLargeStonesPerIsland = 33;

        /// <summary>
        /// 섬 지형 오브젝트 위에 (1) 내륙 풀밭 캡 메시와 (2) 초목(야자수/덤불/풀포기)을 배치한다.
        ///
        /// 왜 필요했나: 지형은 단색(당시 모래 #C2B280, B11부터 Meadow Green)으로 칠한 메시 하나뿐이고 초목을 만드는 코드는 프로젝트
        /// 어디에도 없었다(WorldMapManager.CreateDefaultTerrainMaterial / CreateProceduralIslandTerrain).
        /// 그래서 실제 게임에 들어가면 반지름 50~200m짜리 모래색 평지만 보였다.
        ///
        /// [콜라이더 정책 — "절대 금지"에서 "선별 허용"으로 (디렉터 지시: 바위에 올라가고 부딪히게)]
        /// 예전 규칙은 [콜라이더 절대 금지]였다. 원래 이유는 TerrainSampler.SnapToGround가 **단일**
        /// Raycast이던 시절 장식 콜라이더가 지형으로 오인돼 "불러오기 후 모든 아이템이 하늘로 떠오르는"
        /// 사고가 났기 때문이다. 그 전제는 이제 성립하지 않는다 — 지형을 조회하는 경로가 전부
        /// **이름/구조 필터를 갖춘 RaycastAll 계열**로 바뀌었다(전수 확인):
        ///   · TerrainSampler.SnapToGround: RaycastNonAlloc + "Island_" 접두사만 채택(TerrainSampler.cs:44-75)
        ///   · BuildingSystem.CastBuildRay: 지형/조각/갑판 외 히트는 통과(BuildingSystem.cs:1436-1448),
        ///     지면 프로브도 "Island_" 필터(BuildingSystem.cs:1727-1737)
        ///   · HazardSource.BearAI.SampleGroundY: SnapToGround 경유 + 실패 감지(HazardSource.BearAI.cs:681-)
        ///   · WeatherSystem.ProbeSplashSurfaceY: SnapToGround + "BuildPiece_" 필터(WeatherSystem.cs:567-585)
        /// 따라서 장식 콜라이더가 배치·건축·곰 이동을 오염시킬 경로가 구조적으로 없다.
        ///
        /// 지금 규칙: **큰 바위 덩어리(convex MeshCollider)와 야자수 줄기(CapsuleCollider)에만** 붙인다.
        ///   · 덤불·풀포기·곁돌·표류물·야자 크라운은 여전히 콜라이더 없음(통과) — 발에 걸리면 짜증나고,
        ///     수백 개라 물리 씬만 무거워진다(예전 (a) 우려는 이 선별로 계속 막는다).
        ///   · 새 콜라이더의 오브젝트 이름은 절대 "Island_"로 시작하면 안 된다(지형 오인 방지의 최후 보루).
        ///   · 콜라이더 추가는 rng를 한 번도 소비하지 않는다 — 배치 재현성 불변([결정성] 주석).
        ///   · BuildIslandSurface 끝에서 Physics.SyncTransforms()를 한 번 부른다(autoSyncTransforms=false,
        ///     AGENT_BRIEF 4장 — 같은 프레임의 후속 레이캐스트가 물리 씬을 결정적으로 보게 한다).
        /// 시각 파츠 생성은 여전히 콜라이더가 자동으로 생기지 않는 경로(공유 메시 + 빈 GameObject +
        /// MeshFilter/MeshRenderer, CreatePart)를 쓴다 — 콜라이더는 위 두 자리에만 **명시적으로** 단다.
        ///
        /// [결정성] 배치에 UnityEngine.Random을 일절 쓰지 않는다. 호출자가 넘긴 섬별 System.Random
        /// 스트림만 소비하며, 소비 횟수도 (반지름 → 개수)가 정해지면 고정이라 같은 worldSeed면 항상
        /// 같은 숲이 나온다(SeededRandomExtensions 상단 주석의 재현성 전제를 그대로 따른다).
        /// </summary>
        /// <param name="islandObject">WorldMapManager가 만든 섬 지형 오브젝트("Island_{id}_{size}").</param>
        /// <param name="radius">이 섬의 지형 반지름(m). IslandSizeMetrics.GetTerrainRadius 값.</param>
        /// <param name="rng">이 섬 전용 결정적 난수 스트림. 다른 스포너의 스트림과 반드시 분리돼 있어야 한다.</param>
        public static void BuildIslandSurface(GameObject islandObject, float radius, System.Random rng)
        {
            if (islandObject == null || rng == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 호출돼도 숲이 겹쳐 두 배로 자라지 않게 한다.
            if (islandObject.transform.Find(SurfaceRootName) != null)
                return;

            var root = new GameObject(SurfaceRootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 머티리얼은 섬당 4개만 만들어 그 섬의 모든 초목 파츠가 공유한다. StructureVisualBuilder.
            // CreateColorMaterial은 호출할 때마다 새 Material을 만들기 때문에, 파츠마다 부르면 섬 하나에
            // 400개가 넘는 고유 머티리얼이 생겨 SRP 배처가 전혀 묶지 못한다(자원 노드는 개수가 수십 개
            // 수준이라 문제되지 않았지만 초목은 자릿수가 다르다).
            //
            // [B8 색 교체] 이전에는 잎/덤불/풀을 전부 Palm Fiber(#948C4C, 올리브)의 명도 변주로 칠했는데,
            // 실기에서 야자수가 통째로 마른 나무처럼 보였다. 근거: Palm Fiber의 상대휘도는 137, 줄기에 쓰던
            // Driftwood(#8C6640)는 107 - 차이가 1.28배뿐인 데다 색상각도 55°/29°로 둘 다 노랑~주황 계열이라
            // 줄기와 잎이 한 덩어리로 뭉쳤다. ArtDirection 1.1에 초목 전용 Frond Green/Meadow Green을
            // 추가하고(디렉터 승인), 줄기는 Driftwood를 어둡게+진하게 눌러 명도 대비를 벌린다.
            // (Palm Fiber는 "수확한 마른 섬유" 아이템 색으로 의미를 유지한다 - 기존 8색의 뜻은 그대로다.)
            //
            // [B9 줄기 색 재조정] 직전 값 Shade(Driftwood, 0.78) = #6D5032 는 명도 대비(1.75배)는 얻었지만
            // 하늘을 배경으로 실루엣이 잡히면 거의 검은 막대로 보였다. 원인은 명도가 아니라 채도다 -
            // Shade()는 세 채널을 같은 비율로 곱하므로 HSV 채도(0.54)는 그대로 두고 명도만 0.549→0.427로
            // 깎는다. 그 결과 유채색량(chroma = max-min)이 76 → 59로 줄어, 밝은 배경 앞에서 색상 정보가
            // 남지 않는 "검은 실루엣"이 됐다. 그래서 이번에는 명도를 조금만 되돌리고(×0.93) 채도를
            // 20% 올려(#82582D) 어두운 채로도 "갈색"이 읽히게 한다.
            //   명도 V 0.427 → 0.510(+19%) · 채도 S 0.541 → 0.654 · chroma 59 → 85(+44%)
            //   상대휘도 84 → 94, 잎(Frond Green 147)과의 대비 1.75배 → 1.57배
            //   (실루엣이 뭉쳤던 예전 조합은 1.28배, 순정 Driftwood라도 1.37배뿐이다 - 1.57배는 그 위다.
            //    게다가 줄기 색상각 30° / 잎 95°로 65° 벌어져 있어 대비가 명도 단독에 기대지 않는다.)
            //   하늘(daySkyTint #73A6D9, 색상각 210°) 앞에서는 거의 보색이라 실루엣이 색으로 분리되고,
            //   지면(Meadow Green 155 / Island Sand 178) 앞에서는 여전히 1.65~1.89배 어두워 분리된다.
            //
            // [B29] 섬마다 새로 만들던 것을 **월드 전체 공유 캐시**로 바꿨다. 색·텍스처 조합이 섬마다
            // 완전히 동일한데(전부 위 팔레트 상수의 결정적 변주다) 섬 9개가 각자 8장씩 만들고 있어
            // 머티리얼이 72장이었다. ResourceVisualLibrary.GetMaterial은 (색+텍스처)당 한 장을 돌려주고
            // enableInstancing까지 켜 주므로, 같은 메시를 쓰는 초목 수백 개가 실제로 인스턴싱된다
            // (자원 노드가 B28에서 같은 이유로 같은 캐시로 옮겼다 - 그쪽이 원본이다).
            // 색 값은 한 채널도 바꾸지 않았다. 위 B8~B11의 대비 계산은 전부 그대로 유효하다.
            Material trunkMaterial = ResourceVisualLibrary.GetMaterial(PalmBarkColor, "bark");

            // 잎/덤불/풀을 각각 단색 한 장으로 칠하면 같은 초록이 반지름 200m를 덮어 "한 톤"으로 읽힌다.
            // 프리미티브(=렌더러) 개수를 늘리지 않고 톤만 늘리는 유일한 방법이 머티리얼 장수를 늘려
            // 인스턴스마다 돌려 쓰는 것이다. SRP 배처는 머티리얼이 아니라 셰이더 변형 단위로 묶으므로
            // 머티리얼이 4장 → 8장이 되어도 배칭은 깨지지 않는다(파츠마다 새로 만들면 400장이 되어
            // 깨지는 것과는 자릿수가 다르다).
            // 변주는 "명도"가 아니라 "색상"으로 준다 - 명도를 깎으면 위에서 확보한 줄기-잎 대비가
            // 같이 무너지기 때문이다. Frond Green ↔ Meadow Green 사이를 조금 섞어 황록/청록 쪽으로만
            // 흔들고 상대휘도는 147~150으로 유지한다.
            // [B29] 야자잎만 "leaf" → "frond"(잎맥 결) 텍스처로 바꿨다. 색은 그대로다.
            var frondMaterials = new[]
            {
                ResourceVisualLibrary.GetMaterial(StructureVisualBuilder.FrondGreen, "frond"),
                ResourceVisualLibrary.GetMaterial(
                    Color.Lerp(StructureVisualBuilder.FrondGreen, StructureVisualBuilder.MeadowGreen, 0.35f), "frond"),
            };
            var bushMaterials = new[]
            {
                ResourceVisualLibrary.GetMaterial(Shade(StructureVisualBuilder.FrondGreen, 0.82f), "leaf"),
                ResourceVisualLibrary.GetMaterial(
                    Shade(Color.Lerp(StructureVisualBuilder.FrondGreen, StructureVisualBuilder.MeadowGreen, 0.40f), 0.90f), "leaf"),
            };
            var tuftMaterials = new[]
            {
                ResourceVisualLibrary.GetMaterial(Shade(StructureVisualBuilder.MeadowGreen, 0.86f), "leaf"),
                ResourceVisualLibrary.GetMaterial(Shade(StructureVisualBuilder.MeadowGreen, 0.98f), "leaf"),
                ResourceVisualLibrary.GetMaterial(
                    Shade(Color.Lerp(StructureVisualBuilder.MeadowGreen, StructureVisualBuilder.FrondGreen, 0.35f), 0.90f), "leaf"),
            };

            // [B29 신규] 바위/표류물 머티리얼도 같은 공유 캐시에서 받는다(월드 전체가 5장을 나눠 쓴다).
            var rockMaterials = new[]
            {
                ResourceVisualLibrary.GetMaterial(StructureVisualBuilder.WeatheredStone, "rock"),
                ResourceVisualLibrary.GetMaterial(Shade(StructureVisualBuilder.WeatheredStone, 0.84f), "rock"),
                ResourceVisualLibrary.GetMaterial(
                    Saturate(Shade(StructureVisualBuilder.WeatheredStone, 1.06f), 1.15f), "rock"),
            };
            var driftMaterials = new[]
            {
                ResourceVisualLibrary.GetMaterial(Shade(StructureVisualBuilder.Driftwood, 0.88f), "driftwood"),
                ResourceVisualLibrary.GetMaterial(StructureVisualBuilder.SupplyKhaki, "driftwood"),
            };

            // (1) 지면 색 구분: 정상부 밝은 풀 / 내륙 풀 / 마른 모래 / 젖은 모래의 4단(전부 덮개 메시다 - B11).
            //     난수 소비 2회(풀밭 경계 위상 2개)로 고정.
            float boundaryPhaseA = rng.NextFloat(0f, Mathf.PI * 2f);
            float boundaryPhaseB = rng.NextFloat(0f, Mathf.PI * 2f);
            BuildGroundCaps(root.transform, islandObject, radius, boundaryPhaseA, boundaryPhaseB);

            // (2) 초목 개수: 반지름에 선형 비례시키되 규모별 상한을 두고, 마지막에 섬 전체 상한을 강제한다.
            //     면적 비례(반지름의 제곱)로 잡으면 특대 섬에서 곧바로 수천 개가 되어 쓸 수 없다.
            //     [B8] 야자수 1그루가 렌더러 5개(줄기1+잎4)에서 13개(줄기3+잎5×2)로 늘었다. 렌더러 총량을
            //     예전과 같은 수준(약 400)으로 묶어두기 위해 그루 수 상한을 42 → 16으로 내려 상쇄한다
            //     (디렉터 지시: "잎 1장당 프리미티브를 늘리려면 나무 수를 줄여서 상쇄해라").
            //     [B9] 덤불 로브와 풀포기를 내장 Sphere(768삼각형)에서 저폴리 메시(20 / 12삼각형)로
            //     교체해 삼각형이 15배 남았다. 남은 예산은 **저폴리가 된 쪽에만** 쓴다 -
            //     덤불 40 → 48, 풀포기 78 → 156. (당시 야자수 16은 그대로 뒀다 - 교체 대상이 아니었고
            //     그루당 렌더러 13개로 가장 비쌌기 때문이다. 그 렌더러 제약은 B48에서 그루당 2개가
            //     되면서 사라졌고, 아래 B49가 그 여유를 그루 수에 쓴다.)
            //     세 상한의 합 80+48+156 = 284 = MaxVegetationInstancesPerIsland로 정확히 맞춰,
            //     아래 트림 블록이 도달 불가 코드가 아니라 살아 있는 가드가 되게 했다.
            //     하한(20/12/20)은 IslandSizeMetrics의 최소 반지름이 50이라 현재 어떤 섬에서도 발동하지
            //     않는다 - 반지름 공식이 바뀔 때를 대비한 방어값이라는 뜻이며, 상한과 달리 "닿는" 값이
            //     아니다(주석이 사실과 어긋나지 않도록 명시해 둔다).
            //     [B49 디렉터 지시 "야자수를 5배로"] 계수 0.12 → **0.60**, 상한 16 → **80**, 하한 4 → 20.
            //     계수와 상한을 **같은 배율(×5)로** 올려야 네 규모가 전부 정확히 5배가 된다
            //     (소 6→30 / 중 11→54 / 대 16→80 / 특대 16→80). 상한만 올리면 소·중형이 안 늘고,
            //     계수만 올리면 대·특대가 16에 묶인 채 트림에 깎인다.
            //     ★ 이 변경은 초목 전용 난수 스트림(WorldMapManager.VegetationSeedSalt = 3000000+islandId)
            //       안에서만 일어난다. 자원 노드 스트림은 CreateForIsland(worldSeed, islandId)로 **별도
            //       인스턴스**라, 야자수 draw가 몇 개 늘든 자원 노드의 배치·세이브에는 영향이 없다.
            //       같은 스트림 안의 덤불·풀포기·바위·표류물은 야자수 뒤에 오므로 위치가 재배치된다
            //       (개수는 그대로, 세이브와 무관한 장식이다).
            int palmCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.60f), 20, 80);
            int bushCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.24f), 12, 48);
            int tuftCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.78f), 20, 156);

            int requested = palmCount + bushCount + tuftCount;
            if (requested > MaxVegetationInstancesPerIsland)
            {
                float trim = (float)MaxVegetationInstancesPerIsland / requested;
                palmCount = Mathf.Max(1, Mathf.FloorToInt(palmCount * trim));
                bushCount = Mathf.Max(1, Mathf.FloorToInt(bushCount * trim));
                tuftCount = Mathf.Max(1, Mathf.FloorToInt(tuftCount * trim));
            }

            // 중심부는 비워 둔다. 시작 섬의 경비행기 잔해(+6,-4)/배 작업대(-6,-3)가 중심 근처에 고정
            // 배치되므로, 여기에 야자수가 서면 상호작용 대상이 나무에 파묻혀 보이지 않는다.
            float innerClearRadius = Mathf.Max(12f, radius * 0.12f);

            // 야자수는 균등 산포 대신 "숲(grove)" 단위로 뭉친다. 같은 개수라도 뭉쳐 있으면 밀도가
            // 훨씬 높게 읽히고, 뻥 뚫린 개활지와 그늘진 숲이 생겨 지형이 밋밋하게 보이지 않는다.
            int groveCount = Mathf.Max(2, Mathf.RoundToInt(palmCount / 4f));
            var groveCenters = new Vector3[groveCount];
            for (int i = 0; i < groveCount; i++)
                groveCenters[i] = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius, radius * 0.45f);

            for (int i = 0; i < palmCount; i++)
            {
                // 야자수/덤불의 바깥 한계는 둘 다 0.50R이다(값은 바꾸지 않는다 - 기존 배치 보존).
                // [B47] 원래 근거였던 "풀밭 경계 최솟값 0.51R"은 B15에서 GrassCap이 사라지고 이번에
                // 모래 경계가 높이 기준으로 바뀌면서 더 이상 존재하지 않는다. 지금 이 값을 지탱하는
                // 근거는 "물가에서 충분히 안쪽" 하나이며, 물에 잠긴 자리는 SnapToLand가 따로 막는다.
                Vector3 center = groveCenters[i % groveCount];
                Vector2 jitter = rng.NextInsideUnitCircle() * 11f;
                Vector3 spot = center + new Vector3(jitter.x, 0f, jitter.y);
                spot = ClampToIslandRing(spot, islandObject.transform.position, innerClearRadius, radius * 0.50f);
                // 머티리얼 선택은 인덱스로만 한다 - rng를 한 번이라도 더 소비하면 같은 worldSeed에서
                // 숲 배치가 통째로 밀려 재현성이 깨진다(파일 상단 [결정성] 주석).
                CreatePalm(root.transform,
                    SnapToLand(spot, islandObject.transform.position, innerClearRadius, radius * 0.50f, VegetationMinGroundY),
                    rng, trunkMaterial, frondMaterials[i % frondMaterials.Length]);
            }

            for (int i = 0; i < bushCount; i++)
            {
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.8f, radius * 0.50f);
                CreateBush(root.transform,
                    SnapToLand(spot, islandObject.transform.position, innerClearRadius * 0.8f, radius * 0.50f, VegetationMinGroundY),
                    rng, bushMaterials[i % bushMaterials.Length]);
            }

            for (int i = 0; i < tuftCount; i++)
            {
                // 풀포기만 풀밭 경계 밖(모래)까지 나갈 수 있게 둔다 - 해안가에 듬성듬성 난 풀처럼 보여
                // 풀밭과 모래의 경계선이 자로 그은 원처럼 보이지 않게 하는 역할이다.
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.5f, radius * 0.70f);
                CreateGrassTuft(root.transform,
                    SnapToLand(spot, islandObject.transform.position, innerClearRadius * 0.5f, radius * 0.70f, VegetationMinGroundY),
                    rng, tuftMaterials[i % tuftMaterials.Length]);
            }

            // ── [B29] 여기서부터 바위·표류물. 난수 소비를 **초목 루프 뒤에** 두는 것이 중요하다 ──
            // 이 스트림(VegetationSeedSalt 대역)은 초목 전용이라 세이브 키와 무관하지만, 앞에 끼워 넣으면
            // 같은 worldSeed에서 기존 숲 배치가 통째로 밀린다. 뒤에 붙이면 초목은 1cm도 움직이지 않는다.

            // (3) 바위 무리. 개수는 반지름 선형(초목과 같은 규칙) - 소형 3 / 중형 5 / 대형 8 / 특대 12.
            //     하나짜리 바위는 "떨어뜨려 놓은 공"으로 읽혀서, 항상 큰 덩어리 1 + 작은 덩어리 2~3의
            //     무리로 만든다(CreateRockCluster 주석).
            int rockClusterCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.06f), 3, MaxRockClustersPerIsland);
            for (int i = 0; i < rockClusterCount; i++)
            {
                // 풀밭과 마른 모래 양쪽에 걸치게 0.78R까지 내보낸다 - 해변에 반쯤 박힌 바위가
                // 해안선을 읽히게 하는 가장 싼 수단이다(자원 노드 돌조각은 0.5m급이라 그 역할을 못 한다).
                // 안쪽 한계를 초목보다 **더 크게** 잡는다(innerClearRadius + 4m). 바위는 폭이 최대 3.6m라
                // 시작 섬의 경비행기 잔해(중심에서 7.2m, 반경 3m)나 배 작업대(6.7m)와 겹치면
                // 상호작용 대상이 돌덩이에 파묻힌다 - 덤불(폭 2.2m, 0.8×innerClear)보다 위험이 크다.
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius + 4f, radius * 0.78f);
                CreateRockCluster(root.transform,
                    SnapToLand(spot, islandObject.transform.position, innerClearRadius + 4f, radius * 0.78f, VegetationMinGroundY),
                    rng, rockMaterials, i);
            }

            // (4) 표류물. 파도선 근처에만 놓는다. 개수는 소형 2 / 중형 4 / 대형 7 / 특대 9.
            //     [B47] 예전에는 "0.845R~0.925R 고리"가 곧 파도선이라고 가정했다. 그 가정은 지형이
            //     완전한 원이던 시절에만 참이었다 - 이제 각도별 반지름 마스크(0.70~1.00R) 때문에
            //     물가가 각 방위마다 다른 반경에 있어서, 고정 고리를 쓰면 표류물이 통째로 물에 잠긴다.
            //     그래서 **반경이 아니라 높이**로 파도선을 찾는다(-0.3m ~ +0.9m = 젖은 모래 띠).
            //     탐색 고리를 0.55R~0.99R로 넓혀, 만이 깊게 파인 방위에서도 물가를 실제로 만날 수 있게 했다.
            //     난수 소비는 그대로 SampleOnIsland 2회뿐이다(탐색은 rng를 쓰지 않는다).
            int driftCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.05f), 2, MaxDriftItemsPerIsland);
            for (int i = 0; i < driftCount; i++)
            {
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, radius * 0.845f, radius * 0.925f);
                CreateDriftItem(root.transform,
                    SnapToLand(spot, islandObject.transform.position, radius * 0.55f, radius * 0.99f,
                        DriftMinGroundY, DriftMaxGroundY),
                    rng, driftMaterials[i % driftMaterials.Length], i);
            }

            // ── [B51] 대형 석재(거암/잔해/겹바위/절벽). 반드시 기존 draw가 **전부 끝난 뒤**에 온다 ──
            // 대나무 증량(IslandResourceSpawner.SpawnExtraBambooNodes)과 같은 "뒤에 덧붙이기" 선례다:
            // 이 스트림은 세이브와 무관한 장식 전용이고, 추가 draw가 전부 꼬리에 붙는 한 같은 worldSeed에서
            // 위 초목·바위·표류물의 위치는 1cm도 밀리지 않으며 재현성(같은 시드 = 같은 월드)도 유지된다.
            // 새 콜라이더(거암/겹바위/절벽 convex)는 아래 Physics.SyncTransforms() 앞에서 생긴다.
            PlaceLargeStones(root.transform, islandObject, radius, rng, innerClearRadius, rockMaterials);

            // 위 루프들이 바위 큰 덩어리·야자수 줄기에 차단 콜라이더를 새로 달았다.
            // Physics.autoSyncTransforms는 기본 false라(AGENT_BRIEF 4장 — 초목이 전부 해수면에 깔렸던
            // 함정) 같은 프레임의 후속 레이캐스트는 이 콜라이더들을 아직 못 본다. 지형을 찾는 프로브는
            // 전부 이름 필터라 못 봐도 무해하지만, "보이거나 안 보이거나"가 프레임 타이밍에 좌우되는
            // 비결정성을 남기지 않기 위해 여기서 한 번 동기화한다(섬당 1회 — 비용 무시 가능).
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 바위 큰 덩어리에 다는 차단용 convex MeshCollider. sourceMesh가 null이면 파츠의 MeshFilter
        /// 메시(폴백 절차 메시)를 그대로 쓴다. convex는 PhysX가 255면 이하 헐로 자동 단순화하므로
        /// 3,366삼각형 모델도 안전하고, 비균일 스케일(폴백 경로)도 지원된다.
        /// rng를 소비하지 않고(배치 재현성 불변), 이름이 "Island_"로 시작하지 않아 지형으로 오인될 수 없다.
        /// </summary>
        private static void AddRockCollider(GameObject part, Mesh sourceMesh)
        {
            if (part == null)
                return;

            if (sourceMesh == null)
            {
                var filter = part.GetComponent<MeshFilter>();
                sourceMesh = filter != null ? filter.sharedMesh : null;
            }
            if (sourceMesh == null)
                return;

            // [B52] cliff_b(凹 굽은 단애)만 convex 헐을 쓰지 않는다. convex는 정의상 오목부를 채우므로
            // 굽은 벽이 만드는 공터(명세 "凹면이 공터를 만든다")가 보이지 않는 벽으로 불룩하게 막혔다
            // (배치부의 "convex 헐이 메워지지만" 주석으로 보고돼 있던 한계). 박스 2개 조합으로 교체.
            if (sourceMesh.name.StartsWith("rock_cliff_b"))
            {
                AddCliffBWingColliders(part);
                return;
            }

            // [B51] 콜라이더 전용 헐이 있으면 그것을 쓴다. PhysX convex cook은 256폴리곤이
            // 상한이라 렌더 메시(3,000면대)를 그대로 물리면 부분 헐로 잘린다(스모크 테스트 검출).
            Mesh hull = GetCollisionHull(sourceMesh);

            var collider = part.AddComponent<MeshCollider>();
            collider.sharedMesh = hull != null ? hull : sourceMesh;
            collider.convex = true;
        }

        /// <summary>
        /// [B52] cliff_b 전용: 凹 굽은 단애의 **두 날개를 박스 하나씩**으로 근사한다(박스 2개 조합).
        ///
        /// 치수 근거 - rockforms.py 주석(W 9.5 / H 6.7(y −0.5~6.2 · CLIFF_SINK 0.5) / D 4.6, 凹 굽음
        /// curve_amp = 앞면 기준면의 0.55)과 디스크 OBJ 정점 실측(메시 로컬: x ±4.75 · y −0.5~6.2 ·
        /// z ±2.3). 지면 대역(y ≤ 2.5)의 앞면(+Z) 프로파일은 중앙 x=0에서 z≈1.91로 물러나고 날개 끝
        /// |x|≈4.3에서 z≈2.2로 감싸 나온다 - 한쪽 날개 안에서는 거의 선형이라, 날개마다 현(chord)을
        /// 앞면으로 삼은 yaw 박스가 실제 곡면과 6cm 이내로 맞는다(가운데 오목부는 그대로 열린다).
        ///
        /// 좌우를 **대칭**으로 근사한 이유: 실측 좌우 기울기 차이(0.077 vs 0.042)는 날개 끝에서 15cm
        /// 이하인 반면, 대칭이면 OBJ 임포터의 축 반전 규약과 무관하게 같은 결과가 나온다.
        ///
        /// 배치부(PlaceLargeStones)의 전제는 전부 유지된다: 파츠 스케일이 균등(0.9~1.15)이라 자식
        /// 박스가 그대로 비례하고 전단도 없다 · 이름이 "Island_"로 시작하지 않아 지형으로 오인되지
        /// 않는다 · 기본 레이어/비트리거라 곰 장애물 캐스트(CreatureMotion)와 플레이어 충돌 모두
        /// 예전 헐과 같은 대상이 된다 · rng를 소비하지 않는다(배치 재현성 불변).
        /// 다른 바위(a~e/mega/stack/rubble/cliff_a)는 위 convex 경로 그대로다.
        /// </summary>
        private static void AddCliffBWingColliders(GameObject part)
        {
            // 날개 박스(메시 로컬 미터): 길이 5.0(현 방향 - 중앙 0.3m 겹침, |x|≈4.6까지) ×
            // 높이 6.7(y −0.5~6.2) × 깊이 4.3(현 앞면에서 뒷비탈 −2.3까지). yaw ±3.4° = 현 기울기
            // atan(0.06). 중심 = 현 중점(x ∓2.15, z 2.04)에서 법선 반대로 깊이 절반만큼.
            Vector3 size = new Vector3(5.0f, 6.7f, 4.3f);
            CreateCliffBWingBox(part.transform, "CliffB_ColWingL", new Vector3(-2.28f, 2.85f, -0.11f), 3.4f, size);
            CreateCliffBWingBox(part.transform, "CliffB_ColWingR", new Vector3(2.28f, 2.85f, -0.11f), -3.4f, size);
        }

        /// <summary>날개 박스 하나. 자식 GameObject에 localRotation yaw만 준다(부모 스케일이 균등이라 전단 없음).</summary>
        private static void CreateCliffBWingBox(Transform parent, string name, Vector3 localCenter,
            float yawDegrees, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localCenter;
            box.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            var collider = box.AddComponent<BoxCollider>();
            collider.size = size;
        }

        /// <summary>
        /// 바위 무리 하나(큰 덩어리 1 + 작은 덩어리 2~3, 렌더러 3~4개).
        ///
        /// 형태 규칙 세 가지 - 셋 다 "놓인 공"과 "박힌 바위"를 가르는 신호다:
        ///  (1) 각진 면. 메시가 정이십면체를 80면으로 소분할한 뒤 방향 함수로 반지름을 흔든 것이라
        ///      평면 셰이딩된 면이 서로 다른 각도로 꺾인다 = 균열/절리로 읽힌다(WorldMeshBuilder.AddChunk).
        ///      직전 배치에서 돌조각(자원)에 쓴 것과 같은 계열이고, 큰 바위는 화면 점유가 훨씬 크므로
        ///      면 수만 20 → 80으로 올렸다(작은 위성 덩어리는 20면 그대로다).
        ///  (2) 지면에 파묻힌다. 중심을 높이의 22~34%만큼 내려 밑동을 지면 아래로 넣는다.
        ///      SnapToGround가 준 y는 **지형 표면**이고 캡은 그 위 8cm에 떠 있으므로, 22%(최소 0.2m)면
        ///      캡보다 확실히 아래로 들어간다.
        ///  (3) 작은 덩어리가 큰 덩어리 쪽으로 기운다. 기울기 축은 두 덩어리를 잇는 방향에 수직인
        ///      수평축이라, 위쪽이 큰 바위 쪽으로 넘어가 "기대어 쌓인" 그림이 된다.
        ///
        /// [물리] 큰 덩어리에만 **convex MeshCollider**를 단다(디렉터 지시 "바위에 올라가고 부딪히게" —
        /// 파일 상단 [콜라이더 정책] 주석 참고. 예전 "절대 금지"의 전제였던 지형 오인 경로는 전부
        /// 이름 필터로 막혀 있음을 전수 확인했다). convex를 고른 이유: 바위는 형태가 거의 볼록해
        /// 헐 근사 손실이 적고, PhysX가 255면 이하로 자동 단순화해 12개×9섬이어도 쿠킹·질의가 싸다.
        /// 낮은 바위(노출 약 0.7~0.9m)는 헐 옆면 경사가 45° 이하인 자리로 걸어 오르거나 점프(0.9m)로
        /// 올라서지고, 큰 바위(노출 약 1.7~2.1m)는 자연스럽게 막힌다. 곁돌(위성 덩어리)은 폭 0.44~2.16m로
        /// 작아 발에 걸리면 짜증만 나므로 콜라이더 없음을 유지한다.
        ///
        /// [B45] 위 (1)"각진 면"은 **큰 덩어리에 한해** 실물 모델(rock_a/b/c)로 대체됐다. 곁돌은 그대로
        /// 절차 메시다(삼각형 예산 근거는 아래 본문 주석). 모델이 없으면 큰 덩어리도 예전 경로로 돌아간다.
        /// 자원 노드 "돌조각"(0.43~0.59m, IslandResourceSpawner)과는 아무 관계가 없다 - 그쪽은 채집
        /// 조준·콜라이더가 걸려 있는 완전히 다른 오브젝트이고 이 변경이 한 줄도 닿지 않는다.
        /// </summary>
        private static void CreateRockCluster(Transform parent, Vector3 groundPosition, System.Random rng,
            Material[] materials, int index)
        {
            float mainWidth = rng.NextFloat(1.7f, 3.6f);
            float mainHeight = mainWidth * rng.NextFloat(0.50f, 0.84f);
            float mainDepth = mainWidth * rng.NextFloat(0.74f, 1.02f);
            float yaw = rng.NextFloat(0f, 360f);
            int satelliteCount = rng.NextInt(2, 4); // 2 또는 3

            var cluster = new GameObject("Deco_RockCluster");
            cluster.transform.SetParent(parent, false);
            cluster.transform.position = groundPosition;
            // 뿌리는 yaw만 + 스케일 1(균등). 자식이 비균일 스케일이라도 전단이 생기지 않는다.
            cluster.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // [B45] 큰 덩어리의 난수는 **경로와 무관하게 여기서 전부 뽑는다.** 모델 경로가 회전 3회 중
            // yaw 하나만 쓰더라도 소비 횟수는 폴백과 비트 단위로 같아야 한다 - 한 번이라도 덜 뽑으면
            // 같은 worldSeed에서 이후의 곁돌·표류물이 통째로 밀린다(파일 상단 [결정성] 주석).
            float mainSinkFraction = rng.NextFloat(0.22f, 0.34f);
            float mainTiltX = rng.NextFloat(-7f, 7f);   // 폴백 전용(모델은 밑면이 평평해 기울이면 뜬다)
            float mainSpin = rng.NextFloat(0f, 360f);   // 두 경로 공용 yaw
            float mainTiltZ = rng.NextFloat(-7f, 7f);   // 폴백 전용

            // [B45] 실물 바위 모델(rock_a/b/c)이 있으면 큰 덩어리만 모델로 바꾼다.
            //  · 왜 큰 덩어리만인가: 모델은 하나가 3,366삼각형이다. 특대 섬의 큰 덩어리 12개만 해도
            //    40,392삼각형이고, 곁돌(무리당 2~3개)까지 바꾸면 141,000이 되어 B9 이전의 초목
            //    총량(157,824)으로 되돌아간다. 곁돌은 폭 0.44~2.16m라 ArtDirection 2장의 디테일 밀도
            //    규칙상 20면 저폴리가 맞는 자리다(GetBoulderMesh 주석과 같은 근거).
            //  · 모델은 **이미 미터 규격**이다(밑면 y=0 · X/Z 중심). 절차 메시가 [-0.5,0.5]^3 단위
            //    규격이라 호출부가 미터 크기를 스케일로 곱하던 것과 규약이 정반대라, 그대로 곱하면
            //    바위가 2~3배로 부푼다. 그래서 모델 경로는 **폭을 모델 실측 폭으로 나눈 균등 배율**만 쓴다.
            //  · 균등 배율이므로 자식 회전과 곱해져도 전단이 생기지 않는다. 그래도 회전은 yaw만 준다 -
            //    모델 밑면이 평면이라 x/z로 기울이면 한쪽 모서리가 지면에서 뜬다.
            Mesh mainModelMesh;
            Vector3 mainModelSize;
            if (TryGetRockModel(mainWidth, groundPosition, out mainModelMesh, out mainModelSize))
            {
                // 뽑아 둔 mainWidth를 그대로 목표 폭으로 쓰므로 폭 분포(1.7~3.6m)가 1mm도 바뀌지 않는다.
                // [B50] 변종은 5종(a~e)이고 선택은 "목표 폭 ±35% 후보 → 위치 해시" 2단계다
                //  (TryGetRockModel 주석 - rng 소비 0, 배율 0.79~1.39). groundPosition은 이미 확정된
                //  배치 좌표라 해시 입력으로 써도 재현성이 깨지지 않는다.
                float fit = mainWidth / Mathf.Max(0.01f, mainModelSize.x);
                float modelHeight = mainModelSize.y * fit;
                // 매립 비율(높이의 22~34%, 최소 0.2m)은 그대로다. 모델 원점이 밑면이라 파묻는 깊이가
                // 곧 -y이고, 절차 메시처럼 높이의 절반을 더할 필요가 없다.
                // ★ 매립은 폭이 아니라 **선택된 모델의 실측 높이**(mainModelSize.y × fit) 기준이다 -
                //   판석 rock_d(높이 0.95m)가 폭 기준 매립이었다면 통째로 잠겼을 것이다(B50 검증).
                float modelSink = Mathf.Max(0.2f, modelHeight * mainSinkFraction);
                var mainPart = CreatePart(cluster.transform, "Deco_RockMain", mainModelMesh,
                    new Vector3(0f, -modelSink, 0f),
                    new Vector3(fit, fit, fit),
                    Quaternion.Euler(0f, mainSpin, 0f),
                    materials[index % materials.Length]);
                AddRockCollider(mainPart, mainModelMesh);
            }
            else
            {
                // 모델이 없으면(임포트 전·프로브 실패) 예전 절차 메시 그대로다. 이 경로는 지우지 않는다.
                float mainSink = Mathf.Max(0.2f, mainHeight * mainSinkFraction);
                var mainPart = CreatePart(cluster.transform, "Deco_RockMain", GetBoulderMesh(index, true),
                    new Vector3(0f, mainHeight * 0.5f - mainSink, 0f),
                    new Vector3(mainWidth, mainHeight, mainDepth),
                    Quaternion.Euler(mainTiltX, mainSpin, mainTiltZ),
                    materials[index % materials.Length]);
                AddRockCollider(mainPart, null); // 절차 메시는 MeshFilter의 것을 그대로 쓴다(비균일 스케일도 convex는 지원)
            }

            for (int i = 0; i < satelliteCount; i++)
            {
                float width = mainWidth * rng.NextFloat(0.26f, 0.60f);
                float height = width * rng.NextFloat(0.52f, 0.98f);
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float lean = rng.NextFloat(9f, 26f);

                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float distance = mainWidth * 0.42f + width * 0.32f;
                float sink = Mathf.Max(0.12f, height * rng.NextFloat(0.20f, 0.36f));

                // Cross(direction, up)을 축으로 +lean만큼 돌리면 위쪽이 -direction, 즉 큰 바위 쪽으로 넘어간다.
                Quaternion tilt = Quaternion.AngleAxis(lean, Vector3.Cross(direction, Vector3.up))
                    * Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

                CreatePart(cluster.transform, $"Deco_RockChip{i}",
                    GetBoulderMesh(index * 3 + i + 1, false),
                    direction * distance + new Vector3(0f, height * 0.5f - sink, 0f),
                    new Vector3(width, height, width * rng.NextFloat(0.72f, 1.05f)),
                    tilt, materials[(index + i + 1) % materials.Length]);
            }
        }

        /// <summary>
        /// 표류물 하나(궤짝 / 통 / 널판 더미 중 하나, 렌더러 1개).
        ///
        /// 셋 다 디테일을 메시에 구웠다 - 궤짝의 널판 홈과 모서리 프레임, 통의 테 3줄, 널판 더미의
        /// 겹친 판 3장이 전부 정점이라 파츠는 하나뿐이다(B28 대나무 마디와 같은 처리).
        /// 파도에 밀려온 것으로 읽히게 하는 것은 형태가 아니라 **자세**다: 모래에 15~35% 파묻히고,
        /// 옆으로 12~34도 기울어 있으며, 방향이 해안선과 무관하게 제각각이다.
        ///
        /// 회전과 비균일 스케일이 같은 오브젝트에 걸리지만 부모(IslandSurface)는 스케일 1이라
        /// 전단이 생기지 않는다(T·R·S 순서상 스케일이 먼저 자기 로컬에서 적용된다).
        /// </summary>
        private static void CreateDriftItem(Transform parent, Vector3 groundPosition, System.Random rng,
            Material material, int index)
        {
            float yaw = rng.NextFloat(0f, 360f);
            float leanRoll = rng.NextFloat(0f, 1f);
            float leanAxis = rng.NextFloat(0f, 360f);
            float scale = rng.NextFloat(0.85f, 1.25f);

            // 이 switch는 (a) 두 경로 공용의 자세(lean)와 (b) 폴백 경로의 메시·크기를 정한다.
            // 폴백 메시 규격은 셋 다 [-0.5,0.5]^3 단위 상자다 → 아래 size는 **미터** 그대로이고, 호출부가
            // 스케일을 따로 곱하지 않는다(과거 "메시만 바꾸고 호출부 스케일을 그대로 둔" 사고 방지).
            Vector3 size;
            Mesh mesh;
            float lean;
            switch (index % 3)
            {
                case 0: // 궤짝: 모서리로 처박혀 있어야 "떠내려온 것"으로 읽힌다.
                    mesh = GetCrateMesh();
                    size = new Vector3(0.82f, 0.66f, 0.74f) * scale;
                    lean = Mathf.Lerp(14f, 34f, leanRoll);
                    break;
                case 1: // 통: 옆으로 굴러 누운 자세. 90도 근처로 눕혀야 "굴러온 통"이 된다.
                    mesh = GetBarrelMesh();
                    size = new Vector3(0.60f, 0.86f, 0.60f) * scale;
                    lean = Mathf.Lerp(74f, 96f, leanRoll);
                    break;
                default: // 널판 더미: 길고 납작해서 조금만 기울여도 한쪽 끝이 크게 들린다.
                    mesh = GetPlankPileMesh();
                    size = new Vector3(2.10f, 0.22f, 0.86f) * scale;
                    lean = Mathf.Lerp(3f, 11f, leanRoll);
                    break;
            }

            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f)
                * Quaternion.AngleAxis(lean, Quaternion.Euler(0f, leanAxis, 0f) * Vector3.forward);

            // ── [B50] 실물 표류물 모델(crate_a / barrel_a / plankpile_a) ──
            //  draw 4개(yaw/leanRoll/leanAxis/scale)는 위에서 전부 뽑았고 종류는 index%3 그대로라
            //  rng 소비가 폴백과 비트 단위로 같다. 모델 실측이 절차 메시의 미터 크기와 정확히 같게
            //  구워져 있어(DriftModelSizes 주석) 명세대로 fit 없이 **배율 지터 0.85~1.25(기존 scale
            //  draw)만** 균등 스케일로 곱한다. 자세(yaw+lean)도 폴백과 같은 값이다.
            //  다른 점은 원점뿐이다: 모델은 접지 중심(밑면 y=0)이라 "중심을 verticalHalf만큼 올리는"
            //  폴백 식을 쓰면 통째로 떠오른다. 대신 기울일 때 밑면 가장자리가 원점보다 내려가는 깊이
            //  (수평 반폭 × sin(lean))만 들어 올린 뒤 같은 sink만큼 파묻는다.
            Mesh driftModelMesh;
            Vector3 driftModelSize;
            if (TryGetDriftModel(index % 3, out driftModelMesh, out driftModelSize))
            {
                Vector3 worldSize = driftModelSize * scale;
                float modelRadians = lean * Mathf.Deg2Rad;
                float horizontalHalf = 0.5f * Mathf.Max(worldSize.x, worldSize.z);
                // 폴백의 verticalHalf와 같은 값이다(0.5·(h·cos + maxWD·sin)) - sink 규칙을 공유해
                // 모델/폴백의 파묻힌 깊이가 같게 유지된다.
                float modelVerticalHalf = 0.5f * worldSize.y * Mathf.Abs(Mathf.Cos(modelRadians))
                    + horizontalHalf * Mathf.Abs(Mathf.Sin(modelRadians));
                float modelSink = Mathf.Min(0.16f, modelVerticalHalf * 0.34f);

                var modelPart = CreatePart(parent, "Deco_Drift" + (index % 3), driftModelMesh,
                    Vector3.zero, Vector3.one * scale, rotation, material);
                modelPart.transform.position = groundPosition
                    + Vector3.up * (horizontalHalf * Mathf.Abs(Mathf.Sin(modelRadians)) - modelSink);
                return;
            }

            // 폴백(절차 메시 - 지우지 않는다). 구 규격 [-0.5,0.5]^3이라 중심을 반높이만큼 올린다.
            // 파묻힘: 기울인 상태의 세로 반높이를 실제로 계산한 뒤 그 일부만 지면 아래로 넣는다.
            // 상수 비율(예: "세로 크기의 22%")로 하면 길게 누운 널판 더미가 통째로 땅에 잠긴다 -
            // 회전을 무시한 값을 쓰는 것이 이 프로젝트가 반복해서 낸 사고 유형이라 여기서 계산한다.
            float radians = lean * Mathf.Deg2Rad;
            float verticalHalf = 0.5f * (size.y * Mathf.Abs(Mathf.Cos(radians))
                + Mathf.Max(size.x, size.z) * Mathf.Abs(Mathf.Sin(radians)));
            float sink = Mathf.Min(0.16f, verticalHalf * 0.34f);

            var part = CreatePart(parent, "Deco_Drift" + (index % 3), mesh,
                Vector3.zero, size, rotation, material);
            part.transform.position = groundPosition + Vector3.up * (verticalHalf - sink);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  [B51] 대형 석재 - "사람보다 큰 바위·깨진 잔해·겹바위·절벽을 랜덤으로"(디렉터/사용자 요청)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>규모별(소/중/대/특대) 개수 하한. 열 순서: 거암 합산 / 잔해 합산 / 겹바위 / 절벽 합산.
        /// 제작 담당 명세표의 검증값 그대로다. 규모 판정은 반지름(50/90/140/200)의 중간값 경계로 한다.</summary>
        private static readonly int[] StoneMegaMin = { 1, 2, 4, 6 };
        private static readonly int[] StoneMegaMax = { 2, 4, 6, 8 };
        private static readonly int[] StoneRubbleMin = { 2, 4, 8, 12 };
        private static readonly int[] StoneRubbleMax = { 4, 8, 12, 16 };
        private static readonly int[] StoneStackMin = { 0, 1, 2, 3 };
        private static readonly int[] StoneStackMax = { 1, 2, 3, 4 };
        private static readonly int[] StoneCliffMin = { 0, 1, 2, 3 };
        private static readonly int[] StoneCliffMax = { 1, 2, 4, 5 };

        /// <summary>이번 배치 안의 상호 최소 간격(m). 큰 것끼리 겹치면 흉하다(명세). 절벽↔거암처럼
        /// 종류가 다르면 **둘 중 큰 값**을 적용한다(TryFindStoneSpot).</summary>
        private const float StoneMegaSpacing = 8f;
        private const float StoneCliffSpacing = 12f;

        /// <summary>
        /// 시작 섬 고정물(경비행기 잔해 +6,-4 · 배 작업대 -6,-3 · 착륙 원)의 섬 중심 기준 최대 도달
        /// 반경(m). 잔해 7.2 / 작업대 6.7 / 착륙 원 상한 12(IslandResourceSpawner.LandingCircleMaxRadius)
        /// 중 최댓값이다. 셋 다 중심 근처 고정이라, 명세의 회피 반경에 이 값을 더한 **중심 이격**으로
        /// 세 점 회피를 한 번에 근사한다 - CreatePalm 주변의 innerClearRadius(중심 비우기) 선례와 같은
        /// 방식이고, 점별 거리 검사보다 보수적이라 항상 안전한 쪽으로 어긋난다.
        /// </summary>
        private const float StartIslandFixturesReach = 12f;

        /// <summary>거암/겹바위를 경사지에서 모서리가 뜨지 않게 파묻는 고정 깊이(m). 모델 밑면이 평평한
        /// 접지 원점(y=0)이라 비율 매립(기존 바위 22~34%)이 필요 없고, rng도 소비하지 않는다.</summary>
        private const float StoneMegaSink = 0.15f;

        /// <summary>잔해(판형/무더기)의 매립 깊이(m). 높이가 0.45~0.75m뿐이라 얕게만 묻는다.</summary>
        private const float StoneRubbleSink = 0.04f;

        /// <summary>
        /// [B51] 대형 석재 4계열(거암 mega_a/b · 잔해 rubble_a/b · 겹바위 stack_a · 절벽 cliff_a/b)을
        /// 배치한다. BuildIslandSurface **맨 끝**에서만 불린다 - 모든 draw가 기존 초목·바위·표류물의
        /// draw 뒤에 오므로 기존 배치는 밀리지 않는다(호출부 주석).
        ///
        /// [모델 필수] TryGetStoneModel이 false면 그 개체는 **아예 배치하지 않는다.** 기존 장식과 달리
        /// 절차 폴백을 만들지 않는다(MeshLibrary [B51] 주석 - 신규 장식이라 "없으면 없는 것"이 폴백이다).
        ///
        /// [난수] 이 함수 안의 draw 수는 재시도(최대 6회)에 따라 변하지만, 스트림의 꼬리라 뒤따르는
        /// 소비자가 없고 System.Random은 시드 결정적이라 같은 worldSeed면 같은 배치가 나온다.
        ///
        /// [콜라이더] 거암/겹바위/절벽은 AddRockCollider(convex MeshCollider - 기존 경로 재사용).
        /// 잔해는 "밟고 지나감"이 명세라 콜라이더 없음. 전부 호출부(BuildIslandSurface)의
        /// Physics.SyncTransforms() 앞에서 생긴다.
        /// </summary>
        private static void PlaceLargeStones(Transform parent, GameObject islandObject, float radius,
            System.Random rng, float innerClearRadius, Material[] materials)
        {
            Vector3 center = islandObject.transform.position;

            // 시작 섬 판정: 이름 규약 "Island_{id}_{size}"(WorldMapManager.SpawnPlaceholder가
            // BuildIslandSurface 호출 **전에** 붙인다). islandId 0이 항상 시작 섬이다
            // (WorldMapManager.GenerateStartingIsland). 고정물은 시작 섬에만 있으므로 다른 섬에서는
            // 이격을 더하지 않는다 - 큰 섬의 내륙 배치 다양성을 공짜로 깎을 이유가 없다.
            bool startIsland = islandObject.name.StartsWith("Island_0_");

            // 규모 판정: 반지름 50/90/140/200(IslandSizeMetrics)의 중간값 경계. 반지름 공식이 바뀌어도
            // 가장 가까운 규모로 떨어지게 하는 방어이지, 현재 값에서는 정확히 4단으로 갈린다.
            int bracket = radius <= 70f ? 0 : radius <= 115f ? 1 : radius <= 170f ? 2 : 3;

            int megaCount = rng.NextInt(StoneMegaMin[bracket], StoneMegaMax[bracket] + 1);
            int rubbleCount = rng.NextInt(StoneRubbleMin[bracket], StoneRubbleMax[bracket] + 1);
            int stackCount = rng.NextInt(StoneStackMin[bracket], StoneStackMax[bracket] + 1);
            int cliffCount = rng.NextInt(StoneCliffMin[bracket], StoneCliffMax[bracket] + 1);

            // 살아 있는 가드(B9 정정의 규칙): 현재 규모표로는 최대 33 = 상한과 정확히 같아 발동하지
            // 않지만, 규모표를 올리는 순간 잔해부터 깎는다(콜라이더 없는 계열이라 잃는 것이 가장 적다).
            int requested = megaCount + rubbleCount + stackCount + cliffCount;
            if (requested > MaxLargeStonesPerIsland)
                rubbleCount = Mathf.Max(0, rubbleCount - (requested - MaxLargeStonesPerIsland));

            // 이번 배치에서 이미 놓인 큰 석재(거암/겹바위/절벽). 상호 간격 검사와 잔해의 "깨져 나온"
            // 연출(60% 확률로 큰 석재 주변 3~6m) 앵커로 쓴다.
            var placedLarge = new List<Vector3>();
            var placedSpacing = new List<float>();

            // ── (1) 거암 mega_a/b - 내륙, 회피 15m, 상호 8m, convex ──
            // 안쪽 한계: 기존 바위 무리와 같은 innerClearRadius+4를 기본으로 하되, 시작 섬은 고정물
            // 도달 반경(12) + 명세 회피 15 = 27m를 강제한다(위 StartIslandFixturesReach 주석).
            float megaMinR = Mathf.Max(innerClearRadius + 4f, startIsland ? StartIslandFixturesReach + 15f : 0f);
            float megaMaxR = Mathf.Max(megaMinR + 4f, radius * 0.55f);
            for (int i = 0; i < megaCount; i++)
            {
                // 교대(명세 "mega_a와 교대"): 짝수 a / 홀수 b. 한쪽만 로드돼 있으면 그쪽으로 대신한다.
                Mesh mesh; Vector3 size;
                if (!TryGetStoneModel(i % 2 == 0 ? StoneMegaA : StoneMegaB, out mesh, out size)
                    && !TryGetStoneModel(i % 2 == 0 ? StoneMegaB : StoneMegaA, out mesh, out size))
                    continue; // 모델 없음 - 배치하지 않는다(rng 미소비라 이후 개체에도 영향 없음)

                Vector3 spot;
                if (!TryFindStoneSpot(center, rng, megaMinR, megaMaxR, StoneMegaSpacing,
                        placedLarge, placedSpacing, out spot))
                    continue; // 6회 안에 자리 못 찾음 - 건너뛴다(PickBearWanderTarget 선례)

                float scale = rng.NextFloat(0.85f, 1.25f);
                float yaw = rng.NextFloat(0f, 360f);
                var part = CreatePart(parent, "Deco_StoneMega", mesh,
                    Vector3.zero, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f),
                    materials[i % materials.Length]);
                part.transform.position = spot + Vector3.down * StoneMegaSink;
                AddRockCollider(part, mesh); // 꼭대기 평탄면에 올라서기(명세) - convex 헐이 그 면을 보존한다

                placedLarge.Add(spot);
                placedSpacing.Add(StoneMegaSpacing);
            }

            // ── (2) 겹바위 stack_a - 내륙 랜드마크. 거암과 같은 규칙(회피 15m·상호 8m·convex) ──
            float stackMaxR = Mathf.Max(megaMinR + 4f, radius * 0.50f);
            for (int i = 0; i < stackCount; i++)
            {
                Mesh mesh; Vector3 size;
                if (!TryGetStoneModel(StoneStackA, out mesh, out size))
                    break; // 단일 모델이라 한 번 없으면 전부 없다

                Vector3 spot;
                if (!TryFindStoneSpot(center, rng, megaMinR, stackMaxR, StoneMegaSpacing,
                        placedLarge, placedSpacing, out spot))
                    continue;

                float scale = rng.NextFloat(0.9f, 1.2f);
                float yaw = rng.NextFloat(0f, 360f);
                var part = CreatePart(parent, "Deco_StoneStack", mesh,
                    Vector3.zero, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f),
                    materials[(i + 1) % materials.Length]);
                part.transform.position = spot + Vector3.down * StoneMegaSink;
                AddRockCollider(part, mesh);

                placedLarge.Add(spot);
                placedSpacing.Add(StoneMegaSpacing);
            }

            // ── (3) 절벽 cliff_a/b - 내륙 경사지, 회피 20m, 상호 12m, convex ──
            // 지형은 정상부에서 해안으로 내려가는 대체 단조 경사라(WorldMeshBuilder 지형 마스크),
            // 0.40R~0.70R 고리가 곧 "내륙 경사지" 근사다. 방향은 지형 경사 샘플링 대신 **섬 중심에서
            // 바깥을 향하는 방위각**으로 잡는다(명세: 수직면 +Z가 내리막/해안 쪽 - 경사 샘플링은 과하다).
            float cliffMinR = Mathf.Max(radius * 0.40f, innerClearRadius + 6f,
                startIsland ? StartIslandFixturesReach + 20f : 0f);
            float cliffMaxR = Mathf.Max(cliffMinR + 6f, radius * 0.70f);
            for (int i = 0; i < cliffCount; i++)
            {
                Mesh mesh; Vector3 size;
                if (!TryGetStoneModel(i % 2 == 0 ? StoneCliffA : StoneCliffB, out mesh, out size)
                    && !TryGetStoneModel(i % 2 == 0 ? StoneCliffB : StoneCliffA, out mesh, out size))
                    continue;

                Vector3 spot;
                if (!TryFindStoneSpot(center, rng, cliffMinR, cliffMaxR, StoneCliffSpacing,
                        placedLarge, placedSpacing, out spot))
                    continue;

                // +Z(수직면)를 섬 바깥으로: Euler(0,yaw,0)는 +Z를 (sin yaw, 0, cos yaw)로 돌리므로
                // yaw = Atan2(바깥.x, 바깥.z)다. ±20° 지터로 여러 절벽이 기계적 방사형이 되는 것을 막는다.
                Vector3 outward = spot - center;
                float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + rng.NextFloat(-20f, 20f);
                float scale = rng.NextFloat(0.9f, 1.15f);

                // 메시가 y=-0.5까지 내려가 있어(경사 얹힘 여유 - 명세) 지표 높이에 그대로 놓는다.
                var part = CreatePart(parent, "Deco_StoneCliff", mesh,
                    Vector3.zero, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f),
                    materials[i % materials.Length]);
                part.transform.position = spot;
                AddRockCollider(part, mesh); // [B52] 凹면(cliff_b)만 convex 헐 대신 날개 박스 2개(AddCliffBWingColliders) - cliff_a는 convex 그대로

                placedLarge.Add(spot);
                placedSpacing.Add(StoneCliffSpacing);
            }

            // ── (4) 잔해 rubble_a/b - 배치 무관, 회피 12m, 콜라이더 없음 ──
            // 60%는 이번에 놓인 큰 석재 주변 3~6m("깨져 나온" 연출 - 명세), 40%는 자유 산포.
            // 낮고(0.45~0.75m) 밟고 지나가는 장식이라 상호 간격 검사와 재시도가 필요 없다.
            float rubbleMinR = Mathf.Max(innerClearRadius * 0.8f, startIsland ? StartIslandFixturesReach + 12f : 0f);
            float rubbleMaxR = Mathf.Max(rubbleMinR + 4f, radius * 0.78f);
            for (int i = 0; i < rubbleCount; i++)
            {
                Mesh mesh; Vector3 size;
                if (!TryGetStoneModel(i % 2 == 0 ? StoneRubbleA : StoneRubbleB, out mesh, out size)
                    && !TryGetStoneModel(i % 2 == 0 ? StoneRubbleB : StoneRubbleA, out mesh, out size))
                    continue;

                Vector3 spot;
                if (placedLarge.Count > 0 && rng.NextValue01() < 0.6f)
                {
                    Vector3 anchor = placedLarge[rng.NextInt(0, placedLarge.Count)];
                    float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                    float dist = rng.NextFloat(3f, 6f);
                    spot = anchor + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                    // 앵커에서 6m 밀리면 시작 섬 고정물 이격(회피 12m)이 살짝 깨질 수 있어 고리로 되민다.
                    spot = ClampToIslandRing(spot, center, rubbleMinR, rubbleMaxR);
                }
                else
                {
                    // 앵커가 없거나(모델 미로드·전부 건너뜀) 40% 자유 산포. 앵커 유무로 분기하므로
                    // draw 수가 경우에 따라 다르지만, 스트림 꼬리라 문제없다(함수 상단 [난수] 주석).
                    spot = SampleOnIsland(center, rng, rubbleMinR, rubbleMaxR);
                }

                spot = SnapToLand(spot, center, rubbleMinR, rubbleMaxR, VegetationMinGroundY);
                if (spot.y <= VegetationMinGroundY)
                    continue; // 육지를 못 찾음(물속) - 배치하지 않는다

                float scale = rng.NextFloat(0.8f, 1.3f);
                float yaw = rng.NextFloat(0f, 360f);
                var part = CreatePart(parent, "Deco_StoneRubble", mesh,
                    Vector3.zero, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f),
                    materials[(i + 2) % materials.Length]);
                part.transform.position = spot + Vector3.down * StoneRubbleSink;
                // 콜라이더 없음(명세 "밟고 지나감") - CreatePart는 콜라이더가 애초에 안 생기는 경로다.
            }
        }

        /// <summary>
        /// [B51] 대형 석재 자리 찾기: 고리 안에서 뽑아 물속을 피하고(SnapToLand), 이번 배치의 다른 큰
        /// 석재와의 상호 간격(서로 다른 종류면 둘 중 큰 값)을 지키는 자리를 **최대 6회** 시도한다.
        /// 못 찾으면 false - 호출부는 그 개체를 건너뛴다(HazardSource.PickBearWanderTarget의 6회
        /// 상한 선례: 억지로 놓는 것보다 빠뜨리는 쪽이 항상 안전하다).
        /// </summary>
        private static bool TryFindStoneSpot(Vector3 center, System.Random rng, float minRadius, float maxRadius,
            float ownSpacing, List<Vector3> placed, List<float> placedSpacing, out Vector3 spot)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 candidate = SnapToLand(SampleOnIsland(center, rng, minRadius, maxRadius),
                    center, minRadius, maxRadius, VegetationMinGroundY);
                if (candidate.y <= VegetationMinGroundY)
                    continue; // SnapToLand가 육지를 못 찾고 원래 스냅을 돌려준 경우(물속)

                bool blocked = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    float need = Mathf.Max(ownSpacing, placedSpacing[i]);
                    float dx = candidate.x - placed[i].x;
                    float dz = candidate.z - placed[i].z;
                    if (dx * dx + dz * dz < need * need)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                {
                    spot = candidate;
                    return true;
                }
            }

            spot = Vector3.zero;
            return false;
        }

        /// <summary>초목·바위가 서 있어도 되는 최소 지면 높이(m). 이보다 낮으면 물에 잠긴 자리로 본다.</summary>
        private const float VegetationMinGroundY = 0.25f;

        /// <summary>표류물의 최소 지면 높이(m). 파도선에 반쯤 잠긴 것이 정상이라 음수까지 허용한다.</summary>
        private const float DriftMinGroundY = -0.3f;

        /// <summary>표류물의 최대 지면 높이(m). 이보다 높으면 "파도에 밀려온 것"으로 안 읽힌다.</summary>
        private const float DriftMaxGroundY = 0.9f;

        /// <summary>
        /// [B47] 뽑은 자리가 **물속이면** 같은 방위선 위에서 육지를 찾아 옮긴다.
        ///
        /// 왜 필요해졌나: 이번 배치부터 섬 안쪽에도 물이 있다(가운데 수로 · 석호 · 초승달의 만).
        /// SampleOnIsland는 지형을 보지 않고 고리 안에서 균등하게 뽑으므로, 그대로 두면 야자수가
        /// 석호 한가운데에 잠긴 채로 서고 바위가 수로 바닥에 놓인다.
        ///
        /// ★ 난수 소비 0 ★ 이 함수는 rng를 인자로 받지도 않는다. 탐색은 (원래 자리, 고리 반경)만 보고
        /// 도는 결정적 루프다. 한 번이라도 추첨을 더 하면 같은 worldSeed에서 기존 숲 배치가 통째로 밀린다
        /// (파일 상단 [결정성] 주석). 방위각은 그대로 두고 **반경만** 조정하므로, 육지가 넉넉한 섬에서는
        /// 첫 판정에서 곧바로 통과해 배치가 예전과 1cm도 다르지 않다.
        ///
        /// 육지를 못 찾으면 원래 스냅 결과를 그대로 돌려준다(자리를 잃는 것보다 낫다).
        /// </summary>
        private static Vector3 SnapToLand(Vector3 spot, Vector3 islandCenter, float minRadius, float maxRadius,
            float minGroundY, float maxGroundY = float.MaxValue)
        {
            Vector3 snapped = TerrainSampler.SnapToGround(spot);
            if (snapped.y > minGroundY && snapped.y <= maxGroundY)
                return snapped;

            float flatX = spot.x - islandCenter.x;
            float flatZ = spot.z - islandCenter.z;
            float distance = Mathf.Sqrt(flatX * flatX + flatZ * flatZ);
            float dirX = distance > 0.01f ? flatX / distance : 1f;
            float dirZ = distance > 0.01f ? flatZ / distance : 0f;

            // 원래 반경에서 가까운 순서로 안쪽/바깥쪽을 번갈아 훑는다. 고리 밖으로는 나가지 않으므로
            // "야자수는 0.50R 안" 같은 기존 배치 규칙이 그대로 유지된다.
            const int Steps = 10;
            float span = Mathf.Max(1f, maxRadius - minRadius) * 0.5f;
            for (int k = 1; k <= Steps; k++)
            {
                float delta = span * k / Steps;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float r = Mathf.Clamp(distance + sign * delta, minRadius, maxRadius);
                    var candidate = new Vector3(islandCenter.x + dirX * r, spot.y, islandCenter.z + dirZ * r);
                    Vector3 hit = TerrainSampler.SnapToGround(candidate);
                    if (hit.y > minGroundY && hit.y <= maxGroundY)
                        return hit;
                }
            }
            return snapped;
        }

        /// <summary>
        /// 섬 중심 기준 [minRadius, maxRadius] 고리 안의 한 점을 뽑는다(면적 균등).
        /// 난수 소비는 호출당 항상 2회(NextInsideUnitCircle)로 고정된다.
        /// </summary>
        private static Vector3 SampleOnIsland(Vector3 islandCenter, System.Random rng, float minRadius, float maxRadius)
        {
            Vector2 unit = rng.NextInsideUnitCircle();
            float length = Mathf.Max(0.0001f, unit.magnitude);
            float distance = Mathf.Lerp(minRadius, maxRadius, length);
            Vector2 direction = unit / length;
            return islandCenter + new Vector3(direction.x * distance, 0f, direction.y * distance);
        }

        /// <summary>지정한 점을 섬 중심 기준 [minRadius, maxRadius] 고리 안으로 밀어 넣는다.</summary>
        private static Vector3 ClampToIslandRing(Vector3 point, Vector3 islandCenter, float minRadius, float maxRadius)
        {
            Vector3 offset = point - islandCenter;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 0.0001f)
                return islandCenter + new Vector3(minRadius, 0f, 0f);

            float clamped = Mathf.Clamp(distance, minRadius, maxRadius);
            return islandCenter + offset / distance * clamped;
        }

        /// <summary>야자수 1그루를 이루는 줄기 마디 수. 마디마다 기울기를 조금씩 더해 휜 기둥을 만든다.</summary>
        private const int PalmTrunkSegments = 3;

        /// <summary>
        /// 야자수 줄기 프리즘의 각 수. 내장 Cylinder(20각, 마디당 80삼각형)를 대체한다.
        ///
        /// [B10] 6각(마디당 20)이 아니라 **8각(마디당 28)** 으로 정했다. 직전 배치에서 스스로 올린 우려
        /// ("줄기는 5m 이내 근접 관찰 대상이라 각이 눈에 띌 수 있다")를 값으로 검증한 결과다.
        ///   · 실루엣 오차: 정n각형의 평균 폭은 Cauchy 공식으로 2nR·sin(π/n)/π다. 원(2R) 대비
        ///     20각 99.6% / 8각 97.5% / 6각 95.5% — 즉 회전에 따라 굵기가 출렁이는 폭이
        ///     8각 7.6% vs 6각 13.4%다. 굵기 인지 한계(약 5%)를 8각은 거의 넘지 않고 6각은 확실히 넘는다.
        ///   · 능선 꺾임각: 6각은 면 사이 법선이 60° 꺾이고 8각은 45°다. 지향성 광원 하나뿐인
        ///     이 씬에서 60° 꺾임은 이웃 면 사이 밝기가 최대 2배 가까이 벌어져, 지금 지면에서 고치고 있는
        ///     "각진 얼룩"과 같은 실패를 굵기 0.3m짜리 근접 오브젝트에서 재현하게 된다.
        ///   · 비용 차이는 그루당 24삼각형(마디 3개 × 8), 특대 섬 16그루 기준 384삼각형 = 교체 전
        ///     총량의 3.7%뿐이다. 가장 자주 근접 관찰되는 오브젝트의 리스크를 그 값에 사는 것이 맞다.
        /// 옆면은 **스무스 셰이딩**(법선을 반경 방향으로 직접 지정)이라 내장 Cylinder와 음영이 사실상
        /// 같다. 덤불/풀의 평면 셰이딩과 달리 여기서 각을 세우지 않는 이유는 위 능선 꺾임각 근거와 같다.
        /// </summary>
        private const int PalmTrunkSides = 8;

        /// <summary>야자수 1그루의 잎 장수. 잎 1장은 안쪽/바깥쪽 2마디로 꺾여 아래로 늘어진다.</summary>
        private const int PalmFrondCount = 5;

        /// <summary>
        /// 야자수 한 그루를 만든다.
        ///
        /// [B48] 실물 모델(palm_a/b/c)이 있으면 **렌더러 2개**(줄기 1 + 크라운 1 / 1,388~1,784삼각형)이고,
        /// 없으면 아래 절차 조립(줄기 8각 프리즘 3 + 잎 박스 5×2 = 렌더러 13개 / 204삼각형)으로 폴백한다.
        /// 폴백 경로는 지우지 않는다 - 임포트 전이나 프로브 실패에서 야자수가 사라지면 안 된다.
        ///
        /// [B8 형태 개선] 이전 형태는 곧은 원기둥 1개 + 방사형으로 뻗은 평평한 판자 4개라서, 실기에서
        /// "가는 장대에 판자를 붙인 것"으로 보이고 야자수로 읽히지 않았다. 진짜 야자수의 실루엣을 만드는
        /// 요소는 두 가지뿐인데 둘 다 없었다:
        ///   (a) 기둥이 곧지 않고 위로 갈수록 한쪽으로 휘며 가늘어진다  → 마디 3개를 각도를 누적시켜 쌓는다.
        ///   (b) 잎이 밑동에서 위로 뻗다가 중간에서 꺾여 아래로 늘어진다 → 잎 1장을 2마디로 꺾는다.
        /// 통짜 기울기(예전 방식: 뿌리 오브젝트 자체를 tilt)로는 (a)가 안 된다 - 기둥 전체가 그대로
        /// 기울어져 밑동이 지면에서 뜨기만 한다. 그래서 뿌리에는 yaw만 주고 휨은 마디 누적으로 만든다.
        ///
        /// 뿌리 오브젝트의 스케일은 항상 1(균등)로 두고 회전만 준다 - 부모 스케일이 비균일한 상태에서
        /// 회전한 자식을 두면 전단(shear)으로 찌그러진다(CreatureVisualBuilder/StructureVisualBuilder
        /// 주석에 반복해서 나오는 이 프로젝트의 기존 함정).
        /// </summary>
        private static void CreatePalm(Transform parent, Vector3 groundPosition, System.Random rng,
            Material trunkMaterial, Material frondMaterial)
        {
            // 굵기: 예전 0.16~0.26m는 5~7m 높이에 대해 너무 가늘어 장대로 보였다. 밑동을 0.26~0.38m로
            // 올리고 위로 갈수록 62%까지 가늘어지게 해서 "굵은 밑동 → 가는 목"의 야자수 비례를 만든다.
            //
            // [B10 호출부 스케일 재검토 — 형태 교체와 함께 반드시 본다는 규칙]
            // 여기 값은 **외접 반지름**(정점이 놓이는 반지름)이다. 내장 Cylinder도 정점이 반지름 0.5에
            // 놓이므로 스케일의 의미 자체는 그대로지만, 화면에 보이는 굵기는 외접 반지름이 아니라
            // **평균 폭**(Cauchy: 2nR·sin(π/n)/π)이다. 20각 0.996·2R → 8각 0.9745·2R 이므로 같은
            // baseRadius를 그대로 넣으면 줄기가 **2.2% 가늘어 보인다**. 그래서 범위를 0.9958/0.9745
            // = 1.0219배 한 0.266~0.388로 올려 교체 전후 평균 굵기를 일치시킨다.
            // (참고: 6각이었다면 보정이 4.3%로 인지 한계에 걸린다 - 8각을 고른 또 하나의 이유다.)
            // 난수 소비는 그대로 1회다. NextFloat(min,max)는 범위와 무관하게 스트림을 한 번만 당기므로
            // 상·하한을 바꿔도 같은 worldSeed에서 이후 배치가 밀리지 않는다(파일 상단 [결정성] 전제 유지).
            float height = rng.NextFloat(4.6f, 7.6f);
            float baseRadius = rng.NextFloat(0.266f, 0.388f);
            float leanDirection = rng.NextFloat(0f, 360f);   // 어느 쪽으로 휘는가
            float leanStart = rng.NextFloat(1f, 5f);         // 밑동 마디의 기울기(거의 수직)
            float leanStep = rng.NextFloat(4f, 9f);          // 마디마다 더해지는 기울기
            float frondLength = rng.NextFloat(2.2f, 3.4f);
            float baseYaw = rng.NextFloat(0f, 360f);

            var palm = new GameObject("Veg_Palm");
            palm.transform.SetParent(parent, false);
            palm.transform.position = groundPosition;
            // 뿌리는 yaw만. 휨은 아래 마디 누적이 만들기 때문에 밑동은 항상 지면에 수직으로 박힌다.
            palm.transform.rotation = Quaternion.Euler(0f, leanDirection, 0f);

            // [줄기 차단 캡슐 — 파일 상단 [콜라이더 정책] 주석] 플레이어가 나무를 통과해 걷지 않게
            // 뿌리에 수직 캡슐 하나를 단다. 잎(크라운)은 통과 유지 — 콜라이더 없음.
            //  · 반지름 = baseRadius(밑동 외접 반지름 0.266~0.388m). 모델 경로도 같은 값을 쓴다 -
            //    모델 줄기 굵기가 이 분포에 맞춰 fit되므로 오차는 수 cm다.
            //  · 높이 = 나무 높이의 60%. 줄기는 위로 갈수록 휘지만(마디당 4~9° 누적) 플레이어가 닿는
            //    2m 아래에서는 수평 이탈이 0.2m 미만이라 수직 캡슐로 충분하다. 위쪽 40%를 비워
            //    휜 상단 줄기·크라운에 보이지 않는 벽이 생기는 것을 막는다.
            //  · rng 소비 0 — 이미 뽑힌 height/baseRadius만 쓴다(배치 재현성 불변).
            var trunkBlocker = palm.AddComponent<CapsuleCollider>();
            trunkBlocker.direction = 1; // Y축
            trunkBlocker.radius = baseRadius;
            trunkBlocker.height = height * 0.6f;
            trunkBlocker.center = new Vector3(0f, height * 0.3f, 0f);

            // ── [B48] 실물 야자수 모델(palm_a/b/c) ────────────────────────────────────
            //  · 렌더러가 13개(줄기 3 + 잎 5×2) → **2개**(줄기 1 + 크라운 1)가 된다. 모델에 줄기의 휨과
            //    잎의 꺾임이 이미 구워져 있어, 아래 마디 누적/잎 2마디 조립이 통째로 필요 없어진다.
            //  · **크기 규약**: 모델은 이미 미터 규격이다(밑면 y=0 · X/Z는 접지 중심). 절차 메시가
            //    단위 규격이라 호출부가 미터 크기를 스케일로 곱하던 것과 규약이 정반대라, 그대로 곱하면
            //    나무가 몇 배로 부푼다. 그래서 바위와 같이 **fit = 목표 높이 / 모델 실측 높이**의
            //    균등 배율만 쓴다(0.87~1.14).
            //  · 균등 배율 + 회전은 뿌리의 yaw뿐이라 전단이 원리적으로 없다(자식 회전은 identity).
            //  · 콜라이더는 여기서 **자동으로 생기지 않는다** - CreatePart는 프리미티브를 거치지 않고,
            //    모델도 프리팹을 Instantiate하지 않고 sharedMesh만 꺼내 쓰므로 임포터가 붙였을 콜라이더가
            //    씬에 안 들어온다. 물리 차단은 위에서 뿌리에 명시적으로 단 줄기 캡슐 하나뿐이다.
            //  · ★ 난수 ★ 변종 선택에 rng를 쓰지 않는다. 위에서 이미 뽑아 둔 height와 이미 확정된
            //    groundPosition(위치 해시 - [B50] 2단계 선택, TryGetPalmModel 주석)으로 고른다. 그리고
            //    아래 잎 루프의 draw는 **모델 경로에서도 전부 그대로 뽑는다**(파일 상단 [결정성] 주석).
            Mesh palmTrunkMesh, palmCrownMesh;
            float palmModelHeight;
            bool useModel = TryGetPalmModel(height, groundPosition, out palmTrunkMesh, out palmCrownMesh, out palmModelHeight);
            if (useModel)
            {
                float fit = height / Mathf.Max(0.01f, palmModelHeight);
                var modelScale = new Vector3(fit, fit, fit);

                var trunkPart = CreatePart(palm.transform, "Veg_PalmTrunk", palmTrunkMesh,
                    Vector3.zero, modelScale, Quaternion.identity, trunkMaterial);

                if (palmCrownMesh != null)
                {
                    CreatePart(palm.transform, "Veg_PalmCrown", palmCrownMesh,
                        Vector3.zero, modelScale, Quaternion.identity, frondMaterial);
                }
                else if (palmTrunkMesh.subMeshCount >= 2)
                {
                    // 임포터가 `o` 2개를 한 메시의 서브메시로 합쳐 온 경우. 렌더러 하나에 머티리얼 두 장을
                    // 주면 서브메시 0(줄기)/1(잎)이 각각 칠해진다 - 메시를 새로 만들지 않는 유일한 방법이다.
                    var renderer = trunkPart != null ? trunkPart.GetComponent<MeshRenderer>() : null;
                    if (renderer != null)
                        renderer.sharedMaterials = new[] { trunkMaterial, frondMaterial };
                }
            }

            float segmentLength = height / PalmTrunkSegments;
            Vector3 cursor = Vector3.zero;      // 지금까지 쌓아 올린 줄기 끝(로컬)
            float lean = 0f;

            for (int i = 0; i < PalmTrunkSegments; i++)
            {
                lean = leanStart + i * leanStep;
                // X축 회전 a는 원기둥의 축(+Y)을 (0, cos a, sin a)로 눕힌다. 마디를 그 방향으로 쌓는다.
                Quaternion rotation = Quaternion.Euler(lean, 0f, 0f);
                Vector3 direction = rotation * Vector3.up;
                float t = (i + 0.5f) / PalmTrunkSegments;
                float segmentRadius = Mathf.Lerp(baseRadius, baseRadius * 0.62f, t);

                // 프리즘 메시는 내장 Cylinder와 동일한 로컬 규격(반지름 0.5·높이 2)이라 아래 스케일 식이
                // 그대로 유효하다. localScale.y에 "마디 길이의 절반"을 넣고, 마디 사이가 벌어져 보이지
                // 않게 길이를 6% 겹쳐 쌓는다.
                // [B48] 모델 경로에서는 파츠만 건너뛴다. 이 루프는 rng를 한 번도 쓰지 않으므로
                // 건너뛰어도 난수 소비가 달라지지 않는다(아래 잎 루프는 사정이 다르다 - 거기 주석 참고).
                if (!useModel)
                {
                    CreatePart(palm.transform, $"Veg_PalmTrunk{i}", GetPalmTrunkPrismMesh(),
                        cursor + direction * (segmentLength * 0.5f),
                        new Vector3(segmentRadius * 2f, segmentLength * 0.53f, segmentRadius * 2f),
                        rotation, trunkMaterial);
                }

                cursor += direction * segmentLength;
            }

            // 잎은 줄기 끝(cursor)에서 뻗는다. 줄기가 휜 만큼 왕관도 따라 기울어져 있어야 자연스럽다.
            Quaternion crownTilt = Quaternion.Euler(lean * 0.6f, 0f, 0f);

            for (int i = 0; i < PalmFrondCount; i++)
            {
                float yaw = baseYaw + i * (360f / PalmFrondCount) + rng.NextFloat(-14f, 14f);
                // 안쪽 마디: 살짝 위로 솟았다가(음수 피치 = 위쪽) 수평 근처까지.
                float innerPitch = rng.NextFloat(-16f, 4f);
                // 바깥 마디: 안쪽에서 40~68° 더 꺾여 아래로 늘어진다. 이 꺾임이 야자수 실루엣의 핵심이다.
                float outerPitch = innerPitch + rng.NextFloat(40f, 68f);

                // ★ [B48] 난수 소비 불변 ★ 위 세 draw(yaw / innerPitch / outerPitch)는 **모델 경로에서도
                // 반드시 뽑는다.** 여기서 한 번이라도 덜 뽑으면 같은 worldSeed에서 뒤따르는 덤불·풀포기·
                // 바위·표류물이 통째로 밀린다(파일 상단 [결정성] 주석 · 바위에서 쓴 방법과 같다).
                if (useModel)
                    continue;

                float innerLength = frondLength * 0.44f;
                float outerLength = frondLength * 0.64f;

                Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
                Quaternion innerRotation = crownTilt * yawRotation * Quaternion.Euler(innerPitch, 0f, 0f);
                Quaternion outerRotation = crownTilt * yawRotation * Quaternion.Euler(outerPitch, 0f, 0f);

                // 잎 박스의 로컬 +Z가 잎 길이 방향이다. 회전시킨 방향으로 길이의 절반만큼 밀어
                // 밑동이 줄기 꼭대기(또는 앞 마디 끝)에 붙게 한다.
                Vector3 innerCenter = cursor + innerRotation * new Vector3(0f, 0f, innerLength * 0.5f);
                Vector3 joint = cursor + innerRotation * new Vector3(0f, 0f, innerLength);
                Vector3 outerCenter = joint + outerRotation * new Vector3(0f, 0f, outerLength * 0.5f);

                CreatePart(palm.transform, $"Veg_PalmFrond{i}A", PrimitiveType.Cube,
                    innerCenter, new Vector3(0.42f, 0.07f, innerLength), innerRotation, frondMaterial);
                // 바깥 마디는 폭/두께를 줄여 끝으로 갈수록 가늘어지게 한다(잎 끝이 뭉툭하면 판자로 보인다).
                CreatePart(palm.transform, $"Veg_PalmFrond{i}B", PrimitiveType.Cube,
                    outerCenter, new Vector3(0.28f, 0.05f, outerLength), outerRotation, frondMaterial);
            }
        }

        /// <summary>
        /// 덤불 한 개(포기 전체가 메시 한 장, 렌더러 1개). [B50] 실물 모델(bush_a 470 / bush_b 566삼각형)이
        /// 있으면 모델을, 없으면 절차 메시(92삼각형)를 쓴다. 야자수보다 낮아 시야를 막지 않는다.
        /// [B29] 로브 3개를 별도 파츠로 붙이던 것을 메시에 구워 렌더러를 3 → 1로 줄이고, 남은 예산으로
        /// 잎끝 8장을 넣었다(GetBushClumpMesh). 폭·높이 범위와 난수 소비는 예전과 완전히 동일하다.
        ///
        /// [B8] 예전에는 매끈한 타원 2개가 거의 동심으로 겹쳐 있어 실루엣이 하나의 매끈한 돌덩이였고,
        /// 돌조각 자원 노드와 구분되지 않았다. 자연물 중 "덤불"만 가진 신호는 (a) 위쪽이 울퉁불퉁하게
        /// 튀어나온 여러 덩이, (b) 폭이 높이보다 확실히 큰 납작한 비례 두 가지다. 로브를 3개로 늘리고
        /// 각 로브를 서로 다른 방향으로 기울여 윤곽선이 매끈한 곡선이 되지 않게 만든다.
        /// (돌은 기울지 않은 단일 덩어리다 - 색이 초록으로 바뀐 것과 합쳐 20m 밖에서도 갈린다.)
        ///
        /// [B9 저폴리 교체] 로브를 내장 Sphere(768삼각형)에서 정이십면체(20삼각형)로 바꿨다. 위 B8 실루엣
        /// 규칙 - 기울인 로브 3개 · 폭 &gt;&gt; 높이 - 은 하나도 바꾸지 않는다(스케일·회전·오프셋 그대로).
        /// 오히려 각진 면이 생겨 "매끈한 돌덩이"와의 구분이 강해진다. 난수 소비 순서·횟수도 그대로다.
        /// </summary>
        private static void CreateBush(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            float width = rng.NextFloat(1.3f, 2.2f);
            float height = rng.NextFloat(0.6f, 1.0f); // 폭 대비 확실히 낮게 - 납작한 비례가 돌과의 1차 구분
            float yaw = rng.NextFloat(0f, 360f);

            // [B29] 난수 소비 순서·횟수를 예전과 **한 번도 다르지 않게** 유지한다. 로브 3개가 메시 한 장에
            // 구워졌지만(아래 GetBushClumpMesh), 여기서 값을 덜 뽑으면 같은 worldSeed에서 뒤따르는
            // 풀포기 배치가 통째로 밀린다. 뽑은 값 중 실제로 쓰는 것은 기울기와 변주 선택뿐이다.
            float tiltZ = rng.NextFloat(-10f, 10f);   // (예전 주 로브의 기울기 - 이제 포기 전체의 기울기다)
            rng.NextInsideUnitCircle();                // 예전 로브0 오프셋
            float variantRoll = rng.NextFloat(0.50f, 0.76f); // 예전 로브0 크기 → 지금은 메시 변주 선택
            rng.NextFloat(0.55f, 0.80f);               // 예전 로브0 높이
            float tiltX = rng.NextFloat(-22f, 22f) * 0.35f;  // 예전 로브0 X기울기 → 포기 전체에 얕게
            rng.NextFloat(-22f, 22f);                  // 예전 로브0 Z기울기
            rng.NextInsideUnitCircle();                // 예전 로브1 오프셋
            rng.NextFloat(0.50f, 0.76f);               // 예전 로브1 크기
            rng.NextFloat(0.55f, 0.80f);               // 예전 로브1 높이
            rng.NextFloat(-22f, 22f);                  // 예전 로브1 X기울기
            rng.NextFloat(-22f, 22f);                  // 예전 로브1 Z기울기

            // ── [B50] 실물 덤불 모델(bush_a/b) ──
            //  위 draw들은 모델 경로에서도 **전부 그대로 뽑았다**(바위·야자수와 같은 선추첨 패턴 -
            //  여기서 덜 뽑으면 같은 worldSeed에서 뒤따르는 풀포기 배치가 통째로 밀린다).
            //  · 변종 선택: 목표 폭 ±35% 후보 → 위치 해시(TryGetBushModel). rng 소비 0.
            //  · 크기: 모델이 미터 규격(밑면 y=0 · X/Z 중심)이라 **균등 배율 fit = width / 실측 폭**만
            //    쓴다(바위·야자수와 같은 규약 - 절차 메시처럼 (폭,높이,깊이)를 곱하면 2배로 부푼다).
            //  · 회전: yaw만. 절차 경로의 tiltX/tiltZ는 주지 않는다 - 모델 밑면이 평평해서 기울이면
            //    한쪽 가장자리가 지면에서 뜬다(바위 모델이 yaw만 받는 것과 같은 이유).
            //  · 다양성 보강: fit이 균등이라 높이가 폭에 묶인다(절차 경로는 높이를 따로 뽑았다).
            //    잃어버린 그 축을 위치 해시의 **다른 salt**(BushStretchSalt)로 세로만 0.92~1.12배
            //    지터해 되살린다 - 새 rng 추첨 없음, yaw 회전+세로 스케일은 축이 일치해 전단 없음.
            Mesh bushModelMesh;
            Vector3 bushModelSize;
            if (TryGetBushModel(width, groundPosition, out bushModelMesh, out bushModelSize))
            {
                float fit = width / Mathf.Max(0.01f, bushModelSize.x);
                float stretch = Mathf.Lerp(0.92f, 1.12f, DecorationPositionHash01(groundPosition, BushStretchSalt));
                var modelBush = CreatePart(parent, "Veg_Bush", bushModelMesh,
                    Vector3.zero, new Vector3(fit, fit * stretch, fit),
                    Quaternion.Euler(0f, yaw, 0f), material);
                modelBush.transform.position = groundPosition; // 접지 원점 - 중심 보정 없음
                return;
            }

            // 폴백(절차 메시 - 지우지 않는다). 메시 규격: x·z ∈ [-0.5, 0.5], **y ∈ [0, 1]이고 원점이
            // 밑동**이다(구 규격이 아니다). 그래서 위치는 지면 그대로, 스케일은 (폭, 높이, 깊이)를
            // 미터로 넣으면 된다. 예전 3파츠 구성과 화면상 크기가 같도록 폭·높이 범위는 그대로다.
            int variant = Mathf.Clamp(Mathf.FloorToInt((variantRoll - 0.50f) / 0.26f * 3f), 0, 2);
            var bush = CreatePart(parent, "Veg_Bush", GetBushClumpMesh(variant),
                Vector3.zero, new Vector3(width, height, width * 0.9f),
                Quaternion.Euler(tiltX, yaw, tiltZ), material);
            bush.transform.position = groundPosition;
        }

        /// <summary>
        /// 풀포기 한 개(렌더러 1개). [B50] 실물 모델(grass_a 84 / grass_b 120삼각형)이 있으면 모델을,
        /// 없으면 절차 메시(잎 5장 부채꼴, 양면·2마디 40삼각형)를 쓴다. 개수가 제일 많아 렌더러 1개 유지.
        /// [B8] 두께를 폭의 80% → 30%로 줄이고 좌우로 살짝 눕혀, 위에서 봐도 "납작한 덩어리"가 아니라
        /// 풀잎 다발이 서 있는 것처럼 보이게 한다.
        /// [B9] 그 "눌린 구"(768삼각형)를 같은 규격의 잎 부채꼴 메시(12삼각형)로 교체했다. 눌린 구가
        /// 화면에서 실제로 하던 일이 "위로 솟은 납작한 잎 다발"이라 실루엣은 사실상 동일하고, 끝이
        /// 뾰족해져 오히려 풀로 더 잘 읽힌다. 스케일·회전·위치 계산과 난수 소비는 한 줄도 바뀌지 않았다.
        /// </summary>
        private static void CreateGrassTuft(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            // [B9 디렉터 수정] 폭 0.7~1.5m 는 풀포기가 아니라 관목 크기였다(플레이어 몸통보다 넓다).
            // 이 값은 이전에 "눌린 구"였을 때 잡은 것인데, 잎 판으로 바뀌면서 그 크기가 그대로 벽이 됐다.
            // 실제 풀포기 비례로 되돌린다.
            float width = rng.NextFloat(0.32f, 0.62f);
            float height = rng.NextFloat(0.26f, 0.46f);
            float yaw = rng.NextFloat(0f, 360f);
            float lean = rng.NextFloat(-14f, 14f);

            // ── [B50] 실물 풀 모델(grass_a/b) ──
            //  draw 4개는 모델 경로에서도 위에서 전부 뽑았다(선추첨 - 소비 순서·횟수 불변).
            //  · 변종 선택: 목표 폭 ±35% 후보 → 위치 해시(TryGetGrassModel). rng 소비 0.
            //  · ★ 원점 함정 ★ 신규 풀 모델은 **접지 중심 원점**(밑면 y=0)이다. 아래 폴백의
            //    groundPosition + up*(height*0.35) 중심 보정은 구 규격([-0.5,0.5]^3) 메시를 지면에
            //    걸치게 하는 값이라, 모델에 그대로 쓰면 풀이 공중에 뜬다. 모델 경로는 보정 없이
            //    지면 좌표를 그대로 쓴다(폴백 경로의 보정은 유지).
            //  · 회전: yaw만. lean(±14°)은 주지 않는다 - 밑면이 지면과 맞닿는 모델을 기울이면
            //    잎다발 전체가 한쪽으로 떠서, 가느다란 풀은 덤불보다 부양이 더 잘 보인다.
            //  · 다양성 보강: 균등 fit으로 잃은 높이 축을 위치 해시의 다른 salt(GrassStretchSalt)로
            //    세로 0.90~1.15배 지터해 되살린다(새 rng 추첨 없음, yaw+세로 스케일이라 전단 없음).
            Mesh grassModelMesh;
            Vector3 grassModelSize;
            GameObject tuft;
            if (TryGetGrassModel(width, groundPosition, out grassModelMesh, out grassModelSize))
            {
                float fit = width / Mathf.Max(0.01f, grassModelSize.x);
                float stretch = Mathf.Lerp(0.90f, 1.15f, DecorationPositionHash01(groundPosition, GrassStretchSalt));
                tuft = CreatePart(parent, "Veg_GrassTuft", grassModelMesh,
                    Vector3.zero, new Vector3(fit, fit * stretch, fit),
                    Quaternion.Euler(0f, yaw, 0f), material);
                tuft.transform.position = groundPosition; // 접지 원점 - 중심 보정 없음
            }
            else
            {
                // 폴백(절차 메시 - 지우지 않는다). 구 규격이라 중심 보정(height*0.35)이 필요하다.
                tuft = CreatePart(parent, "Veg_GrassTuft", GetGrassBladeMesh(),
                    Vector3.zero, new Vector3(width, height, width * 0.30f),
                    Quaternion.Euler(0f, yaw, lean), material);
                tuft.transform.position = groundPosition + Vector3.up * (height * 0.35f);
            }

            // 풀포기는 5m 밖에서 그림자가 보이지 않는데 개수만 많아, 그림자 드리우기를 끈다
            // (ArtDirection 2장 "폴리곤을 아낄 곳은 5m 밖에서 안 보이는 디테일").
            var renderer = tuft.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

    }
}
