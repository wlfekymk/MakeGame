# 실사감 고도화 계획 — 잔디 · 파도 · 날씨

조사일 2026-08-20 / 대상 Unity 6.5(6000.5.6f1) · URP 17.x
근거 자료는 문서 끝 [출처] 참조. 현재 구현 상태는 코드를 직접 읽고 확인했다.

---

## 0. 결론 먼저

**지금 우리 게임에 없는 것 중, "실사처럼 보이는가"를 가장 크게 가르는 것은 파도 모델도 잔디 개수도 아니다.**
조사 결과 세 영역 모두에서 같은 답이 나왔다.

| 영역 | 전문가들이 꼽는 1순위 | 우리 현황 |
|---|---|---|
| 잔디 | 균일하지 않은 밀도 + **눈에 보이는 돌풍이 지나가는 바람** | 바람 세기가 상수 1.0 고정, 날씨와 무연동 |
| 바다 | 깊이 기반 색 + **굴절** | 굴절 없음(알파 블렌딩만) |
| 하늘 | **구름 그림자**와 **높이 기반 산란 안개** | 둘 다 없음 |

세 가지의 공통점: **파도를 FFT로 바꾸거나 볼류메트릭 구름을 넣는 것보다 훨씬 싸고, 효과는 더 크다.**
Subnautica 팀이 남긴 말이 이 문서 전체의 기준이다 — 물리적으로 정확한 값은 칙칙하고 예쁘지 않다.
목표는 "정확함"이 아니라 "진짜처럼 보임"이다.

---

## 0.5 진행 상황 (2026-08-20)

| 파도 | 내용 | 버전 | 상태 |
|---|---|---|---|
| **B** | 전역 바람 (WindSystem) | 0.2.59 | ✅ 검증·커밋 (`3d2c378`) |
| **C1·C3** | 구름 + 구름 그림자 (SkySystem · MGCloudDome) | 0.2.60 | ✅ 검증·커밋 (`3f630e8`) |
| **C2** | 고도 연동 안개 | 0.2.61 | ⏳ 코드 완료, **컴파일 검증 전** |
| **F1·F2·F4** | 잔디 매크로 변주 · 지형 정렬 · 밑동 AO | 0.2.61 | ⏳ 코드 완료, **컴파일 검증 전** |
| **D3** | 파도 마루 투과광 | 0.2.61 | ⏳ 코드 완료, **컴파일 검증 전** |
| A1·A2 | 포스트프로세싱 볼륨 일원화 · 죽은 데이터 정리 | — | 미착수 |
| D1·D2 | 바다 굴절 · 하늘 반사 | — | 미착수 |
| E | 젖음 전면화 · 웅덩이 | — | 미착수 |
| F3 | 크리처 밟힘 | — | 미착수 |

### 0.2.61 적용 절차 (검증이 남아 있다)

`D:\MakeGame\_incoming\wave0261.tgz` 에 있고 **아직 Assets에 풀지 않았다.**
에디터는 검증이 끝난 0.2.60 상태 그대로다. 순서는 평소와 같다:
압축 풀기 → Ctrl+R → 콘솔 에러 0 → 스모크 → 육안 → 커밋.

육안으로 볼 것 세 가지:
1. **잔디** — 경사면에서 카드가 지면을 따라 눕는가(예전에는 전부 수직이었다).
   멀리서 볼 때 구역마다 색이 다른 큰 얼룩이 보이는가. 밑동이 검게 뭉개지지는 않았는가.
2. **안개** — 언덕에 올라가면 수평선이 트이고 물가로 내려오면 다시 뿌예지는가.
3. **바다** — 해를 마주 보고 볼 때 거친 파도의 서 있는 면이 청록으로 타오르는가.
   잔잔한 날에는 **한 점도 안 나와야** 한다(seaRough를 곱해 뒀다).

### 0.2.61에 함께 들어간 감사 수정 2건 (실사감과 무관한 별개 결함)

- **`EndingChecker`** — 경비행기 엔딩의 최소 경과 일수만 `SurvivalBalanceConfig` 폴백 배선이
  빠져 있었다. 예전 주석은 "config에 대응 필드가 아직 없다"고 적혀 있었는데 그 필드는 나중에
  추가됐고 주석만 낡은 채 남아 배선이 마저 안 됐다. 지금은 코드 기본값(8)과 config 값(8)이
  우연히 같아 티가 안 났지만, 밸런스 파일에서 그 값만 바꾸면 **아무 일도 일어나지 않는** 상태였다.
