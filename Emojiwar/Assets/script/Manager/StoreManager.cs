using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Game.Data;

// mod基本信息
// [Serializable]
// public class ModBasicInfo
// {
//     public string modId;           // mod唯一标识符
//     public string modName;         // mod名称
//     public string description;     // mod描述
//     public string prefabPath;      // 预制体路径
//     public string imagePath;       // 图片路径
//     public string category;        // mod分类
//     public int rarity;             // 稀有度 (1-5)
//     public float weight;           // 权重（用于随机抽取）
// }

// mod配置
[Serializable]
public class ModConfig
{
    public List<ModBasicInfo> mods;  // mod列表
}

// mod预制体映射
[Serializable]
public class ModPrefabMapping
{
    public string modId;           // mod ID
    public GameObject modPrefab;   // mod预制体
}

public class StoreManager : MonoBehaviour
{
    private static StoreManager _instance;

    public static StoreManager instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new StoreManager();
            }
            return _instance;
        }
        set { _instance = value; }
    }
      private ModCatalog _modCatalog;
    public ModCatalog modCatalog{
        get{
            if(_modCatalog == null)
            {
                return GameManager.Instance.modCatalog;
            }
            return _modCatalog;
        }
        set{
            _modCatalog = value;
        }
    }
    private WeaponCatalog _weaponCatalog;
    public WeaponCatalog weaponCatalog{
        get{
            if(_weaponCatalog == null)
            {
                return GameManager.Instance.weaponCatalog;
            }
            return _weaponCatalog;
        }
        set{
            _weaponCatalog = value;
        }
    }
    private bool active = false;

    //Obj预制体
    public GameObject ObjPrefab;
    [Header("UI元素")] 
    public GameObject StorePanel; //商店主面板
    public GameObject StoreBackground; //商店背景
    public GameObject tabPanel; //背包
    public GameObject refreshButtun;
    
    [Header("mod池")] 
    public List<GameObject> MODS = new List<GameObject>();

    [Header("权重")] 
    public float weaponWeight = 0.3f;
    public float modWeight = 0.7f;

    [Header("商店格位")] 
    public int count; //商店格位数
    public List<GameObject> Goods = new List<GameObject>();
    

    [Header("JSON配置文件路径")]
    [SerializeField] private string modConfigPath = "mod_config.json";
    
    [Header("mod信息")]
   // public List<ModBasicInfo> modBasicInfoList = new List<ModBasicInfo>();
    public List<ModPrefabMapping> modPrefabMappings = new List<ModPrefabMapping>();
    
    [Header("调试信息")]
    public bool isModsLoaded = false;
    public int totalModsLoaded = 0;

    private void Awake()
    {
        _instance = this;
        GameManager.Instance.onGameStateChanged+= (GameState state) =>
        {
            if (state == GameState.Store)
            {
                enterStore();
            }
        };
    }
    
    private void Start()
    {
        // 加载mod配置
        //LoadModConfig();
    }

    //离开商店页
    public void exit()
    {
        storeSwitch(false);
        EnemyManager.Instance.StartBattle();
    }

    
    //进入商店页
    public void enterStore()
    {
        refresh();//刷新商店
        refreshButtun.GetComponent<refreshButtun>().init();//初始化刷新按钮
        storeSwitch(true);//打开商店页显示
    }

    //开关商店页
    public void storeSwitch(bool active)
    {
        StorePanel.SetActive(active);
        StoreBackground.SetActive(active);
        tabPanel.SetActive(active);
    }

    //刷新方法
    public void refresh()
    {
        List<GameObject> mods = new List<GameObject>();
        //随机出对应个数的模组
        for (int i = 0; i < count; i++)
        {
            mods.Add(rollObj());
        }

        for (int i = 0; i < count; i++)
        {
            GameObject obj = Mod2Obj(mods[i]);
            // 商店中的商品禁用拖拽吸附功能
            obj.GetComponent<SnapDrag>().enabled = false;
            Goods[i].GetComponent<good>().SetContent(obj);
        }
    }

    //将逻辑功能道具包装成obj
    public GameObject Mod2Obj(GameObject mod)
    {
        GameObject obj = Instantiate(ObjPrefab); //创建一个空物体
      
        obj.GetComponent<Obj>().inObj = mod; //obj获取mod引用
        obj.GetComponent<RectTransform>().localScale = Vector3.one;
        return obj;
    }

    //随机抽取一个物体
    public GameObject rollObj(){
        float random = UnityEngine.Random.Range(0, modWeight+weaponWeight);
        if (random < weaponWeight)
        {
            // 抽取武器
            return rollWeapon();
        }
        else
        {
            // 抽取mod
            return rollMod();
        }

    }
    //随机抽取一个mod
    public GameObject rollMod()
    {
        if (MODS.Count == 0)
        {
            Debug.LogWarning("[StoreManager] mod池为空，无法抽取");
            return null;
        }
        
        // 使用权重随机抽取
        return GetWeightedRandomMod();
    }
    
    /// <summary>
    /// 根据权重随机抽取mod
    /// </summary>
    private GameObject GetWeightedRandomMod()
    {
        if (modCatalog.mods.Count == 0) return null;
        
        // 计算总权重
        float totalWeight = 0f;
        foreach (var modInfo in modCatalog.mods)
        {
            totalWeight += modInfo.weight;
        }
        
        if (totalWeight <= 0f) return null;
        
        // 随机权重值
        float randomWeight = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        // 根据权重选择mod
        foreach (var modInfo in modCatalog.mods)
        {
            currentWeight += modInfo.weight;
            if (randomWeight <= currentWeight)
            {
                // 找到对应的预制体
                var mapping = modCatalog.mods.Find(m => m.modId == modInfo.modId);
                if (mapping != null && mapping.modPrefab != null)
                {
                    //记录基本信息
                    GameObject mod = Instantiate(mapping.modPrefab);
                    mod.GetComponent<mod>().modInfo = modInfo;
                    return mod;
                }
            }
        }
        
        // 如果权重选择失败，返回第一个可用的mod
        if (modCatalog.mods.Count > 0)
        {
            var firstMapping = modCatalog.mods[0];
            if (firstMapping.modPrefab != null)
            {
                return Instantiate(firstMapping.modPrefab);
            }
        }
        
        return null;
    }


    //随机抽取一个武器
    public GameObject rollWeapon(){
        return GetWeightedRandomWeapon();
    }
     private GameObject GetWeightedRandomWeapon()
    {
        if (weaponCatalog.weapons.Count == 0) return null;
        
        // 计算总权重
        float totalWeight = 0f;
        foreach (var weaponInfo in weaponCatalog.weapons)
        {
            totalWeight += weaponInfo.weight;
        }
        
        if (totalWeight <= 0f) return null;
        
        // 随机权重值
        float randomWeight = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        // 根据权重选择武器
        foreach (var weaponInfo in weaponCatalog.weapons)
        {
            currentWeight += weaponInfo.weight;
            if (randomWeight <= currentWeight)
            {
                // 找到对应的预制体
                var mapping = weaponCatalog.weapons.Find(w => w.weaponId == weaponInfo.weaponId);
                if (mapping != null && mapping.prefab != null)
                {
                    //记录基本信息
                    GameObject weapon = Instantiate(mapping.prefab);
                    weapon.GetComponent<WeaponBase>().Info = weaponInfo;
                    return weapon;
                }
            }
        }
        
        // 如果权重选择失败，返回第一个可用的武器
        if (weaponCatalog.weapons.Count > 0)
        {
            var firstMapping = weaponCatalog.weapons[0];
            if (firstMapping.prefab != null)
            {
                return Instantiate(firstMapping.prefab);
            }
        }
        
        return null;
    }
    // /// <summary>
    // /// 加载mod配置
    // /// </summary>
    // public void LoadModConfig()
    // {
    //     StartCoroutine(LoadModConfigCoroutine());
    // }
    
    // /// <summary>
    // /// 加载mod配置协程 - 使用资产配置
    // /// </summary>
    // private IEnumerator LoadModConfigCoroutine()
    // {
    //     if (modCatalog == null || modCatalog.mods == null)
    //     {
    //         Debug.LogWarning("[StoreManager] 未配置 ModCatalog 资产，使用默认配置");
    //         CreateDefaultModConfig();
    //         yield return null;
    //         yield break;
    //     }

    //     // 清空现有配置
    //     modBasicInfoList.Clear();
    //     modPrefabMappings.Clear();

    //     foreach (var entry in modCatalog.mods)
    //     {
    //         // 基本信息
    //         var info = new ModBasicInfo
    //         {
    //             modId = entry.modId,
    //             modName = entry.modName,
    //             description = entry.description,
    //             prefabPath = entry.prefabPath,
    //             imagePath = entry.imagePath,
    //             category = entry.category,
    //             rarity = entry.rarity,
    //             weight = entry.weight
    //         };
    //         modBasicInfoList.Add(info);

    //         // 预制体映射：优先使用资产中的直接引用
    //         if (entry.prefab != null)
    //         {
    //             var existing = modPrefabMappings.Find(m => m.modId == entry.modId);
    //             if (existing != null)
    //             {
    //                 existing.modPrefab = entry.prefab;
    //             }
    //             else
    //             {
    //                 modPrefabMappings.Add(new ModPrefabMapping
    //                 {
    //                     modId = entry.modId,
    //                     modPrefab = entry.prefab
    //                 });
    //             }
    //         }
    //         else
    //         {
    //             // Fallback：根据路径尝试加载
    //             LoadModPrefabById(entry.modId);
    //         }
    //     }

    //     // 更新状态
    //     totalModsLoaded = modPrefabMappings.Count;
    //     isModsLoaded = totalModsLoaded > 0;
    //     UpdateMODSList();

    //     Debug.Log($"[StoreManager] 成功从资产加载 mod 配置，共 {modBasicInfoList.Count} 个 mod，已映射 {modPrefabMappings.Count} 个预制体");

    //     yield return null;
    // }
    
    // /// <summary>
    // /// 应用mod配置
    // /// </summary>
    // private void ApplyModConfig(ModConfig config)
    // {
    //     // 清空现有配置
    //     modBasicInfoList.Clear();
    //     modPrefabMappings.Clear();
        
    //     // 应用新配置
    //     foreach (var modInfo in config.mods)
    //     {
    //         modBasicInfoList.Add(modInfo);
            
    //         // 尝试加载预制体
    //         if (LoadModPrefabById(modInfo.modId))
    //         {
    //             Debug.Log($"[StoreManager] 成功加载mod: {modInfo.modId} - {modInfo.modName}");
    //         }
    //         else
    //         {
    //             Debug.LogWarning($"[StoreManager] 无法加载mod预制体: {modInfo.modId}");
    //         }
    //     }
        
    //     // 更新状态
    //     totalModsLoaded = modPrefabMappings.Count;
    //     isModsLoaded = totalModsLoaded > 0;
        
    //     // 更新MODS列表（保持向后兼容）
    //     UpdateMODSList();
        
    //     Debug.Log($"[StoreManager] mod配置应用完成，成功加载 {totalModsLoaded}/{modBasicInfoList.Count} 个mod");
    // }
    
    /// <summary>
    /// 根据mod ID加载预制体
    /// </summary>
    public bool LoadModPrefabById(string modId)
    {
        // 查找mod基本信息
        ModBasicInfo modInfo = modCatalog.mods.Find(m => m.modId == modId);
        if (modInfo == null)
        {
            Debug.LogWarning($"[StoreManager] 未找到mod ID: {modId}");
            return false;
        }
        
        // 加载预制体
        GameObject prefab = LoadModPrefab(modInfo.prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[StoreManager] 无法加载mod预制体: {modId} -> {modInfo.prefabPath}");
            return false;
        }
        
        // 添加到映射列表
        var existingMapping = modPrefabMappings.Find(m => m.modId == modId);
        if (existingMapping != null)
        {
            existingMapping.modPrefab = prefab;
            Debug.Log($"[StoreManager] 更新mod预制体映射: {modId} -> {prefab.name}");
        }
        else
        {
            var newMapping = new ModPrefabMapping
            {
                modId = modId,
                modPrefab = prefab
            };
            modPrefabMappings.Add(newMapping);
            Debug.Log($"[StoreManager] 添加mod预制体映射: {modId} -> {prefab.name}");
        }
        
        return true;
    }
    
    /// <summary>
    /// 用路径加载mod预制体
    /// </summary>
    private GameObject LoadModPrefab(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogWarning("[StoreManager] 预制体路径为空");
            return null;
        }
        
        // 使用Resources.Load加载预制体
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        
        if (prefab == null)
        {
            Debug.LogWarning($"[StoreManager] 无法加载预制体: {prefabPath}");
        }
        
        return prefab;
    }
    
    /// <summary>
    /// 加载所有mod预制体
    /// </summary>
    public void LoadAllModPrefabs()
    {
        Debug.Log("[StoreManager] 开始加载所有mod预制体...");
        
        int successCount = 0;
        int totalCount = modCatalog.mods.Count;
        
        foreach (var modInfo in modCatalog.mods)
        {
            if (LoadModPrefabById(modInfo.modId))
            {
                successCount++;
            }
        }
        
        Debug.Log($"[StoreManager] mod预制体加载完成: {successCount}/{totalCount} 成功");
        
        // 更新状态
        totalModsLoaded = successCount;
        isModsLoaded = successCount > 0;
        
        // 更新MODS列表
        UpdateMODSList();
    }
    
    /// <summary>
    /// 更新MODS列表（保持向后兼容）
    /// </summary>
    private void UpdateMODSList()
    {
        MODS.Clear();
        foreach (var mapping in modPrefabMappings)
        {
            if (mapping.modPrefab != null)
            {
                MODS.Add(mapping.modPrefab);
            }
        }
    }
    
    /// <summary>
    /// 创建默认mod配置
    /// </summary>
    private void CreateDefaultModConfig()
    {
        ModConfig defaultConfig = new ModConfig
        {
            mods = new List<ModBasicInfo>
            {
                new ModBasicInfo
                {
                    modId = "default_mod",
                    modName = "默认mod",
                    description = "这是一个默认的mod配置",
                    prefabPath = "Prefabs/Mods/DefaultMod",
                    category = "通用",
                    rarity = 1,
                    weight = 1f
                }
            }
        };
        
        string jsonContent = JsonUtility.ToJson(defaultConfig, true);
        string filePath = Path.Combine(Application.persistentDataPath, "mod_config.json");
        
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, jsonContent);
            Debug.Log($"[StoreManager] 创建默认mod配置文件: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[StoreManager] 创建默认mod配置文件失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 根据ID获取mod预制体
    /// </summary>
    public GameObject GetModPrefabById(string modId)
    {
        // 资产优先：直接从 ModCatalog 取预制体
        if (modCatalog != null && modCatalog.mods != null)
        {
            var assetEntry = modCatalog.mods.Find(m => m.modId == modId);
            if (assetEntry != null)
            {
                if (assetEntry.modPrefab != null)
                {
                    return assetEntry.modPrefab;
                }
                if (!string.IsNullOrEmpty(assetEntry.prefabPath))
                {
                    var prefab = Resources.Load<GameObject>(assetEntry.prefabPath);
                    if (prefab != null)
                    {
                        // 缓存到映射，减少后续查找
                        var existing = modPrefabMappings.Find(m => m.modId == modId);
                        if (existing != null) existing.modPrefab = prefab;
                        else modPrefabMappings.Add(new ModPrefabMapping{ modId = modId, modPrefab = prefab });
                        return prefab;
                    }
                }
            }
        }

        // 资产不可用或未命中时，回退到映射/路径加载
        var mapping = modPrefabMappings.Find(m => m.modId == modId);
        if (mapping != null && mapping.modPrefab != null)
        {
            return mapping.modPrefab;
        }
        LoadModPrefabById(modId);
        mapping = modPrefabMappings.Find(m => m.modId == modId);
        return mapping?.modPrefab;
    }
    
    /// <summary>
    /// 清空所有mod预制体映射
    /// </summary>
    public void ClearModPrefabMappings()
    {
        modPrefabMappings.Clear();
        MODS.Clear();
        Debug.Log("[StoreManager] 清空所有mod预制体映射");
    }
    
    /// <summary>
    /// 获取所有可用的mod ID
    /// </summary>
    public List<string> GetAllModIds()
    {
        List<string> ids = new List<string>();
        foreach (var mapping in modPrefabMappings)
        {
            if (mapping.modPrefab != null)
            {
                ids.Add(mapping.modId);
            }
        }
        return ids;
    }
    
    /// <summary>
    /// 检查mod配置是否完整
    /// </summary>
    public bool ValidateModConfig(string modId)
    {
        bool hasPrefab = GetModPrefabById(modId) != null;
        
        if (!hasPrefab)
        {
            Debug.LogWarning($"[StoreManager] mod {modId} 缺少预制体");
        }
        
        return hasPrefab;
    }
    
    /// <summary>
    /// 打印mod配置信息
    /// </summary>
    [ContextMenu("打印mod配置信息")]
    public void PrintModConfigs()
    {
        Debug.Log("=== mod配置信息 ===");
        foreach (var mapping in modPrefabMappings)
        {
            string prefabStatus = mapping.modPrefab != null ? "✓" : "✗";
            Debug.Log($"{prefabStatus} {mapping.modId} -> {(mapping.modPrefab != null ? mapping.modPrefab.name : "未设置")}");
        }
        Debug.Log("---");
    }
    
    /// <summary>
    /// 根据稀有度筛选mod
    /// </summary>
    public List<ModBasicInfo> GetModsByRarity(int rarity)
    {
        return modCatalog.mods.FindAll(m => m.rarity == rarity);
    }
    
    /// <summary>
    /// 根据分类筛选mod
    /// </summary>
    public List<ModBasicInfo> GetModsByCategory(string category)
    {
        return modCatalog.mods.FindAll(m => m.category == category);
    }
}