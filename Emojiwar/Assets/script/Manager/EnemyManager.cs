using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Game.Data;

// 敌人生成数据

public class EnemyRunData
{
    public string enemyId;                    // 敌人ID
    public GameObject enemyPrefab;            // 敌人预制体（运行时设置）
    public int maxCount = 10;                // 最大数量
    public float spawnInterval = 2f;         // 生成间隔
    public float spawnRadius = 15f;          // 生成半径
    public int currentCount = 0;             // 当前数量

    public float lastSpawnTime;
}



// // 敌人基本信息
[System.Serializable]
public class EnemyBasicInfo
{
    public string enemyId;                    // 敌人ID
    public string enemyName;                  // 敌人名称
    public string prefabPath;                 // 预制体路径
}

// 敌人预制体映射
[System.Serializable]
public class EnemyPrefabMapping
{
    public string enemyId;                    // 敌人ID
    public GameObject enemyPrefab;            // 敌人预制体
}




public class EnemyManager : MonoBehaviour
{
    [Header("单例设置")]
    private static EnemyManager _instance;
    public static EnemyManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<EnemyManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("EnemyManager");
                    _instance = go.AddComponent<EnemyManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    [Header("敌人预制体映射")]
    public List<EnemyPrefabMapping> enemyPrefabMappings = new List<EnemyPrefabMapping>();
    
    [Header("敌人基本信息列表")]
    public List<EnemyBasicInfo> enemyBasicInfoList = new List<EnemyBasicInfo>();
    
    private EnemyCatalog _enemyCatalog;
    public EnemyCatalog enemyCatalog{
        get{
            if(_enemyCatalog == null)
            {
                return GameManager.Instance.enemyCatalog;
            }
            return _enemyCatalog;
        }
        set{
            _enemyCatalog = value;
        }
    }
    [Header("JSON配置文件路径")]
    [SerializeField] private string enemyConfigPath = "enemy_config.json";
    //[SerializeField] private BattleConfigAsset battleConfigAsset; // 迁移后的战斗配置
    [SerializeField] private string battleConfigResourcesPath = "Configs/BattleConfig";
    [Header("战斗流程配置")]
    public List<BattlePhase> battlePhases = new List<BattlePhase>();
    public bool autoStartBattle = true;
    
    [Header("敌人预制体")]
    public List<EnemySpawnData> enemyTypes = new List<EnemySpawnData>();
    
    [Header("刷怪设置")]
    public bool isSpawning = false;
    public float spawnDuration = 30f;
    public float spawnDelay = 5f;
    
    [Header("生成区域设置")]
    public float minSpawnDistance = 12f;
    public float maxSpawnDistance = 20f;
    public LayerMask obstacleLayer = 1;
    
    
    public GameObject playertarget{
        get{
           
            
                return GameManager.Instance.player;
           
        }
     
    }
    
    [Header("调试信息")]
    public int totalEnemiesSpawned = 0;
    public float spawnTimer = 0f;
    public int currentPhaseIndex = -1;
    public bool isBattleActive = false;
    public BattleConfig currentBattleConfig;
    
    // 事件系统
    public static event Action OnBattleStart;
    public static event Action<int> OnPhaseStart;
    public static event Action<int> OnPhaseComplete;
    public static event Action OnBattleComplete;
    public static event Action OnLevelComplete;
    public static event Action<BattleConfig> OnConfigLoaded;
    
    // 私有变量
    private Camera mainCamera;
    private List<GameObject> activeEnemies = new List<GameObject>();
    private Dictionary<String ,EnemyRunData> enemyrundata= new Dictionary<String,EnemyRunData>();
    private Coroutine spawnCoroutine;
    private Coroutine battleCoroutine;
    private int totalEnemiesKilled = 0;
    private Dictionary<int, int> phaseKillCounts = new Dictionary<int, int>();
    
