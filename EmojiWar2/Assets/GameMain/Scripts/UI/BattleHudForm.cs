//------------------------------------------------------------
// EmojiWar GameMain - 战斗 HUD 界面
// 显示：金币、波次、玩家血量、武器栏（图标/名称/属性/弹药/冷却进度条/Mod 槽）。
// 参考旧版 WeaponPanel（武器名+图标+Mod 槽）与 SimpleWeaponPanel（射击冷却进度条）。
//------------------------------------------------------------
// D19（S6）：武器栏改为「法杖名 + 已装/槽数 + 下一发法术 + 魔力/充能」，
// 删除读旧武器表的"伤害/射速"（误导数据：法杖只提供框架与资源，不提供伤害）。

using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.Simulation;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 战斗 HUD：局内状态显示（含完整武器栏）。
    /// </summary>
    public partial class BattleHudForm : UGuiForm
    {
        [SerializeField]
        private Text m_CoinText = null;

        [SerializeField]
        private Text m_WaveText = null;

        [SerializeField]
        private Text m_HpText = null;

        // ---- 武器栏 ----
        [SerializeField]
        private Image m_WeaponIconImage = null;    // 武器图标（emoji）

        [SerializeField]
        private Text m_WeaponText = null;          // 当前武器名

        [SerializeField]
        private Text m_AmmoText = null;            // 弹药 / 装弹状态

        [SerializeField]
        private Text m_WeaponStatsText = null;     // 武器属性行（伤害/射速/散射）

        [SerializeField]
        private Image m_FireProgressFill = null;   // 射击冷却进度条填充（参考旧版 SimpleWeaponPanel.progress）

        // ---- 新 HUD（2026-09-10）：左下血条/能量条、右上设置 ----
        [SerializeField]
        private Image m_HpFill = null;             // 左下：血条填充

        [SerializeField]
        private Image m_EnergyFill = null;         // 左下：能量条填充（本轮占位：随时间缓慢回复）


        private float m_EnergyPlaceholder = 0.65f; // 能量占位值（0..1，暂未接入玩法）

        [SerializeField]
        private Text m_ModSlotsText = null;        // 已装备 Mod 槽（参考旧版 WeaponPanel.ModGrid）

        // ---- 进度条平滑插值（统一走 UiBarSmoother，见 UI/UiBarSmoother.cs） ----
        [SerializeField]
        private float m_BarLerpSpeed = 8f;         // 每秒收敛速度（越大越快，8≈0.12s 到位）

        [SerializeField]
        private float m_BarInnerGap = 4f;          // 填充条相对底条的内边距（px）

        private UiBarSmoother m_HpBar = null;
        private UiBarSmoother m_EnergyBar = null;
        private UiBarSmoother m_FireBar = null;

        private float m_WeaponRefreshTimer = 0f;
        private float m_BarProbeTimer = 0f;
        private int m_BarProbeCount = 0;
        private bool m_BarProbeSettledLogged = false;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged += OnCoinChanged;
            }

            if (m_BtnSettings != null)
            {
                m_BtnSettings.onClick.RemoveAllListeners();
                m_BtnSettings.onClick.AddListener(OpenSettings);
            }

            RefreshAll();
        }

        /// <summary>右上角设置：打开设置页（放 Popup 组，避免把本 HUD 所在的 Default 组暂停）。</summary>
        private void OpenSettings()
        {
            if (GameEntry.UI == null)
            {
                return;
            }
            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Popup))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Popup, Constant.UIGroup.DepthPopup);
            }
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.SettingsForm, Constant.UIGroup.Popup, this);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged -= OnCoinChanged;
            }
            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            // 低频刷新（0.1s 节流：波次/血量/冷却进度，避免每帧 UI 与对象查找开销）
            m_HudRefreshTimer -= Time.deltaTime;
            if (m_HudRefreshTimer <= 0f)
            {
                m_HudRefreshTimer = 0.1f;
                RefreshWave();
                RefreshHp();
                RefreshBars();
                RefreshFireProgress();
            }

            // 武器信息更低频刷新（弹药/装弹/属性/Mod，0.2s）
            m_WeaponRefreshTimer -= Time.deltaTime;
            if (m_WeaponRefreshTimer <= 0f)
            {
                m_WeaponRefreshTimer = 0.2f;
                RefreshWeaponInfo();
            }

            // 进度条平滑插值在 UiBarSmoother.Update 里逐帧推进（此处只做探针）
            ReportBarProbe();
        }

        private float m_HudRefreshTimer = 0f;

        /// <summary>取/建进度条平滑器（首次即以当前目标就位，避免开窗从初值扫一遍）。</summary>
        private UiBarSmoother EnsureBar(ref UiBarSmoother slot, Image fill)
        {
            if (fill == null)
            {
                return null;
            }
            if (slot == null)
            {
                slot = UiBarSmoother.Attach(fill, m_BarLerpSpeed, m_BarInnerGap);
            }
            return slot;
        }

        /// <summary>探针：进度条插值过程 + 收敛结果（自动化验证用）。</summary>
        private void ReportBarProbe()
        {
            if (m_HpBar == null && m_EnergyBar == null && m_FireBar == null)
            {
                return;
            }

            bool settling = (m_HpBar != null && !m_HpBar.IsSettled)
                || (m_EnergyBar != null && !m_EnergyBar.IsSettled)
                || (m_FireBar != null && !m_FireBar.IsSettled);

            m_BarProbeTimer -= Time.unscaledDeltaTime;
            if (m_BarProbeTimer > 0f)
            {
                return;
            }
            m_BarProbeTimer = 0.25f;

            if (settling && m_BarProbeCount < 40)
            {
                m_BarProbeCount++;
                WriteProbe(string.Format("[hudbar] lerp hp={0:F3}/{1:F3} energy={2:F3}/{3:F3} fire={4:F3}/{5:F3}",
                    BarCurrent(m_HpBar), BarTarget(m_HpBar),
                    BarCurrent(m_EnergyBar), BarTarget(m_EnergyBar),
                    BarCurrent(m_FireBar), BarTarget(m_FireBar)));
                return;
            }

            if (!settling && !m_BarProbeSettledLogged)
            {
                m_BarProbeSettledLogged = true;
                WriteProbe(string.Format("[hudbar] settled hp={0:F3} energy={1:F3} fire={2:F3} lerpSpeed={3:F1}",
                    BarCurrent(m_HpBar), BarCurrent(m_EnergyBar), BarCurrent(m_FireBar), m_BarLerpSpeed));
            }
        }

        private static float BarCurrent(UiBarSmoother bar)
        {
            return bar != null ? bar.CurrentRatio : -1f;
        }

        private static float BarTarget(UiBarSmoother bar)
        {
            return bar != null ? bar.TargetRatio : -1f;
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }

        /// <summary>刷新射击冷却进度条（参考旧版 SimpleWeaponPanel.setProgress；节流避免每帧改布局）。</summary>
        private void RefreshFireProgress()
        {
            if (m_FireProgressFill == null)
            {
                return;
            }

            float progress = 1f;
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null && simPlayer.FireRate > 0f)
            {
                // 网络模式：冷却进度 = 距下次开火的间隔比例
                float interval = 1f / simPlayer.FireRate;
                progress = Mathf.Clamp01(1f - simPlayer.FireCooldown / interval);
            }
            else
            {
                var player = Object.FindObjectOfType<Entity.PlayerEntity>();
                var weapon = player != null ? player.PrimaryWeapon : null;
                if (weapon != null)
                {
                    progress = weapon.GetProgress();
                }
            }

            // 进度条填充：交给 UiBarSmoother 做 lerp 平滑（此处只给目标比例）
            m_FireBar = EnsureBar(ref m_FireBar, m_FireProgressFill);
            if (m_FireBar != null)
            {
                m_FireBar.SetTarget(Mathf.Clamp01(progress), !m_FireBar.HasTarget);
            }
        }

        /// <summary>刷新当前武器信息（图标/名称/弹药/属性/Mod 槽）。网络模式从确定性模拟读取。</summary>
        private void RefreshWeaponInfo()
        {
            if (m_WeaponIconImage == null && m_WeaponText == null && m_AmmoText == null
                && m_WeaponStatsText == null && m_ModSlotsText == null)
            {
                return;
            }

            // 网络模式：本地玩家状态来自确定性模拟
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null)
            {
                if (m_WeaponIconImage != null)
                {
                    var weaponRow = Data.ConfigService.GetWeapon(simPlayer.WeaponId);
                    m_WeaponIconImage.sprite = weaponRow != null ? weaponRow.IconSprite : null;
                }
                if (m_WeaponText != null)
                {
                    m_WeaponText.text = "武器：" + simPlayer.WeaponName;
                }
                if (m_AmmoText != null)
                {
                    // 弹药已取消（无限释放，2026-09-02 需求）：恒显示 ∞，无装弹状态
                    m_AmmoText.text = "弹药 ∞";
                }
                if (m_WeaponStatsText != null)
                {
                    // D19：**删掉读旧武器表的"伤害/射速"**（误导数据），
                    // 改为当前手「法杖名 + 已装法术数/槽数 + 下一发法术 + 魔力/充能」
                    m_WeaponStatsText.text = BuildSpellLine(simPlayer, false);
                }
                if (m_ModSlotsText != null)
                {
                    // 网络模式：Mod 由局内背包携带（确定性模拟暂不模拟 Mod 装备）
                    var session = Battle.RunSession.Instance;
                    int modCount = session != null ? session.ModBag.Count : 0;
                    m_ModSlotsText.text = modCount > 0
                        ? string.Format("Mod 槽：已装备 {0} 个", modCount)
                        : "Mod 槽：无";
                }
                return;
            }

            // 单机模式：本地 PlayerEntity
            var player = UnityEngine.Object.FindObjectOfType<Entity.PlayerEntity>();
            var weaponBase = player != null ? player.PrimaryWeapon : null;

            if (m_WeaponIconImage != null)
            {
                var character = player != null && player.CharacterId > 0 && GameEntry.Data != null
                    ? GameEntry.Data.GetCharacter(player.CharacterId)
                    : null;
                var weaponRow = character != null && GameEntry.Data != null
                    ? GameEntry.Data.GetWeapon(character.DefaultWeaponId)
                    : null;
                m_WeaponIconImage.sprite = weaponRow != null ? weaponRow.IconSprite : null;
            }

            if (m_WeaponText != null)
            {
                m_WeaponText.text = "武器：" + (weaponBase != null ? weaponBase.WeaponName : "无");
            }

            if (m_AmmoText != null)
            {
                if (weaponBase != null)
                {
                    // 弹药已取消（无限释放）：恒显示 ∞
                    m_AmmoText.text = "弹药 ∞";
                }
                else
                {
                    m_AmmoText.text = "";
                }
            }

            if (m_WeaponStatsText != null && weaponBase != null)
            {
                // D19：单机兜底路径同样不再显示旧武器表的"伤害/射速"
                var local = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
                m_WeaponStatsText.text = local != null ? BuildSpellLine(local, false) : "";
            }

            if (m_ModSlotsText != null && weaponBase != null && weaponBase.ModComponent != null)
            {
                int modCount = weaponBase.ModComponent.ModCount;
                m_ModSlotsText.text = modCount > 0
                    ? string.Format("Mod 槽：{0} 个", modCount)
                    : "Mod 槽：无";
            }
            else if (m_ModSlotsText != null)
            {
                m_ModSlotsText.text = "Mod 槽：无";
            }
        }

        /// <summary>
        /// D19：当前手信息行 —— 「法杖名 + 已装法术数/槽数 + 下一发法术 + 魔力/充能」。
        /// **不再读旧武器表的"伤害/射速"**（那是误导数据：法杖只提供框架与资源，不提供伤害）。
        /// 数据全部来自确定性模拟的 CastProgram / CastRuntimeState（只读，不反向影响逻辑）。
        /// </summary>
        private string BuildSpellLine(SimPlayer p, bool secondary)
        {
            var program = secondary ? p.SecondaryProgram : p.PrimaryProgram;
            if (!program.IsValid)
            {
                program = p.SecondaryProgram.IsValid ? p.SecondaryProgram : p.PrimaryProgram;
                secondary = program.IsValid && program.WandId == p.SecondaryProgram.WandId;
            }
            if (!program.IsValid) { return "无法杖"; }

            var state = secondary ? p.SecondaryCast : p.PrimaryCast;
            var hand = secondary ? "右手" : "左手";

            string wandName = "法杖#" + program.WandId;
            var wandRow = Data.ConfigService.GetWand(program.WandId);
            if (wandRow != null) { wandName = wandRow.DisplayName; }

            // 下一发法术（游标指向的非空物品）
            string next = "—";
            for (int i = state.Cursor; i < program.SlotCount; i++)
            {
                var sp = program.SpellAt(i);
                if (sp.IsEmpty) { continue; }
                var itemRow = Data.ConfigService.GetItem(sp.ItemId);
                next = itemRow != null ? itemRow.DisplayName : ("法术#" + sp.SpellId);
                break;
            }

            float rechargeSeconds = state.RechargeRemainingFrames * Simulation.CastResolver.TickSeconds;
            return string.Format("{0} {1} {2}/{3} · 下一发 {4} · 魔力 {5:F0}/{6} · 充能 {7:F2}s",
                hand, wandName, program.LoadedCount, program.SlotCount, next,
                state.Mana, program.ManaMax, rechargeSeconds);
        }

        private void OnCoinChanged(int coin)
        {
            if (m_CoinText != null)
            {
                m_CoinText.text = "金币：" + coin;
            }
        }

        private void RefreshAll()
        {
            RefreshCoin();
            RefreshWave();
            RefreshHp();
            RefreshWeaponInfo();
        }

        private void RefreshCoin()
        {
            if (m_CoinText == null)
            {
                return;
            }
            var session = Battle.RunSession.Instance;
            m_CoinText.text = "金币：" + (session != null ? session.Coin : 0);
        }

        private void RefreshWave()
        {
            if (m_WaveText == null)
            {
                return;
            }
            // 网络模式：波次来自确定性模拟
            var sim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            if (sim != null)
            {
                // WaveIndex=0 = 准备阶段商店（还没出第 1 波敌人）
                m_WaveText.text = sim.WaveIndex <= 0 ? "准备阶段" : ("波次：" + sim.WaveIndex);
                return;
            }
            var session = Battle.RunSession.Instance;
            int w = session != null ? session.WaveIndex : 0;
            m_WaveText.text = w <= 0 ? "准备阶段" : ("波次：" + w);
        }

        private void RefreshHp()
        {
            if (m_HpText == null)
            {
                return;
            }

            // 网络模式：本地玩家 HP 来自确定性模拟
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null)
            {
                m_HpText.text = string.Format("HP: {0:F0}/100", simPlayer.Hp);
                return;
            }

            var player = Object.FindObjectOfType<Entity.PlayerEntity>();
            if (player != null && player.Data != null)
            {
                m_HpText.text = string.Format("HP: {0:F0}/{1:F0}", player.CurrentHealth, player.Data.MaxHealth);
            }
            else
            {
                if (m_HpText != null) { m_HpText.text = "HP: --"; }
            }
        }

        /// <summary>左下血条 + 能量条填充（统一经 UiBarSmoother 做 lerp 平滑插值）。</summary>
        private void RefreshBars()
        {
            // 血条：Hp / MaxHealth（角色配置）
            if (m_HpFill != null)
            {
                float ratio = 1f;
                var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
                if (simPlayer != null)
                {
                    var character = Data.ConfigService.GetCharacter(simPlayer.CharacterId);
                    float max = character != null && character.MaxHealth > 0 ? character.MaxHealth : 100f;
                    ratio = Mathf.Clamp01(simPlayer.Hp / max);
                }
                else
                {
                    var player = Object.FindObjectOfType<Entity.PlayerEntity>();
                    if (player != null && player.Data != null && player.Data.MaxHealth > 0)
                    {
                        ratio = Mathf.Clamp01(player.CurrentHealth / (float)player.Data.MaxHealth);
                    }
                }
                m_HpBar = EnsureBar(ref m_HpBar, m_HpFill);
                if (m_HpBar != null) { m_HpBar.SetTarget(ratio, !m_HpBar.HasTarget); }
            }

            // 能量条：占位（暂无玩法），缓慢来回变化以证明接线可用
            if (m_EnergyFill != null)
            {
                m_EnergyPlaceholder += Time.deltaTime * 0.08f;
                if (m_EnergyPlaceholder > 1f) { m_EnergyPlaceholder = 0.15f; }
                m_EnergyBar = EnsureBar(ref m_EnergyBar, m_EnergyFill);
                if (m_EnergyBar != null) { m_EnergyBar.SetTarget(m_EnergyPlaceholder, !m_EnergyBar.HasTarget); }
            }
        }
    }
}
