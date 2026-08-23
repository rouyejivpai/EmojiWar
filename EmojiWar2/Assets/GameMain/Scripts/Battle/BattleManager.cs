//------------------------------------------------------------
// EmojiWar GameMain - 战斗管理器
// 负责：生成玩家、波次敌人生成、战斗状态。
// 后续网络化：生成/波次由服务器权威驱动。
//------------------------------------------------------------

using System.Collections;
using UnityEngine;

namespace EmojiWar.GameMain.Battle
{
    /// <summary>
    /// 战斗管理器：单机本地战斗逻辑。
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        private const string PlayerPrefabPath = "Assets/GameMain/Entities/Player.prefab";
        private const string EnemyPrefabPath = "Assets/GameMain/Entities/Enemy.prefab";

        [Header("波次配置")]
        [SerializeField]
        private int m_EnemiesPerWave = 5;

        [SerializeField]
        private float m_WaveInterval = 10f;

        [SerializeField]
        private float m_SpawnRadius = 12f;

        private bool m_BattleRunning = false;
        private int m_WaveIndex = 0;
        private int m_AliveEnemies = 0;

        public bool BattleRunning { get { return m_BattleRunning; } }
        public int WaveIndex { get { return m_WaveIndex; } }

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

            SpawnPlayer(characterId);
            StartCoroutine(WaveLoop());
        }

        private void SpawnPlayer(int characterId)
        {
            var character = GameEntry.Data != null ? GameEntry.Data.GetCharacter(characterId) : null;
            GameObject prefab = LoadPrefab(PlayerPrefabPath);
            if (prefab == null)
            {
                return;
            }

            GameObject playerGo = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var player = playerGo.GetComponent<Entity.PlayerEntity>();
            if (player != null)
            {
                player.Team = Entity.EntityTeam.Player;
                player.MoveSpeed = character != null ? character.MoveSpeed : 5f;

                // 装备主武器（从数据表配置）
                int weaponId = character != null ? character.DefaultWeaponId : 1;
                EquipWeapon(player, weaponId, true);
            }
        }

        private void EquipWeapon(Entity.PlayerEntity player, int weaponId, bool isPrimary)
        {
            var weaponRow = GameEntry.Data != null ? GameEntry.Data.GetWeapon(weaponId) : null;
            if (weaponRow == null || player == null)
            {
                return;
            }

            // 简化：主武器直接用数据行配置的远程武器逻辑挂载
            var weapon = player.GetComponentInChildren<Weapon.WeaponBase>();
            if (weapon == null)
            {
                return;
            }

            weapon.Configure(weaponRow);
            weapon.SetOwner(player);
            player.PrimaryWeapon = weapon;
        }

        private IEnumerator WaveLoop()
        {
            while (m_BattleRunning)
            {
                m_WaveIndex++;
                Debug.Log(string.Format("[BattleManager] 第 {0} 波开始", m_WaveIndex));

                yield return StartCoroutine(SpawnWave(m_WaveIndex));

                // 等待本波敌人清完或超时
                float timeout = 30f;
                while (m_AliveEnemies > 0 && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                yield return new WaitForSeconds(m_WaveInterval);
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
                m_AliveEnemies--;
                return;
            }

            // 玩家周围随机位置生成
            Vector3 playerPos = FindPlayerPosition();
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            Vector3 spawnPos = playerPos + (Vector3)(randomDir * m_SpawnRadius);

            GameObject enemyGo = Instantiate(prefab, spawnPos, Quaternion.identity);
            var enemy = enemyGo.GetComponent<Entity.EnemyEntity>();
            if (enemy != null)
            {
                enemy.Team = Entity.EntityTeam.Enemy;
                enemy.MoveSpeed = 3f;
                enemy.ContactDamage = 8f;
                enemy.OnDeath += OnEnemyDeath;
            }
        }

        private void OnEnemyDeath(Entity.EntityBase enemy)
        {
            enemy.OnDeath -= OnEnemyDeath;
            m_AliveEnemies = Mathf.Max(0, m_AliveEnemies - 1);
        }

        private Vector3 FindPlayerPosition()
        {
            var player = Object.FindObjectOfType<Entity.PlayerEntity>();
            return player != null ? player.transform.position : Vector3.zero;
        }

        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        private void OnDestroy()
        {
            m_BattleRunning = false;
            StopAllCoroutines();
        }
    }
}
