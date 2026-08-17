// MGKelpSway - 해저 해초(kelp) 전용 정점 스웨이 URP 셰이더.
//
// SeabedFloraSpawner가 Resources.Load<Shader>("Shaders/MGKelpSway")로 로드해 해초 머티리얼에만
// 쓰고, 로드가 실패하면 기존 ResourceVisualLibrary.GetMaterial(색, "leaf") URP Lit 경로로
// 폴백한다(게임은 이 셰이더 없이도 돌아가야 한다 - MGOcean과 같은 폴백 계약).
//
// 설계 계약(SeabedFloraSpawner 쪽 주석과 맞물린다):
//  * 정점 스웨이: 오브젝트 밑동(로컬 y=0) 고정, 위로 갈수록 크게 흔들리는 굽힘.
//    가중치는 (피벗 위 월드 높이 / _BendHeight)² - y² 가중이라 밑동 근처는 거의 안 움직이고
//    끝이 크게 젖혀지는 "뿌리 박힌 해초" 움직임이 된다. _BendHeight(2.5m) 위는 포화라
//    리본형 큰 해초(최대 4.15m)도 진폭이 _SwayAmplitude를 넘지 않고, 방석형(0.26~0.64m)은
//    가중치가 수 % 수준이라 거의 정지 - 형태별로 자연스럽게 차등이 생긴다.
//  * 위상은 오브젝트 피벗의 **월드 위치** 기반 - 한 포기 안의 정점들은 같은 위상으로 통째로
//    굽고(찢김 없음), 포기마다 타이밍이 달라 숲 전체가 제각각 일렁인다.
//  * 진폭 합계 _SwayAmplitude = 0.12m(끝 기준, 기본값), 주기 4.6s + 3.4s 사인 2겹 합성.
//    콜라이더 없는 순수 장식이라(스포너 계약) 시각 전용 변위가 어떤 판정과도 어긋나지 않는다.
//  * 시간은 셰이더 내장 _Time이 아니라 C#(KelpSwayDriver.Update)이 매 프레임 넣는
//    _MG_SwayTime(Time.time)이다 - Time.timeScale = 0에서 멈추므로 타이틀 화면에서 해초가
//    정지하는 프로젝트 관례(MGOcean의 _MG_WaveTime과 동일)를 그대로 따른다.
//  * 라이팅은 URP Lit 간이형(램버트 + SH 앰비언트)의 Opaque. 알파 불필요. Cull Off - 해초
//    blade 메시는 얇은 면이라 뒷면도 그려야 하고, 뒷면은 SV_IsFrontFace로 노멀을 뒤집는다.
//  * [MainTexture] _BaseMap / [MainColor] _BaseColor - C#의 mainTexture/mainTextureScale/
//    material.color 대입이 URP Lit 때와 같은 의미로 그대로 통한다.
//  * CBUFFER(UnityPerMaterial)에 Properties 스칼라/색 전부 - SRP Batcher 호환(MGOcean과 동일).
//  * ShadowCaster/DepthOnly 패스는 넣지 않는다 - 해초 렌더러는 캐스팅/수신 모두 꺼져 있고
//    (SeabedFloraSpawner.CreateVisualPart), 뎁스 프리패스가 도는 구성에서도 해초가 깊이
//    텍스처에서 빠질 뿐(바다 물기둥 계산이 해저 모래를 대신 읽는다) 렌더는 망가지지 않는다
//    (MGOcean의 "패스 생략 - 우아한 열화"와 같은 선택).
Shader "MG/KelpSway"
{
    Properties
    {
        [MainTexture] _BaseMap("표면 그레인 텍스처(leaf.png)", 2D) = "white" {}
        [MainColor] _BaseColor("해초 색(4단 녹갈 팔레트가 들어온다)", Color) = (0.35, 0.45, 0.20, 1)
        _SwayAmplitude("끝 흔들림 진폭(m, 2겹 합계)", Range(0.0, 0.5)) = 0.12
        _BendHeight("굽힘 포화 높이(m, 이 높이부터 최대 진폭)", Float) = 2.5
        _MG_SwayTime("흔들림 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
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

            // 얇은 blade 메시의 뒷면도 그린다(잠수 중 어느 방향에서든 보인다).
            Cull Off

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
                float _SwayAmplitude;
                float _BendHeight;
                float _MG_SwayTime;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // ---- 스웨이 2겹: 주기 4.6s / 3.4s (과제 대역 3~5s), 방향·위상 소금이 서로 다르다. ----
            // 진폭 배분은 5:3(합 1) - _SwayAmplitude가 곧 끝 기준 합계 진폭(m)이 된다.
            #define MG_SWAY_W1 1.366        // 2*PI / 4.6s
            #define MG_SWAY_W2 1.848        // 2*PI / 3.4s
            #define MG_SWAY_A1 0.625
            #define MG_SWAY_A2 0.375
            #define MG_SWAY_DIR1 float2( 0.874,  0.485)
            #define MG_SWAY_DIR2 float2(-0.443,  0.897)
            // 위상 소금: 피벗 월드 xz에 곱해 포기마다 다른 타이밍을 만든다. 파장이 수 m 단위라
            // 같은 군락(반경 1.5~4.5m) 안에서도 이웃끼리 살짝 어긋나되, 흐름은 이어져 보인다.
            #define MG_SWAY_PHASE1 float2(0.53, 0.71)
            #define MG_SWAY_PHASE2 float2(-0.87, 0.39)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 피벗(밑동) 월드 위치 - 모델 계약상 밑면이 로컬 y=0이라 피벗이 곧 밑동이다.
                float3 originWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // y² 가중 굽힘: 피벗 위 월드 높이(스포너의 0.8~1.4 스케일까지 반영된 실제 높이)를
                // _BendHeight로 정규화해 제곱 - 밑동 0(고정), _BendHeight 이상 1(최대 진폭 포화).
                float bend = saturate(max(positionWS.y - originWS.y, 0.0) / max(_BendHeight, 0.001));
                bend *= bend;

                // 포기 단위 위상(피벗 기준)이라 한 포기는 통째로 굽는다 - 정점별 위상이면 얇은
                // blade가 물결치며 찢겨 보인다.
                float t = _MG_SwayTime;
                float phase1 = dot(originWS.xz, MG_SWAY_PHASE1);
                float phase2 = dot(originWS.xz, MG_SWAY_PHASE2);
                float2 sway = MG_SWAY_DIR1 * (MG_SWAY_A1 * sin(t * MG_SWAY_W1 + phase1))
                            + MG_SWAY_DIR2 * (MG_SWAY_A2 * sin(t * MG_SWAY_W2 + phase2));
                // 월드 공간 변위(m) - 오브젝트 스케일과 무관하게 끝 진폭이 _SwayAmplitude로 유지된다.
                positionWS.xz += sway * (bend * _SwayAmplitude);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // 변위가 0.12m 수준이라 노멀 재계산은 생략한다(램버트 확산광에서 식별 불가 수준).
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // Cull Off 뒷면은 노멀을 뒤집어야 라이팅이 성립한다(MGOcean과 같은 규칙).
                float3 normalWS = normalize(IN.normalWS);
                normalWS = isFrontFace ? normalWS : -normalWS;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                // 라이팅: 메인 라이트 램버트 + SH 앰비언트(URP Lit 간이형 - 수중 장식이라
                // 스페큘러/그림자 수신은 뺀다. 렌더러도 receiveShadows = false다).
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * ndotl);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
