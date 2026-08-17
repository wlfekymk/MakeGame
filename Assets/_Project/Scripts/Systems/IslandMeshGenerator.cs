using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 지형용 절차적 메시를 생성하는 유틸리티.
    /// 밋밋한 원기둥 플레이스홀더 대신, 중심이 살짝 높고 가장자리로 갈수록 완만하게 낮아지며
    /// 약간의 굴곡(펄린 노이즈)이 있는, 실제로 걸어다닐 수 있는 낮은 언덕 모양의 지형을 만든다.
    ///
    /// [B5 확장 - "민둥산" 해소] 지형 메시 생성에 더해, 그 위에 얹는 지면 구분(해안 모래 / 내륙 풀)과
    /// 초목(야자수·덤불·풀포기) 배치도 이 클래스가 담당한다. ArtDirection.md 0장이 "정점을 직접 찍는
    /// 절차적 메시(IslandMeshGenerator)가 야자수·바위처럼 프리미티브 조합보다 한 단계 복잡한 형태를
    /// 만들 수 있는 유일한 검증된 경로"라고 명시하고 있어, 섬 표면 시각 요소의 단일 소스를 여기로 모은다.
    /// </summary>
    public static partial class IslandMeshGenerator
    {
        // ── [B22 물가 전이] 해안선을 원이 아니게 만드는 상수 ───────────────────────────
        // 문제: 예전 높이식은 가장자리(t=1)에서 baseHeight도 노이즈도 정확히 0이라, 지형이
        // **반지름 R의 완벽한 원 위에서 해수면(y=0)과 정확히 만났다.** 바다 평면도 y=0 불투명이라
        // 모래와 바다가 자로 그은 원에서 딱 잘렸다("물가가 잘린다" 신고의 실체).
        // 조치: 바깥 ShoreBandFraction 구간을 해수면 **아래로** 내리고, 그 구간에서만 살아나는
        // 별도 펄린을 더한다. 그러면 눈에 보이는 물가는 메시의 바깥 테두리가 아니라
        // **y=0 등고선**이 되고, 그 등고선은 노이즈 때문에 각도마다 들쭉날쭉해진다.
        // 바깥 테두리(y<0)는 불투명한 바다 평면 아래에 잠겨 보이지 않는다.
        //
        // 중요: 메시의 **XZ 반경은 1mm도 바뀌지 않는다.** 정점 개수/인덱스/UV/삼각형 감는 방향도
        // 그대로다. 즉 콜라이더 footprint·스포너 산포 반경(0.8R)·TerrainSampler 스냅은 전부 무영향이고,
        // 바뀌는 것은 바깥 12% 구간의 y뿐이다(0.8R 지점은 shoreT=0이라 예전 값과 비트 단위로 동일).
        /// <summary>해수면 아래로 내리기 시작하는 바깥 구간의 비율(t 기준). 0.12 = 바깥 12%.</summary>
        private const float ShoreBandFraction = 0.12f;

        /// <summary>메시 바깥 테두리(t=1)가 해수면 아래로 내려가는 깊이(m).</summary>
        private const float ShoreSubmergeDepth = 1.8f;

        /// <summary>물가 등고선을 흔드는 펄린의 주파수. 0.035 ≈ 격자 29m라 큰 섬에서도 만(灣)처럼 읽힌다.</summary>
        private const float ShoreNoiseScale = 0.035f;

        /// <summary>물가 등고선을 흔드는 진폭(m). 클수록 해안선이 더 들쭉날쭉해진다.</summary>
        private const float ShoreNoiseAmplitude = 1.4f;

        // ── [B46 섬별 시드] 노이즈 오프셋을 섬마다 갈라 놓는 장치 ────────────────────────
        // 문제: 위 두 펄린은 **섬 로컬 좌표**에 고정 오프셋(+1000 / +517)을 더해 찍는다. 섬 오브젝트의
        // 월드 위치는 메시에 전혀 들어가지 않으므로(정점은 원점 대칭으로 굽고 오브젝트만 옮긴다 -
        // CreateProceduralIslandTerrain), **반지름·링·세그먼트가 같은 섬은 지형 메시가 비트 단위로 동일**했다.
        // 씬의 섬 9개는 규모가 4종뿐이라 실제로 같은 모양이 여러 개 있었다.
        //
        // 조치: 오프셋만 섬마다 다르게 준다. **형태 언어(코사인 낙차·1옥타브 펄린·해안 잠수)는 한 줄도
        // 바꾸지 않는다** - 옥타브/능선/석호는 다음 배치다. 즉 "같은 언어로 쓴 다른 문장"이 된다.
        //
        // ★ 난수 소비 0 ★ System.Random을 단 한 번도 건드리지 않는다. 오프셋은 (worldSeed, islandId)만
        // 입력으로 받는 **순수 해시**다. 스포너가 쓰는 섬별 스트림은 이 파일을 거치지 않고, 지형 생성은
        // 어떤 rng도 넘겨받지 않으므로 자원·위험요소의 추첨 순서가 한 칸도 밀리지 않는다
        // (HazardSpawner.IsBearCubIndividual가 같은 제약을 같은 방법으로 지킨다 - 그쪽이 선례다).

        /// <summary>
        /// noiseSeed 인자를 생략했을 때의 센티널. 이 값이면 오프셋이 예전 상수(1000 / 517)로 고정되어
        /// **예전과 비트 단위로 동일한 메시**가 나온다(회귀 안전장치). ComputeNoiseSeed는 이 값을 절대 돌려주지 않는다.
        /// </summary>
        public const int LegacyNoiseSeed = int.MinValue;

        /// <summary>
        /// 오프셋이 흩어지는 폭(노이즈 입력 단위). Unity Mathf.PerlinNoise는 순열표가 256 주기라
        /// 이보다 크게 잡아도 실효 오프셋은 256으로 접힌다 - 그래서 정확히 한 주기를 쓴다.
        /// noiseScale 0.05 기준 1단위 = 20m이므로, 지형 노이즈에서 256단위는 5,120m다.
        /// 가장 큰 섬(지름 400m = 20단위)도 서로 겹치지 않을 자리가 넉넉하다.
        /// </summary>
        private const float NoiseOffsetSpan = 256f;

        /// <summary>
        /// (worldSeed, islandId)에서 지형 노이즈 시드를 결정적으로 유도한다. **난수를 소비하지 않는 순수 함수다.**
        /// 같은 월드를 다시 열면 같은 섬이 항상 같은 지형으로 나온다.
        ///
        /// 해시는 두 소수 곱 → xorshift-곱 마무리(FNV/Murmur 계열 finalizer)다. worldSeed와 islandId가
        /// 둘 다 작은 정수라 단순 덧셈만으로는 상관이 남는다(HazardSpawner.IsBearCubIndividual와 같은 근거).
        /// SeededRandomExtensions.CreateForSalt와 같은 (worldSeed, salt) 규약을 따르되, 그쪽은 System.Random을
        /// **만들어** 돌려주므로 여기서는 쓸 수 없다 - 지형은 난수 스트림을 하나도 만들지 않는 것이 요건이다.
        /// </summary>
        public static int ComputeNoiseSeed(int worldSeed, int islandId)
        {
            unchecked
            {
                uint h = (uint)(worldSeed * 73856093) ^ (uint)(islandId * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                int seed = (int)h;
                // 센티널과 겹치면 예전 지형으로 조용히 되돌아간다 - 확률은 2^-32이지만 실패 모드가
                // "이 섬만 다른 섬과 같은 모양"이라 눈으로 못 잡는다. 값 하나를 비켜 준다.
                return seed == LegacyNoiseSeed ? 0 : seed;
            }
        }

        /// <summary>시드와 축별 salt를 섞어 [0, NoiseOffsetSpan) 구간의 오프셋 하나를 만든다(난수 소비 0).</summary>
        private static float NoiseOffsetFromSeed(int noiseSeed, uint axisSalt)
        {
            unchecked
            {
                uint h = (uint)noiseSeed ^ axisSalt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                // 정수 오프셋만 쓰면 펄린 격자가 섬마다 똑같이 정렬돼 "격자 위상"까지 같아진다.
                // 24비트를 실수로 펴서 소수부까지 갈라 놓는다(오프셋일 뿐이라 형태 언어는 그대로다).
                return (h & 0xFFFFFFu) / (float)0x1000000u * NoiseOffsetSpan;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  [B47 형태 언어] 프로파일 8종 — "원판 하나"를 조각 가능한 높이장으로 바꾼다
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // 문제(B46 이후 남은 것): 섬마다 노이즈 오프셋은 갈렸지만 **형태 언어가 얕아서** 실기에서 거의
        // 같아 보였다. 반지름은 t·radius(완벽한 원), 높이는 maxHeight·cos(t·π/2)(회전 대칭 돔 하나),
        // 노이즈는 1옥타브에 내륙 표준편차 0.25m — 90m 섬에서 눈높이로는 평지다.
        //
        // ★ 이번 조치의 핵심 원칙: 메시 토폴로지는 한 줄도 바꾸지 않는다. **높이만 조각한다.** ★
        // 이 메시는 (중심 + 링 × 세그먼트) 부채꼴 원판이고, 그 토폴로지를 유지한 채 높이를 해수면
        // 아래로 내리면 그 부분이 곧 바다가 된다(바다 평면은 y=0 불투명이라 y<0은 보이지 않는다).
        // 사용자가 요청한 것 전부가 이것만으로 표현된다:
        //   가운데 물길 → 섬을 가로지르는 골짜기를 y<0까지 내린다
        //   반달/초승달 → 한쪽을 크게 파낸다(q 를 국소적으로 부풀리는 "만")
        //   석호/환초   → 중간 반지름을 올리고 중심을 내린다
        //   길쭉한 섬   → 이방성 감쇠
        //   쌍봉        → 돔 중심 2개
        // 마칭스퀘어·격자 재작성 같은 개조는 하지 않는다. 정점 수·인덱스·UV·삼각형 감는 방향·XZ 반경은
        // 이전 배치와 **1mm도 바뀌지 않는다** — 바뀌는 것은 y뿐이다. 따라서 콜라이더 footprint,
        // 스포너 산포 반경(0.8R), TerrainSampler 스냅, 지면 캡(BuildCapLayer가 이 메시를 잘라 쓴다)의
        // 전제가 전부 그대로 성립한다.
        //
        // ★ 난수 소비 0 (B46과 같은 제약, 같은 방법) ★
        // 프로파일 선택도 파라미터 지터도 전부 (worldSeed, islandId) 순수 해시다. System.Random을
        // 만들지도 소비하지도 않는다. 한 번이라도 추첨을 더 하면 IslandResourceSpawner의
        // (islandIndex, spawnOrder) 세이브 키가 통째로 밀린다.
        //
        // ★ 자원이 물에 빠지지 않게 하는 제약 ★
        // 자원/위험요소는 산포 반지름 0.8R 안에 **지형과 무관하게** 뿌려진다(IslandResourceSpawner는
        // 지형을 보지 않는다). 그래서 아래 8종은 전부 "0.8R 원 안의 육지 비율(y>0) ≥ 70%"를 만족하도록
        // 튜닝했다. 물 지형(수로·만·석호)은 폭을 좁게 잡거나 산포 원 바깥으로 밀어냈다.
        // 실측치는 Tools/terrain/preview.py 가 출력한다(파라미터를 고치면 반드시 다시 돌려서 확인해라):
        //   P0 초원 98% / P1 단봉 91% / P2 쌍봉 94% / P3 초승달 75% /
        //   P4 수로 82% / P5 석호 86% / P6 능선 95% / P7 고원 96%
        //
        // ★ 경사 상한 ★ PlayerController의 slopeLimit은 45도다. 메시 정점 격자에서 잰 육지 최대 경사는
        // P7(고원+절벽)의 53도를 빼면 전부 30도 미만이고, P7의 53도는 **의도한 절벽**이다.
        // 그 섬도 방위각의 85%에서 45도를 넘지 않는 접근로가 있다(메사 가장자리 폭을 절벽 방향
        // mesaSoftMin=0.09, 반대 방향 mesaSoftMax=1.70으로 비대칭으로 잡은 것이 그 우회로다).

        /// <summary>프로파일 개수. islandId 0은 항상 0번(가장 완만한 프로파일)이다.</summary>
        public const int ShapeProfileCount = 8;

        /// <summary>q(프로파일 해안선 기준 정규화 반경)가 1을 넘어선 뒤 1당 더 내려가는 깊이(m).</summary>
        private const float ShelfDropPerQ = 3.2f;

        /// <summary>위 추가 하강의 상한(m). 메시 테두리가 바다 평면 아래에 확실히 잠기게만 하면 된다.</summary>
        private const float ShelfDropMax = 9.0f;

        /// <summary>시드와 salt를 섞어 [0,1) 스칼라 하나를 만든다(난수 소비 0, NoiseOffsetFromSeed와 같은 finalizer).</summary>
        private static float Hash01FromSeed(int noiseSeed, uint salt)
        {
            unchecked
            {
                uint h = (uint)noiseSeed ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        /// <summary>Hash01FromSeed를 [lo, hi) 구간으로 편 것.</summary>
        private static float HashRangeFromSeed(int noiseSeed, uint salt, float lo, float hi)
        {
            return lo + (hi - lo) * Hash01FromSeed(noiseSeed, salt);
        }

        /// <summary>uint 하나를 섞는다(위 두 함수와 같은 finalizer). 프로파일 순열 셔플에만 쓴다.</summary>
        private static uint Mix32(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>
        /// (worldSeed, islandId)에서 지형 프로파일 번호를 고른다. **난수를 소비하지 않는 순수 함수다.**
        ///
        ///  · islandId 0(시작 섬)은 **항상 0번 = 가장 완만한 프로파일**이다. 튜토리얼 구간이고
        ///    사용자가 여기서 처음 집을 짓는다. 경비행기 잔해(+6,-4)·배 작업대(-6,-3)가 중심 근처에
        ///    고정 배치되므로 중심부가 평평해야 한다는 제약도 여기서 지킨다.
        ///  · islandId 1~7은 나머지 7종의 **worldSeed 의존 순열**을 받는다. 단순 해시로 고르면 섬 9개
        ///    중 같은 프로파일이 여러 개 나올 수 있는데(생일 문제), 순열이면 첫 8개 섬이 서로 다른
        ///    프로파일을 받는 것이 보장된다 — "섬마다 다르게"라는 요구에 직접 대응하는 부분이다.
        ///  · islandId 8 이상은 순수 해시로 1~7 중 하나를 고른다(현재 씬은 섬 9개라 여기 오는 것은 1개다).
        /// </summary>
        public static int SelectShapeProfile(int worldSeed, int islandId)
        {
            if (islandId <= 0)
                return 0;

            unchecked
            {
                if (islandId < ShapeProfileCount)
                {
                    // Fisher-Yates. 셔플 시드는 worldSeed만 쓰므로, 같은 월드에서 어떤 섬이 물어도
                    // 같은 순열이 재현된다(섬마다 따로 계산해도 결과가 일관된다).
                    var pool = new int[ShapeProfileCount - 1];
                    for (int i = 0; i < pool.Length; i++)
                        pool[i] = i + 1;

                    uint h = Mix32((uint)worldSeed ^ 0x2545F491u);
                    for (int i = pool.Length - 1; i > 0; i--)
                    {
                        h = Mix32(h ^ 0x9E3779B9u);
                        int j = (int)(h % (uint)(i + 1));
                        int tmp = pool[i];
                        pool[i] = pool[j];
                        pool[j] = tmp;
                    }
                    return pool[islandId - 1];
                }

                uint g = Mix32((uint)(worldSeed * 40503) ^ (uint)(islandId * 2654435761u));
                return 1 + (int)(g % (uint)(ShapeProfileCount - 1));
            }
        }

        /// <summary>
        /// 지형 조각에 쓰는 파라미터 묶음. 좌표/반경/폭은 **반지름 R 대비 비율**, 높이/깊이는
        /// **maxHeight 대비 비율**이다(그래서 섬 규모가 달라도 형태가 그대로 확대·축소된다).
        ///
        /// Tools/terrain/preview.py 의 Profile 클래스와 필드가 1:1로 대응한다. 한쪽만 고치면 미리보기가
        /// 거짓말을 하게 되므로 반드시 함께 고쳐라.
        /// </summary>
        public sealed class IslandShapeProfile
        {
            /// <summary>프로파일 번호(0~7). 로그/디버그용.</summary>
            public int index;

            /// <summary>기본 낙차 높이 배율(× maxHeight). 씬의 terrainMaxHeight=8을 기준으로 섬 높이를 가른다.</summary>
            public float heightScale = 0.6f;

            /// <summary>cos 낙차의 지수. 1 미만이면 정상부가 평평해지고 가장자리가 가팔라진다(고원).</summary>
            public float plateauPow = 1f;

            /// <summary>각도별 반지름 마스크의 기본값과 하모닉 진폭(2·3·5차). 윤곽을 원에서 벗어나게 한다.</summary>
            public float maskBase = 0.94f;
            public float maskH2, maskH3, maskH5;

            /// <summary>
            /// ★ 사진에서 딴 윤곽을 꽂는 자리 ★ 등간격 각도 샘플 배열(0번 = +X축, 반시계 방향).
            /// 값은 "이 방위에서 육지가 R의 몇 배까지 나가는가"(0.15~1.0). null이면 위 하모닉을 쓴다.
            /// 길이는 자유(선형 보간한다). 위에서 본 섬 사진의 윤곽을 극좌표로 풀어 넣으면 그대로 동작한다.
            /// </summary>
            public float[] radialMask;

            /// <summary>이방성(1보다 크면 X축으로 길어진다)과 섬 전체 회전(라디안).</summary>
            public float stretch = 1f;
            public float spin;

            /// <summary>만(灣). 이 원 안에서 q를 부풀려 해안선을 안쪽으로 밀어 넣는다(초승달).</summary>
            public float biteX, biteZ, biteRadius, biteStrength;

            /// <summary>돔 2개. (중심 x, 중심 z, 반경, 높이).</summary>
            public float domeAX, domeAZ, domeARadius, domeAAmp;
            public float domeBX, domeBZ, domeBRadius, domeBAmp;

            /// <summary>능선(선분 기준 거리 감쇠). (끝점1, 끝점2, 폭, 높이).</summary>
            public float ridgeX0, ridgeZ0, ridgeX1, ridgeZ1, ridgeWidth, ridgeAmp;

            /// <summary>수로(음의 능선). 깊이가 충분하면 y&lt;0까지 내려가 실제로 물이 흐른다.</summary>
            public float channelX0, channelZ0, channelX1, channelZ1, channelWidth, channelDepth;

            /// <summary>석호: 고리 융기(링) + 중앙 분지(음).</summary>
            public float ringRadius, ringWidth, ringAmp;
            public float basinRadius, basinDepth;

            /// <summary>
            /// 메사(고원). cliffAngle 방향은 가장자리 폭이 softMin이라 절벽이 되고,
            /// 그 반대 방향은 softMax라 완경사 우회로가 된다(45도 상한 대응).
            /// </summary>
            public float mesaAmp, mesaRadius, mesaX, mesaZ, mesaCliffAngle, mesaSoftMin, mesaSoftMax;

            /// <summary>다중 옥타브 노이즈의 진폭 배율과 주파수 배율.</summary>
            public float noiseAmp = 1f;
            public float roughness = 1f;
        }

        /// <summary>
        /// (프로파일 번호, 섬 시드) → 파라미터. 같은 프로파일이라도 섬마다 회전과 몇몇 값이 갈리므로
        /// "같은 종류의 다른 섬"이 된다. 난수 소비 0(전부 Hash01FromSeed).
        ///
        /// 각 프로파일의 실측(육지 비율 / 최대 경사 / 최고 높이 / 평탄 구역)은 이 파일 위쪽 주석과
        /// Tools/terrain/preview.py 출력에 있다.
        /// </summary>
        public static IslandShapeProfile BuildProfile(int index, int noiseSeed)
        {
            float spin = HashRangeFromSeed(noiseSeed, 0x3C6EF372u, 0f, Mathf.PI * 2f);
            float j1 = Hash01FromSeed(noiseSeed, 0x85EBCA6Bu);
            float j2 = Hash01FromSeed(noiseSeed, 0xC2B2AE35u);
            float j3 = Hash01FromSeed(noiseSeed, 0x27D4EB2Fu);

            var p = new IslandShapeProfile { index = index, spin = spin };

            switch (index)
            {
                case 0: // 완만한 초원 — 시작 섬 전용. 넓고 평평하며 흔들림이 가장 얌전하다(그래도 원은 아니다).
                    p.heightScale = 0.30f; p.plateauPow = 0.40f;
                    p.maskBase = 0.90f; p.maskH2 = 0.070f + 0.020f * j1; p.maskH3 = 0.050f; p.maskH5 = 0.026f;
                    p.domeAX = 0.20f - 0.40f * j2; p.domeAZ = 0.18f - 0.36f * j3;
                    p.domeARadius = 0.55f; p.domeAAmp = 0.09f;
                    p.noiseAmp = 0.50f; p.roughness = 0.9f;
                    break;

                case 1: // 단봉 — 중심에서 비껴난 봉우리 하나 + 반대편 완만한 어깨.
                    p.heightScale = 0.36f; p.plateauPow = 1.15f;
                    p.maskBase = 0.87f; p.maskH2 = 0.115f; p.maskH3 = 0.080f + 0.025f * j1; p.maskH5 = 0.040f;
                    p.domeAX = 0.26f; p.domeAZ = 0.12f - 0.24f * j2; p.domeARadius = 0.50f; p.domeAAmp = 0.72f;
                    p.domeBX = -0.38f; p.domeBZ = -0.30f; p.domeBRadius = 0.40f; p.domeBAmp = 0.16f;
                    break;

                case 2: // 쌍봉 — 안부(saddle)로 이어진 봉우리 둘.
                    p.heightScale = 0.30f; p.plateauPow = 1.0f;
                    p.maskBase = 0.86f; p.maskH2 = 0.135f; p.maskH3 = 0.070f; p.maskH5 = 0.038f + 0.020f * j1;
                    p.stretch = 1.22f;
                    p.domeAX = 0.40f; p.domeAZ = 0.18f; p.domeARadius = 0.40f; p.domeAAmp = 0.72f;
                    p.domeBX = -0.38f; p.domeBZ = -0.22f; p.domeBRadius = 0.36f; p.domeBAmp = 0.62f + 0.10f * j2;
                    p.ridgeX0 = 0.34f; p.ridgeZ0 = 0.15f; p.ridgeX1 = -0.32f; p.ridgeZ1 = -0.19f;
                    p.ridgeWidth = 0.26f; p.ridgeAmp = 0.16f;
                    p.roughness = 1.05f;
                    break;

                case 3: // 초승달 — 한쪽에 큰 만이 파인다.
                    p.heightScale = 0.34f; p.plateauPow = 0.75f;
                    p.maskBase = 0.92f; p.maskH2 = 0.060f; p.maskH3 = 0.050f; p.maskH5 = 0.026f;
                    // 만의 중심을 섬 가장자리(0.79R)에 두고 반경을 크게 잡아, 안으로 깊게 파고들되
                    // 파인 면적의 상당 부분이 산포 원(0.8R) 바깥에 남게 한다(육지 비율 75%).
                    p.biteX = 0.79f; p.biteZ = 0f; p.biteRadius = 0.82f; p.biteStrength = 3.9f + 0.5f * j1;
                    p.domeAX = -0.44f; p.domeAZ = 0f; p.domeARadius = 0.54f; p.domeAAmp = 0.46f;
                    p.ridgeX0 = -0.30f; p.ridgeZ0 = 0.56f; p.ridgeX1 = -0.30f; p.ridgeZ1 = -0.56f;
                    p.ridgeWidth = 0.28f; p.ridgeAmp = 0.30f;
                    p.noiseAmp = 0.95f;
                    break;

                case 4: // 가운데 수로 — 좁은 물길이 섬을 두 쪽으로 가른다.
                    p.heightScale = 0.26f; p.plateauPow = 0.50f;
                    p.maskBase = 0.88f; p.maskH2 = 0.100f; p.maskH3 = 0.065f; p.maskH5 = 0.034f;
                    p.domeAX = 0.42f; p.domeAZ = 0.36f; p.domeARadius = 0.44f; p.domeAAmp = 0.34f;
                    p.domeBX = -0.42f; p.domeBZ = -0.36f; p.domeBRadius = 0.44f; p.domeBAmp = 0.30f + 0.08f * j2;
                    // 끝점을 마스크 바깥(±1.25R)에 두어 수로가 섬을 **완전히** 가로지르게 한다.
                    // 폭 0.150R은 육지 비율 82%를 지키는 값이고, 깊이 0.50은 "중심 육지 높이 + 약 1.5m"라
                    // 물이 확실히 흐르면서도 둑 경사가 45도를 넘지 않는다(실측 최대 24도, R=50에서 39도).
                    p.channelX0 = -1.25f; p.channelZ0 = 0.66f; p.channelX1 = 1.25f; p.channelZ1 = -0.66f;
                    p.channelWidth = 0.150f; p.channelDepth = 0.50f;
                    p.noiseAmp = 0.8f;
                    break;

                case 5: // 석호(환초) — 가운데가 얕은 물인 고리 모양 섬.
                    // 링을 **넓고 낮게** 잡는다. 좁고 높은 링은 능선이라 건축 평탄 구역이 생기지 않는다
                    // (1차 튜닝에서 실제로 평탄 구역 0%가 나왔다).
                    p.heightScale = 0.16f; p.plateauPow = 0.45f;
                    p.maskBase = 0.89f; p.maskH2 = 0.090f; p.maskH3 = 0.060f + 0.020f * j1; p.maskH5 = 0.032f;
                    p.stretch = 1.10f;
                    p.ringRadius = 0.62f; p.ringWidth = 0.38f; p.ringAmp = 0.38f;
                    p.basinRadius = 0.46f; p.basinDepth = 0.56f;
                    p.noiseAmp = 0.6f;
                    break;

                case 6: // 길쭉한 능선 — 한 축으로 늘어난 섬.
                    p.heightScale = 0.28f; p.plateauPow = 0.85f;
                    p.maskBase = 0.85f; p.maskH2 = 0.100f; p.maskH3 = 0.070f; p.maskH5 = 0.036f;
                    p.stretch = 1.38f;
                    p.ridgeX0 = 0.55f; p.ridgeZ0 = 0f; p.ridgeX1 = -0.55f; p.ridgeZ1 = 0f;
                    p.ridgeWidth = 0.50f; p.ridgeAmp = 0.62f + 0.08f * j3;
                    p.domeAX = 0.12f; p.domeAZ = 0f; p.domeARadius = 0.72f; p.domeAAmp = 0.12f;
                    p.noiseAmp = 0.95f;
                    break;

                default: // 7: 고원 + 절벽.
                    p.heightScale = 0.26f; p.plateauPow = 0.45f;
                    p.maskBase = 0.88f; p.maskH2 = 0.095f; p.maskH3 = 0.062f; p.maskH5 = 0.033f;
                    p.mesaAmp = 0.80f; p.mesaRadius = 0.34f; p.mesaX = 0.20f; p.mesaZ = 0.10f;
                    p.mesaCliffAngle = 0f; p.mesaSoftMin = 0.09f; p.mesaSoftMax = 1.70f;
                    p.domeAX = -0.40f; p.domeAZ = -0.05f; p.domeARadius = 0.60f; p.domeAAmp = 0.12f;
                    p.noiseAmp = 0.6f;
                    break;
            }

            return p;
        }

        // ── 조립 가능한 높이 프리미티브 ────────────────────────────────────────────────

        /// <summary>정규화 거리 d: 0에서 1, 1 이상에서 0. (1-d²)²이라 경계에서 기울기가 0이라 이음매가 없다.</summary>
        private static float Bump(float d)
        {
            float dd = Mathf.Clamp01(d);
            float w = 1f - dd * dd;
            return w * w;
        }

        /// <summary>점 (x,z)에서 선분 (x0,z0)-(x1,z1)까지의 거리. 능선/수로의 축이다.</summary>
        private static float SegmentDistance(float x, float z, float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0;
            float dz = z1 - z0;
            float ll = dx * dx + dz * dz;
            if (ll <= 1e-6f)
                return Mathf.Sqrt((x - x0) * (x - x0) + (z - z0) * (z - z0));

            float s = Mathf.Clamp01(((x - x0) * dx + (z - z0) * dz) / ll);
            float px = x0 + s * dx;
            float pz = z0 + s * dz;
            return Mathf.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }

        /// <summary>
        /// 각도별 반지름 마스크(0.15~1.0). 윤곽을 원에서 벗어나게 하는 유일한 장치다.
        /// radialMask 배열이 주입돼 있으면 그것을 선형 보간해서 쓴다(사진 윤곽 주입 경로).
        /// </summary>
        private static float MaskAt(float angle, IslandShapeProfile p)
        {
            float m;
            if (p.radialMask != null && p.radialMask.Length >= 3)
            {
                int n = p.radialMask.Length;
                float twoPi = Mathf.PI * 2f;
                float a = angle - Mathf.Floor(angle / twoPi) * twoPi; // [0, 2π)
                float f = a / twoPi * n;
                int i0 = (int)Mathf.Floor(f) % n;
                if (i0 < 0) i0 += n;
                int i1 = (i0 + 1) % n;
                float w = f - Mathf.Floor(f);
                m = Mathf.Lerp(p.radialMask[i0], p.radialMask[i1], w);
            }
            else
            {
                m = p.maskBase
                    + p.maskH2 * Mathf.Cos(2f * angle)
                    + p.maskH3 * Mathf.Cos(3f * angle + 1.7f)
                    + p.maskH5 * Mathf.Cos(5f * angle + 0.6f);
            }
            return Mathf.Clamp(m, 0.15f, 1f);
        }

        /// <summary>
        /// 다중 옥타브 펄린(3옥타브). 반환값 대략 [-0.5, 0.5].
        /// 예전 1옥타브는 내륙 표준편차가 0.25m뿐이라 눈높이에서 평지로 보였다 — 그 문제의 직접 대응이다.
        /// 주파수 배율 2.17은 정수가 아니라, 옥타브끼리 격자가 겹쳐 축 정렬 무늬가 생기는 것을 피한다
        /// (B9에서 펄린 격자가 축에 나란해 직선 이음매가 생겼던 것과 같은 계열의 사고 방지).
        /// </summary>
        private static float Fbm(float x, float z, float offsetX, float offsetZ, float scale)
        {
            float total = 0f;
            float amp = 1f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < 3; i++)
            {
                total += (Mathf.PerlinNoise(x * scale * freq + offsetX * freq, z * scale * freq + offsetZ * freq) - 0.5f) * amp;
                norm += amp;
                amp *= 0.45f;
                freq *= 2.17f;
            }
            return total / norm;
        }

        /// <summary>
        /// ★ 조각된 높이장 ★ 한 점 (x,z)의 지형 높이. y &lt; 0 이면 그 자리는 그대로 바다가 된다.
        /// Tools/terrain/preview.py 의 sculpt_height 와 항 순서까지 1:1로 대응한다.
        /// </summary>
        private static float SculptHeight(float x, float z, float radius, float maxHeight,
            IslandShapeProfile p, float noiseOffsetX, float noiseOffsetZ, float shoreOffsetX, float shoreOffsetZ,
            float noiseScale, float noiseAmplitude)
        {
            // (1) 섬 전체 회전 — 같은 프로파일이라도 섬마다 방향이 다르다.
            float ca = Mathf.Cos(p.spin);
            float sa = Mathf.Sin(p.spin);
            float xr = x * ca + z * sa;
            float zr = -x * sa + z * ca;

            // (2) 이방성. u·v 곱이 보존되므로 늘려도 육지 면적이 줄지 않는다(길쭉한 섬이 야위지 않는다).
            float u = xr * p.stretch;
            float v = zr / p.stretch;
            float re = Mathf.Sqrt(u * u + v * v);
            float ang = Mathf.Atan2(v, u);

            // (3) 각도별 반지름 마스크 → q. q = 1 이 이 프로파일의 해안선이다.
            float mask = MaskAt(ang, p);
            float q = re / Mathf.Max(1e-4f, radius * mask);

            // (4) 만(灣). q를 국소적으로 부풀려 해안을 안쪽으로 밀어 넣는다(초승달).
            if (p.biteStrength > 0f && p.biteRadius > 0f)
            {
                float bx = xr - p.biteX * radius;
                float bz = zr - p.biteZ * radius;
                float bd = Mathf.Sqrt(bx * bx + bz * bz) / (p.biteRadius * radius);
                q *= 1f + p.biteStrength * Bump(bd);
            }

            float qc = Mathf.Min(q, 1f);

            // (5) 기본 낙차. plateauPow < 1 이면 정상부가 평평해지고 가장자리가 가팔라진다.
            //     Cos이 부동소수 오차로 아주 작은 음수가 되면 Pow가 NaN을 내므로 0으로 잘라 둔다.
            float cosTerm = Mathf.Max(0f, Mathf.Cos(qc * Mathf.PI * 0.5f));
            float y = maxHeight * p.heightScale * Mathf.Pow(cosTerm, p.plateauPow);

            // (6) 돔 2개
            if (p.domeAAmp != 0f && p.domeARadius > 0f)
            {
                float dx = xr - p.domeAX * radius;
                float dz = zr - p.domeAZ * radius;
                y += maxHeight * p.domeAAmp * Bump(Mathf.Sqrt(dx * dx + dz * dz) / (p.domeARadius * radius));
            }
            if (p.domeBAmp != 0f && p.domeBRadius > 0f)
            {
                float dx = xr - p.domeBX * radius;
                float dz = zr - p.domeBZ * radius;
                y += maxHeight * p.domeBAmp * Bump(Mathf.Sqrt(dx * dx + dz * dz) / (p.domeBRadius * radius));
            }

            // (7) 능선(선분 거리 감쇠)
            if (p.ridgeAmp != 0f && p.ridgeWidth > 0f)
            {
                float d = SegmentDistance(xr, zr, p.ridgeX0 * radius, p.ridgeZ0 * radius,
                    p.ridgeX1 * radius, p.ridgeZ1 * radius) / (p.ridgeWidth * radius);
                y += maxHeight * p.ridgeAmp * Bump(d);
            }

            // (8) 수로(음의 능선). 깊이가 충분해 y<0까지 내려간다 = 섬 가운데로 물이 흐른다.
            if (p.channelDepth != 0f && p.channelWidth > 0f)
            {
                float d = SegmentDistance(xr, zr, p.channelX0 * radius, p.channelZ0 * radius,
                    p.channelX1 * radius, p.channelZ1 * radius) / (p.channelWidth * radius);
                y -= maxHeight * p.channelDepth * Bump(d);
            }

            // (9) 석호: 링(양) + 중앙 분지(음)
            if (p.ringAmp != 0f && p.ringWidth > 0f)
            {
                float rr = Mathf.Sqrt(xr * xr + zr * zr);
                y += maxHeight * p.ringAmp * Bump(Mathf.Abs(rr - p.ringRadius * radius) / (p.ringWidth * radius));
            }
            if (p.basinDepth != 0f && p.basinRadius > 0f)
            {
                float rr = Mathf.Sqrt(xr * xr + zr * zr);
                y -= maxHeight * p.basinDepth * Bump(rr / (p.basinRadius * radius));
            }

            // (10) 메사(고원). 절벽 방향은 가장자리 폭이 좁고(급경사), 반대쪽은 넓다(걸어 오르는 우회로).
            if (p.mesaAmp != 0f && p.mesaRadius > 0f)
            {
                float mx = xr - p.mesaX * radius;
                float mz = zr - p.mesaZ * radius;
                float rr = Mathf.Sqrt(mx * mx + mz * mz);
                float a = Mathf.Atan2(mz, mx);
                float soft = p.mesaSoftMin
                    + (p.mesaSoftMax - p.mesaSoftMin) * 0.5f * (1f - Mathf.Cos(a - p.mesaCliffAngle));
                float k = Mathf.Clamp01(((1f + soft) - rr / (p.mesaRadius * radius)) / Mathf.Max(1e-4f, soft));
                y += maxHeight * p.mesaAmp * (k * k * (3f - 2f * k)); // smoothstep
            }

            // (11) 다중 옥타브 노이즈. 해안 쪽에서 줄여 물가 등고선이 지저분해지지 않게 한다.
            y += Fbm(x, z, noiseOffsetX, noiseOffsetZ, noiseScale * p.roughness)
                 * noiseAmplitude * p.noiseAmp * (1f - qc * 0.85f);

            // (12) 해안 잠수. q 기준이라 마스크·만으로 안쪽에 들어온 해안에서도 똑같이 동작한다.
            //      q > 1 구간(프로파일 해안 바깥 ~ 메시 테두리)은 계속 더 내려가 바다 평면 아래에 잠긴다.
            float shoreT = (q - (1f - ShoreBandFraction)) / ShoreBandFraction;
            float band = Mathf.Clamp01(shoreT);
            float submerge = ShoreSubmergeDepth * band * band
                             + Mathf.Min(ShelfDropMax, ShelfDropPerQ * Mathf.Max(0f, q - 1f));
            float shoreNoise =
                (Mathf.PerlinNoise(x * ShoreNoiseScale + shoreOffsetX, z * ShoreNoiseScale + shoreOffsetZ) - 0.5f)
                * ShoreNoiseAmplitude * band;

            return y - submerge + shoreNoise;
        }

        /// <summary>
        /// 지정한 반지름과 최대 높이를 가진 둥근 언덕 모양의 섬 지형 메시를 생성한다.
        /// 중심에서 가장자리로 갈수록 코사인 곡선으로 완만하게 낮아지고,
        /// 펄린 노이즈로 자연스러운 굴곡을 더하되 가장자리에서는 노이즈를 줄여 매끄럽게 물과 맞닿게 한다.
        /// 바깥 ShoreBandFraction 구간은 해수면 아래로 잠기며(위 상수 주석), 그 덕에 물가 경계가 원이 아니다.
        ///
        /// [B46] noiseSeed를 넘기면 지형/해안 펄린의 **샘플 위치(오프셋)만** 갈라져 섬마다 다른 지형이 나온다.
        /// 넘기지 않으면(기본값 LegacyNoiseSeed) 오프셋이 예전 상수 그대로라 **예전과 동일한 메시**가 나온다.
        /// 정점 수·인덱스·UV·삼각형 감는 방향·XZ 반경은 시드와 무관하게 1mm도 바뀌지 않는다 - 바뀌는 것은 y뿐이다.
        ///
        /// [B47] shapeProfile을 넘기면 높이가 **프로파일 8종 중 하나로 조각된다**(위 [B47] 주석 블록).
        /// 마스크로 윤곽이 원에서 벗어나고, 돔/능선/수로/석호/메사가 얹히며, 노이즈가 3옥타브가 된다.
        /// 메시 토폴로지는 여전히 한 줄도 바뀌지 않는다 - 조각되는 것은 y뿐이다.
        ///
        /// ★ 회귀 안전장치 ★ noiseSeed를 생략하면(= LegacyNoiseSeed) shapeProfile 값과 무관하게
        /// **예전 코사인 돔 경로**로 들어간다. 그 분기 안의 식은 이번 배치에서 한 글자도 손대지 않았다.
        /// </summary>
        /// <param name="noiseSeed">
        /// IslandMeshGenerator.ComputeNoiseSeed(worldSeed, islandId)로 만든 값.
        /// 생략하면 예전 고정 오프셋(1000 / 517) + 예전 코사인 돔 높이식을 그대로 쓴다.
        /// </param>
        /// <param name="shapeProfile">
        /// IslandMeshGenerator.SelectShapeProfile(worldSeed, islandId)로 고른 프로파일 번호(0~7).
        /// 음수를 넘기면 noiseSeed만으로 유도한다(호출부가 islandId를 모르는 경우의 폴백).
        /// </param>
        /// <param name="radialMask">
        /// 각도별 반지름 마스크를 외부에서 주입한다(0번 = +X축, 반시계 등간격, 값 = R 대비 육지 반경 0.15~1.0).
        /// 나중에 "위에서 본 섬 사진"에서 뽑은 윤곽 배열을 그대로 꽂는 자리다. null이면 프로파일 하모닉을 쓴다.
        /// </param>
        public static Mesh GenerateIslandMesh(float radius, float maxHeight, int ringCount = 6, int radialSegments = 24, float noiseScale = 0.05f, float noiseAmplitude = 2.0f, int noiseSeed = LegacyNoiseSeed, int shapeProfile = -1, float[] radialMask = null)
        {
            var mesh = new Mesh();
            mesh.name = "IslandTerrain";

            // 시드가 없으면 예전 상수 그대로다. 아래 두 펄린 호출은 오프셋만 변수로 바뀌었고 식은 동일하므로,
            // 이 분기에서 예전 값이 들어가면 결과가 예전과 비트 단위로 같다.
            float noiseOffsetX = 1000f;
            float noiseOffsetZ = 1000f;
            float shoreOffsetX = 517f;
            float shoreOffsetZ = 517f;
            bool legacy = noiseSeed == LegacyNoiseSeed;
            IslandShapeProfile profile = null;
            if (!legacy)
            {
                // 축마다 다른 salt를 쓴다(x/z에 같은 오프셋을 주면 모든 섬이 대각선 대칭축을 공유한다).
                // 해안 펄린도 **따로** 갈라야 물가 모양이 섬마다 달라진다 - 지형 오프셋만 바꾸면
                // 해수면 등고선의 들쭉날쭉한 무늬가 전 섬 동일하게 남는다.
                noiseOffsetX = 1000f + NoiseOffsetFromSeed(noiseSeed, 0x51ED270Bu);
                noiseOffsetZ = 1000f + NoiseOffsetFromSeed(noiseSeed, 0x1B873593u);
                shoreOffsetX = 517f + NoiseOffsetFromSeed(noiseSeed, 0x27D4EB2Fu);
                shoreOffsetZ = 517f + NoiseOffsetFromSeed(noiseSeed, 0x165667B1u);

                // [B47] 프로파일 선택. 호출부가 islandId를 아는 경우 SelectShapeProfile로 미리 고른
                // 값을 넘겨주고(첫 8개 섬이 서로 다른 프로파일을 받는 것이 보장된다), 모르면 시드로
                // 유도한다. 어느 쪽이든 난수를 소비하지 않는 순수 해시다.
                int chosen;
                unchecked
                {
                    // 음수 int → uint 캐스팅은 checked 컨텍스트에서 예외가 된다. 이 파일의 다른 해시들과
                    // 같은 방식으로 명시적으로 unchecked에 넣는다(NoiseOffsetFromSeed 참고).
                    chosen = shapeProfile >= 0
                        ? shapeProfile % ShapeProfileCount
                        : 1 + (int)(Mix32((uint)noiseSeed ^ 0x6A09E667u) % (uint)(ShapeProfileCount - 1));
                }
                profile = BuildProfile(chosen, noiseSeed);
                profile.radialMask = radialMask;
            }

            int vertexCount = 1 + ringCount * radialSegments;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            // 중심점 (인덱스 0). 조각된 지형에서는 중심이 반드시 최고점이 아니다(석호는 중심이 물이고,
            // 메사는 중심이 비껴 있다) - 그래서 maxHeight를 그대로 쓰지 않고 높이장을 실제로 평가한다.
            // 예전 경로에서는 여전히 maxHeight다(회귀 안전장치).
            vertices[0] = new Vector3(0f,
                legacy
                    ? maxHeight
                    : SculptHeight(0f, 0f, radius, maxHeight, profile,
                        noiseOffsetX, noiseOffsetZ, shoreOffsetX, shoreOffsetZ, noiseScale, noiseAmplitude),
                0f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            int index = 1;
            for (int ring = 1; ring <= ringCount; ring++)
            {
                float t = (float)ring / ringCount; // 0(중심)~1(가장자리)
                float r = t * radius;
                float baseHeight = maxHeight * Mathf.Cos(t * Mathf.PI * 0.5f);

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;

                    float y;
                    if (!legacy)
                    {
                        // [B47] 조각 경로. XZ는 위에서 이미 정해졌고(예전과 동일) y만 새로 만든다.
                        y = SculptHeight(x, z, radius, maxHeight, profile,
                            noiseOffsetX, noiseOffsetZ, shoreOffsetX, shoreOffsetZ, noiseScale, noiseAmplitude);
                    }
                    else
                    {
                        float noise = (Mathf.PerlinNoise(x * noiseScale + noiseOffsetX, z * noiseScale + noiseOffsetZ) - 0.5f) * noiseAmplitude * (1f - t);

                        // 바깥 ShoreBandFraction 구간에서만 0 → 1로 자라는 진행도. 안쪽은 정확히 0이라
                        // 이 블록 전체가 무효가 되고, 예전 높이식과 결과가 완전히 같다.
                        float shoreT = Mathf.Clamp01((t - (1f - ShoreBandFraction)) / ShoreBandFraction);
                        if (shoreT <= 0f)
                        {
                            y = Mathf.Max(0f, baseHeight + noise);
                        }
                        else
                        {
                            // 제곱으로 내려서 물가 근처는 완만하고(걸어 들어갈 수 있는 얕은 경사)
                            // 테두리로 갈수록 빠르게 깊어지게 한다.
                            float submerge = ShoreSubmergeDepth * shoreT * shoreT;
                            float shoreNoise =
                                (Mathf.PerlinNoise(x * ShoreNoiseScale + shoreOffsetX, z * ShoreNoiseScale + shoreOffsetZ) - 0.5f)
                                * ShoreNoiseAmplitude * shoreT;
                            y = baseHeight + noise - submerge + shoreNoise;
                        }
                    }

                    vertices[index] = new Vector3(x, y, z);
                    uvs[index] = new Vector2(x / radius * 0.5f + 0.5f, z / radius * 0.5f + 0.5f);
                    index++;
                }
            }

            var triangles = new List<int>();

            // 중심 -> 첫 번째 링 (부채꼴)
            // 주의: c, b 순서로 추가해야 위쪽을 향하는 법선이 나온다 (b, c 순서면 아래를 향해 컬링되어
            // 지형 중앙에 구멍이 뚫린 것처럼 보이고, 콜라이더도 그 구멍으로 플레이어가 빠지는 버그가 있었다).
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int b = 1 + seg;
                int c = 1 + (seg + 1) % radialSegments;
                triangles.Add(0);
                triangles.Add(c);
                triangles.Add(b);
            }

            // 링과 링 사이 (사각형을 삼각형 2개로)
            for (int ring = 1; ring < ringCount; ring++)
            {
                int ringStart = 1 + (ring - 1) * radialSegments;
                int nextRingStart = 1 + ring * radialSegments;

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    int a = ringStart + seg;
                    int b = ringStart + (seg + 1) % radialSegments;
                    int c = nextRingStart + seg;
                    int d = nextRingStart + (seg + 1) % radialSegments;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(d);

                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 지형 메시를 잘라내 만든 "지면 캡" 4장(B11에 3 → 4)을 지형 바로 위에 덮어, 단색이던 지면에 색 변화를 준다.
        ///
        /// 왜 머티리얼 교체가 아니라 덮개 메시인가: 지형 머티리얼은 WorldMapManager가 만들고(이 배치의
        /// 편집 범위 밖) 섬 전체에 하나만 적용되므로, 그것만으로는 해안과 내륙을 나눌 수 없다. 셰이더를
        /// 새로 만들 수도 없다. 그래서 WorldMapManager가 얕은 물 띠(ShorelineBand)를 별도 고리 메시로
        /// 해결한 것과 정확히 같은 방식 - 별도 메시 + 별도 머티리얼 - 을 그대로 따른다.
        ///
        /// 4장의 구성(안쪽 → 바깥쪽):
        ///   HighlandCap  : 섬 정상부의 밝은 풀. 고도(정점 y)로 잘라내 능선이 색으로 드러나게 한다.
        ///   GrassCap     : 내륙 풀밭. 경계는 각도에 따라 출렁여 자로 그은 원이 되지 않는다.
        ///   DrySandCap   : 마른 모래 해변.
        ///   WetSandCap   : 해안의 젖은 모래 띠. 마른 모래 → 젖은 모래 → 얕은 물 띠(ShorelineBand)로 이어진다.
        ///
        /// [B11] 예전에는 "캡 3장 사이에 드러나는 지형 기본색(Island Sand)이 네 번째 톤"이었는데, 그 노출이
        /// 실기 "황갈색 각진 얼룩" 신고의 정체였다(아래 BuildGroundCaps 본문 주석에 값과 근거). 지금은
        /// **지형 본체가 GrassCap과 같은 초록**이고 모래는 전부 덮개다. 즉 덮개에 구멍이 나도 드러나는 것이
        /// 같은 초록이라 얼룩이 될 수 없다 - 지면 4단은 유지하면서 실패 모드만 없앤 구조다.
        /// GrassCap/DrySandCap/WetSandCap은 서로 겹치지 않는 정확한 여집합이고 합집합이 섬 전체다.
        /// </summary>
        private static void BuildGroundCaps(Transform surfaceRoot, GameObject islandObject, float radius,
            float phaseA, float phaseB)
        {
            // [B15 기록] 지면 패치의 출처를 여기서 확정했다. 이 메서드 첫 줄에 `return;` 을 넣어
            // 캡을 통째로 끄자 패치가 완전히 사라졌다 — 가설 7개를 거친 뒤에야 반으로 가르는 실험을
            // 한 것이 이 추적의 교훈이다. 같은 의심이 또 생기면 같은 방법을 먼저 써라.
            // (const bool 플래그로 남기면 CS0162 "도달 불가 코드" 경고가 나므로 코드로 두지 않는다.)
            var sourceFilter = islandObject.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                return; // islandPlaceholderPrefab을 쓰는 구성이면 지형 메시를 알 수 없으므로 조용히 건너뛴다.

            Mesh source = sourceFilter.sharedMesh;

            // 지형 최대 높이는 WorldMapManager.terrainMaxHeight(인스펙터 값, 실기에서 2.5 → 8로 상향)라
            // 코드 상수로 가정하면 안 된다. 메시 바운즈에서 읽어 항상 실제 지형에 맞춘다.
            // [B15] HighlandCap 제거로 이 값을 읽는 곳이 사라졌지만, 캡 오프셋 주석과 진단 시
            // 지형 높이를 확인하는 기준으로 남긴다. 사용처가 없으면 컴파일러가 CS0219를 내므로
            // 실제로 안 쓰게 되면 지워야 한다 - 지금은 아래 주석이 참조한다.
            _ = Mathf.Max(0.01f, source.bounds.max.y);

            // 캡을 띄우는 높이. 8cm 고정이었는데, terrainMaxHeight가 3배 넘게 커지면 같은 8cm도 상대적으로
            // 얇아져 원거리에서 깊이 정밀도에 눌릴 수 있다. 지형 기복에 비례시키되 하한 8cm를 유지한다.
            // (주의: 지형은 y=f(x,z) 단일값 높이장이므로 캡을 +Y로 평행이동하면 경사가 아무리 급해도
            //  절대 지형에 파묻히지 않는다 - 경사면에서 수직 간격이 cos(경사각)배로 줄 뿐이다.
            //  실기에서 풀밭이 안 보였던 원인은 이 오프셋이 아니라 캡 색이었다. 아래 GrassCap 주석 참고.)
            // [B14 디렉터] peakHeight 비례를 버리고 8cm 고정으로 되돌린다.
            // terrainMaxHeight가 2.5 → 8로 오르면서 이 값이 0.165m가 됐는데, TerrainSampler.SnapToGround는
            // **캡이 아니라 지형 콜라이더** 기준으로 스냅한다. 그 결과 눈에 보이는 지면(캡)이 배치물보다
            // 0.165m 위에 있어, 납작한 자원 노드(천조각 0.05m · 금속조각 0.06m · 부싯돌 0.10m)가 통째로
            // 캡 아래에 묻히고 풀포기도 절반 이상 잠겼다. "고쳤는데 안 보이더라"의 정체다.
            // 8cm면 z-파이팅을 피하면서 가장 납작한 노드(0.05m)만 살짝 걸린다 — 스포너를 건드리는 것보다
            // 부작용이 작다(스포너는 세이브 키를 쥔 파일이라 손대는 비용이 크다).
            const float capOffset = 0.08f;

            // [B9 이음매 사고 원인] 실기에서 "지면 한가운데에 직선 경계의 사각형 얼룩"이 보고됐다.
            // 코드로 특정한 원인은 아래 HighlandCap의 고도 컷이다. 근거(값은 terrainMaxHeight=8 기준
            // 실제 메시를 재현해 계산):
            //   · 지형 높이 = maxHeight·cos(t·π/2) + perlin(x·0.05, z·0.05)·2·(1-t)
            //   · 정상부(고도 컷이 걸리는 t≤0.33 구간)에서 코사인 항의 낙차는 0.92~1.07m인데
            //     펄린 항의 진폭은 1.33~1.38m다 → 컷 등고선을 결정하는 것은 반지름이 아니라 펄린이다.
            //   · 그 펄린은 noiseScale 0.05, 즉 격자 한 칸이 정확히 20m인 축 정렬(axis-aligned) 격자다.
            //     시작 섬(반지름 50)에서 캡 지름은 33m = 격자 1.7칸 → 캡이 사실상 펄린 격자 한 칸이 되어
            //     경계가 X/Z축에 나란한 직선으로 잘린다. 게다가 캡 중심은 항상 섬 중심 = 플레이어
            //     시작 지점이라 그 직선이 화면 정중앙 지면에 온다. 신고 내용과 정확히 일치한다.
            // 배제한 후보(추측이 아니라 값으로 확인):
            //   (a) z-파이팅: 겹치는 캡 쌍은 Grass↔Highland 하나뿐이고 둘은 6cm 벌어져 있다. reversed-Z
            //       깊이에서 6cm는 100m 거리에서도 밀리미터 단위 여유가 있어 지글거림이 나올 수 없다.
            //       실제 증상도 "지글"이 아니라 "고정된 직선 경계"였다.
            //       (B11 이후 값: Grass/DrySand/WetSand 0.165m 공통, Highland 0.225m. 앞의 셋은 서로
            //        겹치지 않는 여집합이라 같은 높이여도 깊이 충돌 자체가 없다 - 아래 B11 주석 참고.)
            //   (c) 텍스처 타일링 경계: GrassCap과 HighlandCap은 UV 소스(지형 메시)와 타일 배수
            //       (radius×0.75)가 완전히 동일해 서로 어긋날 수 없고, 타일 경계라면 얼룩 하나가 아니라
            //       섬 전체에 2.7m 간격으로 반복돼야 한다.
            // 조치: 고도 컷을 "삼각형 단위 디더"로 흩뜨려 연속된 직선 경계가 아예 생길 수 없게 하고,
            //       밝기 단차도 1.18배 → 1.10배로 낮춘다. 원형 경계(GrassCap/WetSandCap)에도 같은
            //       디더를 얇게 걸어 네 캡의 경계 처리를 한 방식으로 통일한다.

            // [B10 "각진 삼각형 얼룩" 후속] 직선 이음매는 위 디더로 사라졌지만, 실기에 **옅은 각진
            // 삼각형 얼룩**이 남았다. 남은 원인은 경계가 아니라 **톤 자체**다. 값으로 특정한 근거:
            //   · ToneIndex의 삼각형 단위 해시 디더 진폭이 0.55(=±0.275)였는데 톤 한 칸의 폭은
            //     1/toneCount = 0.333이다. 즉 디더가 칸 폭의 165%라, 저주파 펄린이 만들려던 "넓은
            //     얼룩"이 완전히 묻히고 **모든 삼각형이 사실상 무작위로 3톤에 배정**됐다.
            //     경계만 점묘가 되는 것이 아니라 캡 전체가 소금·후추 노이즈가 된 것이다.
            //   · 그 3톤의 차이가 명도(±6%)라 이웃 삼각형 사이 최대 단차가 1.06/0.94 = **12.8%**다.
            //     넓고 평평하게 조명된 면에서 12.8% 명도 단차는 육안 식별 한계(수 %)를 크게 넘는다.
            //     삼각형 하나하나가 도드라져 보이는 이유가 이것이고, 평면 셰이딩이 아니라 톤 배정이
            //     범인이다(캡 메시는 RemapVertex가 정점을 공유해 스무스 셰이딩된다 - 위 주석 참고).
            // 정점 색 검토(디렉터 요청): **불가능하다.** URP Lit 셰이더는 정점 색 입력 자체가 없고
            //   (Attributes에 color 시맨틱이 없다), 이 프로젝트는 셰이더/Shader Graph 에셋이 0개라
            //   (AGENT_BRIEF 1장) 정점 색을 읽는 셰이더를 만들 수단이 없다. 그래서 "서브메시를 늘리지
            //   않고 정점 색으로 부드러운 그라데이션"은 이 파이프라인에서 성립하지 않는다.
            // 조치(서브메시 개수는 그대로 3/1/2, 드로우콜 변화 0):
            //   (1) 톤 변주를 명도가 아니라 **색상**으로 준다. ToneVariant가 상대휘도를 기준색에 정확히
            //       고정하므로 톤 사이 명도 단차가 **0%**가 된다 - 삼각형이 밝기 얼룩으로 보일 수가 없다.
            //       사람 눈은 색차의 공간 해상도가 명도보다 훨씬 낮아(크로마 서브샘플링이 성립하는 이유)
            //       같은 크기의 변주라도 삼각형 단위에서는 거의 보이지 않고 넓은 패치에서만 읽힌다.
            //       이 파일이 잎 머티리얼에 이미 쓰고 있는 규칙("변주는 명도가 아니라 색상으로")과 같다.
            //   (2) ToneIndex의 삼각형 디더를 0.55 → 0.20으로 낮춰, 톤 배정이 저주파 펄린(격자 ≈29m)에
            //       지배되게 되돌린다. 패치 경계 근처 삼각형만 섞이므로 원래 의도였던 "경계만 점묘"가 된다.
            //   (3) HighlandCap의 명도 단차 1.10배(=10%)도 같은 이유로 1.05배로 낮추고, 부족해진 구분은
            //       색상(Frond Green 쪽으로 0.55) 으로 채운다. 능선은 조명 자체가 달라 5%면 충분히 읽힌다.

            // 캡 경계를 흩뜨리는 폭. 삼각형 하나(2~5m)보다 넓은 띠에 걸쳐 포함/제외가 섞이게 만들어
            // 경계가 선이 아니라 점묘(stipple)로 읽히게 하는 것이 목적이다.
            // [B47] 단위가 "반지름 비율"에서 **미터**로 바뀌었다(아래 높이 경계 주석 참고).

            // [B11 "황갈색 각진 조각" 원인규명 — 지면 캡 구조 자체를 뒤집는다]
            //
            // 신고: 초록 지면 위에 **밝은 황갈색 각진 조각**이 여러 개 뭉쳐 보인다. 색은 지형 기본색
            // (0.76, 0.7, 0.5)와 같은 계열. 즉 "덮개"가 아니라 **덮개가 없는 구멍으로 비치는 지형 본체**다.
            //
            // 값으로 배제한 후보(재조사 금지):
            //   · GrassCap 디더가 중심부 삼각형을 빠뜨렸을 가능성 → **불가능하다.**
            //     GrassBoundaryRadius의 하한은 0.51R(=25.5m), 디더는 ±radius·0.05/2 = ±1.25m라
            //     컷은 아무리 안쪽으로 와도 24.25m다. 중심 10m 안 삼각형은 어떤 Hash01 값에도 통과한다.
            //   · 캡 좌표계가 밀렸을 가능성 → **아니다.** GenerateIslandMesh는 정점을 원점 대칭으로 굽고
            //     CreateProceduralIslandTerrain은 그 메시를 담은 오브젝트의 world position만 옮긴다
            //     (스케일 1, 자식 오프셋 0). 지형 메시 로컬 원점 = 섬 중심이 성립한다.
            //   · WetSandCap이 내륙을 침범했을 가능성 → **아니다.** 안쪽 경계는 0.84R ± 1.0m = 41~43m다.
            //
            // 실제 원인: **"3장 사이에 노출되는 원래 지형색을 네 번째 톤으로 쓴다"는 설계 자체다.**
            // GrassCap 바깥 끝(0.51~0.73R)과 WetSandCap 안쪽 끝(0.84R) 사이 = 시작 섬 기준 31~42m 고리가
            // 통째로 맨 지형이고, 이것만으로 **섬 바닥 면적의 32%**다. 그 고리의 안쪽 경계는 삼각형 단위
            // 디더로 들쭉날쭉해서 풀밭 쪽으로 삼각형이 하나씩 떨어져 나온다 — 신고된 "각진 조각"이 그것이다.
            //   왜 "중심에서 10m"로 보였나: 시작 섬은 반지름 50m에 높이 8m인 돔이고 캡 중심 = 플레이어
            //   시작 지점 = 돔 꼭대기다. 눈높이 1.6m에서 내려다본 부각은 r=10m에서 11.3°, r=31m에서 9.4°,
            //   r=42m에서 10.3° — **10m부터 50m까지가 화면상 2.4° 안에 전부 겹친다.** 시점을 낮출수록 더
            //   겹친다. 즉 "잔해(7m) 주변"과 "풀밭 경계(31m)"는 이 지형에서 눈으로 구분할 수 없다.
            //
            // 조치(구조 반전): **지형 본체를 초록(Meadow Green)으로 바꾸고, 모래를 덮개로 만든다.**
            //   (1) WorldMapManager.CreateDefaultTerrainMaterial의 기본색 = Meadow Green = GrassCap 기준색.
            //       → 캡에 구멍/틈/이음매가 생겨도 드러나는 것이 같은 초록이라 **원리적으로 안 보인다.**
            //   (2) 사라진 마른 모래 띠를 DrySandCap으로 명시 생성한다(예전 노출 고리와 같은 색·같은 범위).
            //   (3) 세 캡의 경계식을 **한 벌의 로컬 함수로 공유**해 서로 정확한 여집합이 되게 한다.
            //       한쪽만 고쳐 틈이 벌어지는 사고(이번 건의 재발 경로)를 코드 구조로 막는 것이 목적이다.
            //   (4) 겹치지 않는 세 캡은 **같은 yOffset**을 쓴다. 예전에는 Grass 0.165 / WetSand 0.082로
            //       8cm 어긋나 있어, 낮은 시점에서 경계마다 떠 있는 턱과 그 아래 지형이 비쳤다.
            //       같은 높이면 정점이 일치해 이음매가 아예 없다(겹치지 않으므로 z-파이팅도 없다).
            //   HighlandCap만 GrassCap과 겹치므로 지금처럼 혼자 +0.06m 위에 둔다.
            //
            // [B47 — 경계를 반경이 아니라 **높이**로 바꾼다] ★ 이번 배치에서 반드시 함께 고쳐야 했던 부분 ★
            //
            // 예전 세 경계(GrassEdge 0.51~0.755R / SandEdge 0.84R / DampEdge 0.90R)는 전부 "반경의 몇 배"
            // 였고, 그것이 성립한 이유는 단 하나 - **물가가 항상 0.93~0.97R의 원이었기 때문**이다.
            // 이번 배치부터 각도별 반지름 마스크(0.70~1.00R) 때문에 물가가 방위마다 다른 반경에 있고,
            // 가운데 수로·석호·만처럼 **섬 안쪽에도 물가가 생긴다.** 반경 기준을 그대로 두면
            //   · 마스크가 작은 방위에서는 모래 띠 전체가 물에 잠겨 **해변이 사라지고**
            //   · 마스크가 큰 방위에서는 풀밭이 물가까지 내려가 **초록 테**가 생기며
            //   · 수로/석호의 안쪽 물가에는 모래가 아예 생기지 않는다.
            //
            // 조치: 세 캡의 경계를 **해수면 기준 높이**로 잡는다. 해변의 높이(수십 cm)는 섬 규모와
            // 무관한 물리량이라 반지름 비례로 잡을 이유가 애초에 없었고, 높이 기준이면 물가가 어디에
            // 있든(바깥이든 수로 안쪽이든) 저절로 따라간다.
            //   마른 모래  : DampTop  ~ DryTop     (약 0.75 ~ 1.30m)
            //   축축한 모래: WetTop   ~ DampTop
            //   젖은 모래  : WetTop 아래 전부      (해수면 아래 포함 - 어차피 바다 평면에 가려진다)
            //   DryTop 위  : 지형 본체 = Meadow Green 초원(B11 구조 그대로, 덮개 없음)
            //
            // 디더는 그대로 유지하되 단위만 미터로 바꾼다. 세 경계가 **같은 Hash01(centroid) 한 값**에
            // **같은 계수**를 곱해 쓰므로 삼각형 하나에서 세 경계가 통째로 평행 이동한다 - 간격
            // 0.55m / 0.45m가 어떤 해시 값에서도 **정확히 보존**되어 원리적으로 뒤집힐 수 없다
            // (겹침 = z-파이팅, 틈 = 지형 노출 - 둘 다 이 파일의 전력이라 구조로 막는다).
            //
            // BandWobble은 방위에 따라 해변 폭을 출렁이게 하는 항이다. 역시 세 경계에 **같은 값**을
            // 더하므로 간격은 그대로다. 섬마다 다른 위상(phaseA/phaseB)을 쓰던 예전 GrassBoundaryRadius의
            // 역할을 여기가 이어받는다 - 그래야 BuildIslandSurface가 뽑아 둔 난수 2회(위상)가 계속
            // 의미를 갖는다(그 2회를 없애면 같은 worldSeed에서 기존 숲 배치가 통째로 밀린다).
            float heightDither = 0.36f;
            float BandWobble(float angle) =>
                0.22f * Mathf.Sin(angle * 2f + phaseA) + 0.12f * Mathf.Sin(angle * 3f + phaseB);
            float DryTop(Vector3 centroid, float angle) =>
                1.30f + BandWobble(angle) + (Hash01(centroid) - 0.5f) * heightDither;
            float DampTop(Vector3 centroid, float angle) =>
                0.75f + BandWobble(angle) + (Hash01(centroid) - 0.5f) * heightDither;
            float WetTop(Vector3 centroid, float angle) =>
                0.30f + BandWobble(angle) + (Hash01(centroid) - 0.5f) * heightDither;

            // 내륙 풀밭. 예전 색은 Shade(PalmFiber, 0.82) = #79733E로, Island Sand(#C2B280)와 색상각이
            // 각각 54°/45°로 9°밖에 차이 나지 않는 같은 황토 계열에 휘도만 1.58배 낮은 값이었다.
            // 그래서 실기에서 "풀밭"이 아니라 "그늘진 모래"로 읽혀 캡이 있는지조차 확인되지 않았다.
            // Meadow Green(#8AA84F, 색상각 80°)으로 바꿔 색상 자체로 구분되게 한다.
            // toneCount 3: 같은 초록 한 장이 섬을 덮던 문제(아래 BuildCapLayer 주석) 해소용.
            // [B11] 이제 지형 본체도 같은 Meadow Green이라, 이 캡은 "색을 덮는 판"이 아니라 "톤을 얹는 판"이다
            //       - 빠진 삼각형이 있어도 그 자리에 같은 색이 있을 뿐이라 얼룩이 될 수 없다.
            // [B15] **GrassCap과 HighlandCap을 제거했다.**
            // 이유: 지형 본체를 모래 → Meadow Green으로 뒤집은 순간(B14) 이 두 캡은 "같은 색 위에
            // 같은 색을 덮는 판"이 됐다. 지형 본체와 색·텍스처("leaf")·타일링(radius×0.75)이 전부
            // 같으므로 화면에 더하는 정보가 0인데, 지면에서 0.08m 떠 있는 별도 메시라는 사실 때문에
            // **각진 패치**만 만들고 있었다.
            // 실기 확정: 캡 3장을 전부 끄자 패치가 완전히 사라지고 지면이 매끈한 단색이 됐다.
            // 이 추적에서 틀린 가설을 7개 거쳤다(풀포기 / 미리보기 UI / z-파이팅 / 텍스처 타일링 /
            // 캡 셀렉터 버그 / 지형 본체 노출 / 톤 변주). 마지막 두 개는 실재하는 별개 결함이라
            // 고친 것이 맞지만, 신고된 증상의 원인은 "덮을 필요가 없는 것을 덮고 있었다"였다.
            // 모래 캡 2장은 남긴다 — 지형 본체와 색이 다르므로 실제로 해변을 만든다.

            // [B11 신규] 마른 모래 해변. 예전에는 "캡을 안 덮어서 드러난 지형"이 이 역할을 했는데, 바로
            // 그 노출이 이번 얼룩 신고의 정체였다. 같은 그림을 **명시적인 덮개**로 다시 만든다 -
            // 색은 예전 지형 기본색과 같은 Island Sand(#C2B280)이므로 해변의 겉모습은 달라지지 않는다.
            // 범위는 GrassCap의 정확한 여집합(안쪽) ~ WetSandCap의 정확한 여집합(바깥)이다.
            BuildCapLayer(surfaceRoot, source, radius, "DrySandCap", StructureVisualBuilder.IslandSand,
                capOffset, radius * 1.5f, "sand",
                (centroid, distance, angle) =>
                    centroid.y >= DampTop(centroid, angle) && centroid.y < DryTop(centroid, angle),
                // 젖은 모래와 같은 규칙의 2톤(채도만 내리는 색조 변주, 명도 단차 0%).
                1);

            // [B22 신규] 축축한 모래. 마른 모래와 젖은 모래 사이의 중간 단계다.
            // 예전에는 마른(100%) → 젖은(80%) 두 단계뿐이라 밝기가 한 번에 20% 떨어져,
            // 해변 한가운데에 **동심원 한 줄**이 그어진 것처럼 보였다. 88%를 사이에 끼워 단차를
            // 20% → 12%/10%로 나눈다(경계마다 ±1.5~2%R 디더가 걸려 있어 선이 아니라 점묘로 읽힌다).
            BuildCapLayer(surfaceRoot, source, radius, "DampSandCap", Shade(StructureVisualBuilder.IslandSand, 0.88f),
                capOffset, radius * 1.5f, "sand",
                (centroid, distance, angle) =>
                    centroid.y >= WetTop(centroid, angle) && centroid.y < DampTop(centroid, angle),
                1);

            // 해안의 젖은 모래. [B11] 바깥 한계 0.955R을 없애고 메시 가장자리까지 덮는다.
            // 예전에는 0.955R~1.0R이 맨 지형이었는데 그 색이 마침 모래였을 뿐이다 - 지형이 초록이 된
            // 지금 그대로 두면 물가에 초록 테가 생긴다. 물 띠(ShorelineBand, 0.95R~)와 겹치는 구간은
            // 반투명 얕은 물 아래로 젖은 모래가 비치는 그림이라 오히려 맞다.
            // [B22] 안쪽 경계가 SandEdge → DampEdge로 밀렸고(위 DampSandCap이 그 사이를 채운다),
            // 이 띠의 바깥쪽 절반은 이제 해수면 아래로 잠겨 불투명한 바다 평면에 가려진다 -
            // 즉 화면에 남는 것은 "물에 막 닿은 가장 어두운 모래" 한 줄이다.
            BuildCapLayer(surfaceRoot, source, radius, "WetSandCap", Shade(StructureVisualBuilder.IslandSand, 0.78f),
                capOffset, radius * 1.5f, "sand",
                (centroid, distance, angle) => centroid.y < WetTop(centroid, angle),
                1);
        }

        /// <summary>
        /// 지형 메시에서 조건에 맞는 삼각형만 골라내 덮개 메시 1장을 만든다.
        ///
        /// 지형 메시의 정점을 그대로 복사해 쓰기 때문에 굴곡이 100% 일치한다(링/세그먼트 개수 계산식을
        /// WorldMapManager와 중복 정의할 필요가 없다 - 그 계산식이 나중에 바뀌어도 자동으로 따라간다).
        /// 콜라이더는 붙이지 않는다(플레이어는 원래 지형 콜라이더 위를 걷는다).
        /// </summary>
        /// <param name="selector">(삼각형 무게중심, 중심축까지의 XZ 거리, 각도) → 이 삼각형을 포함할지.</param>
        /// <param name="toneCount">
        /// 캡 하나를 서브메시 몇 장으로 쪼개 서로 다른 밝기로 칠할지(1이면 기존과 100% 동일한 단색).
        ///
        /// [B9 "지형 색이 한 톤"] 캡 1장 = 단색 1개라, 같은 초록이 반지름 200m를 통째로 덮어 실기에서
        /// 평평한 색판으로 보였다. 정점 색(URP Lit은 정점 색을 읽지 않는다)도, 캡 메시 추가(드로우콜과
        /// 오브젝트가 캡마다 늘어난다)도 쓰지 않고 톤을 늘릴 수 있는 방법은 하나뿐이다 - 이미 만든 캡
        /// 메시를 서브메시로 쪼개 머티리얼 슬롯만 늘리는 것. GameObject·MeshFilter·정점은 그대로 1벌이고
        /// 늘어나는 것은 드로우콜 (toneCount-1)개뿐이다(섬당 총 +3). 초목 프리미티브 상한 180과는
        /// 무관하다 - 프리미티브를 하나도 추가하지 않는다.
        /// </param>
        /// <param name="toneSpread">
        /// 마지막 톤이 toneShift 쪽으로 얼마나 섞이는지(0~1). [B10] 예전에는 "밝기 폭(±비율)"이었는데,
        /// 명도 변주가 삼각형 단위 얼룩의 직접 원인이라 **색상 혼합 비율**로 의미를 바꿨다.
        /// 상대휘도는 ToneVariant가 기준색에 고정하므로 이 값이 아무리 커도 명도 단차는 0이다.
        /// </param>
        /// <param name="toneShift">톤이 섞여 들어갈 상대 색. 비우면 변주 없음(단색)과 같다.</param>
        private static void BuildCapLayer(Transform surfaceRoot, Mesh source, float radius, string name,
            Color color, float yOffset, float textureTiling, string textureName,
            System.Func<Vector3, float, float, bool> selector, int toneCount = 1, float toneSpread = 0.30f,
            Color? toneShift = null)
        {
            Vector3[] sourceVertices = source.vertices;
            int[] sourceTriangles = source.triangles;
            Vector2[] sourceUvs = source.uv;
            bool hasSourceUv = sourceUvs != null && sourceUvs.Length == sourceVertices.Length;

            toneCount = Mathf.Clamp(toneCount, 1, 4);

            var remap = new Dictionary<int, int>(sourceVertices.Length);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var toneTriangles = new List<int>[toneCount];
            for (int i = 0; i < toneCount; i++)
                toneTriangles[i] = new List<int>();

            int selectedCount = 0;
            for (int t = 0; t + 2 < sourceTriangles.Length; t += 3)
            {
                int i0 = sourceTriangles[t];
                int i1 = sourceTriangles[t + 1];
                int i2 = sourceTriangles[t + 2];

                Vector3 centroid = (sourceVertices[i0] + sourceVertices[i1] + sourceVertices[i2]) / 3f;
                float distance = new Vector2(centroid.x, centroid.z).magnitude;
                float angle = Mathf.Atan2(centroid.z, centroid.x);
                if (!selector(centroid, distance, angle))
                    continue;

                var bucket = toneTriangles[ToneIndex(centroid, toneCount)];
                bucket.Add(RemapVertex(i0, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                bucket.Add(RemapVertex(i1, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                bucket.Add(RemapVertex(i2, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                selectedCount++;
            }

            if (selectedCount == 0)
                return;

            // 비어 있는 톤은 서브메시를 만들지 않는다(빈 서브메시는 드로우콜만 소모한다).
            var usedTones = new List<int>(toneCount);
            for (int i = 0; i < toneCount; i++)
            {
                if (toneTriangles[i].Count > 0)
                    usedTones.Add(i);
            }

            var mesh = new Mesh();
            mesh.name = $"Island{name}";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = usedTones.Count;
            for (int s = 0; s < usedTones.Count; s++)
                mesh.SetTriangles(toneTriangles[usedTones[s]], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(surfaceRoot, false);
            // 지형과 정확히 같은 높이면 z-파이팅으로 지글거린다. 캡마다 다른 오프셋을 줘서 캡끼리도
            // 겹치는 구간(HighlandCap ⊂ GrassCap)에서 깊이 충돌이 나지 않게 한다.
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[usedTones.Count];
            for (int s = 0; s < usedTones.Count; s++)
            {
                // 톤 0 → 기준색 그대로, 마지막 톤 → toneShift 쪽으로 toneSpread만큼(toneCount 1이면 0).
                // 상대휘도는 전 톤이 동일하다(ToneVariant) - 이웃 삼각형 사이 명도 단차 0%.
                float mix = toneCount <= 1
                    ? 0f
                    : usedTones[s] / (float)(toneCount - 1) * toneSpread;
                var material = StructureVisualBuilder.CreateColorMaterial(
                    ToneVariant(color, toneShift ?? color, mix), textureName);
                // UV가 섬 전체에 0~1로 정규화돼 있어(GenerateIslandMesh) 타일 반복을 반지름에 비례시키지
                // 않으면 큰 섬에서 잎 무늬 한 칸이 수십 미터로 늘어나 흐릿한 단색이 된다.
                // WorldMapManager.CreateDefaultTerrainMaterial의 모래 타일링과 같은 계산 방식이다.
                material.mainTextureScale = new Vector2(textureTiling, textureTiling);
                // 톤마다 타일 위상을 어긋나게 해, 같은 그레인 무늬가 톤 경계에서 이어지며
                // "색만 다른 같은 얼룩"으로 보이지 않게 한다.
                material.mainTextureOffset = new Vector2(usedTones[s] * 0.37f, usedTones[s] * 0.19f);
                materials[s] = material;
            }
            renderer.sharedMaterials = materials;

            // 지면에 몇 cm 떠 있는 덮개라 그림자를 드리우면 자기 그림자로 얼룩진다. 받기만 한다
            // (야자수 그림자는 풀밭 위에 정상적으로 떨어져야 한다).
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// 원본 지형 정점을 캡 메시로 옮기고(중복 없이) 새 인덱스를 돌려준다.
        /// </summary>
        private static int RemapVertex(int sourceIndex, Dictionary<int, int> remap, Vector3[] sourceVertices,
            Vector2[] sourceUvs, bool hasSourceUv, float radius, List<Vector3> vertices, List<Vector2> uvs)
        {
            if (remap.TryGetValue(sourceIndex, out int existing))
                return existing;

            Vector3 v = sourceVertices[sourceIndex];
            int newIndex = vertices.Count;
            vertices.Add(v);
            uvs.Add(hasSourceUv
                ? sourceUvs[sourceIndex]
                : new Vector2(v.x / radius * 0.5f + 0.5f, v.z / radius * 0.5f + 0.5f));
            remap[sourceIndex] = newIndex;
            return newIndex;
        }

        // [B47] GrassBoundaryRadius(풀밭/모래의 반경 경계)는 제거했다. B15에서 GrassCap이 사라진 뒤로
        // 이 함수의 유일한 사용처는 모래 캡의 안쪽 경계였고, 그 경계가 이번에 "반경"에서 "높이"로
        // 바뀌면서(BuildGroundCaps의 [B47] 주석) 호출자가 하나도 남지 않았다. 각도별 출렁임은
        // BandWobble이 같은 phaseA/phaseB로 이어받는다.

        /// <summary>
        /// 팔레트 색의 명도만 바꾼 변주를 만든다(알파는 항상 1로 유지 - URP Lit Opaque에서 알파가
        /// 딸려 어두워지는 실수를 막는다). 새 색을 만드는 것이 아니라 같은 색의 밝기 단계다.
        /// factor > 1(밝게)도 허용하되 채널을 0~1로 잘라, 밝히는 쪽에서 색이 흰색으로 튀거나
        /// URP Lit이 HDR 범위를 받아 과노출되는 것을 막는다.
        /// </summary>
        private static Color Shade(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                1f);
        }

        /// <summary>
        /// 명도(HSV의 V)와 색상각은 그대로 두고 채도(S)만 배율로 바꾼다.
        ///
        /// 왜 Shade로는 안 되는가: Shade는 세 채널에 같은 수를 곱하므로 HSV 채도가 정확히 보존된다.
        /// 즉 "어둡게" 하면 명도만 떨어지고 유채색량(chroma = max-min)이 같이 줄어, 밝은 배경(하늘)
        /// 앞에서 색상 정보가 남지 않는 검은 실루엣이 된다 - 야자수 줄기에서 실제로 일어난 일이다.
        /// 채도를 따로 올릴 수단이 필요해서 짝이 되는 헬퍼를 둔다(새 팔레트 색을 만드는 것이 아니라
        /// 같은 색상각 위의 변주라는 점은 Shade와 같다).
        /// </summary>
        private static Color Saturate(Color color, float factor)
        {
            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            if (max <= 0.0001f || max - min <= 0.0001f)
                return new Color(color.r, color.g, color.b, 1f); // 무채색은 채도를 곱해도 무채색이다.

            float saturation = Mathf.Clamp01((max - min) / max * factor);
            float newMin = max * (1f - saturation);
            float scale = (max - newMin) / (max - min);
            return new Color(
                Mathf.Clamp01(newMin + (color.r - min) * scale),
                Mathf.Clamp01(newMin + (color.g - min) * scale),
                Mathf.Clamp01(newMin + (color.b - min) * scale),
                1f);
        }

        /// <summary>Rec.709 상대휘도. 톤 변주가 명도를 건드리지 않았는지 판정하는 기준이다.</summary>
        private static float Luma(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        /// <summary>
        /// 기준색과 같은 상대휘도를 유지한 채 색상만 shiftTarget 쪽으로 amount만큼 민 변주를 만든다.
        ///
        /// 왜 Shade가 아니라 이것인가(B10): 지면 캡의 톤 변주를 명도로 주면 이웃 삼각형 사이에 명도
        /// 단차가 생기고, 넓고 평평하게 조명된 지면에서 그 단차는 몇 %만 되어도 "각진 삼각형 얼룩"으로
        /// 읽힌다(실기 보고). 색상 변주는 같은 크기라도 삼각형 단위에서는 거의 보이지 않는다 - 사람 눈의
        /// 색차 공간 해상도가 명도보다 훨씬 낮기 때문이다. 여기서 휘도를 강제로 되맞추므로 명도 단차는
        /// 정확히 0이 되고, 남는 것은 넓은 패치에서만 읽히는 색조 변화뿐이다.
        /// </summary>
        private static Color ToneVariant(Color baseColor, Color shiftTarget, float amount)
        {
            if (amount <= 0.0001f)
                return new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

            Color mixed = Color.Lerp(baseColor, shiftTarget, Mathf.Clamp01(amount));
            float mixedLuma = Luma(mixed);
            if (mixedLuma <= 0.0001f)
                return new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

            return Shade(mixed, Luma(baseColor) / mixedLuma);
        }

        /// <summary>
        /// 야자수 줄기(나무껍질) 색. Driftwood(#8C6640)의 명도를 0.93배로 낮추고 채도를 1.20배로 올린
        /// 변주 = #82582D. 팔레트에 새 색을 추가한 것이 아니라 Driftwood의 한 단계다("목재" 의미 유지).
        /// 수치 근거는 BuildIslandSurface의 머티리얼 생성 지점 주석에 있다.
        /// </summary>
        private static readonly Color PalmBarkColor =
            Saturate(Shade(StructureVisualBuilder.Driftwood, 0.93f), 1.20f);

        /// <summary>
        /// 위치만으로 결정되는 0~1 해시. 난수 스트림을 소비하지 않으므로 재현성(같은 worldSeed = 같은
        /// 숲/지형)에 아무 영향이 없고, 같은 지형 메시면 항상 같은 결과가 나온다.
        /// 입력은 항상 섬 로컬 좌표(|x|,|z| ≤ radius ≤ 200)라 float 정밀도 문제가 생기지 않는다.
        /// </summary>
        private static float Hash01(Vector3 p)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>
        /// 캡 삼각형 하나를 어느 톤(서브메시)에 넣을지 고른다.
        ///
        /// 저주파 펄린(격자 ≈29m)으로 넓은 얼룩을 만들되, 삼각형 단위 해시를 섞어 얼룩의 경계를
        /// 점묘로 흩뜨린다. 이 디더가 없으면 펄린 격자가 축 정렬(axis-aligned)이라는 사실이 그대로
        /// 드러나 직선 경계의 사각 얼룩이 생긴다 - HighlandCap에서 실제로 났던 사고와 같은 원인이다.
        ///
        /// [B10] 디더 진폭을 0.55 → 0.20으로 낮췄다. 톤 한 칸의 폭이 1/toneCount = 0.333인데 0.55는
        /// ±0.275, 즉 칸 폭의 165%라 펄린 패치가 통째로 묻히고 **모든** 삼각형이 무작위 배정됐다
        /// (= 캡 전체가 소금·후추 노이즈). 0.20은 ±0.10 = 칸 폭의 30%라, 패치 경계 근처 삼각형만
        /// 섞이고 패치 안쪽은 한 톤으로 남는다 - 원래 의도했던 "경계만 점묘"가 된다.
        /// </summary>
        private static int ToneIndex(Vector3 centroid, int toneCount)
        {
            if (toneCount <= 1)
                return 0;

            // 펄린은 실제로 0~1을 다 쓰지 않고 대략 0.25~0.75에 몰려 있어, 그대로 나누면 양 끝 톤이
            // 거의 안 쓰인다. 1.6배로 펴서 세 톤이 고르게 나오게 한다.
            float patch = (Mathf.PerlinNoise(centroid.x * 0.035f + 517f, centroid.z * 0.035f + 517f) - 0.5f) * 1.6f + 0.5f;
            float dithered = Mathf.Clamp01(patch + (Hash01(centroid) - 0.5f) * 0.20f);
            return Mathf.Clamp(Mathf.FloorToInt(dithered * toneCount), 0, toneCount - 1);
        }
    }
}
