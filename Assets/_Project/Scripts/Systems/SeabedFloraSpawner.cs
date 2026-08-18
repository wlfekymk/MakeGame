using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬별 해저 생태(산호밭 / 해초 숲 / 수중 바위) + 해저 지형지물(seaform) 분포기.
    ///
    /// SeabedGenerator가 해저 스커트(섬 가장자리 → 수심 18m 환형 모래 바닥)를 깔고 레코드를 등록한
    /// **직후, 같은 동기 흐름에서** 호출된다(SeabedGenerator.Build 끝부분 - 스커트가 먼저 있어야
    /// TrySampleSeabed 접지 샘플이 유효하다).
    ///
    /// ── [세이브 무관 - 순수 배경] ──────────────────────────────────────────────────
    /// 여기서 만드는 것은 전부 채집 불가 장식이다. ResourceNode/HazardSource 등 세이브 대상 컴포넌트를
    /// 하나도 붙이지 않으므로 SaveLoadController의 FindObjectsByType 순회(ResourceNode/Campfire/…)에
    /// 걸리지 않고, 저장 파일에 한 바이트도 들어가지 않는다. 불러오기(RegenerateWorld) 후에는 같은
    /// worldSeed → 같은 시드 → 같은 배치로 그냥 다시 생성될 뿐이다.
    /// 예외는 침몰 화물(SpawnSunkenCargo)·진주조개(SpawnPearlClams)·채집 해초(PlaceKelp의 해시
    /// 당첨 서브셋)의 AirlinerSalvagePoint인데, 그 컴포넌트도 세이브 비대상이라(수거 여부 미저장 -
    /// AirlinerSalvagePoint [한계] 주석과 같은 한계) 저장 파일에는 여전히 한 바이트도 들어가지
    /// 않고, 로드할 때마다 수거 지점이 리셋될 뿐이다.
    ///
    /// ── rng 격리 (최중요) ─────────────────────────────────────────────────────────
    /// 섬마다 `new System.Random(unchecked(worldSeed * 397 ^ islandId ^ 0x5EABED))` 로 만든
    /// **전용 독립 스트림**만 소비한다. 기존 스트림(섬 레이아웃 CreateForSalt, 초목
    /// VegetationSeedSalt(3000000+id), 자원 CreateForIsland, 위험요소/사냥감/도면 salt 대역)은
    /// 어느 것도 만들지도, 이어 뽑지도 않는다 - 0x5EABED xor 시드는 위 어떤 salt 조합과도 겹치지
    /// 않는 별도 시드 공간이고, 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 한 칸도 밀리지 않는다.
    /// UnityEngine.Random은 일절 쓰지 않는다(전역 상태 오염 금지 - SeededRandomExtensions 상단 주석).
    /// 진주조개는 **또 다른 완전 분리 스트림**(0xC1A0 솔트 - SpawnPearlClams 주석)을 쓴다. 검증이
    /// 끝난 0x5EABED 스트림의 꼬리에 draw를 덧붙이는 것조차 금지다(기존 월드 재현성 1비트 불변).
    /// 채집 해초 서브셋 판정은 아예 rng가 아니라 순수 위치 해시(draw 소비 0 - PositionHash)다.
    /// 해저 지형지물(seaform)도 **제3의 완전 분리 스트림**(0x5EAF 솔트 - SpawnSeaForms 주석)이라,
    /// 지형지물이 몇 개 서든 기존 산호/해초/searock/화물/조개 추첨은 1비트도 밀리지 않는다.
    ///
    /// ── 생명주기 편승 ────────────────────────────────────────────────────────────
    /// 배치물 루트는 반드시 **그 섬의 루트 오브젝트("Island_{id}_{size}") 자식**이다. 그래서
    /// RegenerateWorld가 섬을 SetActive(false)+Destroy 하면 함께 사라지고(스커트와 동일한 편승),
    /// 별도의 정리/추적 코드가 필요 없다. 정적 캐시는 공유 메시/머티리얼뿐이라 월드 재생성과 무관하다.
    ///
    /// ── 기존 배치가 영향받지 않는 근거 (SeabedGenerator와 동일 계열) ───────────────
    ///  (1) 모든 오브젝트 이름이 "SeabedFlora_"/"Coral_"/"Kelp_"/"SeaRock_" - "Island_"로 시작하지
    ///      않으므로 TerrainSampler.SnapToGround류 지형 판정에서 구조적으로 제외된다.
    ///  (2) 전부 r ≥ R(섬 메시 밖) 해수면 아래라, 뭍 배치(산포 반경 0.8R 이내)의 레이가 위를
    ///      지나갈 일 자체가 없다.
    ///  (3) 콜라이더는 "큰 바위"에만 BoxCollider(대략치)를 달아 잠수 시 실제로 부딪히는 랜드마크로
    ///      만들고, 산호/해초/작은 바위는 콜라이더 없음(수중 이동을 막지 않는다). 해저 지형지물
    ///      (seaform)은 같은 규약을 따르되 관통형 a(아치)/d(협곡)와 오버행 e만 비볼록
    ///      MeshCollider다 - 볼록 헐은 통로·언더컷을 메워 버린다(PlaceSeaForm 주석).
    ///
    /// ── 시각 로더 ────────────────────────────────────────────────────────────────
    /// IslandResourceSpawner.MeshLibrary(ResourceVisualLibrary.TryLoadTwoPartModel)의 검증된 패턴
    /// 그대로다: Resources.Load&lt;GameObject&gt; + GetComponentsInChildren&lt;MeshFilter&gt;로 공유 메시만
    /// 꺼내 쓰고(Instantiate 금지 - 임포터가 붙였을 수 있는 콜라이더가 씬에 못 들어온다), 프레임당
    /// 1회만 프로브하며, 실패를 영구 래치하지 않는다. 산호 OBJ는 `o` 오브젝트 2개(body/tip)라
    /// Unity 6.5 임포터가 서브메시 2짜리 한 메시로 합쳐 올 수 있는데(AirlinerWreck 실사고 0.2.13),
    /// 그때는 렌더러 하나에 sharedMaterials = [body색, tip색] 배열을 준다(AirlinerWreck/
    /// IslandResourceSpawner.Visuals의 subMeshCount>=2 분기와 같은 규칙). 머티리얼은 전부
    /// ResourceVisualLibrary.GetMaterial 공유 캐시(색+텍스처당 1장, enableInstancing)에서 받는다.
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 섬당 렌더러 약 20~45개(산호는 서브메시 2라 드로우콜 기준 약 30~60) + 해저 지형지물 최대 5개
    /// (소형 0~1 / 중형 1~2 / 대형 2~4 / 특대 3~5, 각 1렌더러). 모델 63종 × 머티리얼 19장(산호
    /// 12색×2단 중 실제 사용 조합 + 해초 4 + 바위 3)은 전부 월드 공유 정적 캐시다. 지형지물은
    /// 바위 팔레트의 첫 색을 그대로 쓰므로 **머티리얼이 한 장도 늘지 않는다**.
    /// 수중이라 그림자는 캐스팅/수신 모두 끈다(스커트와 같은 이유 - 보이지 않는 그림자에 드로우를
    /// 쓰지 않는다).
    /// </summary>
    public static class SeabedFloraSpawner
    {
        /// <summary>섬별 배치 루트 이름 접두사. "Island_"로 시작하지 않는 것이 지형 판정 안전의 전제다.</summary>
        private const string FloraRootPrefix = "SeabedFlora_";

        /// <summary>rng 격리용 시드 소금. 기존 어떤 salt 대역(3000000+ 등)과도 겹치지 않는 xor 상수.</summary>
        private const int SeedSalt = 0x5EABED;

        /// <summary>진주조개 전용 **제2 격리 스트림**의 시드 소금. 0x5EABED 스트림과도 다른 값이라
        /// 조개가 몇 개 뽑히든 기존 산호/해초/바위/화물 추첨은 1비트도 밀리지 않는다(꼬리 draw 추가
        /// 금지 - 검증 완료된 기존 스트림의 draw 수·순서 동결이 원칙이다).</summary>
        private const int ClamSeedSalt = 0xC1A0;

        /// <summary>해저 지형지물(seaform_a~h) 전용 **제3 격리 스트림**의 시드 소금. 기존
        /// 0x5EABED(생태)·0xC1A0(조개)·0xCA7E(동굴) 및 3000000+ salt 대역과 겹치지 않는 값이라,
        /// 여기서 몇 번을 뽑든 산호/해초/searock/화물/조개의 추첨 순서·결과는 1비트도 밀리지 않는다.
        /// (섬 id는 50 미만이므로 `worldSeed*397 ^ islandId ^ salt` 끼리도 충돌할 수 없다 -
        /// 두 스트림이 같은 시드가 되려면 islandId 차이가 salt 차이(≥0x5EF552)와 같아야 한다.)</summary>
        private const int SeaFormSeedSalt = 0x5EAF;

        // ── 모델 카탈로그 (Resources/Models, 확장자 없음) ──────────────────────────

        /// <summary>산호 20종. 계열 순서: branch 6 → table 4 → brain 3 → fan 4 → tube 3.</summary>
        private static readonly string[] CoralModelNames =
        {
            "coral_branch_a", "coral_branch_b", "coral_branch_c", "coral_branch_d", "coral_branch_e", "coral_branch_f",
            "coral_table_a", "coral_table_b", "coral_table_c", "coral_table_d",
            "coral_brain_a", "coral_brain_b", "coral_brain_c",
            "coral_fan_a", "coral_fan_b", "coral_fan_c", "coral_fan_d",
            "coral_tube_a", "coral_tube_b", "coral_tube_c",
        };

        /// <summary>산호 계열(branch/table/brain/fan/tube)의 CoralModelNames 시작 인덱스와 개수.
        /// 같은 패치엔 같은 계열 60% 편향(실제 산호초 군락감)을 주는 데 쓴다.</summary>
        private static readonly int[] CoralFamilyStart = { 0, 6, 10, 13, 17 };
        private static readonly int[] CoralFamilyCount = { 6, 4, 3, 4, 3 };

        /// <summary>해초 10종(kelp_a~j, `o` 1개 = 양면 blade 메시).</summary>
        private static readonly string[] KelpModelNames =
        {
            "kelp_a", "kelp_b", "kelp_c", "kelp_d", "kelp_e",
            "kelp_f", "kelp_g", "kelp_h", "kelp_i", "kelp_j",
        };

        /// <summary>각 해초 모델의 실측 전체 높이(m, OBJ 정점 maxY - 밑면 y=0). 리본형/방석형 판정 소스다:
        /// 높이 1.4m 이상(a~d,h~j)은 물결에 뻗는 리본형 → 깊은 쪽, 0.7m 미만(e~g)은 방석형 → 얕은 쪽.</summary>
        private static readonly float[] KelpModelHeights =
        {
            2.47f, 3.67f, 1.62f, 4.15f, 0.37f,
            0.26f, 0.64f, 2.10f, 2.66f, 1.45f,
        };

        /// <summary>수중 바위 20종(searock_a~t, `o` 1개). a~e 표석, f~j 판형, k~o 첨탑(1.2~3.1m), p~t 군집.</summary>
        private static readonly string[] RockModelNames =
        {
            "searock_a", "searock_b", "searock_c", "searock_d", "searock_e",
            "searock_f", "searock_g", "searock_h", "searock_i", "searock_j",
            "searock_k", "searock_l", "searock_m", "searock_n", "searock_o",
            "searock_p", "searock_q", "searock_r", "searock_s", "searock_t",
        };

        /// <summary>각 바위 모델의 실측 크기(m, W×H×D · 밑면 y=0 · XZ 대략 중심). OBJ 정점 실측값 -
        /// BoxCollider 대략치와 "큰 것(&gt;1.5m)" 판정에 쓴다(OreRockModelSizes와 같은 사본 정책).</summary>
        private static readonly Vector3[] RockModelSizes =
        {
            new Vector3(0.87f, 0.72f, 0.89f), new Vector3(1.47f, 1.06f, 1.28f), new Vector3(0.56f, 0.45f, 0.50f),
            new Vector3(1.79f, 1.37f, 1.93f), new Vector3(1.10f, 0.90f, 1.15f),
            new Vector3(1.21f, 0.40f, 1.34f), new Vector3(2.15f, 0.54f, 1.99f), new Vector3(0.88f, 0.33f, 0.91f),
            new Vector3(3.06f, 0.64f, 2.09f), new Vector3(1.59f, 0.46f, 1.49f),
            new Vector3(0.60f, 1.75f, 0.84f), new Vector3(1.10f, 2.47f, 0.91f), new Vector3(0.55f, 1.22f, 0.51f),
            new Vector3(1.12f, 3.07f, 1.12f), new Vector3(0.75f, 2.01f, 0.73f),
            new Vector3(0.89f, 0.41f, 0.87f), new Vector3(1.52f, 0.45f, 1.13f), new Vector3(0.56f, 0.29f, 0.65f),
            new Vector3(2.06f, 0.73f, 1.58f), new Vector3(1.16f, 0.46f, 1.23f),
        };

        /// <summary>첨탑 바위(searock_k~o)의 인덱스 구간. 잠수 랜드마크라 깊이 6m 이상에만 배치한다.</summary>
        private const int SpireStart = 10;
        private const int SpireCount = 5;

        // ── 해저 지형지물 (seaform_a~h - searock 소품과 역할이 다른 "통과·단차·오버행" 계열) ──
        // searock(60~300tri 소품)은 바닥에 흩는 장식이지만 seaform은 잠수 동선을 만드는 지형이다.
        // 그래서 배치 경로 자체가 분리돼 있다: 전용 rng 스트림(SeaFormSeedSalt) + 수심대 분리 +
        // 반경 합 기반 최소 간격(occupancy) + 모델 높이 기준 수면 돌출 방지.

        /// <summary>해저 지형지물 8종(seaform_a~h, `o` 1개 + mtllib 동봉 = 단일 메시·서브메시 1).</summary>
        private static readonly string[] SeaFormModelNames =
        {
            "seaform_a", "seaform_b", "seaform_c", "seaform_d",
            "seaform_e", "seaform_f", "seaform_g", "seaform_h",
        };

        /// <summary>각 지형지물의 실측 전체 크기(m, W×H×D · 밑면 y=0). OBJ 정점 실측값 -
        /// BoxCollider 대략치 / 수면 돌출 판정(H) / footprint 반경(max(W,D)/2)에 쓴다
        /// (RockModelSizes와 같은 사본 정책).</summary>
        private static readonly Vector3[] SeaFormModelSizes =
        {
            new Vector3(6.13f, 4.21f, 3.11f), // a 해저 아치(관통 개구 2.94m)
            new Vector3(4.84f, 4.72f, 4.39f), // b 기둥 군집
            new Vector3(8.12f, 2.49f, 4.28f), // c 계단 리지
            new Vector3(6.11f, 4.28f, 4.31f), // d 협곡 블록쌍(통로 1.6m 관통)
            new Vector3(4.44f, 2.71f, 3.51f), // e 오버행 바위(언더컷 1.6m)
            new Vector3(7.23f, 0.99f, 4.96f), // f 균열 암반판
            new Vector3(2.65f, 5.97f, 2.42f), // g 탑 바위
            new Vector3(4.75f, 2.10f, 3.62f), // h 잔해 더미
        };

        /// <summary>모델별 수심대 하한(m). f·c는 얕은~중간, a·d·e는 중간, g·b·h는 깊은 곳이다.
        /// 실제 하한은 max(이 값, 모델높이×스케일 + 1m 여유)라 수면 위로는 절대 튀어나오지 않는다
        /// (예: seaform_g 5.97m × 1.2 = 7.16m → 8.16m 이상 수심에만 선다).</summary>
        private static readonly float[] SeaFormDepthMin = { 4f, 7f, 2f, 4f, 4f, 2f, 7f, 7f };

        /// <summary>모델별 수심대 상한(m). 스커트 최심(약 18m) 안쪽이라 항상 유효 구간이 남는다.</summary>
        private static readonly float[] SeaFormDepthMax = { 10f, 15f, 8f, 10f, 10f, 8f, 15f, 15f };

        /// <summary>비볼록 MeshCollider가 필수인 관통형 표식. a(아치)·d(협곡)는 볼록 근사를 쓰면
        /// 통로가 통째로 메워져 지형지물의 존재 이유가 사라지고, e(오버행)도 언더컷 그늘이 채워진다
        /// (IslandMeshGenerator.Vegetation의 [B52] cliff_b 사고와 같은 근거). 나머지 5종은 기존 큰
        /// searock 규약대로 BoxCollider 대략치다.</summary>
        private static readonly bool[] SeaFormNonConvex = { true, false, false, true, true, false, false, false };

        /// <summary>수심대 3그룹의 모델 인덱스 풀. 얕은~중간(f·c) / 중간(a·d·e) / 깊은 곳(g·b·h).</summary>
        private static readonly int[] SeaFormShallowGroup = { 5, 2 };
        private static readonly int[] SeaFormMidGroup = { 0, 3, 4 };
        private static readonly int[] SeaFormDeepGroup = { 6, 1, 7 };

        /// <summary>지형지물 스케일 범위. 상한 1.2는 "모델 높이 + 1m 여유 ≤ 스커트 최심"을 지키는 값이다.</summary>
        private const float SeaFormScaleMin = 0.9f;
        private const float SeaFormScaleMax = 1.2f;

        /// <summary>지형지물 밑단(y=0)을 모래 기복(±0.6m)에 파묻는 침하량(m). 큰 접지면이라
        /// searock(0.10m)보다 깊게 문다 - 가장자리 들뜸/틈 방지.</summary>
        private const float SeaFormSink = 0.15f;

        /// <summary>지형지물과 다른 배치물 사이의 여유 간격(m). 실제 최소 거리는
        /// (내 footprint 반경 + 상대 예약 반경 + 이 값)이다(반경 합 기반).</summary>
        private const float SeaFormClearance = 2.5f;

        /// <summary>후보 하나당 최대 시도 수(무한 루프 금지 - TryPickPoint와 같은 규칙).</summary>
        private const int SeaFormMaxAttempts = 24;

        /// <summary>동굴 착지 링 회피를 **강한 선호**로 거는 앞쪽 시도 수. 이 구간의 후보는 링을
        /// 피한 자리만 채택하고, 그래도 자리를 못 찾으면 남은 시도는 링 회피를 풀어 배치 자체를
        /// 살린다. 하드 배제로 두지 않는 이유는 계산으로 확인된 사실 때문이다: 동굴은 "수심 8m가
        /// 되는 첫 지점"에 서므로 그 링이 중간 수심대(a·d 4~10m)의 반경 구간을 통째로 덮어,
        /// 하드 배제로 두면 대형·특대 섬에서 아치·협곡이 아예 서지 못한다. 링을 푼 뒤 실제로 겹칠
        /// 확률은 방위까지 맞아야 하므로 낮다(특대 섬 기준 지형지물 1개당 약 1~2%).</summary>
        private const int SeaFormCaveGuardAttempts = 8;

        // ── 점유 예약 (반경 합 기반 최소 간격) ──────────────────────────────────────
        // (x, z, 반경) 목록. Spawn 진입 시 비우고, **이 파일이 이번 섬에서 자리를 확정한 순간**
        // 등록한다(메시 로드 여부와 무관하게 - 그래야 임포트가 한 프레임 늦어도 지형지물의 후보
        // 채택/거절이 같아 draw 수가 흔들리지 않는다). searock은 일부러 넣지 않는다: ≤3m 소품이고,
        // 그 배치 루프의 반복 횟수 자체가 메시 로드 여부에 좌우돼(placed 증가 조건) 예약 집합이
        // 비결정적이 되기 때문이다. 소품이 지형지물 발치에 붙는 것은 애초에 자연스럽다(암설).

        /// <summary>산호 패치 중심 예약 반경(m). 패치 산포가 중심 반경 3~7m라 그 외곽과 같다.</summary>
        private const float CoralPatchReserve = 7f;

        /// <summary>해초 숲 중심 예약 반경(m). 군락 산포가 중심 반경 1.5~4.5m라 그 외곽과 같다.</summary>
        private const float KelpGroveReserve = 4.5f;

        /// <summary>침몰 화물 더미 예약 반경(m). 컨테이너 0.6m + 파편 1.7m 산포의 외곽이다.</summary>
        private const float CargoPileReserve = 2.5f;

        /// <summary>수중 동굴 예약 반경(m). 동굴 footprint 반쪽(≤7.2m - UnderwaterCaveSpawner
        /// 배치 스캔 주석)의 올림값이다.</summary>
        private const float CaveReserve = 8f;

        /// <summary>이번 섬의 점유 목록. (x, y=z, z=반경)로 담는 Vector3 재사용 버퍼다
        /// (섬마다 Clear - 월드 재생성/도메인 리로드는 ResetStaticCache가 함께 비운다).</summary>
        private static readonly List<Vector3> occupancy = new List<Vector3>(48);

        // ── 침몰 화물 (잠수 보상 - crate_a/barrel_a 재사용) ─────────────────────────

        /// <summary>침몰 화물 컨테이너 모델 2종. 인덱스 0=궤짝, 1=통(CargoModelSizes와 일대일).</summary>
        private static readonly string[] CargoModelNames = { "crate_a", "barrel_a" };

        /// <summary>컨테이너 실측 크기(m, W×H×D · 밑면 y=0). 뭍 표류물 로더(IslandMeshGenerator.
        /// MeshLibrary의 DriftModelSizes)와 같은 값의 사본이다(RockModelSizes와 같은 사본 정책 -
        /// 그쪽은 private라 참조할 수 없다).</summary>
        private static readonly Vector3[] CargoModelSizes =
        {
            new Vector3(0.82f, 0.66f, 0.74f), // crate_a
            new Vector3(0.60f, 0.86f, 0.60f), // barrel_a
        };

        // ── 진주조개 (수심 2~8m 채집 노드 - clam_a/b/c) ────────────────────────────

        /// <summary>진주조개 3종(clam_a~c). `o` 오브젝트 2개(shell → pearl 순서, mtllib 포함)라
        /// 산호와 같은 병합 임포트 대비가 필요하다(서브메시 2 = [shell, pearl] 머티리얼 배열).</summary>
        private static readonly string[] ClamModelNames = { "clam_a", "clam_b", "clam_c" };

        /// <summary>조개 변종 수(= ClamModelNames.Length). UnderwaterCaveSpawner가 동굴 내부
        /// 조개 변종을 뽑을 때 쓴다 - 배열 자체를 노출하지 않고 개수만 준다.</summary>
        internal const int ClamVariantCount = 3;

        /// <summary>각 조개 모델의 실측 전체 크기(m, W×H×D · 밑면 y=0). OBJ 정점 실측값 -
        /// 상호작용 BoxCollider 대략치에 쓴다(RockModelSizes와 같은 사본 정책).</summary>
        private static readonly Vector3[] ClamModelSizes =
        {
            new Vector3(0.50f, 0.37f, 0.45f), // clam_a
            new Vector3(0.35f, 0.21f, 0.33f), // clam_b
            new Vector3(0.70f, 0.60f, 0.63f), // clam_c
        };

        // ── 채집 해초 서브셋 (순수 위치 해시 - rng draw 소비 0) ─────────────────────

        /// <summary>배치된 kelp 중 채집 가능("해초 군락") 서브셋의 당첨 비율(~15%).</summary>
        private const float HarvestKelpChance = 0.15f;

        /// <summary>위치 해시 salt. 같은 위치라도 용도(당첨 판정 / 지급 수량)마다 독립인 값이 나오게
        /// 가른다(IslandMeshGenerator.MeshLibrary의 salt 상수 계열과 같은 규칙 - 값은 그쪽
        /// 0x51A7B0xx 대역과 겹치지 않는 별도 대역이다).</summary>
        private const uint KelpHarvestSelectSalt = 0x6B3E1F01u;
        private const uint KelpHarvestCountSalt = 0x6B3E1F02u;

        // ── 팔레트 (순수 Color 상수라 필드 초기화식에 두어도 안전하다 - Unity API 호출 없음) ────

        /// <summary>산호 12색 팔레트(핑크/주황/자홍/노랑/청록/보라 등). 변종 인덱스 % 12로 순환하고,
        /// tip은 같은 색을 Shade 1.35로 밝혀 성장점이 살아 있는 산호로 읽히게 한다.</summary>
        private static readonly Color[] CoralPalette =
        {
            new Color(0.94f, 0.55f, 0.55f), // 산호 핑크
            new Color(0.93f, 0.53f, 0.25f), // 주황
            new Color(0.85f, 0.30f, 0.55f), // 자홍
            new Color(0.90f, 0.78f, 0.32f), // 노랑
            new Color(0.30f, 0.75f, 0.70f), // 청록
            new Color(0.62f, 0.42f, 0.78f), // 보라
            new Color(0.88f, 0.36f, 0.28f), // 주홍
            new Color(0.95f, 0.68f, 0.50f), // 살구
            new Color(0.48f, 0.55f, 0.85f), // 청보라
            new Color(0.42f, 0.78f, 0.50f), // 초록빛 청록
            new Color(0.90f, 0.45f, 0.65f), // 장미
            new Color(0.85f, 0.62f, 0.25f), // 호박
        };

        /// <summary>해초 4단 녹갈 팔레트(어두운 갈조 → 밝은 녹조). 변종 인덱스 % 4로 순환한다.</summary>
        private static readonly Color[] KelpPalette =
        {
            new Color(0.33f, 0.28f, 0.14f),
            new Color(0.38f, 0.40f, 0.16f),
            new Color(0.30f, 0.46f, 0.22f),
            new Color(0.42f, 0.58f, 0.26f),
        };

        /// <summary>수중 바위 3단(어두운 현무암 회색 → 해조 낀 회록). 변종 인덱스 % 3으로 순환한다.</summary>
        private static readonly Color[] RockPalette =
        {
            new Color(0.26f, 0.27f, 0.28f),
            new Color(0.33f, 0.35f, 0.34f),
            new Color(0.36f, 0.42f, 0.36f),
        };

        // ── 공유 메시 캐시 (모델 63종 = 산호 20 + 해초 10 + 바위 20 + 화물 2 + 조개 3 + 지형지물 8) ──
        // 산호는 body/tip 두 장(임포터가 합치면 primary 한 장에 서브메시 2, secondary는 null).
        private static readonly Mesh[] coralPrimary = new Mesh[20];
        private static readonly Mesh[] coralSecondary = new Mesh[20];
        private static readonly Mesh[] kelpMeshes = new Mesh[10];
        private static readonly Mesh[] rockMeshes = new Mesh[20];
        private static readonly Mesh[] cargoMeshes = new Mesh[2]; // crate_a / barrel_a (`o` 1개 = 단일 메시)
        // 조개도 산호처럼 두 장(shell/pearl). 병합 임포트면 primary 한 장에 서브메시 2, secondary는 null.
        private static readonly Mesh[] clamPrimary = new Mesh[3];
        private static readonly Mesh[] clamSecondary = new Mesh[3];
        // 해저 지형지물 8종(seaform_a~h). `o` 1개 + usemtl 1종이라 단일 메시·서브메시 1이다.
        private static readonly Mesh[] seaFormMeshes = new Mesh[8];

        /// <summary>프레임당 1회 프로브 가드(TryGetBambooModel/AirlinerWreck.probeFrame과 같은 규칙).
        /// 실패를 영구 래치하지 않으므로 임포트가 한 프레임 늦어도 다음 섬/다음 월드에서 자연 복구된다.</summary>
        private static int probeFrame = -1;

        // ── 해초 흔들림(MGKelpSway) 머티리얼 경로 ──────────────────────────────────
        // 해초만 커스텀 정점 스웨이 셰이더(Resources/Shaders/MGKelpSway)를 쓴다. 셰이더 로드가
        // 실패하면 기존 GetMaterial(색, "leaf") URP Lit 경로 그대로다(폴백 필수 - MGOcean과 같은
        // 계약). 산호/바위/화물의 GetMaterial 경로는 이 블록과 무관하게 불변이다.

        /// <summary>MGKelpSway의 스웨이 시간 프로퍼티 ID. 셰이더는 내장 _Time 대신 이 값을 쓰므로
        /// C#(KelpSwayDriver)이 넣는 Time.time이 곧 흔들림의 시계다(타이틀 화면 정지 관례 -
        /// WorldMapManager.OceanWaveTimeProperty와 같은 설계).</summary>
        private static readonly int SwayTimeProperty = Shader.PropertyToID("_MG_SwayTime");

        /// <summary>MGKelpSway 셰이더 캐시. 로드 실패를 영구 래치하지 않는다(프레임 가드는 아래).</summary>
        private static Shader kelpSwayShader;

        /// <summary>셰이더 프로브 프레임 가드(probeFrame과 같은 규칙 - 같은 프레임의 해초 수십 개가
        /// Resources.Load를 반복하지 않게 하되, 실패가 다음 프레임/다음 월드에서 자연 복구되게 한다).</summary>
        private static int kelpShaderProbeFrame = -1;

        /// <summary>스웨이 머티리얼 정적 캐시 - KelpPalette(4단 녹갈)와 일대일이라 월드 전체에서
        /// 최대 4장이다. 파괴된 머티리얼은 Unity의 == 오버로드가 null로 알려주므로 다시 만든다
        /// (ResourceVisualLibrary.GetMaterial과 같은 검사).</summary>
        private static readonly Material[] kelpSwayMaterials = new Material[4];

        /// <summary>스웨이 시간 갱신 프레임 가드 - 섬마다 KelpSwayDriver가 하나씩 붙어도 SetFloat는
        /// 프레임당 머티리얼 4장 × 1회만 나간다.</summary>
        private static int swayTimeFrame = -1;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시/래치가 이전 실행의 파괴된 자원을 들고
        /// 시작하지 않게 초기 상태로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            System.Array.Clear(coralPrimary, 0, coralPrimary.Length);
            System.Array.Clear(coralSecondary, 0, coralSecondary.Length);
            System.Array.Clear(kelpMeshes, 0, kelpMeshes.Length);
            System.Array.Clear(rockMeshes, 0, rockMeshes.Length);
            System.Array.Clear(cargoMeshes, 0, cargoMeshes.Length);
            System.Array.Clear(clamPrimary, 0, clamPrimary.Length);
            System.Array.Clear(clamSecondary, 0, clamSecondary.Length);
            System.Array.Clear(seaFormMeshes, 0, seaFormMeshes.Length);
            occupancy.Clear();
            probeFrame = -1;
            kelpSwayShader = null;
            kelpShaderProbeFrame = -1;
            System.Array.Clear(kelpSwayMaterials, 0, kelpSwayMaterials.Length);
            swayTimeFrame = -1;
        }

        /// <summary>
        /// _MG_SwayTime을 매 프레임 넣는 최소 드라이버. 섬의 생태 루트(SeabedFlora_*)에 붙어
        /// RegenerateWorld의 섬 파괴에 함께 편승한다(스포너의 "생명주기 편승" 원칙 - 별도 정리 불요).
        /// Time.time은 timeScale = 0에서 멈추므로 타이틀 화면에서 해초도 바다처럼 정지한다.
        /// </summary>
        private sealed class KelpSwayDriver : MonoBehaviour
        {
            private void Update()
            {
                if (swayTimeFrame == Time.frameCount)
                    return; // 다른 섬의 드라이버가 이번 프레임 몫을 이미 갱신했다
                swayTimeFrame = Time.frameCount;

                float time = Time.time;
                for (int i = 0; i < kelpSwayMaterials.Length; i++)
                {
                    Material material = kelpSwayMaterials[i];
                    if (material != null)
                        material.SetFloat(SwayTimeProperty, time);
                }
            }
        }

        /// <summary>
        /// 해초 머티리얼 하나(변종 → 4단 녹갈 팔레트 순환은 기존과 동일). MGKelpSway 셰이더가
        /// 로드되면 스웨이 머티리얼(정적 캐시, leaf 텍스처 + 팔레트 색 - 폴백 경로와 같은 외형 문법),
        /// 아니면 기존 GetMaterial(색, "leaf") 폴백이다. 어느 경로든 rng를 일절 소비하지 않는다.
        /// </summary>
        private static Material GetKelpMaterial(int variant)
        {
            int paletteIndex = variant % KelpPalette.Length;
            Color color = KelpPalette[paletteIndex];

            Material cached = kelpSwayMaterials[paletteIndex];
            if (cached != null)
                return cached;

            if (kelpSwayShader == null && kelpShaderProbeFrame != Time.frameCount)
            {
                kelpShaderProbeFrame = Time.frameCount;
                kelpSwayShader = Resources.Load<Shader>("Shaders/MGKelpSway");
            }

            if (kelpSwayShader == null)
                return ResourceVisualLibrary.GetMaterial(color, "leaf"); // 기존 경로 - 캐시는 그쪽이 가진다

            var material = new Material(kelpSwayShader);
            // 런타임 생성 표식(StructureVisualBuilder.CreateColorMaterial과 같은 근거).
            material.name = StructureVisualBuilder.RuntimeMaterialPrefix + "KelpSway_" + paletteIndex;
            material.color = color; // [MainColor] _BaseColor
            // 폴백 경로(CreateColorMaterial)와 같은 표면 텍스처/타일링 - 셰이더 유무로 질감이 튀지 않게.
            var leafTexture = Resources.Load<Texture2D>("Textures/leaf");
            if (leafTexture != null)
            {
                material.mainTexture = leafTexture; // [MainTexture] _BaseMap
                material.mainTextureScale = new Vector2(1.5f, 1.5f);
            }
            material.enableInstancing = true;
            material.SetFloat(SwayTimeProperty, Time.time); // 첫 프레임부터 드라이버와 같은 시계
            kelpSwayMaterials[paletteIndex] = material;
            return material;
        }

        /// <summary>
        /// 섬 하나의 해저 생태를 배치한다. SeabedGenerator.Build가 스커트 레코드를 등록한 직후
        /// 같은 동기 흐름에서 호출한다(그래야 아래 TrySampleSeabed가 이 섬의 스커트를 안다).
        /// </summary>
        /// <param name="islandObject">섬 지형 루트("Island_{id}_{size}"). 배치물은 전부 이 자식이다.</param>
        /// <param name="radius">섬 지형 반지름 R(m). 스커트 안쪽 경계와 같다.</param>
        public static void Spawn(GameObject islandObject, float radius)
        {
            if (islandObject == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 생태가 겹으로 깔리지 않게 한다(SeabedGenerator.Build와 동일).
            string rootName = FloraRootPrefix + islandObject.name;
            if (islandObject.transform.Find(rootName) != null)
                return;

            // worldSeed/seaLevel은 섬 루트의 부모(WorldMapManager.transform - SpawnPlaceholder가
            // 섬을 그 자식으로 만든다)에서 읽는다. rng를 얻기 위한 읽기 전용 접근이라 어떤 스트림도
            // 소비하지 않는다. islandId는 이름 "Island_{id}_{size}"에서 파싱한다(이름은
            // BuildIslandSurface 호출 전에 확정된다 - WorldMapManager.SpawnPlaceholder 순서).
            var manager = islandObject.GetComponentInParent<WorldMapManager>();
            int worldSeed = manager != null ? manager.worldSeed : 0;
            float seaLevel = manager != null ? manager.seaLevel : 0f;
            int islandId = ParseIslandId(islandObject.name);

            // [rng 격리] 이 섬 전용 독립 스트림. 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 불변이다.
            var rng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ SeedSalt));

            // 이번 섬의 점유 예약 목록을 비운다(지형지물 최소 간격 판정 전용 - rng 소비 0).
            occupancy.Clear();

            EnsureModelsLoaded();

            var root = new GameObject(rootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Vector3 center = islandObject.transform.position;
            // 스커트 폭. SeabedGenerator.SkirtWidth와 같은 식의 사본이다(그쪽은 private) - 어긋나도
            // 후보 적중률만 떨어질 뿐, 접지 정답은 항상 TrySampleSeabed가 준다(범위 밖이면 false).
            float skirtWidth = Mathf.Clamp(radius * 0.6f, 30f, 90f);
            float rMin = radius + 1f;
            float rMax = radius + skirtWidth - 1f;

            // 섬 크기별 스케일: Small ×0.7 / Medium ×1.0 / Large ×1.4 / XL ×1.8.
            // 반지름 경계(50/90/140/200 - IslandSizeMetrics.GetTerrainRadius)의 중간값으로 가른다
            // (IslandMeshGenerator.Vegetation의 규모 판정과 같은 전략 - 반지름 공식이 바뀌어도 따라간다).
            float scale = SizeScale(radius);

            SpawnCoralPatches(rng, root.transform, center, rMin, rMax, seaLevel, scale);
            SpawnKelpGroves(rng, root.transform, center, rMin, rMax, seaLevel, scale);
            SpawnSeaRocks(rng, root.transform, center, rMin, rMax, seaLevel, scale);

            // [맨 끝 draw 원칙] 침몰 화물은 반드시 산호/해초/바위 draw가 **전부 끝난 뒤**에만 뽑는다.
            // 이 격리 스트림의 꼬리에 붙는 추가 소비라, 위 세 배치의 추첨 순서·결과는 1비트도 밀리지
            // 않는다(PlaceLargeStones가 BuildIslandSurface 맨 끝에서만 불리는 것과 같은 원칙).
            SpawnSunkenCargo(rng, root.transform, center, rMin, rMax, seaLevel, radius);

            // [rng 격리 3 - 해저 지형지물] seaform_a~h는 searock 소품과 역할이 다른 **지형 계열**이라
            // 배치 경로도 스트림도 분리한다. 기존 0x5EABED 스트림의 꼬리에 붙이지 않으므로(0x5EAF
            // 제3 독립 스트림) 위 네 배치의 draw 수·순서·결과는 지형지물 추가 전후로 완전 동일하다.
            // 호출 위치가 화물 다음인 이유는 rng가 아니라 **점유 예약** 때문이다 - 산호밭/해초숲/
            // 화물 자리가 먼저 등록돼 있어야 반경 합 기반 간격 판정이 그것들을 피할 수 있다.
            var seaFormRng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ SeaFormSeedSalt));
            SpawnSeaForms(seaFormRng, root.transform, center, radius, skirtWidth, seaLevel);

            // [rng 격리 2] 진주조개는 기존 0x5EABED 스트림의 꼬리에도 붙이지 않고 **별도 솔트의
            // 제2 독립 스트림**으로만 뽑는다 - 이미 검증된 위 네 배치의 draw 수·순서·결과는
            // 조개 추가 전후로 1비트도 다르지 않다(완전 분리가 안전 - 헤더 rng 격리 주석).
            var clamRng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ ClamSeedSalt));
            SpawnPearlClams(clamRng, root.transform, center, rMin, rMax, seaLevel, radius);

            // 해초 스웨이 시간 구동: 스웨이 머티리얼이 하나라도 살아 있으면 이 섬의 생태 루트에
            // 드라이버를 붙인다(rng 소비 없음 - 추첨 순서 불변). 프레임 가드 덕에 섬이 몇 개든
            // SetFloat는 프레임당 4회 수준이고, 루트가 파괴되면 드라이버도 함께 사라진다.
            for (int i = 0; i < kelpSwayMaterials.Length; i++)
            {
                if (kelpSwayMaterials[i] != null)
                {
                    root.AddComponent<KelpSwayDriver>();
                    break;
                }
            }
        }

        // ── 배치 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 산호밭 2~3패치(×크기 스케일). 패치 중심은 깊이 1.5~7m 대역에서 뽑고, 패치당 산호 5~9개를
        /// 중심 반경 3~7m에 산포한다. 같은 패치엔 같은 계열 60% 편향(군락감).
        /// </summary>
        private static void SpawnCoralPatches(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int patchCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(2, 4) * scale), 1, 5);
            for (int p = 0; p < patchCount; p++)
            {
                // 패치 중심: 깊이 1.5~7m 대역. 최대 시도 수 고정(무한 루프 금지) - 실패하면 이 패치만 버린다.
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 1.5f, 7f, 12,
                        out Vector3 patchCenter, out _))
                    continue;

                // 지형지물 간격 판정용 점유 등록(rng 소비 0 - 이 패치의 추첨은 아래 그대로다).
                Reserve(patchCenter, CoralPatchReserve);

                int patchFamily = rng.NextInt(0, CoralFamilyStart.Length);
                int coralCount = rng.NextInt(5, 10);
                for (int c = 0; c < coralCount; c++)
                {
                    // 산포 후보: 중심 반경 3~7m 극좌표. 접지 실패(스커트 범위 밖)면 그 후보만 버린다.
                    Vector3 pos = Vector3.zero;
                    bool found = false;
                    for (int attempt = 0; attempt < 4 && !found; attempt++)
                    {
                        float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                        float dist = rng.NextFloat(3f, 7f);
                        var candidate = patchCenter
                            + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                        if (SeabedGenerator.TrySampleSeabed(candidate, out float y))
                        {
                            pos = new Vector3(candidate.x, y, candidate.z);
                            found = true;
                        }
                    }

                    if (!found)
                        continue;

                    // 계열 편향 60% + 계열 내 변종 균등. 변종 인덱스가 곧 팔레트 순환 키다.
                    int family = rng.NextValue01() < 0.6f ? patchFamily : rng.NextInt(0, CoralFamilyStart.Length);
                    int variant = CoralFamilyStart[family] + rng.NextInt(0, CoralFamilyCount[family]);
                    float yaw = rng.NextFloat(0f, 360f);
                    float size = rng.NextFloat(0.8f, 1.35f);
                    PlaceCoral(root, center, pos, variant, yaw, size);
                }
            }
        }

        /// <summary>
        /// 해초 숲 1~3군데(×크기 스케일), 깊이 2~10m. 군데당 kelp 5~10개를 중심 반경 1.5~4.5m에 심고,
        /// 심는 지점의 실제 깊이로 리본형(깊은 쪽, 높이 1.4m+)/방석형(얕은 쪽)을 가른다.
        /// </summary>
        private static void SpawnKelpGroves(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int groveCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(1, 4) * scale), 1, 5);
            for (int g = 0; g < groveCount; g++)
            {
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 2f, 10f, 12,
                        out Vector3 groveCenter, out _))
                    continue;

                // 지형지물 간격 판정용 점유 등록(rng 소비 0).
                Reserve(groveCenter, KelpGroveReserve);

                int kelpCount = rng.NextInt(5, 11);
                for (int k = 0; k < kelpCount; k++)
                {
                    Vector3 pos = Vector3.zero;
                    float depth = 0f;
                    bool found = false;
                    for (int attempt = 0; attempt < 4 && !found; attempt++)
                    {
                        float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                        float dist = rng.NextFloat(1.5f, 4.5f);
                        var candidate = groveCenter
                            + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                        if (SeabedGenerator.TrySampleSeabed(candidate, out float y))
                        {
                            pos = new Vector3(candidate.x, y, candidate.z);
                            depth = seaLevel - y;
                            found = true;
                        }
                    }

                    if (!found)
                        continue;

                    // 리본형은 깊은 쪽(수면까지 뻗을 공간이 필요), 방석형은 얕은 쪽. 경계 4.5m.
                    int variant = PickKelpVariant(rng, depth >= 4.5f);
                    if (variant < 0)
                        continue;

                    float yaw = rng.NextFloat(0f, 360f);
                    float size = rng.NextFloat(0.8f, 1.4f);
                    PlaceKelp(root, center, pos, variant, yaw, size);
                }
            }
        }

        /// <summary>
        /// 수중 바위 8~14개(×크기 스케일)를 스커트 전 깊이에 산포한다. 첨탑(k~o)은 깊이 6m 이상에만
        /// (잠수 랜드마크), 스케일 적용 후 최대 치수 1.5m 초과인 것에만 BoxCollider(대략치)를 단다.
        /// </summary>
        private static void SpawnSeaRocks(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int rockCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(8, 15) * scale), 5, 26);
            int placed = 0;
            int maxAttempts = rockCount * 4; // 시도 수 고정 - 무한 루프 금지
            for (int attempt = 0; attempt < maxAttempts && placed < rockCount; attempt++)
            {
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 0.5f, 18.5f, 1,
                        out Vector3 pos, out float depth))
                    continue;

                // 깊이 6m 이상에서만 30% 확률로 첨탑. 얕은 곳은 표석/판형/군집(비첨탑 15종)만.
                int variant;
                if (depth >= 6f && rng.NextValue01() < 0.30f)
                {
                    variant = SpireStart + rng.NextInt(0, SpireCount);
                }
                else
                {
                    int pick = rng.NextInt(0, RockModelNames.Length - SpireCount); // 0~14
                    variant = pick < SpireStart ? pick : pick + SpireCount;        // 첨탑 구간 건너뜀
                }

                float yaw = rng.NextFloat(0f, 360f);
                float size = rng.NextFloat(0.8f, 1.6f);
                if (PlaceRock(root, center, pos, variant, yaw, size))
                    placed++;
            }
        }

        // ── 해저 지형지물 (seaform_a~h - searock 소품과 분리된 배치 경로) ─────────────

        /// <summary>
        /// 잠수 동선을 만드는 해저 지형지물(통과형 아치/협곡, 단차 리지/암반판, 오버행, 탑·기둥·잔해)을
        /// 섬 규모별로 소수 배치한다.
        ///
        /// ── 규모별 개수(SizeScale과 같은 반지름 중간값 경계 50/90/140/200 → 70/115/170) ──
        ///   소형 0~1 · 중형 1~2 · 대형 2~4 · 특대 3~5. 지형지물 1개 = 렌더러 1개(단일 서브메시)라
        ///   섬당 추가 렌더러는 최대 5개다.
        ///
        /// ── 수심대 분리(SeaFormDepthMin/Max) ──
        ///   f(균열 암반판)·c(계단 리지) 2~8m / a(아치)·d(협곡)·e(오버행) 4~10m /
        ///   g(탑)·b(기둥군집)·h(잔해더미) 7~15m. 실제 수심은 SeabedGenerator.TrySampleSeabed 판정이다.
        ///
        /// ── 수면 돌출 방지 ──
        ///   실제 하한은 max(대역 하한, 모델높이 × 스케일 + 1m)다. 가장 높은 seaform_g(5.97m)는
        ///   스케일 1.2에서 8.16m 이상 수심에만 서므로 어떤 경우에도 꼭대기가 수면 아래 1m 이상이다.
        ///
        /// ── 최소 간격(반경 합 기반) ──
        ///   내 footprint 반경(= max(W,D)/2 × 스케일) + 상대 예약 반경 + 여유 2.5m. 상대는 이미
        ///   자리가 확정된 산호밭(7m)·해초숲(4.5m)·침몰 화물(2.5m)·먼저 놓인 지형지물이다.
        ///   이 넷은 하드 조건이고, 대형/특대 섬의 **수중 동굴 착지 링**(8m)만 앞쪽 시도에서
        ///   강하게 선호하는 소프트 조건이다(IsClearOfCaveRing / SeaFormCaveGuardAttempts).
        ///
        /// ── [결정성] ──
        ///   개수/그룹/변종/요/스케일 draw는 배치 성공 여부·메시 로드 여부를 보기 **전에** 끝낸다
        ///   (PlaceCargoPile과 같은 규칙). 후보 채택 판정은 지형 샘플과 점유 예약뿐인데 둘 다 메시
        ///   로드와 무관하므로, 임포트가 한 프레임 늦어도 같은 시드의 다음 월드에서 같은 자리에 선다.
        /// </summary>
        private static void SpawnSeaForms(System.Random rng, Transform root, Vector3 center,
            float radius, float skirtWidth, float seaLevel)
        {
            int count;
            if (radius < 70f)
                count = rng.NextInt(0, 2);       // 소형 0~1
            else if (radius < 115f)
                count = rng.NextInt(1, 3);       // 중형 1~2
            else if (radius < 170f)
                count = rng.NextInt(2, 5);       // 대형 2~4
            else
                count = rng.NextInt(3, 6);       // 특대 3~5

            // 동굴은 대형/특대 섬에만 생긴다(UnderwaterCaveSpawner.Spawn의 115m 경계).
            bool caveIsland = radius >= 115f;

            for (int i = 0; i < count; i++)
            {
                // ── 1) draw 전부 ──
                int group = rng.NextInt(0, 3);
                int[] pool = group == 0 ? SeaFormShallowGroup
                    : (group == 1 ? SeaFormMidGroup : SeaFormDeepGroup);
                int variant = pool[rng.NextInt(0, pool.Length)];
                float yaw = rng.NextFloat(0f, 360f);
                float scale = rng.NextFloat(SeaFormScaleMin, SeaFormScaleMax);

                // ── 2) 수심 조건 + 후보 채택(rng는 후보 각/반경만 소비 - 지형 샘플은 결정적) ──
                Vector3 size = SeaFormModelSizes[variant];
                float depthMin = Mathf.Max(SeaFormDepthMin[variant], size.y * scale + 1f);
                float depthMax = SeaFormDepthMax[variant];
                float footprint = 0.5f * Mathf.Max(size.x, size.z) * scale;

                if (depthMin > depthMax)
                    continue; // 구조적으로 설 수 없는 조합(현재 표에서는 발생하지 않는다)

                if (!TryPickSeaFormPoint(rng, center, radius, skirtWidth, seaLevel, footprint,
                        depthMin, depthMax, caveIsland, out Vector3 pos))
                    continue; // 자리 없음 - draw는 이미 전부 소비됐다(결정성)

                // ── 3) 점유 등록 → 생성 ──
                // 등록은 **메시 확인 전**이다. 그래야 임포트 지연으로 시각이 빠져도 뒤따르는
                // 지형지물의 채택/거절이 같아 draw 수가 흔들리지 않는다.
                Reserve(pos, footprint);
                PlaceSeaForm(root, center, pos, variant, yaw, scale, i);
            }
        }

        /// <summary>
        /// 지형지물 후보 하나를 뽑는다. 스커트 환형 안쪽/바깥쪽으로 footprint + 2m를 물려 모델이
        /// 환형 밖으로 삐져나오지 않게 하고, 수심대·최소 간격·동굴 링 회피를 모두 통과한 첫 후보를
        /// 채택한다. 시도 수는 고정(무한 루프 금지 - TryPickPoint와 같은 규칙)이다.
        /// </summary>
        private static bool TryPickSeaFormPoint(System.Random rng, Vector3 center, float radius,
            float skirtWidth, float seaLevel, float footprint, float depthMin, float depthMax,
            bool caveIsland, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;

            float rMin = radius + footprint + 2f;
            float rMax = radius + skirtWidth - footprint - 2f;
            if (rMax <= rMin)
                return false; // 스커트가 이 모델을 담기엔 좁다(draw 소비 없이 즉시 실패)

            for (int attempt = 0; attempt < SeaFormMaxAttempts; attempt++)
            {
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(rMin, rMax);
                var candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * r, 0f, center.z + Mathf.Sin(angle) * r);

                if (!SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                    continue;

                float depth = seaLevel - seabedY;
                if (depth < depthMin || depth > depthMax)
                    continue;

                if (!IsClear(candidate, footprint))
                    continue;

                // 앞쪽 시도에서만 동굴 착지 링을 피한다(뒤쪽은 완화 - SeaFormCaveGuardAttempts).
                if (caveIsland && attempt < SeaFormCaveGuardAttempts
                    && !IsClearOfCaveRing(center, radius, skirtWidth, seaLevel, angle, r, footprint))
                    continue;

                worldPos = new Vector3(candidate.x, seabedY, candidate.z);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 지형지물 하나를 세운다. 재질은 기존 수중 바위 규약(ResourceVisualLibrary.GetMaterial
        /// 공유 캐시)의 어두운 현무암 한 색이라 지형지물이 몇 개든 **새 머티리얼이 0장**이다
        /// (이 파일에 깊이별 명암 규칙은 없으므로 단일 색 - searock의 RockPalette[0]과 같은 장을 쓴다).
        ///
        /// [콜라이더] 기존 큰 searock 규약(스케일 후 최대 치수 &gt; 1.5m → 콜라이더)을 따른다.
        /// 8종 전부 그 문턱을 넘으므로 전부 콜라이더가 붙되, 관통형 a(아치)·d(협곡)와 오버행 e는
        /// **비볼록 정적 MeshCollider**(Rigidbody 없음, convex 기본 false, sharedMesh = 렌더 메시 -
        /// 동굴 셸/해저 스커트와 같은 경로)라 통로·언더컷이 실제로 뚫려 있다. 나머지는 BoxCollider
        /// 대략치다. 이름이 "SeaForm_"이라 TerrainSampler 지형 판정("Island_" 접두 필터)에는
        /// 구조적으로 안 잡힌다(SeaRock/CaveShell과 같은 안전 근거).
        /// </summary>
        private static void PlaceSeaForm(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale, int index)
        {
            Mesh mesh = seaFormMeshes[variant];
            if (mesh == null)
                return; // 이 변종만 아직 안 로드됨 - 조용히 건너뛴다(래치 없음, 다음 월드에서 복구)

            Material material = ResourceVisualLibrary.GetMaterial(RockPalette[0], "rock");
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -SeaFormSink, 0f);
            var part = CreateVisualPart(root, "SeaForm_" + index + "_" + SeaFormModelNames[variant],
                mesh, material, localPos, yaw, scale);

            if (SeaFormNonConvex[variant])
            {
                // 비볼록 정적 MeshCollider. 볼록 헐은 정의상 오목부를 메우므로 아치 개구(2.94m)/
                // 협곡 통로(1.6m)/오버행 언더컷(1.6m)이 통째로 막힌다.
                part.AddComponent<MeshCollider>().sharedMesh = mesh; // convex 기본값 false
            }
            else
            {
                // 파츠 로컬 공간 기준 대략치 - 부모 스케일이 균등이라 콜라이더도 함께 커진다
                // (PlaceRock의 큰 바위 경로와 같은 식·같은 0.85 폭 계수).
                Vector3 size = SeaFormModelSizes[variant];
                var box = part.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = new Vector3(size.x * 0.85f, size.y, size.z * 0.85f);
            }
        }

        // ── 점유 예약 / 간격 판정 (전부 rng 소비 0인 순수 기하 판정) ──────────────────

        /// <summary>이번 섬의 점유 목록에 (중심 XZ, 반경)을 등록한다.</summary>
        private static void Reserve(Vector3 worldPos, float reserveRadius)
        {
            occupancy.Add(new Vector3(worldPos.x, worldPos.z, reserveRadius));
        }

        /// <summary>등록된 모든 점유와 (내 반경 + 상대 반경 + 여유) 이상 떨어져 있으면 true.</summary>
        private static bool IsClear(Vector3 worldPos, float ownRadius)
        {
            for (int i = 0; i < occupancy.Count; i++)
            {
                Vector3 entry = occupancy[i];
                float dx = worldPos.x - entry.x;
                float dz = worldPos.z - entry.y;
                float minDistance = ownRadius + entry.z + SeaFormClearance;
                if (dx * dx + dz * dz < minDistance * minDistance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 수중 동굴(UnderwaterCaveSpawner)의 착지 지점을 피한다.
        ///
        /// 동굴은 이 스포너가 끝난 **뒤**에 놓이므로 자리를 예약해 줄 수 없고, 그 방위(baseAngle)는
        /// 동굴 전용 rng가 정한다. 대신 동굴의 착지 규칙 자체를 이용한다: 동굴은 각 방위 레이를
        /// radius+8m부터 3m 간격으로 훑어 **수심 8~14m가 되는 첫 지점**에 선다. 즉 동굴 자리는
        /// 방위와 무관하게 "첫 수심 8m 교차점"이 그리는 얇은 링 위에만 있다. 그래서 후보의 자기
        /// 방위 레이에서 그 링까지의 반경 거리만 재면, 동굴이 어느 방위를 뽑든 겹치지 않는다
        /// (링이 방위에 따라 완만하게 변하는 곡선이라 이 근사가 보수적으로 안전하다).
        ///
        /// rng를 한 칸도 소비하지 않고(순수 지형 샘플), 채택 직전 후보에서만 부르므로 비용은
        /// 섬당 수백 회 샘플 수준이다(월드 생성 1회).
        /// </summary>
        private static bool IsClearOfCaveRing(Vector3 center, float radius, float skirtWidth,
            float seaLevel, float angle, float dist, float footprint)
        {
            // UnderwaterCaveSpawner의 스캔 상수 사본(그쪽은 private): 안팎 8m 물림, 3m 간격,
            // 수심 8~14m. 동굴 2종의 높이 하한(max(8, 높이+1) = 8)도 이 대역에 흡수된다.
            float distMin = radius + 8f;
            float distMax = radius + skirtWidth - 8f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            for (float d = distMin; d <= distMax; d += 3f)
            {
                var probe = new Vector3(center.x + cos * d, 0f, center.z + sin * d);
                if (!SeabedGenerator.TrySampleSeabed(probe, out float seabedY))
                    continue;

                float depth = seaLevel - seabedY;
                if (depth < 8f || depth > 14f)
                    continue;

                // 첫 교차점이 곧 동굴 착지점이다(동굴 스캔과 같은 "first hit wins").
                return Mathf.Abs(dist - d) >= CaveReserve + footprint;
            }

            return true; // 이 방위에는 동굴이 설 수 있는 수심대가 없다
        }

        /// <summary>
        /// 침몰 화물 더미(잠수 보상): 깊이 8m 이상 해저에 crate_a/barrel_a 2~4개를 비스듬히 쌓고
        /// 주변에 searock 파편 1~2개를 흩은 뒤, 더미 루트에 BoxCollider + AirlinerSalvagePoint를
        /// 붙여 E키 수거(InteractionController가 GetComponentInParent로 잡는다) 대상으로 만든다.
        /// 대형 섬 1곳 / 특대 섬 1~2곳, 그 외 규모는 만들지 않는다(draw도 없다 - 스트림 꼬리라 무관).
        ///
        /// [결정성] 모든 rng draw(개수/위치/자세/지급표 선택)는 메시 로드 여부를 보기 **전에** 끝낸다.
        /// 그래서 임포트가 한 프레임 늦어 이번 월드에서 화물이 안 보여도, 같은 시드의 다음 월드에서는
        /// 같은 자리·같은 지급표로 나온다(PlaceCoral의 "래치 없음" 규칙과 같은 계열).
        /// </summary>
        private static void SpawnSunkenCargo(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float radius)
        {
            // 규모 판정은 SizeScale과 같은 반지름 중간값 경계(115/170)를 쓴다.
            if (radius < 115f)
                return; // 소형/중형: 화물 없음
            int pileCount = radius < 170f ? 1 : rng.NextInt(1, 3); // 대형 1 / 특대 1~2

            for (int p = 0; p < pileCount; p++)
            {
                // 깊이 8m 이상(스커트 최심 18m 언저리까지) 해저 접지. 실패하면 이 더미만 버린다.
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 8f, 18.5f, 16,
                        out Vector3 pos, out _))
                    continue;

                // 지형지물 간격 판정용 점유 등록(rng 소비 0 - 메시 로드 여부와도 무관하게 등록한다).
                Reserve(pos, CargoPileReserve);

                PlaceCargoPile(rng, root, center, pos, p);
            }
        }

        /// <summary>
        /// 화물 더미 하나를 조립한다. draw 전부 → 메시 확인 → 생성 순서(결정성 - SpawnSunkenCargo 주석).
        /// 두 컨테이너 메시가 모두 미로드면 아무것도 만들지 않는다 - 보이지 않는 보물(콜라이더만 있는
        /// 수거 지점)은 만들지 않는 것이 올바른 폴백이다.
        /// </summary>
        private static void PlaceCargoPile(System.Random rng, Transform root, Vector3 islandCenter,
            Vector3 worldPos, int pileIndex)
        {
            // ── 1) draw 전부 (메시 로드 여부와 무관하게 항상 같은 횟수·순서로 소비) ──
            int containerCount = rng.NextInt(2, 5); // 2~4
            var kinds = new int[containerCount];    // 0=궤짝 1=통
            var offsets = new Vector3[containerCount];
            var yaws = new float[containerCount];
            var leans = new float[containerCount];
            var scales = new float[containerCount];
            for (int i = 0; i < containerCount; i++)
            {
                kinds[i] = rng.NextValue01() < 0.55f ? 0 : 1;
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                // 바닥층(처음 2개)은 중심 주변에 흩고, 그 위(3~4번째)는 중심 근처에 쌓아 올린다.
                bool grounded = i < 2;
                float dist = grounded ? rng.NextFloat(0.22f, 0.60f) : rng.NextFloat(0.05f, 0.30f);
                float stackY = grounded ? 0f : 0.45f + 0.20f * (i - 2);
                offsets[i] = new Vector3(Mathf.Cos(angle) * dist, stackY, Mathf.Sin(angle) * dist);
                yaws[i] = rng.NextFloat(0f, 360f);
                // 궤짝은 모서리로 처박힌 기울기, 통은 옆으로 굴러 누운 자세(뭍 표류물과 같은 문법).
                leans[i] = kinds[i] == 0 ? rng.NextFloat(8f, 30f) : rng.NextFloat(68f, 98f);
                scales[i] = rng.NextFloat(0.9f, 1.15f);
            }

            int fragmentCount = rng.NextInt(1, 3); // searock 파편 1~2
            var fragVariants = new int[fragmentCount];
            var fragOffsets = new Vector3[fragmentCount];
            var fragYaws = new float[fragmentCount];
            var fragScales = new float[fragmentCount];
            for (int i = 0; i < fragmentCount; i++)
            {
                int pick = rng.NextInt(0, RockModelNames.Length - SpireCount); // 비첨탑 15종
                fragVariants[i] = pick < SpireStart ? pick : pick + SpireCount;
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float dist = rng.NextFloat(0.9f, 1.7f);
                fragOffsets[i] = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                fragYaws[i] = rng.NextFloat(0f, 360f);
                fragScales[i] = rng.NextFloat(0.45f, 0.75f);
            }

            float boxSize = rng.NextFloat(1.2f, 1.6f);
            bool lootPlanA = rng.NextValue01() < 0.5f; // 지급표 2안 중 택1

            // ── 2) 메시 확인 → 생성 ──
            if (cargoMeshes[0] == null && cargoMeshes[1] == null)
                return; // 둘 다 미로드 - 이번 월드는 이 더미를 통째로 건너뛴다(draw는 이미 소비됨)

            // 물에 잠긴 어두운 나무/금속 틴트. GetMaterial 공유 캐시라 더미가 몇 개든 머티리얼은 2장.
            Material woodMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.24f, 0.19f, 0.14f), "wood");
            Material metalMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.22f, 0.24f, 0.26f), "metal");

            // 더미 루트. "SunkenCargo_"는 "Island_"로 시작하지 않으므로 지형 판정에서 구조적으로 제외
            // (SeaRock 콜라이더와 같은 안전 근거). root(SeabedFlora_*)의 자식 = 섬 루트의 자손이라
            // RegenerateWorld의 섬 파괴에 함께 편승한다.
            var pile = new GameObject("SunkenCargo_" + pileIndex);
            pile.transform.SetParent(root, false);
            pile.transform.localPosition = worldPos - islandCenter + new Vector3(0f, -0.06f, 0f);

            for (int i = 0; i < containerCount; i++)
            {
                // 한쪽 메시만 로드됐으면 그쪽으로 대체한다(추가 draw 없음 - 자세 draw는 이미 확정).
                int kind = cargoMeshes[kinds[i]] != null ? kinds[i] : 1 - kinds[i];
                Mesh mesh = cargoMeshes[kind];
                Vector3 worldSize = CargoModelSizes[kind] * scales[i];

                // 접지 원점(밑면 y=0) 모델을 기울이면 밑면 가장자리가 원점 아래로 내려간다.
                // 그 깊이(수평 반폭 × sin(lean))만큼 들어 올린 뒤 sink만큼 모래에 파묻는다
                // (뭍 표류물 CreateDriftItem의 모델 분기와 같은 식).
                float radians = leans[i] * Mathf.Deg2Rad;
                float horizontalHalf = 0.5f * Mathf.Max(worldSize.x, worldSize.z);
                float lift = horizontalHalf * Mathf.Abs(Mathf.Sin(radians));
                float sink = Mathf.Min(0.12f, (0.5f * worldSize.y + lift) * 0.3f);

                var rotation = Quaternion.Euler(0f, yaws[i], 0f)
                    * Quaternion.AngleAxis(leans[i], Vector3.forward);
                CreateVisualPart(pile.transform, "Cargo_" + CargoModelNames[kind], mesh,
                    kind == 0 ? woodMaterial : metalMaterial,
                    offsets[i] + Vector3.up * (lift - sink), rotation, scales[i]);
            }

            for (int i = 0; i < fragmentCount; i++)
            {
                Mesh rock = rockMeshes[fragVariants[i]];
                if (rock == null)
                    continue; // 파편은 순수 장식 - 미로드면 그 파편만 없다
                Material rockMaterial = ResourceVisualLibrary.GetMaterial(
                    ResourceVisualLibrary.Shade(RockPalette[0], 0.9f), "rock");
                CreateVisualPart(pile.transform, "CargoRock_" + RockModelNames[fragVariants[i]], rock,
                    rockMaterial, fragOffsets[i] + new Vector3(0f, -0.04f, 0f),
                    Quaternion.Euler(0f, fragYaws[i], 0f), fragScales[i]);
            }

            // ── 3) 수거 지점 ──
            // 더미 루트의 BoxCollider를 InteractionController 레이가 맞으면 GetComponentInParent로
            // 같은 오브젝트의 AirlinerSalvagePoint가 잡힌다(여객기 잔해 지점과 같은 경로).
            var box = pile.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, boxSize * 0.45f, 0f);
            box.size = new Vector3(boxSize, boxSize * 0.9f, boxSize);

            // [한계] 수거 여부는 세이브 미저장 - AirlinerSalvagePoint [한계] 주석과 동일한 한계다
            // (월드 재생성 배경 오브젝트라 로드마다 리셋. 잔해 수거 세이브 연동 확장 때 함께 넣는다).
            var salvage = pile.AddComponent<AirlinerSalvagePoint>();
            salvage.displayName = "침몰 화물";
            salvage.loot = lootPlanA
                ? new[]
                {
                    new AirlinerSalvagePoint.LootEntry("금속조각", 3),
                    new AirlinerSalvagePoint.LootEntry("연료", 1),
                    new AirlinerSalvagePoint.LootEntry("천조각", 2),
                }
                : new[]
                {
                    new AirlinerSalvagePoint.LootEntry("금속조각", 2),
                    new AirlinerSalvagePoint.LootEntry("엔진부품", 1),
                    new AirlinerSalvagePoint.LootEntry("생수", 1),
                };
        }

        /// <summary>
        /// 진주조개(수심 2~8m 채집 노드): 섬 크기별 소수(소형 0~1 / 중형 2~3 / 대형·특대 3~5)를
        /// 해저에 접지 배치하고, 각 조개에 BoxCollider + AirlinerSalvagePoint("진주조개",
        /// 진주 1~2 - 배치 시 결정적 확정)를 붙인다. 규모 경계는 SizeScale과 같은 반지름 중간값이다.
        ///
        /// [rng 격리 2] 인자 rng는 Spawn이 만든 **조개 전용 제2 독립 스트림**(ClamSeedSalt)이다.
        /// 기존 0x5EABED 스트림은 여기서 만지지도, 이어 뽑지도 않는다.
        /// [결정성] 조개 하나의 모든 draw(위치/변종/자세/진주 수)는 메시 로드 여부를 보기 **전에**
        /// 끝낸다(PlaceCargoPile과 같은 규칙) - 임포트가 한 프레임 늦어도 같은 시드의 다음 월드에서
        /// 같은 자리·같은 진주 수로 나온다.
        /// </summary>
        private static void SpawnPearlClams(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float radius)
        {
            int clamCount;
            if (radius < 70f)
                clamCount = rng.NextInt(0, 2);       // 소형 0~1
            else if (radius < 115f)
                clamCount = rng.NextInt(2, 4);       // 중형 2~3
            else
                clamCount = rng.NextInt(3, 6);       // 대형/특대 3~5

            for (int i = 0; i < clamCount; i++)
            {
                // 수심 2~8m 해저 접지(기존 TryPickPoint/TrySampleSeabed 재사용). 실패하면 이 조개만 버린다.
                bool found = TryPickPoint(rng, center, rMin, rMax, seaLevel, 2f, 8f, 12,
                    out Vector3 pos, out _);

                // draw 전부 (접지 실패든 메시 미로드든 항상 같은 횟수·순서로 소비 - 결정성).
                int variant = rng.NextInt(0, ClamModelNames.Length);
                float yaw = rng.NextFloat(0f, 360f);
                float size = rng.NextFloat(0.85f, 1.25f);
                int pearlCount = rng.NextInt(1, 3); // 진주 1~2 - 배치 시 결정적 확정

                if (!found)
                    continue;

                PlaceClam(root, center, pos, variant, yaw, size, pearlCount, i);
            }
        }

        /// <summary>
        /// 진주조개 하나. 시각은 산호와 같은 두-파트 규칙(병합 임포트면 렌더러 하나 +
        /// sharedMaterials [껍데기, 진주], 개별 메시 2장이면 파츠 2개)이고, 수거는 침몰 화물과
        /// 같은 규칙(루트에 BoxCollider + AirlinerSalvagePoint - InteractionController의
        /// GetComponentInParent 경로)이다. 메시 미로드면 아무것도 만들지 않는다(보이지 않는
        /// 수거 지점 금지 - PlaceCargoPile과 같은 폴백, 래치 없음).
        /// </summary>
        /// <summary>UnderwaterCaveSpawner가 동굴 내부 조개에 그대로 재사용한다(0.2.38에서
        /// private → internal 승격, 본문·시그니처 무변경).</summary>
        internal static void PlaceClam(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale, int pearlCount, int clamIndex)
        {
            Mesh shell = clamPrimary[variant];
            if (shell == null)
                return; // 이 변종만 아직 안 로드됨 - 조용히 건너뛴다(다음 월드에서 복구)

            // 껍데기 = 모래빛 크림/베이지, 진주 = 밝은 진주광(살짝 분홍끼 흰색).
            // 산호/화물과 같은 GetMaterial 공유 캐시 규약이라 조개가 몇 개든 머티리얼은 2장이다.
            Material shellMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.86f, 0.79f, 0.63f), "noise");
            Material pearlMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.97f, 0.91f, 0.93f), "noise");

            // 조개 루트. "PearlClam_"은 "Island_"로 시작하지 않으므로 TerrainSampler 지형 판정에서
            // 구조적으로 제외된다(SunkenCargo/SeaRock 콜라이더와 같은 안전 근거).
            var clam = new GameObject("PearlClam_" + clamIndex);
            clam.transform.SetParent(root, false);
            // 밑면 y=0 접지 모델을 모래 기복 속에 살짝 묻는다(PlaceCoral과 같은 이유).
            clam.transform.localPosition = worldPos - islandCenter + new Vector3(0f, -0.04f, 0f);

            var part = CreateVisualPart(clam.transform, "Clam_" + ClamModelNames[variant], shell,
                shellMaterial, Vector3.zero, yaw, scale);
            var renderer = part.GetComponent<MeshRenderer>();
            if (clamSecondary[variant] != null)
            {
                // 개별 메시 임포트: 진주를 같은 위치/회전/스케일의 파츠로 하나 더(임포터 동작 방어).
                CreateVisualPart(clam.transform, "Clam_" + ClamModelNames[variant] + "_pearl",
                    clamSecondary[variant], pearlMaterial, Vector3.zero, yaw, scale);
            }
            else if (renderer != null && shell.subMeshCount >= 2)
            {
                // 병합 임포트(Unity 6.5 실동작): 서브메시 순서는 OBJ `o` 순서(shell → pearl)다.
                renderer.sharedMaterials = new[] { shellMaterial, pearlMaterial };
            }

            // 상호작용 콜라이더: 조개 루트의 BoxCollider(실측 크기 + 여유 패딩, 조준 가능한 최소
            // 크기 보장). 레이가 맞으면 GetComponentInParent로 같은 오브젝트의 AirlinerSalvagePoint가
            // 잡힌다(SunkenCargo와 같은 경로).
            Vector3 worldSize = ClamModelSizes[variant] * scale;
            var box = clam.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, worldSize.y * 0.5f, 0f);
            box.size = new Vector3(
                Mathf.Max(worldSize.x * 1.4f, 0.55f),
                Mathf.Max(worldSize.y * 1.3f, 0.45f),
                Mathf.Max(worldSize.z * 1.4f, 0.55f));

            // [한계] 수거 여부는 세이브 미저장 - SunkenCargo와 동일한 한계(헤더 주석).
            var salvage = clam.AddComponent<AirlinerSalvagePoint>();
            salvage.displayName = "진주조개";
            salvage.loot = new[] { new AirlinerSalvagePoint.LootEntry("진주", pearlCount) };
        }

        // ── 개별 배치물 조립 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 산호 하나. 병합 임포트(서브메시 2)면 렌더러 하나 + sharedMaterials [body색, tip색],
        /// 개별 메시 2장이면 파츠 2개(각각 한 색). tip은 body색의 Shade 1.35(성장점이 밝다).
        /// </summary>
        private static void PlaceCoral(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh body = coralPrimary[variant];
            if (body == null)
                return; // 이 변종만 아직 안 로드됨 - 조용히 건너뛴다(래치 없음, 다음 월드에서 복구)

            Color bodyColor = CoralPalette[variant % CoralPalette.Length];
            Material bodyMaterial = ResourceVisualLibrary.GetMaterial(bodyColor, "noise");
            Material tipMaterial = ResourceVisualLibrary.GetMaterial(
                ResourceVisualLibrary.Shade(bodyColor, 1.35f), "noise");

            // 밑면 y=0 접지 모델을 모래 기복(±0.6m) 속에 살짝 묻는다 - 가장자리 들뜸 방지.
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.08f, 0f);
            var part = CreateVisualPart(root, "Coral_" + CoralModelNames[variant], body,
                bodyMaterial, localPos, yaw, scale);

            var renderer = part.GetComponent<MeshRenderer>();
            if (coralSecondary[variant] != null)
            {
                // 개별 메시 임포트: tip을 같은 위치/회전/스케일의 파츠로 하나 더(임포터 동작 방어).
                CreateVisualPart(root, "Coral_" + CoralModelNames[variant] + "_tip",
                    coralSecondary[variant], tipMaterial, localPos, yaw, scale);
            }
            else if (renderer != null && body.subMeshCount >= 2)
            {
                // 병합 임포트(Unity 6.5 실동작): 서브메시 순서는 OBJ `o` 순서(body → tip)다.
                renderer.sharedMaterials = new[] { bodyMaterial, tipMaterial };
            }
        }

        /// <summary>
        /// 해초 하나(양면 blade 메시 1장). 기본은 콜라이더 없는 순수 장식이지만, **순수 위치 해시**로
        /// 뽑힌 ~15% 서브셋은 채집 노드("해초 군락", 해조류 2~4)가 된다 - 판정·수량 모두 해시라
        /// rng draw 소비가 0이고, 기존 해초 배치의 추첨 순서·시각(스웨이 셰이더 포함)은 완전 불변이다.
        /// 채집 후에도 시각은 남는다(1회 수거 지점 규약 - SunkenCargo와 동일 한계).
        /// </summary>
        private static void PlaceKelp(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh blade = kelpMeshes[variant];
            if (blade == null)
                return;

            // 해초만 스웨이 셰이더 머티리얼(로드 실패 시 기존 GetMaterial "leaf" 폴백 - GetKelpMaterial).
            Material material = GetKelpMaterial(variant);
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.06f, 0f);
            var part = CreateVisualPart(root, "Kelp_" + KelpModelNames[variant], blade, material,
                localPos, yaw, scale);

            // 채집 서브셋 판정: 배치 좌표는 시드가 정하는 결정적 값이라 해시도 결정적이다.
            if (PositionHash01(worldPos, KelpHarvestSelectSalt) >= HarvestKelpChance)
                return;

            // 상호작용 콜라이더: 해초 파츠 로컬 공간 기준(부모 스케일이 균등이라 함께 커진다 -
            // PlaceRock 콜라이더와 같은 근거). blade 밑동 주변의 좁은 기둥이라 수중 이동을 크게
            // 막지 않고, 이름이 "Kelp_"라 지형 판정("Island_" 필터)에는 구조적으로 안 잡힌다.
            float height = Mathf.Min(KelpModelHeights[variant], 1.6f);
            var box = part.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(0.55f, height, 0.55f);

            // 지급 수량 2~4도 위치 해시(별도 salt)로 결정적 확정 - draw 소비 0.
            int count = 2 + (int)(PositionHash(worldPos, KelpHarvestCountSalt) % 3u);

            // [한계] 수거 여부는 세이브 미저장 - SunkenCargo와 동일한 한계(헤더 주석).
            var salvage = part.AddComponent<AirlinerSalvagePoint>();
            salvage.displayName = "해초 군락";
            salvage.loot = new[] { new AirlinerSalvagePoint.LootEntry("해조류", count) };
        }

        /// <summary>
        /// 수중 바위 하나. 스케일 적용 후 최대 치수가 1.5m를 넘으면 BoxCollider(실측 크기의 85% 폭,
        /// 대략치)를 붙여 잠수 시 실제로 부딪히는 랜드마크로 만든다. 작은 것은 콜라이더 없음.
        /// </summary>
        private static bool PlaceRock(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh mesh = rockMeshes[variant];
            if (mesh == null)
                return false;

            Material material = ResourceVisualLibrary.GetMaterial(
                RockPalette[variant % RockPalette.Length], "rock");
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.10f, 0f);
            var part = CreateVisualPart(root, "SeaRock_" + RockModelNames[variant], mesh,
                material, localPos, yaw, scale);

            Vector3 size = RockModelSizes[variant];
            float maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale;
            if (maxDimension > 1.5f)
            {
                // 파츠 로컬 공간 기준 대략치 - 부모 스케일이 균등이라 콜라이더도 함께 커진다.
                // 이름이 "SeaRock_"이라 지형 판정("Island_" 필터)에는 구조적으로 안 잡힌다.
                var box = part.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = new Vector3(size.x * 0.85f, size.y, size.z * 0.85f);
            }

            return true;
        }

        /// <summary>
        /// 공유 메시 + 공유 머티리얼로 순수 시각 파츠 하나(콜라이더 없는 경로 - CreateMeshPart).
        /// 수중이라 그림자 캐스팅/수신을 모두 끈다(스커트와 같은 규칙).
        /// </summary>
        private static GameObject CreateVisualPart(Transform root, string name, Mesh mesh,
            Material material, Vector3 localPos, float yaw, float scale)
        {
            return CreateVisualPart(root, name, mesh, material, localPos,
                Quaternion.Euler(0f, yaw, 0f), scale);
        }

        /// <summary>침몰 화물처럼 요(yaw) 외 기울기가 필요한 파츠용 회전 지정 변형. 규칙은 동일하다.</summary>
        private static GameObject CreateVisualPart(Transform root, string name, Mesh mesh,
            Material material, Vector3 localPos, Quaternion localRotation, float scale)
        {
            var go = StructureVisualBuilder.CreateMeshPart(root, name, mesh,
                localPos, Vector3.one * scale, localRotation, material);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go;
        }

        // ── 후보 샘플링 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 섬 중심 기준 극좌표 후보를 뽑아 SeabedGenerator.TrySampleSeabed로 접지하고, 깊이
        /// (seaLevel - 해저 y)가 [depthMin, depthMax]에 들면 채택한다. 시도 수는 고정(무한 루프 금지)
        /// 이고, 실패한 후보는 그 후보만 버린다. worldPos.y에는 해저 y가 들어간다.
        /// </summary>
        private static bool TryPickPoint(System.Random rng, Vector3 center, float rMin, float rMax,
            float seaLevel, float depthMin, float depthMax, int maxAttempts,
            out Vector3 worldPos, out float depth)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(rMin, rMax);
                var candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * r, 0f, center.z + Mathf.Sin(angle) * r);

                if (!SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                    continue;

                float d = seaLevel - seabedY;
                if (d < depthMin || d > depthMax)
                    continue;

                worldPos = new Vector3(candidate.x, seabedY, candidate.z);
                depth = d;
                return true;
            }

            worldPos = Vector3.zero;
            depth = 0f;
            return false;
        }

        /// <summary>
        /// 리본형(높이 1.4m 이상)/방석형 중 요청된 형에서 로드된 변종 하나를 뽑는다.
        /// 해당 형이 하나도 안 로드됐으면 -1(호출부가 이 후보만 버린다).
        /// 후보 집계가 결정적(모델 로드는 전부-아니면-일부지만 실측 높이 표는 상수)이라
        /// 같은 시드면 같은 선택이 나온다.
        /// </summary>
        private static int PickKelpVariant(System.Random rng, bool ribbon)
        {
            int count = 0;
            for (int i = 0; i < kelpMeshes.Length; i++)
            {
                if (kelpMeshes[i] != null && (KelpModelHeights[i] >= 1.4f) == ribbon)
                    count++;
            }

            if (count == 0)
                return -1;

            int pick = rng.NextInt(0, count);
            for (int i = 0; i < kelpMeshes.Length; i++)
            {
                if (kelpMeshes[i] == null || (KelpModelHeights[i] >= 1.4f) != ribbon)
                    continue;
                if (pick == 0)
                    return i;
                pick--;
            }

            return -1;
        }

        // ── 로더 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 모델 63종(산호 20 + 해초 10 + 바위 20 + 화물 2 + 조개 3 + 지형지물 8)의 공유 메시를 채운다. ResourceVisualLibrary.TryLoadTwoPartModel(검증된
        /// Load&lt;GameObject&gt;+MeshFilter 로더)을 그대로 쓰고, 프레임당 1회만 프로브하며(같은 프레임의
        /// 섬 50개 생성 루프에서 Load가 50번 반복되지 않게), 실패를 영구 래치하지 않는다.
        /// 산호 OBJ의 `o` 2개는 이름 키워드(trunk/leaf류)에 안 걸리므로 그 로더의 "o 등장 순서" 폴백이
        /// body → tip 순서를 보장하고, 병합 임포트면 첫 메시(서브메시 2)만 오고 tip은 null이다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = false;
            for (int i = 0; i < coralPrimary.Length && !anyMissing; i++)
                anyMissing = coralPrimary[i] == null;
            for (int i = 0; i < kelpMeshes.Length && !anyMissing; i++)
                anyMissing = kelpMeshes[i] == null;
            for (int i = 0; i < rockMeshes.Length && !anyMissing; i++)
                anyMissing = rockMeshes[i] == null;
            for (int i = 0; i < cargoMeshes.Length && !anyMissing; i++)
                anyMissing = cargoMeshes[i] == null;
            for (int i = 0; i < clamPrimary.Length && !anyMissing; i++)
                anyMissing = clamPrimary[i] == null;
            for (int i = 0; i < seaFormMeshes.Length && !anyMissing; i++)
                anyMissing = seaFormMeshes[i] == null;

            if (!anyMissing || probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            for (int i = 0; i < CoralModelNames.Length; i++)
            {
                if (coralPrimary[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + CoralModelNames[i],
                        out Mesh body, out Mesh tip))
                {
                    coralPrimary[i] = body;
                    coralSecondary[i] = tip; // 병합 임포트면 null - PlaceCoral의 서브메시 분기가 처리
                }
            }

            for (int i = 0; i < KelpModelNames.Length; i++)
            {
                if (kelpMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + KelpModelNames[i],
                        out Mesh blade, out _))
                    kelpMeshes[i] = blade;
            }

            for (int i = 0; i < RockModelNames.Length; i++)
            {
                if (rockMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + RockModelNames[i],
                        out Mesh rock, out _))
                    rockMeshes[i] = rock;
            }

            // 침몰 화물 컨테이너(crate_a/barrel_a). `o` 1개짜리 단일 메시라 TryLoadTwoPartModel의
            // "메시 하나면 그것이 trunk" 규칙으로 그대로 온다(두 번째 out은 항상 null - 버린다).
            for (int i = 0; i < CargoModelNames.Length; i++)
            {
                if (cargoMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + CargoModelNames[i],
                        out Mesh cargo, out _))
                    cargoMeshes[i] = cargo;
            }

            // 진주조개(clam_a~c). `o` 2개(shell/pearl)는 산호와 같은 처지다: 이름 키워드
            // (trunk/leaf류)에 안 걸리므로 로더의 "o 등장 순서" 폴백이 shell → pearl 순서를
            // 보장하고, 병합 임포트면 첫 메시(서브메시 2)만 오고 pearl은 null이다(PlaceClam 분기).
            for (int i = 0; i < ClamModelNames.Length; i++)
            {
                if (clamPrimary[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + ClamModelNames[i],
                        out Mesh shell, out Mesh pearl))
                {
                    clamPrimary[i] = shell;
                    clamSecondary[i] = pearl;
                }
            }

            // 해저 지형지물(seaform_a~h). `o` 1개 + usemtl 1종이라 로더의 "메시 하나면 그것이
            // trunk" 규칙으로 단일 메시가 그대로 온다(두 번째 out은 항상 null - 버린다).
            // 이 메시는 렌더 메시이자 관통형(a/d/e)의 비볼록 MeshCollider용 물리 메시다.
            for (int i = 0; i < SeaFormModelNames.Length; i++)
            {
                if (seaFormMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + SeaFormModelNames[i],
                        out Mesh form, out _))
                    seaFormMeshes[i] = form;
            }
        }

        // ── 유틸 ────────────────────────────────────────────────────────────────────

        /// <summary>"Island_{id}_{size}"에서 islandId를 파싱한다(이름은 SpawnPlaceholder가 붙인다).
        /// 파싱 실패(placeholder 프리팹 등 비표준 이름)면 0 - 그래도 worldSeed 격리는 유지된다.</summary>
        private static int ParseIslandId(string islandName)
        {
            if (string.IsNullOrEmpty(islandName))
                return 0;
            string[] tokens = islandName.Split('_');
            if (tokens.Length >= 2 && int.TryParse(tokens[1], out int id))
                return id;
            return 0;
        }

        /// <summary>
        /// 배치 위치 → 결정적 해시. IslandMeshGenerator.MeshLibrary.DecorationPositionHash의 사본이다
        /// (그쪽은 private라 참조할 수 없다 - RockModelSizes와 같은 사본 정책. 알고리즘 근거는 그쪽
        /// [B50] 주석: 0.1m 양자화 + 소수 곱 + xorshift-곱 마무리로 이웃 위치 상관 제거).
        /// rng 소비 0 - 순수 함수라 기존 어떤 추첨 순서에도 영향이 없다.
        /// </summary>
        private static uint PositionHash(Vector3 worldPosition, uint salt)
        {
            unchecked
            {
                int qx = Mathf.RoundToInt(worldPosition.x * 10f);
                int qz = Mathf.RoundToInt(worldPosition.z * 10f);
                uint h = (uint)(qx * 73856093) ^ (uint)(qz * 19349663) ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>위 해시를 [0,1) 실수로(채집 해초 당첨 판정용 - DecorationPositionHash01의 사본).</summary>
        private static float PositionHash01(Vector3 worldPosition, uint salt)
        {
            return (PositionHash(worldPosition, salt) & 0xFFFFFFu) / (float)0x1000000;
        }

        /// <summary>섬 크기별 배치 스케일. 반지름 경계(50/90/140/200)의 중간값으로 가른다.</summary>
        private static float SizeScale(float radius)
        {
            if (radius < 70f) return 0.7f;   // Small
            if (radius < 115f) return 1.0f;  // Medium
            if (radius < 170f) return 1.4f;  // Large
            return 1.8f;                     // ExtraLarge
        }
    }
}
