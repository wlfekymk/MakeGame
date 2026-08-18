// MGCaustics - 수중 카우스틱(수면 굴절 빛무늬) 가산 합성 전용 URP 셰이더.
//
// UnderwaterVisuals가 Resources.Load<Shader>("Shaders/MGCaustics")로 로드해 카메라 자식의
// 오버레이 쿼드에 쓰고, 로드가 실패하면 카우스틱 전체를 조용히 생략한다(게임은 이 셰이더
// 없이도 돌아가야 한다 - MGOcean/MGKelpSway/MGGrass와 같은 폴백 계약).
//
// ── 구현 방식: "해저에 깔린 데칼 메시"가 아니라 "깊이 재투영 오버레이" ─────────────────
// 과제 후보는 (a) 해저 머티리얼 교체와 (b) 해저 위에 띄운 얇은 데칼 메시였고, 여기서는
// (b)의 변형인 **화면 크기 오버레이 쿼드 1장 + _CameraDepthTexture 재투영**을 택했다.
// 기존 머티리얼을 하나도 건드리지 않는다는 점에서 (b)와 같은 관용구(RockCap/CrashSoilOverlay -
// "기존 재질 불변 + 가산 오버레이")이고, 다음 세 가지가 평면 데칼 메시보다 명백히 낫다:
//   (1) **바위·산호·동굴·난파선에도 얹힌다.** 해저 위 0.05m에 띄운 평면 데칼은 원리적으로
//       평면 위에만 그려진다 - 그 위에 선 바위/산호는 카우스틱이 없는 채로 남고, 데칼은
//       오히려 바위에 가려진다. 과제 요구는 "지형·바위·산호 위"라 평면으로는 절반만 만족한다.
//   (2) **섬 해안 경사면(수몰 지형)에도 얹힌다.** 잠수의 대부분은 섬 테두리 얕은 물인데
//       그 지면은 SeabedGenerator의 스커트가 아니라 섬 메시(Island_) 본체이고,
//       TrySampleSeabed는 스커트 범위만 답한다 - 평면 데칼은 정작 가장 많이 보는 자리에
//       높이를 못 맞춘다. 깊이 재투영은 "화면에 그려진 무엇이든" 대상으로 삼으므로 무관하다.
//   (3) **비용이 진짜로 상수다.** 드로우콜 1 · 정점 4 · 정점당 지형 높이 샘플 0.
//       40×40m 격자 데칼(예: 2m 간격이면 정점 441)은 플레이어가 움직일 때마다 높이를
//       다시 샘플링해 메시를 갱신해야 하지만, 이쪽은 갱신할 메시 자체가 없다.
// 대가(의도된 트레이드오프): 프래그먼트가 화면 픽셀 수만큼 돈다. 다만 텍스처 2회 샘플 +
// 산술 수십 개짜리 아주 싼 셰이더이고, 수중일 때만 렌더러가 켜지므로 물 밖 비용은 0이다.
//
// ── 깊이 텍스처 계약과 우아한 열화 ──────────────────────────────────────────────
// _CameraDepthTexture는 MGOcean v3가 이미 쓰고 있고(URP 에셋 m_RequireDepthTexture: 1 실측),
// 같은 include(DeclareDepthTexture.hlsl) · 같은 샘플 방식(_ScaledScreenParams로 만든 screenUV)
// 을 그대로 복사했다. 만약 에셋 설정이 꺼져 깊이가 기본값으로 잡히면 아이 깊이가 근평면
// 근처(<0.5m)나 원평면으로 나오는데, 아래 게이트 두 개가 그 경우를 전부 0으로 떨어뜨린다 -
// 카우스틱이 사라질 뿐 화면이 망가지지 않는다(MGOcean과 같은 열화 계약).
//
// ── 그 밖의 설계 계약 ──────────────────────────────────────────────────────────
//  * 가산 합성(Blend One One, ZWrite Off). 알파 0의 곱셈 항이 없으므로 어두운 곳은 그대로 두고
//    빛무늬만 얹는다 - 기존 지형/바위/산호 머티리얼은 한 줄도 바뀌지 않는다.
//  * ZTest Always + 정점에서 클립 좌표 직접 출력(오브젝트 변환 무시) = 화면 전체 쿼드.
//    깊이 비교는 프래그먼트가 직접 읽은 씬 깊이로 하므로 하드웨어 ZTest는 쓸 일이 없다.
//  * Queue "Transparent-10"(2990) - 불투명 다음, 바다 수면(MGOcean, 3000)보다 먼저 그린다.
//    수면을 올려다볼 때 카우스틱 위로 수면 알파가 얹히는 순서가 맞다.
//  * 애니메이션: 카우스틱 텍스처 한 장을 배율·속도·방향이 다르게 두 번 흘려 min()으로 합친다.
//    프레임 시퀀스(수십 MB)를 만들지 않고도 계속 변형되는 그물망이 나오는 표준 트릭이다.
//    min은 더하기와 달리 밝기가 누적되지 않아 필라멘트가 가늘게 유지된다.
//  * 시간은 셰이더 내장 _Time이 아니라 C#(UnderwaterVisuals)이 매 프레임 넣는 _MG_CausticsTime.
//    Time.timeScale = 0에서 멈추는 프로젝트 관례(MGOcean _MG_WaveTime과 동일).
//  * 수심 감쇠: 빛무늬가 닿은 **지면의 수심**으로 감쇠한다(카메라 수심이 아니다) - 얕은 모래는
//    밝고 깊은 골은 어둡다. 해수면 위 지면(수심 < 0)은 완전히 0이라 물 밖으로 새지 않는다.
//    카메라가 물 밖일 때는 C#이 렌더러 자체를 끄므로 이중으로 막힌다.
//  * CBUFFER(UnityPerMaterial)에 Properties 스칼라/색 전부 - SRP Batcher 호환(MGOcean과 동일).
//  * ShadowCaster/DepthOnly 패스 없음 - 가산 오버레이가 그림자를 드리울 이유가 없다.
Shader "MG/Caustics"
{
    Properties
    {
        [MainTexture] _CausticsMap("카우스틱 그물망 텍스처(caustics.png, 무이음)", 2D) = "black" {}
        _CausticsColor("빛무늬 색(수중이라 청록 기조)", Color) = (0.55, 0.92, 0.85, 1)
        _Intensity("가산 세기", Range(0.0, 3.0)) = 0.85
        _TileSize("월드 타일 크기(m, 텍스처 1장이 덮는 폭)", Float) = 7.0
        _ScrollSpeed("흐름 속도 배율(1 = 기본)", Range(0.0, 3.0)) = 1.0
        _SeaLevel("해수면 y(C#이 WorldMapManager.seaLevel을 넣는다)", Float) = 0.0
        _FadeDepth("빛무늬가 사라지는 지면 수심(m)", Float) = 26.0
        _MaxDistance("카우스틱을 그리는 최대 거리(m)", Float) = 45.0
        _SunFactor("햇빛 계수(C#이 낮=1, 밤=0으로 넣는다)", Range(0.0, 1.0)) = 1.0
        _MG_CausticsTime("카우스틱 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // 불투명 다음, 바다 수면(3000)보다 먼저. 파티클(마린 스노우, 3000)보다도 먼저다.
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CausticsOverlay"
            Tags { "LightMode" = "UniversalForward" }

            // 가산 합성 - 어두운 곳은 그대로 두고 빛만 더한다.
            Blend One One
            ZWrite Off
            // 화면 전체 쿼드라 하드웨어 깊이 비교는 무의미하다(직접 읽은 씬 깊이로 판단한다).
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 씬 깊이(_CameraDepthTexture) 샘플러 - MGOcean v3가 쓰는 것과 같은 include다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색은 전부 UnityPerMaterial 안에 둔다
            // (텍스처 오브젝트 자체는 CBUFFER 밖이 규칙이다).
            CBUFFER_START(UnityPerMaterial)
                float4 _CausticsMap_ST;
                half4 _CausticsColor;
                half _Intensity;
                float _TileSize;
                half _ScrollSpeed;
                float _SeaLevel;
                float _FadeDepth;
                float _MaxDistance;
                half _SunFactor;
                float _MG_CausticsTime;
            CBUFFER_END

            TEXTURE2D(_CausticsMap);
            SAMPLER(sampler_CausticsMap);

            // 두 겹의 흐름: 배율(1.0 / 0.68)과 방향/속도가 서로 소인수가 아니게 어긋나 있어
            // 겹친 무늬가 눈에 띄는 주기로 되풀이되지 않는다.
            #define MG_CAU_SCALE2 0.68
            #define MG_CAU_DRIFT1 float2( 0.031, 0.019)
            #define MG_CAU_DRIFT2 float2(-0.017, 0.027)

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // 오브젝트 변환을 **의도적으로 무시**하고 클립 좌표를 직접 낸다. 쿼드 메시의 로컬
            // 좌표가 [-0.5, 0.5]²라 ×2가 곧 NDC [-1,1]² - 카메라 FOV/자세와 무관하게 언제나
            // 화면을 정확히 덮는다(프러스텀 컬링은 C#이 메시 바운즈를 크게 잡아 막는다).
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = float4(IN.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 화면 UV: SV_POSITION의 픽셀 좌표를 화면 크기로 나눈다(MGOcean과 같은 방식).
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;

                float rawDepth = SampleSceneDepth(screenUV);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // 게이트 1: 근평면 코앞(깊이 텍스처가 꺼져 기본값이 잡힌 경우 포함)과 원거리는 버린다.
                // **조기 return이 아니라 곱셈 게이트**인 이유: 아래에서 ddx/ddy를 쓰는데, 화면
                // 2x2 쿼드 안에서 일부 픽셀만 먼저 빠져나가면 남은 픽셀의 미분이 정의되지 않는다
                // (HLSL 규칙). 전 픽셀이 같은 경로를 돌게 두고 마지막에 0을 곱한다.
                float validGate = step(0.5, eyeDepth) * step(eyeDepth, _MaxDistance);

                // 씬 깊이 → 월드 위치 재투영. URP 표준 경로(코어 Common.hlsl)이고,
                // screenUV와 rawDepth가 같은 규약이라 플랫폼별 Y 뒤집힘이 서로 상쇄된다.
                float3 positionWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                // NaN/inf 소독: 깊이 텍스처가 없거나 원평면 픽셀이면 재투영이 발산할 수 있고,
                // 그 좌표로 만든 UV/미분은 NaN이 되어 가산 합성에서 흰 점으로 남는다.
                // clamp(=min(max(...)))는 대부분의 GPU에서 NaN을 두 번째 피연산자로 씻어 낸다.
                positionWS = clamp(positionWS, -100000.0, 100000.0);

                // 게이트 2: 빛무늬가 닿은 지면의 수심. 해수면 위 지면에는 절대 그리지 않는다.
                float groundDepth = _SeaLevel - positionWS.y;
                validGate *= step(0.0001, groundDepth);

                // 수심 감쇠 - 얕을수록 밝다. 제곱으로 떨어뜨려 얕은 구간을 넓게 남긴다.
                float depthFade = saturate(1.0 - groundDepth / max(_FadeDepth, 0.001));
                depthFade *= depthFade;

                // ---- 그물망: 같은 텍스처를 배율/방향이 다르게 두 번 흘려 min()으로 합친다. ----
                float t = _MG_CausticsTime * _ScrollSpeed;
                float invTile = 1.0 / max(_TileSize, 0.001);
                float2 uv1 = positionWS.xz * invTile + MG_CAU_DRIFT1 * t;
                float2 uv2 = positionWS.xz * (invTile * MG_CAU_SCALE2) + MG_CAU_DRIFT2 * t;
                half c1 = SAMPLE_TEXTURE2D(_CausticsMap, sampler_CausticsMap, uv1).g;
                half c2 = SAMPLE_TEXTURE2D(_CausticsMap, sampler_CausticsMap, uv2).g;
                // min은 두 그물이 **겹친 자리만** 남긴다 - 무늬가 계속 생겼다 사라지며 흐른다.
                // 곱(c1*c2)은 너무 어둡고 합(c1+c2)은 밝기가 누적돼 필라멘트가 뭉개진다.
                half web = min(c1, c2);
                // 겹침만 남기면 전체가 어두워지므로 배율로 되살린다(텍스처 최대치가 1로 정규화돼 있다).
                web = saturate(web * 1.8);

                // ---- 표면 기울기: 화면 미분으로 월드 노멀을 복원해 수직 벽을 죽인다. ----
                // 카우스틱은 위에서 내려오는 빛이라 수평면에 가장 진하고 절벽 면에는 거의 없다.
                // abs()를 쓰는 이유: cross의 부호가 플랫폼 Y 방향에 따라 뒤집힐 수 있어
                // 위/아래 구분을 신뢰하지 않는다(동굴 천장에 약간 얹히는 것은 감수한다).
                float3 ddxWS = ddx(positionWS);
                float3 ddyWS = ddy(positionWS);
                float3 faceCross = cross(ddyWS, ddxWS);
                float crossLen = length(faceCross);
                // 퇴화(두 미분이 평행 → 외적 0)면 normalize가 NaN이 된다. 가산 합성에서 NaN 한 픽셀은
                // 흰 점으로 남으므로 길이를 직접 검사해 0으로 떨어뜨린다.
                half flatness = crossLen > 1e-8 ? (half)saturate(abs(faceCross.y) / crossLen) : (half)0.0;
                flatness *= flatness;

                // ---- 실루엣 보호: 깊이가 급변하는 픽셀(물체 가장자리)은 미분이 폭발해
                //      노멀/월드 위치가 무의미해진다. 미분 크기가 거리 대비 과하면 통째로 끈다. ----
                float derivMag = length(ddxWS) + length(ddyWS);
                half edgeFade = (half)saturate(1.0 - derivMag / (eyeDepth * 0.05 + 0.05));

                // 거리 페이드: 먼 곳의 가산 빛이 수중 안개를 뚫고 빛나 보이는 것을 막는다.
                half distFade = (half)smoothstep(_MaxDistance, _MaxDistance * 0.35, eyeDepth);

                half strength = web * _Intensity * (half)depthFade * flatness * edgeFade * distFade
                    * _SunFactor * (half)validGate;
                half3 color = _CausticsColor.rgb * strength;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
