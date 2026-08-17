# MaldivesMap — 유니티용 몰디브 지형 데이터

실제 몰디브 지형(섬 1,296개 + 환초 32구역)을 유니티에서 바로 쓸 수 있게 재구성한 패키지입니다.
지명은 넣지 않았고 좌표와 ID만 있습니다.

```
MaldivesMap/
├── Data/
│   ├── maldives_meshes.bytes    삼각분할까지 끝난 바이너리 (496 KB) ← 권장
│   ├── maldives_map.json        같은 내용의 JSON (817 KB, JsonUtility 호환)
│   ├── maldives_shapes.csv      도형 목록 (ID·월드좌표·위경도·면적)
│   └── maldives_game_map.geojson  원본 WGS84 GeoJSON (외부 GIS 도구용)
├── Scripts/
│   ├── MaldivesGeo.cs           위경도 ↔ 월드 좌표 변환
│   ├── MaldivesMapData.cs       JSON 스키마 (직렬화 클래스)
│   ├── MaldivesMeshPack.cs      .bytes 리더
│   ├── MaldivesMeshBuilder.cs   Mesh 생성 (평면 / 압출 / 외곽선)
│   ├── MaldivesMapLoader.cs     씬에 지형을 까는 메인 컴포넌트
│   └── Editor/MaldivesMapMenu.cs  Tools ▸ Maldives 메뉴
├── maldives_map.html            참고용 인터랙티브 뷰어 (브라우저)
└── README.md
```

---

## 1. 설치

폴더째로 `Assets/MaldivesMap/` 아래에 넣으세요. 경로를 그대로 쓰면 에디터 메뉴가 데이터를 자동으로 찾습니다.

```
Assets/MaldivesMap/Data/maldives_meshes.bytes
Assets/MaldivesMap/Scripts/...
```

> `.bytes` 확장자는 유니티가 바이너리 TextAsset 으로 읽기 위한 것입니다. 이름을 바꾸지 마세요.

## 2. 30초 만에 띄우기

메뉴 **Tools ▸ Maldives ▸ Create Map In Scene**

씬에 `Maldives Map` 오브젝트가 생기고 그 아래에 `Zones`(환초 32개)와 `Islands`(섬 1,296개)가 깔립니다.

수동으로 하려면: 빈 GameObject → `MaldivesMapLoader` 추가 → `Mesh Pack`에
`maldives_meshes.bytes` 지정 → 인스펙터 우클릭 **Build Now**.

## 3. 좌표계

| 항목 | 값 |
|---|---|
| 투영 | 국지 등거리(local equidistant) |
| 월드 원점 (0,0,0) | 73.15°E, 3.20°N — 열도 한가운데 |
| 단위 | 1 유닛 = 1 m (기본값) |
| 축 | XZ 평면 (X=동, Z=북, Y=높이). `axis`를 XY로 바꾸면 2D용 |
| 전체 범위 | X −57 km ~ +68 km, Z −432 km ~ +432 km |

```csharp
using Maldives;

Vector3 male = MaldivesGeo.LatLonToWorld(4.1755, 73.5093, 0f);  // 말레
Vector2 ll   = MaldivesGeo.WorldToLatLon(transform.position);   // x=위도, y=경도
double km    = MaldivesGeo.DistanceMeters(4.1755, 73.5093, -0.6934, 73.1556) / 1000.0;  // 539.82
string cell  = MaldivesGeo.CellName(4.1755, 73.5093);           // 10 km 격자 셀 이름
```

정확도는 WGS84 측지선 거리와 대조해 검증했습니다.

| 구간 | 이 투영 | 실제 측지선 |
|---|---|---|
| 말레 ↔ 아두 간 | 539.81 km | 539.82 km |
| 말레 ↔ 하니마두 | 286.50 km | 286.53 km |

### 스케일 조절

실제 크기(남북 820 km)가 부담되면 `Units Per Meter`를 낮추세요.

- `1` — 실측. 1인칭·비행 게임처럼 실제 거리감이 필요할 때
- `0.001` — 전체가 820 유닛. 전략·항해 게임에서 지도 전체를 한 씬에 올릴 때

### float 정밀도

각 섬 GameObject는 **자기 중심점을 Transform 위치**로 갖고, 메시 정점은 그 기준의 로컬 좌표(±2 km 이내)입니다.
그래서 남쪽 끝(월드 Z ≈ −432,000)에 있는 섬도 정점 정밀도가 밀리미터 단위로 유지됩니다.
카메라를 멀리 옮길 때만 floating origin을 따로 신경 쓰면 됩니다.

## 4. ID 규칙

- 환초 구역: `Z01` ~ `Z32` — 북쪽부터 남쪽 순
- 섬: `Z15-076` — 소속 환초 + 그 안에서 북쪽부터 번호

생성된 오브젝트마다 `MaldivesFeature` 컴포넌트가 붙어 `id / zone / latitude / longitude / areaKm2`를 들고 있습니다.
`refName`에는 참고용 실제 환초 이름이 들어 있지만(예: `North Ari`) 화면에는 쓰이지 않습니다.

