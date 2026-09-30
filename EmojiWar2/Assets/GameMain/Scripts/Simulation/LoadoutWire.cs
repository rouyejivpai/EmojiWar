//------------------------------------------------------------
// EmojiWar GameMain - 装备 Id 的线格式（W-06）
//
// 为什么需要它：
//   报告 A10 / 根因 4：原先 `SimConfigFactory` 让**每一端各自读本机 ItemSystem** 编译 loadout，
//   而且 Host 是"用**本机的杖**为**所有玩家**编译"→ 只要任何玩家动过背包，
//   两端/多端就得到不同的 CastProgram，从第 1 帧起分叉。
//
//   关键约束：**没有背包同步机制**，所以 Host 根本不知道其他玩家装了什么。
//   因此正确做法是：
//     ① 每个玩家上报**自己**的装备 Id（法杖物品 Id + 各槽法术物品 Id，纯 int）；
//     ② Host 收齐后广播给所有端；
//     ③ **所有端（包括玩家自己）都用同一份 Id 表编译** → 逐位一致。
//
// 线格式（与既有 S2CShopOffer/S2CPlayerList 一致的"字符串传可变长数据"做法）：
//   "entityId:wandL:sp1,sp2,sp3:wandR:sp1,sp2;entityId:..."
//   · 空手 = wand 0 且槽表为空（如 `1001:0::0:`）
//   · 空法术槽 = 0
//   · 只传 Id，**不传任何资产引用/字符串名**（工程约定 §二：资产类字段一律用引用，
//     但网络消息只能传 Id —— 两端用同一份数据表把 Id 解析回资产）
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>单个玩家的装备 Id（法杖 + 该手各槽法术），联机下发与录像重放共用。</summary>
    public struct PlayerLoadoutIds
    {
        public int EntityId;
        public int WandLeftItemId;
        public int[] LeftSpellItemIds;
        public int WandRightItemId;
        public int[] RightSpellItemIds;

        public bool HasLeftHand { get { return WandLeftItemId > 0; } }
        public bool HasRightHand { get { return WandRightItemId > 0; } }
    }

    /// <summary>装备 Id 的编码/解码（无分配友好：Encode 复用 StringBuilder 由调用方持有）。</summary>
    public static class LoadoutWire
    {
        /// <summary>编码成线格式字符串。list 为空返回空串。</summary>
        public static string Encode(IList<PlayerLoadoutIds> list)
        {
            if (list == null || list.Count == 0) { return string.Empty; }

            var sb = new StringBuilder(128);
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) { sb.Append(';'); }
                PlayerLoadoutIds p = list[i];
                sb.Append(p.EntityId).Append(':').Append(p.WandLeftItemId).Append(':');
                AppendIds(sb, p.LeftSpellItemIds);
                sb.Append(':').Append(p.WandRightItemId).Append(':');
                AppendIds(sb, p.RightSpellItemIds);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 解码。结构不合法时返回 false 并清空 outList
        /// —— **宁可整批作废，也不要"半条"**（半条会让某个玩家用空 loadout 参战 = 静默分叉）。
        /// </summary>
        public static bool TryDecode(string text, List<PlayerLoadoutIds> outList)
        {
            if (outList == null) { return false; }
            outList.Clear();
            if (string.IsNullOrEmpty(text)) { return true; }

            string[] entries = text.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.IsNullOrEmpty(entries[i])) { continue; }

                string[] f = entries[i].Split(':');
                if (f.Length < 5)
                {
                    outList.Clear();
                    return false;
                }

                int entityId;
                if (!int.TryParse(f[0], out entityId))
                {
                    outList.Clear();
                    return false;
                }

                var p = new PlayerLoadoutIds
                {
                    EntityId = entityId,
                    WandLeftItemId = ParseInt(f[1], 0),
                    LeftSpellItemIds = ParseIds(f[2]),
                    WandRightItemId = ParseInt(f[3], 0),
                    RightSpellItemIds = ParseIds(f[4]),
                };
                outList.Add(p);
            }
            return true;
        }

        /// <summary>按 entityId 查找（找不到返回 false，调用方决定回落策略）。</summary>
        public static bool TryFind(List<PlayerLoadoutIds> list, int entityId, out PlayerLoadoutIds found)
        {
            found = default(PlayerLoadoutIds);
            if (list == null) { return false; }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].EntityId == entityId) { found = list[i]; return true; }
            }
            return false;
        }

        private static void AppendIds(StringBuilder sb, int[] ids)
        {
            if (ids == null) { return; }
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0) { sb.Append(','); }
                sb.Append(ids[i]);
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static int[] ParseIds(string s)
        {
            if (string.IsNullOrEmpty(s)) { return new int[0]; }
            string[] parts = s.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) { result[i] = ParseInt(parts[i], 0); }
            return result;
        }
    }
}