- **`SkySystem.UpdateDome`** — `Camera.main`(태그 검색)을 캐시 없이 매 프레임 불렀다.
  이 프로젝트의 다른 시스템 24곳이 전부 캐시 규약을 지키는데 방금 쓴 이 한 곳만 벗어나 있었다.

### 0.2.61 정적 검수에서 잡아 고친 것 (컴파일러 없이 리뷰로만 잡은 것)

- **밑동 AO 이중 감쇠**: `_RootColor`가 이미 지면색의 어두운 변주라 밑동/끝 명도비가 0.85다.
  거기에 AO 0.62를 더 곱하면 최종 0.5까지 떨어져 밑동이 검게 뭉갠다. 하한을 0.80으로 올렸다.
- **폭우에서 고도 안개가 사라짐**: 비 안개는 `ClearFogDensity`에서 `weather.rainFogDensity`(고정
  상수)로 보간하므로, 고도 배율을 맑은 쪽에만 곱하면 비가 최고조일 때 정확히 0이 된다.
  배율을 프레임당 한 번 구해 **양쪽에** 곱하도록 고쳤다.

## 1. 고품질 게임은 실제로 어떻게 하는가

### 1.1 잔디

**Ghost of Tsushima**(GDC 2021, Sucker Punch)가 이 분야의 기준점이다.
셸 텍스처링이 아니라 **타일당 draw call 1회의 절차적 블레이드**다. 512×512 타일 텍스처에
지형 높이·머티리얼·잔디 종류·뭉침 계수·블레이드 크기·바람을 전부 구워 두고, 버텍스 셰이더가
그것을 읽어 블레이드를 휜다. Voronoi로 뭉침(클럼핑)을 제어하고, 타일 경계는 디더 전환으로 감춘다.
**100만 블레이드를 2ms.**

"게임 잔디"와 "진짜 잔디"를 가르는 것, 우선순위 순:

1. **비균일 밀도와 뭉침** — 균일 격자는 즉시 카펫으로 읽힌다
2. **방향성 있는 돌풍** — 블레이드마다 랜덤 사인만으로는 안 된다. 필드를 가로지르는 *파도*가 보여야 한다
3. **밑동 접지감(AO)과 지형 노멀 정렬** — "떠 있는 잔디"는 가장 흔한 실패 신호다
4. **매크로 + 마이크로 이중 색 변주** — 단색 초록 필드는 인공적이다
5. **역광 투과광** — 반사만 있고 투과가 없으면 매트하게 보인다
6. **팝핑 없는 LOD**

바람의 업계 표준은 SpeedTree의 4계층 모델이다: Shared Motion(전체 저주파) + Branch + Ripple +
**Gusting**. 핵심은 gust가 "일정한 앰비언트 위에 얹히는 변동"이고, 강도 변화가 순간적이지 않고
response time을 두고 서서히 적용된다는 점이다.

### 1.2 바다

- **Sea of Thieves**(SIGGRAPH 2018): Tessendorf **FFT**. 변위에서 뽑은 wave peak mask로 얕은/깊은 물 색을
  블렌딩. 거품은 파도 정점과 물체 교차부에서 발생시켜 **매 프레임 블러+피드백**으로 번지게 한 뒤 텍스처와
  섞는다. 수면 아래에서는 **Snell's window**로 위를 보여 준다.
- **AC4 블랙 플래그**: 유체 시뮬레이션이 **없다.** 배 안에 보이지 않는 lid를 depth buffer에만 그려 물이
  안 들어오게 하고, 배의 변위는 수면에 투영하는 작은 displacement mask로 흉내 낸다. 전부 트릭이다.
- **Subnautica**: Tessendorf로 갈아엎으면서, 태양 감쇠율과 눈에 도달하는 반사광 감쇠율을 **일부러 분리**해
  물리적으로 정확한 결과 대신 "수중 사진가가 찍은 것 같은" 그림을 만들었다.

"진짜 물"을 가르는 것, 우선순위 순:

