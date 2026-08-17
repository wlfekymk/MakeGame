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
    public static class IslandMeshGenerator
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
        /// 섬 지형 오브젝트 위에 (1) 내륙 풀밭 캡 메시와 (2) 초목(야자수/덤불/풀포기)을 배치한다.
        ///
        /// 왜 필요했나: 지형은 단색(당시 모래 #C2B280, B11부터 Meadow Green)으로 칠한 메시 하나뿐이고 초목을 만드는 코드는 프로젝트
        /// 어디에도 없었다(WorldMapManager.CreateDefaultTerrainMaterial / CreateProceduralIslandTerrain).
        /// 그래서 실제 게임에 들어가면 반지름 50~200m짜리 모래색 평지만 보였다.
        ///
        /// [콜라이더 절대 금지] 여기서 만드는 오브젝트에는 콜라이더를 단 하나도 붙이지 않는다.
        /// TerrainSampler.SnapToGround가 이름이 "Island_"로 시작하는 콜라이더만 지형으로 인정하는데,
        /// 초목에 콜라이더가 붙으면 (a) 이름 규칙상 지형으로 인정되지는 않더라도 물리 씬에 불필요한
        /// 콜라이더가 수천 개 늘어나고, (b) 이후 누군가 판정 규칙을 손대는 순간 "불러오기 후 모든
        /// 아이템이 하늘로 떠오르는" 사고가 재발한다. 그래서 프리미티브를 만들고 콜라이더를 지우는
        /// (Destroy가 프레임 끝까지 지연되는) 방식조차 쓰지 않고, 아예 콜라이더가 생기지 않는 경로
        /// (공유 메시 + 빈 GameObject + MeshFilter/MeshRenderer)로 만든다. 공유 메시는 내장 프리미티브
        /// (GetPrimitiveMesh)이거나 이 클래스가 만든 저폴리 메시(GetBushClumpMesh/GetGrassBladeMesh 등)이며,
        /// 후자는 프리미티브를 거치지 않으므로 콜라이더가 한 프레임도 존재하지 않는다.
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
            //       인스턴스**라, 야자수 draw가 몇 개 늘든 (islandIndex, spawnOrder) 세이브 키는 불변이다.
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
        /// 콜라이더는 붙이지 않는다(CreatePart 경로 = 프리미티브를 아예 거치지 않는다). 바위는 지금
        /// 통과할 수 있지만, 콜라이더를 붙이는 순간 TerrainSampler와 초목/자원 배치가 전부 영향을 받는다
        /// (파일 상단 [콜라이더 절대 금지] 주석). 물리를 주려면 디렉터 결정이 필요하다.
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
            if (TryGetRockModel(mainWidth, out mainModelMesh, out mainModelSize))
            {
                // 뽑아 둔 mainWidth를 그대로 목표 폭으로 쓰므로 폭 분포(1.7~3.6m)가 1mm도 바뀌지 않는다.
                // 세 모델의 기본 폭이 1.85 / 2.60 / 3.20이고 가장 가까운 것을 고르므로 배율은 0.86~1.21이다.
                float fit = mainWidth / Mathf.Max(0.01f, mainModelSize.x);
                float modelHeight = mainModelSize.y * fit;
                // 매립 비율(높이의 22~34%, 최소 0.2m)은 그대로다. 모델 원점이 밑면이라 파묻는 깊이가
                // 곧 -y이고, 절차 메시처럼 높이의 절반을 더할 필요가 없다.
                float modelSink = Mathf.Max(0.2f, modelHeight * mainSinkFraction);
                CreatePart(cluster.transform, "Deco_RockMain", mainModelMesh,
                    new Vector3(0f, -modelSink, 0f),
                    new Vector3(fit, fit, fit),
                    Quaternion.Euler(0f, mainSpin, 0f),
                    materials[index % materials.Length]);
            }
            else
            {
                // 모델이 없으면(임포트 전·프로브 실패) 예전 절차 메시 그대로다. 이 경로는 지우지 않는다.
                float mainSink = Mathf.Max(0.2f, mainHeight * mainSinkFraction);
                CreatePart(cluster.transform, "Deco_RockMain", GetBoulderMesh(index, true),
                    new Vector3(0f, mainHeight * 0.5f - mainSink, 0f),
                    new Vector3(mainWidth, mainHeight, mainDepth),
                    Quaternion.Euler(mainTiltX, mainSpin, mainTiltZ),
                    materials[index % materials.Length]);
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

            // 메시 규격은 셋 다 [-0.5,0.5]^3 단위 상자다 → 아래 size는 **미터** 그대로이고, 호출부가
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

            // ── [B48] 실물 야자수 모델(palm_a/b/c) ────────────────────────────────────
            //  · 렌더러가 13개(줄기 3 + 잎 5×2) → **2개**(줄기 1 + 크라운 1)가 된다. 모델에 줄기의 휨과
            //    잎의 꺾임이 이미 구워져 있어, 아래 마디 누적/잎 2마디 조립이 통째로 필요 없어진다.
            //  · **크기 규약**: 모델은 이미 미터 규격이다(밑면 y=0 · X/Z는 접지 중심). 절차 메시가
            //    단위 규격이라 호출부가 미터 크기를 스케일로 곱하던 것과 규약이 정반대라, 그대로 곱하면
            //    나무가 몇 배로 부푼다. 그래서 바위와 같이 **fit = 목표 높이 / 모델 실측 높이**의
            //    균등 배율만 쓴다(0.87~1.14).
            //  · 균등 배율 + 회전은 뿌리의 yaw뿐이라 전단이 원리적으로 없다(자식 회전은 identity).
            //  · 콜라이더는 붙이지 않는다 - CreatePart는 프리미티브를 거치지 않고, 모델도 프리팹을
            //    Instantiate하지 않고 sharedMesh만 꺼내 쓰므로 임포터가 붙였을 콜라이더가 씬에 안 들어온다.
            //  · ★ 난수 ★ 변종 선택에 rng를 쓰지 않는다. 위에서 이미 뽑아 둔 height로 고른다. 그리고
            //    아래 잎 루프의 draw는 **모델 경로에서도 전부 그대로 뽑는다**(파일 상단 [결정성] 주석).
            Mesh palmTrunkMesh, palmCrownMesh;
            float palmModelHeight;
            bool useModel = TryGetPalmModel(height, out palmTrunkMesh, out palmCrownMesh, out palmModelHeight);
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
        /// 덤불 한 개(포기 전체가 메시 한 장, 렌더러 1개 · 92삼각형). 야자수보다 낮아 시야를 막지 않는다.
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

            // 메시 규격: x·z ∈ [-0.5, 0.5], **y ∈ [0, 1]이고 원점이 밑동**이다(구 규격이 아니다).
            // 그래서 위치는 지면 그대로, 스케일은 (폭, 높이, 깊이)를 미터로 넣으면 된다.
            // 예전 3파츠 구성과 화면상 크기가 같도록 폭·높이 범위는 한 글자도 바꾸지 않았다.
            int variant = Mathf.Clamp(Mathf.FloorToInt((variantRoll - 0.50f) / 0.26f * 3f), 0, 2);
            var bush = CreatePart(parent, "Veg_Bush", GetBushClumpMesh(variant),
                Vector3.zero, new Vector3(width, height, width * 0.9f),
                Quaternion.Euler(tiltX, yaw, tiltZ), material);
            bush.transform.position = groundPosition;
        }

        /// <summary>
        /// 풀포기 한 개(잎 5장 부채꼴, 양면·2마디라 40삼각형). 개수가 제일 많아 렌더러 1개를 유지한다.
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

            var tuft = CreatePart(parent, "Veg_GrassTuft", GetGrassBladeMesh(),
                Vector3.zero, new Vector3(width, height, width * 0.30f),
                Quaternion.Euler(0f, yaw, lean), material);
            tuft.transform.position = groundPosition + Vector3.up * (height * 0.35f);

            // 풀포기는 5m 밖에서 그림자가 보이지 않는데 개수만 많아, 그림자 드리우기를 끈다
            // (ArtDirection 2장 "폴리곤을 아낄 곳은 5m 밖에서 안 보이는 디테일").
            var renderer = tuft.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 콜라이더가 전혀 붙지 않는 시각 전용 파츠를 만든다.
        /// StructureVisualBuilder.CreateVisualPart는 GameObject.CreatePrimitive로 만든 뒤 콜라이더를
        /// Object.Destroy하는데, Destroy는 프레임 끝까지 지연되므로 그 사이에 실행되는 다른 스포너의
        /// SnapToGround 레이가 초목 콜라이더를 스칠 수 있다. 초목은 개수가 수백 개라 그 위험을 감수할
        /// 이유가 없어, 콜라이더가 애초에 생기지 않는 경로로 따로 만든다.
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            return CreatePart(parent, name, GetPrimitiveMesh(primitiveType),
                localPosition, localScale, localRotation, material);
        }

        /// <summary>
        /// 위와 같지만 내장 프리미티브 대신 이 클래스가 만든 저폴리 메시를 쓴다(B9).
        /// 메시는 반드시 캐시된 공유 메시를 넘겨라 - 파츠마다 새 Mesh를 만들면 수백 개가 쌓인다.
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, Mesh mesh,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (mesh != null)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 프리미티브 종류별 내장 메시를 한 번만 뽑아 캐시한다.
        /// 임시 프리미티브에 자동으로 붙는 콜라이더가 물리 씬에 한 프레임도 남지 않도록, 지연 파괴
        /// (Object.Destroy)에 앞서 즉시 SetActive(false)로 비활성화한다(비활성화는 즉시 반영된다).
        /// 반환하는 메시는 Unity 내장 공유 메시라 임시 오브젝트를 파괴해도 사라지지 않는다.
        /// </summary>
        private static Mesh GetPrimitiveMesh(PrimitiveType primitiveType)
        {
            if (primitiveMeshCache.TryGetValue(primitiveType, out Mesh cached) && cached != null)
                return cached;

            var temporary = GameObject.CreatePrimitive(primitiveType);
            temporary.SetActive(false);
            var filter = temporary.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Object.Destroy(temporary);

            primitiveMeshCache[primitiveType] = mesh;
            return mesh;
        }

        private static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshCache = new Dictionary<PrimitiveType, Mesh>();

        // ─────────────────────────────────────────────────────────────────────────
        //  저폴리 초목 메시 (B9)  —  "가장 안 보이는 파츠가 삼각형 예산의 96%를 쓰던" 문제
        // ─────────────────────────────────────────────────────────────────────────
        //
        // 실측(특대 섬, 교체 전): 풀포기 78×768 = 59,904 · 덤불 로브 120×768 = 92,160 ·
        // 야자수 전체 5,760 → 합계 157,824. 즉 덤불+풀이 96%였다. 반면 야자수는 그루당 360삼각형
        // (내장 Cylinder 3×80 + 큐브 10×12)뿐이라 애초에 쌌다 - B8에서 "그루 수 42 → 16으로 상쇄했다"고
        // 한 것은 렌더러 예산에는 맞았지만 삼각형 관점에서는 잘못된 곳을 줄인 것이었다.
        //
        // 내장 Sphere는 768삼각형짜리 UV 구다. 덤불 로브와 풀포기는 둘 다 비균일 스케일로 납작하게
        // 눌러 쓰는 데다 대부분 5m 밖에서 보이므로, 그 정밀도가 화면에 도달하지 않는다
        // (ArtDirection 2장 "폴리곤을 아낄 곳은 언제나 5m 밖에서 안 보이는 디테일").
        //
        // 두 메시 모두 **내장 Sphere와 동일한 로컬 규격**(지름 1, 중심 원점, [-0.5,0.5]^3)으로 만든다.
        // 그래야 호출부의 스케일·회전·오프셋을 한 줄도 고치지 않고 그대로 쓸 수 있고, B8에서 확정한
        // 실루엣 규칙(덤불: 기울인 로브 3개·폭>>높이 / 풀: 두께가 폭의 30%)이 그대로 보존된다.

        /// <summary>
        /// 덤불 로브용 저폴리 덩어리 = 정이십면체(20삼각형). 내장 Sphere 768삼각형의 1/38.
        ///
        /// 왜 정이십면체인가: 20삼각형만으로 실루엣이 거의 원에 가깝고(면이 균일해 어느 각도에서 봐도
        /// 윤곽이 무너지지 않는다), 평면 셰이딩된 각진 면이 오히려 "매끈한 돌덩이와 덤불을 가른다"는
        /// B8의 목표를 강화한다. 로우폴리/스타일라이즈드 방향(ArtDirection 0장)과도 정확히 맞는다.
        ///
        /// 평면 셰이딩을 위해 정점을 면마다 분리한다(60정점) - 정점 수는 삼각형과 달리 이 프로젝트의
        /// 병목이 아니고, 공유 정점으로 부드럽게 셰이딩하면 20면짜리 저폴리가 찌그러진 구로 보인다.
        /// </summary>
        /// <summary>
        /// [B29] 덤불 한 포기 전체를 담은 메시(로브 3개 + 삐져나온 잎끝 8장, 92삼각형).
        ///
        /// 예전에는 정이십면체 로브 3개가 각각 별도 파츠였다(B9). 형태는 맞았지만 파츠 3개를 쓰면서도
        /// 실루엣은 "매끈한 덩어리 3개"였다 - 덤불에만 있는 신호인 **삐져나온 잎끝**이 없었기 때문이다.
        /// 지금은 로브를 메시 안으로 옮기고(파츠 3 → 1) 그 예산으로 잎끝을 넣었다.
        ///   · 로브: 방향 함수로 반지름을 흔든 각진 덩어리(WorldMeshBuilder.AddChunk) - 매끈한 구가
        ///     아니라 울퉁불퉁해서 바위와 형태가 겹치지 않는다.
        ///   · 잎끝: 두께 없는 양면 사각면 8장. 윤곽선 위로 튀어나와야 20m 밖에서 "잎"으로 읽힌다.
        ///
        /// **규격(호출부와의 계약): x·z ∈ [-0.5, 0.5], y ∈ [0, 1], 원점이 밑동.**
        /// 구 규격([-0.5,0.5]^3)이 아니다. CreateBush가 위치에 지면 좌표를 그대로 넣고 스케일에
        /// (폭, 높이, 깊이)를 미터로 넣는 것이 이 규격 때문이다 - 둘 중 하나만 바꾸면 안 된다.
        /// </summary>
        private static Mesh GetBushClumpMesh(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "bushClump" + v;
            Mesh cached;
            if (decorationMeshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            int seed = 4300 + v * 17;

            builder.AddChunk(new Vector3(0f, 0.42f, 0f), new Vector3(1.00f, 0.80f, 0.94f), seed, 0.30f, 0);
            builder.AddChunk(new Vector3(-0.17f, 0.62f, 0.12f), new Vector3(0.62f, 0.58f, 0.60f), seed + 5, 0.34f, 0);
            builder.AddChunk(new Vector3(0.19f, 0.57f, -0.14f), new Vector3(0.56f, 0.54f, 0.54f), seed + 11, 0.34f, 0);

            // 잎끝: 로브 표면에서 바깥·위로 뻗는다. 끝을 가늘게 좁혀 "판"이 아니라 "잎"으로 읽히게 한다.
            const int bladeCount = 8;
            for (int i = 0; i < bladeCount; i++)
            {
                float angle = (i * 360f / bladeCount + v * 17f) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                float lift = 0.52f + 0.10f * Mathf.Sin(angle * 3f + v);
                Vector3 baseCenter = outward * 0.26f + new Vector3(0f, lift, 0f);
                Vector3 tipCenter = outward * (0.46f + 0.04f * Mathf.Sin(angle * 2f))
                    + new Vector3(0f, lift + 0.34f, 0f);

                Vector3 b0 = baseCenter - side * 0.075f;
                Vector3 b1 = baseCenter + side * 0.075f;
                Vector3 t1 = tipCenter + side * 0.018f;
                Vector3 t0 = tipCenter - side * 0.018f;
                builder.AddQuad(b0, b1, t1, t0, Vector3.up, true);
            }

            Mesh mesh = builder.Finish("Veg_BushClump" + v);
            decorationMeshCache[key] = mesh;
            return mesh;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  [B45] 실물 바위 모델 (rock_a / rock_b / rock_c)
        // ─────────────────────────────────────────────────────────────────────────
        //
        // ── 좌표 계약(에셋이 이렇게 구워져 있다. 여기서 축·원점을 다시 만지지 않는다) ──────────
        //   단위 = 미터 · +Y 위 · +Z 정면 · **밑면이 정확히 y = 0** · X/Z 중심 정렬 · UV 있음.
        //   실측: rock_a 1.85×1.20×1.60 · rock_b 2.60×1.55×2.30 · rock_c 3.20×2.35×2.60 (W×H×D),
        //   각각 3,366 / 3,364 / 3,366삼각형(AssetPipeline 2장 "중형 소품 4,000" 이내).
        //
        // ── 절차 메시와 규약이 반대다 ─────────────────────────────────────────────────
        //   GetBoulderMesh는 [-0.5,0.5]^3 단위 규격이라 호출부가 (폭,높이,깊이)를 미터로 곱했다.
        //   이 모델들은 이미 미터라 같은 스케일을 곱하면 2~3배로 부푼다. 모델 경로는 목표 폭을
        //   모델 실측 폭으로 나눈 **균등 배율** 하나만 쓴다(CreateRockCluster 참고).
        //
        // ── 프리팹을 Instantiate하지 않는다 ──────────────────────────────────────────
        //   OBJ는 Resources.Load<GameObject>로만 온다(Mesh로는 null). 하지만 필요한 것은 메시 한 장뿐이고,
        //   이 파일의 파츠 생성 경로(CreatePart)가 이미 "빈 GameObject + MeshFilter + MeshRenderer"라
        //   프리팹 인스턴스를 만들면 계층·컴포넌트·머티리얼 슬롯이 공짜로 딸려 온다. 게다가 임포터
        //   설정에 따라 **MeshCollider가 딸려 올 수 있는데** 이 파일은 콜라이더가 한 프레임도 존재하면
        //   안 된다(파일 상단 [콜라이더 절대 금지]). 메시만 꺼내 쓰면 그 위험이 구조적으로 없다.
        //
        // ── 머티리얼 ────────────────────────────────────────────────────────────────
        //   새로 만들지 않는다. BuildIslandSurface가 만든 rockMaterials(WeatheredStone × "rock" 텍스처,
        //   ResourceVisualLibrary.GetMaterial 공유 캐시 = "MG~" 접두사 · enableInstancing)를 그대로 받는다.

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음 - 붙이면 항상 null이 돌아온다).</summary>
        private static readonly string[] RockModelResourcePaths =
        {
            "Models/rock_a", "Models/rock_b", "Models/rock_c"
        };

        /// <summary>각 모델의 실측 크기(m, W×H×D). 위 경로와 인덱스가 일대일로 대응한다.</summary>
        private static readonly Vector3[] RockModelSizes =
        {
            new Vector3(1.85f, 1.20f, 1.60f),
            new Vector3(2.60f, 1.55f, 2.30f),
            new Vector3(3.20f, 2.35f, 2.60f),
        };

        private static readonly Mesh[] rockModelMeshes = new Mesh[3];
        private static int rockModelProbeFrame = -1;

        /// <summary>
        /// 목표 폭에 가장 가까운 바위 모델의 **공유 메시**를 돌려준다. 하나도 못 찾으면 false다.
        ///
        /// [로드 규칙] Resources.Load는 정적 필드 초기자에서 부르지 않는다 - 초기자는 생성자 시점에
        /// 돌 수 있고 Unity가 그 시점의 Load를 막아 null을 준다. 그리고 **실패를 영구히 캐시하지 않는다.**
        /// 그 null을 "에셋 없음"으로 굳히면 세션 내내 절차 바위만 나온다(곰 모델이 실제로 그렇게 죽었다,
        /// AGENT_BRIEF 4장 3번). 성공할 때까지 프레임당 한 번만 다시 살핀다 -
        /// CreatureVisualBuilder.BearModelPrefab과 같은 패턴이고, 섬 하나가 바위를 최대 12개 만들므로
        /// 프레임 가드가 없으면 한 프레임에 Load가 36번 불린다.
        /// </summary>
        private static bool TryGetRockModel(float targetWidth, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            bool anyMissing = false;
            for (int i = 0; i < rockModelMeshes.Length; i++)
            {
                if (rockModelMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && rockModelProbeFrame != Time.frameCount)
            {
                rockModelProbeFrame = Time.frameCount;
                for (int i = 0; i < rockModelMeshes.Length; i++)
                {
                    if (rockModelMeshes[i] != null)
                        continue;

                    // OBJ는 GameObject로 온다. 메시는 루트 또는 그 자식의 MeshFilter.sharedMesh다
                    // (Unity의 OBJ 임포터는 `o` 그룹을 자식으로 만들 수도, 루트에 얹을 수도 있다).
                    var prefab = Resources.Load<GameObject>(RockModelResourcePaths[i]);
                    if (prefab == null)
                        continue;

                    var filter = prefab.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        filter = prefab.GetComponentInChildren<MeshFilter>(true);
                    if (filter != null)
                        rockModelMeshes[i] = filter.sharedMesh;
                }
            }

            // 변종 선택에 난수를 쓰지 않는다. 이미 뽑아 둔 목표 폭에 **가장 가까운 기본 폭**을 고르므로
            // 결정적이고(같은 worldSeed면 같은 변종), 배율이 항상 1 근처(0.86~1.21)라 모델이 늘어나 보이지 않는다.
            float bestDelta = float.MaxValue;
            for (int i = 0; i < rockModelMeshes.Length; i++)
            {
                if (rockModelMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(RockModelSizes[i].x - targetWidth);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                mesh = rockModelMeshes[i];
                size = RockModelSizes[i];
            }

            return mesh != null;
        }

        // ── [B48] 야자수 실물 모델 ──────────────────────────────────────────────────
        //   위 바위 로더와 **같은 패턴**이다(프레임당 1회 프로브 · 실패를 영구 캐시하지 않음 ·
        //   프리팹을 Instantiate하지 않고 공유 메시만 꺼냄 · 폴백 경로 유지).
        //   다른 점은 하나뿐이다: 야자수 OBJ는 `o` 오브젝트가 **2개**(줄기 + 크라운)라 머티리얼이
        //   둘(갈색 껍질 / 초록 잎)이고, 메시도 두 장을 꺼내야 한다.

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음 - 붙이면 항상 null이 돌아온다).</summary>
        private static readonly string[] PalmModelResourcePaths =
        {
            "Models/palm_a", "Models/palm_b", "Models/palm_c"
        };

        /// <summary>각 모델의 실측 전체 높이(m, 밑면 y=0 기준). 위 경로와 인덱스가 일대일로 대응한다.</summary>
        private static readonly float[] PalmModelHeights = { 5.295f, 6.789f, 7.954f };

        private static readonly Mesh[] palmTrunkMeshes = new Mesh[3];
        private static readonly Mesh[] palmCrownMeshes = new Mesh[3];
        private static int palmModelProbeFrame = -1;

        /// <summary>
        /// 목표 높이에 가장 가까운 야자수 모델의 **공유 메시 두 장**(줄기 / 크라운)을 돌려준다.
        /// 하나도 못 찾으면 false이고, 그때 호출부는 예전 절차 메시로 돌아간다.
        ///
        /// [로드 규칙] TryGetRockModel과 동일하다 - Resources.Load를 정적/필드 초기자에서 부르지 않고,
        /// 실패를 영구히 캐시하지 않는다(그 null을 굳히면 세션 내내 절차 야자수만 나온다,
        /// AGENT_BRIEF 4장 3번). 성공할 때까지 **프레임당 한 번만** 다시 살핀다 - 섬 하나가 야자수를
        /// 최대 16그루 만들므로 프레임 가드가 없으면 한 프레임에 Load가 48번 불린다.
        ///
        /// [변종 선택] 난수를 쓰지 않는다. 이미 뽑아 둔 목표 높이(4.6~7.6m)에 **가장 가까운 기본 높이**를
        /// 고르므로 결정적이고(같은 worldSeed면 같은 변종), 균등 배율이 0.87~1.14에 머물러 모델이
        /// 늘어나 보이지 않는다.
        /// </summary>
        private static bool TryGetPalmModel(float targetHeight, out Mesh trunk, out Mesh crown, out float modelHeight)
        {
            trunk = null;
            crown = null;
            modelHeight = 1f;

            bool anyMissing = false;
            for (int i = 0; i < palmTrunkMeshes.Length; i++)
            {
                if (palmTrunkMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && palmModelProbeFrame != Time.frameCount)
            {
                palmModelProbeFrame = Time.frameCount;
                for (int i = 0; i < palmTrunkMeshes.Length; i++)
                {
                    if (palmTrunkMeshes[i] != null)
                        continue;

                    // `o` 2개짜리 OBJ에서 줄기/잎 메시를 갈라 꺼내는 공용 로더(자원 노드의 대나무와 공유).
                    Mesh loadedTrunk, loadedCrown;
                    if (!ResourceVisualLibrary.TryLoadTwoPartModel(PalmModelResourcePaths[i], out loadedTrunk, out loadedCrown))
                        continue;

                    palmTrunkMeshes[i] = loadedTrunk;
                    palmCrownMeshes[i] = loadedCrown;
                }
            }

            float bestDelta = float.MaxValue;
            for (int i = 0; i < palmTrunkMeshes.Length; i++)
            {
                if (palmTrunkMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(PalmModelHeights[i] - targetHeight);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                trunk = palmTrunkMeshes[i];
                crown = palmCrownMeshes[i];
                modelHeight = PalmModelHeights[i];
            }

            return trunk != null;
        }

        /// <summary>
        /// [B29] 큰 바위 / 곁에 붙는 작은 덩어리의 공유 메시(구 규격 [-0.5,0.5]^3).
        ///
        /// large면 정이십면체를 한 번 소분할한 80면, 아니면 20면이다. 면 수를 크기에 맞춰 나누는 이유는
        /// ArtDirection 2장의 디테일 밀도 규칙 그대로다 - 3m짜리 바위는 화면을 크게 차지하니 20면이면
        /// 실루엣이 각져 보이고, 0.8m짜리 곁돌은 80면을 줘도 화면에 도달하지 않는다.
        /// 변주는 4종뿐이고 전부 정적 캐시라, 섬 9개의 바위 전부가 이 8장을 나눠 쓴다.
        ///
        /// [B45] large=true 경로는 이제 **폴백 전용**이다(모델이 없을 때만 큰 덩어리에 쓰인다).
        /// large=false(곁돌)는 예전 그대로 항상 이 메시를 쓴다 - 지우지 마라, 두 경로 다 살아 있어야 한다.
        /// </summary>
        private static Mesh GetBoulderMesh(int variant, bool large)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = (large ? "boulder" : "rockChip") + v;
            Mesh cached;
            if (decorationMeshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = WorldMeshBuilder.Chunk("Deco_" + key, Vector3.one,
                large ? 7100 + v * 13 : 7700 + v * 19, large ? 0.32f : 0.44f, large ? 1 : 0);
            decorationMeshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 표류 궤짝(구 규격 [-0.5,0.5]^3). 몸통 상자 하나에 모서리 기둥 4개와 결속 띠 1개를
        /// 겹쳐 구웠다 - 널판 사이의 홈이 실루엣이 아니라 그림자로 읽히는 것이 목표라 파츠로 나눌 이유가 없다.
        /// 자연물과 인공물을 가르는 신호(ArtDirection 2장 4번)인 "각진 모서리 + 결속"을 그대로 쓴다.
        /// </summary>
        private static Mesh GetCrateMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("crate", out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            builder.AddBox(Vector3.zero, new Vector3(0.86f, 0.86f, 0.86f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * 0.43f;
                float z = (i % 2 == 0 ? -1f : 1f) * 0.43f;
                builder.AddBox(new Vector3(x, 0f, z), new Vector3(0.14f, 1.0f, 0.14f), Quaternion.identity);
            }
            builder.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(1.0f, 0.13f, 0.92f), Quaternion.identity);
            builder.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(0.92f, 0.13f, 1.0f), Quaternion.identity);

            Mesh mesh = builder.Finish("Deco_Crate");
            decorationMeshCache["crate"] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 표류 통(구 규격 [-0.5,0.5]^3). 배가 부른 옆선과 테 2줄을 **반지름 변화만으로** 만든다
        /// (B28에서 대나무 마디를 원반 파츠 → 줄기 굵기 변화로 바꾼 것과 같은 처리다).
        /// 8각이라 옆면이 각져서, 같은 자리에 있는 바위(둥근 덩어리)와 실루엣이 겹치지 않는다.
        /// </summary>
        private static Mesh GetBarrelMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("barrel", out cached) && cached != null)
                return cached;

            float[] heights = { -0.50f, -0.46f, -0.30f, -0.26f, 0f, 0.26f, 0.30f, 0.46f, 0.50f };
            float[] radii = { 0.355f, 0.395f, 0.445f, 0.480f, 0.500f, 0.480f, 0.445f, 0.395f, 0.355f };

            var centers = new Vector3[heights.Length];
            for (int i = 0; i < heights.Length; i++)
                centers[i] = new Vector3(0f, heights[i], 0f);

            var builder = new WorldMeshBuilder();
            builder.AddTube(centers, radii, 8, true, true, 1f);

            Mesh mesh = builder.Finish("Deco_Barrel");
            decorationMeshCache["barrel"] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 밀려온 널판 더미(구 규격 [-0.5,0.5]^3, 호출부가 2.1m × 0.22m × 0.86m로 늘린다).
        /// 널판 3장을 서로 다른 각도로 겹쳐 한 메시에 구웠다 - 판이 어긋나 쌓인 그림이라야 "쌓아 둔 것"이
        /// 아니라 "밀려와 걸린 것"으로 읽힌다.
        /// </summary>
        private static Mesh GetPlankPileMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("plankPile", out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(0.02f, -0.33f, -0.22f), new Vector3(0.96f, 0.32f, 0.30f),
                Quaternion.Euler(0f, 5f, 0f));
            builder.AddBox(new Vector3(-0.03f, 0f, 0.10f), new Vector3(0.90f, 0.32f, 0.28f),
                Quaternion.Euler(0f, -8f, 0f));
            builder.AddBox(new Vector3(0.05f, 0.33f, -0.05f), new Vector3(0.78f, 0.30f, 0.26f),
                Quaternion.Euler(0f, 13f, 4f));

            Mesh mesh = builder.Finish("Deco_PlankPile");
            decorationMeshCache["plankPile"] = mesh;
            return mesh;
        }

        /// <summary>[B29] 장식물(덤불 포기·바위·표류물) 공유 메시 캐시. 월드 전체가 15장 안팎을 나눠 쓴다.</summary>
        private static readonly Dictionary<string, Mesh> decorationMeshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// 야자수 줄기 마디용 저폴리 프리즘(8각, 28삼각형). 내장 Cylinder 80삼각형의 35%.
        ///
        /// 규격은 **내장 Cylinder와 완전히 동일**하다 - 정점이 반지름 0.5 원 위에 놓이고 높이는 y = -1~+1.
        /// 그래야 CreatePalm의 스케일 식 (segmentRadius*2, segmentLength*0.53, segmentRadius*2)의 의미가
        /// 한 글자도 바뀌지 않는다(굵기 보정은 스케일 식이 아니라 baseRadius 범위에서 한다 - 그쪽 주석 참고).
        ///
        /// 삼각형 내역: 옆면 8×2 = 16, 캡 2×(8-2) = 12 → 28. 캡은 중심 정점 없이 모서리에서 부채꼴로
        /// 감아(=n-2개) 삼각형을 아낀다. 캡을 아예 빼면 12삼각형까지 내려가지만, 마디가 6% 겹쳐 있다는
        /// 전제가 깨지는 순간(기울기 누적이 커지면 이음매가 벌어질 수 있다) 줄기 속이 뚫려 보이므로 남긴다.
        ///
        /// 옆면 법선은 반경 방향으로 **직접 지정**한다. RecalculateNormals에 맡기면 정점을 면마다 나눈
        /// 구조가 아니어도 면 법선이 평균돼 결과가 파이프라인 버전에 의존하고, 무엇보다 평면 셰이딩이
        /// 되면 이웃 면 사이 45° 법선 단차가 그대로 밝기 단차로 나온다(PalmTrunkSides 주석의 근거).
        /// 캡은 정점을 따로 두고 ±Y 법선을 줘 옆면과 섞이지 않게 한다.
        /// </summary>
        private static Mesh GetPalmTrunkPrismMesh()
        {
            if (palmTrunkPrismMesh != null)
                return palmTrunkPrismMesh;

            const int sides = PalmTrunkSides;
            const float radius = 0.5f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            // 옆면: 이음매 정점을 한 번 더 둬서 UV가 한 바퀴 돌 때 되감기지 않게 한다.
            int sideStart = vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 p = radial * radius;
                float u = (float)i / sides;

                vertices.Add(new Vector3(p.x, -1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 0f));

                vertices.Add(new Vector3(p.x, 1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 1f));
            }

            int topStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, 1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            int bottomStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, -1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            Vector3[] positions = vertices.ToArray();
            var triangles = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                int b0 = sideStart + i * 2;
                int t0 = b0 + 1;
                int b1 = b0 + 2;
                int t1 = b0 + 3;

                float mid = ((float)i + 0.5f) / sides * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                AddOrientedTriangle(triangles, positions, b0, t0, t1, outward);
                AddOrientedTriangle(triangles, positions, b0, t1, b1, outward);
            }

            for (int i = 1; i < sides - 1; i++)
            {
                AddOrientedTriangle(triangles, positions, topStart, topStart + i, topStart + i + 1, Vector3.up);
                AddOrientedTriangle(triangles, positions, bottomStart, bottomStart + i, bottomStart + i + 1, Vector3.down);
            }

            var mesh = new Mesh { name = "Veg_PalmTrunkPrism" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); // 법선은 위에서 직접 넣었으므로 RecalculateNormals를 부르면 안 된다.

            palmTrunkPrismMesh = mesh;
            return palmTrunkPrismMesh;
        }

        /// <summary>
        /// 삼각형 하나를 감김 방향까지 맞춰 넣는다. 기하 법선이 reference와 반대면 감김을 뒤집는다.
        /// 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로 옮기면 통째로 안쪽을 향해 컬링되는
        /// 사고가 반복됐다(BuildFlatShadedMesh 주석). 표를 믿지 않고 계산으로 확정하는 방식을 그대로 쓴다.
        /// </summary>
        private static void AddOrientedTriangle(List<int> triangles, Vector3[] positions,
            int i0, int i1, int i2, Vector3 reference)
        {
            Vector3 geometric = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
            if (Vector3.Dot(geometric, reference) < 0f)
            {
                int swap = i1;
                i1 = i2;
                i2 = swap;
            }

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        /// <summary>
        /// 풀포기용 저폴리 잎다발 = 부채꼴로 벌린 잎 5장(양면·2마디라 40삼각형). 내장 Sphere(768)의 1/19.
        ///
        /// 풀포기는 이미 "두께를 폭의 30%로 눌러 좌우로 눕힌" 형태라(B8), 눌린 구가 실제로 화면에서
        /// 하던 일은 "위로 솟은 납작한 잎 다발"이었다. 그 실루엣은 평면 조합으로 그대로 재현되고,
        /// 오히려 끝이 뾰족한 잎이 생겨 풀로 더 잘 읽힌다.
        ///
        /// 잎은 단면이라 뒷면에서 보이지 않으므로 감김을 뒤집은 사본을 함께 넣어 양면으로 만든다
        /// (양면 셰이더나 두께가 있는 상자를 쓰는 것보다 싸다). 규격은 Sphere와 같은
        /// [-0.5,0.5]^3 이라 호출부의 (width, height, width*0.30) 스케일 의미가 그대로 유지된다.
        /// </summary>
        private static Mesh GetGrassBladeMesh()
        {
            if (grassBladeMesh != null)
                return grassBladeMesh;

            var points = new List<Vector3>();
            var faces = new List<int>();

            // [B9 디렉터 수정] 잎 3장 × 폭 0.30 은 실기에서 "풀"이 아니라 **반투명 판때기**로 보였다.
            // 원인은 지오메트리가 아니라 비례다 - 호출부 스케일(폭 0.7~1.5m)에 잎이 3장뿐이라
            // 한 장이 0.5m 폭짜리 벽이 됐다. 잎을 늘리고 각각을 가늘게 해야 풀로 읽힌다.
            // 잎 5장을 0°/40°/78°/118°/155°로 벌린다. 호출부가 z를 30%로 누르므로 결과는 부채꼴이 된다.
            float[] yaws = { 0f, 40f, 78f, 118f, 155f };
            float[] tipHeights = { 0.50f, 0.34f, 0.44f, 0.30f, 0.40f };   // 끝 높이를 다르게 해 윗변이 평평해지지 않게 한다
            float[] tipOuts = { 0.30f, 0.18f, 0.38f, 0.16f, 0.26f };      // 바깥으로 벌어지는 정도

            // [B29] 잎 하나를 **2마디로 꺾어** 휘게 만들었다. 곧은 사각형 한 장은 어느 각도에서 봐도
            // 직선 윤곽이라 5장을 부채꼴로 펴도 "삐죽한 판"으로 읽혔다. 중간 마디를 바깥으로 조금만
            // 내보내면 윤곽선이 곡선이 되고 끝이 아래로 처져 풀로 읽힌다(야자수 잎을 2마디로 꺾은
            // B8의 처리와 같은 이유다 - 이 프로젝트에서 식물을 식물로 만드는 것은 언제나 꺾임이다).
            // 규격은 그대로 [-0.5,0.5]^3이라 호출부 스케일 (width, height, width*0.30)의 의미가 안 바뀐다.
            for (int i = 0; i < yaws.Length; i++)
            {
                float rad = yaws[i] * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 side = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

                // 폭: 밑동 0.10 → 중간 0.06 → 끝 0.03. 잎이 "칼날"이 아니라 "풀잎"으로 읽히는 최소 비례다
                // (높이 1.0 대비 폭 0.10 = 10:1). 이전 0.30은 3.3:1이라 판때기였다.
                float midHeight = tipHeights[i] * 0.34f;               // 꺾이는 지점(밑동과 끝 사이)
                float midOut = tipOuts[i] * 0.30f;                     // 중간은 거의 곧게 선다
                Vector3 b0 = side * -0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 b1 = side * 0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 m0 = side * -0.03f + outward * midOut + Vector3.up * midHeight;
                Vector3 m1 = side * 0.03f + outward * midOut + Vector3.up * midHeight;
                Vector3 t0 = side * -0.012f + outward * tipOuts[i] + Vector3.up * tipHeights[i];
                Vector3 t1 = side * 0.012f + outward * tipOuts[i] + Vector3.up * tipHeights[i];

                int b = points.Count;
                points.Add(b0); points.Add(b1); points.Add(m1); points.Add(m0);
                points.Add(t1); points.Add(t0);

                // 아래 마디(밑동 → 중간)
                faces.Add(b); faces.Add(b + 1); faces.Add(b + 2);
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 3);
                // 위 마디(중간 → 끝)
                faces.Add(b + 3); faces.Add(b + 2); faces.Add(b + 4);
                faces.Add(b + 3); faces.Add(b + 4); faces.Add(b + 5);
                // 뒷면(감김 반대). 법선도 반대로 나오므로 양쪽에서 정상적으로 조명을 받는다.
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 1);
                faces.Add(b); faces.Add(b + 3); faces.Add(b + 2);
                faces.Add(b + 3); faces.Add(b + 4); faces.Add(b + 2);
                faces.Add(b + 3); faces.Add(b + 5); faces.Add(b + 4);
            }

            // 잎은 닫힌 볼륨이 아니라 중심 기준 바깥 판정(ensureOutward)을 쓸 수 없다 - 감김을 그대로 둔다.
            grassBladeMesh = BuildFlatShadedMesh("Veg_GrassBlades", points.ToArray(), faces.ToArray(), false);
            return grassBladeMesh;
        }

        /// <summary>
        /// 면마다 정점을 분리한 평면 셰이딩 메시를 만든다.
        /// ensureOutward가 켜져 있으면 각 삼각형의 법선이 원점 바깥을 향하도록 감김을 바로잡는다
        /// (닫힌 볼록 다면체에만 유효하다 - 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로
        /// 옮기면 안쪽을 향해 통째로 컬링되는 사고가 나기 쉬워, 표를 믿지 않고 계산으로 확정한다).
        /// UV는 XY 평면 투영이다 - 표면 그레인 텍스처를 곱하는 용도라 정밀한 전개가 필요 없다.
        /// </summary>
        private static Mesh BuildFlatShadedMesh(string meshName, Vector3[] points, int[] faces, bool ensureOutward)
        {
            var vertices = new Vector3[faces.Length];
            var uvs = new Vector2[faces.Length];
            var triangles = new int[faces.Length];

            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 p0 = points[faces[f]];
                Vector3 p1 = points[faces[f + 1]];
                Vector3 p2 = points[faces[f + 2]];

                if (ensureOutward && Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f) < 0f)
                {
                    Vector3 swap = p1;
                    p1 = p2;
                    p2 = swap;
                }

                vertices[f] = p0;
                vertices[f + 1] = p1;
                vertices[f + 2] = p2;
                uvs[f] = new Vector2(p0.x + 0.5f, p0.y + 0.5f);
                uvs[f + 1] = new Vector2(p1.x + 0.5f, p1.y + 0.5f);
                uvs[f + 2] = new Vector2(p2.x + 0.5f, p2.y + 0.5f);
                triangles[f] = f;
                triangles[f + 1] = f + 1;
                triangles[f + 2] = f + 2;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh grassBladeMesh;
        private static Mesh palmTrunkPrismMesh;

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
