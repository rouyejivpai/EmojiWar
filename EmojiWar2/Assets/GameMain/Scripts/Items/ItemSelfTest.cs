//------------------------------------------------------------
// EmojiWar GameMain - 物品事务自检（P2 建立 / P4c 按"法术槽属于手"重写）
//
// 需求口径（用户冻结）：
//   · 物品种类只有法杖与法术，两者都能进背包；
//   · 每个手部槽放一把法杖；法术槽数量由法杖决定、动态变化；
//   · **换杖时法术不随杖移动**（保留给新法杖）；只有"新杖槽数装不下的那部分"才挤回背包；
//   · 背包放不下溢出法术时，换杖整笔拒绝（原子）。
//
// 同一份自检被两条通道调用：编辑器菜单 + 运行时 -autoitems（构建版探针）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>自检报告收集器。</summary>
    public sealed class ItemSelfTestReport
    {
        private readonly List<string> m_Lines = new List<string>();

        public int Pass { get; private set; }
        public int Fail { get; private set; }

        public void Check(string name, bool ok, string detail)
        {
            if (ok) { Pass++; } else { Fail++; }
            m_Lines.Add("[ItemTest] " + (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " · " + detail));
        }

        public void Note(string text) { m_Lines.Add("[ItemTest]   " + text); }

        public string Text()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Lines.Count; i++) { sb.Append(m_Lines[i]).Append('\n'); }
            sb.Append("[ItemTest] passed=").Append(Pass).Append(" failed=").Append(Fail);
            return sb.ToString();
        }
    }

    /// <summary>背包/装备/商店事务自检。</summary>
    public static class ItemSelfTest
    {
        private const string Bag = "Backpack";
        private const string HandL = "Hand_L";
        private const string HandR = "Hand_R";
        private const string Shop = "Shop";
        private const string SpellsL = "HandSpells_L";

        public static string Run(IItemTable table)
        {
            var r = new ItemSelfTestReport();
            if (table == null) { r.Check("物品表可用", false, "table == null"); return r.Text(); }

            int wand1 = table.FindItemIdByWand(1);   // 学徒法杖（3 槽）
            int wand2 = table.FindItemIdByWand(2);   // 镜像长杖（5 槽）
            int fireCard = table.FindItemIdBySpell(101);   // 火花弹
            int iceCard = table.FindItemIdBySpell(102);    // 冰锥
            int upCard = table.FindItemIdBySpell(104);     // 节能符文
            int tripleCard = table.FindItemIdBySpell(107); // 快速充能
            r.Note(string.Format("cards: wand1={0} wand2={1} spark={2} ice={3} rune={4} quick={5}",
                wand1, wand2, fireCard, iceCard, upCard, tripleCard));
            if (wand1 <= 0 || wand2 <= 0 || fireCard <= 0 || iceCard <= 0 || upCard <= 0 || tripleCard <= 0)
            {
                r.Check("物品卡齐备（两把法杖 + 四张法术）", false, "缺少物品卡，先跑 Build Spell Content (S1)");
                return r.Text();
            }

            var svc = new InventoryService(table);
            svc.RegisterContainer(Bag, SlotKind.Backpack, ConfigService.BackpackCapacity, "背包");
            svc.RegisterContainer(HandL, SlotKind.WandHand, 1, "左手");
            svc.RegisterContainer(HandR, SlotKind.WandHand, 1, "右手");
            svc.RegisterContainer(Shop, SlotKind.Shop, ConfigService.ShopCapacity, "商店货架", 0, true);
            svc.EnsureHandSpellContainers();

            var pack = svc.GetContainer(Bag);
            var handLeft = svc.GetContainer(HandL);
            var spells = svc.GetContainer(SpellsL);

            // 1) 放入 + 堆叠
            // 1) 放入并占格（**法术卡不可堆叠**，P4：StackMax=1 → 每次占一个新格）
            var a1 = svc.TryAdd(Bag, fireCard, 1);
            r.Check("放入并占格（法术卡不可堆叠）", a1.Ok && pack.Get(0).ItemId == fireCard && pack.Get(0).Count == 1,
                "result=" + a1 + " slot0=" + pack.Get(0));

            var a2 = svc.TryAdd(Bag, fireCard, 1);
            r.Check("法术卡不可堆叠：第二张占新格（不会被并进同格）",
                a2.Ok && pack.Get(0).Count == 1 && pack.Get(1).ItemId == fireCard && pack.Get(1).Count == 1,
                "slot0=" + pack.Get(0) + " slot1=" + pack.Get(1));

            // 3) 手上没杖时，法术槽不可用（可用槽数=0，拖进去必须被拒）
            var noWand = svc.TryAdd(Bag, iceCard, 1);
            int iceIdx = -1;
            for (int i = 0; i < pack.Capacity; i++) { if (pack.Get(i).ItemId == iceCard) { iceIdx = i; break; } }
            var intoEmptyHandSlots = svc.TryMove(new SlotRef(Bag, iceIdx), new SlotRef(SpellsL, 0));
            r.Check("没杖时法术槽不可用（可用槽数 0）",
                noWand.Ok && !intoEmptyHandSlots.Ok && intoEmptyHandSlots.Reason == MoveReason.IndexInvalid,
                "move=" + intoEmptyHandSlots + " spells=" + spells.Dump());

            // 4) 装备法杖（3 槽）：法术槽可用槽数随杖变化
            var wandA = svc.CreateWandInstance(wand1);
            svc.TryAdd(Bag, wand1, 1, wandA.InstanceId);
            var wandSlotInBag = new SlotRef(Bag, pack.IndexOfInstance(wandA.InstanceId));
            var equipA = svc.TryEquipWand(wandSlotInBag, HandL);
            r.Check("装备法杖（槽数随杖：3 槽 + 出厂装填随杖进袋）",
                equipA.Ok && !handLeft.Get(0).IsEmpty && spells.ActiveCapacity == 3,
                "equip=" + equipA + " hand=" + handLeft.Get(0) + " spells=" + spells.Dump());

            // 5) 装填法术：背包法术卡 → 左手法术槽（实体卡）
            int iceBefore = pack.CountOf(iceCard);
            var load = svc.TryLoadSpell(new SlotRef(Bag, iceIdx), new SlotRef(SpellsL, 0));
            r.Check("装填法术并从背包移出（实体卡）",
                load.Ok && spells.Get(0).ItemId == iceCard && pack.CountOf(iceCard) == iceBefore - 1,
                "load=" + load + " spells=" + spells.Dump());

            // 6) 换到 5 槽法杖：法术**不随杖移动**，原地保留，槽数变 5
            var wandB = svc.CreateWandInstance(wand2);
            svc.TryAdd(Bag, wand2, 1, wandB.InstanceId);
            var wandBSlot = new SlotRef(Bag, pack.IndexOfInstance(wandB.InstanceId));
            var equipB = svc.TryEquipWand(wandBSlot, HandL);
            r.Check("换成 5 槽法杖：法术保留原槽 + 槽数变 5",
                equipB.Ok && spells.ActiveCapacity == 5 && spells.Get(0).ItemId == iceCard,
                "equip=" + equipB + " active=" + spells.ActiveCapacity + " spells=" + spells.Dump());

            // 7) 装满 5 槽，再换回 3 槽杖：装不下的 2 张挤回背包，前 3 张保留
            svc.TryLoadSpell(new SlotRef(Bag, BagIndexOrMinus1(svc, fireCard)), new SlotRef(SpellsL, 1));
            svc.TryAdd(Bag, upCard, 1);
            svc.TryLoadSpell(new SlotRef(Bag, BagIndexOrMinus1(svc, upCard)), new SlotRef(SpellsL, 2));
            svc.TryAdd(Bag, tripleCard, 1);
            svc.TryLoadSpell(new SlotRef(Bag, BagIndexOrMinus1(svc, tripleCard)), new SlotRef(SpellsL, 3));
            svc.TryAdd(Bag, fireCard, 1);
            svc.TryLoadSpell(new SlotRef(Bag, BagIndexOrMinus1(svc, fireCard)), new SlotRef(SpellsL, 4));
            int loadedBefore = spells.ActiveCapacity;
            int packedCount = 0;
            for (int i = 0; i < spells.Capacity; i++) { if (!spells.Get(i).IsEmpty) { packedCount++; } }

            svc.TryAdd(Bag, wand1, 1);                                  // 放进背包准备换装
            int bagItemsBefore = BackpackItemCount(pack);               // 基准：含这把待换的杖
            var wand1Again = new SlotRef(Bag, BagIndexOrMinus1(svc, wand1));
            var equipBackTo3 = svc.TryEquipWand(wand1Again, HandL);
            int afterCount = 0;
            for (int i = 0; i < spells.Capacity; i++) { if (!spells.Get(i).IsEmpty) { afterCount++; } }
            int bagItemsAfter = BackpackItemCount(pack);
            r.Check("换成 3 槽法杖：装不下的法术挤回背包、其余保留",
                equipBackTo3.Ok && spells.ActiveCapacity == 3 && afterCount == 3
                && bagItemsAfter == bagItemsBefore + (packedCount - 3),
                "equip=" + equipBackTo3 + " active=" + spells.ActiveCapacity
                + " loadedBefore=" + packedCount + " loadedAfter=" + afterCount
                + " bagItems " + bagItemsBefore + "→" + bagItemsAfter);

            // 8) 违规与边界：法杖→法术槽 / 法术→手部 / 商店只读 / 超出可用槽数
            var badWand = svc.TryMove(new SlotRef(HandL, 0), new SlotRef(SpellsL, 0));
            var badSpell = svc.TryMove(new SlotRef(SpellsL, 0), new SlotRef(HandR, 0));
            var badShop = svc.TryMove(new SlotRef(HandL, 0), new SlotRef(Shop, 0));
            var beyondActive = svc.TryMove(new SlotRef(Bag, BagIndexOrMinus1(svc, fireCard)), new SlotRef(SpellsL, 6));
            r.Check("规则校验（法杖≠法术槽 / 法术≠手部 / 商店只读 / 超出可用槽拒）",
                !badWand.Ok && badWand.Reason == MoveReason.NotSpell
                && !badSpell.Ok && badSpell.Reason == MoveReason.NotWand
                && !badShop.Ok && badShop.Reason == MoveReason.ReadOnlySlot
                && !beyondActive.Ok && (beyondActive.Reason == MoveReason.IndexInvalid || beyondActive.Reason == MoveReason.EmptySource),
                "wandIntoSpell=" + badWand + " spellIntoHand=" + badSpell + " intoShop=" + badShop + " beyond=" + beyondActive);

            // 9) 再换回 5 槽法杖（法术保留）→ 空出槽 3/4 → 单张法术卡拖入空槽
            svc.TryAdd(Bag, wand2, 1);
            var wand2Again = new SlotRef(Bag, BagIndexOrMinus1(svc, wand2));
            var equipAgain5 = svc.TryEquipWand(wand2Again, HandL);
            bool keptAfterReEquip = spells.Get(0).ItemId == iceCard;
            int cardIdx = -1, emptySpellIdx = -1;
            for (int i = 0; i < pack.Capacity; i++) { if (pack.Get(i).ItemId == fireCard) { cardIdx = i; break; } }
            for (int i = 0; i < spells.ActiveCapacity; i++) { if (spells.Get(i).IsEmpty) { emptySpellIdx = i; break; } }
            var oneCard = (cardIdx >= 0 && emptySpellIdx >= 0)
                ? svc.TryMove(new SlotRef(Bag, cardIdx), new SlotRef(SpellsL, emptySpellIdx))
                : ItemMoveResult.Fail(MoveReason.NothingToDo);
            r.Check("再次换杖法术保留 + 法术卡拖入空槽（P4：每次只搬 1 张）",
                equipAgain5.Ok && spells.ActiveCapacity == 5 && keptAfterReEquip
                && oneCard.Ok && oneCard.MovedCount == 1 && spells.Get(emptySpellIdx).Count == 1
                && pack.Get(cardIdx).IsEmpty,
                "equip=" + equipAgain5 + " active=" + spells.ActiveCapacity + " move=" + oneCard
                + " slot" + emptySpellIdx + "=" + spells.Get(emptySpellIdx) + " src=" + pack.Get(cardIdx));

            // 10) 分堆：法术卡不可堆叠（StackMax=1）→ 分堆无意义，预期被拒
            int bigIdx = -1, freeIdx = -1;
            for (int i = 0; i < pack.Capacity; i++) { if (pack.Get(i).ItemId == fireCard) { bigIdx = i; break; } }
            freeIdx = pack.FirstEmptyIndex();
            var split = (bigIdx >= 0 && freeIdx >= 0)
                ? svc.TrySplit(new SlotRef(Bag, bigIdx), new SlotRef(Bag, freeIdx))
                : ItemMoveResult.Fail(MoveReason.NothingToDo);
            r.Check("法术卡不可堆叠 → 分堆被拒（P4：StackMax=1，无堆可分）",
                !split.Ok,
                "split=" + split + " src=" + pack.Get(bigIdx));

            // 11) 满仓整笔失败（不留半成品）
            var tiny = svc.RegisterContainer("Tiny", SlotKind.Backpack, 2, "测试用小背包");
            var t1 = svc.TryAdd("Tiny", fireCard, 1);
            var t2 = svc.TryAdd("Tiny", iceCard, 1);
            var t3 = svc.TryAdd("Tiny", upCard, 3);
            r.Check("满仓整笔失败（不留半成品）",
                t1.Ok && t2.Ok && !t3.Ok && t3.Reason == MoveReason.Full
                && tiny.Get(0).ItemId == fireCard && tiny.Get(1).ItemId == iceCard,
                "t3=" + t3 + " tiny=" + tiny.Dump());

            // 12) 买卖
            var shop = svc.GetContainer(Shop);
            shop.Set(0, ItemInstance.Of(fireCard, 1));
            var wallet = new SimpleWallet(100);
            int price = table.GetItem(fireCard).Price;
            int fireBefore = pack.CountOf(fireCard);
            var buy = svc.TryBuy(Shop, 0, wallet);
            r.Check("购买（扣钱 + 进背包 + 下架）",
                buy.Ok && wallet.Coin == 100 - price && shop.Get(0).IsEmpty && pack.CountOf(fireCard) == fireBefore + 1,
                "buy=" + buy + " coin=" + wallet.Coin);

            shop.Set(1, ItemInstance.Of(fireCard, 1));
            var poor = new SimpleWallet(1);
            var buyPoor = svc.TryBuy(Shop, 1, poor);
            r.Check("钱不够拒绝购买（且不扣钱/不下架）",
                !buyPoor.Ok && buyPoor.Reason == MoveReason.NotEnoughCoin && poor.Coin == 1 && !shop.Get(1).IsEmpty,
                "buyPoor=" + buyPoor + " coin=" + poor.Coin);

            int sellIdx = BagIndexOrMinus1(svc, fireCard);
            int sellCount = sellIdx >= 0 ? pack.Get(sellIdx).Count : 0;
            int coinBeforeSell = wallet.Coin;
            var sell = sellIdx >= 0 ? svc.TrySell(new SlotRef(Bag, sellIdx), wallet) : ItemMoveResult.Fail(MoveReason.NothingToDo);
            r.Check("出售（整叠回钱 + 物品移除）",
                sell.Ok && sell.MovedCount == sellCount && wallet.Coin == coinBeforeSell + table.GetItem(fireCard).SellPrice * sellCount,
                "sell=" + sell + " coin=" + wallet.Coin + " count=" + sellCount);

            // 13) 编译：按"手"编译（此处手上是 5 槽法杖）
            var program = LoadoutCompiler.CompileHand(HandL, table, svc);
            r.Check("编译 CastProgram（按手：杖 Id/槽数/已装填）",
                program.IsValid && program.SlotCount == 5 && program.WandId == 2 && program.LoadedCount >= 3,
                program.Dump());

            var snapA = LoadoutCompiler.CompileLoadout(handLeft.Get(0), ItemInstance.Empty, table, svc, HandL);
            var snapB = LoadoutCompiler.CompileLoadout(handLeft.Get(0), ItemInstance.Empty, table, svc, HandL);
            svc.TryRemove(new SlotRef(SpellsL, 0), 0);
            var snapC = LoadoutCompiler.CompileLoadout(handLeft.Get(0), ItemInstance.Empty, table, svc, HandL);
            r.Check("编译确定性（同装填同哈希 / 改装填变哈希）",
                snapA.Hash == snapB.Hash && snapA.Hash != snapC.Hash,
                "A=0x" + snapA.Hash.ToString("X16") + " C=0x" + snapC.Hash.ToString("X16"));

            r.Check("空手编译安全（返回 Empty 而非异常）",
                !LoadoutCompiler.CompileHand(HandR, table, svc).IsValid, "HandR 空手");

            // ================================================================
            // 14) S3 / D14：临时 Buff 挂在**物品实例**上、随卡移动（P5）
            //     用独立的 service 实例，避免受上文事务状态影响
            // ================================================================
            {
                BuffLimits lim = BuffLimits.Factory;
                var bdef = MakeBuff("buff_d14", BuffStat.ManaCost, 2f, 3, 5);
                BuffSet two = BuffRuntime.Apply(BuffRuntime.Apply(BuffSet.Empty, bdef, lim), bdef, lim);   // 2 层

                var svc2 = new InventoryService(table);
                svc2.RegisterContainer(Bag, SlotKind.Backpack, ConfigService.BackpackCapacity, "背包");
                svc2.RegisterContainer(HandL, SlotKind.WandHand, 1, "左手");
                svc2.EnsureHandSpellContainers();
                var pack2 = svc2.GetContainer(Bag);
                var spells2 = svc2.GetContainer(SpellsL);

                // 3 槽法杖上手 → 法术槽可用
                var w = svc2.CreateWandInstance(wand1);
                svc2.TryAdd(Bag, wand1, 1, w.InstanceId);
                svc2.TryEquipWand(new SlotRef(Bag, pack2.IndexOfInstance(w.InstanceId)), HandL);

                // 背包放一张挂了 2 层 buff 的火花弹
                svc2.TryAdd(Bag, fireCard, 1);
                int ci = BagIndexOrMinus1(svc2, fireCard);
                pack2.Set(ci, pack2.Get(ci).WithBuffs(two));
                r.Check("D14：物品实例可挂 Buff（HasBuffs / 层数 / 显示）",
                    pack2.Get(ci).HasBuffs && pack2.Get(ci).Buffs.Count == 1 && pack2.Get(ci).Buffs.At(0).Stacks == 2,
                    pack2.Get(ci).ToString());

                // (a) 装填进法术槽：buff 随卡进入
                var loaded = svc2.TryMove(new SlotRef(Bag, ci), new SlotRef(SpellsL, 0));
                r.Check("D14：装填进法术槽后 buff 随卡进入（2 层）",
                    loaded.Ok && spells2.Get(0).ItemId == fireCard && spells2.Get(0).HasBuffs
                    && spells2.Get(0).Buffs.Count == 1 && spells2.Get(0).Buffs.At(0).Stacks == 2,
                    "load=" + loaded + " spells=" + spells2.Dump());

                // (b) P5：拖回背包后 buff **保留**
                int freeBag = -1;
                for (int i = 0; i < pack2.Capacity; i++) { if (pack2.IsEmptyAt(i)) { freeBag = i; break; } }
                var back = freeBag >= 0
                    ? svc2.TryMove(new SlotRef(SpellsL, 0), new SlotRef(Bag, freeBag))
                    : ItemMoveResult.Fail(MoveReason.Full);
                r.Check("P5：法术卡拖回背包后 buff 保留（2 层）",
                    back.Ok && pack2.Get(freeBag).ItemId == fireCard && pack2.Get(freeBag).HasBuffs
                    && pack2.Get(freeBag).Buffs.At(0).Stacks == 2,
                    "back=" + back + " bag=" + pack2.Get(freeBag));

                // (c) 搬运**既有实例**必须保留 buff（裁槽挤回背包走的正是这条路）
                var svc3 = new InventoryService(table);
                svc3.RegisterContainer(Bag, SlotKind.Backpack, ConfigService.BackpackCapacity, "背包");
                var pack3 = svc3.GetContainer(Bag);
                svc3.TryAdd(Bag, fireCard, 1);
                int ci3 = BagIndexOrMinus1(svc3, fireCard);
                var carried = pack3.Get(ci3).WithBuffs(two);
                pack3.Set(ci3, ItemInstance.Empty);   // 先腾出格子，模拟"从别处搬来"

                var viaInstance = svc3.TryAddInstance(Bag, carried);
                r.Check("D14：TryAddInstance 搬运既有实例保留 buff（2 层）",
                    viaInstance.Ok && pack3.Get(0).HasBuffs && pack3.Get(0).Buffs.At(0).Stacks == 2,
                    "add=" + viaInstance + " slot0=" + pack3.Get(0));

                // 对照：TryAdd(itemId, count) 是"凭空造新物品"，**不含** buff
                var svc4 = new InventoryService(table);
                svc4.RegisterContainer(Bag, SlotKind.Backpack, ConfigService.BackpackCapacity, "背包");
                var pack4 = svc4.GetContainer(Bag);
                svc4.TryAdd(Bag, fireCard, 1);
                r.Check("D14：TryAdd(id,count) 造出的新物品无 buff（与 TryAddInstance 的区别）",
                    !pack4.Get(0).HasBuffs,
                    "slot0=" + pack4.Get(0));

                // (d) WithBuffs 值语义：改副本不影响容器里的原件
                var origin = pack2.Get(freeBag);
                var copy = origin.WithBuffs(BuffRuntime.Apply(origin.Buffs, bdef, lim));   // 副本 3 层
                r.Check("D14：WithBuffs 值语义（改副本后原件仍 2 层，不共享数组）",
                    copy.Buffs.At(0).Stacks == 3 && pack2.Get(freeBag).Buffs.At(0).Stacks == 2,
                    "copy=" + copy.Buffs.At(0).Stacks + " origin=" + pack2.Get(freeBag).Buffs.At(0).Stacks);

                // (e) ItemInstance.ToString 带 buff 信息（排查"buff 丢在哪一步"的探针依据）
                r.Check("D14：ToString 输出 buff 信息",
                    pack2.Get(freeBag).ToString().Contains("buff#"),
                    pack2.Get(freeBag).ToString());
            }

            return r.Text();
        }

        /// <summary>构造一条测试用 Buff 定义（S3/D14）。</summary>
        private static BuffInstance MakeBuff(string key, BuffStat stat, float perStack, int maxStacks, int duration)
        {
            CastBuffDef d = new CastBuffDef();
            d.KeyHash = CastBuffDef.HashKey(key);
            d.Stat = stat;
            d.ValuePerStack = perStack;
            d.MaxStacks = maxStacks;
            d.Timing = BuffTiming.ByTriggerCount;
            d.Duration = duration;
            d.StackRule = BuffStackRule.Additive;
            d.IsNegative = false;
            return BuffInstance.Create(d, BuffLimits.Factory);
        }

        private static int BagIndexOrMinus1(InventoryService svc, int itemId)
        {
            var bag = svc.GetContainer(Bag);
            if (bag == null) { return -1; }
            for (int i = 0; i < bag.Capacity; i++)
            {
                if (bag.Get(i).ItemId == itemId) { return i; }
            }
            return -1;
        }

        /// <summary>背包内物品总件数（含堆叠数量）。</summary>
        private static int BackpackItemCount(ItemContainer bag)
        {
            if (bag == null) { return 0; }
            int n = 0;
            for (int i = 0; i < bag.Capacity; i++)
            {
                var it = bag.Get(i);
                if (!it.IsEmpty) { n += it.Count; }
            }
            return n;
        }
    }
}
