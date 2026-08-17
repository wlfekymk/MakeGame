using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// CreatureVisualBuilder의 곰 partial 분할 파일. 곰 규격 두 벌(BearProcedural*/BearModel*/BearCub*)·모델 로더·
    /// 곰/새끼 곰 빌더·곰 전용 머티리얼/색 파생을 CreatureVisualBuilder.cs에서 **내용 수정 없이 그대로** 옮겨 왔다
    /// (순수 이동 리팩토링 - HazardSource.BearAI.cs와 같은 방식). 공용 유틸·공유 머티리얼 캐시·
    /// 다른 생물 빌더는 CreatureVisualBuilder.cs에 남아 있다.
    /// </summary>
    public static partial class CreatureVisualBuilder
    {

        // ── [B33] 곰이 쓰는 몸통 규격 ────────────────────────────────────────────────────
        // 대왕 크랩과 같은 이유로 **public**이다 - HazardSpawner.GetVisualConfig가 숫자를 다시 적지 않고
        // 이 상수를 직접 참조해야, 메시(미터)와 프리미티브 localScale이 갈라져 곰이 조용히 늘어나거나
        // 눌리는 사고가 구조적으로 불가능해진다.
        //
        // ⚠️ 프리미티브가 **캡슐에서 큐브로 바뀌었다**(대왕 크랩과 같은 판단).
        // 감독 스펙(다리만 1.1~1.3m)을 그대로 지키면 곰은 몸길이 2.52m · 어깨 혹 1.78m의 네 발 짐승이
        // 된다. 세워 놓은 캡슐(지름 0.9 · 높이 2.2)은 그 몸의 앞뒤 2.5m 중 0.9m만 덮어서, 옆구리로
        // 지나가면 판정이 아예 없고 머리·엉덩이는 판정 밖이었다. 큐브(BoxCollider)는 축이 메시와 같고
        // (회전 0) 네 발 짐승의 넓고 긴 부피에 맞는다.
        //   x 0.86 = 가슴 폭 0.77 + 여유 · y 1.80 = 혹 꼭대기 1.78 + 여유 · z 2.56 = 코끝~엉덩이 2.52 + 여유
        //   groundOffset 0.90 = 높이의 절반 → 콜라이더 바닥이 정확히 지면이고, 메시의 발바닥 4개도
        //   같은 높이(y = -0.90)에 닿도록 작성돼 있다.
        // (털 다발·발톱·꼬리는 큐브 밖으로 조금 삐져나온다 - 대왕 크랩의 다리/집게와 같은 의도된 상태다.)
        // ※ 위 문단의 숫자는 전부 **절차 폴백 곰**의 실측값이다(아래 BearProcedural*).
        //
        // ── [B36] 실물 3D 모델(bear_adult.obj)이 들어오면서 곰 규격이 **두 벌**이 됐다 ──────────
        // 두 벌은 한 세션 안에서 절대 섞이지 않는다: 모델 에셋이 있으면 곰은 전부 모델로 만들어지고
        // (절차 메시는 한 장도 굽지 않는다), 없으면 전부 절차 메시로 만들어진다. 그래서 아래 두
        // 공개 멤버(BearBodyScale/BearGroundOffset)는 "지금 세션에서 실제로 쓰이는 쪽"을 돌려주고,
        // 스포너·추격 AI·모션이 전부 이 하나를 읽으므로 규격이 갈라지는 사고가 구조적으로 없다.
        //
        // ⚠️ 절차 쪽 두 상수를 바꾸면 **폴백 곰이 부서진다**(직접 확인함):
        //   · BearProceduralBodyScale.y - 보이는 파츠는 전부 1/nominal로 상쇄돼(MeterSpacePart /
        //     ReshapeSphere / BearPawUnit의 ScaleVertices) 겉모습이 안 변한다. 콜라이더 높이만 바뀐다.
        //   · BearProceduralGroundOffset - 이쪽은 다르다. CreatureMeshLibrary.BearGround가 이 값이고,
        //     발바닥 y(BearSoleCenterY)가 여기서 나온다. 반면 다리 하부 끝(y -0.745/-0.770)은 상수라
        //     0.90 → 0.61로 줄이면 발이 다리에서 20cm 떠오르고 정강이가 지면을 뚫는다.
        //   즉 이 두 값은 **함께만** 의미가 있고, 절차 곰(코끝~엉덩이 2.52m · 혹 1.78m)의 실측값이다.
        //   모델 곰(높이 1.219m)에 맞춘 값은 아래 BearModel* 쪽에 따로 둔다.

        /// <summary>절차 폴백 곰의 몸통 규격(m). 이 값은 절차 메시가 미터→로컬 변환에 쓰는 divisor다.</summary>
        internal static readonly Vector3 BearProceduralBodyScale = new Vector3(0.86f, 1.80f, 2.56f);

        /// <summary>절차 폴백 곰의 피벗 높이(m). CreatureMeshLibrary.BearGround가 이 값을 그대로 쓴다.</summary>
        internal const float BearProceduralGroundOffset = 0.90f;

        /// <summary>
        /// 모델 곰(bear_adult.obj)의 몸통 규격(m). 실측 폭 0.981 × 높이 1.219 × 길이 2.562.
        /// y만 1.80 → **1.22**로 낮췄다(모델 높이 1.219에 맞춘 값 - 예전 콜라이더는 몸보다 58cm 높았다).
        /// x(0.86)와 z(2.56)는 **절대 바꾸지 않는다** - 곰 추격 AI의 접촉 사거리가 이 부피를 전제로
        /// 튜닝돼 있다(HazardSource.bearAttackRange 주석의 "앞뒤 반폭 1.15~1.47m").
        /// </summary>
        private static readonly Vector3 BearModelBodyScale = new Vector3(0.86f, 1.22f, 2.56f);

        /// <summary>모델 곰의 피벗 높이(m). 큐브 높이(1.22)의 절반 = 콜라이더 바닥이 정확히 지면이고,
        /// 모델의 발바닥(로컬 y = 0)도 같은 지면에 닿는다.</summary>
        private const float BearModelGroundOffset = 0.61f;

        /// <summary>곰 몸통 프리미티브(큐브)의 localScale(m). HazardSpawner.GetVisualConfig와 같은 값이어야 한다.</summary>
        public static Vector3 BearBodyScale
        {
            get { return HasBearModel ? BearModelBodyScale : BearProceduralBodyScale; }
        }

        /// <summary>곰 피벗을 지면에서 띄우는 높이(m). 큐브 높이의 절반 = 콜라이더 바닥이 지면.</summary>
        public static float BearGroundOffset
        {
            get { return HasBearModel ? BearModelGroundOffset : BearProceduralGroundOffset; }
        }

        // ── [B36] 실물 곰 모델 ───────────────────────────────────────────────────────────
        /// <summary>곰 모델 에셋 경로(Resources 기준, 확장자 없음 - 붙이면 항상 null이 돌아온다).</summary>
        private const string BearModelResourcePath = "Models/bear_adult";

        private static GameObject bearModelPrefab;
        private static int bearModelProbeFrame = -1;
        private static Material bearModelMaterial;

        /// <summary>
        /// 곰 모델 프리팹(없으면 null). Resources.Load는 **한 번만** 부른다 - 곰이 여러 마리 스폰되고
        /// 규격 프로퍼티(BearBodyScale/BearGroundOffset)가 매 프레임 읽힐 수 있어서다.
        /// </summary>
        private static GameObject BearModelPrefab
        {
            get
            {
                // [B44] 실패를 영구히 확정하지 않는다. 생성자/필드 초기자처럼 Load가 금지된 시점에
                // 불리면 Unity가 null을 돌려주는데, 그걸 "에셋 없음"으로 굳히면 이 세션 내내
                // 절차 곰만 나온다. 성공할 때까지는 프레임당 한 번만 다시 살핀다.
                if (bearModelPrefab == null && bearModelProbeFrame != Time.frameCount)
                {
                    bearModelProbeFrame = Time.frameCount;
                    bearModelPrefab = Resources.Load<GameObject>(BearModelResourcePath);
                }
                return bearModelPrefab;
            }
        }

        /// <summary>모델 에셋이 프로젝트에 있는가. false면 곰은 예전 그대로 절차 메시로 만들어진다.</summary>
        public static bool HasBearModel
        {
            get { return BearModelPrefab != null; }
        }

        // ── [B37] 새끼 곰(bear_cub.obj) ─────────────────────────────────────────────────
        // 성체와 **같은 UV 아틀라스**를 쓴다 → 머티리얼은 BearModelMaterial() 한 장을 그대로 재사용한다
        // (MG~BearModel 캐시. 새 머티리얼을 만들면 이 세션의 곰 머티리얼이 두 장이 되어 SRP 배처가 갈린다).
        // 좌표 계약도 성체와 동일하다: 미터 · +Y 위 · +Z 정면 · 발바닥 y = 0 · X/Z 중심 정렬.
        // 실측 0.452 × 0.644 × 1.734 m / 6,999 삼각형.
        //
        // ⚠️ 아래 두 상수는 **새끼 전용**이다. 성체 규격(BearProcedural* / BearModel*)은 이번 배치에서
        //    한 글자도 건드리지 않았다 - 두 규격이 섞이면 곰이 조용히 늘어나거나 눌린다(이 파일의 상습 사고).
        /// <summary>새끼 곰 모델 에셋 경로(Resources 기준, 확장자 금지).</summary>
        private const string BearCubModelResourcePath = "Models/bear_cub";

        private static GameObject bearCubModelPrefab;
        private static int bearCubModelProbeFrame = -1;

        /// <summary>새끼 곰 모델 프리팹(없으면 null). 성체와 같은 이유로 Resources.Load는 한 번만 부른다.</summary>
        private static GameObject BearCubModelPrefab
        {
            get
            {
                // [B44] 성체와 같은 이유로 실패를 굳히지 않는다(위 BearModelPrefab 주석 참고).
                if (bearCubModelPrefab == null && bearCubModelProbeFrame != Time.frameCount)
                {
                    bearCubModelProbeFrame = Time.frameCount;
                    bearCubModelPrefab = Resources.Load<GameObject>(BearCubModelResourcePath);
                }
                return bearCubModelPrefab;
            }
        }

        /// <summary>새끼 곰 모델이 프로젝트에 있는가. false면 아래 폴백(성체를 축소)이 돈다.</summary>
        public static bool HasBearCubModel
        {
            get { return BearCubModelPrefab != null; }
        }

        /// <summary>새끼 곰 모델의 몸통 규격(m). 모델 실측(0.452 × 0.644 × 1.734)에 여유를 얹은 히트박스.</summary>
        private static readonly Vector3 BearCubModelBodyScale = new Vector3(0.45f, 0.65f, 1.73f);

        /// <summary>새끼 곰 모델의 피벗 높이(m) = 히트박스 높이의 절반 → 콜라이더 바닥 = 지면 = 발바닥.</summary>
        private const float BearCubModelGroundOffset = 0.325f;

        /// <summary>
        /// 모델이 없을 때 성체를 그대로 **균등 축소**해 새끼로 쓰는 배율. 균등이어야 하는 이유:
        /// 성체 빌더(AddBearDetails)는 파츠를 성체 규격(BearBodyScale)으로 나눠 미터 공간에 올리므로,
        /// 루트만 균등 배율로 줄이면 비율이 그대로 유지된 채 크기만 줄어든다. 축마다 다른 배율을 주면
        /// 그 순간 전단·왜곡이 생긴다. 0.58은 모델 실측 비율(높이 0.53 / 길이 0.68)의 중간값이다.
        /// </summary>
        private const float BearCubFallbackShrink = 0.58f;

        /// <summary>
        /// 새끼 곰 몸통 프리미티브(큐브)의 localScale(m). HazardSpawner가 이 값을 그대로 읽는다.
        /// 모델이 있으면 모델 실측 히트박스, 없으면 성체 규격의 균등 축소본이다.
        /// </summary>
        public static Vector3 BearCubBodyScale
        {
            get { return HasBearCubModel ? BearCubModelBodyScale : BearBodyScale * BearCubFallbackShrink; }
        }

        /// <summary>새끼 곰 피벗을 지면에서 띄우는 높이(m). 큐브 높이의 절반 = 콜라이더 바닥이 지면.</summary>
        public static float BearCubGroundOffset
        {
            get { return HasBearCubModel ? BearCubModelGroundOffset : BearGroundOffset * BearCubFallbackShrink; }
        }

        /// <summary>
        /// [B33 텍스처 계약] 곰 겉털 결(grizzled, 무채색). Resources/Textures/bearfur.
        /// 아직 없으면 CreateColorMaterial이 조용히 단색으로 넘어간다(GetMaterial → Resources.Load null).
        /// </summary>
        private const string BearFurTexture = "bearfur";

        /// <summary>[B33 텍스처 계약] 발바닥/마른 진흙(갈라진 가죽). Resources/Textures/bearpad.</summary>
        private const string BearPadTexture = "bearpad";

        /// <summary>
        /// 곰(HazardType.Bear). [B33] 감독이 직접 써 준 실측 스펙을 그대로 옮긴 판이다.
        /// 아래 숫자는 전부 **실제 미터**이고, 지면을 0으로 잡은 높이다(피벗은 지면 위 0.90m).
        ///
        /// ── 치수(감독 스펙 → 실제 들어간 값) ────────────────────────────────────────────
        ///   머리 길이 0.45~0.50 → **0.48**(뒤통수 z 0.80 ~ 코끝 z 1.28) · 얼굴 폭 0.35~0.40 → **0.38**
        ///   주둥이 0.15~0.20 → **0.18**(두께 0.236 × 0.212 - 원뿔이 아니라 뭉툭한 기둥)
        ///   눈 지름 0.03~0.04 → **0.035**(얼굴 폭의 9%) · 미간 0.15~0.20 → **0.169**(중심간 0.204)
        ///   앞다리 1.1~1.2 → **1.18**, 둘레 0.60~0.70 → **0.68**(반지름 0.108)
        ///   뒷다리 1.2~1.3 → **1.27**(굽은 경로), 허벅지 둘레 0.80~0.90 → **0.89**(반지름 0.142)
        ///   앞발 폭 0.25~0.30 → **0.27**, 길이 0.35 → **0.35**, 발톱 5개 × 0.10~0.12 → **0.12**(밑동 지름 0.068)
        ///   뒷발 길이 0.35~0.40 → **0.39**, 폭 0.20~0.25 → **0.23**, 발뒤꿈치 패드가 발등보다 3.7cm 높다
        /// ── 실루엣 ──────────────────────────────────────────────────────────────────────
        ///   어깨 혹 꼭대기 **1.78m = 실루엣 최고점**(등 한가운데 1.615 · 엉덩이 1.375 · 귀 끝 1.51).
        ///   목 경계 없음: 목 단면 폭 0.414 vs 두개골 폭 0.380 - 거의 같은 굵기로 이어지고, 머리는
        ///   목 능선보다 6.7cm 아래로 처진다. 귀는 지름 0.15m로 두개골 위 0.11m만 솟는다.
        ///   코끝~엉덩이 **2.52m** · 다리 사이 배 밑 공간 0.83m(길이/높이 = 1.42).
        ///
        /// ── 파츠 9개(예전 6개) - 렌더러가 나뉘는 기준이 **색**과 **움직임의 소속** 둘이다 ──────
        ///   루트(다리 하단 + 발 + 발뒤꿈치 패드, 마른 진흙 회갈색 · bearpad) ← **콜라이더가 여기 있다**
        ///   Coat     : 몸통 + 머리 + 귀 + 꼬리 + 목 털 다발 (옅은 금갈색 grizzled · bearfur)
        ///   Hump     : [B34] 어깨 혹 + 혹 능선 털 (Coat와 **같은 색·같은 머티리얼**. 오브젝트만 나눴다 -
        ///              CreatureMotion이 이 덩어리만 한 박자 늦게 끌고 다니는 관성 연출 때문이다)
        ///   Underside: 배 껍질 + 배 밑 털 다발 (거의 검정 · bearfur)
        ///   Limbs    : [B34] 다리 상부 4 (Underside와 같은 색/머티리얼. 몸통 오프셋의 절반만 따라간다)
        ///   Claws    : [B34] 발톱 20 (Underside와 같은 색/머티리얼. 발과 함께 **지면에 고정**된다)
        ///   Snout    : 주둥이 + 코 (젖은 짙은 색 · bearpad)
        ///   EyeL/EyeR: 스포너가 만든 구체를 옮겨 씀(새로 만들지 않는다)
        /// 파츠가 늘어도 머티리얼은 예전 그대로 5장뿐이다(루트 · Coat+Hump · Underside+Limbs+Claws · Snout · 눈).
        /// 메시는 종류당 1장 정적 캐시, 머티리얼은 (색+텍스처)당 1장 공유 캐시 → 개체가 몇 마리든
        /// 신규 생성 0이다. 발이 지면에 닿는 것은 SnapPivotForJitter가 sizeJitter까지 보정한다.
        ///
        /// **루트에 발 메시를 둔 것은 의도된 것이다**: 숨쉬기 펄스(HazardSource)가 자식만 늘였다 줄이므로,
        /// 콜라이더가 붙은 루트는 스케일이 절대 변하지 않아야 하고 발은 지면에 박혀 움직이면 안 된다.
        /// </summary>
        public static void AddBearDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            // [B36] 실물 모델이 있으면 그것 하나로 몸을 대신하고 아래 절차 파츠는 한 개도 만들지 않는다.
            // 모델이 없는 환경(에셋 미포함 빌드/체크아웃)에서는 예전 경로가 그대로 돌아 곰이 사라지지 않는다.
            if (BuildBearFromModel(body, appliedScale, bodyColor))
                return;

            // 루트 = 다리 하단 + 발. 콜라이더가 붙은 오브젝트라 스케일이 절대 변하지 않는다.
            ApplyBodyMesh(body, CreatureMeshLibrary.BearPawUnit());
            ApplySharedMaterial(body, BearPawColor(bodyColor), BearPadTexture);
            SnapPivotForJitter(body, appliedScale, BearBodyScale, BearGroundOffset);

            MeterSpacePart(body.transform, "Coat", BearBodyScale, CreatureMeshLibrary.BearCoatMeters(),
                BearCoatColor(bodyColor), BearFurTexture);

            // [B34] 어깨 혹. Coat와 색/텍스처가 같아 머티리얼도 같은 한 장을 공유하므로(Shared 캐시)
            // 정지 상태에서는 예전과 구분되지 않는다. 파츠를 나눈 목적은 CreatureMotion이 이 덩어리만
            // 한 박자 늦게 끌고 다니기 위해서다 - 이름 "Hump"는 CreatureMotion.AttachBear가 찾는 이름이라
            // 바꾸면 관성 연출이 조용히 죽는다.
            MeterSpacePart(body.transform, "Hump", BearBodyScale, CreatureMeshLibrary.BearHumpMeters(),
                BearCoatColor(bodyColor), BearFurTexture);

            MeterSpacePart(body.transform, "Underside", BearBodyScale, CreatureMeshLibrary.BearUndersideMeters(),
                BearUndersideColor(bodyColor), BearFurTexture);

            // [B34] 다리 상부와 발톱. 색/텍스처가 Underside와 같아 머티리얼도 같은 한 장을 공유하므로
            // 정지 상태에서는 예전 한 덩어리였을 때와 구분되지 않는다. 나눈 것은 **움직임의 소속**뿐이다
            // (Limbs = 몸통 오프셋의 절반 / Claws = 발과 함께 지면 고정). 이름을 바꾸면 CreatureMotion이
            // 파츠를 못 찾아 전부 몸통과 함께 움직이고, 발톱이 발에서 빠진다.
            MeterSpacePart(body.transform, "Limbs", BearBodyScale, CreatureMeshLibrary.BearLimbsMeters(),
                BearUndersideColor(bodyColor), BearFurTexture);
            MeterSpacePart(body.transform, "Claws", BearBodyScale, CreatureMeshLibrary.BearClawsMeters(),
                BearUndersideColor(bodyColor), BearFurTexture);

            MeterSpacePart(body.transform, "Snout", BearBodyScale, CreatureMeshLibrary.BearMuzzleMeters(),
                BearMuzzleColor(bodyColor), BearPadTexture);

            // 눈 위치 검산(z 1.02의 두개골 단면 = 가로 반경 0.169m · 세로 반경 0.145m 타원):
            // 이상 타원 **표면에서 법선 방향으로 5mm 안쪽**에 눈 중심을 둔다. 표면에 정확히 얹으면
            // 안 되는 이유는 두개골이 매끈한 타원이 아니라 12각 다면체이기 때문이다 - 면 한가운데는
            // 이상 표면보다 5.8mm 내려앉아 있어서, 표면에 얹은 눈은 그 지점에서 노출이 23mm/35mm가
            // 되어 머리에서 떨어져 보인다(B33 1차 판 "눈이 허공에 떠 있다"의 잔여 원인).
            // 5mm 묻으면 정점에서 노출 12.5mm, 면 한가운데서도 눈이 표면을 확실히 파고든 상태다.
            // 미간은 0.169m로 여전히 스펙(0.15~0.20) 안이고, 거대한 두상(폭 0.38)에 눈은 0.035뿐이다.
            ReshapeSphere(body.transform, "EyeL", BearBodyScale, new Vector3(0.1022f, 0.4179f, 1.02f), 0.035f, EyeBlack, "noise");
            ReshapeSphere(body.transform, "EyeR", BearBodyScale, new Vector3(-0.1022f, 0.4179f, 1.02f), 0.035f, EyeBlack, "noise");

            // 예전 방식의 귀 파츠는 메시에 들어갔다. 남아 있으면 머리 위에 혹 두 개로 겹치므로 지운다.
            RemoveLegacyPart(body.transform, "EarL");
            RemoveLegacyPart(body.transform, "EarR");
        }

        /// <summary>[B36] 모델 곰의 몸 자식 이름. CreatureMotion이 이 이름을 특별 취급하지는 않는다
        /// (파츠 역할 이름 Hump/Limbs/Claws와 겹치지 않기만 하면 된다).</summary>
        private const string BearModelPartName = "Model";

        /// <summary>
        /// [B36] 실물 곰 모델(bear_adult.obj)을 몸으로 붙인다. 붙였으면 true - 호출부는 절차 파츠를
        /// 한 개도 만들지 않고 빠져나간다. 모델이 없으면 아무 것도 하지 않고 false다.
        ///
        /// ── 좌표 계약(에셋이 이미 이렇게 구워져 있다. 여기서 배율/회전을 다시 만지지 않는다) ────
        ///   단위 = 미터 · +Y 위 · +Z 정면(주둥이) · +X 오른쪽 · **발바닥이 정확히 y = 0** · X/Z 중심 정렬.
        ///   실측 0.981 × 1.219 × 2.562 m.
        ///
        /// ── 비균등 부모 스케일 처리(이 파일에서 사고가 나는 유일한 지점) ────────────────────
        ///   루트 localScale = BearBodyScale(0.86 × 1.22 × 2.56) × sizeJitter로 **비균등**이다.
        ///   자식을 그냥 붙이면 모델이 축마다 다르게 늘어난다. MeterSpacePart와 똑같이 자식 localScale을
        ///   1/BearBodyScale로 두면 자식의 로컬 1단위 = 월드 1미터가 되고(sizeJitter만 살아남는다),
        ///   모델은 구워진 그대로의 비율로 선다.
        ///   **localRotation은 반드시 identity다.** 비균등 부모 스케일 × 자식 회전 = 전단(shear)이고,
        ///   이 프로젝트에서 곰이 z축 0.39배로 찌부러졌던 사고의 원인이 정확히 그것이다.
        ///   localPosition.y = -BearGroundOffset / BearBodyScale.y → 모델 발바닥이 정확히 지면에 닿는다
        ///   (부모 스케일이 곱해지므로 월드로는 -groundOffset × jitter. 접지 규칙은 ReshapeSphere와 같다).
        /// </summary>
        private static bool BuildBearFromModel(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            GameObject prefab = BearModelPrefab;
            if (prefab == null)
                return false;

            Vector3 nominal = BearModelBodyScale;
            Material material = BearModelMaterial();

            // 루트(콜라이더 소유자)는 큐브 프리미티브라 그대로 두면 갈색 상자가 모델을 감싼 채 보인다.
            // 렌더러를 끄지 않고 **빈 메시**로 바꾸는 이유: HazardSource.SetVisualActive(true)가 재등장 때
            // 자식 렌더러를 전부 다시 켜므로(enabled 기반 은닉은 되살아난다), 메시 쪽을 비워야 영구적이다.
            ApplyBodyMesh(body, EmptyMesh());
            // 스포너가 개체마다 새로 만든 1회용 머티리얼(MG~ 접두어)을 여기서 회수한다. 루트도 모델과
            // 같은 공유본을 물려 이 세션의 곰 머티리얼이 정확히 한 장이 되게 한다(아무 것도 그리지 않는다).
            ApplySharedMaterial(body, material);
            SnapPivotForJitter(body, appliedScale, nominal, BearModelGroundOffset);

            Transform existing = body.transform.Find(BearModelPartName);
            GameObject model;
            if (existing != null)
            {
                model = existing.gameObject;
            }
            else
            {
                model = Object.Instantiate(prefab);
                model.name = BearModelPartName;
                model.transform.SetParent(body.transform, false);
            }

            model.transform.localPosition = new Vector3(0f, -BearModelGroundOffset / nominal.y, 0f);
            model.transform.localRotation = Quaternion.identity;   // ★ 전단 방지 - 절대 회전시키지 마라
            model.transform.localScale = new Vector3(1f / nominal.x, 1f / nominal.y, 1f / nominal.z);

            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && material != null)
                    renderers[i].sharedMaterial = material;
            }

            // 임포터 설정에 따라 모델에 콜라이더가 딸려 올 수 있다. 판정은 루트의 트리거 하나뿐이라는
            // 규칙(이 파일 상단 주석)을 지키기 위해 자식 콜라이더는 전부 걷어낸다.
            var colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Object.Destroy(colliders[i]);
            }

            // 스포너가 만들어 둔 임시 눈 구체는 모델에 눈이 이미 그려져 있어 필요 없다(머리 밖에 뜬다).
            RemoveLegacyPart(body.transform, "EyeL");
            RemoveLegacyPart(body.transform, "EyeR");
            RemoveLegacyPart(body.transform, "EarL");
            RemoveLegacyPart(body.transform, "EarR");
            return true;
        }

        /// <summary>
        /// [B37] 새끼 곰의 외형을 완성한다. HazardSpawner가 곰 엔트리 중 새끼로 판정된 개체에서만 부른다
        /// (성체 경로 AddBearDetails는 이 메서드와 완전히 분리돼 있고 한 줄도 바뀌지 않았다).
        ///
        /// 두 경로뿐이다:
        ///  1) bear_cub.obj가 있으면 그 모델 하나를 몸으로 붙인다(성체와 같은 방식 · 같은 머티리얼).
        ///  2) 없으면 **성체 빌더를 그대로 부르고 루트만 균등 축소한다**. 성체 빌더가 모델 곰이든
        ///     절차 곰이든 알아서 처리하므로, 새끼 때문에 성체 폴백이 갈라질 여지가 구조적으로 없다.
        /// </summary>
        public static void AddBearCubDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            if (body == null)
                return;

            if (BuildBearCubFromModel(body, appliedScale, bodyColor))
                return;

            // ── 폴백: 성체 한 마리를 그대로 축소해서 새끼로 쓴다 ──────────────────────────
            // 루트 localScale은 스포너가 이미 BearCubBodyScale(= BearBodyScale × 0.58)로 넣어 뒀다.
            // 성체 빌더는 파츠를 성체 규격으로 나눠 붙이므로, 루트가 균등하게 0.58배면 파츠도 통째로
            // 0.58배가 되고 비율은 1도 변하지 않는다.
            AddBearDetails(body, appliedScale, bodyColor);

            // 성체 빌더 안의 SnapPivotForJitter는 **성체** 규격(BearGroundOffset)을 기준으로 피벗을
            // 맞춘다. 스포너가 넣어 둔 시작 높이는 새끼 규격(= BearGroundOffset × shrink)이라 그 차이만큼
            // 어긋난다. 어긋남은 지터와 무관한 상수 BearGroundOffset × (1 - shrink)이므로 여기서 되돌린다.
            //   최종 피벗 = 지면 + BearGroundOffset × shrink × sizeJitter = 콜라이더 바닥 = 발바닥.
            Vector3 position = body.transform.position;
            position.y += BearGroundOffset * (1f - BearCubFallbackShrink);
            body.transform.position = position;
        }

        /// <summary>
        /// [B37] 실물 새끼 곰 모델(bear_cub.obj)을 몸으로 붙인다. 붙였으면 true.
        /// 좌표/스케일 계약은 BuildBearFromModel과 **글자 그대로 같다** - 자식 localScale = 1/규격,
        /// localRotation은 **반드시 identity**(비균등 부모 스케일 × 자식 회전 = 전단. 곰이 z축 0.39배로
        /// 찌부러졌던 사고의 원인이 정확히 그것이다), localPosition.y = -groundOffset/규격.y.
        /// 머티리얼은 성체와 같은 UV 아틀라스라 BearModelMaterial() 공유본을 그대로 물린다.
        /// </summary>
        private static bool BuildBearCubFromModel(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            GameObject prefab = BearCubModelPrefab;
            if (prefab == null)
                return false;

            Vector3 nominal = BearCubModelBodyScale;
            Material material = BearModelMaterial();   // ★ 새 머티리얼을 만들지 않는다(MG~BearModel 재사용)

            // 루트(콜라이더 소유자)의 큐브를 지운다. 렌더러를 끄지 않고 메시를 비우는 이유는
            // 성체와 같다 - SetVisualActive(true)가 재등장 때 렌더러를 전부 다시 켜기 때문이다.
            ApplyBodyMesh(body, EmptyMesh());
            ApplySharedMaterial(body, material);
            SnapPivotForJitter(body, appliedScale, nominal, BearCubModelGroundOffset);

            Transform existing = body.transform.Find(BearModelPartName);
            GameObject model;
            if (existing != null)
            {
                model = existing.gameObject;
            }
            else
            {
                model = Object.Instantiate(prefab);
                model.name = BearModelPartName;
                model.transform.SetParent(body.transform, false);
            }

            model.transform.localPosition = new Vector3(0f, -BearCubModelGroundOffset / nominal.y, 0f);
            model.transform.localRotation = Quaternion.identity;   // ★ 전단 방지 - 절대 회전시키지 마라
            model.transform.localScale = new Vector3(1f / nominal.x, 1f / nominal.y, 1f / nominal.z);

            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && material != null)
                    renderers[i].sharedMaterial = material;
            }

            // 판정은 루트의 트리거 하나뿐이라는 규칙을 지킨다(임포터가 콜라이더를 딸려 보낼 수 있다).
            var colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Object.Destroy(colliders[i]);
            }

            // 스포너가 만들어 둔 임시 눈/귀 구체는 모델에 이미 그려져 있어 필요 없다.
            RemoveLegacyPart(body.transform, "EyeL");
            RemoveLegacyPart(body.transform, "EyeR");
            RemoveLegacyPart(body.transform, "EarL");
            RemoveLegacyPart(body.transform, "EarR");
            return true;
        }

        /// <summary>
        /// [B36] 곰 모델 전용 URP Lit 머티리얼. **한 번만 만들어 모든 개체가 공유한다**
        /// (곰은 섬마다 여러 마리 스폰된다 - 개체마다 만들면 SRP 배처가 죽는다는 이 파일 상단 주석 그대로).
        ///
        /// 셰이더를 찾는 방법은 StructureVisualBuilder.CreateColorMaterial과 **완전히 같다**
        /// (Shader.Find("Universal Render Pipeline/Lit"), 없으면 "Standard"). 이름을 상상해서 적지 않는다.
        /// 이름에 StructureVisualBuilder.RuntimeMaterialPrefix("MG~")를 붙이는 것도 같은 이유다 -
        /// 이 접두어가 없는 머티리얼을 Destroy하면 내장 에셋을 파괴해 "Destroying assets is not permitted"가
        /// 쏟아진다(ApplySharedMaterial 주석의 54건 사고). 여기서 만든 것은 sharedMaterials에 등록해
        /// 두므로 그 가드에 걸려 **절대 파괴되지 않는다**.
        ///
        /// metallic / smoothness는 원본 PBR 맵을 **일부러 쓰지 않는다**:
        ///   · metallic 맵은 평균 21/255짜리 노이즈다. 곰은 금속이 아니므로 상수 0.
        ///   · roughness 맵은 평균 140/255라 그대로 쓰면 젖은 플라스틱처럼 반들거린다. 털은 거의 무광이라
        ///     smoothness 0.17 상수(StructureVisualBuilder.DefaultSmoothness와 같은 계열의 판단)로 고정한다.
        /// </summary>
        private static Material BearModelMaterial()
        {
            if (bearModelMaterial != null)
                return bearModelMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.name = StructureVisualBuilder.RuntimeMaterialPrefix + "BearModel";

            // 알베도가 색을 전부 담고 있으므로 틴트는 흰색이다(곱해져 어두워지면 텍스처가 죽는다).
            material.color = Color.white;

            var albedo = Resources.Load<Texture2D>("Textures/bear_albedo");
            if (albedo != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", albedo);
                else
                    material.mainTexture = albedo;  // Standard 폴백(_MainTex). URP에서는 위가 같은 슬롯이다
            }

            var normal = Resources.Load<Texture2D>("Textures/bear_normal");
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                if (material.HasProperty("_BumpScale"))
                    material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", BearModelSmoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", BearModelSmoothness);

            bearModelMaterial = material;
            sharedMaterials.Add(material);   // 공유본 = 파괴 금지 목록
            return material;
        }

        /// <summary>[B36] 곰 털의 반사도. 0.15~0.2 구간(원본 roughness 맵은 쓰지 않는다).</summary>
        private const float BearModelSmoothness = 0.17f;

        /// <summary>
        /// [B36] 정점이 0개인 공유 메시. 모델 곰의 루트(콜라이더 소유자)가 큐브를 그리지 않게 만든다.
        /// 렌더러를 끄는 대신 메시를 비우는 이유는 BuildBearFromModel 주석 참고.
        /// </summary>
        private static Mesh emptyMesh;

        private static Mesh EmptyMesh()
        {
            if (emptyMesh == null)
            {
                emptyMesh = new Mesh();
                emptyMesh.name = "Cre_Empty";
            }
            return emptyMesh;
        }

        /// <summary>
        /// [B33] 등·어깨의 grizzled 색. 스펙 "옅은 금갈색~회백색(햇빛에 바랜 느낌)".
        /// 씬/config가 정한 bodyColor에서 파생시키므로 디렉터가 색을 바꾸면 네 부위가 함께 따라간다.
        /// 무채색 겉털 텍스처(bearfur)가 이 색 위에 곱해져 털끝의 밝은 결이 생긴다 - 텍스처가 아직
        /// 없으면 그냥 밝은 금갈색 등판이 된다(그것만으로도 등/배 대비는 성립한다).
        /// </summary>
        private static Color BearCoatColor(Color bodyColor)
        {
            return Color.Lerp(ResourceVisualLibrary.Shade(bodyColor, 1.25f), new Color(0.62f, 0.58f, 0.52f), 0.28f);
        }

        /// <summary>[B33] 배 밑·다리 안쪽·발톱. 스펙 "거의 검은색" - 무게감을 아래로 깐다.</summary>
        private static Color BearUndersideColor(Color bodyColor)
        {
            return ResourceVisualLibrary.Shade(bodyColor, 0.28f);
        }

        /// <summary>[B33] 다리 하단·발바닥. 스펙 "진흙이 마른 회갈색" - 채도를 빼고 밝기를 올린다.</summary>
        private static Color BearPawColor(Color bodyColor)
        {
            return Color.Lerp(ResourceVisualLibrary.Shade(bodyColor, 0.6f), new Color(0.46f, 0.42f, 0.36f), 0.55f);
        }

        /// <summary>[B33] 입 주변. 스펙 "촉촉하게 젖은 짙은 색" - 등판보다 4배 어둡다.</summary>
        private static Color BearMuzzleColor(Color bodyColor)
        {
            return ResourceVisualLibrary.Shade(bodyColor, 0.34f);
        }
    }
}
