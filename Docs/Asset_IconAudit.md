# 아이템 아이콘/GUID 감사 (#17)

- 감사 대상: `Assets/_Project/ScriptableObjects/Item_*.asset` 31개, `Recipes/Recipe_*.asset` 11개, `IslandSpawnConfig_Default.asset` 1개
- 감사 방식: 파일 읽기만 수행(수정 없음). 디렉터가 제공한 아이콘 GUID 표(31개, `.png.meta` 미스테이징이라 이 표를 근거로 사용) vs 각 `Item_*.asset`의 `icon: {fileID, guid}` 필드를 스크립트로 전수 대조.
- 결론 먼저: **조치가 필요한 항목 없음.** 31개 아이템 전부 이름이 일치하는 아이콘에 정확히 연결되어 있다. 앞서 온보딩 때 "최우선 점검 대상"으로 지목했던 우려는 이번 감사로 해소됐다(스테이징 안 됐던 SO를 직접 열어보기 전까지는 확정할 수 없던 부분이었다).

## 판정 요약
- 정상 31 / 미연결 0 / 오연결 0
- 아이콘 31개 vs 아이템 31개 — 남거나 모자란 쪽 없음 (1:1 완전 대응, 중복 사용된 아이콘 GUID도 없음)

## 조치 필요 (있으면)

없음. 아래는 전수 대조 결과(참고용 원표).

| 아이템 | icon.guid (asset 내) | 기대 GUID(Icon_아이템명) | 판정 |
|---|---|---|---|
| 고무보트 | 5d01a46405ecf36780077af131c6ef3c | 5d01a46405ecf36780077af131c6ef3c | 정상 |
| 구운고기 | 89a8a65d8e4c4fb13f4fe59da251eb29 | 89a8a65d8e4c4fb13f4fe59da251eb29 | 정상 |
| 구운생선 | d4446ed0623c200cafcf92318c481202 | d4446ed0623c200cafcf92318c481202 | 정상 |
| 금속조각 | dc9c6e7f88f66ef9103721d98615b979 | dc9c6e7f88f66ef9103721d98615b979 | 정상 |
| 나뭇가지 | fd1bd84cfecabe1a503612fab0098b6d | fd1bd84cfecabe1a503612fab0098b6d | 정상 |
| 노끈 | 07e38efc9eebc2454ce0019ac4137803 | 07e38efc9eebc2454ce0019ac4137803 | 정상 |
| 대나무 | 447e3ce0f2fda52cc51732d74f3bbc06 | 447e3ce0f2fda52cc51732d74f3bbc06 | 정상 |
| 돌조각 | 4d2b909b038bca71fb03a9b038bf02fe | 4d2b909b038bca71fb03a9b038bf02fe | 정상 |
| 라이터 | cd9984997dcea306e1a61fa2cac94f05 | cd9984997dcea306e1a61fa2cac94f05 | 정상 |
| 모닥불키트 | 6c6366dcc3f618311fc6f67dd8d62e2d | 6c6366dcc3f618311fc6f67dd8d62e2d | 정상 |
| 물증류기키트 | 7fcc7bdaaa031d5053a2b676ef6dcf63 | 7fcc7bdaaa031d5053a2b676ef6dcf63 | 정상 |
| 물통 | 2eb385d6732136e1525e16dff9285e8e | 2eb385d6732136e1525e16dff9285e8e | 정상 |
| 부력통 | c7a0b1a690228104e36db146f0d4ad95 | c7a0b1a690228104e36db146f0d4ad95 | 정상 |
| 부목 | 4dea26465d9c88a7a188d83bd5eded83 | 4dea26465d9c88a7a188d83bd5eded83 | 정상 |
| 부싯돌 | d74005344b6d5e0af76095fc887693b1 | d74005344b6d5e0af76095fc887693b1 | 정상 |
| 붕대 | 0f6afb9c0ada5743e1339f35339ac104 | 0f6afb9c0ada5743e1339f35339ac104 | 정상 |
| 비상식량 | a03169bb121f41a5bfc9fa348659e9ee | a03169bb121f41a5bfc9fa348659e9ee | 정상 |
| 생고기 | 03cb0630ba25bd06599db8b0d7fdba8e | 03cb0630ba25bd06599db8b0d7fdba8e | 정상 |
| 생선 | 75bffcd221ab2dbd1a285a1af40c30df | 75bffcd221ab2dbd1a285a1af40c30df | 정상 |
| 생수 | 1fc492dab1056eb514f4804a2c4aa942 | 1fc492dab1056eb514f4804a2c4aa942 | 정상 |
| 손도끼 | 978dd6cf290a81da8e34755ad3654b65 | 978dd6cf290a81da8e34755ad3654b65 | 정상 |
| 쉼터키트 | f54a78ed0b778b89cf6277b19a131433 | f54a78ed0b778b89cf6277b19a131433 | 정상 |
| 야자잎 | d2e7c75dbc449dfdaab96ab795aefe59 | d2e7c75dbc449dfdaab96ab795aefe59 | 정상 |
| 엔진부품 | d68372a1e32979b34083028204d69d17 | d68372a1e32979b34083028204d69d17 | 정상 |
| 연료 | e4020ede5b7054c8bc7bcea7a2c6d0e3 | e4020ede5b7054c8bc7bcea7a2c6d0e3 | 정상 |
| 창 | f3ddccc1c81ffa1ee8105a8c1a3b92ab | f3ddccc1c81ffa1ee8105a8c1a3b92ab | 정상 |
| 천조각 | d1973b362a467ef770cf8f0a96fceb2e | d1973b362a467ef770cf8f0a96fceb2e | 정상 |
| 칼 | 394ac2a9eb802d16960524ee0cedb339 | 394ac2a9eb802d16960524ee0cedb339 | 정상 |
| 코코넛 | 5d5cb6c34afeca1abcb2f69eea58be71 | 5d5cb6c34afeca1abcb2f69eea58be71 | 정상 |
| 파이어스타터 | 0895bf4140962763a2d95250a6edd5dd | 0895bf4140962763a2d95250a6edd5dd | 정상 |
| 해독제 | ccb5114b290e2572e2a73ae283f0dffb | ccb5114b290e2572e2a73ae283f0dffb | 정상 |

