//------------------------------------------------------------
// EmojiWar GameMain - 战斗管理器
// 负责：生成玩家、波次敌人生成、波间商店、战斗状态。
// 流程：开始 → 波次战斗 → 波间商店 → 下一波 → ...
// 后续网络化：生成/波次由服务器权威驱动。
//------------------------------------------------------------

using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmojiWar.GameMain.Battle
{
    /// <summary>
    /// 战斗管理器：单机本地战斗逻辑。
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        private const string PlayerPrefabPath = "Assets/GameMain/Resources/Entities/Player.prefab";
        private const string EnemyPrefabPath = "Assets/GameMain/Resources/Entities/Enemy.prefab";

        [Header("波次配置")]
        [SerializeField]
        private int m_EnemiesPerWave = 5;

        [SerializeField]
        private float m_SpawnRadius = 12f;

        /// <summary>波间商店打开事件（参数：波次号）。</summary>
        public static event Action<int> OnShopPhase;

        /// <summary>战斗结束事件（参数：波次号）。</summary>
        public static event Action<int> OnBattleEnd;

        private bool m_BattleRunning = false;
        private bool m_WaitingForShop = false;
        private int m_WaveIndex = 0;
        private int m_AliveEnemies = 0;
        private Coroutine m_WaveLoop = null;

        public bool BattleRunning { get { return m_BattleRunning; } }
        public int WaveIndex { get { return m_WaveIndex; } }

        private void Awake()
        {
            // 从全局配置读取波次参数（未配置时用默认值）
            LoadWaveConfig();
        }

        /// <summary>
        /// 从 GameFramework Config 读取波次配置。
        /// 配置键：Battle.EnemiesPerWave / Battle.SpawnRadius / Battle.WaveInterval
        /// </summary>
        private void LoadWaveConfig()
        {
            if (GameEntry.Config != null)
            {
                m_EnemiesPerWave = GameEntry.Config.GetInt("Battle.EnemiesPerWave", m_EnemiesPerWave);
                float radius = GameEntry.Config.GetFloat("Battle.SpawnRadius", -1f);
                if (radius > 0f)
                {
                    m_SpawnRadius = radius;
                }
            }
        }

        /// <summary>
        /// 开始战斗：生成玩家 + 启动波次协程。
        /// </summary>
        public void StartBattle(int characterId)
        {
            if (m_BattleRunning)
            {
                return;
            }

            m_BattleRunning = true;
            m_WaveIndex = 0;

            // 局内会话
            var session = RunSession.Instance;
            if (session == null)
            {
                var go = new GameObject("RunSession");
                session = go.AddComponent<RunSession>();
            }
            session.StartRun(characterId);

            SpawnPlayer(characterId);
            m_WaveLoop = StartCoroutine(WaveLoop());
        }

        /// <summary>
        /// 商店关闭后继续下一波（由 ShopForm 调用）。
        /// </summary>
        public void ResumeAfterShop()
        {
            m_WaitingForShop = false;
        }

        private void SpawnPlayer(int characterId)
        {
            var character = GameEntry.Data != null ? GameEntry.Data.GetCharacter(characterId) : null;
            GameObject prefab = LoadPrefab(PlayerPrefabPath);
            var playerSprite = Art.ArtManager.GetPlayerSprite();
            WriteProbe(string.Format("[battle] SpawnPlayer prefab={0} player={1} sprite={2}",
                prefab != null ? "OK" : "NULL", "?",
                playerSprite != null ? "OK" : "NULL"));
            if (prefab == null)
            {
                return;
            }

            GameObject playerGo = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var player = playerGo.GetComponent<Entity.PlayerEntity>();
            WriteProbe("[battle] SpawnPlayer instantiated, PlayerEntity=" + (player != null ? "OK" : "NULL"));
            if (player != null)
            {
                // 本机角色 ID（渲染角色专属美术）与网络实体 ID
                player.CharacterId = characterId;
                player.NetworkEntityId = GetLocalNetworkEntityId();

                player.Team = Entity.EntityTeam.Player;
                player.MoveSpeed = character != null ? character.MoveSpeed : 5f;

                // 装备主武器（从数据表配置）
                int weaponId = character != null ? character.DefaultWeaponId : 1;
                EquipWeapon(player, weaponId, true);

                // 应用背包中的 Mod 到武器
                var session = RunSession.Instance;
                if (session != null)
                {
                    foreach (var modId in session.ModBag)
                    {
                        var modRow = GameEntry.Data.GetMod(modId);
                        if (modRow != null && player.PrimaryWeapon != null)
                        {
                            player.PrimaryWeapon.ModComponent.AddMod(modRow);
                        }
                    }
                }
            }
        }

        /// <summary>获取本机网络实体 ID（Host=session0 实体，Client=MyEntityId）。</summary>
        private int GetLocalNetworkEntityId()
        {
            var net = GameEntry.NetworkService;
            if (net == null)
            {
                return -1;
            }

            if (net.Mode == Network.NetMode.Host)
            {
                var hostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                    : null;
                return hostLogic != null ? hostLogic.GetLocalEntityId() : -1;
            }

            var clientLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponentInChildren<Network.NetClientLogic>()
                : null;
            return clientLogic != null ? clientLogic.MyEntityId : -1;
        }

        private void EquipWeapon(Entity.PlayerEntity player, int weaponId, bool isPrimary)
        {
            var weaponRow = GameEntry.Data != null ? GameEntry.Data.GetWeapon(weaponId) : null;
            if (weaponRow == null || player == null)
            {
                return;
            }

            var weapon = player.GetComponentInChildren<Weapon.WeaponBase>();
            if (weapon == null)
            {
                return;
            }

            weapon.Configure(weaponRow);
            weapon.SetOwner(player);
            var ranged = weapon as Weapon.RangedWeapon;
            if (ranged != null)
            {
                ranged.InitRanged(weaponRow);
            }
            player.PrimaryWeapon = weapon;
        }

        private IEnumerator WaveLoop()
        {
            while (m_BattleRunning)
            {
                m_WaveIndex++;
                RunSession.Instance?.AdvanceWave(m_WaveIndex);
                Debug.Log(string.Format("[BattleManager] 第 {0} 波开始", m_WaveIndex));

                yield return StartCoroutine(SpawnWave(m_WaveIndex));

                // 等待本波敌人清完或超时
                float timeout = 40f;
                while (m_AliveEnemies > 0 && timeout > 0f && m_BattleRunning)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (!m_BattleRunning)
                {
                    yield break;
                }

                // 波间商店
                m_WaitingForShop = true;
                Debug.Log(string.Format("[BattleManager] 第 {0} 波结束，进入商店", m_WaveIndex));
                OnShopPhase?.Invoke(m_WaveIndex);

                // 等待商店关闭
                while (m_WaitingForShop && m_BattleRunning)
                {
                    yield return null;
                }

                // 敌人生成位置基于当前玩家位置，无需额外处理
                yield return new WaitForSeconds(1f);
            }
        }

        private IEnumerator SpawnWave(int waveIndex)
        {
            int count = m_EnemiesPerWave + (waveIndex - 1) * 2;
            m_AliveEnemies = count;

            for (int i = 0; i < count; i++)
            {
                SpawnEnemy();
                yield return new WaitForSeconds(0.8f);
            }
        }

        private void SpawnEnemy()
        {
            GameObject prefab = LoadPrefab(EnemyPrefabPath);
            if (prefab == null)
            {
                WriteProbe("[battle] SpawnEnemy prefab=NULL");
                m_AliveEnemies--;
                return;
            }

            Vector3 playerPos = FindPlayerPosition();
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            Vector3 spawnPos = playerPos + (Vector3)(randomDir * m_SpawnRadius);

            GameObject enemyGo = Instantiate(prefab, spawnPos, Quaternion.identity);
            var enemy = enemyGo.GetComponent<Entity.EnemyEntity>();
            if (enemy != null)
            {
                enemy.Team = Entity.EntityTeam.Enemy;
                enemy.MoveSpeed = 2.5f;
                enemy.ContactDamage = 6f;   // 平衡：降低接触伤害
                enemy.OnDeath += OnEnemyDeath;
            }
        }

        private void OnEnemyDeath(Entity.EntityBase enemy)
        {
            enemy.OnDeath -= OnEnemyDeath;

            // 击杀奖励
            var session = RunSession.Instance;
            if (session != null)
            {
                session.AddCoin(15);
            }

            m_AliveEnemies = Mathf.Max(0, m_AliveEnemies - 1);
        }

        private Vector3 FindPlayerPosition()
        {
            var player = UnityEngine.Object.FindObjectOfType<Entity.PlayerEntity>();
            return player != null ? player.transform.position : Vector3.zero;
        }

        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            // 运行时：从 Resources 目录按相对路径加载（去掉 "Assets/.../Resources/" 前缀）
            const string resourcesMarker = "Resources/";
            int index = path.IndexOf(resourcesMarker, System.StringComparison.Ordinal);
            string resourcesPath = index >= 0 ? path.Substring(index + resourcesMarker.Length) : path;
            resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - ".prefab".Length);
            return Resources.Load<GameObject>(resourcesPath);
#endif
        }

        private void OnDestroy()
        {
            m_BattleRunning = false;
            if (m_WaveLoop != null)
            {
                StopCoroutine(m_WaveLoop);
            }
        }

        /// <summary>运行时探针（验证构建版战斗实体加载）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
