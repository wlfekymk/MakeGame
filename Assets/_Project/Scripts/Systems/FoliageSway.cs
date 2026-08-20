using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 나무를 밑동에서 통째로 기울여 바람에 흔든다.
    ///
    /// 왜 필요한가: 전역 바람(WindSystem)이 생긴 뒤로 잔디도, 해초도, 파도도, 빗줄기도 같은 바람을
    /// 탄다. 그런데 **야자수만 미동도 하지 않는다.** 폭풍이 몰아쳐 풀이 눕는 화면에서 나무가 못처럼
    /// 박혀 있으면, 잘 만든 잔디가 오히려 나무의 정지를 드러낸다. 장면 전체가 한 바람을 타야 한다는
    /// 것이 조사에서 날씨 영역이 준 결론이었다(Docs/RealismPlan.md).
    ///
    /// ★ 왜 셰이더가 아니라 트랜스폼인가.
    ///   잔디는 우리가 만든 셰이더(MGGrass)라 정점을 마음대로 밀 수 있지만, 야자수는 URP Lit이다.
    ///   정점 흔들림을 넣으려면 URP Lit을 대체하는 셰이더를 써야 하고 그러면 그림자·라이팅·인스턴싱을
    ///   전부 다시 만들어야 한다. 반면 야자수는 밑동이 피벗이고(CreatePalm이 groundPosition에 뿌리를
    ///   두고 자식을 로컬 원점에서 쌓는다) **뿌리를 기울이면 나무 전체가 밑동에서 휘어진다** -
    ///   실제 야자수가 흔들리는 방식과 같다. 트랜스폼 하나로 끝난다.
    ///
    /// ★ 왜 자기 Update가 없는가.
    ///   섬 50개에 야자수가 수백~수천 그루다. 그루마다 Update를 돌리면 그 자체가 프레임 비용이다.
    ///   대신 명부에만 올라가 있고, <see cref="WindSystem"/>이 **카메라 근처의 것들만** 골라
    ///   매 프레임 흔든다. 멀리 있는 나무는 어차피 몇 픽셀이라 흔들려도 보이지 않는다.
    /// </summary>
    public class FoliageSway : MonoBehaviour
    {
        /// <summary>이 그루가 흔들리는 최대 각(도). 큰 나무일수록 작게(뻣뻣하게) 준다.</summary>
        public float swayDegrees = 2.4f;

        /// <summary>그루마다 다른 흔들림 타이밍. 같으면 숲 전체가 한 몸처럼 움직여 즉시 가짜로 보인다.</summary>
        public float phase;

        /// <summary>흔들기 전의 원래 회전. 여기서부터 매번 다시 계산한다(누적하면 나무가 쓰러진다).</summary>
        private Quaternion baseRotation;

        private static readonly List<FoliageSway> registry = new List<FoliageSway>();

        /// <summary>
        /// 중복 등록을 O(1)에 거르는 집합. 리스트를 선형 스캔하면 **섬 생성이 O(n²)** 이 된다 -
        /// 명부는 월드 전체가 공유하고 정글 특대 섬 하나에 야자수가 130그루쯤, 섬이 50개면
        /// 수천 그루다. 그루마다 "지금까지 만든 전부"를 훑으면 로딩이 눈에 띄게 걸린다.
        /// </summary>
        private static readonly HashSet<FoliageSway> registered = new HashSet<FoliageSway>();

        /// <summary>명부 전체. WindSystem이 읽는다.</summary>
        public static IReadOnlyList<FoliageSway> All => registry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            registry.Clear();
            registered.Clear();
        }

        private void Awake()
        {
            baseRotation = transform.localRotation;

            // 위치에서 위상을 만든다 - 난수를 쓰면 섬 생성의 rng 스트림을 건드려 배치가 밀린다
            // (이 프로젝트의 [결정성] 전제. IslandMeshGenerator.Vegetation 파일 상단 주석 참고).
            Vector3 p = transform.position;
            phase = Mathf.Repeat(p.x * 0.7311f + p.z * 1.3733f, 6.2832f);
        }

        private void OnEnable()
        {
            if (registered.Add(this))
                registry.Add(this);
        }

        private void OnDisable()
        {
            if (registered.Remove(this))
                registry.Remove(this);

            // 꺼질 때는 원래 자세로 되돌린다. 기울어진 채로 굳으면 섬을 다시 켰을 때 기운 나무가 남는다.
            transform.localRotation = baseRotation;
        }

        /// <summary>
        /// 이번 프레임의 바람으로 자세를 정한다. WindSystem이 고른 그루에 대해서만 불린다.
        /// </summary>
        /// <param name="windDir">바람 방향(월드 XZ, 정규화)</param>
        /// <param name="strength">바람 세기(1 = 산들바람)</param>
        /// <param name="windPhase">바람 누적 위상(WindSystem.Phase)</param>
        internal void ApplySway(Vector2 windDir, float strength, float windPhase)
        {
            // 흔들림 2겹. 주기가 서로 나누어떨어지지 않아 합성 주기가 길다(같은 흔들림이 반복되지 않는다).
            float wobble =
                0.58f * Mathf.Sin(windPhase * 0.9f + phase) +
                0.42f * Mathf.Sin(windPhase * 1.63f + phase * 1.7f);

            // 눕는 성분: 세기가 0.7을 넘으면 바람 쪽으로 기운 채로 흔들린다. 잔디의 lean과 같은 발상이고,
            // 이게 없으면 폭풍이 "빨리 흔들리는 잔잔한 바람"으로만 보인다.
            float lean = Mathf.Max(0f, strength - 0.7f) * swayDegrees * 0.9f;

            float angle = wobble * swayDegrees * Mathf.Clamp(strength, 0.35f, 2.2f) + lean;

            // 바람 방향으로 기울이는 회전축은 바람에 **수직인 수평축**이다.
            // (dz, 0, -dx)를 축으로 양의 각을 주면 꼭대기가 바람 방향으로 간다.
            var axis = new Vector3(windDir.y, 0f, -windDir.x);
            if (axis.sqrMagnitude < 0.0001f)
                return;

            // 축은 **월드** 바람에서 나왔는데 아래에서 곱하는 것은 로컬 회전이다. 지금은 섬 뿌리가
            // 전부 identity라 둘이 같지만, 나중에 섬을 회전시켜 배치하면 조용히 어긋난다.
            // 부모 공간으로 한 번 옮겨 두면 그 가정 자체가 필요 없어진다(검수 지적).
            Transform parent = transform.parent;
            if (parent != null)
                axis = parent.InverseTransformDirection(axis);

            transform.localRotation = Quaternion.AngleAxis(angle, axis) * baseRotation;
        }
    }
}
