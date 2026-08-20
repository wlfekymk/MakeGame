using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 비가 오면 **세상이 젖는다.** 지금까지 젖음(_MG_Wetness)을 읽는 것은 모래(MGShoreline)와
    /// 바다(MGOcean 파문)뿐이었다. 그래서 폭우가 쏟아져도 오두막 지붕도, 바위도, 통나무도,
    /// 뗏목 갑판도 뽀송뽀송했다 - "젖는 세계"라는 설계가 절반만 구현돼 있었다
    /// (Docs/RealismPlan.md E1).
    ///
    /// ★ 왜 셰이더가 아니라 머티리얼인가.
    ///   이 프로젝트의 구조물·자원·바위는 전부 런타임에 만든 **URP Lit** 머티리얼을 쓴다. 젖음을
    ///   셰이더에서 처리하려면 URP Lit을 대체하는 커스텀 셰이더를 써야 하고, 그러면 그림자 수신·
    ///   라이팅·인스턴싱을 전부 우리가 다시 만들어야 한다. 반면 젖음이 하는 일은 물리적으로 두 가지뿐이다:
    ///     · 표면이 어두워진다(물막이 빛을 가둔다)
    ///     · 매끈해진다(물막이 미세 요철을 메운다 → 반사가 또렷해진다)
    ///   둘 다 URP Lit이 이미 가진 프로퍼티(_BaseColor / _Smoothness)로 표현된다. 그러면 셰이더를
    ///   건드릴 이유가 없다.
    ///
    /// ★ 마른 값을 등록 시점에 저장하는 것이 이 클래스의 핵심이다.
    ///   젖음을 "지금 색에 곱하는" 방식으로 만들면 프레임마다 곱이 누적돼 세상이 영영 검어진다.
    ///   항상 **저장해 둔 마른 값에서 다시 계산한다.** 그래서 wetness가 0으로 돌아오면 등록 당시와
    ///   비트 단위로 같은 값이 복원된다.
    ///
    /// 등록 지점은 <see cref="StructureVisualBuilder.CreateColorMaterial"/> 한 곳이다. 이 게임의
    /// 프리미티브 시각 파츠(쉼터·물증류기·모닥불·바위·통나무·자원·위험요소…)가 전부 그 메서드를
    /// 거치므로, 거기 한 줄이면 세계 전체가 젖는다. 생물(CreatureVisualBuilder)은 자체 경로로
    /// 머티리얼을 만들어 자동으로 빠진다 - 털이 젖어 번들거리는 것은 별개 문제라 지금은 다루지 않는다.
    /// </summary>
    public static class SurfaceWetness
    {
        /// <summary>완전히 젖었을 때 원래 색에 곱하는 값. 젖은 나무·바위는 눈에 띄게 어두워진다.</summary>
        private const float WetDarken = 0.70f;

        /// <summary>완전히 젖었을 때의 매끈함. 마른 값(보통 0.1 안팎)에서 여기까지 올라간다.</summary>
        private const float WetSmoothness = 0.62f;

        /// <summary>
        /// 이만큼 달라져야 다시 칠한다. 소나기가 시작될 때 젖음이 초당 0.4씩 오르므로, 0.02면
        /// 초당 20회(3프레임에 한 번) 명부 전체를 훑게 된다 - 등록 수가 수백을 넘으면 그 자체가
        /// 히치다. 0.05면 그 빈도가 2.5배 줄고, 남는 오차는 색으로 보이지 않는다.
        /// </summary>
        private const float ApplyThreshold = 0.05f;

        /// <summary>파괴된 머티리얼을 걷어내는 주기(프레임). 맑은 날에도 명부가 자라기만 하는 것을 막는다.</summary>
        private const int PruneIntervalFrames = 600;

        private sealed class Entry
        {
            public Material material;
            public Color dryColor;
            public float drySmoothness;
            public bool hasSmoothness;
        }

        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>
        /// 중복 등록을 O(1)에 거르기 위한 집합. 리스트를 선형 스캔하면 등록 n건에 O(n²)이 된다
        /// (이 게임은 파츠마다 머티리얼을 만드는 경로가 있어 n이 수백~수천까지 간다).
        /// </summary>
        private static readonly HashSet<Material> registered = new HashSet<Material>();

        private static float appliedWetness = -1f;
        private static int lastPruneFrame;

        /// <summary>지금 적용돼 있는 젖음(0~1). 디버그·검증용.</summary>
        public static float AppliedWetness => appliedWetness;

        /// <summary>등록된 머티리얼 수. 디버그·검증용.</summary>
        public static int RegisteredCount => entries.Count;

        /// <summary>
        /// 씬을 다시 로드하면 머티리얼은 전부 새로 만들어진다. 옛 명부를 남겨 두면 파괴된
        /// 머티리얼을 계속 붙들고 있게 되므로 비운다(프로젝트 공통 R1 리셋 훅).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            entries.Clear();
            registered.Clear();
            appliedWetness = -1f;
            lastPruneFrame = 0;
        }

        /// <summary>
        /// 머티리얼을 젖음 명부에 올린다. **마른 값을 만든 직후에 부를 것** - 이미 젖음이 적용된
        /// 뒤에 부르면 그 젖은 값이 "마른 값"으로 굳어 비가 그쳐도 안 마른다.
        /// 같은 머티리얼을 두 번 올려도 안전하다(무시한다).
        /// </summary>
        public static void Register(Material material)
        {
            if (material == null)
                return;

            if (!registered.Add(material))
                return;

            var entry = new Entry
            {
                material = material,
                dryColor = material.color,
                hasSmoothness = material.HasProperty("_Smoothness"),
            };
            entry.drySmoothness = entry.hasSmoothness ? material.GetFloat("_Smoothness") : 0f;

            entries.Add(entry);

            // 이미 젖어 있는 상태에서 새로 생긴 것(비 오는 중에 지은 오두막)은 즉시 젖은 모습이어야 한다.
            if (appliedWetness > 0f)
                ApplyTo(entry, appliedWetness);
        }

        /// <summary>
        /// 젖음을 세계에 반영한다. RainWetness가 매 프레임 부르지만, 실제로 칠하는 것은
        /// 값이 ApplyThreshold 넘게 달라졌을 때뿐이다.
        /// </summary>
        public static void Apply(float wetness)
        {
            wetness = Mathf.Clamp01(wetness);

            // ★ 끝점(0과 1)은 임계와 무관하게 반드시 적용한다. 이게 없으면 젖음이 정확히 0으로
            //   돌아와도 appliedWetness가 0.04쯤에 남아 세계가 영영 살짝 어둡고 살짝 번들거린다.
            //   "마르면 등록 당시 값이 그대로 복원된다"는 이 클래스의 계약이 거기서 깨진다.
            bool endpoint = (wetness <= 0f || wetness >= 1f) && wetness != appliedWetness;

            if (!endpoint && appliedWetness >= 0f && Mathf.Abs(wetness - appliedWetness) < ApplyThreshold)
            {
                PruneOccasionally();
                return;
            }

            appliedWetness = wetness;

            // 파괴된 머티리얼(씬 전환 잔재)은 이 참에 명부에서 걷어낸다.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (entry.material == null)
                {
                    // ★ 두 컬렉션을 **함께** 지워야 한다. 여기서 entries만 지우면 그 엔트리는
                    //   PruneOccasionally의 시야에서도 사라져 registered에 남은 참조를 영영 못 지운다
                    //   (검수에서 잡힌 누수 - dedup 집합만 계속 자란다).
                    registered.Remove(entry.material);
                    entries.RemoveAt(i);
                    continue;
                }

                ApplyTo(entry, wetness);
            }
        }

        /// <summary>
        /// 맑은 날에도 가끔은 명부를 훑어 파괴된 머티리얼을 걷어낸다. Apply가 조기 반환하는 동안
        /// 프루닝이 한 번도 안 돌면 명부는 자라기만 한다.
        /// </summary>
        private static void PruneOccasionally()
        {
            if (Time.frameCount - lastPruneFrame < PruneIntervalFrames)
                return;

            lastPruneFrame = Time.frameCount;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].material == null)
                {
                    registered.Remove(entries[i].material);
                    entries.RemoveAt(i);
                }
            }
        }

        private static void ApplyTo(Entry entry, float wetness)
        {
            Color dry = entry.dryColor;
            float darken = Mathf.Lerp(1f, WetDarken, wetness);

            // ★ 알파는 **지금 값을 그대로 유지한다.** 등록 시점의 dry.a를 다시 써 넣으면 안 된다 -
            //   건축 미리보기 고스트는 CreateColorMaterial(= 등록) 직후에 알파를 0.38로 낮추므로,
            //   등록 알파(1.0)를 되쓰면 비가 오는 순간 미리보기가 불투명해져 실물과 구분이 안 된다.
            //   젖음이 알파에 관여할 이유도 없다.
            float alpha = entry.material.color.a;
            entry.material.color = new Color(dry.r * darken, dry.g * darken, dry.b * darken, alpha);

            if (entry.hasSmoothness)
                entry.material.SetFloat("_Smoothness", Mathf.Lerp(entry.drySmoothness, WetSmoothness, wetness));
        }
    }
}
