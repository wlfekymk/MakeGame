// MGCloudDome - 하늘 구름 돔. SkySystem이 Resources.Load<Shader>("Shaders/MGCloudDome")로 로드한다.
//
// 무엇을 그리는가:
//  * 반구 메시 위에서, **시선 방향이 구름 평면(_CloudHeight)과 만나는 지점**의 커버리지를 샘플한다.
//    메시 UV를 쓰지 않는 이유가 여기 있다 - 평면 투영이라야 구름이 지평선에서 수렴하는 원근이 생긴다.
//    돔 텍스처를 그냥 감으면 머리 위와 지평선의 구름 크기가 같아져 "천장에 붙인 벽지"가 된다.
//  * 두 겹을 쓴다. 두 번째 겹은 **더 높은 평면**에서 샘플하므로 시선이 움직일 때 첫 겹보다 천천히
//    흐른다 - 이게 시차(parallax)다. 같은 평면에서 크기만 바꿔 겹치면 시차가 생기지 않는다.
//  * 태양 쪽 구름 가장자리가 밝아지는 실버라이닝은 전방산란 근사다(Nubis가 Henyey-Greenstein으로
//    하는 일의 값싼 판). pow(시선·태양)이 그 역할을 한다.
//
// 렌더 순서: Transparent-100(2900). URP는 불투명 → 스카이박스 → 반투명 순서라, 스카이박스보다
// 뒤여야 하늘 위에 그려지고, 다른 반투명(파도 리본·비)보다는 앞이어야 그것들에 가려진다.
// ZWrite Off / ZTest LEqual - 앞의 지형이 구름을 제대로 가린다.
// Cull Off - 돔 안쪽에서 보므로 앞면만 버려도 되지만, 그러려면 메시 와인딩이 맞아야 한다.
// 반구 안쪽에서는 어느 시선이든 면을 정확히 한 번만 지나므로 양면을 그려도 이중으로 그려지지
// 않는다. 와인딩 실수 하나로 하늘이 통째로 사라지는 위험을 없애는 쪽을 택했다.
//
// 안개: 돔은 카메라에서 항상 같은 거리(900m)라 URP 포그를 그대로 쓰면 하늘 전체가 균일하게
// 안개색이 된다. 대신 **고도각**으로 안개를 섞는다 - 지평선 쪽 구름이 대기에 잠기는 그림이 맞다.
//
// 그림자와의 관계: 커버리지 텍스처는 SkySystem이 만든 것 한 장이고, 태양 라이트 쿠키도 같은
// 원본에서 나온다. 스크롤도 같은 전역(_MG_CloudScroll, 미터)을 쓴다. 다만 좌표계는 다르다
// (여긴 월드 XZ, 쿠키는 조명 공간) - 이유는 SkySystem 클래스 주석에 적어 뒀다.
Shader "MG/CloudDome"
{
    Properties
    {
        _CoverageTex("구름 커버리지(R = 두께). SkySystem이 런타임에 만들어 넣는다", 2D) = "black" {}
        _Coverage("구름 양(0~1). SkySystem이 날씨에 맞춰 매 프레임 넣는다", Range(0,1)) = 0.34
        _CloudEdge("구름 문턱값. SkySystem이 백분위 표에서 뽑아 넣는다(직접 만지지 말 것)", Range(0,1)) = 0.62
        _CloudHeight("구름층 높이(m)", Float) = 900
        _TileMeters("커버리지 한 장이 덮는 월드 크기(m)", Float) = 2600
        _CloudColor("햇빛 받는 면 색", Color) = (1.0, 0.98, 0.95, 1)
        _ShadowColor("구름 밑면 색", Color) = (0.42, 0.46, 0.55, 1)
        _Opacity("최대 불투명도", Range(0,1)) = 0.92
        _Silver("실버라이닝 세기", Range(0,3)) = 1.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CloudDome"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoverageTex_ST;
                float _Coverage;
                float _CloudEdge;
                float _CloudHeight;
                float _TileMeters;
                half4 _CloudColor;
                half4 _ShadowColor;
                float _Opacity;
                float _Silver;
            CBUFFER_END

            // 구름이 흐른 거리(m). SkySystem이 전역으로 넣는다 - 라이트 쿠키와 **같은 값**이다.
            float4 _MG_CloudScroll;

            TEXTURE2D(_CoverageTex);
            SAMPLER(sampler_CoverageTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            // 시선이 높이 h의 수평면과 만나는 지점을 커버리지 UV로 바꾼다.
            float2 PlaneUV(float3 camPos, float3 dir, float h, float tile)
            {
                float t = h / max(dir.y, 0.0001);
                float2 hit = camPos.xz + dir.xz * t - _MG_CloudScroll.xy;
                return hit / max(tile, 1.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 camPos = _WorldSpaceCameraPos;
                float3 dir = normalize(IN.positionWS - camPos);

                // 지평선 아래는 하늘이 아니다. 여기서 자르지 않으면 dir.y가 0을 지나며
                // 평면 교점이 무한대로 튀어 화면 전체에 줄무늬가 생긴다.
                if (dir.y < 0.015)
                    discard;

                // 두 겹: 아래 겹(_CloudHeight)과 위 겹(1.7배 높이 + 다른 타일 크기).
                // 높이가 다르므로 시선이 움직일 때 흐르는 속도가 달라진다 = 시차.
                float2 uv1 = PlaneUV(camPos, dir, _CloudHeight, _TileMeters);
                float2 uv2 = PlaneUV(camPos, dir, _CloudHeight * 1.7, _TileMeters * 1.9) + float2(0.37, 0.61);
                float2 uv3 = PlaneUV(camPos, dir, _CloudHeight * 0.92, _TileMeters * 0.26) + float2(0.11, 0.83);

                float c1 = SAMPLE_TEXTURE2D(_CoverageTex, sampler_CoverageTex, uv1).r;
                float c2 = SAMPLE_TEXTURE2D(_CoverageTex, sampler_CoverageTex, uv2).r;

                // ★ 구름이냐 아니냐는 **아래 겹 하나로만** 정한다. 두 겹을 섞은 값으로 문턱을 걸면
                //   분포가 좁아져(평균은 분산을 줄인다) 같은 문턱에서도 구름 면적이 확 줄고,
                //   무엇보다 SkySystem의 백분위 표(= 라이트 쿠키가 쓰는 그 표)와 어긋난다.
                float density = c1 - _CloudEdge;
                if (density <= 0.0)
                    discard;

                // ★ 문턱을 넘자마자 두께를 1로 포화시키면 구름이 **오려 붙인 종이**가 된다
                //   (첫 판이 그랬다 - 하늘이 한 톤짜리 회색 판이었다). 문턱에서 중심으로 갈수록
                //   두꺼워지는 연속 기울기를 줘야 가장자리가 얇고 속이 짙은 덩어리로 읽힌다.
                float thick = saturate(density / 0.30);

                // 위 겹(다른 높이 = 시차)으로 두께를 흔들어 단조로움을 깬다.
                thick *= lerp(0.6, 1.0, c2);

                // 잘게 흐르는 세 번째 겹으로 실루엣을 갉아낸다. Nubis가 고주파 Worley로 하는
                // detail erosion과 같은 발상의 값싼 판이고, 이게 있어야 윤곽이 매끈한 타원에서
                // 벗어난다. 두꺼운 속은 덜 갉히도록 (1-thick)을 곱한다.
                float erode = SAMPLE_TEXTURE2D(_CoverageTex, sampler_CoverageTex, uv3).r;
                thick = saturate(thick - (1.0 - erode) * 0.45 * (1.0 - thick));

                if (thick <= 0.004)
                    discard;

                // ---- 색 ----
                Light mainLight = GetMainLight();

                // 두꺼운 곳이 어둡다(빛이 못 통과한다). 얇은 가장자리는 밝게 남는다.
                half3 body = lerp(_CloudColor.rgb, _ShadowColor.rgb, saturate(thick * 1.05));

                // 실버라이닝: 태양 쪽을 볼 때 얇은 가장자리가 타오른다. 전방산란 근사다.
                half sunDot = saturate(dot(dir, mainLight.direction));
                half rim = pow(sunDot, 12.0) * (1.0 - thick) * _Silver;

                // 햇빛 색을 그대로 받는다 - 노을이면 구름이 주황이 되고 밤이면 어두워진다.
                // SH 앰비언트를 더해 그늘진 면이 새까맣게 죽지 않게 한다.
                half3 color = body * (mainLight.color + SampleSH(half3(0, 1, 0))) + rim * mainLight.color;

                // ---- 지평선 안개 ----
                // 돔은 어디서나 거리가 같아 URP 포그가 균일하게 먹는다. 고도각으로 대신 섞는다.
                half haze = 1.0 - saturate((dir.y - 0.02) / 0.30);
                color = lerp(color, unity_FogColor.rgb, haze * 0.85);

                // 알파: 두께 × 최대 불투명도 × 지평선 페이드.
                // 속은 불투명하게, 가장자리는 얇게. thick을 그대로 알파로 쓰면 구름 전체가
                // 반투명해져 하늘색이 비쳐 뿌옇게 죽는다.
                half alpha = saturate(thick * 1.7) * _Opacity * smoothstep(0.015, 0.10, dir.y);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    // 폴백 없음 - 이 셰이더가 없으면 SkySystem이 돔 자체를 만들지 않는다(구름만 사라지고
    // 구름 그림자는 라이트 쿠키라 그대로 남는다. 우아한 열화, MGOcean과 같은 선택).
}
