// MGGodRay - 수면에서 내려오는 빛줄기(god ray / 크레퍼스큘러 샤프트) 전용 URP 셰이더.
//
// UnderwaterVisuals가 Resources.Load<Shader>("Shaders/MGGodRay")로 로드해 빛기둥 메시에 쓰고,
// 로드가 실패하면 갓레이 전체를 조용히 생략한다(게임은 이 셰이더 없이도 돌아가야 한다 -
// MGOcean/MGKelpSway/MGGrass/MGCaustics와 같은 폴백 계약).
//
// ── 기하 계약(UnderwaterVisuals.BuildGodRayMesh와 한 쌍) ──────────────────────────
// 빛기둥 N개(기본 7)를 **메시 한 장**에 굽고 셰이더가 정점에서 빌보드를 푼다. 기둥마다
// GameObject를 두면 드로우콜이 N개가 되지만, 이 방식은 어떤 각도에서도 드로우콜 1이다.
//   · positionOS = 그 기둥의 **중심 오프셋**(xz, y = 0). 쿼드 4정점이 전부 같은 값을 갖는다.
//   · uv0.x = 0/1 (기둥 좌/우 모서리), uv0.y = 0(수면 쪽 위) / 1(아래 끝).
//   · uv1 = (반폭 m, 길이 m), uv2 = (개별 밝기 0~1, 위상 0~1).
//     정점 색(COLOR)이 아니라 UV 채널을 쓰는 이유: 메시 정점 색의 기본 저장 포맷은 Color32
//     (8비트 UNorm)라 반폭·길이 같은 **미터 단위 값이 0~1로 잘린다**. UV 채널은 float32라 안전하다.
// 메시 루트(오브젝트 원점)는 C#이 매 프레임 플레이어 XZ · 해수면 y에 놓는다. 즉 기둥의 위
// 끝은 항상 수면에 닿아 있고, 아래로 uv1.y(길이)만큼 뻗는다.
//
// ── 시각 계약 ────────────────────────────────────────────────────────────────
//  * 가산 합성(Blend One One) + ZWrite Off. "은은하게"가 요구라 기본 _Intensity는 0.30이고
//    아래 페이드 여섯 겹을 전부 통과한 픽셀만 그 세기에 도달한다.
//  * 축은 수직이 아니라 **태양 방위로 살짝 기운 하강 방향**이다(_SunTilt). 물속에서는 굴절
//    (스넬의 창) 때문에 태양이 낮게 떠 있어도 빛줄기가 거의 수직으로 내려오는 것이 실제 모습이라,
//    태양 방향을 그대로 쓰지 않고 0.25배만 섞는다.
//  * 페이드: (1) 가로 sin 프로파일로 양 모서리 소멸, (2) 아래로 갈수록 제곱으로 소멸,
//    (3) 수면 접합부 미세 페이드(수면과의 하드 에지 제거), (4) 시선-태양 각도
//    (태양 쪽을 볼 때 밝다 - 실제 산란의 전방 우세), (5) 거리(너무 가까우면 화면을 덮으므로
//    죽이고, 멀면 안개에 묻히게 죽인다), (6) 씬 깊이 소프트 페이드(해저와 만나는 자리의
//    칼선 제거). 여기에 C#이 넣는 _MG_GodRayStrength(태양 강도 × 카메라 수심)가 곱해진다.
//  * 월드 위치 기반 밝기 얼룩: 기둥이 플레이어를 따라다니므로(로컬 볼륨) 그대로 두면 "빛기둥이
//    나에게 붙어 있는" 느낌이 난다. 기둥 중심의 **월드 XZ**로 밝기를 변조해, 헤엄쳐 나아가면
//    빛기둥이 밝아졌다 어두워지며 지나가는 시차감을 준다(비용 = 사인 2회/정점).
//  * 시간은 셰이더 내장 _Time이 아니라 C#이 매 프레임 넣는 _MG_GodRayTime(Time.time)이다 -
//    타이틀 화면(timeScale = 0) 정지 관례(MGOcean _MG_WaveTime과 동일).
//  * ZTest LEqual - 빛기둥이 해저/바위를 뚫고 보이면 안 된다(가산이라 더 눈에 띈다).
//  * Queue "Transparent-5"(2995) - 카우스틱(2990) 다음, 바다 수면(3000)보다 먼저.
//  * CBUFFER(UnityPerMaterial)에 Properties 스칼라/색/벡터 전부 - SRP Batcher 호환.
//  * ShadowCaster/DepthOnly 패스 없음 - 반투명 가산 장식이 그림자/깊이에 낄 이유가 없다.
Shader "MG/GodRay"
{
    Properties
    {
        _RayColor("빛기둥 색(수중 산란이라 청백)", Color) = (0.62, 0.88, 0.92, 1)
        _Intensity("가산 세기(은은함이 요구 - 기본 0.30)", Range(0.0, 2.0)) = 0.30
        _SunTilt("태양 방위로 기우는 정도(0 = 완전 수직)", Range(0.0, 1.0)) = 0.25
        _MaxDistance("빛기둥을 그리는 최대 거리(m)", Float) = 55.0
        _NearFade("이 거리보다 가까우면 사라진다(m, 화면 덮기 방지)", Float) = 6.0
        _SoftDistance("해저와 만나는 자리의 소프트 페이드 거리(m)", Float) = 3.0
        _MG_SunDir("태양 진행 방향(C#이 Light.transform.forward를 넣는다)", Vector) = (0, -1, 0, 0)
        _MG_GodRayStrength("전체 세기(C#이 태양 강도 × 수심으로 넣는다. 밤 = 0)", Range(0.0, 1.0)) = 1.0
        _MG_GodRayTime("빛기둥 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GodRayShaft"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            // 빌보드라 정면만 보이지만, 카메라가 기둥 안으로 들어가는 순간의 뒷면도 그려야
            // 갑자기 사라지지 않는다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 해저와 만나는 자리의 소프트 페이드용 씬 깊이 - MGOcean/MGCaustics와 같은 include다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색/벡터는 전부 UnityPerMaterial 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half4 _RayColor;
                half _Intensity;
                half _SunTilt;
                float _MaxDistance;
                float _NearFade;
                float _SoftDistance;
                float4 _MG_SunDir;
                half _MG_GodRayStrength;
                float _MG_GodRayTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // xz = 기둥 중심 오프셋, y = 0
                float2 uv         : TEXCOORD0;  // x = 좌/우(0/1), y = 위/아래(0/1)
                float2 shaftSize  : TEXCOORD1;  // x = 반폭(m), y = 길이(m)
                float2 shaftVar   : TEXCOORD2;  // x = 개별 밝기(0~1), y = 위상(0~1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // x = 가로 0~1, y = 세로 0(위)~1(아래), z = 정점에서 합친 밝기, w = 위상
                float4 uvData     : TEXCOORD0;
                // 소프트 페이드용 이 픽셀의 아이 깊이(원근 보정 보간이라 월드 선형이 유지된다).
                float  eyeDepth   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float halfWidth = IN.shaftSize.x;
                float shaftLen = IN.shaftSize.y;

                // 기둥 중심(월드). 루트 y가 해수면이므로 이 점이 곧 수면 위의 기둥 머리다.
                float3 centerWS = TransformObjectToWorld(float3(IN.positionOS.x, 0.0, IN.positionOS.z));

                // 하강 축: 완전 수직에 태양 방위를 _SunTilt만큼만 섞는다(굴절 근사 - 헤더 주석).
                float3 axis = normalize(float3(_MG_SunDir.x * _SunTilt, -1.0,
                                               _MG_SunDir.z * _SunTilt));

                // 빌보드: 축에 수직이면서 카메라를 향하는 평면의 가로 방향을 구한다.
                float3 camPos = GetCameraPositionWS();
                float3 toCam = camPos - centerWS;
                float3 perp = toCam - axis * dot(toCam, axis);
                float perpLen = length(perp);
                // 카메라가 축과 정확히 일직선(기둥을 바로 위/아래에서 내려다봄)이면 가로 방향이
                // 정의되지 않는다(0으로 나눠 NaN). 그 경우는 폭이 화면상 0이라 어차피 안 보이므로
                // 임의의 축으로 대체한다 - 나눗셈 자체를 피해야 NaN이 아예 생기지 않는다.
                float3 perpDir = perpLen > 1e-4 ? perp / perpLen : float3(0.0, 0.0, 1.0);
                float3 right = normalize(cross(axis, perpDir));

                float3 positionWS = centerWS
                    + axis * (IN.uv.y * shaftLen)
                    + right * ((IN.uv.x - 0.5) * 2.0 * halfWidth);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;

                // ---- 정점에서 끝나는 페이드들(프래그먼트를 가볍게 유지한다) ----
                // (4) 시선-태양 각도: 태양 쪽(= 빛 진행의 반대 = 물속에서는 위쪽)을 **바라볼 때**
                //     밝다. 카메라 전방 벡터를 쓴다 - 기둥 중심으로 향하는 벡터를 쓰면 깊이 잠수한
                //     순간 모든 기둥이 위쪽에 있어 각도 항이 항상 1이 되어 페이드가 죽는다.
                //     뷰 행렬의 3행이 카메라 -전방이므로 부호를 뒤집으면 전방이다(URP
                //     GetViewForwardDir()과 같은 식 - 함수 대신 식을 직접 써서 의존을 줄인다).
                float3 camForward = -UNITY_MATRIX_V._m20_m21_m22;
                // 바닥값 0.30은 "등지고 있어도 완전히 사라지지는 않는다"는 최소치다.
                float facing = 0.30 + 0.70 * saturate(dot(camForward, -_MG_SunDir.xyz));

                // (5) 거리: 너무 가까우면(화면을 덮는다) 죽이고, 멀면 안개에 묻히게 죽인다.
                float dist = distance(centerWS, camPos);
                float nearFade = smoothstep(_NearFade * 0.35, _NearFade, dist);
                float farFade = smoothstep(_MaxDistance, _MaxDistance * 0.45, dist);

                // 월드 XZ 밝기 얼룩 - 헤엄쳐 나아가면 빛기둥이 밝아졌다 어두워진다(시차감).
                float worldMod = 0.55 + 0.45 * sin(centerWS.x * 0.071 + 1.3)
                                            * sin(centerWS.z * 0.059 - 0.7);

                OUT.uvData = float4(IN.uv.x, IN.uv.y,
                    IN.shaftVar.x * facing * nearFade * farFade * worldMod, IN.shaftVar.y);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float phase = IN.uvData.w * 6.2832;
                float t = _MG_GodRayTime;

                // (1) 가로 프로파일: 양 모서리에서 부드럽게 0. 시간에 따라 살짝 휘어 흔들린다.
                float wobble = 0.055 * sin(t * 0.45 + phase + IN.uvData.y * 2.7);
                float u = saturate(IN.uvData.x + wobble);
                float across = sin(u * PI);
                across *= across;

                // (2) 세로: 위(수면) 밝고 아래로 제곱 소멸. (3) 수면 접합부 미세 페이드.
                float down = saturate(1.0 - IN.uvData.y);
                float vertical = down * down * smoothstep(0.0, 0.08, IN.uvData.y);

                // 느린 맥동 - 수면 파도가 빛을 모았다 흩는 리듬.
                float pulse = 0.72 + 0.28 * sin(t * 0.55 + phase * 1.7);

                // (6) 소프트 페이드: 해저/바위와 만나는 자리의 칼선을 없앤다. 씬 깊이가 없으면
                //     아이 깊이 차가 크게 잡혀 1로 수렴하므로 그냥 하드 에지로 열화될 뿐이다.
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneEye - IN.eyeDepth) / max(_SoftDistance, 0.001));

                half strength = (half)(across * vertical * pulse * soft * IN.uvData.z)
                    * _Intensity * _MG_GodRayStrength;

                return half4(_RayColor.rgb * strength, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
