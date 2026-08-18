// MGShoreBreak - 부서지는 파도 마루 리본(ShoreRibbon_*) 전용 URP 셰이더.
//
// ShorelineRibbon.cs가 Resources.Load<Shader>("Shaders/MGShoreBreak")로 로드해 월드 공유 머티리얼
// 한 장을 만든다. 로드가 실패하면 **리본 자체를 만들지 않는다**(URP Lit 폴백으로 그리면 바다 위에
// 6m 폭 불투명 판이 깔린다 - MGGrass의 "셰이더 없으면 잔디 생략"과 같은 계약이다).
//
// ── 무엇을 그리는가 ────────────────────────────────────────────────────────────
// 물가(y=0 등고선)에서 **바다 쪽으로 6m** 뻗은 띠 위에, 바다에서 밀려와 부서지는 마루를 그린다.
//   (1) 정점 변위: 마루가 솟고(앞면이 가파르고 뒤가 완만한 비대칭 능선) 꼭대기가 물가 쪽으로
//       말린다(curl). 높이는 OceanWaves가 미는 파도 진폭 전역에서 유도한다 - 아래 참고.
//   (2) 프래그먼트: 앞면은 반투명 청록(뒤가 비친다), 꼭대기와 부서진 뒤쪽은 흰 거품.
//       거품은 알파 페이드가 아니라 **디졸브**(노이즈 임계)로 지워진다(MGShoreline과 같은 방식).
//
// ── 리본 로컬 좌표 계약 (ShorelineRibbon.cs가 굽는다) ──────────────────────────
//   TEXCOORD0 uv   : x = 해안선을 따라간 거리 u(m), y = 물가로부터의 바다쪽 진행도 v(0~1).
//   TEXCOORD1 uv2  : x = **부호 있는 물가 거리(m)** - 리본은 전부 바다 쪽이라 음수다.
//                    (BakeShoreField가 지형 UV2에 굽는 부호 규약과 정확히 같다: + 내륙 / - 물속)
//                    y = 이 리본의 폭(m). v ↔ 미터 환산을 셰이더가 상수로 가정하지 않게 하려고 싣는다.
//   TEXCOORD2 uv3  : 물가에서 바다로 향하는 **단위 방향(XZ)**. 마루 말림(수평 변위)과 노멀 재구성용.
// 리본은 해수면(y = 0)에 평평하게 놓인다. 바다 메시도 정점을 옮기지 않으므로(MGOcean vert 주석 -
// 격자 한 칸이 625m라 파도를 정점에 싣지 않는다) 리본 밑면과 수면이 항상 같은 평면이다.
//
// ── 위상 계약: 스와시(MGShoreline)와 **같은 파도** ─────────────────────────────
// MGShoreline은 물가에서 d미터 **안쪽**을 d/celerity초 늦게 적신다:
//     tLocal = _MG_ShoreTime - max(d, 0) / celerity
// 리본은 그 식을 d < 0(바다 쪽)으로 그대로 연장한 것이다. s = -d(바다쪽 거리, m)라 두면
//     tLocal = _MG_ShoreTime + s / celerity
// 즉 **바다 쪽일수록 위상이 앞선다**. 결과적으로 마루는 celerity(1.8 m/s)로 물가를 향해 달려와
// frac(tLocal/period) = 0인 순간 정확히 s = 0에 도달하고, 바로 그 순간 MGShoreline의 스와시 전선이
// d = 0에서 출발한다 - 두 셰이더의 전선이 물가에서 한 점으로 이어진다(끊김·중복 없음).
// 연안 방향 변주(MGShoreWobble × period × _AlongshoreWobble)도 **같은 함수·같은 계수**를 쓴다.
// 값을 바꾸려면 두 셰이더를 함께 고쳐야 한다(그렇지 않으면 거품과 마루가 따로 논다).
//
// 한 주기(7.5s)에 마루가 지나가는 거리는 celerity × period = 13.5m다. 리본 폭이 6m이므로
// 마루는 주기의 6/13.5 = 44%(3.33초) 동안만 리본 안에 있고, 나머지 시간에는 리본이 비어 있다
// (body → 0 → 알파 0). "밀려와 → 부서지고 → 사라진다"의 1주기가 이 구조에서 저절로 나온다.
//
// ── 마루 높이: OceanWaves 전역에서 유도(상수 고정 금지) ────────────────────────
//   crestHeight = min( (ΣA_i) × _CrestGain × lerp(_CalmScale, 1, 거칠기), _CrestMaxHeight )
// ΣA_i는 _MG_WaveAmp(OceanWaves.cs가 미는 성분별 진폭 4개)의 합이다. OceanWaves 쪽 진폭 표가
// 바뀌면(다른 작업으로 파고를 올리는 중이다) 리본 마루도 **자동으로 같은 비율로** 따라 오른다.
// 현재 OceanWaves 표(잔잔 성분합 0.500m, stormAmplitudeScale 2.9 → 폭풍 1.450m) 기준:
//   잔잔  ΣA 0.500 → 0.32m (거의 평평 - 물결이 스치는 정도)
//   폭풍  ΣA 1.450 → 2.61m (확실히 솟아 벽을 이룬다)
// _CrestMaxHeight는 그 비례를 막는 값이 아니라 폭주 방지 난간이다(기본 4.0m). 지금 진폭에서 폭풍이
// 2.61m이므로 진폭이 **여기서 50% 더 올라도** 난간에 닿지 않는다 - 진행 중인 파고 상향 작업을
// 그대로 따라간다. (이 값을 잡을 때 진폭 표가 0.212 → 0.500으로 올라간 상태였다. 상수를 박지 않고
//  전역에서 유도하기 때문에 그 변경에 코드 한 줄도 고칠 필요가 없었다는 것이 이 설계의 요점이다.)
//
// ── 전역 (전부 읽기 전용. Properties/CBUFFER 밖 - 기존 셰이더 5종과 같은 규약) ─
//   _MG_ShoreTime   : ShorelineWaves.cs (= Time.time. timeScale 0에서 멈춘다 = 타이틀 정지 계약)
//   _MG_ShoreParams : ShorelineWaves.cs (x 주기 s, y 전선 속도 m/s, z·w 도달거리 - 리본은 x,y만 쓴다)
//   _MG_SeaState    : OceanWaves.cs (x 거칠기 0~1, y 해수면 y)
//   _MG_WaveAmp     : OceanWaves.cs (성분별 진폭 m)
// ※ Properties에 넣으면 머티리얼 값이 이겨서 전역 설정이 무시되고, CBUFFER(UnityPerMaterial)에
//   넣으면 SRP Batcher 레이아웃과 충돌한다. 넷 다 절대 넣지 않는다.
//
// ── 거품 텍스처 계약 (Textures/shore_foam, MGShoreline과 공유) ─────────────────
//   R = 거품 마스크 / G = 디졸브 노이즈 / B = 미세 디테일 / A = 큰 덩어리 마스크.
// 파일이 없으면 _FoamMap 기본값 "black"이라 디졸브 항이 0이 된다. 그때는 _FoamBase(0.45)만 남아
// **디테일 없는 흰 마루**로 조용히 폴백한다(경고도 분기도 없다 - 우아한 열화).
//
// ── 렌더 상태 ──────────────────────────────────────────────────────────────────
//   · Queue "Transparent+10"(3010) - 바다 수면(MGOcean, Transparent 3000)보다 **뒤에** 그려져
//     물 위에 얹힌다. 카우스틱(2990)/마린스노우(3000)와도 순서가 정해진다.
//   · ZWrite Off - 반투명이라 깊이를 쓰면 뒤따르는 투명체가 잘린다(MGOcean과 같은 규칙).
//     ZTest는 기본(LEqual)이라 **불투명 지형·해저에는 정상적으로 가려진다**.
//   · Cull Off - 마루가 말리면 뒷면이 보이고, 잠수 중 아래에서 올려다볼 수도 있다.
//   · ShadowCaster/DepthOnly/DepthNormals 패스 없음(헤더 규약). 리본 렌더러는
//     shadowCastingMode.Off · receiveShadows = false다.
//   · Fallback URP Lit - 패스가 필요해지면 그쪽이 채운다.
Shader "MG/ShoreBreak"
{
    Properties
    {
        _FoamMap("거품(R 마스크 / G 디졸브 / B 디테일 / A 덩어리). 없으면 black = 단색 폴백", 2D) = "black" {}
        _FoamColor("거품색", Color) = (0.96, 0.98, 0.99, 1)
        _CrestColor("마루 앞면 물색(반투명 청록)", Color) = (0.26, 0.62, 0.62, 1)

        _CrestGain("마루 높이 배율 - 파도 진폭 합(ΣA)에 곱한다", Range(0.5, 6.0)) = 1.8
        _CalmScale("잔잔할 때(거칠기 0) 높이 계수. 작을수록 잔잔한 날 평평하다", Range(0.0, 1.0)) = 0.35
        _CrestMaxHeight("마루 높이 상한(m) - 폭주 방지 난간", Range(0.5, 6.0)) = 4.0

        _CrestFrontWidth("마루 앞면 폭(m). 작을수록 벽처럼 가파르다", Range(0.3, 4.0)) = 1.4
        _CrestBackWidth("마루 뒷면 폭(m). 클수록 뒤로 길게 끌린다", Range(0.5, 8.0)) = 3.6
        _CurlAmount("꼭대기가 물가 쪽으로 말리는 양(마루 높이 대비 비율)", Range(0.0, 2.0)) = 0.55

        _ShoalPeakV("마루가 최대로 솟는 지점(v). 이보다 바다쪽은 낮다", Range(0.05, 0.8)) = 0.30
        _BreakV("부서져 주저앉는 지점(v). 이보다 물가쪽은 무너진다", Range(0.02, 0.5)) = 0.16

        _FoamLipStart("꼭대기 거품이 시작되는 마루 프로파일 값", Range(0.2, 1.0)) = 0.62
        _WakeLength("마루가 지나간 뒤 흰 물이 남는 길이(m)", Range(0.2, 8.0)) = 3.0
        _FoamBase("텍스처 없이도 남는 흰 정도(단색 폴백의 근거)", Range(0.0, 1.0)) = 0.45
        _FoamTiling("거품 텍스처 타일링(1/m). 0.22 = 한 타일 4.5m - MGShoreline과 같은 값", Float) = 0.22
        _FoamDissolveSoft("디졸브 경계 부드러움", Range(0.01, 0.60)) = 0.20

        _CrestAlpha("물 몸통 알파(뒤가 비치는 정도)", Range(0.0, 1.0)) = 0.55
        _FoamOpacity("거품 알파", Range(0.0, 1.0)) = 0.92

        _AlongshoreWobble("연안 방향 도달 시각 변주 - ★ MGShoreline과 같은 값이어야 한다 ★", Range(0.0, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShoreBreakForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            // include 경로는 MGShoreline/MGOcean에서 그대로 복사한 것이다(URP 17).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다
            // (텍스처 오브젝트 자체는 CBUFFER 밖이 규칙이다).
            CBUFFER_START(UnityPerMaterial)
                float4 _FoamMap_ST;
                half4 _FoamColor;
                half4 _CrestColor;
                half _CrestGain;
                half _CalmScale;
                float _CrestMaxHeight;
                float _CrestFrontWidth;
                float _CrestBackWidth;
                half _CurlAmount;
                half _ShoalPeakV;
                half _BreakV;
                half _FoamLipStart;
                float _WakeLength;
                half _FoamBase;
                float _FoamTiling;
                half _FoamDissolveSoft;
                half _CrestAlpha;
                half _FoamOpacity;
                half _AlongshoreWobble;
            CBUFFER_END

            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);

            // ---- C#이 Shader.SetGlobal*로 밀어주는 전역(Properties/CBUFFER 밖 - 헤더 규약) ----
            float _MG_ShoreTime;    // ShorelineWaves.cs
            float4 _MG_ShoreParams; // ShorelineWaves.cs : (주기 s, 전선 속도 m/s, 도달 잔잔 m, 도달 거침 m)
            float4 _MG_SeaState;    // OceanWaves.cs(읽기 전용) : x = 거칠기 0~1, y = 해수면 y(m)
            float4 _MG_WaveAmp;     // OceanWaves.cs(읽기 전용) : 성분별 진폭 A_i(m)

            // ★ MGShoreline.shader의 MGShoreWobble과 **한 글자도 다르지 않은 사본**이다. ★
            // 파장 63m/41m라 몇 미터 안에서는 거의 상수 - 물가를 사이에 둔 두 셰이더가 같은 지점에서
            // 같은 변주를 받아 전선이 이어진다. 한쪽만 고치면 마루와 거품이 어긋난다.
            float MGShoreWobble(float2 p)
            {
                return 0.6 * sin(p.x * 0.099 + p.y * 0.061)
                     + 0.4 * sin(p.x * -0.043 + p.y * 0.152 + 2.1);
            }

            // 이 지점을 마루가 지나간 뒤 경과 시간 tau(s). 0 = 지금 마루가 여기 있다.
            // MGShoreline의 tau와 같은 정의이며, 부호만 바다 쪽으로 연장돼 있다(헤더의 위상 계약).
            float MGBreakTau(float s, float2 pWS, float period, float celerity)
            {
                float tLocal = _MG_ShoreTime + s / max(celerity, 0.2);
                tLocal += MGShoreWobble(pWS) * period * _AlongshoreWobble;
                return frac(tLocal / period) * period; // HLSL frac은 음수에서도 0~1을 준다
            }

            // 마루 단면(0~1). 앞면(물가 쪽)은 sqrt로 급하게 서고 뒷면은 제곱으로 길게 끌린다 -
            // 실제 쇄파의 비대칭(앞이 벽, 뒤가 사면)이 이 두 지수 차이 하나로 나온다.
            float MGBreakProfile(float tau, float period, float celerity, out float lag)
            {
                lag = tau * celerity;                    // 마루는 지금 여기서 lag m 물가 쪽에 있다
                float lead = (period - tau) * celerity;  // 다음 마루는 lead m 바다 쪽에 있다
                float back = saturate(1.0 - lag / max(_CrestBackWidth, 0.05));
                float front = saturate(1.0 - lead / max(_CrestFrontWidth, 0.05));
                return max(back * back, sqrt(front));
            }

            // 바다→물가 진행에 따른 포락선. 바깥에서 0(아직 안 솟았다) → _ShoalPeakV에서 최대(쇄파 직전)
            // → 물가에서 다시 0(부서져 주저앉았다). 마루가 달리는 동안 자기 위치의 이 값을 밟고 가므로
            // "밀려와 솟았다가 부서져 사라지는" 한 주기가 그대로 나온다.
            float MGBreakEnv(float v)
            {
                float shoal = smoothstep(1.0, _ShoalPeakV, saturate(v));
                float collapse = smoothstep(0.0, max(_BreakV, 0.01), saturate(v));
                return shoal * collapse;
            }

            // 마루 높이(m). OceanWaves가 미는 진폭 합에서 유도한다(헤더 참고 - 상수 고정 금지).
            float MGCrestHeight(half sea)
            {
                float ampSum = dot(_MG_WaveAmp, float4(1.0, 1.0, 1.0, 1.0));
                float h = ampSum * _CrestGain * lerp(_CalmScale, 1.0, sea);
                return min(h, _CrestMaxHeight);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0; // (u 연안거리 m, v 0~1)
                float2 shore      : TEXCOORD1; // (부호 있는 물가 거리 m[음수], 리본 폭 m)
                float2 seaward    : TEXCOORD2; // 바다 방향 단위벡터(XZ)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 wave       : TEXCOORD3; // (profile, env, lag m, s m)
                float  fogFactor  : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float period = max(_MG_ShoreParams.x, 0.5);
                float celerity = max(_MG_ShoreParams.y, 0.2);
                half sea = saturate(_MG_SeaState.x);
                float crestH = MGCrestHeight(sea);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float2 basePos = positionWS.xz;

                float s = max(-IN.shore.x, 0.0);        // 물가에서 바다 쪽으로 s미터
                float width = max(IN.shore.y, 0.5);
                float v = saturate(IN.uv.y);

                float lag;
                float tau = MGBreakTau(s, basePos, period, celerity);
                float profile = MGBreakProfile(tau, period, celerity, lag);
                float env = MGBreakEnv(v);
                float lift = profile * env * crestH;

                // 해안 직교 방향의 유한차분으로 노멀을 잡는다(해석 미분보다 짧고, 말림 변위가
                // 들어간 뒤에도 어긋나지 않는다). 연안 방향 기울기는 마루가 능선이라 무시해도 된다.
                const float ds = 0.35;
                float s2 = s + ds;
                float v2 = saturate(s2 / width);
                float lag2;
                float tau2 = MGBreakTau(s2, basePos + IN.seaward * ds, period, celerity);
                float lift2 = MGBreakProfile(tau2, period, celerity, lag2) * MGBreakEnv(v2) * crestH;
                float slope = (lift2 - lift) / ds; // 바다 쪽으로 갈 때의 높이 변화

                // 꼭대기만 물가 쪽으로 말린다(profile^3라 능선 정점 근방에서만 값이 남는다).
                // 말림 양은 마루 높이에 비례하므로 진폭이 오르면 같이 커진다. 다만 **물가를 넘어
                // 뭍으로 올라오지는 못하게** 자기 위치(s)에서 0.3m를 남기고 자른다 - 마루가 모래
                // 위로 밀려 들어가면 스와시(MGShoreline) 담당 구간과 겹쳐 두 겹으로 보인다.
                float curl = profile * profile * profile * env * _CurlAmount * crestH;
                curl = min(curl, max(s - 0.3, 0.0));
                positionWS.xz -= IN.seaward * curl;
                positionWS.y += lift;

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.normalWS = normalize(float3(-IN.seaward.x * slope, 1.0, -IN.seaward.y * slope));
                OUT.wave = float4(profile, env, lag, s);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half sea = saturate(_MG_SeaState.x);
                float profile = IN.wave.x;
                float env = IN.wave.y;
                float lag = IN.wave.z;
                float s = IN.wave.w;
                float v = saturate(IN.uv.y);

                // ---- 물 몸통: 솟아 있는 곳에만 있다(마루가 없는 시간에는 리본이 통째로 투명) ----
                float body = saturate(profile * env);

                // ---- 거품 ----
                // (a) 꼭대기 립: 마루 능선. 거칠수록 굵다.
                half lip = smoothstep(_FoamLipStart, 1.0, profile) * env;
                // (b) 부서진 뒤 흰 물: 마루가 지나간 직후(lag이 작을 때)이고 물가 쪽 절반일 때만.
                //     env를 곱하지 않는다 - 부서진 흰 물은 마루가 주저앉은 **뒤에도** 남아야
                //     MGShoreline의 스와시 거품으로 끊김 없이 이어진다.
                half breakZone = 1.0 - smoothstep(0.10, 0.55, v);
                half wake = saturate(1.0 - lag / max(_WakeLength, 0.1)) * breakZone;
                half foamAmt = saturate(max(lip, wake) * lerp(0.70, 1.15, sea));
                // 바깥 가장자리에서는 거품도 사라진다(리본 경계가 직선으로 읽히지 않게).
                foamAmt *= 1.0 - smoothstep(0.85, 1.0, v);

                // 디졸브(노이즈 임계). MGShoreline의 스와시 거품과 같은 방식·같은 채널 계약이라
                // 물가를 사이에 두고 두 거품의 재질감이 같다. UV는 리본 로컬 미터(u, s)라
                // 마루를 따라 흰 줄무늬가 늘어난다.
                // (닫힌 해안선 고리에서는 u가 둘레 끝에서 0으로 돌아가므로 무늬가 이어지지 않는
                //  이음매가 **한 칸 폭**으로 생긴다. 정점 좌표는 정확히 붙어 있어 기하학적 틈은
                //  없고, 노이즈 무늬가 한 줄 어긋날 뿐이라 움직이는 흰 물 위에서는 읽히지 않는다.)
                float2 foamUV = float2(IN.uv.x, s) * _FoamTiling + float2(lag * -0.03, lag * 0.02);
                half4 fx = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, foamUV);
                half clump = lerp(0.55, 1.0, fx.a);
                half shape = fx.r * clump;
                half threshold = 1.0 - saturate(foamAmt);
                half dissolve = smoothstep(threshold, threshold + _FoamDissolveSoft, fx.g);
                // 텍스처가 없으면(black) shape·dissolve가 0이라 앞항만 남는다 = 단색 흰 마루 폴백.
                half foam = saturate(max(foamAmt * _FoamBase, shape * dissolve));

                half3 foamCol = _FoamColor.rgb * (0.85 + 0.30 * fx.b);
                half3 albedo = lerp(_CrestColor.rgb, foamCol, foam);

                // ---- 라이팅: 물이라 램버트를 그대로 쓰면 앞면이 새까매진다. 반램버트(wrap) + SH. ----
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.normalWS);
                half wrap = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * wrap);

                // 앞면 투과광: 얇은 물벽이 햇빛을 통과시켜 위쪽 가장자리가 밝게 뜬다.
                // 거품이 덮은 곳에서는 꺼서(1-foam) 흰 거품이 더 하얘지지 않게 한다.
                half through = saturate(profile * profile) * env * (1.0 - foam);
                color += _CrestColor.rgb * mainLight.color * (through * 0.45);

                half alpha = saturate(body * _CrestAlpha + foam * _FoamOpacity);
                color = MixFog(color, IN.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
        // ShadowCaster/DepthOnly/DepthNormals 패스는 넣지 않는다(헤더 규약).
    }

    Fallback "Universal Render Pipeline/Lit"
}
