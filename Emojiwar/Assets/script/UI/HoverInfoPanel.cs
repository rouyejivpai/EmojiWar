using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

/// <summary>
/// 悬停信息面板，用于显示物品的详细信息
/// </summary>
public  class HoverInfoPanel : MonoBehaviour
{
    //单例模式
    private static HoverInfoPanel _ins;

     public static HoverInfoPanel Ins
     {
        set
        {
            _ins = value;
        }
        get
        {
            return _ins;
        }
    }
    [Header("UI组件")]
    [SerializeField] private GameObject panel;//
    [SerializeField] private TextMeshProUGUI titleText;//标题
    [SerializeField] private TextMeshProUGUI descriptionText;//描述
     [SerializeField] private TextMeshProUGUI typeText;//统计信息
    [SerializeField] private Image iconImage;//图标
    [SerializeField] private Image backgroundImage;//背景
    
    [Header("显示设置")]
    [SerializeField] private float showDelay = 0.5f;  // 悬停多久后显示
    [SerializeField] private Vector2 offset = new Vector2(20f, -20f);  // 相对于鼠标的偏移
    [SerializeField] private float fadeInDuration = 0.2f;  // 淡入时间
    [SerializeField] private float fadeOutDuration = 0.1f;  // 淡出时间
    
    [Header("样式设置")]
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);//背景颜色
    [SerializeField] private Color titleColor = Color.white;//标题颜色                                              
    [SerializeField] private Color descriptionColor = new Color(0.8f, 0.8f, 0.8f, 1f);//描述颜色
    [SerializeField] private Color statsColor = new Color(0.7f, 0.9f, 1f, 1f);//统计信息颜色
    
    private bool isVisible = false;//是否可见
    public float hoverTimer = 0f;//悬停计时
    private Camera uiCamera;//UI相机
    private Canvas canvas;
    private CanvasGroup canvasGroup;//CanvasGroup

    public Vector2 screenSize;
    public RectTransform uiRect;

    public Vector2 uiSize;
    // 动画相关
    private Coroutine fadeCoroutine;//淡入淡出动画
    private Vector3 targetPosition;//目标位置
    private bool isAnimating = false;
    
    private void Awake()
    {
        _ins = this;
        
        // 初始隐藏面板
        // if (panel != null)
        // {
        //     panel.SetActive(false);
        //     canvasGroup.alpha = 0f;
        // }
    }
    
    private void Start()
    {
        // 获取屏幕尺寸
        screenSize = new Vector2(Screen.width, Screen.height);

// 获取UI组件的RectTransform
        uiRect = panel.GetComponent<RectTransform>();

// 获取UI组件的尺寸
         uiSize = uiRect.sizeDelta;
        
        // 确保初始状态是隐藏的
        HideInfo();
    }
    
    private void Update()
    {
        // if (isVisible && !isAnimating)
        // {
        //     // 更新面板位置跟随鼠标
        //     UpdatePosition();
        // }
    }
    
    /// <summary>
    /// 初始化样式
    /// </summary>
    private void InitializeStyles()
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = backgroundColor;
        }
        
        if (titleText != null)
        {
            titleText.color = titleColor;
        }
        
        if (descriptionText != null)
        {
            descriptionText.color = descriptionColor;
        }
        
        if (typeText != null)
        {
            typeText.color = statsColor;
        }
    }
    
    /// <summary>
    /// 显示信息面板
    /// </summary>
    public void ShowInfo(Obj obj)
    {
        if (panel == null) return;
        Debug.Log("显示信息");
        // 设置信息内容
        //SetContent(title, description, stats, icon);
        
         // 获取UI组件在屏幕上的实际位置
        RectTransform objRect = obj.gameObject.GetComponent<RectTransform>();
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(Camera.main, objRect.position);
        // 更新位置
                UpdatePosition(screenPos);
        //更新内容
            SetContent(obj);
        // 显示面板
        panel.SetActive(true);
        isVisible = true;
        
        
        
        
    }
    
    /// <summary>
    /// 隐藏信息面板
    /// </summary>
    public void HideInfo()
    {
        if (panel == null) return;
        
        isVisible = false;
        panel.SetActive(false);
      
    }
    
    /// <summary>
    /// 设置面板内容
    /// </summary>
    private void SetContent(Obj obj)
    {
        if (titleText != null)
        {
            titleText.text = obj.info.name;
        }
        
        if (descriptionText != null)
        {
            descriptionText.text =obj.info.description;
        }
        
        if (typeText != null)
        {
            typeText.text = obj.info.type;
            
        }
        
        if (iconImage != null)
        {
            if (obj.info.icon != null)
            {
                iconImage.sprite = obj.info.icon;
                iconImage.gameObject.SetActive(true);
            }
            else
            {
                iconImage.gameObject.SetActive(false);
            }
        }
    }
    
    /// <summary>
    /// 更新面板位置
    /// </summary>
    private void UpdatePosition(Vector3 screenPos)
    {
        if (panel == null) return;
        
        // 计算面板位置（考虑偏移）
        Vector3 panelPos = screenPos + new Vector3(offset.x, offset.y, 0);
        
        // 获取面板的RectTransform
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        
        // 检查右边界
        if (panelPos.x + uiSize.x > screenSize.x)
        {
            panelPos.x = screenPos.x - uiSize.x - offset.x;
        }
        
        // 检查上边界
        if (panelPos.y + uiSize.y > screenSize.y)
        {
            panelPos.y = screenPos.y - uiSize.y - offset.y;
        }
        
        // 检查左边界
        if (panelPos.x < 0)
        {
            panelPos.x = 0;
        }
        
        // 检查下边界
        if (panelPos.y < 0)
        {
            panelPos.y = 0;
        }
        
        // 将屏幕坐标转换为UI坐标
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            panelRect.parent as RectTransform,
            panelPos,
            Camera.main,
            out localPoint
        );
        
        // 设置面板位置
        panelRect.anchoredPosition = localPoint+screenSize/2;
        targetPosition = panelPos;
    }
    /// <summary>
    /// 开始悬停计时
    /// </summary>
    public void StartHoverTimer()
    {
        hoverTimer = 0f;
    }
    
    /// <summary>
    /// 更新悬停计时
    /// </summary>
    public void UpdateHoverTimer()
    {
        if (!isVisible)
        {
            hoverTimer += Time.deltaTime;
            if (hoverTimer >= showDelay)
            {
                // 延迟显示，这里可以触发显示逻辑
                // 通常由Obj的OnPointerEnter调用ShowInfo
            }
        }
    }
    
    /// <summary>
    /// 重置悬停计时
    /// </summary>
    public void ResetHoverTimer()
    {
        hoverTimer = 0f;
    }
    
    /// <summary>
    /// 淡入动画
    /// </summary>
    private System.Collections.IEnumerator FadeIn()
    {
        isAnimating = true;
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsedTime < fadeInDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeInDuration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, progress);
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
        isAnimating = false;
    }
    
    /// <summary>
    /// 淡出动画
    /// </summary>
    private System.Collections.IEnumerator FadeOut()
    {
        isAnimating = true;
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeOutDuration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        panel.SetActive(false);
        isAnimating = false;
    }
    
    /// <summary>
    /// 设置显示延迟
    /// </summary>
    public void SetShowDelay(float delay)
    {
        showDelay = Mathf.Max(0f, delay);
    }
    
    /// <summary>
    /// 设置偏移量
    /// </summary>
    public void SetOffset(Vector2 newOffset)
    {
        offset = newOffset;
    }
    
    /// <summary>
    /// 设置背景颜色
    /// </summary>
    public void SetBackgroundColor(Color color)
    {
        backgroundColor = color;
        if (backgroundImage != null)
        {
            backgroundImage.color = backgroundColor;
        }
    }
    
    /// <summary>
    /// 设置标题颜色
    /// </summary>
    public void SetTitleColor(Color color)
    {
        titleColor = color;
        if (titleText != null)
        {
            titleText.color = titleColor;
        }
    }
    
    /// <summary>
    /// 设置描述颜色
    /// </summary>
    public void SetDescriptionColor(Color color)
    {
        descriptionColor = color;
        if (descriptionText != null)
        {
            descriptionText.color = descriptionColor;
        }
    }
    
    /// <summary>
    /// 设置统计信息颜色
    /// </summary>
    public void SetStatsColor(Color color)
    {
        statsColor = color;
        if (typeText != null)
        {
            typeText.color = statsColor;
        }
    }
    
    /// <summary>
    /// 强制刷新位置
    /// </summary>
    public void ForceUpdatePosition()
    {
       
    }
    
    /// <summary>
    /// 检查面板是否可见
    /// </summary>
    public bool IsVisible()
    {
        return isVisible && panel.activeInHierarchy;
    }
    
    /// <summary>
    /// 获取当前悬停计时器值
    /// </summary>
    public float GetHoverTimer()
    {
        return hoverTimer;
    }
    
    /// <summary>
    /// 调试信息
    /// </summary>
    [ContextMenu("打印调试信息")]
    public void PrintDebugInfo()
    {
        Debug.Log($"[HoverInfoPanel] 面板状态: {(isVisible ? "可见" : "隐藏")}");
        Debug.Log($"[HoverInfoPanel] 动画状态: {(isAnimating ? "动画中" : "静止")}");
        Debug.Log($"[HoverInfoPanel] 悬停计时: {hoverTimer:F2}s");
        Debug.Log($"[HoverInfoPanel] 面板位置: {transform.position}");
        Debug.Log($"[HoverInfoPanel] 目标位置: {targetPosition}");
    }
}
