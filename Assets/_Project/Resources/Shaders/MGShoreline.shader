// MGShoreline - 해변 모래 캡(DrySand / DampSand / WetSand) 전용 URP 셰이더.
//
// IslandMeshGenerator.BuildCapLayer가 Resources.Load<Shader>("Shaders/MGShoreline")로 로드해
// 모래 캡 3장의 머티리얼 셰이더를 갈아 끼운다. 로드가 실패하면 캡은 예전 그대로 URP Lit으로
// 남는다(게임은 이 셰이더 없이도 돌아가야 한다 - MGOcean/MGGrass와 같은 폴백 계약).
//
// ── 무엇을 그리는가 ────────────────────────────────────────────────────────────
// 파도가 밀려왔다 빠지는 해변을 2단으로 그린다.
//   (1) 젖은 모래: 파도가 닿았던 자리가 어두워지고(_WetDarken 0.6) 반들거린다(_WetSmoothness).
//       밀려올 때 0.3초 만에 젖고(_WetInSeconds), 빠진 뒤 지수 감쇠로 천천히 마른다(_DryOutSeconds 3초).
//   (2) 스와시 거품: 전선 근처의 얇은 흰 띠. **알파 페이드가 아니라 디졸브**(노이즈 임계)로
//       지워져 물이 모래에 스며드는 느낌이 난다.
// 둘 다 프래그먼트 계산이고 정점은 한 톨도 움직이지 않는다(캡 메시는 지형 메시의 복사본이라
// 움직이면 지형과 어긋난다). 드로우콜은 캡 머티리얼을 갈아 끼우는 것뿐이라 +0이다.
//
// ── 물가 거리장(shore SDF) 계약 ────────────────────────────────────────────────
// 파도 위상이 **해안선 모양을 따라 평행하게** 진행해야 한다(만은 오목하게, 곶은 늦게).
// 그래서 각 정점의 "물가(y=0 등고선)까지의 수평 거리"를 지형 생성 시점에 굽는다:
//   IslandMeshGenerator.BakeShoreField() → 지형 메시의 **UV2(TEXCOORD1)**
//     uv2.x = 물가까지의 부호 있는 수평 거리(m). + = 내륙(해발), - = 물속.
//     uv2.y = 그 정점의 해수면 기준 높이(m). 예비 채널(현재 셰이더는 x만 쓴다).
// BuildCapLayer가 캡 메시로 정점을 옮길 때 UV2도 같이 옮긴다. 런타임 비용 0이다.
//
// 왜 정점 색이 아니라 UV2인가:
//   · 값이 **미터 단위 부호 있는 실수**이고 범위가 섬 크기에 비례한다(실측 -139m ~ +121m,
//     시작 섬 -13.8m ~ +37.1m). 정점 색은 Color32로 눌려 0~1 8비트라, 200m를 담으면
//     1LSB가 0.78m가 된다 - 스와시 폭(1~3m)이 3~4단계로 계단이 진다.
//   · UV2는 float2라 양자화가 없고, 부호도 그대로 실린다.
//   · 캡 머티리얼은 이 셰이더 로드 실패 시 URP Lit으로 남는데, URP Lit은 UV2를 라이트맵
//     좌표로만 보고 라이트맵이 없으면 무시한다(정점 색과 마찬가지로 무해).
//
// ── 시간·파라미터 전역 (ShorelineWaves.cs가 단일 소스) ─────────────────────────
//   _MG_ShoreTime   : 파도 시계(초). C#이 매 프레임 Time.time을 넣는다. Time.timeScale = 0에서
//                     멈추므로 타이틀 화면에서 파도가 정지하는 프로젝트 계약이 그대로 유지된다
//                     (MGOcean _MG_WaveTime / MGGrass _MG_WindTime 선례).
//   _MG_ShoreParams : x = 주기(s), y = 스와시 전선 속도(m/s), z = 잔잔할 때 최대 도달거리(m),
//                     w = 거칠 때 최대 도달거리(m).
//   _MG_SeaState    : **OceanWaves.cs가 이미 밀고 있는 전역**(읽기만 한다).
//                     x = 바다 거칠기 0~1, y = 평균 해수면 y(m). 거칠수록 파도가 멀리 올라오고
//                     거품이 굵어진다.
// ※ 이 셋은 반드시 Properties 블록 **밖**·UnityPerMaterial CBUFFER **밖**에 선언한다.
//   Properties에 넣으면 머티리얼 프로퍼티가 되어 전역 설정이 무시되고(머티리얼 값이 이긴다),
//   CBUFFER 안에 넣으면 SRP Batcher 레이아웃과 충돌한다(MGOcean 헤더와 같은 규약).
// ※ 드라이버(ShorelineWaves)가 없으면 _MG_ShoreParams가 0이라 도달거리 0 = 젖음/거품 없음.
//   런타임에는 SubsystemRegistration 시점에 기본값이 즉시 밀리므로 실제로 0인 프레임은 없다.
//
// ── 거품 텍스처 계약 (Textures/shore_foam, 무이음 1024²) ───────────────────────
//   R = 거품 마스크 / G = 디졸브 노이즈 / B = 미세 디테일 / A = 큰 덩어리 마스크.
// 아직 파일이 없을 수 있다. _FoamMap의 기본값이 "black"이라 **텍스처가 없으면 R=0 → 거품 0**,
// 즉 젖음만 동작하고 거품은 조용히 생략된다(분기도 경고도 없다 - 우아한 열화).
//
// ── ShorelineBand / MGOcean 해안 거품과의 중복 방지 ────────────────────────────
//   · WorldMapManager.CreateShorelineBand(반경 0.95R 원형 반투명 고리, 해수면 +0.05m)는
//     MGOcean이 살아 있으면 **아예 생성되지 않는다**(oceanCustomShaderActive). 폴백 경로에서만
//     생기고, 그때도 안쪽 끝이 0.95R = 실측 물가(0.72~0.87R)보다 4m 이상 바다 쪽이라
//     스와시 구간(물가 ±3m)과 겹치지 않는다.
//   · 실제로 겹칠 뻔한 것은 MGOcean의 **깊이 기반 해안 거품**(물 기둥 0~0.7m)이다. 그쪽은
//     물가에서 바다 쪽으로 2m 남짓을 덮는다. 그래서 이 셰이더의 거품은 smoothstep(-0.30, 0.15, d)로
//     **물가 안쪽(모래 위)만** 담당한다 - 두 거품이 물가를 사이에 두고 한 줄로 이어진다.
//
// ── 셰이더 규약(기존 5종과 동일) ───────────────────────────────────────────────
//   · Properties의 스칼라/색/벡터는 전부 CBUFFER(UnityPerMaterial) - SRP Batcher 호환.
//     텍스처 오브젝트 자체는 CBUFFER 밖이 규칙이다.
//   · ShadowCaster/DepthOnly 패스는 넣지 않는다. 모래 캡 렌더러는 shadowCastingMode.Off이고
//     (BuildCapLayer - 지면에서 8cm 뜬 덮개가 자기 그림자를 드리우면 얼룩이 된다),
//     깊이/그림자 패스가 필요해지면 Fallback의 URP Lit 패스가 대신 채운다.
//   · **그림자는 받는다.** 캡 렌더러의 receiveShadows = true이고 지형 본체/초지 캡은 URP Lit이라,
//     여기만 그림자를 안 받으면 야자수 그림자가 모래 경계에서 뚝 끊긴다.
//   · Opaque/Geometry 큐 - URP Lit이던 예전과 같은 자리에서 그려진다. 캡의 y 오프셋 0.08m과
//     렌더 순서를 한 글자도 건드리지 않으므로 z-파이팅 상황이 달라지지 않는다.
Shader "MG/Shoreline"
{
    Properties
    {
        // C#(StructureVisualBuilder.CreateColorMaterial → BuildCapLayer)이 넣는 값들.
        // [MainTexture]/[MainColor]라 material.mainTexture / material.color / mainTextureScale이
        // URP Lit 때와 **같은 의미로 그대로** 통한다(셰이더를 갈아 끼워도 값이 살아남는 근거).
        [MainTexture] _BaseMap("모래 텍스처(sand.png)", 2D) = "white" {}
        [MainColor] _BaseColor("모래색 - 아키타입 sandColor의 명도 단계", Color) = (0.761, 0.698, 0.502, 1)

        _FoamMap("스와시 거품(R 마스크 / G 디졸브 / B 디테일 / A 덩어리). 없으면 black = 거품 없음", 2D) = "black" {}
        _FoamColor("거품색", Color) = (0.95, 0.97, 0.98, 1)

        _WetDarken("젖은 모래 알베도 배율(1 = 안 어두워짐)", Range(0.30, 1.0)) = 0.60
        _DrySmoothness("마른 모래 매끄러움", Range(0.0, 1.0)) = 0.05
        _WetSmoothness("젖은 모래 매끄러움(반들거림)", Range(0.0, 1.0)) = 0.62
        _SpecularStrength("스페큘러 세기", Range(0.0, 2.0)) = 0.55

        _WetInSeconds("젖는 시간(s) - 스와시가 덮은 뒤 완전히 젖기까지", Range(0.05, 2.0)) = 0.30
        _DryOutSeconds("마르는 시간상수(s) - 지수 감쇠", Range(0.5, 8.0)) = 3.0

        _FoamLifeSeconds("거품 수명(s) - 전선 통과 후. 폭 = 이 값 × 전선 속도", Range(0.05, 3.0)) = 0.55
        _BackwashFoam("빠질 때 남는 잔거품 세기", Range(0.0, 1.0)) = 0.35
        _FoamTiling("거품 텍스처 타일링(1/m). 0.22 = 한 타일 4.5m", Float) = 0.22
        _FoamDissolveSoft("디졸브 경계 부드러움(작을수록 딱딱하게 지워진다)", Range(0.01, 0.60)) = 0.18

        _AlongshoreWobble("연안 방향 도달 시각 변주(주기 대비 비율)", Range(0.0, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            // 그림자 수신(위 규약 주석). URP Lit이 쓰는 것과 같은 배리언트 집합이다.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _FoamColor;
                half _WetDarken;
                half _DrySmoothness;
                half _WetSmoothness;
                half _SpecularStrength;
                float _WetInSeconds;
                float _DryOutSeconds;
                float _FoamLifeSeconds;
                half _BackwashFoam;
                float _FoamTiling;
                half _FoamDissolveSoft;
                half _AlongshoreWobble;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);

            // ---- C#이 Shader.SetGlobal*로 밀어주는 전역(Properties/CBUFFER 밖 - 헤더 규약) ----
            float _MG_ShoreTime;    // ShorelineWaves.cs
            float4 _MG_ShoreParams; // ShorelineWaves.cs : (주기 s, 전선 속도 m/s, 도달 잔잔 m, 도달 거침 m)
            float4 _MG_SeaState;    // OceanWaves.cs(읽기 전용) : x = 거칠기 0~1, y = 해수면 y(m)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 shore      : TEXCOORD1; // 물가 거리장(x = 거리 m, y = 높이 m) - 위 계약 참고
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 shore      : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            // 연안(해안선을 따라가는) 방향의 도달 시각 변주. -1~1.
            // 파장 63m/41m라 몇 미터 안에서는 거의 상수다 - "파도가 해안선과 평행하게 밀려온다"를
            // 깨지 않으면서, 섬 전체가 한 순간에 똑같이 젖는 인공적인 동기화만 부순다.
            float MGShoreWobble(float2 p)
            {
                return 0.6 * sin(p.x * 0.099 + p.y * 0.061)
                     + 0.4 * sin(p.x * -0.043 + p.y * 0.152 + 2.1);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shore = IN.shore;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // d = 물가까지의 수평 거리(m). + 내륙 / - 물속.
                float d = IN.shore.x;

                float period = max(_MG_ShoreParams.x, 0.5);
                float celerity = max(_MG_ShoreParams.y, 0.2);
                half sea = saturate(_MG_SeaState.x);
                // 거칠수록 파도가 더 멀리 올라온다(잔잔 z → 거침 w).
                float reach = max(lerp(_MG_ShoreParams.z, _MG_ShoreParams.w, sea), 0.05);

                // ---- 파도 위상 ----
                // 스와시 전선은 물가에서 내륙으로 celerity(m/s)로 올라간다 → d만큼 안쪽 지점은
                // d/celerity초 늦게 같은 파형을 겪는다. 설계식
                //   phase = frac(_MG_ShoreTime / period - shoreDistance / waveSpeed)
                // 와 같은 형태이며(waveSpeed = celerity × period), 지연을 초 단위로 먼저 빼서
                // "전선이 지나간 뒤 경과 시간 tau"를 그대로 얻는다(젖음/거품 곡선이 tau의 함수다).
                float tLocal = _MG_ShoreTime - max(d, 0.0) / celerity;
                tLocal += MGShoreWobble(IN.positionWS.xz) * period * _AlongshoreWobble;
                float phase = frac(tLocal / period);   // HLSL frac은 음수에서도 0~1을 준다
                float tau = phase * period;            // 전선 통과 후 경과 시간(s)

                // ---- (1) 젖음: 비대칭 곡선 - 밀려올 때 빠르고(0.3s) 빠질 때 느리다(지수 감쇠 3s) ----
                float wetIn = max(_WetInSeconds, 0.02);
                float dryTau = max(_DryOutSeconds, 0.10);
                float rise = saturate(tau / wetIn);                          // 0 → 1 (0.3초)
                float fall = exp(-max(tau - wetIn, 0.0) / dryTau);           // 그 뒤 지수 건조
                // 직전 주기의 잔여 습기. 이게 없으면 tau가 주기 끝에서 0으로 감길 때 젖음이
                // 뚝 끊겨 물가 전체에 깜빡이는 줄이 생긴다(두 항의 max라 C0 연속이다).
                float prevCycle = exp(-(tau + period - wetIn) / dryTau);
                float wetCycle = max(rise * fall, prevCycle);

                // 도달 한계. reach를 넘는 마른 모래는 이번 파도가 닿지 않는다(가장자리 0.45m 소프트).
                float reachFade = 1.0 - smoothstep(max(reach - 0.45, 0.0), reach + 0.10, d);
                // 해수면 아래는 늘 젖어 있다(물속 모래 캡 - 반투명 바다 너머로 비친다).
                float submerged = 1.0 - smoothstep(-0.35, 0.05, d);
                float wet = saturate(max(submerged, wetCycle * reachFade));

                // ---- (2) 스와시 거품 ----
                // 전선 근처(tau가 작을 때)의 얇은 띠. 폭 ≈ 전선 속도 × _FoamLifeSeconds.
                float foamFront = saturate(1.0 - tau / max(_FoamLifeSeconds, 0.05));
                // 빠질 때 남는 잔거품 - 훨씬 약하게, 디졸브로 서서히 지워진다(backwash).
                float backwash = _BackwashFoam * exp(-max(tau - _FoamLifeSeconds, 0.0) / (dryTau * 0.6));
                float foamAmt = max(foamFront, backwash);
                // 물가 **안쪽(모래 위)** 전용. 바다 쪽 거품은 MGOcean의 깊이 거품이 담당한다(헤더 참고).
                foamAmt *= smoothstep(-0.30, 0.15, d) * reachFade * lerp(0.85, 1.20, sea);

                // 디졸브(노이즈 임계). 알파 페이드가 아니라 임계를 올려 지우므로 물이 모래에
                // 스며들듯 얼룩덜룩하게 사라진다. 텍스처가 없으면 fx = 0 → 거품 0(조용한 생략).
                float2 foamUV = IN.positionWS.xz * _FoamTiling + float2(phase * 0.07, phase * -0.04);
                half4 fx = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, foamUV);
                half clump = lerp(0.55, 1.0, fx.a);                       // A: 큰 덩어리 - 굵기 변조
                half shape = fx.r * clump;                                // R: 거품 마스크
                half threshold = 1.0 - saturate(foamAmt);                 // 세기가 낮을수록 임계가 높다
                half dissolve = smoothstep(threshold, threshold + _FoamDissolveSoft, fx.g); // G: 디졸브 노이즈
                half foam = saturate(shape * dissolve);
                half3 foamCol = _FoamColor.rgb * (0.82 + 0.30 * fx.b);    // B: 미세 디테일

                // ---- 합성 ----
                // 모래색은 아키타입 sandColor(× 명도 단계)를 그대로 쓴다 - 화산암섬의 검은 모래가
                // 노래지지 않는 근거는 여기 한 줄이다(_BaseColor를 다른 색으로 덮지 않는다).
                half3 sand = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                half3 albedo = lerp(sand, sand * _WetDarken, wet);
                half smoothness = lerp(_DrySmoothness, _WetSmoothness, wet);
                albedo = lerp(albedo, foamCol, foam);
                smoothness = lerp(smoothness, 0.30, foam);               // 거품은 젖은 모래만큼 반짝이지 않는다

                // ---- 라이팅: 메인 라이트 램버트(그림자 수신) + SH 앰비언트 + 블린-퐁 스페큘러 ----
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 normalWS = normalize(IN.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half shadow = mainLight.shadowAttenuation;

                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * (ndotl * shadow));

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 halfDir = normalize(viewDir + mainLight.direction);
                half specPower = exp2(smoothness * 10.0 + 1.0);
                // 세기에 smoothness를 한 번 더 곱한다 - 마른 모래(0.05)는 사실상 무광,
                // 젖은 모래(0.62)에서만 물기 하이라이트가 뜬다.
                half spec = pow(saturate(dot(normalWS, halfDir)), specPower)
                    * _SpecularStrength * smoothness;
                color += mainLight.color * (spec * shadow) * step(0.001, ndotl);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
        // ShadowCaster/DepthOnly/DepthNormals 패스는 넣지 않는다(헤더 규약).
        // 필요해지면 아래 Fallback의 URP Lit 패스가 그 자리를 채운다.
    }

    Fallback "Universal Render Pipeline/Lit"
}