    private void Awake()
    {
        // 单例模式设置
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // 获取主摄像机
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }
        GameManager.Instance.onGameStateChanged+= (GameState state)=>
        {
            if(state==GameState.Fighting)
            {
                StartBattle();
            }
        };
    }
    
    private void Start()
    {
        // 加载敌人基本信息
       // LoadEnemyInfo();
        // 生成时间记录将在阶段启动时按需初始化到 enemyrundata

        // 初始化阶段击杀计数
        for (int i = 0; i < battlePhases.Count; i++)
        {
            phaseKillCounts[i] = 0;
        }

        // 加载战斗配置
        LoadBattleConfig();
    }
    
    private void Update()
    {
        if (isSpawning)
        {
            UpdateSpawnTimer();
        }
        
        // 检查过关条件
        CheckLevelCompleteCondition();
    }
    
    /// <summary>
    /// 根据敌人ID获取预制体
    /// </summary>
    public GameObject GetEnemyPrefabById(string enemyId)
    {
        // 资产优先：直接从 EnemyCatalog 取预制体
        if (enemyCatalog != null && enemyCatalog.enemies != null)
        {
            var assetEntry = enemyCatalog.enemies.Find(e => e.enemyId == enemyId);
            if (assetEntry != null)
            {
                if (assetEntry.prefab != null)
                {
                    return assetEntry.prefab;
                }
                if (!string.IsNullOrEmpty(assetEntry.prefabPath))
                {
                    var prefab = Resources.Load<GameObject>(assetEntry.prefabPath);
                    if (prefab != null)
                    {
                        // 缓存到映射，减少后续查找
                        var existing = enemyPrefabMappings.Find(m => m.enemyId == enemyId);
                        if (existing != null) existing.enemyPrefab = prefab;
                        else enemyPrefabMappings.Add(new EnemyPrefabMapping{ enemyId = enemyId, enemyPrefab = prefab });
                        return prefab;
                    }
                }
            }
        }

        // 资产不可用或未命中时，回退到映射/路径加载
        var mapping = enemyPrefabMappings.Find(m => m.enemyId == enemyId);
        if (mapping != null && mapping.enemyPrefab != null)
        {
            return mapping.enemyPrefab;
        }
        //LoadEnemyPrefabById(enemyId);
        mapping = enemyPrefabMappings.Find(m => m.enemyId == enemyId);
        return mapping?.enemyPrefab;
    }
    
    
    /// <summary>
    /// 清空所有预制体映射
    /// </summary>
    public void ClearEnemyPrefabMappings()
    {
        enemyPrefabMappings.Clear();
        Debug.Log("[EnemyManager] 清空所有敌人预制体映射");
    }
    
    /// <summary>
    /// 加载敌人信息 - 使用资产配置
    /// </summary>
