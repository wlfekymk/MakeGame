# B2-8 도면 배치 반경

## 목적
`BoatBlueprintSpawner.placementOffset = 4`(고정, 모든 섬 규모 동일)이 특대 섬(지형 반지름 200)
에서도 그대로 적용돼, 도면이 항상 섬 중심 4m 안에 생성된다. 규모별로 늘릴지, 고정 유지가
의도된 설계인지 결정한다.

## 결정: **규모별 반경으로 변경한다.**

### 근거
1. **"찾기 쉬워서 좋다"로 보기엔 사실상 탐색 자체가 사라진다.** `IslandTravel.TeleportPlayerToIsland`
   (IslandTravel.cs:83-98)는 플레이어를 항상 `destination.mapPosition + (2, 0, 2)`에 착지시킨다.
   즉 플레이어는 섬에 도착하는 순간 이미 도면 스폰 기준점(mapPosition)에서 반경 약 2.8m 안에
   서 있고, 도면도 같은 mapPosition 기준 반경 4m 안에 스폰된다 — **도착 즉시 시야에 들어오거나
   몇 걸음 안에 발견되는 것이 사실상 보장**된다. `BoatBlueprintSpawner.cs:19` 자체 주석이
   "완전히 1로 고정하지 않은 이유는 약간의 탐험 긴장감을 남기기 위함"이라고 스폰 확률(0.9/0.95)
   설계 의도를 밝히고 있는데, 배치 반경이 4m 고정이라 그 "탐험 긴장감"이 **스폰 여부의 확률
   긴장감**으로만 남고 **찾는 과정의 탐험**은 전혀 존재하지 않는다. 대형/특대 섬을 굳이 만들어
   놓고도 그 넓은 면적을 걸어볼 이유가 도면 찾기에서는 사라진다.
2. **다른 콘텐츠(자원/위험요소)는 이미 섬 규모에 비례해 흩뿌려지도록 설계돼 있다** (B2-4).
   도면만 예외로 고정 반경을 유지할 근거가 없다 — 오히려 "배 엔딩으로 가는 핵심 오브젝트"이니만큼
   섬을 실제로 둘러보게 만드는 편이 대형/특대 섬 방문의 목적성과 맞는다.
3. 단, 도면은 **잃어버리면 안 되는 유일 오브젝트**이므로 일반 자원 산포 반경(112/160)만큼 넓게
   흩뿌리면 "운 나쁘면 섬 반대편까지 뒤져야 하는" 과도한 탐색 부담이 된다. 스폰 확률이 이미
   0.9~0.95로 높게 잡혀 있어(놓칠 위험 자체는 낮게 설계됨) 배치 반경까지 함께 넓히면 리스크가
   중첩된다. 따라서 일반 자원 반경보다는 작게, 하지만 착지 지점보다는 확실히 멀게 잡는다.

## 수치 (확정)
| 규모 | placementOffset |
|---|---|
| Large (지형 반지름 140) | **30m** |
| ExtraLarge (지형 반지름 200) | **45m** |

근거: 각각 해당 규모의 일반 자원 scatterRadius(B2-4 결정 기준 112 / 160)의 약 27~28%에 해당하는
값으로, "도착하자마자 바로 보인다"는 4m와 "섬 전체를 다 뒤져야 한다"는 112~160m의 중간 지점을
택했다. 도보로 30~45m는 대략 20~30초 이동 거리로, 착지 지점 주변을 한 바퀴 둘러보는 정도의
가벼운 탐색을 유도하되 좌절스러운 수준의 수색은 아니다.

Small/Medium 섬에는 도면이 스폰되지 않으므로(`SpawnBlueprintForIsland`가 Large/ExtraLarge만
처리) 해당 규모의 값은 정의하지 않는다.

## 수정 대상 파일
- `Assets/_Project/Scripts/Systems/BoatBlueprintSpawner.cs`
  - `placementOffset` 단일 필드를 `largePlacementOffset = 30f` / `extraLargePlacementOffset = 45f`
    두 개로 분리하고, `SpawnBlueprintForIsland`에서 `island.size`에 따라 선택하도록 변경.
- `Assets/Scenes/SampleScene.unity`의 `BoatBlueprintSpawner` 컴포넌트 — 필드가 분리되므로 씬 값도
  새 필드명에 맞춰 재설정 필요(기존 `placementOffset: 4` 값은 그대로 두면 안 됨).

## 수용 기준
1. 대형 섬에서 도면이 mapPosition 기준 반경 30m 안, 특대 섬에서는 45m 안에 무작위 배치됨.
2. 착지 지점(mapPosition + (2,2))에서 도면이 항상 바로 보이지는 않되, 몇 걸음~30초 내 도달
   가능한 거리 안에는 항상 위치함(방향을 못 찾아 영구히 헤매는 일은 없음 — TerrainSampler로
   지형 위에 스냅되므로 바다에 빠지는 문제도 없음, 기존 로직 유지).

## 담당 제안
- 코드 수정: systems-engineer (`BoatBlueprintSpawner.cs` 필드 분리)
- 씬 값 반영: unity-operator/systems-engineer
- 체감 난이도 검증(선택): qa-reviewer 또는 unity-operator 플레이 스루
