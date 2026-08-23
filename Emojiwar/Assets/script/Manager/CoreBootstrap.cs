using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 挂在 Core 场景中的引导脚本：进入播放后自动加载并显示战斗场景。
/// </summary>
public class CoreBootstrap : MonoBehaviour
{
  [Header("启动目标场景名（Single 或 Additive）")]
  public string gameplayShellScene = "Gameplay"; // 可留空表示不加载外壳
  public string combatSceneName = "fightScenes"; // 你的战斗场景名
  public bool useAdditive = true;                 // 推荐：Core 常驻 + Additive 加载战斗
  
  [Header("背包页面设置")]
  public bool autoLoadInventory = true;           // 是否自动加载背包页面
  public string inventorySceneName = "Inventory"; // 背包场景名称

  private IEnumerator Start()
  {
    // 若需要外壳场景
    if (!string.IsNullOrEmpty(gameplayShellScene))
    {
      if (useAdditive)
      {
        yield return SceneLoader.Instance.LoadAdditiveAsync(gameplayShellScene);
        SceneLoader.Instance.TrySetActive(gameplayShellScene);
      }
      else
      {
        yield return SceneLoader.Instance.LoadSingleAsync(gameplayShellScene);
      }
    }

    // 加载战斗场景（Additive）
    if (useAdditive)
    {
      yield return SceneLoader.Instance.LoadAdditiveAsync(combatSceneName);
      // 确保激活场景为外壳或 Core 以外的主要承载
      if (!string.IsNullOrEmpty(gameplayShellScene))
      {
        SceneLoader.Instance.TrySetActive(gameplayShellScene);
      }
      else
      {
        // 若无外壳，则将战斗设为激活场景
        SceneLoader.Instance.TrySetActive(combatSceneName);
      }
    }
    else
    {
      // 若不使用 Additive，直接单场景切换到战斗（Core 将被卸载，通常不推荐）
      yield return SceneLoader.Instance.LoadSingleAsync(combatSceneName);
    }
    // 加载背包场景（Additive，常驻）
    if (autoLoadInventory && !string.IsNullOrEmpty(inventorySceneName))
    {
      yield return SceneLoader.Instance.LoadAdditiveAsync(inventorySceneName);
    }
  }

}




