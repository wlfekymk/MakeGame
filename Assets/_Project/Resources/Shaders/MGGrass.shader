// MGGrass v2 - Graphics.RenderMeshInstanced로 그리는 인스턴싱 잔디 전용 URP 셰이더.
//
// GrassFieldSystem이 Resources.Load<Shader>("Shaders/MGGrass")로 로드해 잔디/꽃 머티리얼에 쓰고,
// 로드가 실패하면 시스템 쪽에서 잔디 전체 생략으로 폴백한다(게임은 이 셰이더 없이도 돌아가야
// 한다 - MGOcean/MGKelpSway와 같은 폴백 계약).
//
// v2 변경(알파 컷아웃 카드 텍스처):
//  * _BaseMap(grass_card, 2×2 아틀라스) 샘플 + clip(alpha - 문턱) 컷아웃. 카드 한 장에 풀잎
//    수십 가닥이 그려져 있어 같은 인스턴스 수로 밀도감이 크게 오른다. 텍스처를 안 꽂으면
//    기본 "white"라 알파 1(클립 통과)·RGB 1이 되어 v1의 틴트 그라데이션 렌더로 자연 폴백한다.
//  * 아틀라스 셀 선택: 정점에서 인스턴스 월드 원점 해시로 잔디 3셀 중 택1
//    (0=(0,0) 촘촘한 초록 / 1=(1,0) 성긴+이삭 / 2=(0,1) 마른 풀 - UV를 0.5 스케일+오프셋).
//    _CellOverride(-1=해시 선택, 0~3=고정)로 꽃 머티리얼이 3=(1,1) 분홍 꽃 스파이크를 고정한다.
//  * 색 = 텍스처 색 × 틴트. 기존 _RootColor→_TipColor 그라데이션(+마른 풀/명도 지터)은
//    _TintStrength(0~1)로 흰색과 보간한 "틴트 승수"로 완화 적용한다 - 텍스처가 이미 뿌리→끝
//    색을 갖고 있으니 틴트는 톤 조절용이다. 꽃 머티리얼은 _TintStrength 0으로 틴트를 끈다.
//  * 원거리 밉에서 알파가 뭉개져 컷아웃이 사라지는 문제 완화: 카메라 거리로 clip 문턱을
//    0.5(40m 이내)→0.3(150m 밖)으로 보간한다(정점 1회 계산, 프래그먼트는 clip 한 번).
//
// v3 추가(음영 - 아틀라스 v3 텍스처와 한 쌍):
//  * 스페큘러 시트: 메인 라이트 블린-퐁 pow(N·H, 24) × UV.y 가중 × _SheenStrength(0.35) -
//    풀끝이 햇빛 각도에서 띠 모양으로 반짝인다.
//  * 투과(백라이트): saturate(-V·L)² × UV.y 가중 × _TranslucencyStrength(0.4) × 알베도 ×
//    라이트 색 가산 - 역광에서 풀끝이 비쳐 보인다.
//  * 인스턴스 색상(hue) 변주: 원점 해시 h(±6%)로 R×(1+h)·B×(1-h) 채널 승수(노랑↔청록 근사
//    회전, G 고정) - 기존 명도 지터에 더해 균일한 초록 카펫 느낌을 부순다.
//  * 원거리 페이드: clip 문턱을 150→300m에서 +1.0까지 올려 알파 높은 텍셀부터 차례로
//    사라진다 - GrassFieldSystem의 300m 하드 컷과 이어져 팝이 없다.
//
// 유지되는 설계 계약(GrassFieldSystem 쪽과 맞물린다):
//  * 카드 메시: 교차 쿼드 2장(피벗 밑동, UV.y = 0(뿌리)~1(끝)). 높이 변주는 인스턴스 행렬
//    스케일로 들어온다 - 셰이더는 UV.y 가중만 쓰므로 스케일과 무관하게 "뿌리 고정, 끝이 크게"
//    굽는 형태가 유지된다(MGKelpSway의 y² 가중과 같은 발상, 기준만 UV).
//  * 인스턴싱: multi_compile_instancing + UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_SETUP_INSTANCE_ID.
//    RenderMeshInstanced 경로에서 UNITY_MATRIX_M이 인스턴스별 행렬로 풀린다(URP 17).
//    퍼 인스턴스 커스텀 데이터는 받지 않는다 - 셀/색 지터도 인스턴스 행렬의 월드 원점 해시뿐.
//  * 정점 바람: UV.y² 가중(뿌리 고정) × 월드 XZ 기반 스크롤 사인 3겹(파장 18m + 4m + 교차
//    플러터). 위상은 카드 원점(피벗) 기준 - 한 포기는 통째로 굽는다(MGKelpSway의 찢김 방지).
//    진폭 끝 기준 합계 ~0.12m × _WindStrength. 콜라이더 없는 순수 장식이라 판정과 안 어긋난다.
//  * 밟힘: 원점과 _MG_PlayerPos의 수평 거리 < _TrampleRadius면 끝을 플레이어 반대 방향으로
//    밀고 눕힌다(부드러운 falloff, UV.y 가중). 눌린 만큼 바람은 죽인다.
//  * 시간은 셰이더 내장 _Time이 아니라 C#이 매 프레임 넣는 _MG_WindTime(Time.time)이다 -
//    Time.timeScale = 0에서 잔디가 정지하는 프로젝트 관례(MGOcean _MG_WaveTime과 동일).
//    _MG_PlayerPos도 C#이 매 프레임 넣는다(기본값은 지하 -10000이라 주입 전엔 밟힘이 없다).
//  * 라이팅: 메인 라이트 램버트 + SH 앰비언트 + URP 포그. Cull Off 양면 - 뒷면은
//    SV_IsFrontFace로 노멀을 뒤집고, 잔디 노멀은 위쪽으로 절반 굽혀 카드 명암 끊김을 편다.
//  * ShadowCaster/DepthOnly 패스는 넣지 않는다(수만 포기 그림자 캐스팅은 비용만 크다 -
//    MGOcean의 "패스 생략 - 우아한 열화"와 같은 선택).
//  * CBUFFER(UnityPerMaterial)에 Properties 스칼라/색/벡터 전부 - SRP Batcher 호환
//    (텍스처 오브젝트 자체는 CBUFFER 밖이 규칙이다).
Shader "MG/Grass"
{
    Properties
    {
        _BaseMap("잔디 카드 텍스처(2×2 아틀라스, 알파 컷아웃)", 2D) = "white" {}
        _RootColor("뿌리 틴트(어두운 초록)", Color) = (0.09, 0.22, 0.06, 1)
        _TipColor("끝 틴트(밝은 초록)", Color) = (0.38, 0.62, 0.18, 1)
        _DryTint("마른 풀 틴트(지터 혼합 목표)", Color) = (0.55, 0.52, 0.26, 1)
        _TintStrength("틴트 세기(0 = 텍스처 원색, 1 = 풀 틴트)", Range(0.0, 1.0)) = 0.65
        _SheenStrength("스페큘러 시트 세기(풀끝 블린-퐁 하이라이트)", Range(0.0, 1.0)) = 0.35
        _TranslucencyStrength("투과(백라이트) 세기(역광 비침)", Range(0.0, 1.0)) = 0.4
        _CellOverride("아틀라스 셀(-1 = 원점 해시로 잔디 3셀 택1, 0~3 = 고정)", Float) = -1.0
        _WindStrength("바람 세기(1 = 끝 진폭 0.12m)", Range(0.0, 2.0)) = 1.0
        _TrampleRadius("밟힘 반경(m)", Float) = 1.2
        _MG_WindTime("바람 시간(C#이 매 프레임 Time.time을 넣는다)", Float) = 0.0
        _MG_PlayerPos("플레이어 월드 위치(C#이 매 프레임 넣는다)", Vector) = (0, -10000, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 얇은 카드의 뒷면도 그린다(잔디밭은 어느 방향에서든 보인다).
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            // RenderMeshInstanced 경로 필수 - 인스턴싱 배리언트가 없으면 전 포기가 원점에 겹친다.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher 호환: Properties의 스칼라/색/벡터는 전부 UnityPerMaterial 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half4 _RootColor;
                half4 _TipColor;
                half4 _DryTint;
                half _TintStrength;
                half _SheenStrength;
                half _TranslucencyStrength;
                float _CellOverride;
                half _WindStrength;
                float _TrampleRadius;
                float _MG_WindTime;
                float4 _MG_PlayerPos;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // ---- 바람장: 월드 XZ를 지나가는 스크롤 사인 3겹. 진폭 배분 합 1 → 끝 기준
            //      합계 진폭이 곧 MG_WIND_AMP(0.12m) × _WindStrength가 된다. ----
            // 1겹: 파장 18m의 큰 물결(k = 2π/18). 전파 속도 ~3.5m/s → ω = k·c ≈ 1.22rad/s.
            // 2겹: 파장 4m의 잔떨림(k = 2π/4). 전파 속도 ~2m/s → ω ≈ 3.14rad/s.
            // 3겹: 1겹과 직교 방향의 약한 플러터 - 한 방향 사인만의 "줄 맞춰 흔들림"을 부순다.
            #define MG_WIND_AMP  0.12
            #define MG_WIND_DIR1 float2( 0.848,  0.530)
            #define MG_WIND_DIR2 float2( 0.940,  0.342)
            #define MG_WIND_DIR3 float2(-0.530,  0.848)
            #define MG_WIND_K1 0.349
            #define MG_WIND_K2 1.571
            #define MG_WIND_K3 0.897
            #define MG_WIND_W1 1.22
            #define MG_WIND_W2 3.14
            #define MG_WIND_W3 2.05
            #define MG_WIND_A1 0.55
            #define MG_WIND_A2 0.30
            #define MG_WIND_A3 0.15

            // 원점 XZ → 0~1 해시. 퍼 인스턴스 데이터 없이 셀/색 지터를 만드는 표준 정점 해시.
            float MGHash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // xy = 아틀라스 UV(0.5 스케일 + 셀 오프셋), z = 원래 UV.y(틴트 그라데이션용),
                // w = 거리 보간된 알파 clip 문턱(0.5→0.3).
                float4 uvData     : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                // 지터(마른 풀 혼합 + 명도) 적용이 끝난 뿌리/끝 틴트 - 프래그먼트는 lerp만 한다.
                half3 rootCol     : TEXCOORD2;
                half3 tipCol      : TEXCOORD3;
                float fogFactor   : TEXCOORD4;
                // 스페큘러 시트/투과의 시선 벡터 계산용 - 프래그먼트에서 카메라와 뺄셈 1회.
                float3 positionWS : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 인스턴스 행렬 셋업 - 이 아래의 UNITY_MATRIX_M/TransformObjectToWorld가
                // RenderMeshInstanced의 인스턴스별 행렬로 풀린다.
                UNITY_SETUP_INSTANCE_ID(IN);

                // 카드 원점(밑동) 월드 위치 - 모델 계약상 피벗이 밑동이다. 위상/밟힘/지터의 기준.
                float3 originWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // UV.y² 가중: 뿌리(0) 고정, 끝(1) 최대. 인스턴스 스케일 높이 변주와 무관하게 성립.
                float tipW = IN.uv.y * IN.uv.y;

                // ---- 밟힘: 원점-플레이어 수평 거리 기반. 카드 단위로 통째로 눕는다. ----
                float2 toPlayer = originWS.xz - _MG_PlayerPos.xz;
                float dist = length(toPlayer);
                // 부드러운 falloff: 반경 밖 0 → 플레이어 바로 밑 1 (smoothstep이라 경계 링이 없다).
                float press = 1.0 - smoothstep(0.0, max(_TrampleRadius, 0.001), dist);
                // 플레이어 반대 방향(수평). 바로 밑(dist≈0)에서도 0 나눗셈 없이 해시 방향으로 도망간다.
                float hDir = MGHash(originWS.xz + 17.31) * 6.2832;
                float2 pushDir = dist > 0.05 ? toPlayer / dist : float2(cos(hDir), sin(hDir));

                // ---- 바람: 스크롤 사인 3겹 합성(위상은 원점 기준 - 한 포기는 통째로 굽는다). ----
                float t = _MG_WindTime;
                float2 wind =
                      MG_WIND_DIR1 * (MG_WIND_A1 * sin(dot(originWS.xz, MG_WIND_DIR1) * MG_WIND_K1 - t * MG_WIND_W1))
                    + MG_WIND_DIR2 * (MG_WIND_A2 * sin(dot(originWS.xz, MG_WIND_DIR2) * MG_WIND_K2 - t * MG_WIND_W2))
                    + MG_WIND_DIR3 * (MG_WIND_A3 * sin(dot(originWS.xz, MG_WIND_DIR3) * MG_WIND_K3 - t * MG_WIND_W3));
                // 눌린 풀은 바람이 거의 안 통한다(누운 채로 또 흔들리는 부자연스러움 방지).
                wind *= (1.0 - 0.85 * press);
                positionWS.xz += wind * (tipW * MG_WIND_AMP * _WindStrength);

                // 밟힘 변위: 끝을 바깥으로 0.45m까지 밀고, 밑동 위 높이의 최대 65%를 낮춰 눕힌다.
                // 월드 공간 변위라 인스턴스 스케일과 무관하게 "발밑에서 갈라지는 풀"이 된다.
                positionWS.xz += pushDir * (press * tipW * 0.45);
                positionWS.y -= max(positionWS.y - originWS.y, 0.0) * (press * tipW * 0.65);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;

                // ---- 아틀라스 셀 선택: _CellOverride < 0이면 원점 해시로 잔디 3셀 중 택1. ----
                // 0=(0,0) 촘촘한 초록 / 1=(1,0) 성긴+이삭 / 2=(0,1) 마른 풀 / 3=(1,1) 분홍 꽃 -
                // 꽃 셀은 해시로는 절대 안 나오고 꽃 머티리얼(_CellOverride = 3)만 고정 선택한다.
                float cell = _CellOverride;
                if (cell < -0.5)
                    cell = min(floor(MGHash(originWS.xz + 57.19) * 3.0), 2.0);
                float2 cellOffset = float2(fmod(cell, 2.0), floor(cell * 0.5)) * 0.5;
                OUT.uvData.xy = IN.uv * 0.5 + cellOffset;
                OUT.uvData.z = IN.uv.y;

                // 원거리 밉에서 알파가 뭉개져 잔디가 "녹아 사라지는" 컷아웃 고질병 완화:
                // clip 문턱을 카메라 거리 40m(0.5) → 150m(0.3)으로 완화한다. 정점 1회 계산.
                // 여기에 원거리 페이드: 150→300m에서 문턱을 다시 +1.0까지 올려(최종 1.3 > 알파 최대)
                // 알파 높은 텍셀부터 차례로 사라지며 잔디가 녹듯 빠진다 - C# 쪽 300m 하드 컷과
                // 정확히 이어져 "섬 단위로 잔디가 툭 꺼지는" 팝이 없어진다.
                float camDist = distance(originWS, GetCameraPositionWS());
                OUT.uvData.w = lerp(0.5, 0.3, saturate((camDist - 40.0) / 110.0))
                    + saturate((camDist - 150.0) / 150.0);

                // 변위가 0.12m 수준이라 노멀 재계산은 생략(MGKelpSway와 같은 판단). 프래그먼트에서
                // 위로 절반 굽히므로 카드 노멀의 정밀도는 어차피 지배적이지 않다.
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // ---- 원점 해시 틴트 지터: _DryTint 0~35% 혼합 + 명도 ±8% + 색상(hue) ±6%. ----
                // 해시 소금을 다르게 세 번 - 마른 정도/명도/색상이 서로 독립으로 흩어진다.
                // v2에서는 이 색이 알베도가 아니라 텍스처에 곱하는 "틴트 승수"다(_TintStrength로 완화).
                // 색상 변주는 정확한 HSV 회전 대신 RGB 채널 가중 승수로 근사: R×(1+h)·B×(1-h)는
                // h > 0에서 노랑 쪽, h < 0에서 청록 쪽으로 기운다(G 고정이라 초록 기조는 유지) -
                // 균일한 초록 카펫이 포기마다 미묘하게 다른 색으로 갈라진다.
                half dry = (half)(MGHash(originWS.xz) * 0.35);
                half bright = (half)(1.0 + (MGHash(originWS.xz + 41.7) * 2.0 - 1.0) * 0.08);
                half hue = (half)((MGHash(originWS.xz + 23.77) * 2.0 - 1.0) * 0.06);
                half3 jitterMul = bright * half3(1.0 + hue, 1.0, 1.0 - hue);
                OUT.rootCol = lerp(_RootColor.rgb, _DryTint.rgb, dry) * jitterMul;
                OUT.tipCol = lerp(_TipColor.rgb, _DryTint.rgb, dry) * jitterMul;

                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 카드 텍스처 샘플 + 알파 컷아웃(문턱은 정점에서 거리 보간해 내려온 값).
                // 텍스처 미지정이면 기본 white라 알파 1 - clip이 절대 걸리지 않는 v1 폴백 경로.
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uvData.xy);
                clip(tex.a - IN.uvData.w);

                // Cull Off 뒷면은 노멀을 뒤집어야 라이팅이 성립한다(MGOcean/MGKelpSway와 같은 규칙).
                float3 faceNormal = normalize(IN.normalWS);
                faceNormal = isFrontFace ? faceNormal : -faceNormal;
                // 잔디 관례: 노멀을 위쪽으로 절반 굽혀 카드 방향별 명암 대비를 죽인다 -
                // 교차 쿼드 2장이 각도에 따라 밝기가 튀는 "십자 무늬"가 밭 전체에서 사라진다.
                float3 normalWS = normalize(lerp(faceNormal, float3(0.0, 1.0, 0.0), 0.6));

                // 색 = 텍스처 색 × 틴트 승수. 틴트는 지터 적용이 끝난 뿌리/끝 색의 세로 그라데이션을
                // _TintStrength로 흰색과 보간한 것 - 0이면 텍스처 원색(꽃 머티리얼), 1이면 풀 틴트.
                half3 tint = lerp(half3(1.0, 1.0, 1.0),
                    lerp(IN.rootCol, IN.tipCol, saturate(IN.uvData.z)), _TintStrength);
                half3 albedo = tex.rgb * tint;

                // 라이팅: 메인 라이트 램버트 + SH 앰비언트(URP Lit 간이형 - 수만 포기라
                // 프래그먼트는 최대한 싸야 한다). 여기에 v3 음영 2종을 가산한다:
                //  * 스페큘러 시트: 블린-퐁 pow(N·H, 24)를 UV.y(끝 쪽) 가중으로 - 풀끝이 햇빛
                //    각도에서 띠 모양으로 반짝인다(아틀라스 v3의 잎맥 하이라이트와 합쳐진다).
                //  * 투과(백라이트): 시선이 라이트를 마주볼 때(-V·L > 0, 제곱으로 좁힘) UV.y 가중
                //    투과광을 알베도×라이트 색으로 가산 - 역광에서 풀끝이 비쳐 보인다.
                // 둘 다 UV.y 가중이라 뿌리 쪽(어두운 AO 지대)은 건드리지 않는다.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                half tipY = saturate(IN.uvData.z);

                float3 halfDir = normalize(viewDir + mainLight.direction);
                half sheen = (half)pow(saturate(dot(normalWS, halfDir)), 24.0)
                    * _SheenStrength * tipY;

                half backLit = saturate(dot(-viewDir, mainLight.direction));
                half transl = backLit * backLit * _TranslucencyStrength * tipY;

                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * ndotl)
                    + mainLight.color * sheen
                    + albedo * mainLight.color * transl;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
        // ShadowCaster/DepthOnly 패스는 넣지 않는다 - 수만 포기 그림자 캐스팅은 비용 대비 이득이
        // 없고, 패스가 없어도 잔디가 그림자/깊이에서 빠질 뿐 렌더는 망가지지 않는다(MGOcean 관례).
    }

    Fallback "Universal Render Pipeline/Lit"
}
