using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// B3-3: 스포너(IslandResourceSpawner/HazardSpawner/CreatureSpawner/BoatBlueprintSpawner/
    /// SharkSpawner)가 UnityEngine.Random 대신 쓰는 결정적(deterministic) 난수 유틸리티.
    ///
    /// 문제: 예전에는 모든 스포너가 전역 UnityEngine.Random을 그대로 썼다. WorldMapManager.Awake가
    /// worldSeed로 UnityEngine.Random.InitState를 한 번 호출해 두긴 했지만, 그 뒤로 여러 스포너가
    /// "섬 A 자원 → 섬 A 위험요소 → 섬 A 도면 → 섬 A 사냥감 → 섬 B 자원 → ..." 순서로 같은 전역
    /// 스트림에서 난수를 이어 뽑는다. 이 순서 하나라도 어긋나면(예: 어떤 섬에 특정 자원이 하나 더/덜
    /// 나오거나, 스포너 호출 순서가 바뀌거나) 그 뒤 모든 섬의 난수 시퀀스가 통째로 밀려버려, 겉보기엔
    /// 같은 worldSeed인데도 재생성 결과가 달라질 수 있다 - 자원 노드 채집 상태를 "섬 인덱스 + 생성
    /// 순번"으로 저장하려면(B3-4) 이 재현성이 절대적으로 보장돼야 한다.
    /// [후속(qa 지적, B3-4/B3-5 전제 붕괴 수정)] 스포너보다 한 계층 위인 섬 "크기"(IslandGenerator)와
    /// "위치"(WorldMapManager.FindValidPosition)도 같은 문제를 그대로 갖고 있었고, 그 전역 스트림을
    /// WeatherSystem도 함께 소비해 실행 순서가 보장되지 않는 상황에서 재현성이 깨질 수 있었다. 이제
    /// WorldMapManager는 Random.InitState를 전혀 호출하지 않고, 섬 레이아웃도 이 클래스의 CreateForSalt로
    /// 만든 전용 격리 스트림을 쓴다(WorldMapManager.islandLayoutRng 참고) - 전역 UnityEngine.Random은
    /// 이제 이 프로젝트의 결정적 재생성 경로 어디에서도 쓰이지 않는다.
    ///
    /// 해결: 섬(또는 독립적인 스폰 그룹)마다 완전히 독립된 System.Random 인스턴스를 worldSeed와
    /// 섬 인덱스(또는 그룹 salt)를 조합한 시드로 새로 만든다. 각 섬의 난수 스트림이 서로 완전히
    /// 분리되어 있으므로, 다른 섬에서 무엇을 몇 개 뽑았는지와 무관하게 "이 섬" 하나만 놓고 봐도
    /// 항상 같은 순서로 같은 값이 나온다. UnityEngine.Random.InitState를 스포너 안에서 추가로 호출하지
    /// 않는 이유도 이것과 같다 - 전역 상태를 건드리면 같은 프레임에 동작하는 다른 시스템(파티클,
    /// 연출, UI 애니메이션 등)의 무작위 결과까지 오염시켜 이 스포너와 무관한 버그를 만들 수 있다.
    /// </summary>
    public static class SeededRandomExtensions
    {
        /// <summary>
        /// worldSeed와 임의의 정수 salt(섬 인덱스, 또는 섬에 속하지 않는 독립 스폰 그룹을 구분하는 값)를
        /// 조합해 결정적인 System.Random 인스턴스를 새로 만든다. 같은 (worldSeed, salt) 조합이면
        /// 언제 호출하든 항상 같은 시드로 시작하는 새 인스턴스를 반환한다.
        /// </summary>
        public static System.Random CreateForSalt(int worldSeed, int salt)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + worldSeed;
                hash = hash * 31 + salt;
                return new System.Random(hash);
            }
        }

        /// <summary>섬 하나 전용 결정적 System.Random 인스턴스를 만든다 (salt로 섬의 islandId를 그대로 쓴다).</summary>
        public static System.Random CreateForIsland(int worldSeed, int islandIndex)
        {
            return CreateForSalt(worldSeed, islandIndex);
        }

        /// <summary>[min, max) 범위의 실수 하나를 뽑는다. UnityEngine.Random.Range(float, float)과 같은 범위 규칙.</summary>
        public static float NextFloat(this System.Random rng, float min, float max)
        {
            return (float)(min + rng.NextDouble() * (max - min));
        }

        /// <summary>[min, max) 범위의 정수 하나를 뽑는다. UnityEngine.Random.Range(int, int)과 동일하게 max는 제외된다.</summary>
        public static int NextInt(this System.Random rng, int minInclusive, int maxExclusive)
        {
            return rng.Next(minInclusive, maxExclusive);
        }

        /// <summary>0 이상 1 미만의 실수 하나를 뽑는다. UnityEngine.Random.value에 대응한다.</summary>
        public static float NextValue01(this System.Random rng)
        {
            return (float)rng.NextDouble();
        }

        /// <summary>
        /// 반지름 1인 원판 안의 균등 분포 무작위 점을 뽑는다. UnityEngine.Random.insideUnitCircle에 대응한다.
        /// (각도, 반지름의 제곱근)으로 뽑는 방식이라 호출 1회당 항상 정확히 2번의 난수 draw만 소비해,
        /// 거부 표집(rejection sampling)처럼 호출마다 소비량이 달라지는 방식보다 시퀀스 추적이 단순하다.
        /// </summary>
        public static Vector2 NextInsideUnitCircle(this System.Random rng)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            double radius = System.Math.Sqrt(rng.NextDouble());
            return new Vector2((float)(System.Math.Cos(angle) * radius), (float)(System.Math.Sin(angle) * radius));
        }
    }
}
