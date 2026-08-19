using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 전투의 "데이터 규약"을 한 곳에 모아 두는 정적 유틸리티. MonoBehaviour가 아니고 씬에도 없다.
    ///
    /// 왜 필요한가: 이번 배치에서 전투에 두 가지가 새로 생겼는데 둘 다 ItemData에 필드가 없다.
    ///  (1) **투척 피해량** — ItemData에는 weaponDamage(근접) 하나뿐이라 던졌을 때의 값이 없다.
    ///  (2) **정제(refined) 등급** — ScriptableObject 에셋 생성은 이 작업의 락 밖이라(아이템/레시피는
    ///      다음 웨이브 담당) 새 필드를 만들어도 채울 사람이 없다.
    /// 둘 다 **ItemData.itemName 문자열 규약**으로 판정한다. 이 프로젝트가 이미 같은 방식을 쓰고 있다
    /// (PlayerController.SwimFinsItemName "오리발" / BuildPieceCatalog의 재료 이름 대조 - AGENT_BRIEF 3장).
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    ///  ★ 다음 담당(game-designer)이 그대로 만들면 되는 이름 규약과 수치표 ★
    ///
    ///  이름 규약: 기본 이름 앞에 **"정제 "**(한글 '정제' + 공백 한 칸)를 붙인다.
    ///            → "정제 칼" · "정제 손도끼" · "정제 창"  (에셋 파일명은 Item_정제칼.asset 처럼 공백 없이)
    ///
    ///  | itemName   | isWeapon | weaponDamage | maxUses | 투척 가능 | 투척 피해 | 채집 배율 |
    ///  |------------|----------|--------------|---------|-----------|-----------|-----------|
    ///  | 칼         | 1        | 8            | -1      | 아니오    | -         | 1.0       |
    ///  | 손도끼     | 1        | 14           | 20      | 아니오    | -         | 1.0       |
    ///  | 창         | 1        | 18           | 15      | **예**    | **14**    | 1.0       |
    ///  | 정제 칼    | 1        | **12**       | -1      | 아니오    | -         | **2.0**   |
    ///  | 정제 손도끼| 1        | **20**       | **30**  | 아니오    | -         | **2.0**   |
    ///  | 정제 창    | 1        | **25**       | **25**  | **예**    | **20**    | 1.0       |
    ///
    ///  · 위 세 줄(칼/손도끼/창)은 **현재 에셋 실측값**이다(Item_칼/손도끼/창.asset). 건드리지 마라.
    ///  · 투척 피해는 에셋에 적는 값이 아니다 — 이 파일의 <see cref="ThrowDamageRatio"/>(0.78)로
    ///    weaponDamage에서 파생된다. 18 → 14, 25 → 20. 즉 **정제 창의 weaponDamage만 25로 정하면
    ///    투척 20은 자동으로 따라온다.**
    ///  · 채집 배율 2.0은 **0.2.51에서 ResourceNode.GetEffectiveYield에 배선됐다.** 수확량 계산의 단일 소스는
    ///    ResourceNode.GetEffectiveYield이고 그 파일은 이 작업의 락 밖이다. 지금 상태로도
    ///    ResourceNode에 이미 있는 (bonusTool, bonusYieldPerHarvest) 두 필드만으로 배선할 수 있다:
    ///    노드마다 bonusTool = 정제 칼, bonusYieldPerHarvest = yieldPerHarvest(= 기본 수확량)으로
    ///    두면 합이 정확히 2배가 된다. 자세한 계산은 <see cref="GetRefinedBonusYield"/> 참고.
    /// ─────────────────────────────────────────────────────────────────────────────
    ///
    /// 이 클래스는 상태를 갖지 않는다(static 캐시 없음 → R1 리셋 훅도 필요 없다). 문자열 비교만 하고
    /// Substring/Split을 쓰지 않으므로 호출해도 GC 할당이 0이다.
    /// </summary>
    public static class CombatSystem
    {
        // ── 이름 규약 ────────────────────────────────────────────────────────────

        /// <summary>정제 등급을 가리키는 접두어. 반드시 뒤에 공백 한 칸이 붙는다("정제 창").</summary>
        public const string RefinedPrefix = "정제 ";

        /// <summary>기본 칼(Item_칼.asset의 itemName).</summary>
        public const string KnifeItemName = "칼";

        /// <summary>기본 손도끼(Item_손도끼.asset의 itemName).</summary>
        public const string HatchetItemName = "손도끼";

        /// <summary>기본 창(Item_창.asset의 itemName).</summary>
        public const string SpearItemName = "창";

        /// <summary>정제 칼(다음 웨이브가 만들 에셋의 itemName).</summary>
        public const string RefinedKnifeItemName = RefinedPrefix + KnifeItemName;

        /// <summary>정제 손도끼(다음 웨이브가 만들 에셋의 itemName).</summary>
        public const string RefinedHatchetItemName = RefinedPrefix + HatchetItemName;

        /// <summary>정제 창(다음 웨이브가 만들 에셋의 itemName).</summary>
        public const string RefinedSpearItemName = RefinedPrefix + SpearItemName;

        // ── 수치 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 투척 피해 = 근접 피해 × 이 비율(반올림). 0.78인 이유: 창 18 → 14.04 → 14 로 감독이 지정한
        /// "근접보다 약간 낮게(예: 14)"에 정확히 떨어지고, 정제 창 25 → 19.5 → 20 으로 깔끔하게 맞는다.
        /// 던지면 무기가 손에서 떠나 회수하러 가야 하므로 피해가 근접보다 낮은 것이 정상이다.
        /// </summary>
        public const float ThrowDamageRatio = 0.78f;

        /// <summary>
        /// 정제 도구의 채집 수확 배율(Stranded Deep의 "정제 칼은 수확 2배" 대응).
        /// **0.2.51에서 ResourceNode에 배선됨** — 정제 칼/손도끼 소지 시 수확이 정확히 2배가 된다.
        /// </summary>
        public const float RefinedHarvestYieldMultiplier = 2f;

        // ── 판정 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 아이템이 정제 등급인지. 판정은 itemName 접두어 하나뿐이다(Ordinal 비교라 할당이 없다).
        /// 아직 정제 에셋이 없으므로 지금은 항상 false이며, 다음 웨이브가 에셋을 만드는 순간 켜진다.
        /// </summary>
        public static bool IsRefined(ItemData data)
        {
            if (data == null || string.IsNullOrEmpty(data.itemName))
                return false;

            return data.itemName.StartsWith(RefinedPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 이 아이템을 던질 수 있는지. 창 계열(기본/정제)만 던질 수 있다.
        /// isWeapon도 함께 확인한다 - 이름만 맞고 무기 플래그가 꺼진 에셋은 근접에서도 안 쓰이므로
        /// 던지기에서만 예외로 인정하면 두 경로의 판정이 갈라진다.
        /// </summary>
        public static bool IsThrowable(ItemData data)
        {
            if (data == null || !data.isWeapon || string.IsNullOrEmpty(data.itemName))
                return false;

            return data.itemName == SpearItemName || data.itemName == RefinedSpearItemName;
        }

        /// <summary>
        /// 이 무기를 던졌을 때의 피해량. weaponDamage에서 파생하므로 **에셋에 별도 필드가 필요 없다**.
        /// 반올림은 Mathf.Round(은행가 반올림)가 아니라 floor(x + 0.5)를 쓴다 - x.5가 항상 위로 가야
        /// 표(창 14 / 정제 창 20)와 코드가 어긋나지 않는다.
        /// </summary>
        public static float GetThrowDamage(ItemData data)
        {
            if (data == null)
                return 0f;

            return Mathf.Max(1f, Mathf.Floor(data.weaponDamage * ThrowDamageRatio + 0.5f));
        }

        /// <summary>
        /// 정제 도구를 들었을 때 ResourceNode.bonusYieldPerHarvest에 넣어야 할 **가산량**을 계산한다.
        /// ResourceNode의 보너스는 곱이 아니라 합(GetEffectiveYield = yieldPerHarvest + bonus)이므로,
        /// 2배를 만들려면 가산량이 기본 수확량과 같아야 한다(2 → +2 = 4).
        /// 이 값은 게임 규칙이 아니라 **에셋 설정을 계산해 주는 표**다.
        /// 0.2.51부터 ResourceNode.GetEffectiveYield가 이 표를 실제로 부른다.
        /// </summary>
        public static int GetRefinedBonusYield(int baseYieldPerHarvest)
        {
            if (baseYieldPerHarvest <= 0)
                return 0;

            return Mathf.Max(0, Mathf.RoundToInt(baseYieldPerHarvest * RefinedHarvestYieldMultiplier) - baseYieldPerHarvest);
        }

        // ── 인벤토리 조회 ────────────────────────────────────────────────────────

        /// <summary>
        /// 인벤토리에서 실제로 던질 창 하나를 고른다. 후보가 여럿이면 **피해량이 가장 높은 것**을 쓴다
        /// (HazardSource.FindBestWeapon / InteractionPromptUI.FindBestWeapon과 같은 규칙 - 근접과
        /// 원거리가 서로 다른 무기를 고르면 플레이어가 어느 것이 닳는지 예측할 수 없다).
        /// 피해량이 같으면 **남은 내구도가 적은 쪽**을 먼저 던진다 - 닳은 창부터 소모하는 편이
        /// 인벤토리에 반쯤 닳은 창이 여러 자루 쌓이는 것을 막는다.
        /// ItemData가 아니라 InventoryItem을 돌려줘야 그 한 자루의 내구도를 실제로 소모시킬 수 있다.
        /// </summary>
        public static InventoryItem FindThrowable(PlayerInventory inventory)
        {
            if (inventory == null)
                return null;

            InventoryItem best = null;
            var items = inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || !IsThrowable(item.data))
                    continue;

                if (best == null)
                {
                    best = item;
                    continue;
                }

                if (item.data.weaponDamage > best.data.weaponDamage)
                    best = item;
                else if (item.data.weaponDamage == best.data.weaponDamage
                    && !item.data.IsUnlimited && item.remainingUses < best.remainingUses)
                    best = item;
            }

            return best;
        }
    }
}
