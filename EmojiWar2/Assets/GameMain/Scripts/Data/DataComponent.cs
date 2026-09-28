//------------------------------------------------------------
// EmojiWar GameMain - 数据组件（业务层数据访问门面）
// 数据源：ScriptableObject 资产（Resources/Data 下，每行一资产，Inspector 可编辑）。
// 运行时同步 Resources.LoadAll 加载；构建版自动进包（与实体/emoji 走 Resources 一致）。
// 替代旧 DataTable txt 机制（2026-09-02 SO 化改造）。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 数据组件：加载并访问全部 SO 配置（角色/武器/Mod/角色选择展示/战斗参数）。
    /// </summary>
    public sealed class DataComponent : MonoBehaviour
    {
        // Resources 加载目录（SO 资产所在子目录；数据驱动方案 B：展示/引用类 SO 直接编辑，数值类后续由表格→导出管线生成）
        private const string CharacterDir = "Data/Character";
        private const string WeaponDir = "Data/Weapon";
        private const string ModDir = "Data/Mod";
        private const string SelectDir = "Data/Select";
        private const string BattleDir = "Data/Battle";
        // 物品/法杖系统（P1：doc/物品与法杖系统设计.md）
        private const string ItemDir = "Data/Item";
        private const string SpellDir = "Data/Spell";
        private const string WandDir = "Data/Wand";
        private const string ProjectileDir = "Data/Projectile";
        // 法术编程系统（doc/法术编程系统-执行文档.md D31/D32）
        private const string SpellSystemDir = "Data/SpellSystem";

        private readonly List<CharacterSO> m_Characters = new List<CharacterSO>();
        private readonly List<WeaponSO> m_Weapons = new List<WeaponSO>();
        private readonly List<ModSO> m_Mods = new List<ModSO>();
        private readonly List<ItemSO> m_Items = new List<ItemSO>();
        private readonly List<SpellSO> m_Spells = new List<SpellSO>();
        private readonly List<WandSO> m_Wands = new List<WandSO>();
        private readonly List<ProjectileProfileSO> m_Projectiles = new List<ProjectileProfileSO>();

        private readonly Dictionary<int, CharacterSO> m_CharacterById = new Dictionary<int, CharacterSO>();
        private readonly Dictionary<int, WeaponSO> m_WeaponById = new Dictionary<int, WeaponSO>();
        private readonly Dictionary<int, ModSO> m_ModById = new Dictionary<int, ModSO>();
        private readonly Dictionary<int, ItemSO> m_ItemById = new Dictionary<int, ItemSO>();
        private readonly Dictionary<int, SpellSO> m_SpellById = new Dictionary<int, SpellSO>();
        private readonly Dictionary<int, WandSO> m_WandById = new Dictionary<int, WandSO>();
        private readonly Dictionary<int, ProjectileProfileSO> m_ProjectileById = new Dictionary<int, ProjectileProfileSO>();

        /// <summary>角色选择展示配置（无则返回 null）。</summary>
        public CharacterSelectConfigSO SelectConfig { get; private set; }

        /// <summary>战斗/波次平衡参数（无则返回 null）。</summary>
        public BattleConfigSO Battle { get; private set; }

        /// <summary>法术系统总配置（上限/兜底/预算口径；D31）。</summary>
        public SpellSystemConfigSO SpellSystem { get; private set; }

        /// <summary>初始装备配置（两手杖 + 预填法术 + 背包初始卡；D32）。</summary>
        public StartingLoadoutSO StartingLoadout { get; private set; }

        /// <summary>是否已初始化（同步加载完成即 true）。</summary>
        public bool IsReady { get; private set; } = false;

        /// <summary>
        /// 初始化：同步加载全部 SO 资产并建索引。
        /// </summary>
        public void Init()
        {
            m_Characters.Clear();
            m_Weapons.Clear();
            m_Mods.Clear();
            m_Items.Clear();
            m_Spells.Clear();
            m_Wands.Clear();
            m_Projectiles.Clear();
            m_CharacterById.Clear();
            m_WeaponById.Clear();
            m_ModById.Clear();
            m_ItemById.Clear();
            m_SpellById.Clear();
            m_WandById.Clear();
            m_ProjectileById.Clear();

            // Resources.LoadAll：未生成资产时目录为空 → 空列表（不报错）
            var chars = Resources.LoadAll<CharacterSO>(CharacterDir);
            foreach (var so in chars)
            {
                if (so == null)
                {
                    continue;
                }
                m_Characters.Add(so);
                if (!m_CharacterById.ContainsKey(so.Id))
                {
                    m_CharacterById[so.Id] = so;
                }
            }

            var weapons = Resources.LoadAll<WeaponSO>(WeaponDir);
            foreach (var so in weapons)
            {
                if (so == null)
                {
                    continue;
                }
                m_Weapons.Add(so);
                if (!m_WeaponById.ContainsKey(so.Id))
                {
                    m_WeaponById[so.Id] = so;
                }
            }

            var mods = Resources.LoadAll<ModSO>(ModDir);
            foreach (var so in mods)
            {
                if (so == null)
                {
                    continue;
                }
                m_Mods.Add(so);
                if (!m_ModById.ContainsKey(so.Id))
                {
                    m_ModById[so.Id] = so;
                }
            }

            var selects = Resources.LoadAll<CharacterSelectConfigSO>(SelectDir);
            SelectConfig = selects != null && selects.Length > 0 ? selects[0] : null;

            var battles = Resources.LoadAll<BattleConfigSO>(BattleDir);
            Battle = battles != null && battles.Length > 0 ? battles[0] : null;

            // ---- 法术系统总配置 + 初始装备（D31/D32） ----
            var spellSystems = Resources.LoadAll<SpellSystemConfigSO>(SpellSystemDir);
            SpellSystem = spellSystems != null && spellSystems.Length > 0 ? spellSystems[0] : null;
            var loadouts = Resources.LoadAll<StartingLoadoutSO>(SpellSystemDir);
            StartingLoadout = loadouts != null && loadouts.Length > 0 ? loadouts[0] : null;

            // ---- 物品 / 法术 / 法杖 / 弹道剖面（P1） ----
            var items = Resources.LoadAll<ItemSO>(ItemDir);
            foreach (var so in items)
            {
                if (so == null) { continue; }
                m_Items.Add(so);
                if (!m_ItemById.ContainsKey(so.Id)) { m_ItemById[so.Id] = so; }
            }

            var spells = Resources.LoadAll<SpellSO>(SpellDir);
            foreach (var so in spells)
            {
                if (so == null) { continue; }
                m_Spells.Add(so);
                if (!m_SpellById.ContainsKey(so.Id)) { m_SpellById[so.Id] = so; }
            }

            var wands = Resources.LoadAll<WandSO>(WandDir);
            foreach (var so in wands)
            {
                if (so == null) { continue; }
                m_Wands.Add(so);
                if (!m_WandById.ContainsKey(so.Id)) { m_WandById[so.Id] = so; }
            }

            var projectiles = Resources.LoadAll<ProjectileProfileSO>(ProjectileDir);
            foreach (var so in projectiles)
            {
                if (so == null) { continue; }
                m_Projectiles.Add(so);
                if (!m_ProjectileById.ContainsKey(so.Id)) { m_ProjectileById[so.Id] = so; }
            }

            IsReady = true;
            Log.Info("[DataComponent] SO 加载完成: Character={0}, Weapon={1}, Mod={2}, Item={3}, Spell={4}, Wand={5}, Proj={6}, Select={7}, Battle={8}, SpellSystem={9}, Loadout={10}",
                m_Characters.Count, m_Weapons.Count, m_Mods.Count,
                m_Items.Count, m_Spells.Count, m_Wands.Count, m_Projectiles.Count,
                SelectConfig != null, Battle != null, SpellSystem != null, StartingLoadout != null);
        }

        // ==================== 角色 ====================

        public List<CharacterSO> GetAllCharacters()
        {
            var results = new List<CharacterSO>(m_Characters);
            // 按 Id 升序（确定性展示顺序）
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public CharacterSO GetCharacter(int id)
        {
            CharacterSO so;
            return m_CharacterById.TryGetValue(id, out so) ? so : null;
        }

        // ==================== 武器 ====================

        public List<WeaponSO> GetAllWeapons()
        {
            var results = new List<WeaponSO>(m_Weapons);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public WeaponSO GetWeapon(int id)
        {
            WeaponSO so;
            return m_WeaponById.TryGetValue(id, out so) ? so : null;
        }

        // ==================== Mod ====================

        public List<ModSO> GetAllMods()
        {
            var results = new List<ModSO>(m_Mods);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public ModSO GetMod(int id)
        {
            ModSO so;
            return m_ModById.TryGetValue(id, out so) ? so : null;
        }

        // ==================== 物品 / 法术 / 法杖 / 弹道剖面（P1） ====================

        public List<ItemSO> GetAllItems()
        {
            var results = new List<ItemSO>(m_Items);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public ItemSO GetItem(int id)
        {
            ItemSO so;
            return m_ItemById.TryGetValue(id, out so) ? so : null;
        }

        public List<SpellSO> GetAllSpells()
        {
            var results = new List<SpellSO>(m_Spells);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public SpellSO GetSpell(int id)
        {
            SpellSO so;
            return m_SpellById.TryGetValue(id, out so) ? so : null;
        }

        public List<WandSO> GetAllWands()
        {
            var results = new List<WandSO>(m_Wands);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public WandSO GetWand(int id)
        {
            WandSO so;
            return m_WandById.TryGetValue(id, out so) ? so : null;
        }

        public List<ProjectileProfileSO> GetAllProjectiles()
        {
            var results = new List<ProjectileProfileSO>(m_Projectiles);
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results;
        }

        public ProjectileProfileSO GetProjectile(int id)
        {
            ProjectileProfileSO so;
            return m_ProjectileById.TryGetValue(id, out so) ? so : null;
        }
    }
}
