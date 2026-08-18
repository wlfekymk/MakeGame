// MGRainScreen - 화면(렌즈)에 맺히는 빗방울 오버레이 전용 URP 셰이더.
//
// StormEffects가 Resources.Load<Shader>("Shaders/MGRainScreen")로 로드해 **카메라 자식의 화면 크기
// 쿼드 1장**에 쓴다. 셰이더나 텍스처(Textures/rain_droplet)가 없으면 오버레이 오브젝트 자체를 만들지
// 않는다 - 게임은 이 셰이더 없이도 그대로 돌아간다(MGCaustics/MGGodRay와 같은 폴백 계약).
//
// ── 왜 "카메라 자식 쿼드 + 클립 좌표 직접 출력"인가 ────────────────────────────────
// UnderwaterVisuals의 카우스틱 오버레이와 정확히 같은 관용구다(MGCaustics.shader 헤더 참고).
// 쿼드 메시의 로컬 좌표가 [-0.5, 0.5]²라 정점에서 ×2하면 곧 NDC가 되므로, 카메라 FOV/자세와
// 무관하게 언제나 화면을 정확히 덮는다. RenderFeature/Blit을 추가하지 않으므로 URP 에셋을
// 건드릴 일이 없고(에이전트가 .asset을 편집할 수 없다), 드로우콜은 정확히 1이다.
// 비가 오지 않거나 실내면 C#이 렌더러를 꺼 버리므로 평상시 비용은 0이다.
//
// ── 텍스처 계약 (Textures/rain_droplet.png, 512²) ───────────────────────────────
//   RG = 물방울 표면 노멀 xy (0.5 = 평평)   → 굴절 오프셋 (rg*2-1)
//   B  = 흘러내린 자국(streak/flow)          → 아래로 천천히 흐르는 층에만 쓴다
//   A  = 물방울 마스크                        → 굴절/하이라이트의 세기
// **중앙이 성기고 가장자리가 촘촘하게** 그려져 있으므로 _Tiling = 1(화면 한 장)이 기본이다.
// 이때 텍스처 좌표가 곧 화면 좌표라 물방울 분포가 설계 그대로 화면에 얹힌다.
//
// ── sRGB 방어 ────────────────────────────────────────────────────────────────
// rain_droplet은 **데이터 텍스처**다(RG가 노멀). 임포터에서 sRGB가 켜져 있으면 0.5가 0.214로
// 밀려 (rg*2-1) = -0.57이 되고, 화면 전체가 한쪽으로 밀린 굴절판이 된다. .meta는 이 작업의
// 편집 범위 밖이라 여기서 고칠 수 없으므로, C#이 런타임에 Texture.isDataSRGB를 물어보고
// _SrgbFix에 1을 넣어 준다. 그때 이 셰이더가 pow(c, 1/2.2)로 저장값을 되돌린다
// (RainWetness / MGShoreline의 _MG_RippleParams.z와 완전히 같은 방식이다).
// A 채널은 어떤 임포트 설정에서도 감마 변환을 받지 않으므로 보정 대상이 아니다.
//
// ── 굴절을 "차분(delta) 가산"으로 만든 이유 = 우아한 열화 ────────────────────────
// 굴절은 _CameraOpaqueTexture(URP Opaque Texture)를 밀어서 읽어야 한다. 그런데 그 텍스처가
// 파이프라인 설정에 따라 없을 수도 있고, 그때 "밀어 읽은 색"을 알파 블렌딩으로 화면에 얹으면
// 화면에 검은 얼룩이 남는다. 그래서 **원래 자리의 색과 밀어 읽은 색의 차이만** 가산한다:
//   · 정상 동작: 차이가 곧 렌즈 왜곡이라 물방울 자리에서 배경이 어긋나 보인다.
//   · 텍스처가 없거나 상수면: 두 샘플이 같아 delta = 0 → 굴절이 **조용히 사라질 뿐** 화면은 멀쩡하다.
// 대가: LDR 렌더 타깃에서는 음수 delta가 0으로 잘려 "어두워지는 쪽" 왜곡이 약해진다.
// 그 손실을 메우려고 노멀 기반 하이라이트를 아주 약하게 하나 더 얹었다(_Highlight).
//
// ── 그 밖의 계약 ─────────────────────────────────────────────────────────────
//  * Blend One One(가산) + ZWrite Off + ZTest Always + Cull Off.
//  * Queue "Transparent+100"(3100) - 바다 수면(3000)·파티클보다 뒤. 렌즈에 붙은 물방울이라
//    월드의 무엇보다도 나중에 그려지는 것이 맞다.
//  * 세기(_Strength)의 단독 소유자는 C#이다. 기본값 0이라 값을 넣기 전에는 아무것도 안 그린다.
//  * 시간은 내장 _Time이 아니라 C#이 넣는 _MG_RainScreenTime(Time.time) - 타이틀/일시정지
//    (timeScale = 0) 정지 관례(MGOcean _MG_WaveTime · MGCaustics _MG_CausticsTime과 동일).
//  * 화면 UV가 두 종류다. 물방울 배치/흐름에는 **정점에서 만든 y-up UV**(dropUV)를 쓰고,
//    씬 색 샘플에는 **SV_POSITION에서 만든 UV**(sceneUV)를 쓴다. 후자는 플랫폼별 Y 뒤집힘이
//    _CameraOpaqueTexture의 규약과 서로 상쇄되는 표준 경로다(MGCaustics의 깊이 샘플과 같다).
//  * CBUFFER(UnityPerMaterial)에 Properties의 스칼라/색 전부 - SRP Batcher 호환.
//  * ShadowCaster/DepthOnly 패스 없음 - 화면 오버레이가 그림자/깊이에 낄 이유가 없다.
Shader "MG/RainScreen"
{
    Properties
    {
        [MainTexture] _DropletMap("물방울 텍스처(RG 노멀 / B 흐름 자국 / A 마스크)", 2D) = "black" {}
        _Tint("물방울 하이라이트 색(비 오는 하늘의 청백)", Color) = (0.78, 0.86, 0.96, 1)
        _Strength("전체 세기(C#이 강우/시선각으로 넣는다. 0 = 안 그림)", Range(0.0, 1.0)) = 0.0
        _Refraction("굴절 화면 오프셋(화면 폭 대비 비율)", Range(0.0, 0.08)) = 0.018
        _Highlight("물방울 하이라이트 세기", Range(0.0, 1.0)) = 0.35
        _FlowSpeed("흐름 자국이 흘러내리는 속도(화면/초)", Range(0.0, 0.5)) = 0.045
        _FlowStrength("흐름 자국 세기", Range(0.0, 1.0)) = 0.45
        _Tiling("물방울 타일링(1 = 텍스처 한 장이 화면 하나)", Range(0.25, 4.0)) = 1.0
        _SrgbFix("sRGB 보정 스위치(C#이 넣는다. 1 = 보정)", Range(0.0, 1.0)) = 0.0
        _MG_RainScreenTime("물방울 시계(초, C#이 Time.time을 넣는다)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // 렌즈에 붙은 물방울 - 바다 수면(3000)과 파티클보다 뒤에 그린다.
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RainScreenOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            // 화면 전체 쿼드라 하드웨어 깊이 비교는 무의미하다.
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // SampleSceneColor(_CameraOpaqueTexture) - 굴절용. 없으면 delta가 0이 되어 조용히 빠진다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다
            // (텍스처 오브젝트 자체는 CBUFFER 밖이 규칙이다).
            CBUFFER_START(UnityPerMaterial)
                float4 _DropletMap_ST;
                half4 _Tint;
                half _Strength;
                half _Refraction;
                half _Highlight;
                half _FlowSpeed;
                half _FlowStrength;
                half _Tiling;
                half _SrgbFix;
                float _MG_RainScreenTime;
            CBUFFER_END

            TEXTURE2D(_DropletMap);
            SAMPLER(sampler_DropletMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // 물방울 배치/흐름 전용 화면 UV. NDC에서 직접 만들기 때문에 어떤 플랫폼에서도
                // **y가 위쪽**이다(흐름 방향이 플랫폼에 따라 뒤집히면 안 된다).
                float2 dropUV     : TEXCOORD0;
            };

            // 오브젝트 변환을 의도적으로 무시하고 클립 좌표를 직접 낸다(MGCaustics와 같은 방식).
            // 쿼드 로컬 좌표가 [-0.5, 0.5]²라 ×2가 곧 NDC [-1,1]²다.
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = float4(IN.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                OUT.dropUV = IN.positionOS.xy + 0.5;
                return OUT;
            }

            // 저장된 색을 선형으로 되돌린다(_SrgbFix = 1일 때만). RainWetness / MGShoreline과 같은 식.
            half3 MGUnSrgb(half3 c)
            {
                return lerp(c, pow(saturate(c), 1.0 / 2.2), _SrgbFix);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 씬 색 샘플용 UV - 플랫폼별 Y 규약이 _CameraOpaqueTexture와 상쇄되는 표준 경로.
                float2 sceneUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float aspect = _ScaledScreenParams.x / max(_ScaledScreenParams.y, 1.0);

                // 물방울 층: 화면 중심을 기준으로 확대/축소한다. _Tiling = 1이면 텍스처 좌표가
                // 곧 화면 좌표라, "중앙 성기고 가장자리 촘촘한" 텍스처 설계가 그대로 화면에 얹힌다.
                float2 dropUV = (IN.dropUV - 0.5) * _Tiling + 0.5;

                half4 d = SAMPLE_TEXTURE2D(_DropletMap, sampler_DropletMap, dropUV);
                half3 dc = MGUnSrgb(d.rgb);
                half2 n = dc.rg * 2.0 - 1.0;
                half mask = d.a;

                // 흐름 자국 층: 같은 텍스처의 B 채널을 세로로 늘여(0.55배) **아래로** 흘린다.
                // dropUV.y는 위쪽이 크므로, 샘플 좌표를 시간에 따라 키우면 무늬가 아래로 내려간다.
                // 시간 항은 half로 내리면 안 된다 - Time.time은 수천까지 커져서 half 정밀도로는
                // 흐름이 계단처럼 끊긴다(모바일 half 실장에서 실제로 드러나는 함정이다).
                float2 flowUV = dropUV * float2(1.0, 0.55)
                    + float2(0.0, _MG_RainScreenTime * (float)_FlowSpeed);
                half4 f = SAMPLE_TEXTURE2D(_DropletMap, sampler_DropletMap, flowUV);
                // B 채널만 쓴다. A(물방울 마스크)를 곱하면 안 된다 — 흐름 자국은 물방울이 **지나간
                // 뒤**에 남는 것이라 마스크와 겹치지 않는 자리에 그려져 있고, 곱하면 통째로 사라진다.
                half trail = MGUnSrgb(f.rgb).b;

                // ---- 굴절: 원래 자리와 밀어 읽은 자리의 **차이만** 가산한다(헤더의 열화 계약). ----
                // x를 종횡비로 나눠 물방울이 가로로 늘어나지 않게 한다.
                float2 offset = float2(n.x / max(aspect, 0.0001), n.y) * mask * _Refraction;
                half3 baseCol = SampleSceneColor(sceneUV);
                half3 refrCol = SampleSceneColor(sceneUV + offset);
                half3 delta = refrCol - baseCol;

                // ---- 하이라이트: 물방울의 위쪽 어깨가 하늘빛을 받아 반짝인다. ----
                // 노멀 z를 0.9로 고정한 반구 근사(텍스처에 z가 없다). 방향은 좌상단 고정.
                half3 nrm = normalize(half3(n, 0.9));
                half hi = saturate(dot(nrm, half3(-0.42, 0.72, 0.55)));
                hi = hi * hi;
                hi = hi * hi * mask * _Highlight;

                half3 outColor = (delta * 0.9
                                  + _Tint.rgb * hi
                                  + _Tint.rgb * (trail * _FlowStrength * 0.22)) * _Strength;

                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
