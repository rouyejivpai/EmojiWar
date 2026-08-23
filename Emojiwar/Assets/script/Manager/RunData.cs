using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本局运行期数据（非永久存档）。
/// 常驻于 Core 场景并 DontDestroyOnLoad，供 HUD/玩法读取与订阅。
/// </summary>
public class RunData : MonoBehaviour
{
  private static RunData instance;
  public static RunData Instance
  {
    get
    {
      if (instance == null)
      {
        var go = new GameObject("RunData");
        instance = go.AddComponent<RunData>();
        DontDestroyOnLoad(go);
      }
      return instance;
    }
  }

  [Header("基础状态")]
  public string selectedCharacterId = string.Empty;
  public int stageIndex = 0;            // 当前关卡/房间序号
 
 private int _coin = 0;
  public int coin 
  {
    get => _coin;
    set
    {
      _coin = value;
      OnCoinChanged?.Invoke(_coin);
    }
  } 
  public int relicCapacity = 32;        // 遗物容量上限（示例）

  [Header("集合数据")]
  public List<string> relicIds = new List<string>();

  [Header("随机性")]
  public int seed = 0;
  public System.Random rng;

  // 事件供 HUD 订阅
  public event Action<int> OnCoinChanged;
  public event Action<string> OnCharacterChanged;
  public event Action<int> OnStageChanged;
  public event Action<string> OnRelicAdded;

  private void Awake()
  {
    if (instance != null && instance != this)
    {
      Destroy(gameObject);
      return;
    }
    instance = this;
    DontDestroyOnLoad(gameObject);
    if (seed == 0) seed = Environment.TickCount;
    rng = new System.Random(seed);
  }

  public void ResetRun()
  {
    stageIndex = 0;
    coin = 0;
    relicIds.Clear();
    seed = Environment.TickCount;
    rng = new System.Random(seed);
    selectedCharacterId = string.Empty;
    OnCoinChanged?.Invoke(coin);
    OnStageChanged?.Invoke(stageIndex);
    OnCharacterChanged?.Invoke(selectedCharacterId);
  }

  public void SetCharacter(string characterId)
  {
    selectedCharacterId = characterId;
    OnCharacterChanged?.Invoke(selectedCharacterId);
  }

  public void AddCoin(int amount)
  {
    coin = Mathf.Max(0, coin + amount);
    OnCoinChanged?.Invoke(coin);
  }

  public bool TrySpendCoin(int amount)
  {
    if (amount <= 0) return true;
    if (coin < amount) return false;
    coin -= amount;
    OnCoinChanged?.Invoke(coin);
    return true;
  }

  public void NextStage()
  {
    stageIndex++;
    OnStageChanged?.Invoke(stageIndex);
  }

  public bool AddRelic(string relicId)
  {
    if (string.IsNullOrEmpty(relicId)) return false;
    if (relicIds.Count >= relicCapacity) return false;
    relicIds.Add(relicId);
    OnRelicAdded?.Invoke(relicId);
    return true;
  }
}