추가로 각 `Item_*.asset`의 `icon:` 필드는 전부 `fileID: 21300000, type: 3`(Sprite 서브에셋) 형식으로 일관되어 임포터 설정 상 오류(Texture 타입인데 Sprite로 슬롯팅되는 등) 징후도 없었다.

## 부수 확인: 설치형 키트의 `placementPrefab` (아이콘은 아니지만 같은 파일에서 함께 확인됨)
- `Item_모닥불키트.asset` → `placementPrefab` guid `c7d2b8a94e6f4d3ab1f0e9c6a5d8b3f2` (Campfire.prefab로 추정, 프리팹 자체 GUID는 미스테이징이라 이름 대조 불가 — **미확정**)
- `Item_물증류기키트.asset` → `placementPrefab` guid `313230279933cee49a30fa72ed1aea5d` (WaterStill.prefab로 추정 — **미확정**)
- `Item_쉼터키트.asset` → `placementPrefab` guid `60ec241f8f0048548aff55b5ac6526e9` (Shelter.prefab로 추정 — **미확정**)
세 값 모두 빈 값(`fileID: 0`)이 아니라 실제 GUID가 채워져 있어 최소한 "연결은 되어 있음"은 확인. 실제로 맞는 프리팹을 가리키는지는 `Prefabs/*.prefab.meta`를 대조해야 확정 가능(이번 스테이징 범위 밖).

## GUID → 아이템명 확정 매핑표

아이템 SO 자신의 GUID(`.asset.meta`)는 스테이징에 없어 직접 읽을 수 없다. 대신 레시피의 `requiredMaterials`/`resultItem`이 그 아이템을 참조할 때 쓰는 GUID와, 각 레시피 `description`에 명시된 재료 이름 순서(예: "나뭇가지와 돌조각으로" → requiredMaterials 배열도 항상 이 순서와 일치)를 교차 대조해 확정했다. 11개 레시피 전부에서 설명 문구 순서와 재료 배열 순서·수량이 예외 없이 일치해(교차 검증 통과) 아래 매핑을 확정으로 분류한다.

