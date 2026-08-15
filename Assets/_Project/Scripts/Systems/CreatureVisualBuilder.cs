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
        /// 독사(HazardType.VenomousSnake) 전용 디테일. 몸통 캡슐은 이미 눕혀서 길고 얇게 배치되어
        /// 형태만으로도 뱀임을 어느 정도 알 수 있으므로, 머리 쪽(로컬 +Z 끝)에 아주 작은 붉은 혀
        /// 돌기 하나만 더해 "머리가 어느 쪽인지"와 뱀 특유의 느낌을 최소한으로 보강한다.
        /// 과한 디테일(독립된 머리 형상 등)은 넣지 않는다.
        /// </summary>
        public static void AddSnakeDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            AddCompensatedBox(body.transform, new Vector3(0f, 0f, 0.95f), new Vector3(0.04f, 0.04f, 0.16f), appliedScale,
                new Color(0.85f, 0.15f, 0.15f), "Tongue"); // 붉은 혀 돌기
        }

        /// <summary>
        /// 전갈(HazardType.Scorpion) 전용 디테일. 몸통 뒤쪽에서 위로 구부러져 올라간 꼬리 마디(캡슐 2개)와
        /// 앞쪽 양옆의 작은 집게(박스 2개)를 붙여, 몸통 프리미티브 하나로는 나오지 않는 전갈 특유의
        /// 실루엣(들린 꼬리 + 집게)을 최소한의 파츠로 표현한다.
        /// </summary>
        public static void AddScorpionDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color darker = bodyColor * 0.85f;

            AddCompensatedCapsule(body.transform, new Vector3(0f, 0.28f, -0.5f), new Vector3(0.1f, 0.32f, 0.1f), appliedScale,
                darker, "TailSegment1", Quaternion.Euler(35f, 0f, 0f));
            AddCompensatedCapsule(body.transform, new Vector3(0f, 0.55f, -0.75f), new Vector3(0.08f, 0.26f, 0.08f), appliedScale,
                darker, "TailSegment2", Quaternion.Euler(-25f, 0f, 0f));

            AddCompensatedBox(body.transform, new Vector3(0.28f, -0.08f, 0.55f), new Vector3(0.16f, 0.07f, 0.2f), appliedScale,
                darker, "PincerL");
            AddCompensatedBox(body.transform, new Vector3(-0.28f, -0.08f, 0.55f), new Vector3(0.16f, 0.07f, 0.2f), appliedScale,
                darker, "PincerR");
        }

        /// <summary>
        /// 함정(HazardType.Trap) 전용 디테일. 얇은 원판 가장자리를 따라 뾰족한 가시(가는 캡슐)를
        /// 여러 개 둘러 박아, 밋밋한 원판이 아니라 "밟으면 위험한 것"이라는 실루엣을 만든다.
        /// </summary>
        public static void AddTrapDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color spikeColor = new Color(0.15f, 0.13f, 0.1f); // 거의 검은 쇠/나무 가시 색
            const int spikeCount = 8;

            for (int i = 0; i < spikeCount; i++)
            {
                float angle = i * (360f / spikeCount) * Mathf.Deg2Rad;
                Vector3 localPos = new Vector3(Mathf.Cos(angle) * 0.42f, 0.05f, Mathf.Sin(angle) * 0.42f);
                AddCompensatedCapsule(body.transform, localPos, new Vector3(0.025f, 0.13f, 0.025f), appliedScale,
                    spikeColor, $"Spike{i}", Quaternion.Euler(90f, 0f, 0f));
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
        /// </summary>
        public static void AddCannibalDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color woodColor = new Color(0.4f, 0.28f, 0.15f);
            AddCompensatedBox(body.transform, new Vector3(0.42f, 0f, 0.05f), new Vector3(0.05f, 1.6f, 0.05f), appliedScale, woodColor, "Spear");
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
        /// </summary>
        public static void AddSharkTailDetails(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color finColor = bodyColor * 0.8f;
            AddCompensatedBox(body.transform, new Vector3(0f, -0.85f, 0.18f), new Vector3(0.05f, 0.32f, 0.16f), appliedScale, finColor, "TailFin");
        }

        /// <summary>
        /// 사냥감(HuntableCreature, 육상형) 몸통 아래에 짧은 다리 4개를 붙인다. 지금까지는 몸통 캡슐 +
        /// 눈 2개뿐이라, 사람처럼 서서 배치되는 위험 요소(곰/식인종) 캡슐과 실루엣이 겹쳐 "이게 잡을
        /// 수 있는 사냥감인지 위험 요소인지" 구분이 잘 안 됐다. 짧은 네 다리를 더하면 사족보행 동물
        /// 실루엣이 되어 사람 형태(위험 요소)와 한눈에 구분된다. 물고기(preferShoreline)에는 다리가
        /// 어울리지 않으므로 이 메서드는 육상 동물 쪽에서만 호출해야 한다.
        /// </summary>
        public static void AddQuadrupedLegs(GameObject body, Vector3 appliedScale, Color bodyColor)
        {
            Color legColor = bodyColor * 0.85f;
            Vector3[] legLocalPositions =
            {
                new Vector3(0.18f, -0.55f, 0.22f),
                new Vector3(-0.18f, -0.55f, 0.22f),
                new Vector3(0.18f, -0.55f, -0.22f),
                new Vector3(-0.18f, -0.55f, -0.22f),
            };

            for (int i = 0; i < legLocalPositions.Length; i++)
                AddCompensatedCapsule(body.transform, legLocalPositions[i], new Vector3(0.08f, 0.35f, 0.08f), appliedScale, legColor, $"Leg{i}");
        }
    }
}
