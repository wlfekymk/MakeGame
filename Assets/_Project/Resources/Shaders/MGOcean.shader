// MGOcean - 바다 평면 전용 커스텀 URP 셰이더.
//
// WorldMapManager.CreateOceanMaterial()이 Resources.Load<Shader>("Shaders/MGOcean")로 로드해
// 사용하고, 로드가 실패하면 기존 URP Lit 경로로 폴백한다(게임은 이 셰이더 없이도 돌아가야 한다).
//
// 설계 계약(WorldMapManager 쪽 주석과 맞물린다):
//  * [v3 개정] 반투명(Transparent) 전환 - "물속이 보이는 바다" 사용자 요청.
//    예전 계약은 "불투명 유지 + _CameraDepthTexture 금지"였다. 두 금지를 **의도적으로 해제**한다:
//      - Opaque 유지의 근거였던 ShorelineBand(해수면 +0.05m 반투명 띠)와의 정렬 문제는,
//        이 셰이더가 살아 있는 동안 WorldMapManager가 띠 생성 자체를 건너뛰는 것으로 해소했다
//        (깊이 기반 해안 거품이 띠의 역할을 대체한다. 셰이더 폴백 시에는 띠가 그대로 생긴다).
//      - _CameraDepthTexture 금지의 근거였던 "파이프라인 에셋에서 꺼져 있을 수 있다"는
//        URP 에셋 실측(m_RequireDepthTexture: 1, Opaque Texture도 켜짐)으로 해제됐다.
//        SampleSceneDepth(DeclareDepthTexture.hlsl)로 씬 깊이를 읽어 물 기둥 깊이
//        (씬 아이 깊이 - 수면 아이 깊이)를 구하고, 깊이 흡수색/알파/해안 거품에 쓴다.
//        만약 에셋 설정이 다시 꺼지면 깊이 텍스처가 기본값으로 잡혀 거품/깊이색이 사라질 뿐
//        렌더 자체는 망가지지 않는다(우아한 열화).
//  * [v3] 반투명 전환의 알려진 부작용(의도된 트레이드오프, WorldMapManager 쪽 주석과 동일):
//      - Transparent 큐는 URP Forward에서 메인 라이트 그림자를 받지 않는다. 이 셰이더는 원래
//        그림자 수신을 넣지 않았으므로(아래 라이팅 주석) 실질 변화 없음.
//      - 반투명끼리는 소트 순서 문제가 생길 수 있으나, 바다는 월드에 평면 1장뿐이라 실질 무해.
//      - ZWrite Off라 뎁스 프리패스/포스트가 수면을 "표면"으로 보지 않는다(수중 지형이 그대로 보임 - 의도).
//  * Cull Off - 잠수 중 아래에서 올려다볼 때 수면 뒷면이 보여야 한다. 뒷면 렌더 시에는
//    SV_IsFrontFace로 노멀을 뒤집고, 깊이차가 음수가 될 수 있으므로 클램프한다(아티팩트 방지).
//  * [파도 v4 - OceanWaves.cs 단일 소스] 파도 파라미터(진폭·파수·각속도·방향)는 더 이상 이 파일에
//    상수로 박혀 있지 않다. C#의 MakeGame.Systems.OceanWaves가 Shader.SetGlobalVector로 밀어주는
//    전역 6개(_MG_WaveAmp / _MG_WaveK / _MG_WaveOmega / _MG_WaveDirX / _MG_WaveDirZ / _MG_SeaState)를
//    읽는다. 같은 수를 C#도 읽어 부력·뗏목 흔들림·수영 수면 판정을 계산하므로, 파라미터를 한 곳
//    (OceanWaves의 상수 표)만 고치면 물리와 시각이 함께 움직인다.
//    ※ 이 전역들은 반드시 Properties 블록 **밖**·UnityPerMaterial CBUFFER **밖**에 선언한다.
//      Properties에 넣으면 머티리얼 프로퍼티가 되어 전역 설정이 무시되고(머티리얼 값이 이긴다),
//      CBUFFER 안에 넣으면 SRP Batcher 레이아웃과 충돌한다.
//    ※ 드라이버(OceanWaves)가 없으면 전역이 0이라 바다가 평평하게 보인다(우아한 열화). 런타임에는
//      SubsystemRegistration 시점에 기본값이 즉시 밀리므로 실제로 0인 프레임은 없다. 에디터
//      씬 뷰(비플레이)에서만 평평하게 보인다.
//  * [파도 v4] **버텍스 파도를 쓰지 않는다.** 바다 메시는 WorldMapManager.GenerateOceanMesh가
//    만든 40,000m / 64칸 격자라 **한 칸이 625m**다. 나이퀴스트상 표현 가능한 최단 파장이 1,250m인데
//    실제 파장은 21~118m이므로, 정점을 옮기면 파도가 아니라 625m짜리 삼각형이 통째로 들썩이는
//    에일리어싱이 된다. 그래서 정점은 평면 그대로 두고, 같은 파고 함수의 **해석적 기울기로 픽셀
//    단위 노멀만** 흔든다(격자 밀도와 무관). 높이 함수는 C#(OceanWaves)에서만 실제로 쓰인다 -
//    부력·뗏목·수영 판정이 거기서 나오므로 파도의 체감은 전부 살아 있다.
//    (이전 v2/v3의 "진폭 합계 0.24m 버텍스 파도" 계약은 이 근거로 폐기했다.)
//  * 파도 시간은 셰이더 내장 _Time이 아니라 C#(Update)이 매 프레임 넣는 _MG_WaveTime을 쓴다.
//    Time.time은 Time.timeScale = 0에서 멈추므로 타이틀 화면에서 바다가 정지하는 기존 동작이
//    그대로 유지된다(UV 스크롤도 같은 이유로 C#에서 멈춘다).
//  * [비 파문 - RainWetness.cs가 단일 소스] 비가 올 때 수면에 빗방울 파문 노멀을 더한다.
//    읽는 전역은 _MG_RainIntensity(비 세기 0~1) / _MG_RainTime(파문 시계 s) / _MG_RippleParams
//    (x 타일링 1/m · y 속도 회/s · z sRGB 보정 스위치 · w 세기 배율) / _MG_RippleMap(파문 텍스처)이다.
//    ※ MGShoreline(모래 캡)이 읽는 것과 **같은 전역·같은 텍스처**다. 두 셰이더가 같은 수를 보므로
//      물가를 사이에 두고 모래 위 파문과 수면 파문의 위상/속도가 어긋나지 않는다.
//    ※ 이 전역들도 Properties 블록 **밖**·CBUFFER **밖**이다(위 파도 v4와 같은 규약).
//    합성 규칙(기존 계산과 싸우지 않게 하는 것이 요점):
//      - 파문은 **slope에 더하기만** 한다. 큰 파도 기울기(waveSlope)와 잔물결(MGRippleSlope)이
//        이미 쌓아 둔 같은 단위(dH/dx, dH/dz)의 양이고, 최종 노멀은 한 번만 normalize된다.
//      - 화이트캡 판정은 예전 그대로 **waveSlope만** 본다(파문을 넣으면 비 오는 날 바다가 통째로
//        하얘진다 - 잔물결을 뺀 것과 같은 이유다). 깊이 거품·투명도 계산도 한 글자도 건드리지 않는다.
//      - 거칠기에 반비례시킨다(1 - 0.75·seaRough). 잔잔한 수면에서는 파문이 또렷하고, 파도가 거칠면
//        1m 넘는 파고에 묻히는 것이 자연스럽다. 폭풍(거칠기 1)에서도 25%는 남겨 완전히 사라지지는 않는다.
//      - 앞면 전용(frontGate) + 거리 감쇠(90m). 파문은 파장 수십 cm라 원거리에서는 알리아싱만 남는다.
//    텍스처가 없으면(RainWetness가 검은 텍스처를 바인딩) A = 0 → 진폭 0 → 파문만 조용히 빠진다.
//  * [MainTexture] _BaseMap + _BaseMap_ST - C#의 mainTexture/mainTextureScale(oceanSize/10)/
//    mainTextureOffset(Update 스크롤)이 URP Lit 때와 같은 의미로 그대로 통한다
//    ("1타일 = 월드 10미터" 툴팁 계약 유지).
Shader "MG/Ocean"
{
    Properties
    {
        [MainTexture] _BaseMap("수면 그레인 텍스처(water.png)", 2D) = "white" {}
        [MainColor] _BaseColor("전체 틴트(호환용, 기본 흰색)", Color) = (1, 1, 1, 1)
        // 기존 바다색 (0.1, 0.35, 0.55) 근처에서 갈라놓은 두 색. 프레넬로 블렌드한다.
        _DeepColor("깊은 바다색(수평선 쪽)", Color) = (0.05, 0.24, 0.48, 1)
        _ShallowColor("얕은 바다색(내려다볼 때, 청록)", Color) = (0.13, 0.46, 0.57, 1)
        // [v3] 깊이 흡수: 물 기둥이 _DepthFadeDeep보다 깊으면 이 색으로 수렴한다(사실상 불투명).
        _AbyssColor("아주 깊은 물 흡수색(짙은 파랑)", Color) = (0.02, 0.11, 0.30, 1)
        _FoamColor("해안 거품색", Color) = (0.92, 0.96, 0.97, 1)
        _DepthFadeShallow("흡수 시작 깊이(m, 이보다 얕으면 청록)", Float) = 2.0
        _DepthFadeDeep("흡수 완료 깊이(m, 이보다 깊으면 짙은 파랑/불투명)", Float) = 12.0
        _FoamDepth("해안 거품 최대 깊이(m)", Float) = 0.7
        _FresnelPower("프레넬 지수(클수록 깊은색이 수평선에 몰린다)", Range(0.5, 8.0)) = 3.0
        _Smoothness("스페큘러 매끄러움", Range(0.0, 1.0)) = 0.85
        _SpecularStrength("스페큘러 세기", Range(0.0, 2.0)) = 0.7
        _WaveAmplitude("큰 파도 노멀 세기 배율(1 = OceanWaves가 밀어준 진폭 그대로)", Range(0.0, 2.0)) = 1.0
        _RippleStrength("잔물결 노멀 퍼터베이션 세기", Range(0.0, 2.0)) = 1.0
        _MG_WaveTime("파도 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
        // 세기 근거(rain_ripple.png 실측): 파문 함수의 출력 |n|은 평균 0.028 · p99 0.40 · 최대 0.89이다.
        // 0.45를 곱하면 기울기가 p99 0.18 / 최대 0.40이 되어, 큰 파도 기울기(최대 0.15)와 잔물결
        // (최대 0.36)에 **묻히지 않으면서 압도하지도 않는** 크기가 된다. 런타임 튜닝 손잡이는
        // RainWetness.rippleStrength(전역 _MG_RippleParams.w)이고 이 값과 곱해진다.
        _RainRippleStrength("빗방울 파문 노멀 세기(수면)", Range(0.0, 2.0)) = 0.45
        _RainRippleFadeDistance("파문이 사라지는 카메라 거리(m)", Float) = 90.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // [v3] 반투명 바다: 알파 블렌드, 깊이 안 씀(수중 지형이 비쳐 보여야 한다),
            // 양면 렌더(잠수 중 아래에서 수면 뒷면이 보여야 한다).
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // [v3] 씬 깊이(_CameraDepthTexture) 샘플러. URP 에셋에서 Depth Texture가 켜져 있음을
            // 실측으로 확인하고 도입했다(헤더의 계약 개정 주석 참고).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _AbyssColor;
                half4 _FoamColor;
                float _DepthFadeShallow;
                float _DepthFadeDeep;
                float _FoamDepth;
                half _FresnelPower;
                half _Smoothness;
                half _SpecularStrength;
                half _WaveAmplitude;
                half _RippleStrength;
                float _MG_WaveTime;
                half _RainRippleStrength;
                float _RainRippleFadeDistance;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // ---- [파도 v4] OceanWaves.cs가 Shader.SetGlobalVector로 밀어주는 파도 파라미터 ----
            // 성분 4개가 각 float4의 x/y/z/w 채널에 하나씩 들어간다(C# 쪽 Vector4 채널 매핑과 동일).
            // CBUFFER(UnityPerMaterial) 밖 = 전역 상수 버퍼. SRP Batcher는 머티리얼 프로퍼티만
            // UnityPerMaterial에 있으면 되므로 이 선언은 배칭을 깨지 않는다.
            float4 _MG_WaveAmp;     // A_i (m)
            float4 _MG_WaveK;       // k_i = 2π / 파장 (rad/m)
            float4 _MG_WaveOmega;   // ω_i = sqrt(g·k_i) · 속도배율 (rad/s)
            float4 _MG_WaveDirX;    // D_i.x (단위벡터)
            float4 _MG_WaveDirZ;    // D_i.z (단위벡터)
            float4 _MG_SeaState;    // x = 바다 거칠기 0~1, y = 평균 해수면 y(m)

            // ---- [비 파문] RainWetness.cs가 미는 전역(위 헤더의 계약) ----
            float _MG_RainIntensity;  // 비 세기 0~1(0이면 아래 분기가 통째로 빠진다)
            float _MG_RainTime;       // 파문 시계(초). timeScale = 0에서 멈춘다(타이틀 정지 계약)
            float4 _MG_RippleParams;  // x 타일링(1/m) · y 속도(회/s) · z sRGB 보정 · w 세기 배율

            // 전역 텍스처. RainWetness가 부트스트랩에서 반드시 무언가를 바인딩한다(실패 시 검정).
            TEXTURE2D(_MG_RippleMap);
            SAMPLER(sampler_MG_RippleMap);

            // 알파 계약(v3): 내려다볼 때 ~0.5(바닥이 보인다) → 스치는 각 ~0.95(수평선은
            // 하늘 반사처럼 사실상 불투명). 프레넬 색 블렌드와 같은 항(fresnel)을 재사용한다.
            #define MG_ALPHA_DOWN  0.5
            #define MG_ALPHA_GRAZE 0.95
            #define MG_ALPHA_ABYSS 0.97   // 깊은 물 기둥 위에서의 상한(깊은 바다는 사실상 불투명).

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            // ---- [파도 v4] Gerstner 성분 4개 합성(수평 압축항 Q = 0 → 방향성 정현파 합) ----
            // 파라미터는 전부 전역이다. C#(OceanWaves.SampleWaveOffset / SampleSlope)이 아래 두 식을
            // **문자 그대로 같은 형태로** 구현하고, 같은 전역값을 쓰므로 두 쪽 결과가 일치한다.
            //   h(p,t)  = Σ A_i · sin( k_i · dot(D_i, p) + ω_i · t )
            //   ∂h/∂p   = Σ D_i · (A_i · k_i) · cos( k_i · dot(D_i, p) + ω_i · t )
            // Q를 0으로 둔 이유: 수평 압축은 정점을 옮겨야만 보이는데 바다 격자(625m)가 그것을
            // 표현할 수 없고(헤더 참고), Q > 0이면 C#(높이 역산 반복 필요)과 셰이더(평면 픽셀 셰이딩)
            // 사이에 위상 불일치만 생긴다.

            // 성분 i의 위상 θ_i = k_i·dot(D_i, p) + ω_i·t 를 4개 한꺼번에 구한다.
            float4 MGWavePhase(float2 p, float t)
            {
                return _MG_WaveK * (_MG_WaveDirX * p.x + _MG_WaveDirZ * p.y) + _MG_WaveOmega * t;
            }

            // 파고(평균 해수면 기준 편차, m). 셰이더에서는 직접 쓰지 않지만(정점 변위 미채택)
            // C# 쪽 식과의 대조를 위해 같은 자리에 남겨 둔다.
            float MGWaveHeight(float2 p, float t)
            {
                return dot(_MG_WaveAmp, sin(MGWavePhase(p, t)));
            }

            // 파고 함수의 해석적 기울기(dH/dx, dH/dz). 프래그먼트에서 큰 파도의 노멀 성분으로 쓴다.
            float2 MGWaveSlope(float2 p, float t)
            {
                float4 c = _MG_WaveAmp * _MG_WaveK * cos(MGWavePhase(p, t));
                return float2(dot(_MG_WaveDirX, c), dot(_MG_WaveDirZ, c));
            }

            // ---- 잔물결 노멀 퍼터베이션 2겹: 스크롤 방향/스케일/속도를 서로 다르게. ----
            // 텍스처 없이 해석적 사인 기울기로 만든다(파이프라인 에셋 의존 없음). 사인은 밉맵이 없어
            // 원거리에서 알리아싱 반짝임이 생기므로, 겹마다 카메라 거리 기반으로 세기를 감쇠시킨다.
            float2 MGRippleSlope(float2 p, float t, float viewDist)
            {
                // 1겹: 파장 ~2.9m, 근거리 디테일. 120m 근방에서 사라진다.
                float2 dir1 = float2(0.799, 0.602);
                float2 dir2 = float2(-0.242, 0.970);
                float fade1 = saturate(1.0 - viewDist / 120.0);
                float2 s = dir1 * (0.22 * fade1) * cos(dot(p, dir1) * 2.17 + t * 2.30);

                // 2겹: 파장 ~11.4m, 중거리 물비늘. 500m 근방에서 사라진다.
                float fade2 = saturate(1.0 - viewDist / 500.0);
                s += dir2 * (0.14 * fade2) * cos(dot(p, dir2) * 0.55 + t * 1.35);
                return s;
            }

            // [v3] 해안 거품 가장자리를 흔드는 잔물결 노이즈(-1~1 근방). 파장 서로 다른 사인 2겹의
            // 곱/합 - 등깊이선(등고선)이 그대로 드러나는 "자로 잰 라인"을 부수는 용도라 저렴해도 충분하다.
            float MGFoamNoise(float2 p, float t)
            {
                float n = sin(p.x * 1.71 + t * 1.90) * sin(p.y * 2.33 - t * 1.40);
                n += 0.6 * sin((p.x + p.y) * 3.07 + t * 2.60);
                return n * 0.625; // 대략 -1~1로 정규화.
            }

            // ---- [비 파문] 빗방울 파문의 기울기(dH/dx, dH/dz). MGShoreline과 **같은 식·같은 전역** ----
            // 텍스처 계약: RG = 접선공간 노멀 xy(0.5 = 평평) / B = 파문별 위상 오프셋 / A = 세기 마스크.
            // 수명 곡선 t(1-t)·4는 0에서 솟아 0.5에서 1이 되고 1에서 닫히는 포물선이라 frac이 감길 때
            // 튀지 않는다(C0 연속). 수면은 법선이 거의 +Y라 접선공간 xy를 그대로 XZ 기울기로 쓴다.
            //
            // [sRGB 방어] 데이터 텍스처라 Linear 임포트가 전제다. sRGB로 임포트되면 0.5가 0.214로
            // 읽혀 파문이 한쪽으로 기운 판이 되는데, .meta는 셰이더의 편집 범위 밖이다. RainWetness가
            // 런타임에 Texture.isDataSRGB를 조회해 _MG_RippleParams.z에 1을 넣으면 여기서
            // pow(c, 1/2.2)로 저장값을 되돌린다(0.214 → 0.496 ≈ 0.5). A 채널은 감마 변환 대상이 아니다.
            float2 MGRainRippleSlope(float2 p, float t)
            {
                float tiling = max(_MG_RippleParams.x, 0.001);
                float speed = _MG_RippleParams.y;
                float srgb = saturate(_MG_RippleParams.z);

                float4 r0 = SAMPLE_TEXTURE2D(_MG_RippleMap, sampler_MG_RippleMap, p * tiling);
                float3 c0 = lerp(r0.rgb, pow(saturate(r0.rgb), 1.0 / 2.2), srgb);
                float t0 = frac(t * speed + c0.b);
                float2 n = (c0.rg * 2.0 - 1.0) * (r0.a * t0 * (1.0 - t0) * 4.0);

                // 둘째 겹: 타일링을 0.47배로 어긋내 1024² 한 장의 반복 격자를 깬다.
                float4 r1 = SAMPLE_TEXTURE2D(_MG_RippleMap, sampler_MG_RippleMap,
                    p * (tiling * 0.47) + float2(0.37, -0.23));
                float3 c1 = lerp(r1.rgb, pow(saturate(r1.rgb), 1.0 / 2.2), srgb);
                float t1 = frac(t * speed * 0.83 + c1.b + 0.5);
                n += (c1.rg * 2.0 - 1.0) * (r1.a * t1 * (1.0 - t1) * 4.0);

                return n * (0.5 * max(_MG_RippleParams.w, 0.0));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // [파도 v4] 정점은 옮기지 않는다. 바다 격자 한 칸이 625m라(WorldMapManager.GenerateOceanMesh,
                // 64칸 × 40,000m) 파장 21~118m의 파도를 정점에 실으면 파도가 아니라 에일리어싱이 된다
                // (나이퀴스트 최단 표현 파장 1,250m). 파도의 시각 표현은 프래그먼트의 해석적 노멀이
                // 전부 담당하고(격자 밀도와 무관), 실제 높이는 C#(OceanWaves)이 물리에만 쓴다.
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                // _BaseMap_ST 존중: C#의 mainTextureScale(oceanSize/10)과 mainTextureOffset(스크롤)이
                // 여기서 URP Lit과 동일하게 적용된다.
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float t = _MG_WaveTime;
                float3 toCamera = _WorldSpaceCameraPos - IN.positionWS;
                float viewDist = length(toCamera);
                float3 viewDir = toCamera / max(viewDist, 0.0001);

                // [파도 v4] 바다 거칠기(0 맑음 ~ 1 폭풍). OceanWaves가 WeatherSystem의 상태에서 보간해
                // 밀어준다. 큰 파도의 진폭·속도는 이미 전역 파라미터에 반영돼 있고, 여기서는 잔물결
                // 세기와 화이트캡만 추가로 태운다.
                half seaRough = saturate(_MG_SeaState.x);

                // 노멀 = 큰 파도 기울기 + 잔물결 2겹. 진폭이 작아도 기울기는 스페큘러를 충분히 흔든다.
                float2 waveSlope = MGWaveSlope(IN.positionWS.xz, t) * _WaveAmplitude;
                float2 slope = waveSlope;
                slope += MGRippleSlope(IN.positionWS.xz, t, viewDist) * (_RippleStrength * (1.0 + 0.8 * seaRough));

                // [비 파문] 비가 올 때만 도는 분기다. 조건이 **전역 하나**뿐이라 드로우콜 전체에서
                // 상수이고(워프가 갈라지지 않는다), 맑은 날에는 텍스처 샘플 2회가 통째로 빠진다.
                // 거칠기에 반비례(1 - 0.75·seaRough) · 거리 감쇠(_RainRippleFadeDistance) ·
                // 앞면 전용. 화이트캡 판정은 위 waveSlope를 그대로 쓰므로 여기 결과에 영향받지 않는다.
                UNITY_BRANCH
                if (_MG_RainIntensity > 0.001)
                {
                    float rippleFade = saturate(1.0 - viewDist / max(_RainRippleFadeDistance, 1.0));
                    float rippleAmt = saturate(_MG_RainIntensity) * rippleFade
                        * (1.0 - 0.75 * seaRough) * (isFrontFace ? 1.0 : 0.0);
                    // 부호가 **빼기**인 이유: 최종 노멀이 float3(-slope.x, 1, -slope.y)라,
                    // 접선공간 노멀 xy를 그대로 더하려면(= MGShoreline과 같은 규약) slope에서 빼야 한다.
                    // 파문은 좌우대칭이라 부호를 틀려도 화면상 거의 같지만, 두 셰이더가 같은 텍스처를
                    // 같은 뜻으로 읽는다는 계약을 지켜 둔다.
                    slope -= MGRainRippleSlope(IN.positionWS.xz, _MG_RainTime)
                        * (_RainRippleStrength * rippleAmt);
                }

                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                // [v3] 수중 처리: Cull Off로 그려지는 뒷면(카메라가 물속에서 위를 올려다볼 때)은
                // 노멀을 뒤집어야 프레넬/라이팅이 성립한다. 뒤집지 않으면 dot(N,V)가 음수로 포화되어
                // 수면 전체가 프레넬 최대(짙은 색, 알파 0.95)로 막혀 물속에서 하늘이 안 보인다.
                normalWS = isFrontFace ? normalWS : -normalWS;

                // ---- [v3] 물 기둥 깊이: 씬 아이 깊이 - 수면 아이 깊이. ----
                // 씬 깊이는 불투명 패스가 남긴 것이라(이 셰이더는 Transparent+ZWrite Off) 수면 아래
                // 지형/오브젝트까지의 거리다. 원근 카메라 전제(이 게임의 플레이 카메라).
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEyeDepth = -TransformWorldToView(IN.positionWS).z;
                // 카메라가 수중일 때(뒷면 렌더)는 수면 너머가 물 밖 세상이라 이 차이가 음수가 되거나
                // 의미를 잃는다 - max(0)으로 클램프하고, 깊이 효과 자체를 앞면 전용으로 게이트해서
                // (아래 frontGate) 수중에서 거품/흡수색이 엉뚱하게 나타나는 아티팩트를 막는다.
                float waterColumn = max(sceneEyeDepth - surfaceEyeDepth, 0.0);
                float frontGate = isFrontFace ? 1.0 : 0.0;

                // 프레넬 기반 색 블렌드: 내려다보면(N·V 큼) 청록, 수평선 쪽(N·V 작음)은 깊은 파랑.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, fresnel);

                // [v3] 깊이 기반 흡수색: 얕음(<_DepthFadeShallow) 청록 → 깊음(>_DepthFadeDeep) 짙은 파랑.
                // 내려다보는 성분(1-fresnel)에만 흡수색을 태워, 스치는 각에서는 기존 프레넬 블렌드
                // (하늘 반사 느낌의 _DeepColor)가 그대로 이긴다.
                half depthT = saturate((waterColumn - _DepthFadeShallow)
                    / max(_DepthFadeDeep - _DepthFadeShallow, 0.001)) * frontGate;
                half3 absorbColor = lerp(_ShallowColor.rgb, _AbyssColor.rgb, depthT);
                waterColor = lerp(absorbColor, waterColor, fresnel);

                // [v3] 깊이 기반 해안 거품: 물 깊이 0~_FoamDepth(0.7m) 구간의 부드러운 흰 띠.
                // MGFoamNoise로 판정 깊이를 ±0.18m 흔들어 등깊이선이 자로 잰 라인으로 안 보이게 하고,
                // 같은 노이즈로 거품 농도도 일렁이게 한다. 앞면 전용(수중에서는 무의미한 값이라 차단).
                float foamNoise = MGFoamNoise(IN.positionWS.xz, t);
                half foam = 1.0 - smoothstep(0.0, max(_FoamDepth, 0.001), waterColumn + foamNoise * 0.18);
                foam *= (0.75 + 0.25 * foamNoise) * frontGate;
                foam = saturate(foam);

                // [파도 v4] 화이트캡: 거친 바다에서 **큰 파도의 마루(기울기가 가장 가파른 곳)** 에만
                // 흰 거품을 얹는다. 판정에 waveSlope(잔물결 제외)를 쓰는 것이 핵심이다 - 잔물결까지
                // 넣으면 근거리 전체가 하얘진다.
                // [파도 v5] 진폭이 2.4배가 되면서 기울기도 그만큼 커졌다(잔잔 최대 0.024 → 0.053,
                // 폭풍 0.066 → 0.154). 임계/폭을 그대로 두면 흐린 날에도 바다가 통째로 하얘지므로
                // (실측 최대 세기 0.066 → 0.450) 임계 0.030 → 0.066 · 폭 0.032 → 0.070으로 같이 올렸다.
                // 임계 0.066은 잔잔한 바다의 **이론 최대 기울기 0.053보다 위**라, 맑은 날에는 한 점도
                // 나오지 않는 성질이 그대로 보장된다. 랜덤 40만 점 실측 최대 세기(맑음/흐림/폭풍):
                //   종전 0.000 / 0.066 / 0.738  →  개정 0.000 / 0.081 / 0.809 (유지~소폭 증가).
                // 임계값이 half의 유효 자릿수 경계에 가까운 작은 수라 float으로 계산한다.
                float waveSteep = length(waveSlope);
                float whitecap = saturate((waveSteep - 0.066) / 0.070) * seaRough;
                // 거품 가장자리를 같은 노이즈로 흩뜨리고, 깊이 거품과 같은 이유로 앞면 전용으로 막는다
                // (수중에서 올려다볼 때 흰 반점이 알파를 불투명 쪽으로 밀면 하늘이 가려진다).
                whitecap *= (0.7 + 0.3 * foamNoise) * frontGate;
                foam = saturate(max(foam, whitecap * 0.6));

                half3 grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half3 albedo = grain * waterColor * _BaseColor.rgb;
                albedo = lerp(albedo, _FoamColor.rgb, foam);

                // 라이팅: 메인 라이트 램버트 + 블린-퐁 스페큘러 + SH 앰비언트.
                // GGX/그림자 수신은 일부러 뺐다 - include 실패 가능성이 낮은 단순한 구성을 택한다.
                // (v3 참고: Transparent 큐는 어차피 메인 라이트 그림자를 받지 않으므로 이 선택과 정합한다.)
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * ndotl);

                float3 halfDir = normalize(mainLight.direction + viewDir);
                half specPower = exp2(_Smoothness * 10.0 + 1.0);
                half spec = pow(saturate(dot(normalWS, halfDir)), specPower);
                // 프레넬만큼 스페큘러를 키워 수평선 쪽 물비늘 반짝임을 강조한다. 거품 위에서는 죽인다.
                color += mainLight.color * (spec * _SpecularStrength * (0.4 + 0.6 * fresnel))
                    * step(0.001, ndotl) * (1.0 - foam);

                // ---- [v3] 알파 합성 ----
                // 1) 프레넬: 내려다보면 ~0.5(바닥이 보인다) → 스치는 각 ~0.95(하늘 반사처럼 불투명).
                // 2) 깊이: 물 기둥이 깊을수록 불투명하게(깊은 바다 바닥은 어차피 안 보여야 자연스럽다).
                // 3) 거품: 거품은 수면 위 흰 막이라 불투명 쪽으로 민다.
                half alpha = lerp(MG_ALPHA_DOWN, MG_ALPHA_GRAZE, fresnel);
                alpha = lerp(alpha, MG_ALPHA_ABYSS, depthT);
                alpha = lerp(alpha, MG_ALPHA_GRAZE, foam);
                alpha *= _BaseColor.a; // 호환용 전체 틴트 알파(기본 1).

                color = MixFog(color, IN.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
        // ShadowCaster/DepthOnly 패스는 넣지 않는다 - 바다 렌더러는 shadowCastingMode.Off이고
        // (CreateOcean 주석 참고), Transparent + ZWrite Off라 뎁스 프리패스에 낄 이유도 없다.
        // (씬 깊이를 "읽는" 쪽이므로 스스로 깊이를 남기면 오히려 물 기둥 계산이 0으로 무너진다.)
    }

    Fallback "Universal Render Pipeline/Lit"
}