1. **깊이 기반 색 그라데이션(Beer-Lambert) + 굴절** — 가장 적은 노력에 가장 큰 인상
2. **해안 거품과 스와시** — 섬 게임에서 가장 자주 보는 곳
3. **반사 품질** — 흐린 반사는 즉시 게임 티가 난다
4. **파도 정점의 백라이트**
5. **거품의 물리적 근거**(Jacobian/wake/교차) — 반복되는 정적 텍스처면 안 된다
6. **수중 전환**(안개 + 카우스틱 + Snell's window)

**파도 형상 자체(FFT냐 Gerstner냐)는 우선순위가 낮다.** 위 6개를 하면 Gerstner도 진짜처럼 보이고,
FFT를 써도 저걸 안 하면 게임 물로 보인다.

### 1.3 날씨와 하늘

- **대기 산란**: Preetham(1999, 부정확) → Hosek-Wilkie(2012, LUT 없이 준수) → Bruneton(정확하지만 무거운
  프리컴퓨트) → **Hillaire 2020**(Bruneton의 실무형 축약, 작은 LUT 4장). 언리얼의 SkyAtmosphere가
  Hillaire 기반이고 사실상 업계 표준이다. 런타임 비용은 LUT 샘플링 수준이라 URP에서도 무리 없다.
- **Aerial perspective**가 섬 게임에서 특히 중요하다. 단순 선형 안개는 "우유빛 벽"이 되지만, 산란 기반
  거리 안개는 높이에 따라 감쇠하고 태양 위치에 따라 원경이 붉거나 푸르게 물든다. **원경 섬이 옅은
  파란빛으로 사라지는 그림은 오직 이것으로만 나온다.**
- **볼류메트릭 구름**: Horizon Zero Dawn의 Nubis(Schneider & Vos). Perlin-Worley로 큰 덩어리, 고주파
  Worley로 가장자리 침식, weather map(RGB에 coverage/type/height) 으로 지역별 형태, powder 효과,
  Henyey-Greenstein 위상함수, **temporal reprojection**으로 비용 절감. 이게 없으면 실시간 불가능이다.
- **구름 그림자**: 태양 방향에서 내려다보는 coverage 텍스처를 지면에 투영. **구름이 없어도 만들 수 있고,
  레이마칭 없이 얻는 가장 비용 대비 효과가 큰 리얼리즘 요소다.**
- **번개**: 실제 번개 영상 분석 결과 **그림자가 거의 생기지 않는다.** 그래서 디렉셔널 라이트를 옮기는 게
  아니라 평평한 앰비언트 부스트로 만든다. 천둥 지연은 정밀 계산 없이 랜덤 0~3초로 충분하다.
- **비 차단**: RDR2/Uncharted는 위에서 내려다보는 depth map을 떠서 "가장 가까운 지붕 높이"와 비교한다.
  파티클 콜리전만으로는 **바닥이 젖는 효과까지는 막을 수 없다**는 게 이 기법이 필요한 이유다.
- **바람은 하나여야 한다.** 잔디·나무·천·파티클·바다가 각자 시뮬레이션하면 오히려 "따로 노는" 느낌이
  두드러진다. 전역 텍스처 하나를 전부가 샘플링하게 만드는 것이 정석이다.

---

## 2. 우리 현황 (코드 직접 확인)

이미 있는 것이 생각보다 많다.

**있음**: Gerstner 4파 + C#/셰이더 공유 수식 기반 부력(뗏목·플레이어·낚시가 실제로 사용) ·
깊이 기반 바다 색 3단 · 거품 3종(얕은수/화이트캡/쇄파 wake) · 젖은 모래 비대칭 곡선 ·
수중 안개/카우스틱/갓레이 · 해안 쇄파 리본의 투과광 · 절차적 하늘 + 낮밤 순환(태양각·달·3색 환경광·안개) ·
비 파티클 + 렌즈 물방울 화면 셰이더 · 번개 섬광 + 음속 지연 천둥 · 날씨 상태 점진 전이 ·
잔디 GPU 인스턴싱 + 패치 클럼핑 + 인스턴스 색 지터 + 투과광 + 플레이어 밟힘 · 블룸/톤매핑/색보정

**없거나 반쪽**:

| # | 항목 | 상태 |
|---|---|---|
| 1 | 포스트프로세싱 볼륨이 **두 벌** | 씬에 배치된 Volume(priority 0)의 값이 `AtmospherePostFX`가 코드로 만드는 Volume(priority 10)에 항상 져서 **화면에 절대 안 나온다** |
| 2 | 전역 바람 | 없음. 잔디(셰이더 상수) · 바다(Gerstner 방향) · 비(`rainWind`) · 해초(셰이더 상수) **4계통이 따로 논다** |
| 3 | 잔디 바람 세기 | `_WindStrength`가 C#에서 한 번도 설정되지 않는다. 폭풍이 와도 잔디는 그대로 |
| 4 | 잔디 돌풍 레이어 | 없음(3겹 사인만) |
| 5 | 바다 굴절 | 없음. `_CameraOpaqueTexture` 미사용 |
| 6 | 바다 반사 | 큐브맵/SSR/플래너 전부 없음. SH 앰비언트 근사만 |
| 7 | 전역 젖음 | `_MG_Wetness`는 있으나 **모래와 바다만** 읽는다. 건물·바위·나무·뗏목 갑판은 비가 와도 그대로 |
| 8 | 구름 / 구름 그림자 | 둘 다 없음 |
| 9 | 높이 기반 · 산란 기반 안개 | 없음(거리 기반 ExponentialSquared만) |
| 10 | 웅덩이 | 없음 |
| 11 | 지형 노멀 블렌딩 | 없음(잔디가 지면과 따로 논다) |
| 12 | 매크로 색 변주 | 없음 |
| 13 | 크리처 밟힘 | 없음(플레이어만) |
| 14 | Snell's window | 없음 |
| 15 | 죽은 데이터 | `DefaultVolumeProfile`에 대응 C#이 없는 유니티 샘플 잔재 컴포넌트 6종 |

---

## 3. 실행 계획 — 효과/공수 순

### 파도 A · 정리와 배관 (반나절~1일, 효과: 이후 전부의 전제)

- **A1. 포스트프로세싱 볼륨 일원화.** 지금 씬 Volume에 넣은 값은 전부 죽어 있다. 하나로 합친다.
- **A2. `DefaultVolumeProfile` 죽은 컴포넌트 제거.**

### 파도 B · 전역 바람 (2~3일, 효과 ★★★★★)

**이 문서에서 가장 중요한 항목이다.** 지금 4계통으로 갈라진 바람을 `WindSystem` 하나로 합친다.

- 전역 셰이더 변수 `_MG_Wind`(xz=방향·세기, w=시각) + 저주파 노이즈 텍스처 1장
- **돌풍 레이어**: 앰비언트 위에 얹히는 변동, response time을 두고 서서히(SpeedTree 방식)
- `WeatherSystem`이 폭풍 강도로 이 하나를 구동하고, 잔디·바다·해초·비·나무가 전부 이것만 읽는다
- 부수 효과: 폭풍이 오면 잔디가 눕고 해초가 같은 방향으로 쓸리고 파도가 커진다 — 지금은 셋이 무관하다

### 파도 C · 하늘과 안개 (3~5일, 효과 ★★★★★)

- **C1. 구름 그림자.** 저주파 노이즈를 태양 디렉셔널 라이트의 **light cookie**로 투영한다. URP가 네이티브로
  지원하는 기능이라 새 셰이더 패스가 필요 없다. 구름 자체가 없어도 지형에 구름 그림자가 흐른다.
  전역 바람이 이 텍스처를 흘려 보낸다(B와 직결).
- **C2. 높이 기반 산란 안개.** 지금의 거리 안개를 높이 감쇠 + 태양 방향 색 편이를 갖는 것으로 교체.
  원경 섬이 파랗게 사라지는 그림이 여기서 나온다.
- **C3. 스카이박스 시차 구름.** 돔에 2~3겹 노이즈를 다른 속도로 흘린다. 레이마칭 없이 비용 거의 0.

### 파도 D · 바다 마감 (2~3일, 효과 ★★★★)

- **D1. `_CameraOpaqueTexture` 굴절.** 얕은 물의 일렁임. URP가 기본 제공하는 텍스처라 켜고 쓰기만 하면 된다.
- **D2. 하늘 반사.** 최소한 큐브맵 + Fresnel. 지금은 하늘도 지형도 수면에 안 비친다.
- **D3. 오픈오션 파도 정점 백라이트.** 쇄파 리본에는 이미 있다 — 같은 계산을 `MGOcean`으로 옮긴다.
- **D4. Snell's window.** 수중에서 올려다볼 때의 굴절 원뿔.

### 파도 E · 젖음 전면화 (2일, 효과 ★★★★)

- **E1.** `_MG_Wetness`를 건물·바위·나무·뗏목 갑판 재질까지 확대(roughness↓ albedo 어둡게).
  "세상이 젖는다"가 지금은 절반만 구현돼 있다.
- **E2.** 웅덩이: 평지 + 낮은 고도 마스크에 젖음이 임계치를 넘으면 웅덩이 레이어.
- **E3.** 다공성: 재질별 젖는 속도/어두워지는 정도.

### 파도 F · 잔디 마감 (3~4일, 효과 ★★★)

- **F1.** 매크로 변주 텍스처(저주파 색 편차)
- **F2.** 지형 노멀 블렌딩 — "떠 있는 잔디" 해소
- **F3.** 크리처 밟힘(지금은 플레이어만)
- **F4.** 밑동 AO를 색 트릭이 아니라 실제 그라데이션으로

### 나중에 (여유 있을 때)

- 볼류메트릭 구름(오픈소스 포팅), 물리 기반 대기 산란(Hillaire 4-LUT), 화이트캡 Jacobian,
  RDR2식 top-down depth map 비 차단, 잔디 compute 컬링 + indirect draw

---

## 4. 하지 말 것 (조사에서 확인된 함정)

| 함정 | 이유 |
|---|---|
| **자체 FFT 바다 구현** | 시각 상한은 최고지만 **부력이 구조적으로 어렵다.** FFT 변위는 GPU 텍스처로만 존재하고 닫힌 역함수가 없어, 임의 지점 높이를 구하려면 async readback이나 반복 근사가 필요하다. 우리는 뗏목 부력이 게임플레이 핵심이다. Gerstner를 유지하는 것이 맞다 |
| **Crest Ocean System 도입** | **"URP에서 무료 오픈소스"는 사실이 아니다.** GitHub의 MIT Crest는 Built-in RP 전용이고, URP/HDRP판은 에셋스토어 유료($240)다 |
| **Unity 공식 Water System** | **URP를 지원하지 않고, 앞으로도 계획이 없다.** 유니티 개발자 공식 발언: 컴퓨트 셰이더 의존도가 높아 URP판은 코드를 거의 공유할 수 없다 |
| **Nubis급 풀 볼류메트릭 구름을 먼저** | temporal reprojection · 노이즈 파이프라인 · weather map을 전부 새로 만들고 디버깅해야 한다. 스카이박스 구름 + 구름 그림자로 먼저 출시 가능한 수준을 확보하는 게 맞다 |
| **셸 텍스처링 잔디** | 근접 시 밴딩/구멍. 걸어 다니며 통과하는 게임 구조와 맞지 않는다. 역사적으로 털/저사양 대체용이다 |
| **지오메트리 셰이더 잔디** | Roystan/Catlike 등 유명 튜토리얼이 쓰지만 모던 모바일/일부 Forward+에서 지원이 불안정하다. compute 방식이 현대적 대체재다 |
| **GPU Resident Drawer가 잔디를 해결해 줄 거란 기대** | 일반 MeshRenderer GameObject만 배칭한다. compute 버퍼로 직접 그리는 커스텀 잔디에는 관여하지 않는다 |
| **물리적으로 정확한 수중 감쇠 수치** | Subnautica 팀이 명시적으로 경고했다. 정확한 값은 칙칙하다. 아티스트가 만질 수 있는 파라미터로 설계해야 한다 |

---

## 5. 쓸 수 있는 오픈소스 / 무료 리소스

라이선스를 확인한 것만 적는다. 실제로 가져오는 시점에 `Docs/Attribution.md`에 항목을 추가한다.

### 코드 (전부 참고·이식 대상, 그대로 넣을 것은 없다)

| 이름 | 라이선스 | 쓸 곳 |
|---|---|---|
| Unity 공식 Shader Graph "Production Ready" 샘플 (Grass / **Weather**) | Unity 내장 | **URP 17에 정확히 대응.** 잔디·젖음 셰이더의 출발점 |
| [jiaozi158/UnityVolumetricCloudsURP](https://github.com/jiaozi158/UnityVolumetricCloudsURP) | MIT | 볼류메트릭 구름(나중에). **light cookie로 구름 그림자를 만드는 방식이 C1의 참고 구현** |
| [sinnwrig/URP-Atmosphere](https://github.com/sinnwrig/URP-Atmosphere) | MIT | 대기 산란(나중에) |
| [Fewes/MinimalAtmosphere](https://github.com/Fewes/MinimalAtmosphere) | MIT | 대기 산란 학습용(저자가 프로덕션 사용을 권하지 않는다고 명시) |
| [sebh/TileableVolumeNoise](https://github.com/sebh/TileableVolumeNoise) | 공개 | 타일링 3D Perlin-Worley 노이즈 생성 — 구름·바람 텍스처의 사실상 표준 소스 |
| [MangoButtermilch/Unity-Grass-Instancer](https://github.com/MangoButtermilch/Unity-Grass-Instancer) | MIT | 잔디 compute 컬링(나중에). **URP 17.0.3 동작 확인됨** |
| [Cyanilux — GPU Instanced Grass / Shoreline Shader](https://www.cyanilux.com/tutorials/) | 공개 튜토리얼 | Unity 6 신 API 기준. 해안 셰이더 글은 우리 쇄파와 직결 |
| [aniruddhahar/URP-WaterShaders](https://github.com/aniruddhahar/URP-WaterShaders) | MIT | URP 물 셰이더 템플릿(굴절·수심 shoreline 참고) |
| [wave-harmonic/water-resources](https://github.com/wave-harmonic/water-resources) | 링크 모음 | 물 렌더링 논문·GDC 자료 큐레이션. 이 분야 1순위 인덱스 |
| [IceFall Games — Lightning & Thunder](https://mtnphil.wordpress.com/2012/04/12/simulating-lightning-and-thunder/) | 공개 글 | 번개의 "그림자가 안 생긴다" 관찰 — 우리 구현과 이미 같은 방향 |

### 텍스처 / 모델 (CC0만)

| 소스 | 라이선스 | 쓸 곳 |
|---|---|---|
| [Poly Haven](https://polyhaven.com/) | **CC0** | HDRI(하늘/반사 프로브), 잔디·모래·바위 PBR 텍스처, groundcover 모델 |
| [ambientCG](https://ambientcg.com/) | **CC0** | 지면·식생 PBR 텍스처 |
| [Quaternius](https://quaternius.com/) | **CC0** | 로우폴리 식생 |
| [Kenney](https://kenney.nl/) | **CC0** | 자연 키트, 식생 스프라이트 |

---

## [출처]

**잔디**
- [GDC — Procedural Grass in Ghost of Tsushima](https://gdcvault.com/play/1027033/Advanced-Graphics-Summit-Procedural-Grass)
- [SpeedTree Games Wind Model](https://docs.unity3d.com/speedtree-modeler/manual/wind-games.html)
- [Unity — Shader Graph Production Ready 샘플](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Shader-Graph-Sample-Production-Ready-Detail.html)
- [Cyanilux — GPU Instanced Grass Breakdown](https://www.cyanilux.com/tutorials/gpu-instanced-grass-breakdown/)
- [Unity — GPU Resident Drawer](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html)
- [80.lv — shell texturing](https://80.lv/articles/classic-video-games-trick-for-rendering-grass-fur)

**바다**
- [Tessendorf — Simulating Ocean Water](https://jtessen.people.clemson.edu/reports/papers_files/coursenotes2004.pdf)
- [SIGGRAPH 2018 — The Technical Art of Sea of Thieves](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf)
- [Simon Schreibt — AC4 Black Flag Waterplane](https://simonschreibt.de/gat/black-flag-waterplane/)
- [Game Developer — How Subnautica plunges deeper into rendering realistic water](https://www.gamedeveloper.com/design/how-i-subnautica-i-plunges-deeper-into-rendering-realistic-water)
- [Cyanilux — Shoreline Shader Breakdown](https://www.cyanilux.com/tutorials/shoreline-shader-breakdown/)
- [Unity Discussions — Water System for HDRP not URP?](https://discussions.unity.com/t/water-system-for-hdrp-not-urp/918838)
- [Crest — Asset Store(유료 URP판)](https://assetstore.unity.com/packages/tools/particles-effects/crest-water-5-oceans-rivers-lakes-268614) / [GitHub MIT판(Built-in 전용)](https://github.com/wave-harmonic/crest)

**날씨·하늘**
- [Guerrilla — Nubis: Authoring Real-Time Volumetric Cloudscapes](https://www.guerrilla-games.com/read/nubis-authoring-real-time-volumetric-cloudscapes-with-the-decima-engine)
- [SIGGRAPH 2017 Nubis PDF](https://advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf)
- [Hillaire 2020 — A Scalable and Production Ready Sky and Atmosphere Rendering Technique](https://onlinelibrary.wiley.com/doi/abs/10.1111/cgf.14050)
- [RDR2 Graphics Study](https://imgeself.github.io/posts/2020-06-19-graphics-study-rdr2/)
- [Sébastien Lagarde — Dynamic rain and its effects](https://seblagarde.wordpress.com/2012/12/27/water-drop-2a-dynamic-rain-and-its-effects/)
- [Unity — What's new in URP 17](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/whats-new/urp-whats-new.html) (볼류메트릭 포그·구름은 **URP 네이티브 미포함** 확인)
- [Shader Graph — Production Ready Weather 샘플](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/Shader-Graph-Sample-Production-Ready-Weather.html)
