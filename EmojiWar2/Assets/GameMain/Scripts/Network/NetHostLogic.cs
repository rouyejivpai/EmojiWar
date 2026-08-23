//------------------------------------------------------------
// EmojiWar GameMain - Host 权威逻辑（服务器端）
// 维护所有玩家/实体的权威状态：
//   收到 C2SPlayerInput → 更新玩家位置 → 广播 S2CEntityState
//   服务器驱动：波次敌人生成、敌人 AI 追逐、血量同步、移除
// 多人合作 PvE 核心：所有战斗逻辑由 Host 计算。
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// Host 端权威状态：服务器模拟逻辑。
    /// </summary>
    public class NetHostLogic : MonoBehaviour
    {
        // 玩家权威状态（sessionId → 状态）
        private sealed class PlayerState
        {
            public int SessionId;
            public int EntityId;
            public Vector2 Position;
            public float Hp = 100f;
        }

        // 敌人权威状态
        private sealed class EnemyState
        {
            public int EntityId;
            public Vector2 Position;
            public float Hp = 30f;
            public float Speed = 2.5f;
            public bool Alive = true;
        }

        [Header("波次配置")]
        [SerializeField]
        private int m_EnemiesPerWave = 3;

        [SerializeField]
        private float m_SpawnRadius = 8f;

        private readonly Dictionary<int, PlayerState> m_Players = new Dictionary<int, PlayerState>();
        private readonly Dictionary<int, EnemyState> m_Enemies = new Dictionary<int, EnemyState>();
        private int m_NextEntityId = 1000;
        private int m_WaveIndex = 0;
        private Coroutine m_WaveCoroutine = null;

        private NetworkService m_Service = null;

        private void Awake()
        {
            // 默认绑定 GameEntry 的服务；测试可通过 Bind 覆盖
            m_Service = GameEntry.NetworkService;
        }

        /// <summary>
        /// 绑定网络服务（测试/多实例场景使用）。
        /// </summary>
        public void Bind(NetworkService service)
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
                m_Service.OnClientDisconnected -= OnClientDisconnected;
            }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
                m_Service.OnClientDisconnected += OnClientDisconnected;
            }
        }

        private void OnClientDisconnected(int sessionId)
        {
            Debug.Log("[NetHostLogic] 客户端 " + sessionId + " 断开");
            HandleLeave(sessionId);
        }

        private void OnEnable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
                m_Service.OnClientDisconnected += OnClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
                m_Service.OnClientDisconnected -= OnClientDisconnected;
            }
            StopWave();
        }

        private void OnModeChanged(NetMode mode)
        {
            if (mode == NetMode.Host)
            {
                m_Players.Clear();
                m_Enemies.Clear();
                m_WaveIndex = 0;
                Debug.Log("[NetHostLogic] Host 模式就绪，等待玩家加入");
            }
        }

        /// <summary>
        /// Host 本机加入（作为 1 号玩家，不走网络连接）。
        /// </summary>
        public void JoinLocal(string playerName)
        {
            var state = new PlayerState
            {
                SessionId = 0,
                EntityId = m_NextEntityId++,
                Position = Vector2.zero,
            };
            m_Players[0] = state;
            Debug.Log("[NetHostLogic] 房主 " + playerName + " 本机加入，实体 " + state.EntityId);

            // 广播生成（供其他客户端看到房主）
            var spawn = new S2CSpawnEntity
            {
                EntityId = state.EntityId,
                Type = 0,
                Team = 1,
                X = state.Position.x,
                Y = state.Position.y,
            };
            m_Service?.BroadcastToClients(spawn);

            // 启动波次
            if (m_Players.Count >= 1 && m_WaveCoroutine == null)
            {
                m_WaveCoroutine = StartCoroutine(WaveLoop());
            }
        }

        /// <summary>
        /// 每帧更新（服务器权威模拟）。
        /// </summary>
        private void Update()
        {
            if (m_Service == null || m_Service.Mode != NetMode.Host)
            {
                return;
            }

            UpdateEnemies();
        }

        // ==================== 消息处理 ====================

        private void OnClientMessage(int sessionId, NetMessage message)
        {
            switch (message.Id)
            {
                case MsgId.JoinRoom:
                    HandleJoin(sessionId, message as C2SJoinRoom);
                    break;

                case MsgId.PlayerInput:
                    HandleInput(sessionId, message as C2SPlayerInput);
                    break;

                case MsgId.LeaveRoom:
                    HandleLeave(sessionId);
                    break;
            }
        }

        private void HandleJoin(int sessionId, C2SJoinRoom join)
        {
            if (join == null)
            {
                return;
            }

            var state = new PlayerState
            {
                SessionId = sessionId,
                EntityId = m_NextEntityId++,
                Position = new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)),
            };
            m_Players[sessionId] = state;

            // 通知所有客户端：新玩家加入 + 生成实体
            var joined = new S2CPlayerJoined { PlayerId = sessionId, PlayerName = join.PlayerName };
            m_Service.BroadcastToClients(joined);

            var spawn = new S2CSpawnEntity
            {
                EntityId = state.EntityId,
                Type = 0,
                Team = 1,
                X = state.Position.x,
                Y = state.Position.y,
            };
            m_Service.BroadcastToClients(spawn);

            // 房间状态
            var room = new S2CRoomState { RoomId = "ROOM-001", PlayerCount = m_Players.Count };
            m_Service.BroadcastToClients(room);

            Debug.Log(string.Format("[NetHostLogic] 玩家 {0}({1}) 加入，生成实体 {2}",
                join.PlayerName, sessionId, state.EntityId));

            // 首个玩家加入时启动波次
            if (m_Players.Count == 1 && m_WaveCoroutine == null)
            {
                m_WaveCoroutine = StartCoroutine(WaveLoop());
            }
        }

        private void HandleInput(int sessionId, C2SPlayerInput input)
        {
            if (input == null || !m_Players.TryGetValue(sessionId, out var state))
            {
                return;
            }

            // Host 权威：根据输入更新位置
            Vector2 moveDir = new Vector2(input.InputX, input.InputY);
            if (moveDir.sqrMagnitude > 1f)
            {
                moveDir = moveDir.normalized;
            }

            const float speed = 5f;
            state.Position += moveDir * speed * Time.deltaTime;

            // 广播玩家状态给所有客户端
            BroadcastEntityState(state.EntityId, state.Position, state.Hp, 1);
        }

        private void HandleLeave(int sessionId)
        {
            if (m_Players.Remove(sessionId, out var state))
            {
                // 广播玩家离开 + 移除实体
                m_Service.BroadcastToClients(new S2CPlayerLeft { PlayerId = sessionId });
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = state.EntityId });
                Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 离开，实体 " + state.EntityId + " 移除，广播已发送");
            }
        }

        // ==================== 波次与敌人模拟 ====================

        private IEnumerator WaveLoop()
        {
            while (m_Service != null && m_Service.Mode == NetMode.Host)
            {
                m_WaveIndex++;

                // 广播波次开始
                var waveStart = new S2CWaveState { WaveIndex = m_WaveIndex, AliveCount = 0, WaveActive = true };
                m_Service.BroadcastToClients(waveStart);
                Debug.Log("[NetHostLogic] 第 " + m_WaveIndex + " 波开始");

                // 生成敌人
                int count = m_EnemiesPerWave + (m_WaveIndex - 1) * 2;
                for (int i = 0; i < count; i++)
                {
                    SpawnEnemy();
                    yield return new WaitForSeconds(0.5f);
                }

                // 等待敌人清完
                while (GetAliveEnemyCount() > 0)
                {
                    yield return new WaitForSeconds(0.5f);
                }

                // 波次结束
                var waveEnd = new S2CWaveState { WaveIndex = m_WaveIndex, AliveCount = 0, WaveActive = false };
                m_Service.BroadcastToClients(waveEnd);
                Debug.Log("[NetHostLogic] 第 " + m_WaveIndex + " 波结束");

                yield return new WaitForSeconds(2f);
            }
        }

        private void SpawnEnemy()
        {
            Vector3 playerPos = GetFirstPlayerPosition();
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            Vector2 spawnPos = (Vector2)playerPos + randomDir * m_SpawnRadius;

            var enemy = new EnemyState
            {
                EntityId = m_NextEntityId++,
                Position = spawnPos,
                Hp = 30f + m_WaveIndex * 5f,
                Speed = 2.5f + m_WaveIndex * 0.3f,
            };
            m_Enemies[enemy.EntityId] = enemy;

            // 广播敌人生成
            var spawnMsg = new S2CSpawnEntity
            {
                EntityId = enemy.EntityId,
                Type = 1,
                Team = 2,
                X = enemy.Position.x,
                Y = enemy.Position.y,
            };
            m_Service.BroadcastToClients(spawnMsg);

            // 立即广播一次状态
            BroadcastEntityState(enemy.EntityId, enemy.Position, enemy.Hp, 1);
        }

        private void UpdateEnemies()
        {
            if (m_Enemies.Count == 0)
            {
                return;
            }

            // 收集待移除
            var toRemove = new List<int>();

            foreach (var kv in m_Enemies)
            {
                var enemy = kv.Value;
                if (!enemy.Alive)
                {
                    toRemove.Add(enemy.EntityId);
                    continue;
                }

                // 追逐最近玩家
                var target = GetNearestPlayer(enemy.Position);
                if (target != null)
                {
                    Vector2 toTarget = target.Position - enemy.Position;
                    if (toTarget.sqrMagnitude > 0.01f)
                    {
                        enemy.Position += toTarget.normalized * enemy.Speed * Time.deltaTime;
                    }

                    // 接触伤害（简化：靠近玩家扣血）
                    if (toTarget.magnitude < 0.6f)
                    {
                        target.Hp = Mathf.Max(0f, target.Hp - 10f);
                        BroadcastEntityState(target.EntityId, target.Position, target.Hp, 1);
                    }
                }

                BroadcastEntityState(enemy.EntityId, enemy.Position, enemy.Hp, 1);
            }

            foreach (var id in toRemove)
            {
                m_Enemies.Remove(id);
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = id });
            }
        }

        /// <summary>
        /// 服务器权威击杀敌人（供测试/客户端击杀请求调用）。
        /// 简化：按实体 ID 直接移除并广播。
        /// </summary>
        public void KillEnemy(int entityId)
        {
            if (m_Enemies.Remove(entityId))
            {
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = entityId });
                Debug.Log("[NetHostLogic] 敌人 " + entityId + " 被击杀");
            }
        }

        private int GetAliveEnemyCount()
        {
            int count = 0;
            foreach (var enemy in m_Enemies.Values)
            {
                if (enemy.Alive)
                {
                    count++;
                }
            }
            return count;
        }

        private Vector3 GetFirstPlayerPosition()
        {
            foreach (var player in m_Players.Values)
            {
                return player.Position;
            }
            return Vector3.zero;
        }

        private PlayerState GetNearestPlayer(Vector2 position)
        {
            PlayerState nearest = null;
            float minDistSqr = float.MaxValue;
            foreach (var player in m_Players.Values)
            {
                float distSqr = (player.Position - position).sqrMagnitude;
                if (distSqr < minDistSqr)
                {
                    minDistSqr = distSqr;
                    nearest = player;
                }
            }
            return nearest;
        }

        private void BroadcastEntityState(int entityId, Vector2 position, float hp, int state)
        {
            if (m_Service == null)
            {
                return;
            }

            var msg = new S2CEntityState
            {
                EntityId = entityId,
                X = position.x,
                Y = position.y,
                Hp = hp,
                State = state,
            };
            m_Service.BroadcastToClients(msg);
        }

        private void StopWave()
        {
            if (m_WaveCoroutine != null)
            {
                StopCoroutine(m_WaveCoroutine);
                m_WaveCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            StopWave();
        }
    }
}
