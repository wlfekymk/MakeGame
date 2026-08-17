// MGOcean - 바다 평면 전용 커스텀 URP 셰이더.
//
// WorldMapManager.CreateOceanMaterial()이 Resources.Load<Shader>("Shaders/MGOcean")로 로드해
// 사용하고, 로드가 실패하면 기존 URP Lit 경로로 폴백한다(게임은 이 셰이더 없이도 돌아가야 한다).
//
// 설계 계약(WorldMapManager 쪽 주석과 맞물린다):
//  * 불투명(Opaque) 유지 - 투명으로 바꾸면 ShorelineBand(해수면 +0.05m의 반투명 띠)와의
//    정렬/소트 문제가 생긴다.
//  * _CameraDepthTexture 사용 금지 - 파이프라인 에셋에서 꺼져 있을 수 있으므로 뎁스 페이드 없이
//    프레넬/거리 기반 효과만 쓴다.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _ShallowColor;
                half _FresnelPower;
                half _Smoothness;
                half _SpecularStrength;
                half _WaveAmplitude;
                half _RippleStrength;
                float _MG_WaveTime;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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
            // 원거리에서 알리아싱 반짝임이 생기므로, 겹마다 카메라 거리 기반으로 세기를 감쇠시킨다
            // (뎁스 텍스처가 아니라 월드 거리만 쓰므로 _CameraDepthTexture 금지 계약과 무관하다).
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

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _MG_WaveTime;
                float3 toCamera = _WorldSpaceCameraPos - IN.positionWS;
                float viewDist = length(toCamera);
                float3 viewDir = toCamera / max(viewDist, 0.0001);

                // 노멀 = 큰 파도 기울기 + 잔물결 2겹. 진폭이 작아도 기울기는 스페큘러를 충분히 흔든다.
                float2 slope = MGWaveSlope(IN.positionWS.xz, t) * _WaveAmplitude;
                slope += MGRippleSlope(IN.positionWS.xz, t, viewDist) * _RippleStrength;
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));

                // 프레넬 기반 색 블렌드: 내려다보면(N·V 큼) 청록, 수평선 쪽(N·V 작음)은 깊은 파랑.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, fresnel);

                half3 grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half3 albedo = grain * waterColor * _BaseColor.rgb;

                // 라이팅: 메인 라이트 램버트 + 블린-퐁 스페큘러 + SH 앰비언트.
                // GGX/그림자 수신은 일부러 뺐다 - include 실패 가능성이 낮은 단순한 구성을 택한다.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * ndotl);

                float3 halfDir = normalize(mainLight.direction + viewDir);
                half specPower = exp2(_Smoothness * 10.0 + 1.0);
                half spec = pow(saturate(dot(normalWS, halfDir)), specPower);
                // 프레넬만큼 스페큘러를 키워 수평선 쪽 물비늘 반짝임을 강조한다.
                color += mainLight.color * (spec * _SpecularStrength * (0.4 + 0.6 * fresnel)) * step(0.001, ndotl);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
        // ShadowCaster/DepthOnly 패스는 넣지 않는다 - 바다 렌더러는 shadowCastingMode.Off이고
        // (CreateOcean 주석 참고), 뎁스 프리패스 의존 기능도 이 셰이더에는 없다.
    }

    Fallback "Universal Render Pipeline/Lit"
}
