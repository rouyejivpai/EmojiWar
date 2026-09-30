//------------------------------------------------------------
// EmojiWar GameMain - 设置界面（窗口化设置）
// 打开方式：主菜单点"设置"按钮 → 本窗体（覆盖在 Menu 上，返回关闭回 Menu）。
// 窗口化勾选用 Button（非 Toggle）显示勾选态：点击切换 Screen.fullScreen。
//------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>设置页事件（打开/关闭由 MenuForm 流程处理）。</summary>
    public static class SettingsEvents
    {
        /// <summary>请求关闭设置页（返回主菜单）。</summary>
        public static event Action OnBackRequested;

        public static void RequestBack() { OnBackRequested?.Invoke(); }
    }

    /// <summary>
    /// 设置界面。
    /// </summary>
    public class SettingsForm : UGuiForm
    {
        [SerializeField]
        private Button m_WindowedButton = null;   // 窗口化勾选（btn 显示 ✓）

        [SerializeField]
        private Text m_WindowedLabel = null;       // 按钮文本（"✓ 窗口化" / "窗口化"）

        [SerializeField]
        private Button m_BackButton = null;        // 返回主菜单

        /// <summary>当前是否窗口化（运行时切换 Screen.fullScreen）。</summary>
        public bool IsWindowed { get; private set; }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 初始状态：读取当前全屏状态
            IsWindowed = !Screen.fullScreen;

            if (m_WindowedButton != null)
            {
                m_WindowedButton.onClick.RemoveAllListeners();
                m_WindowedButton.onClick.AddListener(OnWindowedClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveAllListeners();
                m_BackButton.onClick.AddListener(OnBackClick);
            }

            RefreshLabels();
            WriteProbe("[settings] OnOpen fullScreen=" + Screen.fullScreen +
                " btn=" + (m_WindowedButton != null ? "OK" : "NULL") +
                " label=" + (m_WindowedLabel != null ? "OK" : "NULL") +
                " back=" + (m_BackButton != null ? "OK" : "NULL"));
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_WindowedButton != null)
            {
                m_WindowedButton.onClick.RemoveListener(OnWindowedClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveListener(OnBackClick);
            }
            WriteProbe("[settings] OnClose isShutdown=" + isShutdown);
            base.OnClose(isShutdown, userData);
        }

        /// <summary>窗口化勾选：切换窗口/全屏（运行时立即生效，构建版可用）。</summary>
        private void OnWindowedClick()
        {
            IsWindowed = !IsWindowed;
            ApplyWindowed();
            RefreshLabels();
            WriteProbe("[settings] OnWindowedClick windowed=" + IsWindowed +
                " fullScreen=" + Screen.fullScreen);
        }

        private void ApplyWindowed()
        {
            if (IsWindowed)
            {
                // 窗口化：关闭全屏（保持当前分辨率，窗口可拖拽）
                Screen.fullScreen = false;
            }
            else
            {
                // 全屏（边界全屏，Alt+Enter 也可切）
                Screen.fullScreen = true;
            }
            Debug.Log("[Settings] 窗口化=" + IsWindowed + " fullScreen=" + Screen.fullScreen);
        }

        private void OnBackClick()
        {
            SettingsEvents.RequestBack();
        }

        private void RefreshLabels()
        {
            if (m_WindowedLabel != null)
            {
                m_WindowedLabel.text = IsWindowed ? "✓ 窗口化" : "窗口化";
            }
        }

        /// <summary>供外部同步状态（如 Menu 关闭设置时）。</summary>
        public bool GetWindowedState() { return IsWindowed; }

        /// <summary>自动化验证：模拟点击窗口化按钮。</summary>
        public void SimulateWindowedToggle()
        {
            if (m_WindowedButton != null)
            {
                m_WindowedButton.onClick.Invoke();
            }
            else
            {
                OnWindowedClick();
            }
        }

        /// <summary>自动化验证：模拟点击返回按钮。</summary>
        public void SimulateBack()
        {
            if (m_BackButton != null)
            {
                m_BackButton.onClick.Invoke();
            }
            else
            {
                OnBackClick();
            }
        }

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

// touch settings
