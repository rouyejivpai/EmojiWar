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

        /// <summary>当前服务器波次（测试/诊断用）。</summary>
        public int WaveIndex { get { return m_WaveIndex; } }

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

                case MsgId.BuyItem:
                    HandleBuyItem(sessionId, message as C2SBuyItem);
                    break;

                case MsgId.LeaveRoom:
                    HandleLeave(sessionId);
                    break;
            }
        }

        /// <summary>
        /// 处理购买请求（Host 权威校验）。
        /// 简化：无金币系统，直接确认购买并广播。
        /// </summary>
        private void HandleBuyItem(int sessionId, C2SBuyItem buy)
        {
            if (buy == null)
            {
                return;
            }

            Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 购买商品索引 " + buy.ShopItemIndex);
            m_Service.SendToClient(sessionId, new S2CShopOffer { Count = 0, Items = "BUY_OK" });
        }

        private void HandleJoin(int sessionId, C2SJoinRoom join)
        {
            if (join == null)
            {
                return;
            }

            // 幂等：同一连接重复加入时，先移除旧实体并广播删除，避免残留多个玩家实体
            if (m_Players.TryGetValue(sessionId, out var oldState))
            {
                m_Players.Remove(sessionId);
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = oldState.EntityId });
                Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 重复加入，移除旧实体 " + oldState.EntityId);
            }

            var state = new PlayerState
            {
                SessionId = sessionId,
                EntityId = m_NextEntityId++,
                Position = new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)),
            };
            m_Players[sessionId] = state;

            // 先告知新加入者"自己的实体 ID"，再广播 spawn —— 否则客户端会先渲染自己（误当成其他玩家）
            m_Service.SendToClient(sessionId, new S2CMyEntity { EntityId = state.EntityId });

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

            // 广播已有玩家实体给新加入者（含房主），保证晚进客户端的可见性
            foreach (var kv in m_Players)
            {
                if (kv.Key == sessionId)
                {
                    continue;
                }
                var p = kv.Value;
                m_Service.SendToClient(sessionId, new S2CSpawnEntity
                {
                    EntityId = p.EntityId,
                    Type = 0,
                    Team = 1,
                    X = p.Position.x,
                    Y = p.Position.y,
                });
            }

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
                int safety = 0;
                while (GetAliveEnemyCount() > 0 && safety < 60)
                {
                    safety++;
                    yield return new WaitForSeconds(0.5f);
                }
                Debug.Log("[NetHostLogic] 波次敌人已清完，alive=" + GetAliveEnemyCount());

                // 波次结束
                var waveEnd = new S2CWaveState { WaveIndex = m_WaveIndex, AliveCount = 0, WaveActive = false };
                m_Service.BroadcastToClients(waveEnd);
                Debug.Log("[NetHostLogic] 第 " + m_WaveIndex + " 波结束，广播波次状态");

                // 波间商店（Host 权威生成商品，广播给所有客户端）
                BroadcastShopOffer();
                Debug.Log("[NetHostLogic] 波间商店已开放");

                // 商店开放 8 秒后继续下一波
                yield return new WaitForSeconds(8f);
            }
        }

        /// <summary>
        /// Host 权威生成商店商品并广播。
        /// 商品格式："type:id:price;type:id:price;..."
        /// </summary>
        private void BroadcastShopOffer()
        {
            if (m_Service == null)
            {
                return;
            }

            var items = new System.Text.StringBuilder();
            int count = 3;
            for (int i = 0; i < count; i++)
            {
                // 50% 武器(0) / 50% Mod(1)
                int type = Random.value < 0.5f ? 0 : 1;
                int id = type == 0 ? Random.Range(1, 3) : Random.Range(1, 5);
                int price = type == 0 ? 80 : 60;

                if (i > 0)
                {
                    items.Append(';');
                }
                items.Append(type).Append(':').Append(id).Append(':').Append(price);
            }

            var offer = new S2CShopOffer { Count = count, Items = items.ToString() };
            m_Service.BroadcastToClients(offer);
            Debug.Log("[NetHostLogic] 商店商品: " + offer.Items);
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

        /// <summary>
        /// 服务器权威清场：击杀所有敌人（供测试/波次推进）。
        /// </summary>
        public void KillAllEnemies()
        {
            var ids = new List<int>(m_Enemies.Keys);
            foreach (var id in ids)
            {
                if (m_Enemies.TryGetValue(id, out var enemy) && enemy.Alive)
                {
                    enemy.Alive = false;
                }
            }

            // 立即广播移除
            foreach (var id in ids)
            {
                m_Enemies.Remove(id);
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = id });
            }

            Debug.Log("[NetHostLogic] 已清场 " + ids.Count + " 个敌人");
        }

        /// <summary>
        /// Reset the run for a new round (host authority):
        /// clear all enemies, reset wave index and player HP,
        /// broadcast entity removals + S2CRunRestart, then restart wave 1.
        /// </summary>
        public void ResetRunAndBroadcast()
        {
            StopWave();

            // Remove all enemies and notify every client.
            var enemyIds = new List<int>(m_Enemies.Keys);
            foreach (var id in enemyIds)
            {
                m_Enemies.Remove(id);
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = id });
            }

            m_WaveIndex = 0;

            // Reset server-authoritative player HP and rebroadcast state.
            foreach (var player in m_Players.Values)
            {
                player.Hp = 100f;
                BroadcastEntityState(player.EntityId, player.Position, player.Hp, 1);
            }

            // Notify all clients: new round begins.
            m_Service.BroadcastToClients(new S2CRunRestart { Seed = Random.Range(0, 100000) });

            // Re-broadcast all player spawns so clients rebuild their views after clearing.
            foreach (var kv in m_Players)
            {
                var p = kv.Value;
                m_Service.BroadcastToClients(new S2CSpawnEntity
                {
                    EntityId = p.EntityId,
                    Type = 0,
                    Team = 1,
                    X = p.Position.x,
                    Y = p.Position.y,
                });
            }

            Debug.Log("[NetHostLogic] Run reset: enemies=" + enemyIds.Count + " players=" + m_Players.Count + ", broadcast S2CRunRestart");

            // Restart wave 1 for the new round.
            if (m_Players.Count >= 1 && m_WaveCoroutine == null)
            {
                m_WaveCoroutine = StartCoroutine(WaveLoop());
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
