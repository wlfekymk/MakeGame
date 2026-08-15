using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 위험 요소(HazardSource)/사냥감(HuntableCreature)처럼 몸통 프리미티브 하나만으로는 종류를 구분하기
    /// 어려운 생물형 오브젝트에, 눈/꼬리/집게/다리/귀/창 같은 최소한의 보조 파츠를 절차적으로 붙여주는
    /// 공용 유틸리티. B2-16(독사/전갈/함정/육상 사냥감)에 이어 B2-17(곰/식인종/상어)까지 더해
    /// HazardType 7종 중 벌떼(이미 충분하다고 판단해 보강 제외)를 뺀 전부가 이 클래스를 거친다.
    /// StructureVisualBuilder(구조물 전용)와 완전히 동일한 패턴 - 프리미티브 조합 + CreateColorMaterial +
    /// 비균일 스케일 보정 - 을 그대로 따른다.
    /// HazardSpawner.AddDetailParts/CreatureSpawner.AddCompensated에 각각 거의 동일한 형태로 중복
    /// 정의되어 있던 "부모의 비균일 스케일을 상쇄한 보조 파츠 생성" 헬퍼를 이 한 곳으로 모아, 새 디테일을
    /// 추가할 때마다 같은 코드를 또 베끼지 않도록 한다.
    /// 실제 호출 연결은 이 클래스 소유가 아닌 HazardSpawner.cs/CreatureSpawner.cs 쪽 몫이라
    /// (파일 소유권 규칙) 여기서는 만들기만 하고 붙이지 않는다.
    /// </summary>
    public static class CreatureVisualBuilder
    {
        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 구체 보조 파츠(눈, 벌떼 등)를 만들어 붙인다.
        /// worldRadius는 부모가 어떻게 눌리거나 늘어나 있어도 항상 같은 월드 크기의 둥근 구체로
        /// 보이게 하기 위한 목표 반지름(미터)이다.
        /// </summary>
        public static GameObject AddCompensatedSphere(Transform parent, Vector3 localPos, float worldRadius, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.x),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.y),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Sphere, localPos, compScale, color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 박스 보조 파츠(지느러미, 가시 등 각진 형태)를 만들어 붙인다.
        /// worldSize는 목표 월드 크기(가로/높이/깊이, 미터)다.
        /// </summary>
        public static GameObject AddCompensatedBox(Transform parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name, Quaternion? localRotation = null)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Cube, localPos, compScale, color, localRotation);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 캡슐 보조 파츠(전갈 꼬리 마디, 다리 등 길쭉한 형태)를 만들어 붙인다.
        /// </summary>
        public static GameObject AddCompensatedCapsule(Transform parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name, Quaternion? localRotation = null)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            return StructureVisualBuilder.CreateVisualPart(parent, name, PrimitiveType.Capsule, localPos, compScale, color, localRotation);
        }

        /// <summary>
        /// 눕혀서 배치된 몸통(뱀/전갈처럼 rotationEuler(0,0,90)으로 세팅된 개체)에 붙일, "일어선"
        /// 좌표계 피벗을 만든다. 이 피벗 아래에서는 로컬 축이 월드 축과 정확히 일치하고(+Y가 위),
        /// 스케일이 1(=1로컬 단위가 1미터)이 되므로 파츠를 미터 단위로, 회전까지 자유롭게 배치할 수 있다.
        ///
        /// 왜 필요한가: 몸통이 Z축 +90도로 눕혀지면 로컬 +X가 월드 위, 로컬 +Y가 몸통 진행 방향,
        /// 로컬 +Z가 좌우가 된다. 상어 등지느러미가 옆구리에 붙어 있던 사고(B4-3)와 아래에서 고친
        /// 뱀 혀/전갈 꼬리·집게가 전부 이 축 뒤바뀜을 눈치채지 못해 생긴 같은 원인의 버그였다.
        /// 매번 축을 손으로 뒤집는 대신 좌표계를 한 번 바로 세워두면 같은 실수가 구조적으로 막힌다.
        ///
        /// 계산 근거: 자식의 월드 변환은 (부모회전 R)·(부모스케일 S)·(자식회전 r)·(자식스케일 s) 순이다.
        /// r = Euler(0,0,-90)을 주면 자식의 +X는 S를 거쳐 부모 Y축으로, +Y는 부모 X축으로 간다.
        /// 따라서 s = (1/S.y, 1/S.x, 1/S.z)로 두면 세 축의 월드 길이가 정확히 1이 되어
        /// 피벗 내부가 무회전·단위스케일 좌표계가 된다(90도 회전이라 전단은 발생하지 않는다).
        /// 부모의 Y축 무작위 회전(yawJitter)은 그대로 상속되므로 개체 방향은 유지된다.
        /// 피벗 기준 축: +Y = 위, +X = 머리 방향(몸통이 좌우 대칭이라 어느 끝을 머리로 볼지는 임의로
        /// 정한 값이다), +Z = 좌우.
        /// </summary>
        public static Transform CreateUprightPivot(GameObject body, Vector3 appliedScale, string name)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(body.transform, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            pivot.transform.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, appliedScale.y),
                1f / Mathf.Max(0.0001f, appliedScale.x),
                1f / Mathf.Max(0.0001f, appliedScale.z));
            return pivot.transform;
        }

        /// <summary>
        /// CreateUprightPivot 아래에 미터 단위로 박스 파츠를 붙인다(로컬 스케일 = 실제 크기).
        /// </summary>
        private static GameObject AddUprightBox(Transform pivot, string name, Vector3 posMeters, Vector3 sizeMeters,
            Color color, Quaternion? rotation = null)
        {
            return StructureVisualBuilder.CreateVisualPart(pivot, name, PrimitiveType.Cube, posMeters, sizeMeters, color, rotation);
        }

        /// <summary>
        /// CreateUprightPivot 아래에 미터 단위로 구체 파츠를 붙인다(지름 = diameterMeters).
        /// </summary>
        private static GameObject AddUprightSphere(Transform pivot, string name, Vector3 posMeters, float diameterMeters, Color color)
        {
            return StructureVisualBuilder.CreateVisualPart(pivot, name, PrimitiveType.Sphere, posMeters,
                new Vector3(diameterMeters, diameterMeters, diameterMeters), color);
        }

        /// <summary>
        /// CreateUprightPivot 아래에 미터 단위로 캡슐/원기둥 파츠를 붙인다.
        /// 주의: 유니티의 캡슐/원기둥 기본 메시는 높이가 2단위라, 전체 길이를 lengthMeters로 맞추려면
        /// 로컬 Y 스케일에 그 절반을 넣어야 한다(이 계산을 빠뜨려 기존 파츠들이 의도의 2배로 커져 있었다).
        /// </summary>
        private static GameObject AddUprightCapsule(Transform pivot, string name, Vector3 posMeters,
            float diameterMeters, float lengthMeters, Color color, Quaternion? rotation = null)
        {
            return StructureVisualBuilder.CreateVisualPart(pivot, name, PrimitiveType.Capsule, posMeters,
                new Vector3(diameterMeters, lengthMeters * 0.5f, diameterMeters), color, rotation);
        }

        /// <summary>
        /// 독사(HazardType.VenomousSnake) 전용 디테일.
        ///
        /// [B4 수정 - 상어 등지느러미와 완전히 같은 축 착각 버그였다] 기존 코드는 혀를 로컬 +Z로 0.95만큼
        /// 밀었는데, 눕혀진 몸통에서 로컬 +Z는 "좌우"다. 즉 혀가 머리 끝이 아니라 몸통 옆구리에 0.17m
        /// 튀어나온 돌기였다(몸통 반지름은 0.09m뿐이라 그냥 옆에 붙은 혹으로 보였다). 몸통의 진행
        /// 방향은 로컬 +Y이고, 그 끝은 로컬 y=±1(월드 0.6m)이다.
        ///
        /// 이제 CreateUprightPivot으로 좌표계를 세운 뒤 미터 단위로 배치한다:
        /// - 살짝 들어올린 머리(구체) — 지면에 붙은 초록 막대기가 20m 밖에서 전혀 식별되지 않던 문제를
        ///   실루엣으로 보강한다(머리 꼭대기 0.275m vs 몸통 등 0.19m).
        /// - 붉은 혀(Danger Red) — 머리가 어느 쪽인지, 그리고 이것이 위험 요소라는 신호.
        /// - 어두운 띠 2개 — 색맹 대응과 야간 가독성을 위한 "무늬" 신호. 색이 아니라 패턴이라
        ///   초록/갈색 구분이 안 되는 조건에서도 독사임이 읽힌다.
        /// </summary>
        public static void AddSnakeDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Transform pivot = CreateUprightPivot(body, appliedScale, "SnakeParts");

            // 머리: 몸통 앞 끝(0.6m)에서 살짝 들려 올라간다.
            AddUprightSphere(pivot, "Head", new Vector3(0.56f, 0.09f, 0f), 0.17f, bodyColor * 0.9f);

            // 혀: 머리 앞으로 뻗은 가는 막대.
            AddUprightBox(pivot, "Tongue", new Vector3(0.70f, 0.09f, 0f), new Vector3(0.10f, 0.015f, 0.015f),
                StructureVisualBuilder.DangerRed);

            // 몸통 무늬 띠 2개(원기둥을 몸통 축과 나란히 눕혀 감는다). 피벗 안은 단위 스케일이라
            // 회전을 줘도 전단이 생기지 않는다.
            Color bandColor = new Color(0.12f, 0.12f, 0.12f);
            Quaternion aroundBody = Quaternion.Euler(0f, 0f, 90f);
            StructureVisualBuilder.CreateVisualPart(pivot, "Band0", PrimitiveType.Cylinder,
                new Vector3(0.10f, 0f, 0f), new Vector3(0.20f, 0.025f, 0.20f), bandColor, aroundBody);
            StructureVisualBuilder.CreateVisualPart(pivot, "Band1", PrimitiveType.Cylinder,
                new Vector3(-0.25f, 0f, 0f), new Vector3(0.20f, 0.025f, 0.20f), bandColor, aroundBody);
        }

        /// <summary>
        /// 전갈(HazardType.Scorpion) 전용 디테일.
        ///
        /// [B4 수정 - 뱀과 동일한 축 착각 버그] 눕혀진 몸통에서 로컬 +X는 "월드 위쪽"인데, 기존 코드는
        /// 집게를 로컬 x=±0.28에 놓았다. 즉 집게 두 개가 좌우가 아니라 위/아래로 한 개씩 붙어 있었다.
        /// 꼬리도 마찬가지로 "위로 들린" 방향(로컬 +X)이 아니라 로컬 +Y(몸통 진행 방향)와 -Z(좌우)로
        /// 밀려 있어서, 들린 꼬리 실루엣이 전혀 만들어지지 않았다. 게다가 캡슐 메시 높이가 2단위라는
        /// 점을 감안하지 않아 꼬리 마디 길이가 의도(0.32/0.26m)의 2배(0.64/0.52m)로, 0.6m짜리 몸통보다
        /// 긴 상태였다.
        ///
        /// 이제 CreateUprightPivot 좌표계에서 미터 단위로 배치한다 - 뒤쪽 위로 솟았다가 앞으로 휘어
        /// 내려오는 꼬리 2마디(끝은 Danger Red 독침)와, 앞쪽 좌우로 벌린 집게 2개.
        /// 전갈은 몸길이 0.6m·굵기 0.16m로 매우 작아, 들린 꼬리가 사실상 유일하게 원거리에서 읽히는
        /// 실루엣 단서다(꼬리 끝 높이 지면 위 0.43m = 몸통 등의 2.3배).
        /// </summary>
        public static void AddScorpionDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Transform pivot = CreateUprightPivot(body, appliedScale, "ScorpionParts");
            Color darker = bodyColor * 0.85f;

            // 꼬리 1마디: 몸통 뒤(-X)에서 위로 솟는다. +Z축 기준 +35도 = +Y(위)가 -X(뒤)쪽으로 기운다.
            AddUprightCapsule(pivot, "TailSegment1", new Vector3(-0.297f, 0.142f, 0f), 0.055f, 0.20f, darker,
                Quaternion.Euler(0f, 0f, 35f));
            // 꼬리 2마디(독침): 1마디 끝에서 반대로 휘어 앞쪽 위를 향한다.
            AddUprightCapsule(pivot, "TailSegment2", new Vector3(-0.295f, 0.284f, 0f), 0.045f, 0.17f,
                StructureVisualBuilder.DangerRed, Quaternion.Euler(0f, 0f, -45f));

            // 집게: 몸통 앞 끝에서 좌우로. 몸통 반지름이 0.08m이므로 ±0.085m면 실루엣 밖으로 나온다.
            AddUprightBox(pivot, "PincerL", new Vector3(0.30f, -0.02f, 0.085f), new Vector3(0.14f, 0.035f, 0.05f), darker);
            AddUprightBox(pivot, "PincerR", new Vector3(0.30f, -0.02f, -0.085f), new Vector3(0.14f, 0.035f, 0.05f), darker);
        }

        /// <summary>
        /// 함정(HazardType.Trap) 전용 디테일. 얇은 원판 가장자리를 따라 뾰족한 가시(가는 캡슐)를
        /// 여러 개 둘러 박아, 밋밋한 원판이 아니라 "밟으면 위험한 것"이라는 실루엣을 만든다.
        ///
        /// [B4 수정 - 가시가 길이 3.9m짜리 바늘로 사방에 뻗어 있었다] 실측 계산:
        /// 함정 몸통은 localScale (0.6, 0.04, 0.6)로 극단적으로 납작한 원판이다. 여기에 가시를
        /// Euler(90,0,0)으로 눕혀 붙였는데, 90도 회전은 자식의 길이축(로컬 Y)을 부모의 Z축으로 옮긴다.
        /// 그래서 스케일 보정값 0.13/0.04 = 3.25가 얇은 Y축(0.04) 대신 넓은 Z축(0.6)에 곱해져
        /// 캡슐 길이가 2 × 3.25 × 0.6 = 3.9m가 됐다(의도는 0.13m). 두께도 반대로 0.0017m까지 눌려,
        /// 지름 0.6m짜리 함정에서 종잇장 같은 3.9m 바늘 8개가 방사형으로 뻗어 나가는 상태였다.
        /// 원인은 상어 등지느러미/뱀 혀와 같은 계열의 실수(회전과 비균일 스케일의 상호작용)다.
        /// 수정: 가시를 회전 없이 위로 세운다(원판 몸통은 rotationEuler가 0이라 로컬 축 = 월드 축이고,
        /// 회전이 없으면 축이 뒤바뀔 여지 자체가 사라진다). 캡슐 메시 높이가 2단위이므로 worldSize.y에는
        /// 목표 길이의 절반(0.08 → 실제 0.16m)을 넣는다. 지면 위 0.17m까지 솟은 이빨 8개가 된다.
        /// </summary>
        public static void AddTrapDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color spikeColor = new Color(0.15f, 0.13f, 0.1f); // 거의 검은 쇠/나무 가시 색
            const int spikeCount = 8;

            for (int i = 0; i < spikeCount; i++)
            {
                float angle = i * (360f / spikeCount) * Mathf.Deg2Rad;
                // 로컬 Y 1.25 = 월드 0.05m (원판 몸통의 Y 스케일이 0.04이므로).
                Vector3 localPos = new Vector3(Mathf.Cos(angle) * 0.42f, 1.25f, Mathf.Sin(angle) * 0.42f);
                AddCompensatedCapsule(body.transform, localPos, new Vector3(0.03f, 0.08f, 0.03f), appliedScale,
                    spikeColor, $"Spike{i}");
            }
        }

        /// <summary>
        /// 지정한 위험 요소 종류에 맞는 디테일 파츠를 몸통에 붙인다. 이 메서드는
        /// HazardSpawner.AddDetailParts의 switch 뒤에서 종류 구분 없이 항상 호출되므로(호출부는 이미
        /// 연결되어 있음 - B2-16), 여기 case를 추가/제거하는 것만으로 새 디테일을 얹거나 뺄 수 있다.
        /// - 독사/전갈/함정: 그동안 몸통 프리미티브 + 색상뿐이라 실루엣 구분이 약해 이 메서드가
        ///   유일한 디테일 소스다(B2-16에서 추가).
        /// - 곰/식인종/상어: HazardSpawner.AddDetailParts가 이미 자체적으로 눈(공통)/등지느러미(상어)를
        ///   만들고 있는데, 그 위에 이 메서드가 종류별 실루엣 보강을 "추가로" 얹는다(B2-17) - 곰은
        ///   귀+주둥이로 "동물 머리"임을, 식인종은 옆에 세운 창으로 "무장한 사람"임을 드러내 실루엣만
        ///   보고도 두 서 있는 캡슐을 구분할 수 있게 하고, 상어는 꼬리지느러미를 더해 몸의 앞/뒤가
        ///   드러나게 한다. 이름이 겹치지 않아(EarL/Snout/Spear/TailFin) 기존 눈/지느러미와 충돌하지 않는다.
        /// - 벌떼: 실물 확인 결과 이미 몸통 구체 + 흩어진 작은 구체 5개로 "무리" 느낌이 충분해
        ///   보강하지 않는다(B2-17에서 판단, 억지로 손대지 않음).
        /// </summary>
        public static void AddHazardDetailsIfMissing(GameObject body, HazardType type, Vector3 appliedScale, Color bodyColor)
        {
            switch (type)
            {
                case HazardType.VenomousSnake:
                    AddSnakeDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Scorpion:
                    AddScorpionDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Trap:
                    AddTrapDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Bear:
                    AddBearDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Cannibal:
                    AddCannibalDetails(body, appliedScale, bodyColor);
                    break;
                case HazardType.Shark:
                    AddSharkTailDetails(body, appliedScale, bodyColor);
                    break;
            }
        }

        /// <summary>
        /// 곰(HazardType.Bear) 전용 보강 디테일(B2-17). 기존에 이미 붙어 있는 눈(공통 케이스)만으로는
        /// 식인종과 똑같이 "서 있는 캡슐"로 보여 구분이 어려웠다. 회전 없는 구체(귀 2개)와 박스(주둥이)만
        /// 사용해 - 캡슐 자체나 기존 눈 배치는 건드리지 않고 - 위쪽에 "동물 머리" 실루엣 단서를 더한다.
        /// 회전이 필요 없는 파츠만 골라 써서(구체는 방향이 없고, 박스는 회전 없이도 로컬 축이 그대로
        /// 원하는 치수와 일치) 비균일 스케일 보정 계산이 어긋날 여지를 없앴다.
        /// </summary>
        public static void AddBearDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color darker = bodyColor * 0.7f;

            // 귀 두 개: 기존 눈(0.18, 0.75, 0.35)보다 위/뒤쪽에 붙인다.
            AddCompensatedSphere(body.transform, new Vector3(0.22f, 0.98f, 0.12f), 0.11f, appliedScale, darker, "EarL");
            AddCompensatedSphere(body.transform, new Vector3(-0.22f, 0.98f, 0.12f), 0.11f, appliedScale, darker, "EarR");

            // 주둥이: 얼굴 앞(+Z)으로 짧게 튀어나온 박스. 곰/식인종은 rotationEuler가 0이라 로컬 축이
            // 곧 월드 축과 같으므로 회전 없이도 원하는 방향(앞)으로 정확히 튀어나온다.
            AddCompensatedBox(body.transform, new Vector3(0f, 0.62f, 0.5f), new Vector3(0.16f, 0.13f, 0.22f), appliedScale, darker, "Snout");
        }

        /// <summary>
        /// 식인종(HazardType.Cannibal) 전용 보강 디테일(B2-17). 곰과 반대 방향의 구분 신호를 준다 -
        /// 몸통(사람 실루엣)은 그대로 두고, 옆에 세워 든 창 하나(회전 없는 세로 박스)만 더해
        /// "무장한 사람"이라는 실루엣을 만든다. 곰(동물 머리 단서)과 나란히 보면 한쪽은 동물,
        /// 한쪽은 무기를 든 사람으로 멀리서도 구분된다.
        ///
        /// [B4 수정 - 창이 몸통 안에 박혀 있어 보이지 않았다] 식인종 몸통은 localScale (0.55, 0.9, 0.55)
        /// 캡슐이라 월드 반지름이 0.275m다. 그런데 창을 로컬 x=0.42(=월드 0.231m)에 두어, 두께
        /// 0.05m를 더해도 최대 반경이 0.256m로 몸통 반지름 안이었다 - 즉 1.6m짜리 창이 통째로 몸통
        /// 안에 묻혀 어느 각도에서도 보이지 않았고, "무장한 사람" 실루엣이라는 이 파츠의 존재 이유가
        /// 통째로 무효였다(곰과 구분되는 유일한 단서였다). 로컬 x=0.62(=월드 0.341m)로 밀어내
        /// 몸통 표면(0.275m) 밖으로 완전히 내놓고, 창 끝이 머리(월드 1.8m) 위로 나오도록 살짝 올렸다.
        /// 돌촉을 하나 더해 멀리서도 막대기가 아니라 무기로 읽히게 한다.
        /// 회전은 주지 않는다 - 부모 스케일이 비균일(0.55/0.9)이라 회전한 자식은 전단으로 찌그러진다.
        /// </summary>
        public static void AddCannibalDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color woodColor = new Color(0.4f, 0.28f, 0.15f);
            AddCompensatedBox(body.transform, new Vector3(0.62f, 0.15f, 0.05f), new Vector3(0.07f, 1.6f, 0.07f), appliedScale, woodColor, "Spear");
            AddCompensatedBox(body.transform, new Vector3(0.62f, 1.06f, 0.05f), new Vector3(0.09f, 0.20f, 0.04f), appliedScale,
                StructureVisualBuilder.WeatheredStone, "SpearHead");
        }

        /// <summary>
        /// 상어(HazardType.Shark) 전용 보강 디테일(B2-17). HazardSpawner.AddDetailParts가 이미 만드는
        /// 등지느러미(Fin, 로컬 y=0.1 부근)와 짝을 이루는 꼬리지느러미를 몸통 뒤쪽 끝(눈이 있는 +Y
        /// 반대편, -Y)에 붙여 몸의 앞/뒤가 실루엣만으로 구분되게 한다. 회전 없는 박스라 상어 몸통의
        /// 기존 회전(Quaternion.Euler(0,0,90))에 대해서도 로컬 축 기준으로 동일하게 동작한다.
        /// 참고(B4-3 갱신): 상어 전체가 SharkSpawner.depthBelowSeaLevel만큼 해수면 아래에 배치된다.
        /// 몸통 캡슐의 수직 반경은 0.225m이고, 1.9배로 키운 등지느러미 꼭대기는 몸통 중심 위 0.39m다.
        /// 따라서 배치 깊이가 0.39m보다 얕아야 지느러미가 수면을 뚫는다. B4-2에서 코드 기본값과 씬
        /// 직렬화 값을 모두 0.3으로 맞춰(이전: 코드 0.6 / 씬 2) 약 0.09m가 수면 위로 드러난다.
        /// 지느러미 크기를 다시 조정하려면 SharkSpawner.depthBelowSeaLevel과 반드시 함께 계산할 것.
        ///
        /// [B5 축 정정 - 등지느러미와 같은 버그가 꼬리지느러미에 그대로 남아 있었다]
        /// 몸통이 Euler(0,0,90)으로 눕혀져 있어 로컬 축의 의미는 "+X = 월드 위쪽, +Y = 몸통 진행 방향,
        /// +Z = 좌우"다(Z축 +90도 회전: +X→+Y, +Y→-X). 그런데 기존 worldSize는 (0.05, 0.32, 0.16)이라
        /// 수직 높이(로컬 X)가 5cm뿐이고 좌우 폭(로컬 Z)이 16cm인, 물 위에 뜬 가로 판때기였다 - 꼬리
        /// 지느러미의 형태 신호(세로로 선 삼각 꼬리)가 전혀 없었다.
        /// (0.32, 0.32, 0.06)으로 바꿔 수직 32cm / 진행 방향 32cm / 두께 6cm의 "세워진 꼬리"로 만든다.
        /// localPosition의 z=0.18도 같은 축 혼동의 산물이었다 - 위로 띄우려던 값이 실제로는 옆구리
        /// 방향(로컬 Z)으로 18cm 밀어낸 것이라 꼬리가 몸통 중심선에서 한쪽으로 어긋나 있었다. 0으로
        /// 되돌려 중심선에 맞춘다. 몸통 뒤쪽 끝(-Y 0.85)과 몸통 캡슐/콜라이더는 건드리지 않는다.
        ///
        /// 판정 불변: 이 파츠는 CreateVisualPart가 콜라이더를 제거한 순수 시각 오브젝트이고, 상어의
        /// 판정은 몸통 캡슐의 트리거 콜라이더 하나뿐이라 꼬리를 세워도 공격 범위는 변하지 않는다.
        /// 노출 높이도 변하지 않는다: 꼬리는 몸통 중심 위 0.16m×몸통 스케일 0.45 = 0.072m까지만
        /// 올라와, 등지느러미 꼭대기(0.39m)보다 훨씬 낮으므로 수면 노출 계산(depthBelowSeaLevel 0.3)에
        /// 아무 영향을 주지 않는다 - depthBelowSeaLevel은 손대지 않는다.
        /// </summary>
        public static void AddSharkTailDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color finColor = bodyColor * 0.8f;
            AddCompensatedBox(body.transform, new Vector3(0f, -0.85f, 0f), new Vector3(0.32f, 0.32f, 0.06f), appliedScale, finColor, "TailFin");
        }

        /// <summary>
        /// 사냥감(HuntableCreature, 육상형) 몸통 아래에 짧은 다리 4개를 붙인다. 지금까지는 몸통 캡슐 +
        /// 눈 2개뿐이라, 사람처럼 서서 배치되는 위험 요소(곰/식인종) 캡슐과 실루엣이 겹쳐 "이게 잡을
        /// 수 있는 사냥감인지 위험 요소인지" 구분이 잘 안 됐다. 짧은 네 다리를 더하면 사족보행 동물
        /// 실루엣이 되어 사람 형태(위험 요소)와 한눈에 구분된다. 물고기(preferShoreline)에는 다리가
        /// 어울리지 않으므로 이 메서드는 육상 동물 쪽에서만 호출해야 한다.
        ///
        /// [B4 수정 - 다리가 몸통 안에 숨어 실제로는 하나도 보이지 않았다] 실측:
        /// 몸통은 localScale (0.45, 0.6, 0.45) 캡슐이고 CreatureSpawner가 position + up*0.6에 놓으므로
        /// 월드 반지름 0.225m, 몸통 바닥이 정확히 지면(중심 기준 -0.6m)이다. 기존 다리는
        /// (a) 가로 위치가 로컬 ±0.18/±0.22 = 월드 ±0.081/±0.099로 몸통 반지름 0.225m 안쪽이었고,
        /// (b) 캡슐 메시 높이가 2단위라는 점을 빠뜨려 worldSize.y=0.35가 실제로는 길이 0.7m가 되는 바람에
        /// 다리가 아래로는 지면 밑 0.08m까지 파묻혔다. 결과적으로 네 다리 전부가 몸통 옆구리와 땅 사이에
        /// 완전히 가려져, "다리 유무 = 잡을 수 있는 대상인가"(ArtDirection 2장 3번)라는 이 프로젝트의
        /// 1차 시각 신호가 사실상 존재하지 않았다.
        /// 수정: 길이를 0.42m로 정확히 맞춰 지면~몸통 아래를 채우고(월드 -0.6 ~ -0.18m), 네 다리를
        /// 몸통 실루엣 밖(월드 반경 0.226m + 다리 반지름 0.045m = 0.271m > 0.225m)으로 벌려 어느
        /// 방향에서 봐도 다리 4개가 보이게 했다. 몸통 위치/스케일/콜라이더는 건드리지 않는다(판정 불변).
        /// </summary>
        public static void AddQuadrupedLegs(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color legColor = bodyColor * 0.7f; // 몸통보다 확실히 어둡게 - 실루엣 경계가 대비로도 읽히게
            Vector3[] legLocalPositions =
            {
                new Vector3(0.356f, -0.65f, 0.356f),
                new Vector3(-0.356f, -0.65f, 0.356f),
                new Vector3(0.356f, -0.65f, -0.356f),
                new Vector3(-0.356f, -0.65f, -0.356f),
            };

            // worldSize.y는 캡슐 메시(높이 2단위) 특성상 "전체 길이의 절반"으로 들어간다 - 0.21 → 0.42m.
            for (int i = 0; i < legLocalPositions.Length; i++)
                AddCompensatedCapsule(body.transform, legLocalPositions[i], new Vector3(0.09f, 0.21f, 0.09f), appliedScale, legColor, $"Leg{i}");
        }
    }
}