//     public void LoadEnemyInfo()

    
    
    
    /// <summary>
    /// 加载战斗配置
    /// </summary>
    public void LoadBattleConfig()
    {
        StartCoroutine(LoadBattleConfigCoroutine());
    }
    
    /// <summary>
    /// 加载战斗配置协程 - 使用资产配置
    /// </summary>
    private IEnumerator LoadBattleConfigCoroutine()
    {
        // 使用 GameManager 中配置的 BattleAsset
        var battleAsset = GameManager.Instance != null ? GameManager.Instance.battleAsset : null;
        if (battleAsset == null || battleAsset.phases == null || battleAsset.phases.Count == 0)
        {
            Debug.LogWarning("[EnemyManager] 未配置 BattleAsset 资产，使用默认战斗配置");
            CreateDefaultBattleConfig();
            yield return null;
            yield break;
        }

        // 选择一个战斗资产：优先选择 autoStart=true 的条目，否则使用第一个
        Game.Data.BattleConfig chosen = null;
        foreach (var p in battleAsset.phases)
        {
            if (p != null && p.autoStart)
            {
                chosen = p;
                break;
            }
        }
        if (chosen == null)
        {
            chosen = battleAsset.phases[0];
        }

        var config = chosen;
        ApplyBattleConfig(config);
        Debug.Log($"[EnemyManager] 成功从资产加载战斗配置: {config.battleName}");
        
        yield return null;
    }

    // private BattleConfig BuildBattleConfigFromAsset(BattleConfig asset)
    // {
    //     var cfg = new BattleConfig
    //     {
    //         battleName = asset.battleName,
    //         description = asset.description,
    //         totalDuration = asset.totalDuration,
    //         autoStart = asset.autoStart,
    //         phases = new List<BattlePhase>()
    //     };

    //     foreach (var p in asset.phases)
    //     {
    //         var newPhase = new BattlePhase
    //         {
    //             phaseName = p.phaseName,
    //             phaseDuration = p.phaseDuration,
    //             phaseDelay = p.phaseDelay,
    //             waitForClear = p.waitForClear,
    //             requiredKills = p.requiredKills,
    //             enemies = new List<EnemySpawnData>()
    //         };
    //         foreach (var e in p.enemies)
    //         {
    //             newPhase.enemies.Add(new EnemySpawnData
    //             {
    //                 enemyId = e.enemyId,
    //                 maxCount = e.maxCount,
    //                 spawnInterval = e.spawnInterval,
    //                 spawnRadius = e.spawnRadius,
    //                 currentCount = 0
    //             });
    //         }
    //         cfg.phases.Add(newPhase);
    //     }

    //     return cfg;
    // }
    

    /// <summary>
    /// 创建默认战斗配置
    /// </summary>
    private void CreateDefaultBattleConfig()
    {
        BattleConfig defaultConfig = new BattleConfig
        {
            battleName = "默认战斗",
            description = "这是一个默认的战斗配置",
            phases = new List<BattlePhase>
            {
                new BattlePhase
                {
                    phaseName = "第一阶段",
                    phaseDuration = 20f,
                    phaseDelay = 2f,
                    waitForClear = false,
                    requiredKills = 0,
                    enemies = new List<EnemySpawnData>()
                }
            },
            totalDuration = 0f,
            autoStart = false
        };
        
        string jsonContent = JsonUtility.ToJson(defaultConfig, true);
        string filePath = Path.Combine(Application.persistentDataPath, "battle_config.json");
        
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, jsonContent);
            Debug.Log($"[EnemyManager] 创建默认战斗配置文件: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[EnemyManager] 创建默认战斗配置文件失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 应用战斗配置
    /// </summary>
    private void ApplyBattleConfig(BattleConfig config)
    {
        currentBattleConfig = config;
        
        // 清空现有配置
        battlePhases.Clear();
        enemyTypes.Clear();
        enemyrundata.Clear();
        // 应用新配置
        foreach (var phase in config.phases)
        {
            var newPhase = new BattlePhase
            {
                phaseName = phase.phaseName,
                phaseDuration = phase.phaseDuration,
                phaseDelay = phase.phaseDelay,
                waitForClear = phase.waitForClear,
                requiredKills = phase.requiredKills,
                enemies = new List<EnemySpawnData>()
            };
            
            // 处理敌人配置
            foreach (var enemyData in phase.enemies)
            {
                // 根据enemyId查找对应的预制体
                GameObject prefab = GetEnemyPrefabById(enemyData.enemyId);
                if (prefab == null)
                {
                    Debug.LogWarning($"[EnemyManager] 跳过敌人配置，未找到预制体: {enemyData.enemyId}");
                    continue;
                }
                
                var newEnemyData = new EnemySpawnData
                {
                    enemyId = enemyData.enemyId,
                    
                    maxCount = enemyData.maxCount,
                    spawnInterval = enemyData.spawnInterval,
                    spawnRadius = enemyData.spawnRadius
                };
                
                newPhase.enemies.Add(newEnemyData);
            }
            
            battlePhases.Add(newPhase);
        }
        
        // 重新初始化击杀计数
        phaseKillCounts.Clear();
        for (int i = 0; i < battlePhases.Count; i++)
        {
            phaseKillCounts[i] = 0;
        }
        
        // 触发配置加载完成事件
        OnConfigLoaded?.Invoke(config);
        
        // 如果配置为自动开始，则开始战斗
        if (config.autoStart)
        {
            StartBattle();
        }
    }
    
    /// <summary>
    /// 开始战斗
    /// </summary>
    public void StartBattle()
    {
        if (isBattleActive) return;
        
//        Debug.Log("[EnemyManager] 开始战斗");
        isBattleActive = true;//战斗开始
        currentPhaseIndex = -1;//当前阶段索引
        totalEnemiesKilled = 0;//总击杀数   
        
        // 重置所有阶段击杀计数
        for (int i = 0; i < battlePhases.Count; i++)
        {
            phaseKillCounts[i] = 0;//重置阶段击杀计数       
        }
        
        // 触发战斗开始事件
        OnBattleStart?.Invoke();
        
        // 开始战斗流程
        if (battleCoroutine != null)
        {
            StopCoroutine(battleCoroutine);//停止战斗流程
        }
        battleCoroutine = StartCoroutine(BattleRoutine());//开始战斗流程
    }
    
    /// <summary>
    /// 停止战斗
    /// </summary>
    public void StopBattle()
    {
        if (!isBattleActive) return;
        
        Debug.Log("[EnemyManager] 停止战斗");
        isBattleActive = false;
        
        if (battleCoroutine != null)
        {
            StopCoroutine(battleCoroutine);
            battleCoroutine = null;
        }
        
        StopSpawning();
    }
    
    /// <summary>
    /// 战斗流程协程
    /// </summary>
    private IEnumerator BattleRoutine()
    {
        // 遍历所有战斗阶段
        for (int i = 0; i < battlePhases.Count; i++)
        {
            currentPhaseIndex = i;
            var phase = battlePhases[i];
            
           // Debug.Log($"[EnemyManager] 开始阶段: {phase.phaseName}");
            
            // 触发阶段开始事件
            OnPhaseStart?.Invoke(i);
            
            // 等待阶段延迟
            if (phase.phaseDelay > 0)
            {
                yield return new WaitForSeconds(phase.phaseDelay);
            }
            
            // 开始该阶段的刷怪
            yield return StartCoroutine(PhaseSpawnRoutine(phase));
            
            // 如果该阶段需要等待敌人全部死亡
            if (phase.waitForClear)
            {
                yield return StartCoroutine(WaitForPhaseClear(phase));
            }
            
           // Debug.Log($"[EnemyManager] 完成阶段: {phase.phaseName}");
            
            // 触发阶段完成事件
            OnPhaseComplete?.Invoke(i);
        }
        
        // 战斗完成
        isBattleActive = false;
//        Debug.Log("[EnemyManager] 战斗完成");
        
        // 触发战斗完成事件
        OnBattleComplete?.Invoke();
        
        // 等待所有敌人死亡
        yield return StartCoroutine(WaitForAllEnemiesDead());
        
        // 触发过关事件
        OnLevelComplete?.Invoke();
        GameManager.Instance.ChangeGameState(GameState.Store);
        Debug.Log("[EnemyManager] 过关！");
    }
    
    /// <summary>
    /// 阶段刷怪协程
    /// </summary>
    private IEnumerator PhaseSpawnRoutine(BattlePhase phase)
    {
        // 设置刷怪参数
        SetSpawnParameters(phase.phaseDuration, 0f);
        
        // 清空并初始化运行期数据：集中维护到 enemyrundata
        enemyrundata.Clear();
        foreach (var enemyData in phase.enemies)
        {
            var run = new EnemyRunData
            {
                enemyId = enemyData.enemyId,
                maxCount = enemyData.maxCount,
                spawnInterval = enemyData.spawnInterval,
                spawnRadius = enemyData.spawnRadius,
                currentCount = 0,
                lastSpawnTime = 0f,
                enemyPrefab = GetEnemyPrefabById(enemyData.enemyId)
            };
            enemyrundata[enemyData.enemyId] = run;
        }
        
        // 开始刷怪
        StartSpawning();
        
        // 等待阶段时间结束
        float phaseTimer = phase.phaseDuration;
        while (phaseTimer > 0 && isBattleActive)
        {
            phaseTimer -= Time.deltaTime;
            yield return null;
        }
        
        // 停止刷怪
        StopSpawning();
    }
    
    /// <summary>
    /// 等待阶段敌人全部死亡
    /// </summary>
    private IEnumerator WaitForPhaseClear(BattlePhase phase)
    {
        while (true)
        {
            // 检查该阶段是否还有敌人存活
            bool hasEnemiesAlive = false;
            foreach (var kv in enemyrundata)
            {
                if (kv.Value.currentCount > 0)
                {
                    hasEnemiesAlive = true;
                    break;
                }
            }
            
            if (!hasEnemiesAlive)
            {
                break;
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    /// <summary>
    /// 等待所有敌人死亡
    /// </summary>
    private IEnumerator WaitForAllEnemiesDead()
    {
        while (activeEnemies.Count > 0)
        {
            // 清理已销毁的敌人
            activeEnemies.RemoveAll(enemy => enemy == null);
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    /// <summary>
    /// 检查过关条件
    /// </summary>
    private void CheckLevelCompleteCondition()
    {
        // 如果战斗未激活或还有敌人存活，不检查过关条件
        if (!isBattleActive || activeEnemies.Count > 0)
        {
            return;
        }
        
        // 检查是否所有阶段都已完成
        if (currentPhaseIndex >= battlePhases.Count - 1)
        {
            // 可以在这里添加额外的过关条件检查
            // 例如：检查是否达到特定击杀数量、时间限制等
        }
    }
    
    /// <summary>
    /// 记录敌人死亡
    /// </summary>
    public void RecordEnemyDeath(GameObject enemy)
    {
        totalEnemiesKilled++;
        
        // 更新当前阶段的击杀计数
        if (currentPhaseIndex >= 0 && currentPhaseIndex < battlePhases.Count)
        {
            phaseKillCounts[currentPhaseIndex]++;
        }
        
        Debug.Log($"[EnemyManager] 敌人死亡，总击杀: {totalEnemiesKilled}，当前阶段击杀: {phaseKillCounts[currentPhaseIndex]}");
    }
    
    /// <summary>
    /// 获取当前阶段信息
    /// </summary>
    public BattlePhase GetCurrentPhase()
    {
        if (currentPhaseIndex >= 0 && currentPhaseIndex < battlePhases.Count)
        {
            return battlePhases[currentPhaseIndex];
        }
        return null;
    }
    
    /// <summary>
    /// 获取战斗进度（0-1）
    /// </summary>
    public float GetBattleProgress()
    {
        if (battlePhases.Count == 0) return 0f;
        return (float)(currentPhaseIndex + 1) / battlePhases.Count;
    }
    
    /// <summary>
    /// 获取当前阶段进度（0-1）
    /// </summary>
    public float GetCurrentPhaseProgress()
    {
        var currentPhase = GetCurrentPhase();
        if (currentPhase == null) return 0f;
        
        if (currentPhase.phaseDuration <= 0) return 0f;
        return 1f - (spawnTimer / currentPhase.phaseDuration);
    }
    
    /// <summary>
    /// 开始刷怪进程
    /// </summary>
    public void StartSpawning()
    {
        if (isSpawning) return;
        
        Debug.Log("[EnemyManager] 开始刷怪进程");
        isSpawning = true;//开始刷怪
        spawnTimer = spawnDuration;//刷怪计时器
        
        if (spawnCoroutine != null)//如果刷怪协程不为空
        {
            StopCoroutine(spawnCoroutine);//停止刷怪协程
        }
        spawnCoroutine = StartCoroutine(SpawnRoutine());//开始刷怪协程
    }
    
    /// <summary>
    /// 停止刷怪进程
    /// </summary>
    public void StopSpawning()
    {
        if (!isSpawning) return;
        
        Debug.Log("[EnemyManager] 停止刷怪进程");
        isSpawning = false;
        
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }
    
    /// <summary>
    /// 刷怪协程
    /// </summary>
    private IEnumerator SpawnRoutine()
    {
        // 等待开始延迟
        yield return new WaitForSeconds(spawnDelay);
        
        while (isSpawning && spawnTimer > 0)
        {
            // 尝试生成敌人
            TrySpawnEnemies();
            
            // 等待下一帧
            yield return null;
        }
        
        // 刷怪结束
        isSpawning = false;
        Debug.Log("[EnemyManager] 刷怪进程结束");
    }
    
    /// <summary>
    /// 更新刷怪计时器
    /// </summary>
    private void UpdateSpawnTimer()
    {
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0)
        {
            StopSpawning();
        }
    }
    
    /// <summary>
    /// 尝试生成敌人
    /// </summary>
    private void TrySpawnEnemies()
    {
        // 遍历运行期字典
        foreach (var kv in enemyrundata)
        {
            var run = kv.Value;
            if (run.enemyPrefab == null) continue;
            if (run.currentCount >= run.maxCount) continue;
            if (Time.time - run.lastSpawnTime >= run.spawnInterval)
            {
                if (TrySpawnEnemy(run))
                {
                    run.lastSpawnTime = Time.time;
                    run.currentCount++;
                    totalEnemiesSpawned++;
                }
            }
        }
    }
    
    /// <summary>
    /// 尝试生成单个敌人
    /// </summary>
    private bool TrySpawnEnemy(EnemyRunData run)
    {
        Vector2 spawnPosition = GetValidSpawnPosition();
        if (spawnPosition == Vector2.zero) return false;
        
        // 生成敌人
        GameObject enemy = Instantiate(run.enemyPrefab, spawnPosition, Quaternion.identity);
        // 标记敌人ID以便统计与移除
        var idComp = enemy.GetComponent<EnemyIdentifier>();
        if (idComp == null) idComp = enemy.AddComponent<EnemyIdentifier>();
        idComp.enemyId = run.enemyId;
        
        // 确保有 BuffSystem 并添加死亡低语 Buff（永久1层）
        var buffSystem = enemy.GetComponent<BuffSystem>();
        if (buffSystem == null) buffSystem = enemy.AddComponent<BuffSystem>();
        buffSystem.AddBuff<Deathwhisper>(-1f, 1);
        
        activeEnemies.Add(enemy);
        
        //Debug.Log($"[EnemyManager] 生成敌人: {enemy.name} 在位置: {spawnPosition}");
        return true;
    }
    
    /// <summary>
    /// 获取有效的生成位置（摄像机视野外）
    /// </summary>
    private Vector2 GetValidSpawnPosition()
    {
        if (mainCamera == null) return Vector2.zero;
        
        Vector2 cameraPos = mainCamera.transform.position;
        
        // 尝试多次找到有效位置
        for (int attempts = 0; attempts < 10; attempts++)
        {
            // 随机角度
            float randomAngle = UnityEngine.Random.Range(0f, 360f);
            float randomDistance = UnityEngine.Random.Range(minSpawnDistance, maxSpawnDistance);
            
            // 计算生成位置
            Vector2 spawnPos = cameraPos + new Vector2(
                Mathf.Cos(randomAngle * Mathf.Deg2Rad) * randomDistance,
                Mathf.Sin(randomAngle * Mathf.Deg2Rad) * randomDistance
            );
            
            // 检查位置是否有效（不在障碍物内）
            if (IsValidSpawnPosition(spawnPos))
            {
                return spawnPos;
            }
        }
        
        return Vector2.zero;
    }
    
    /// <summary>
    /// 检查生成位置是否有效
    /// </summary>
    private bool IsValidSpawnPosition(Vector2 position)
    {
        // 检查是否在障碍物内
        Collider2D obstacle = Physics2D.OverlapPoint(position, obstacleLayer);
        if (obstacle != null) return false;
        
        // 检查是否与其他敌人重叠
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null && Vector2.Distance(position, enemy.transform.position) < 2f)
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 手动生成敌人
    /// </summary>
    public GameObject SpawnEnemy(string enemyId, Vector2 position)
    {
        GameObject prefab = GetEnemyPrefabById(enemyId);
        if (prefab == null) return null;
        
        GameObject enemy = Instantiate(prefab, position, Quaternion.identity);
        
        // 确保有 BuffSystem 并添加死亡低语 Buff（永久1层）
        var buffSystem = enemy.GetComponent<BuffSystem>();
        if (buffSystem == null) buffSystem = enemy.AddComponent<BuffSystem>();
        buffSystem.AddBuff<Deathwhisper>(-1f, 1);
        
        activeEnemies.Add(enemy);
        
        totalEnemiesSpawned++;
        return enemy;
    }
    
    /// <summary>
    /// 移除敌人（当敌人死亡时调用）
    /// </summary>
    public void RemoveEnemy(GameObject enemy)
    {
        if (enemy == null) return;
        
        activeEnemies.Remove(enemy);
        
        // 更新计数
        var idComp = enemy.GetComponent<EnemyIdentifier>();
        if (idComp != null && !string.IsNullOrEmpty(idComp.enemyId))
        {
            if (enemyrundata.TryGetValue(idComp.enemyId, out var run))
            {
                run.currentCount = Mathf.Max(0, run.currentCount - 1);
            }
        }
        
        // 记录敌人死亡
        RecordEnemyDeath(enemy);
    }
    
    /// <summary>
    /// 清理所有敌人
    /// </summary>
    public void ClearAllEnemies()
    {
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy);
            }
        }
        
        activeEnemies.Clear();
        
        // 重置运行期计数
        foreach (var kv in enemyrundata)
        {
            kv.Value.currentCount = 0;
            kv.Value.lastSpawnTime = 0f;
        }
        
        totalEnemiesSpawned = 0;
    }
    
    /// <summary>
    /// 获取当前活跃敌人数量
    /// </summary>
    public int GetActiveEnemyCount()
    {
        return activeEnemies.Count;
    }
    
    /// <summary>
    /// 获取刷怪进度（0-1）
    /// </summary>
    public float GetSpawnProgress()
    {
        if (spawnDuration <= 0) return 0f;
        return 1f - (spawnTimer / spawnDuration);
    }
    
    /// <summary>
    /// 设置刷怪参数
    /// </summary>
    public void SetSpawnParameters(float duration, float delay)
    {
        spawnDuration = duration;
        spawnDelay = delay;
    }
    
    /// <summary>
    /// 获取所有可用的敌人ID
    /// </summary>
    public List<string> GetAllEnemyIds()
    {
        List<string> ids = new List<string>();
        foreach (var mapping in enemyPrefabMappings)
        {
            if (mapping.enemyPrefab != null)
            {
                ids.Add(mapping.enemyId);
            }
        }
        return ids;
    }
    
    /// <summary>
    /// 检查敌人配置是否完整
    /// </summary>
    public bool ValidateEnemyConfig(string enemyId)
    {
        bool hasPrefab = GetEnemyPrefabById(enemyId) != null;
        
        if (!hasPrefab)
        {
            Debug.LogWarning($"[EnemyManager] 敌人 {enemyId} 缺少预制体");
        }
        
        return hasPrefab;
    }
    
    /// <summary>
    /// 打印敌人配置信息
    /// </summary>
    [ContextMenu("打印敌人配置信息")]
    public void PrintEnemyConfigs()
    {
        Debug.Log("=== 敌人配置信息 ===");
        foreach (var mapping in enemyPrefabMappings)
        {
            string prefabStatus = mapping.enemyPrefab != null ? "✓" : "✗";
            Debug.Log($"{prefabStatus} {mapping.enemyId} -> {(mapping.enemyPrefab != null ? mapping.enemyPrefab.name : "未设置")}");
        }
        Debug.Log("---");
    }
    
    // 绘制生成区域
    private void OnDrawGizmosSelected()
    {
        if (mainCamera == null) return;
        
        // 绘制生成区域
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(mainCamera.transform.position, minSpawnDistance);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(mainCamera.transform.position, maxSpawnDistance);
    }
}
