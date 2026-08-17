using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// IslandResourceSpawner의 시각(형태·색·텍스처) partial 분할 파일. 자원 종류별 형태 결정
    /// (GetNodeShape)·접지 계산(GetHalfHeight)·루트 메시 교체(ApplyRootMesh)·채집 콜라이더 보정
    /// (WidenHarvestCollider)·보조 파츠(AddResourceDetailParts/AddPart/AddMeshPart)·표면 색/텍스처
    /// 결정(GetWorldSurfaceColor/GetSurfaceTextureName 계열)과 파츠 명도 변주표(CulmTints 등)를
    /// IslandResourceSpawner.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 배치 로직(스폰 루프·stableKey/종류별 카운터·착륙 원·대나무 증량·SpawnSingleNode)은
    /// IslandResourceSpawner.cs에, 공유 메시/머티리얼 보관소(ResourceVisualLibrary)는
    /// IslandResourceSpawner.MeshLibrary.cs에 있다.
    /// </summary>
    public partial class IslandResourceSpawner : MonoBehaviour
    {
        // [B28] 파츠별 명도 변주표. 감독 지시("줄기마다 색을 조금씩 다르게")를 만족시키되, 값을 **고정 표**로
        // 두는 것이 핵심이다 - 무작위 실수 배율을 쓰면 색 조합마다 머티리얼이 새로 만들어져 공유가 깨진다.
        // 표가 4단계뿐이라 자원 하나가 쓰는 머티리얼은 최대 4개이고, 그 4개를 월드의 모든 섬이 공유한다.
        // 색상(hue)은 건드리지 않고 명도만 바꾼다 - 팔레트 소유권은 ArtDirection/UIBuilder에 있다.
        private static readonly float[] CulmTints = { 0.86f, 1.06f, 0.94f, 1.00f };
        private static readonly float[] TwigTints = { 1.04f, 0.88f, 0.96f, 0.80f };
        private static readonly float[] RockTints = { 0.92f, 1.05f, 0.84f };
        private static readonly float[] FrondTints = { 0.90f, 1.04f, 0.96f };

        /// <summary>
        /// 자원 종류별로 실제 사용할 프리미티브 형태/크기/기울기를 정한다. 예전엔 전부 큐브(1x1.5x1)
        /// 하나뿐이었던 것을, 원기둥(대나무/부력통/연료 스파우트 등)·구(돌/코코넛)·납작한 큐브(천/금속조각)
        /// 등으로 나눠 실루엣만 봐도 어떤 자원인지 구분할 수 있게 했다.
        /// </summary>
        private void GetNodeShape(string itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            switch (itemName)
            {
                case "나뭇가지": // 굵은 가지 하나(옹이·테이퍼는 ApplyRootMesh) + 흩어진 잔가지 2~4개(AddResourceDetailParts)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.09f, 0.32f, 0.09f);
                    break;
                case "대나무": // 한 포기의 중심 줄기. 마디는 **메시 안에** 있고(ApplyRootMesh), 곁줄기 2~4개와 잎다발은 AddResourceDetailParts
                    // [B29 감독 보고 "대나무가 너무 짧음"] 1.05 → 2.10.
                    // 실린더는 로컬 높이가 2(=-1~+1)이므로 **총 높이 = scale.y × 2**다. 2.10이면 4.2m이고,
                    // 세로 지터(0.85~1.25)까지 합치면 3.57~5.25m - 눈높이 1.6m를 한참 올려다보게 된다
                    // (예전은 1.79~2.63m로 사람 키 남짓이었다).
                    // 가로 0.14 → 0.30은 두 가지를 동시에 노린다:
                    //  (1) **콜라이더 = 채집 판정**이다. CreatePrimitive가 붙인 캡슐의 반지름은
                    //      0.5 × scale.x이므로 지름이 0.14m → 0.30m가 된다. 눈에 보이는 포기 폭(곁줄기가
                    //      중심에서 0.34m까지 퍼진다)에 비해 판정이 너무 가늘다는 지적을 여기서 갚는다.
                    //      높이도 2.1 → 4.2m가 되어 올려다보는 각도에서도 줄기 어디를 조준하든 맞는다.
                    //  (2) 보이는 줄기 굵기는 콜라이더와 **분리해서** 정한다 - BambooCulmUnit의 메시
                    //      반지름을 0.34 → 0.22로 함께 줄였으므로 실제로 보이는 지름은 0.136m가 아니라
                    //      0.132m다(예전 0.095m). 높이가 2배가 됐는데 굵기가 그대로면 국수 가락이 되고,
                    //      정비례로 2배(0.19m)면 통나무가 된다. 세장비 22 → 32로 **더 늘씬해지되**
                    //      절대 굵기는 1.4배로 함께 키운 값이다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.30f, 2.10f, 0.30f);
                    break;
                case "돌조각": // 각진 파편 무더기 (파편 형태는 ApplyRootMesh, 곁돌 2~3개는 AddResourceDetailParts)
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.5f, 0.32f, 0.5f);
                    break;
                case "부싯돌": // 얇고 각진 석기 파편 - 살짝 비스듬히 기울여 둠 (형태는 ApplyRootMesh)
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.32f, 0.1f, 0.42f);
                    rotation = Quaternion.Euler(8f, 25f, -5f);
                    break;
                case "코코넛": // 둥근 열매 (여분 하나는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "천조각": // 얇고 넓은 천 조각
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.55f, 0.05f, 0.4f);
                    break;
                case "야자잎": // 짧은 잎자루 위로 잎맥 있는 잎 3장이 부채꼴로 퍼짐 (AddResourceDetailParts에서 추가)
                    // 이 스케일은 **보이는 잎자루(지름 5cm · 높이 16cm)**의 크기이고, 잎 3장은 여기에
                    // 포함되지 않는다(AddMeshPart가 부모 스케일을 정확히 상쇄해 미터 메시를 그대로 세운다).
                    // 그래서 채집 판정을 이 스케일에 맡기면 지름 5cm짜리 막대만 맞고 폭 1m가 넘는 잎은
                    // 통째로 허공이 된다 - 조준이 거의 안 맞던 실제 원인이다.
                    // **콜라이더는 WidenHarvestCollider가 따로 넓힌다**(대나무에서 검증한 "보이는 굵기와
                    // 채집 판정을 분리한다" 규칙과 같다). 이 값을 키워서 고치려 하지 마라 - 잎자루가
                    // 함께 굵어지고, 잎이 붙는 높이(stemTop = parentScale.y × 0.9)까지 같이 올라간다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.05f, 0.08f, 0.05f);
                    break;
                case "금속조각": // 찌그러진 얇은 금속판
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.5f, 0.06f, 0.34f);
                    rotation = Quaternion.Euler(6f, 20f, 0f);
                    break;
                case "부력통": // 짧고 통통한 드럼통 형태
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "비상식량": // 작은 배급 상자
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.34f, 0.22f, 0.26f);
                    break;
                case "연료": // 각진 연료통 몸체 (주둥이는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.28f, 0.4f, 0.22f);
                    break;
                case "엔진부품": // 짧은 원판형 부품 (볼트는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.3f, 0.22f, 0.3f);
                    break;
                case "석재": // [4티어] 밝은 회색의 각진 원석 한 덩어리 - 돌조각(납작한 파편 무더기)보다 크고 도톰하다
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.62f, 0.44f, 0.62f);
                    break;
                case "대리석": // [4티어] 거의 흰(0.9) 노두 - 형태는 석재와 같은 각진 덩어리, 색과 매끈한 표면("noise")으로 갈린다
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.52f, 0.40f, 0.52f);
                    break;
                case "생수": // 표류한 생수병 (목/뚜껑은 AddResourceDetailParts에서 추가)
                    // [B28 버그 수정] 씬 resourceEntries 13번째 항목이 생수인데(baseCount 1, 중형 섬 이상)
                    // 여기에 case가 없어서 default로 떨어졌다 - 중형 이상 섬마다 **1×1.5×1m짜리 파란 큐브**가
                    // 2~4개씩 서 있었다. 다른 자원 노드(0.2~0.5m)의 세 배 크기라 멀리서 보면 건축물처럼
                    // 보인다. 실제 크기(지름 0.14m · 높이 0.28m)의 병으로 바꾼다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.07f, 0.14f, 0.07f);
                    break;
                default: // 목록에 없는 새 자원이 추가되면 기존 큐브로 안전하게 폴백
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(1f, 1.5f, 1f);
                    break;
            }
        }

        /// <summary>
        /// 프리미티브 종류별 로컬 단위 형태 차이를 감안해, 지정한 스케일일 때 피벗(중심)을 지면 위
        /// 몇 미터에 둬야 바닥이 정확히 지면에 닿는지 계산한다. 큐브/구는 반높이가 scale.y*0.5인데
        /// 실린더는 기본 높이가 2(로컬 -1~+1)라서 반높이가 scale.y*1이다 - 이 차이를 반영하지 않으면
        /// 프리미티브 종류에 따라 절반이 땅에 묻히거나 붕 떠 보인다.
        ///
        /// [B28] 회전을 함께 받는다. 예전에는 스케일의 y성분만 봤기 때문에, **기울여 놓은** 자원
        /// (부싯돌 Euler(8,25,-5) · 금속조각 Euler(6,20,0))은 기울어진 만큼 모서리가 지면 아래로
        /// 파고들었다. Y축만 도는 자원은 아래 식이 예전 값과 **정확히 같은 값**을 돌려주므로
        /// (회전 행렬 2행이 (0,1,0)이라 y항만 남는다) 기존 배치는 1mm도 움직이지 않는다.
        /// </summary>
        private float GetHalfHeight(PrimitiveType primitive, Vector3 scale, Quaternion rotation)
        {
            // 회전 행렬의 2행 = 로컬 축들이 월드 Y에 기여하는 비율.
            Matrix4x4 basis = Matrix4x4.Rotate(rotation);
            float rx = basis.m10;
            float ry = basis.m11;
            float rz = basis.m12;

            if (primitive == PrimitiveType.Sphere)
            {
                // 구(타원체)는 상자 근사를 쓰면 회전할 때마다 최대 73%까지 과대평가되어 공중에 뜬다.
                // 타원체의 지지함수는 정확히 아래 형태라 회전과 무관하게 딱 맞는다.
                float ex = rx * scale.x * 0.5f;
                float ey = ry * scale.y * 0.5f;
                float ez = rz * scale.z * 0.5f;
                return Mathf.Sqrt(ex * ex + ey * ey + ez * ez);
            }

            float halfY = primitive == PrimitiveType.Cylinder || primitive == PrimitiveType.Capsule ? 1f : 0.5f;
            return Mathf.Abs(rx) * scale.x * 0.5f + Mathf.Abs(ry) * scale.y * halfY + Mathf.Abs(rz) * scale.z * 0.5f;
        }

        /// <summary>
        /// 루트 프리미티브의 메시를 자원 종류 전용 절차 메시로 갈아 끼운다(해당 종류가 없으면 그대로 둔다).
        ///
        /// 지켜야 하는 계약이 하나 있다: **메시는 프리미티브의 로컬 규격을 벗어나지 않는다.**
        /// 실린더/캡슐은 y가 -1~+1, 큐브/구는 -0.5~+0.5다. 이 규격을 지키는 한
        /// (a) CreatePrimitive가 붙여 준 콜라이더(= 채집 판정 범위)와 (b) 위 GetHalfHeight의 지면 접지
        /// 계산과 (c) ResourceNode.RootTopLocalY의 파츠 부착 기준이 전부 예전 값 그대로 유지된다.
        /// 콜라이더는 손대지 않는다 - 형태만 바뀌고 채집 판정 범위는 이전과 동일하다.
        /// </summary>
        private void ApplyRootMesh(GameObject go, string itemName, int variant)
        {
            Mesh mesh = null;
            switch (itemName)
            {
                case "대나무": mesh = ResourceVisualLibrary.BambooCulmUnit(variant); break;
                case "나뭇가지": mesh = ResourceVisualLibrary.BranchStickUnit(variant); break;
                case "돌조각": mesh = ResourceVisualLibrary.RockChunkUnit(variant); break;
                case "부싯돌": mesh = ResourceVisualLibrary.StoneFlakeUnit(variant); break;
                // [4티어] 원석 2종은 돌조각과 같은 각진 파편 메시를 루트로 재사용한다(신규 모델 없음 규격).
                // 실물 바위 모델이 로드되면 AddResourceDetailParts가 루트 렌더러를 끄고 모델로 갈아 끼운다.
                case StoneOreItemName: mesh = ResourceVisualLibrary.RockChunkUnit(variant); break;
                case MarbleOreItemName: mesh = ResourceVisualLibrary.RockChunkUnit(variant); break;
            }

            if (mesh == null)
                return;

            var filter = go.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = mesh;
        }

        /// <summary>
        /// **채집 판정 콜라이더만** 실제 실루엣에 맞춰 넓힌다. 보이는 메시는 한 폴리곤도 건드리지 않는다.
        ///
        /// 대부분의 자원은 루트 프리미티브 자체가 실루엣의 대부분이라 "루트 스케일 = 콜라이더"로 충분하다
        /// (돌조각: 구 반지름 0.25m, 대나무: 캡슐 반지름 0.15m). 그런데 야자잎만은 루트가 **잎자루**이고
        /// 실루엣의 99%가 AddMeshPart로 붙는 잎 3장이다. AddMeshPart는 부모 스케일을 정확히 상쇄하므로
        /// (자식 localScale = S⁻¹) 잎 크기는 루트 스케일과 완전히 독립이고, 결과적으로 지름 5cm짜리
        /// 콜라이더가 폭 1m가 넘는 잎을 대표하고 있었다.
        ///
        /// 여기서 콜라이더 필드(=로컬 단위)를 직접 조정한다. 루트 스케일을 키우는 방식은 잎자루 굵기와
        /// 잎이 붙는 높이가 함께 변해 승인된 디자인이 바뀌므로 쓸 수 없다.
        ///
        /// 목표 반지름 0.24m의 근거: 잎 길이가 0.44~0.58m(FrondMeters 변주 0~2)이므로 잎 하나의 안쪽
        /// 절반을 덮는 값이고, 대나무(보이는 포기 반경 0.34m에 콜라이더 반지름 0.15m)와 같은 비율대다.
        /// 예전 판정(반지름 0.025m)의 약 10배다.
        ///
        /// 캡슐은 높이(0.16~0.20m)보다 지름(0.48m)이 커서 Unity가 **구로 클램프**한다 - 잎이 사방으로
        /// 퍼진 납작한 부채꼴에는 오히려 이쪽이 맞다. 지터(scale.x = scale.z)를 나눠 주므로 개체마다
        /// 판정 크기가 흔들리지 않고 항상 정확히 0.24m다(잎 메시가 미터 고정이라 그게 맞다).
        /// </summary>
        private void WidenHarvestCollider(GameObject go, string itemName, Vector3 scale)
        {
            if (itemName != "야자잎")
                return;

            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return;

            const float FrondHitRadiusMeters = 0.24f;

            // CapsuleCollider의 월드 반지름 = radius x max(|scale.x|, |scale.z|).
            float horizontal = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            capsule.radius = FrondHitRadiusMeters / horizontal;
        }

        /// <summary>
        /// 자원 종류별 보조 파츠를 덧붙여 기본 프리미티브만으로는 부족한 디테일(마디/부채꼴 잎/볼트 등)을
        /// 더한다. 파츠는 순수 시각용이라 콜라이더를 만들지 않고(AddPart에서 제거), 부모의 상호작용용
        /// 콜라이더와 절대 간섭하지 않는다.
        /// </summary>
        private void AddResourceDetailParts(GameObject go, string itemName, Vector3 parentScale, Color color, string textureName, System.Random rng)
        {
            switch (itemName)
            {
                case "나뭇가지":
                    // [B28] "주워 모을 잔가지 더미"로 다시 만들었다. 예전에는 세운 원기둥에 곁가지를
                    // Euler(15,0,±55~135)로 붙였는데, 부모 스케일이 (0.09, 0.32, 0.09)라 x:y가 3.5:1이고
                    // **비균일 스케일 부모 밑에서 회전한 자식은 전단(shear)으로 찌그러진다** - 굵기가 각도에
                    // 따라 3배까지 변해서 가지가 아니라 구부러진 리본으로 보였다.
                    // 지금은 기울기를 메시에 구워 넣고(AddMeshPart 주석 참고) 자식에는 Y 회전만 준다.
                    // 길이(0.26~0.52m)·굵기(2.2~4.0cm)·들린 각도(12~70도)·갈래 유무가 변주 6종에 나뉘어 있어
                    // 같은 더미 안에서도 굵기와 각도가 제각각으로 읽힌다.
                    {
                        float groundY = -parentScale.y; // 루트 실린더 바닥(로컬 -1)까지의 미터 거리
                        int twigCount = rng.NextInt(2, 5);
                        for (int i = 0; i < twigCount; i++)
                        {
                            float around = rng.NextFloat(0f, 360f) * Mathf.Deg2Rad;
                            float dist = rng.NextFloat(0.015f, 0.085f);
                            Vector3 offset = new Vector3(Mathf.Cos(around) * dist, groundY + 0.008f, Mathf.Sin(around) * dist);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, TwigTints[i % TwigTints.Length]), textureName);
                            AddMeshPart(go, $"Twig{i}", offset, parentScale,
                                ResourceVisualLibrary.TwigMeters(rng.NextInt(0, 6)), material, rng.NextFloat(0f, 360f));
                        }
                        break;
                    }

                case "대나무":
                    // [B28 최우선 과제] 밋밋한 원통 하나 → **한 포기(3~5줄기)**.
                    // 예전 마디는 `worldSize (1.15, 0.02, 1.15)`였는데 AddPart의 worldSize는 로컬 배수가
                    // 아니라 **미터**다(아래 AddPart 주석). 즉 지름 14cm 줄기에 지름 **1.15m짜리 원반**이
                    // 2~3장 꽂혀 있었다 - 이게 대나무가 대나무로 안 보이던 진짜 원인이다.
                    // 지금 마디는 파츠가 아니라 줄기 메시 자체의 굵기 변화다(마디 아래를 0.93으로 조이고
                    // 마디에서 1.22로 부풀린다). 파츠를 하나도 쓰지 않으므로 줄기당 3~5마디가 공짜다.
                    {
                        // [B29] groundY는 **미터**이고 루트 피벗이 지면 위 parentScale.y(=반높이)에 있으므로,
                        // 루트 스케일을 키워도 곁줄기 밑동은 자동으로 지면에 붙는다(계산을 다시 할 필요가 없다).
                        float groundY = -parentScale.y;

                        // ── [B48] 실물 대나무 모델(bamboo_a~f) ──────────────────────────────
                        //  · 모델 하나가 이미 **한 포기**(줄기 여러 대 + 잎)라, 절차 곁줄기·잎다발을 통째로
                        //    대체한다. 파츠는 최대 8개 → 2개다.
                        //  · 목표 높이는 **이미 뽑아 둔 세로 지터**가 정한 루트 높이(parentScale.y × 2 =
                        //    3.57~5.25m)다. 새 난수는 0회이고, 변종 선택도 그 값으로 결정론적으로 한다.
                        //  · 크기 규약: 모델은 이미 미터 규격(밑면 y=0)이다. 그래서 AddMeshPart의 미터
                        //    좌표계에 **fit = 목표 높이 / 모델 실측 높이**의 균등 배율만 곱한다(6종 기준 0.93~1.08).
                        //  · ★ 채집 콜라이더는 손대지 않는다 ★ 루트 캡슐(지름 0.30m × 세로 지터)이 조준
                        //    판정이고, 여기서 만드는 것은 콜라이더가 없는 순수 시각 파츠뿐이다.
                        float clumpHeight = parentScale.y * 2f;
                        Mesh bambooCulms, bambooLeaves;
                        float bambooModelHeight;
                        bool useBambooModel = ResourceVisualLibrary.TryGetBambooModel(
                            clumpHeight, out bambooCulms, out bambooLeaves, out bambooModelHeight);

                        int culmCount = rng.NextInt(2, 5); // 루트 줄기 + 2~4 = 한 포기에 3~5줄기
                        for (int i = 0; i < culmCount; i++)
                        {
                            float around = rng.NextFloat(0f, 360f) * Mathf.Deg2Rad;
                            // [B29] 0.07~0.19m → 0.12~0.34m. 줄기가 2배 길어졌는데 밑동 간격이 그대로면
                            // 한 다발로 뭉쳐 굵은 기둥 하나처럼 보인다(기울기도 함께 2배로 키웠다).
                            float dist = rng.NextFloat(0.12f, 0.34f);
                            // ★ [B48] 난수 소비 불변 ★ 아래 두 draw(메시 변주 · 방위각)는 모델 경로에서도
                            // **반드시 여기서 뽑는다.** 인자 안에서 뽑던 것을 지역변수로 끌어낸 이유가 그것이다
                            // (바위에서 쓴 방법). 한 번이라도 덜 뽑으면 같은 worldSeed에서 뒤따르는 노드의
                            // 위치·지터가 통째로 밀린다(같은 시드 = 같은 월드 재현성 위반).
                            // [세이브 키 v2] 세이브 키는 이제 종류별 안정 해시(stableKey)라 키 자체는 안
                            // 밀리지만, 노드의 "위치"가 바뀌면 결국 같은 세이브가 다른 월드 위에 얹히는
                            // 것이므로 이 rng 규율은 여전히 유효하다.
                            int culmVariant = rng.NextInt(0, 5);
                            float culmYaw = rng.NextFloat(0f, 360f);
                            if (useBambooModel)
                                continue;

                            Vector3 offset = new Vector3(Mathf.Cos(around) * dist, groundY, Mathf.Sin(around) * dist);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, CulmTints[i % CulmTints.Length]), textureName);
                            AddMeshPart(go, $"Culm{i}", offset, parentScale,
                                ResourceVisualLibrary.BambooCulmMeters(culmVariant), material, culmYaw);
                        }

                        // 잎 다발: 실루엣 위쪽을 깨 주는 역할이라 성기게 붙인다(대나무 잎은 작고 성기다).
                        // 살아 있는 잎이므로 팔레트의 Frond Green을 쓴다 - 줄기(B48 이후 Bamboo Culm
                        // 황록색)와 색이 갈라져야 "줄기 + 잎"으로 읽힌다.
                        // [B29] 1~2 → 2~3. 4~5m 줄기 꼭대기에 잎다발이 하나뿐이면 위쪽이 텅 빈 장대가 된다.
                        // 파츠 예산은 루트 1 + 곁줄기 2~4 + 잎 2~3 = 최대 8로 ClumpVisualPrimitives(8)와 정확히 같다.
                        // 붙는 높이는 parentScale.y에 비례하는 식이라(아래) 루트가 커진 만큼 저절로 따라 올라간다.
                        int sprigCount = rng.NextInt(2, 4);
                        Material leafMaterial = ResourceVisualLibrary.GetMaterial(StructureVisualBuilder.FrondGreen, "frond");
                        for (int i = 0; i < sprigCount; i++)
                        {
                            // ★ [B48] 난수 소비 불변 ★ 위 곁줄기 루프와 같은 이유로 두 draw를 먼저 뽑는다.
                            float sprigT = rng.NextFloat(0.62f, 0.94f);
                            float sprigYaw = rng.NextFloat(0f, 360f);
                            if (useBambooModel)
                                continue;

                            float height = groundY + sprigT * parentScale.y * 2f;
                            AddMeshPart(go, $"Sprig{i}", new Vector3(0f, height, 0f), parentScale,
                                ResourceVisualLibrary.FrondMeters(3 + (i % 2)), leafMaterial, sprigYaw);
                        }

                        if (useBambooModel)
                        {
                            // 루트 실린더(절차 줄기)는 **그리지 않는다.** 메시와 콜라이더는 그대로 둔 채
                            // 렌더러만 끈다 - ResourceNode.RootTopLocalY와 GetHalfHeight가 루트 메시의
                            // 경계상자에 걸려 있어서, 메시를 지우면 접지·파츠 높이 계산이 조용히 어긋난다.
                            var rootRenderer = go.GetComponent<MeshRenderer>();
                            if (rootRenderer != null)
                                rootRenderer.enabled = false;

                            float fit = clumpHeight / Mathf.Max(0.01f, bambooModelHeight);
                            // 방위각은 0으로 둔다. 루트 오브젝트가 이미 무작위 Y 회전을 갖고 있어(SpawnSingleNode)
                            // 포기 전체가 그대로 돌아간다 - 여기서 rng를 더 뽑을 이유가 없다.
                            var culmPart = AddMeshPart(go, "BambooModelCulms", new Vector3(0f, groundY, 0f),
                                parentScale, bambooCulms,
                                ResourceVisualLibrary.GetMaterial(color, textureName), 0f, fit);

                            if (bambooLeaves != null)
                            {
                                AddMeshPart(go, "BambooModelLeaves", new Vector3(0f, groundY, 0f),
                                    parentScale, bambooLeaves, leafMaterial, 0f, fit);
                            }
                            else if (bambooCulms.subMeshCount >= 2 && culmPart != null)
                            {
                                // 임포터가 `o` 2개를 한 메시의 서브메시로 합쳐 온 경우 - 렌더러 하나에
                                // 머티리얼 두 장을 주면 줄기/잎이 각각 칠해진다(메시를 새로 만들지 않는다).
                                var culmRenderer = culmPart.GetComponent<MeshRenderer>();
                                if (culmRenderer != null)
                                {
                                    culmRenderer.sharedMaterials = new[]
                                    {
                                        ResourceVisualLibrary.GetMaterial(color, textureName),
                                        leafMaterial
                                    };
                                }
                            }
                        }
                        break;
                    }

                case "돌조각":
                    // [B28] 곁돌 개수를 0~3 → 2~3으로 올려 "무더기"의 최소 밀도를 보장한다(0개가 나오면
                    // 눌린 구 하나뿐이라 무더기로 읽히지 않았다). 대신 곁돌도 루트와 같은 각진 파편 메시를
                    // 쓰고 살짝 파묻히게 놓아, 개수가 늘어도 실루엣이 지저분해지지 않는다.
                    {
                        int rockCount = rng.NextInt(2, 4);
                        Vector3[] offsets = { new Vector3(0.60f, -0.25f, 0.22f), new Vector3(-0.52f, -0.30f, -0.28f), new Vector3(0.15f, -0.22f, -0.62f) };
                        for (int i = 0; i < rockCount && i < offsets.Length; i++)
                        {
                            float size = rng.NextFloat(0.18f, 0.30f);
                            AddPart(go, $"Rock{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(size, size * 0.72f, size),
                                parentScale, Quaternion.identity,
                                ResourceVisualLibrary.Shade(color, RockTints[i % RockTints.Length]), textureName,
                                ResourceVisualLibrary.RockChunkUnit(rng.NextInt(0, 4)));
                        }
                        break;
                    }

                case "코코넛":
                    // 퀄리티 개선: 열매가 1개짜리 노드도, 2개까지 뭉친 노드도 나오게 해서 다발 크기가 다양해 보이게 했다.
                    // [B28 파츠 예산] 여분 열매 0~2 → 0~1. 코코넛은 이미 구 하나로 충분히 읽히는 유일한
                    // 자원이라(다른 자원과 실루엣이 겹치지 않는다) 여기서 아낀 예산을 대나무 포기에 넘긴다.
                    {
                        int extraCount = rng.NextInt(0, 2);
                        Vector3[] offsets = { new Vector3(0.4f, -0.05f, 0.1f), new Vector3(-0.35f, -0.08f, 0.25f) };
                        for (int i = 0; i < extraCount && i < offsets.Length; i++)
                            AddPart(go, $"Coconut{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(0.38f, 0.38f, 0.38f), parentScale, Quaternion.identity, color, textureName);
                        break;
                    }

                case "천조각":
                    // 퀄리티 개선: 접힌 주름이 있을 때도(70% 확률) 없을 때도 있게 해 밋밋한 조각과 구겨진 조각이 섞여 보이게 했다.
                    if (rng.NextValue01() < 0.7f)
                        AddPart(go, "Fold", PrimitiveType.Cube, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.4f, 0.05f, 0.3f), parentScale, Quaternion.Euler(0f, rng.NextFloat(0f, 36f), 3f), ResourceVisualLibrary.Shade(color, 0.92f), textureName);
                    break;

                case "야자잎":
                    {
                        // [B28] 예전 잎은 `Cube (0.05, 0.02, 0.45~0.62)` - 두께 2cm짜리 **납작한 판**이었고,
                        // 게다가 Euler(-20, angle, 0)로 돌린 자식이라 부모 스케일(0.05, 0.08, 0.05)의
                        // y:z = 1.6:1 비대칭 때문에 살짝 찌그러져 있었다.
                        // 지금은 잎 한 장이 "잎맥(중앙 리브) + 좌우로 갈라진 잎깃 5~7쌍"으로 된 메시 한 장이다.
                        // 톱니 실루엣이 생겨 멀리서도 야자잎으로 읽히고, 잎깃은 양면이라 아래에서 봐도 사라지지
                        // 않는다(단면 메시가 컬링되어 없어지는 사고 방지). 기울기·처짐은 전부 메시에 구워
                        // 넣었으므로 자식 회전은 부채꼴 각도(Y)뿐이다 - 전단이 원리적으로 생기지 않는다.
                        const int leafCount = 3;
                        float spread = rng.NextFloat(96f, 148f); // 부채꼴 전체 펼침 각도
                        float baseYaw = rng.NextFloat(0f, 360f);
                        float stemTop = parentScale.y * 0.9f; // 줄기(실린더) 꼭대기 = 로컬 +1
                        for (int i = 0; i < leafCount; i++)
                        {
                            float yaw = baseYaw - spread * 0.5f + spread * i / (leafCount - 1) + rng.NextFloat(-7f, 7f);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, FrondTints[i % FrondTints.Length]), textureName);
                            AddMeshPart(go, $"Frond{i}", new Vector3(0f, stemTop, 0f), parentScale,
                                ResourceVisualLibrary.FrondMeters(rng.NextInt(0, 3)), material, yaw);
                        }
                        break;
                    }

                case "금속조각":
                    // 퀄리티 개선: 구부러진 정도(각도)를 무작위로 바꿔 찌그러진 모양이 조금씩 다르게 보이게 했다.
                    AddPart(go, "Bend", PrimitiveType.Cube, new Vector3(-0.05f, 0.4f, 0.05f), new Vector3(0.32f, 0.06f, 0.22f), parentScale, Quaternion.Euler(0f, rng.NextFloat(-50f, -20f), 8f), ResourceVisualLibrary.Shade(color, 0.85f), textureName);
                    break;

                case "부력통":
                    // [B28 버그 수정] worldSize는 로컬 배수가 아니라 **미터**다. 1.08을 넘기고 있어서
                    // 지름 0.42m 드럼통에 지름 **1.08m짜리 원반**이 꽂혀 있었다(대나무 마디와 같은 실수).
                    // 드럼통보다 살짝 큰 테(0.47m)로 고친다 - 파츠 개수는 그대로 1개다.
                    AddPart(go, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(0.47f, 0.05f, 0.47f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.8f), textureName);
                    break;

                case "비상식량":
                    AddPart(go, "Label", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.51f), new Vector3(0.26f, 0.1f, 0.02f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(Color.white, 0.9f), "noise");
                    break;

                case "연료":
                    AddPart(go, "Spout", PrimitiveType.Cylinder, new Vector3(0.08f, 0.62f, 0f), new Vector3(0.14f, 0.12f, 0.14f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.85f), textureName);
                    break;

                case "생수":
                    // [B28] 병목 + 뚜껑. 원기둥 하나만으로는 부력통/엔진부품과 실루엣이 겹치는데,
                    // 위로 갈수록 가늘어지는 2단 실루엣은 이 자원에만 있다.
                    // 위치는 부모 로컬 단위라(아래 AddPart 주석) 세로 지터가 붙어도 몸통을 정확히 따라간다.
                    AddPart(go, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.16f, 0f), new Vector3(0.036f, 0.022f, 0.036f),
                        parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.88f), textureName);
                    AddPart(go, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 1.42f, 0f), new Vector3(0.05f, 0.014f, 0.05f),
                        parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.6f), textureName);
                    break;

                case StoneOreItemName:
                case MarbleOreItemName:
                    // [4티어 원석 - B48 패턴] 실물 바위 모델(rock_a~c 재사용)을 원석 크기로 줄여 본체로 쓴다.
                    // 곁돌 1~2개는 돌조각 무더기와 같은 각진 파편 메시를 재사용한다(파츠 예산: 루트 1 +
                    // 곁돌 최대 2 + 모델 1 = 4, 돌조각의 최대치와 같다). 색은 GetWorldSurfaceColor가
                    // 종별 틴트(석재 밝은 회색 / 대리석 거의 흰색)로 이미 갈라 놓았다 - 여기서는 그대로 쓴다.
                    {
                        int chipCount = rng.NextInt(1, 3);
                        Vector3[] chipOffsets = { new Vector3(0.55f, -0.30f, 0.25f), new Vector3(-0.48f, -0.34f, -0.30f) };
                        for (int i = 0; i < chipCount && i < chipOffsets.Length; i++)
                        {
                            float size = rng.NextFloat(0.14f, 0.24f);
                            AddPart(go, $"OreChip{i}", PrimitiveType.Sphere, chipOffsets[i], new Vector3(size, size * 0.7f, size),
                                parentScale, Quaternion.identity,
                                ResourceVisualLibrary.Shade(color, RockTints[i % RockTints.Length]), textureName,
                                ResourceVisualLibrary.RockChunkUnit(rng.NextInt(0, 4)));
                        }

                        // ★ 난수 소비 불변(대나무 B48의 방법) ★ 변종 draw는 모델 로드 성공 여부와 무관하게
                        // 먼저 뽑는다 - 모델 임포트 전/후에 같은 worldSeed의 배치가 갈라지지 않게.
                        int modelVariant = rng.NextInt(0, ResourceVisualLibrary.OreRockVariantCount);
                        Mesh oreMesh;
                        Vector3 oreModelSize;
                        if (ResourceVisualLibrary.TryGetOreRockModel(modelVariant, out oreMesh, out oreModelSize))
                        {
                            // 루트 파편은 그리지 않는다. 메시·콜라이더는 그대로 둔 채 렌더러만 끈다 -
                            // GetHalfHeight/RootTopLocalY가 루트 메시 경계상자에 걸려 있다(대나무 주석과 동일).
                            var rootRenderer = go.GetComponent<MeshRenderer>();
                            if (rootRenderer != null)
                                rootRenderer.enabled = false;

                            // 목표 폭 = 루트 구의 가로 지름(m) = 채집 콜라이더 폭. 모델은 미터 규격·밑면 y=0이라
                            // 균등 fit 배율 하나만 곱하고, 밑면을 루트 바닥(-반높이)에 맞춘다.
                            float fit = parentScale.x / Mathf.Max(0.01f, Mathf.Max(oreModelSize.x, oreModelSize.z));
                            AddMeshPart(go, "OreRockModel", new Vector3(0f, -parentScale.y * 0.5f, 0f), parentScale,
                                oreMesh, ResourceVisualLibrary.GetMaterial(color, textureName), 0f, fit);
                        }
                        break;
                    }

                case "엔진부품":
                    // 퀄리티 개선: 볼트 개수를 무작위로 바꿔 부품마다 조립 상태가 달라 보이게 했다.
                    // [tech-artist-B 요청 - 파츠 예산] 3~6개 → 2~3개 (야자잎 주석의 근거와 동일).
                    // 볼트는 원주 배치라 개수가 줄어도 360/boltCount 간격이 자동으로 벌어져 형태가 깨지지 않는다.
                    {
                        int boltCount = rng.NextInt(2, 4);
                        for (int i = 0; i < boltCount; i++)
                        {
                            float rad = i * (360f / boltCount) * Mathf.Deg2Rad;
                            Vector3 localPos = new Vector3(Mathf.Cos(rad) * 0.24f, 0.05f, Mathf.Sin(rad) * 0.24f);
                            AddPart(go, $"Bolt{i}", PrimitiveType.Cube, localPos, new Vector3(0.06f, 0.06f, 0.06f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.8f), textureName);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 순수 시각용 보조 파츠 하나를 만들어 parent의 자식으로 붙인다. worldSize를 parentScale로 나눠
        /// 자식의 localScale로 지정하면, 부모가 비균일 스케일(예: 얇고 넓은 큐브)이어도 파츠가 찌그러지지
        /// 않고 의도한 크기로 보인다(CreatureSpawner.AddCompensated와 동일한 보정 방식).
        /// 자동으로 붙는 콜라이더는 즉시 제거해 부모의 상호작용용 콜라이더와 간섭하지 않게 한다.
        ///
        /// **단위 주의(사고 2건의 원인):** localPosition은 **부모 로컬 단위**(실린더 y=1이 곧 꼭대기)인데
        /// worldSize는 **미터**다. 이 둘을 헷갈려 대나무 마디(1.15)와 부력통 테(1.08)가 각각 지름 1m가
        /// 넘는 원반으로 나와 있었다. 새 파츠를 넣을 때는 worldSize에 반드시 실제 미터 값을 적어라.
        ///
        /// meshOverride를 주면 프리미티브 메시 대신 그 메시를 쓴다. 메시가 프리미티브의 로컬 규격
        /// (큐브/구 |v|<=0.5, 실린더 |y|<=1)을 지키면 worldSize의 의미가 그대로 유지된다.
        /// </summary>
        private void AddPart(GameObject parent, string name, PrimitiveType primitive, Vector3 localPosition,
            Vector3 worldSize, Vector3 parentScale, Quaternion localRotation, Color color, string textureName,
            Mesh meshOverride = null)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));

            var collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            if (meshOverride != null)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter != null)
                    filter.sharedMesh = meshOverride;
            }

            // [B28] renderer.material(복제)에서 공유 머티리얼로. SpawnSingleNode의 루트 주석과 같은 이유다.
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = ResourceVisualLibrary.GetMaterial(color, textureName);
        }

        /// <summary>
        /// **미터 단위로 만들어 둔 절차 메시**를 파츠 하나로 붙인다(대나무 줄기·잔가지·야자잎 전용).
        ///
        /// 왜 별도 경로가 필요한가 - 이 프로젝트에서 반복된 "기울인 자식이 찌그러진다" 사고의 근본 원인:
        /// 부모 스케일이 S = diag(a, b, a)이고 자식이 회전 R을 가지면 합성 행렬에 S·R이 들어가는데,
        /// a != b면 X/Z축 회전에서 전단(shear)이 생긴다(대나무 x:y = 1:7.5, 나뭇가지 1:3.5).
        /// 여기서는 자식 스케일을 **정확히 S⁻¹**로 두어 합성 스케일을 1로 만든다. 그러면
        ///   v_world = S·t + R_y·v_mesh
        /// 가 되어 (1) 메시 좌표가 곧 미터이고 (2) Y 회전은 a == a 덕분에 스케일과 교환되어 **정확한 회전**이
        /// 된다. 기울기·굽음은 전부 메시에 구워 넣고 여기서는 방위각(Y)만 돌리므로 전단이 원리적으로 없다.
        /// (부모의 가로 지터를 x/z 공통으로 바꾼 것이 이 성질의 전제다 - SpawnSingleNode 주석 참고.)
        ///
        /// CreatePrimitive를 쓰지 않아 콜라이더가 **처음부터 생기지 않는다**(만들었다 지우는 낭비도 없다).
        /// 시각 파츠에는 콜라이더가 없어야 한다는 규칙을 구조적으로 보장하는 경로다.
        /// </summary>
        private GameObject AddMeshPart(GameObject parent, string name, Vector3 worldOffset, Vector3 parentScale,
            Mesh mesh, Material material, float yawDegrees, float uniformScale = 1f)
        {
            if (mesh == null)
                return null;

            float sx = Mathf.Max(0.0001f, parentScale.x);
            float sy = Mathf.Max(0.0001f, parentScale.y);
            float sz = Mathf.Max(0.0001f, parentScale.z);

            var part = new GameObject(name);
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = new Vector3(worldOffset.x / sx, worldOffset.y / sy, worldOffset.z / sz);
            part.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            // [B48] uniformScale은 **균등** 배율이다(기본 1 = 예전과 완전히 동일). S⁻¹에 균등 배율을
            // 곱한 것이므로 위 주석의 성질이 그대로 유지된다: S·(R_y·k·S⁻¹)v = k·R_y·v - 배율이
            // 균등하고 회전이 Y뿐이라 전단이 원리적으로 생기지 않는다. 미터 규격 OBJ를 목표 치수에
            // 맞추는 fit 배율(= 목표 높이 / 모델 실측 높이)이 여기로 들어온다.
            part.transform.localScale = new Vector3(uniformScale / sx, uniformScale / sy, uniformScale / sz);

            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = part.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;

            return part;
        }

        /// <summary>
        /// 아이템이 어떤 표면 질감 텍스처(Resources/Textures/*)를 씌울지 결정한다.
        /// B3-9: game-designer의 Spec_B2_11_MaterialFamilyField.md 권장 매핑에 따라, ItemData.materialFamily
        /// 필드가 설정돼 있으면(None이 아니면) 그 값을 우선 참조한다. 필드가 아직 None인 경우(43개의
        /// 기존 .asset이 이 필드가 추가되기 전부터 있었으므로 game-designer가 값을 채우기 전까지는 전부
        /// None이다) 예전과 동일한 itemName 문자열 추론 로직(GetSurfaceTextureNameFromName)으로 폴백해,
        /// .asset 값이 채워지기 전까지는 동작이 전혀 바뀌지 않는다.
        /// </summary>
        private string GetSurfaceTextureName(ItemData item)
        {
            if (item == null)
                return "noise";

            // [B28] 종(種) 전용 텍스처가 먼저다. materialFamily는 "나무 계열"까지만 구분할 수 있는데,
            // 대나무(마디 있는 매끈한 세로결)와 나뭇가지(거친 껍질)는 같은 Wood 계열이면서 표면이 완전히
            // 다르다 - 계열 필드로는 표현할 수 없는 차이라 이름으로 먼저 가른다. 여기서 잡히지 않는
            // 자원은 예전 경로(계열 → 이름 폴백)로 그대로 내려간다.
            string speciesTexture = GetSpeciesTextureName(item.itemName);
            if (speciesTexture != null)
                return speciesTexture;

            switch (item.materialFamily)
            {
                case MaterialFamily.Wood: return "wood";
                case MaterialFamily.Stone: return "stone";
                case MaterialFamily.Metal: return "metal";
                case MaterialFamily.Fiber: return "leaf";
                case MaterialFamily.Fruit: return "noise";
                case MaterialFamily.Supply: return "noise";
                case MaterialFamily.None:
                default:
                    return GetSurfaceTextureNameFromName(item.itemName);
            }
        }

        /// <summary>
        /// [B28] 자원 종류별 전용 타일 텍스처 이름(해당 없으면 null).
        ///
        /// 이름은 Resources/Textures/ 아래 파일명(확장자 없음)이며 StructureVisualBuilder.CreateColorMaterial이
        /// Resources.Load로 집어간다. **파일이 아직 없어도 안전하다** - 로드가 null이면 CreateColorMaterial이
        /// 텍스처를 씌우지 않고 단색으로 넘어가므로(StructureVisualBuilder.cs:152 가드) 예외도, 분홍색
        /// 머티리얼도 나오지 않는다. 즉 이 표는 텍스처가 들어오는 순간 저절로 켜진다.
        /// </summary>
        /// <summary>
        /// [B48] **월드에 서 있는 실물**의 표면색이 아이템 카테고리 색과 달라야 하는 종(種)만 여기서 덮는다.
        /// 해당 없으면 넘겨받은 카테고리 색을 그대로 돌려준다.
        ///
        /// 왜 UIBuilder를 고치지 않는가: UIBuilder.GetItemCategoryColor는 인벤토리/제작 UI의 카테고리
        /// 색까지 겸하고 있고(재질 계열 = 목재 = 갈색), 그 규칙은 UI에서 여전히 맞다. 반면 월드에 자라
        /// 있는 대나무는 **살아 있는 식물**이라 마른 목재의 갈색(Driftwood #8C6640)이면 마른 나뭇가지로
        /// 보인다(디렉터 지적). 그래서 UI 규칙은 그대로 두고 월드 표면만 황록색으로 가른다.
        /// 잎(Sprig/모델 잎)은 예전 그대로 Frond Green이다 - 줄기와 잎이 색으로 갈려야 한다.
        ///
        /// 이 값은 루트 줄기·곁줄기·모델 줄기 · EffectBuilder.PlayHarvestPop의 채집 입자 색까지
        /// 한 곳에서 따라간다(전부 이 색을 읽는다).
        /// </summary>
        /// <summary>[4티어] 석재 원석의 월드 표면색 - 밝은 회색(돌조각의 카테고리 회갈색보다 확실히 밝다).
        /// 팔레트 색상(hue)을 새로 만들지 않는 무채색 계열이라 ArtDirection 소유권과 충돌하지 않는다.</summary>
        private static readonly Color StoneOreSurfaceColor = new Color(0.72f, 0.73f, 0.74f);

        /// <summary>[4티어] 대리석 원석의 월드 표면색 - 거의 흰색(0.88+ 규격). 매끈함은 "noise" 텍스처가 맡는다.</summary>
        private static readonly Color MarbleOreSurfaceColor = new Color(0.91f, 0.90f, 0.88f);

        private Color GetWorldSurfaceColor(string itemName, Color categoryColor)
        {
            if (itemName == "대나무")
                return StructureVisualBuilder.BambooCulm;

            // [4티어] 원석 2종은 같은 바위 형태를 색으로 가른다(석재 = 밝은 회색, 대리석 = 거의 흰색).
            // EffectBuilder.PlayHarvestPop의 채집 입자 색도 이 색을 그대로 따라간다.
            if (itemName == StoneOreItemName)
                return StoneOreSurfaceColor;
            if (itemName == MarbleOreItemName)
                return MarbleOreSurfaceColor;

            return categoryColor;
        }

        private string GetSpeciesTextureName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            switch (itemName)
            {
                case "대나무": return "bamboo";   // 마디 사이의 매끈한 세로결
                case "나뭇가지": return "bark";     // 거친 나무 껍질
                case "야자잎": return "frond";    // 잎맥
                case "돌조각": return "rock";     // 거친 암석
                case "부싯돌": return "rock";
                case StoneOreItemName: return "rock";   // [4티어] 석재 원석 = 거친 암석 그대로(밝은 틴트로만 구분)
                case MarbleOreItemName: return "noise"; // [4티어] 대리석 원석 = 매끈한 표면(거친 rock 결을 쓰지 않는다)
                case "천조각": return "thatch";   // 엮인 섬유
                case "코코넛": return "thatch";   // 코코넛 겉껍질의 섬유질
                case "비상식량": return "driftwood"; // 표류한 나무 배급 상자
                default: return null;
            }
        }

        /// <summary>
        /// (B3-9 이전 로직, materialFamily가 None일 때의 폴백) 아이템 이름을 보고 어떤 표면 질감
        /// 텍스처(Resources/Textures/*)를 씌울지 추론한다. 처음에는 wood/stone/noise 3종뿐이었는데,
        /// 금속과 잎/식물류가 돌·나무와 뭉뚱그려져 있어 leaf(잎맥 얼룩)와 metal(브러시드 메탈 스크래치)을
        /// 추가로 분리했다.
        /// </summary>
        private string GetSurfaceTextureNameFromName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return "noise";

            if (itemName.Contains("금속조각"))
                return "metal";

            if (itemName.Contains("야자잎"))
                return "leaf";

            if (itemName.Contains("나뭇가지") || itemName.Contains("대나무"))
                return "wood";

            if (itemName.Contains("돌조각") || itemName.Contains("부싯돌"))
                return "stone";

            return "noise";
        }
    }
}
