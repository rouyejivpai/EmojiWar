using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 轻量场景调度工具：封装异步加载/卸载（Single/Additive）、设置激活场景、预加载与进度回调。
/// 用法示例：SceneLoader.Instance.LoadSingleAsync("Gameplay");
/// </summary>
public class SceneLoader : MonoBehaviour
{
    //单例设置
  /// <summary>
  /// 全局唯一实例（首次访问时自动创建并标记为 DontDestroyOnLoad）。
  /// </summary>
  private static SceneLoader instance;
  public static SceneLoader Instance
  {
    get
    {
      if (instance == null)
      {
        var go = new GameObject("SceneLoader");
        instance = go.AddComponent<SceneLoader>();
        DontDestroyOnLoad(go);
      }
      return instance;
    }
  }

  /// <summary>
  /// 以 Single 模式异步加载目标场景（会卸载除 DontDestroyOnLoad 外的其他场景）。
  /// </summary>
  /// <param name="sceneName">场景名称（需在 Build Settings 中）</param>
  /// <param name="onProgress">进度回调，范围 0~1（已做 0.9 归一化）</param>
  /// <param name="onCompleted">加载并激活完成后的回调</param>
  /// <param name="manualActivate">是否手动激活（true 则需外部设置 allowSceneActivation=true）</param>
  /// <returns>用于控制该加载流程的 Coroutine</returns>
  public Coroutine LoadSingleAsync(string sceneName, Action<float> onProgress = null, Action onCompleted = null, bool manualActivate = false)
  {
    return StartCoroutine(LoadSceneAsyncInternal(sceneName, LoadSceneMode.Single, onProgress, onCompleted, manualActivate));
  }

  /// <summary>
  /// 以 Additive 模式异步叠加加载目标场景（不卸载当前场景）。
  /// </summary>
  /// <param name="sceneName">场景名称（需在 Build Settings 中）</param>
  /// <param name="onProgress">进度回调，范围 0~1（已做 0.9 归一化）</param>
  /// <param name="onCompleted">加载并激活完成后的回调</param>
  /// <param name="manualActivate">是否手动激活（true 则需外部设置 allowSceneActivation=true）</param>
  /// <returns>用于控制该加载流程的 Coroutine</returns>
  public Coroutine LoadAdditiveAsync(string sceneName, Action<float> onProgress = null, Action onCompleted = null, bool manualActivate = false)
  {
    return StartCoroutine(LoadSceneAsyncInternal(sceneName, LoadSceneMode.Additive, onProgress, onCompleted, manualActivate));
  }

  /// <summary>
  /// 异步卸载已加载的场景。
  /// </summary>
  /// <param name="sceneName">场景名称</param>
  /// <param name="onCompleted">卸载完成回调</param>
  /// <returns>用于控制该卸载流程的 Coroutine</returns>
  public Coroutine UnloadAsync(string sceneName, Action onCompleted = null)
  {
    return StartCoroutine(UnloadSceneAsyncInternal(sceneName, onCompleted));
  }

  /// <summary>
  /// 判断场景是否处于“已加载”状态。
  /// </summary>
  /// <param name="sceneName">场景名称</param>
  /// <returns>true：已加载；false：未加载或无效</returns>
  public bool IsLoaded(string sceneName)
  {
    Scene s = SceneManager.GetSceneByName(sceneName);
    return s.IsValid() && s.isLoaded;
  }

  /// <summary>
  /// 尝试将指定场景设置为激活场景（影响新创建对象的默认归属以及部分全局设置）。
  /// </summary>
  /// <param name="sceneName">场景名称</param>
  /// <returns>true：设置成功；false：场景未加载或无效</returns>
  public bool TrySetActive(string sceneName)
  {
    Scene scene = SceneManager.GetSceneByName(sceneName);
    if (scene.IsValid() && scene.isLoaded)
    {
      return SceneManager.SetActiveScene(scene);
    }
    return false;
  }

  /// <summary>
  /// 预加载（Additive，不激活），可用于下个房间的提前加载；加载完成后保持 allowSceneActivation=false，由调用方决定何时激活。
  /// 返回 AsyncOperation 以便外部在合适时机设置 allowSceneActivation=true。
  /// </summary>
  /// <param name="sceneName">要预加载的场景名称</param>
  /// <param name="onProgress">进度回调，范围 0~1（已做 0.9 归一化）</param>
  /// <returns>AsyncOperation（默认 allowSceneActivation=false）</returns>
  public AsyncOperation PreloadAdditive(string sceneName, Action<float> onProgress = null)
  {
    var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    if (op == null) return null;
    op.allowSceneActivation = false;
    StartCoroutine(TrackProgress(op, onProgress));
    return op;
  }

  /// <summary>
  /// 内部通用的异步加载实现（支持 Single/Additive 与可选手动激活）。
  /// </summary>
  /// <param name="sceneName">场景名称</param>
  /// <param name="mode">加载模式（Single 或 Additive）</param>
  /// <param name="onProgress">进度回调，范围 0~1</param>
  /// <param name="onCompleted">激活完成回调</param>
  /// <param name="manualActivate">是否手动激活</param>
  private IEnumerator LoadSceneAsyncInternal(string sceneName, LoadSceneMode mode, Action<float> onProgress, Action onCompleted, bool manualActivate)
  {
    AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, mode);
    if (op == null)
    {
      Debug.LogError($"[SceneLoader] 无法开始加载场景: {sceneName}");
      yield break;
    }

    op.allowSceneActivation = !manualActivate;

    // 进度：0~0.9 为加载阶段；0.9~1 为等待激活阶段
    while (op.progress < 0.9f)
    {
      onProgress?.Invoke(op.progress / 0.9f);
      yield return null;
    }
    onProgress?.Invoke(1f);

    if (manualActivate)
    {
      // 由外部决定激活时机：例如过场动画结束后再激活
      yield return new WaitUntil(() => op.allowSceneActivation);
    }

    // 等待激活完成
    while (!op.isDone)
    {
      yield return null;
    }

    onCompleted?.Invoke();
  }

  /// <summary>
  /// 内部通用的异步卸载实现。
  /// </summary>
  /// <param name="sceneName">场景名称</param>
  /// <param name="onCompleted">卸载完成回调</param>
  private IEnumerator UnloadSceneAsyncInternal(string sceneName, Action onCompleted)
  {
    if (!IsLoaded(sceneName))
    {
      onCompleted?.Invoke();
      yield break;
    }

    AsyncOperation op = SceneManager.UnloadSceneAsync(sceneName);
    if (op == null)
    {
      Debug.LogError($"[SceneLoader] 无法开始卸载场景: {sceneName}");
      yield break;
    }

    while (!op.isDone)
    {
      yield return null;
    }
    onCompleted?.Invoke();
  }

  /// <summary>
  /// 跟踪一个 AsyncOperation 的加载阶段并规范化进度（0~0.9 → 0~1）。
  /// </summary>
  /// <param name="op">要跟踪的异步操作</param>
  /// <param name="onProgress">进度回调（0~1）</param>
  private IEnumerator TrackProgress(AsyncOperation op, Action<float> onProgress)
  {
    while (op != null && op.progress < 0.9f)
    {
      onProgress?.Invoke(op.progress / 0.9f);
      yield return null;
    }
    onProgress?.Invoke(1f);
  }
}


