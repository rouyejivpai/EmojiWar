//------------------------------------------------------------
// EmojiWar GameMain - UI 列表 Cell 行源（"列表下放一个预制体"规范的统一实现）
//
// 规范：doc/UI列表与Cell规范.md §一.3
//   预制体里，**列表容器（正）下方必须放一个 Cell 预制体实例**——它是作者态占位，
//   同时就是运行时的"行源"（WYSIWYG：容器里看到多大，运行时就多大）。
//   运行时由本类取用：把模板移出容器（→ GfUiItemPool.GlobalHiddenRoot，容器 100% 干净），
//   之后行实例一律 Instantiate(行源) 生成；容器内永远只有"当前应显示的行"。
//   若容器下确实没放模板（旧数据/临时列表），才回落到 items/ 预制体路径（UiPrefab）加载。
//
// 用法（宿主窗体）：
//   m_CellSource = UiListCell.Resolve(m_Container, "GridContainer",
//                       Constant.UIItemAssetPath.InventorySlot, m_CellSource, BuildIfNeeded);
//   if (m_CellSource == null) { return; }                       // 等加载完成回调重试
//   ClearContainer();                                          // 规范：先清空
//   go = pool.Acquire(() => Instantiate(m_CellSource), m_Container);  // 再生成
//   关窗：UiListCell.Release(ref m_CellSource);
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>列表 Cell 行源解析：作者态模板优先，items 路径回落。</summary>
    public static class UiListCell
    {
        private static readonly HashSet<string> s_RequestedPaths = new HashSet<string>();
        private static readonly HashSet<string> s_FallbackLogged = new HashSet<string>();
        private static readonly HashSet<GameObject> s_TemplateSources = new HashSet<GameObject>();

        /// <summary>
        /// 取容器下预放的作者态 Cell 模板（没有则返回 null）。
        /// 取走后该模板不再属于容器（移到全局隐藏根并隐藏），容器内不会留下它；
        /// 容器里若放了多个（历史残留），只保留第一个，其余销毁。
        /// </summary>
        public static GameObject TakeContainerTemplate(RectTransform container, string label)
        {
            if (container == null || container.childCount == 0)
            {
                return null;
            }

            GameObject keep = null;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (child == null)
                {
                    continue;
                }
                // 嵌套子容器（例如"武器槽 → 法术槽子列表"）不是 Cell 模板/残留，必须保留
                if (child.GetComponent<UiSlotContainer>() != null)
                {
                    continue;
                }
                if (keep == null)
                {
                    keep = child.gameObject;
                    continue;
                }
                WriteProbe("[uilist] 容器 " + label + " 只认一个 Cell 模板，多余子物体销毁: " + child.name);
                DestroyObject(child.gameObject);
            }
            if (keep == null)
            {
                return null;
            }

            keep.transform.SetParent(GfUiItemPool.GlobalHiddenRoot, false);
            keep.SetActive(false);
            s_TemplateSources.Add(keep);
            WriteProbe("[uilist] 容器 " + label + " 取用作者态 Cell 模板作为行源: " + keep.name);
            return keep;
        }

        /// <summary>
        /// 解析行源：1) 调用方缓存；2) 容器下预放的 Cell 模板（规范首选）；3) items/ 路径预制体（回落）。
        /// 尚未就绪时返回 null，并在加载完成后回调 onReady 让宿主重试构建。
        /// </summary>
        public static GameObject Resolve(RectTransform container, string label, string fallbackItemPath,
            GameObject cachedSource, Action onReady)
        {
            if (cachedSource != null)
            {
                return cachedSource;
            }

            var template = TakeContainerTemplate(container, label);
            if (template != null)
            {
                return template;
            }

            var prefab = UiPrefab.GetCached(fallbackItemPath);
            if (prefab != null)
            {
                if (s_FallbackLogged.Add(label))
                {
                    WriteProbe("[uilist] 容器 " + label + " 未放 Cell 模板，回落 items 路径: " + fallbackItemPath);
                }
                return prefab;
            }

            if (!string.IsNullOrEmpty(fallbackItemPath) && s_RequestedPaths.Add(fallbackItemPath))
            {
                UiPrefab.Load(fallbackItemPath, p =>
                {
                    if (p == null)
                    {
                        WriteProbe("[uilist] items 预制体加载失败: " + fallbackItemPath);
                        return;
                    }
                    WriteProbe("[uilist] items 预制体已加载: " + fallbackItemPath);
                    if (onReady != null)
                    {
                        onReady();
                    }
                });
            }
            return null;
        }

        /// <summary>释放行源（窗体关闭时调用）：只有"从容器取来的模板实例"归本类销毁，items 预制体资产不动。</summary>
        public static void Release(ref GameObject source)
        {
            if (source != null && s_TemplateSources.Remove(source))
            {
                DestroyObject(source);
            }
            source = null;
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

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
    }
}
