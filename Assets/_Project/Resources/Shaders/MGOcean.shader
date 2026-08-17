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
//  * 버텍스 파도 진폭 합계 0.24m(_WaveAmplitude=1 기준) ≤ 0.25m - 수영/잠수 판정이
//    y = seaLevel 하나로 이뤄지는 시각 전용 파도라, 크게 두면 판정과 어긋나 보인다.
//  * 파도 시간은 셰이더 내장 _Time이 아니라 C#(Update)이 매 프레임 넣는 _MG_WaveTime을 쓴다.
//    Time.time은 Time.timeScale = 0에서 멈추므로 타이틀 화면에서 바다가 정지하는 기존 동작이
//    그대로 유지된다(UV 스크롤도 같은 이유로 C#에서 멈춘다).
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
        _WaveAmplitude("버텍스 파도 진폭 배율(1 = 합계 0.24m)", Range(0.0, 1.0)) = 1.0
        _RippleStrength("잔물결 노멀 퍼터베이션 세기", Range(0.0, 2.0)) = 1.0
        _MG_WaveTime("파도 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
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
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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

            // ---- 버텍스 파도: 방향 사인 3겹 합성. 진폭 0.10 + 0.08 + 0.06 = 0.24m(≤ 0.25m). ----
            // 파장은 121m / 71m / 40m - 바다 격자 칸이 625m라 근거리에서는 정점 단위로 샘플링되는
            // 저주파 일렁임으로 보이고, 진폭이 작아 어떤 경우에도 판정(y = seaLevel)과 크게 어긋나지 않는다.
            #define MG_WAVE_DIR1 float2( 0.913,  0.408)
            #define MG_WAVE_DIR2 float2(-0.500,  0.866)
            #define MG_WAVE_DIR3 float2( 0.197, -0.980)
            #define MG_WAVE_K1 0.052   // 2*PI / 121m
            #define MG_WAVE_K2 0.089   // 2*PI / 71m
            #define MG_WAVE_K3 0.157   // 2*PI / 40m
            #define MG_WAVE_A1 0.10
            #define MG_WAVE_A2 0.08
            #define MG_WAVE_A3 0.06
            #define MG_WAVE_S1 0.80
            #define MG_WAVE_S2 1.10
            #define MG_WAVE_S3 1.50

            float MGWaveHeight(float2 p, float t)
            {
                float h = 0.0;
                h += MG_WAVE_A1 * sin(dot(p, MG_WAVE_DIR1) * MG_WAVE_K1 + t * MG_WAVE_S1);
                h += MG_WAVE_A2 * sin(dot(p, MG_WAVE_DIR2) * MG_WAVE_K2 + t * MG_WAVE_S2);
                h += MG_WAVE_A3 * sin(dot(p, MG_WAVE_DIR3) * MG_WAVE_K3 + t * MG_WAVE_S3);
                return h;
            }

            // 파고 함수의 해석적 기울기(dH/dx, dH/dz). 프래그먼트에서 큰 파도의 노멀 성분으로 쓴다.
            float2 MGWaveSlope(float2 p, float t)
            {
                float2 s = float2(0.0, 0.0);
                s += MG_WAVE_DIR1 * (MG_WAVE_A1 * MG_WAVE_K1) * cos(dot(p, MG_WAVE_DIR1) * MG_WAVE_K1 + t * MG_WAVE_S1);
                s += MG_WAVE_DIR2 * (MG_WAVE_A2 * MG_WAVE_K2) * cos(dot(p, MG_WAVE_DIR2) * MG_WAVE_K2 + t * MG_WAVE_S2);
                s += MG_WAVE_DIR3 * (MG_WAVE_A3 * MG_WAVE_K3) * cos(dot(p, MG_WAVE_DIR3) * MG_WAVE_K3 + t * MG_WAVE_S3);
                return s;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 파도 위상은 월드 위치 기반 - 메시가 원점 고정이지만, 계약상 오브젝트 공간이 아니라
                // 월드 공간을 기준으로 계산한다(TransformObjectToWorld).
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS.y += MGWaveHeight(positionWS.xz, _MG_WaveTime) * _WaveAmplitude;

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

                // 노멀 = 큰 파도 기울기 + 잔물결 2겹. 진폭이 작아도 기울기는 스페큘러를 충분히 흔든다.
                float2 slope = MGWaveSlope(IN.positionWS.xz, t) * _WaveAmplitude;
                slope += MGRippleSlope(IN.positionWS.xz, t, viewDist) * _RippleStrength;
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