```csharp
var f = hit.collider.GetComponent<MaldivesFeature>();
Debug.Log($"{f.id} · {f.latitude:F4}, {f.longitude:F4} · {f.areaKm2} km² · {f.CellName}");
```

## 5. MaldivesMapLoader 주요 옵션

| 옵션 | 설명 |
|---|---|
| `Mesh Pack` / `Json Data` | 둘 중 하나만 지정. `.bytes`가 훨씬 빠릅니다 |
| `Axis` | `XZ`(3D) 또는 `XY`(2D) |
| `Units Per Meter` | 월드 스케일 |
| `Min Island Area Km2` | 작은 섬 걸러내기. `0.01`이면 1 ha 미만 제외 (약 500개 감소) |
| `Island Extrude Meters` | 섬을 입체로 밀어 올리는 높이. `0`이면 평면 한 장 |
| `Zone Plane Meters` | 석호 면 높이. 보통 섬보다 살짝 아래 |
| `Add Mesh Colliders` | 섬에 MeshCollider 부착 (클릭·레이캐스트용) |
| `Flip Winding` | 면이 뒤집혀 보이면 체크 해제 |

### 메시를 에셋으로 굽기

**Tools ▸ Maldives ▸ Bake Meshes To Assets** 를 누르면 1,328개 메시가
`Assets/MaldivesMap/GeneratedMeshes/` 에 `.asset` 으로 저장됩니다.
런타임 파싱 없이 프리팹에 직접 물릴 수 있습니다. (몇 분 걸리고 용량이 늘어납니다.)

## 6. 데이터 형식

### maldives_map.json

`JsonUtility.FromJson<MaldivesMapData>()` 로 바로 읽힙니다. JsonUtility가 중첩 배열을 못 다루므로
링과 정점을 1차원으로 펼쳐 두었습니다.

```jsonc
{
  "shapes": [{
    "id": "Z15-076",
    "kind": "island",          // "island" | "zone"
    "zone": "Z15",
    "originX": 43210.5,        // 월드 좌표(m) — GameObject 위치로 사용
    "originZ": 112233.4,
    "lat": 4.22036, "lon": 73.54250,
    "areaKm2": 3.9429,
    "sizeX": 2130.0, "sizeZ": 1880.0,
    "ringStarts": [0],         // 각 외곽 링이 verts에서 시작하는 정점 인덱스
    "verts": [ -120.4, 88.1, ... ],   // origin 기준 로컬 (x,z) 쌍, 미터
    "tris":  [ 0, 5, 6, ... ]         // 삼각분할 인덱스 (CCW)
  }]
}
```

### maldives_meshes.bytes

리틀엔디언 바이너리. 내용은 위와 같고 파싱이 빠릅니다.

```
"MLDV" | uint32 version | uint32 shapeCount
shape:  uint32 idLen | utf8 id
        byte kind(0=island,1=zone) | float32 originX | float32 originZ
        uint32 ringCount  | uint32[] ringStarts
        uint32 vertCount  | float32[vertCount*2] xz
        uint32 indexCount | uint32[] tris
```

삼각형은 CCW(수학 표준)로 저장되어 있고, `MaldivesMeshBuilder`가 유니티용으로 한 번 뒤집습니다.

## 7. 원본 데이터와 검증

- 해안선·행정 경계: [geoBoundaries](https://github.com/wmgeolab/geoBoundaries) (OpenStreetMap 기반)
- 산호초 라인: [Natural Earth 10m Reefs](https://github.com/nvkelso/natural-earth-vector)

확인한 것:

- 말레·아두 간·하니마두·푸바물라 좌표를 실측치와 대조 — 오차 130~450 m (폴리곤 중심점 기준)
- 섬 면적 합계 200.8 km² (측지 계산값 200.5 km², 투영 오차 0.15%)
- 도형 1,328개 전부 링 방향 CCW, 인덱스 범위 정상, 퇴화 삼각형 제거 완료
- C# 스크립트 컴파일 및 18개 항목 단위 테스트 통과 (좌표 왕복, 거리, 메시 정점·인덱스 수, XY/XZ 모드, 스케일)

원본 데이터에서 **푸바물라(Fuvahmulah) 섬이 누락**되고 해당 환초가 9 km 남쪽으로 어긋나 있어 별도로 보정했습니다.

## 8. 알아둘 점

- 섬은 **평면 폴리곤**입니다. 고도 데이터는 없습니다 (실제로도 최고점이 해발 2.4 m인 나라입니다).
  기복이 필요하면 `Island Extrude Meters`로 두께를 주거나 노이즈를 얹으세요.
- 환초(zone) 폴리곤은 행정 경계에서 온 것이라 실제 산호초 링과 몇 백 m 차이가 날 수 있습니다.
  석호 영역 표시용으로 쓰기엔 충분하지만 정밀한 리프 지형에는 맞지 않습니다.
- 섬 1,296개를 전부 GameObject로 만들면 드로우콜이 많습니다.
  `Min Island Area Km2`로 거르거나, 정적 배칭 / GPU 인스턴싱을 켜는 것을 권합니다.
