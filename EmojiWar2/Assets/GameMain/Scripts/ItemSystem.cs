//------------------------------------------------------------
// EmojiWar GameMain - 物品系统运行时装配点（P3）
//
// 把 InventoryService 与标准容器挂到游戏里（单机与 Host 都有一份；客户端将来只读镜像）。
// 容器：Backpack(30) / Hand_L / Hand_R / Shop(6)。P4 的界面与 P5 的模拟接线都从这里取。
//------------------------------------------------------------

using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;
using UnityEngine;

namespace EmojiWar.GameMain
{
    /// <summary>物品系统运行时入口（服务 + 标准容器）。</summary>
    public static class ItemSystem
    {
        public static string BackpackId = "Backpack";
        public static string HandLeftId = "Hand_L";
        public static string HandRightId = "Hand_R";
        public static string ShopId = "Shop";

        /// <summary>背包容量（D33：来自 SpellSystemConfigSO，配置缺失才用出厂默认）。</summary>
        public static int BackpackCapacity { get { return ConfigService.BackpackCapacity; } }

        /// <summary>商店货架容量（D33：来自 SpellSystemConfigSO）。</summary>
        public static int ShopCapacity { get { return ConfigService.ShopCapacity; } }

        private static InventoryService s_Service = null;

        public static InventoryService Service
        {
            get
            {
                if (s_Service == null) { EnsureCreated(); }
                return s_Service;
            }
        }

        /// <summary>创建（或重建）服务与标准容器。</summary>
        public static InventoryService EnsureCreated()
        {
            if (s_Service != null) { return s_Service; }

            s_Service = new InventoryService(ConfigItemTable.Instance);
            s_Service.RegisterContainer(BackpackId, SlotKind.Backpack, BackpackCapacity, "背包");
            s_Service.RegisterContainer(HandLeftId, SlotKind.WandHand, 1, "左手");
            s_Service.RegisterContainer(HandRightId, SlotKind.WandHand, 1, "右手");
            s_Service.RegisterContainer(ShopId, SlotKind.Shop, ShopCapacity, "商店货架", 0, true);
            // 手部法术槽（槽数由手上的法杖决定，换杖时法术原地保留、装不下的挤回背包）
            s_Service.EnsureHandSpellContainers();
            return s_Service;
        }

        /// <summary>新一局/换场景时清空（P4/P5 调用）。</summary>
        public static void Reset()
        {
            s_Service = null;
        }

        /// <summary>
        /// 初始装备（D32 配置驱动；决策 #8：只靠商店 + 初始装备）：
        /// 两手各一把法杖 + 该手预填法术序列 + 背包初始物品卡，全部来自 `StartingLoadoutSO`。
        /// 配置缺失时才回落到"按物品卡查找"的兜底路径（绝不写死数值）。
        /// </summary>
        public static void GrantStartingLoadout()
        {
            var svc = Service;
            if (svc == null) { return; }

            var loadout = ConfigService.StartingLoadout;
            if (loadout == null)
            {
                Debug.LogWarning("[ItemSystem] StartingLoadout 配置缺失（D32）；本局不发放初始装备。");
                return;
            }

            // 左手 = 主武器（左键），右手 = 副武器（右键）；法术槽属于"手"
            GrantHand(svc, HandLeftId, loadout.LeftHand);
            GrantHand(svc, HandRightId, loadout.RightHand);

            // 背包初始物品卡（资产引用 → 物品卡 Id）
            var bag = loadout.BackpackItems;
            for (int i = 0; bag != null && i < bag.Length; i++)
            {
                if (bag[i] == null) { continue; }
                svc.TryAdd(BackpackId, bag[i].Id, 1);
            }
        }

        /// <summary>按配置给一只手发杖 + 预填法术序列（出厂装填只在初始装备时发生，P8）。</summary>
        private static void GrantHand(InventoryService svc, string handId, Data.StartingHand hand)
        {
            if (hand.Wand == null) { return; }   // 该手空手 → 不施法

            int cardId = svc.Table.FindItemIdByWand(hand.Wand.Id);
            if (cardId <= 0)
            {
                Debug.LogWarning("[ItemSystem] 初始法杖 Wand Id=" + hand.Wand.Id + " 没有对应物品卡，跳过。");
                return;
            }
            if (!GrantWandToHand(svc, handId, cardId)) { return; }

            var spells = svc.GetContainer(InventoryService.HandSpellContainerId(handId));
            if (spells == null) { return; }

            spells.ActiveCapacity = Mathf.Min(spells.Capacity, Mathf.Max(1, hand.Wand.SlotCount));
            for (int i = 0; hand.Spells != null && i < hand.Spells.Length && i < spells.ActiveCapacity; i++)
            {
                if (hand.Spells[i] == null) { continue; }
                int spellCard = svc.Table.FindItemIdBySpell(hand.Spells[i].Id);
                if (spellCard > 0) { spells.Set(i, ItemInstance.Of(spellCard, 1)); }
            }
            svc.ResyncHand(handId);   // 刷新杖上的法术快照（供编译/存档）
        }

        /// <summary>
        /// 给某只手发一把法杖（**不预填法术**：出厂装填的权威来源是 StartingLoadoutSO，P8）。
        /// 换杖也不会按 DefaultSpellIds 补齐空槽 —— 空槽就是"少一段序列"。
        /// </summary>
        private static bool GrantWandToHand(InventoryService svc, string handId, int wandItemId)
        {
            if (svc == null || wandItemId <= 0) { return false; }
            var hand = svc.GetContainer(handId);
            if (hand == null || !hand.Get(0).IsEmpty) { return false; }

            var item = svc.Table.GetItem(wandItemId);
            if (item == null || item.Category != ItemCategory.Wand || item.Wand == null) { return false; }

            var wand = svc.CreateWandInstance(wandItemId);
            if (wand.IsEmpty) { return false; }
            if (!svc.TryAdd(handId, wandItemId, 1, wand.InstanceId).Ok) { return false; }

            var spells = svc.GetContainer(InventoryService.HandSpellContainerId(handId));
            if (spells != null)
            {
                spells.ActiveCapacity = Mathf.Min(spells.Capacity, Mathf.Max(1, item.Wand.SlotCount));
            }
            return true;
        }
    }
}