| GUID | 아이템명 | 근거 |
|---|---|---|
| 519258073502428085c5b0ec1011eb9d | 나뭇가지 | Recipe_모닥불키트/손도끼/창 3곳에서 "나뭇가지와 돌조각" 순서와 재료 배열·수량이 모두 일치 |
| 5d951f1b7640476989632c5784092378 | 돌조각 | 위와 동일 3곳 + Recipe_파이어스타터("부싯돌과 돌조각") 순서 일치, 총 4곳 교차 검증 |
| dce46f1400c042da94b13073a17e1b11 | 대나무 | Recipe_물증류기키트/물통/부목/쉼터키트 4곳에서 "대나무, ..." 순서 일치 |
| 4dfa08c680734e5abc2e6ed6904ac0c1 | 노끈 | Recipe_노끈의 resultItem + 물증류기키트/물통/부목/쉼터키트 4곳의 재료 순서 일치 (총 5곳) |
| 3f55ff4a55f7414b891a323c5e7c303a | 야자잎 | Recipe_노끈("야자잎을 꼬아") 재료 + Recipe_쉼터키트("... 야자잎 ...") 순서·수량(6) 일치 |
| b86d36da6d5b45bba13e7d0c24e6a66e | 천조각 | Recipe_물증류기키트/붕대("천조각으로 붕대를")/해독제("... 천조각으로") 3곳 순서 일치 |
| e6d70fced200407eb31b08731cd6db82 | 부싯돌 | Recipe_파이어스타터("부싯돌과 돌조각") 순서 일치 |
| 0b631157c4864eb3adb1b0bf99ba09da | 코코넛 | Recipe_해독제("코코넛과 천조각") 순서 일치 |
| f83e11fa1f604ceabd6de3ff30e93819 | 구운고기 | Item_생고기.asset의 `cookedResult` 필드가 이 GUID를 가리키고, 설명상 생고기를 구우면 구운고기가 되므로 확정 |
| 000803e45e0b49fba685b6d469cde39c | 구운생선 | Item_생선.asset의 `cookedResult` 필드가 이 GUID를 가리킴 |
| 7a3d5e91c4b64f2e9d0a1b8c6e4f2a90 | 모닥불키트 | Recipe_모닥불키트.resultItem |
| 25fad7a6c7d84ed9809d1dc8458dc0d9 | 물증류기키트 | Recipe_물증류기키트.resultItem |
| 4427ef709e804d3487fb24440b9995f7 | 물통 | Recipe_물통.resultItem |
| 91d3649162b446f4bd856b5d6f3ac4d0 | 부목 | Recipe_부목.resultItem |
| 3e5f88728e114bd689d243715c2450b0 | 붕대 | Recipe_붕대.resultItem |
| e78c2cccce274d2fac0d814667672602 | 손도끼 | Recipe_손도끼.resultItem |
| b538f6f80f3342c99094b7acef7b4779 | 쉼터키트 | Recipe_쉼터키트.resultItem |
| 53f26fad42714f3eb765334ed469db71 | 창 | Recipe_창.resultItem |
| 8883e182994d44b4bde936ddb9ff29f7 | 파이어스타터 | Recipe_파이어스타터.resultItem |
| 4a241ab5170140758aae80e111c89ccf | 해독제 | Recipe_해독제.resultItem |

**확정 20개.** (참고: 위 표의 GUID는 각 아이템 ScriptableObject 자신의 에셋 GUID이며, 이번 배치 표두에 있던 "아이콘 GUID"와는 별개의 값이다 — 혼동 주의.)

## 확정 못 한 것 + 이유

다음 11개 아이템은 어떤 레시피의 재료·결과물로도 등장하지 않고, 다른 SO의 참조 필드(`cookedResult`, `placementPrefab` 등)에도 걸리지 않아 **이번에 스테이징된 자료만으로는 자신의 GUID를 확정할 방법이 없었다**(전부 채집/드랍 전용 원료이거나 시작 지급 아이템이라 레시피 결과물이 아니고, 다른 아이템이 재료로 요구하지도 않음 — 즉 "참조당하는 지점" 자체가 이 11개 파일 세트 안에 없음):

- 고무보트, 금속조각, 라이터, 부력통, 비상식량, 생고기, 생선, 생수, 엔진부품, 연료, 칼

확정하려면 다음 중 하나가 필요:
1. 각 `Item_*.asset.meta` 파일(미스테이징)을 직접 여는 것
2. 이 11개를 참조하는 다른 SO/씬/프리팹(예: `CreatureEntry.yieldItem`이 생고기/생선을 참조하는 `CreatureSpawner` 인스펙터 값, `HazardSpawner`나 시작 인벤토리 설정 등 — 코드가 아니라 씬/프리팹에 직렬화된 값)을 스테이징받는 것

추측(예: "금속조각 GUID는 아마 이럴 것이다")은 하지 않았다 — 위 사유로 "미확정"으로만 남긴다.
