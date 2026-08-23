using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance;
    
    [Header("UI References")]
    public GameObject settingsPanel;
    public Button openSettingsButton;
    public Button closeSettingsButton;
    
    [Header("Settings")]
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public Toggle fullscreenToggle;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SetupUI();
            LoadSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void SetupUI()
    {
        // 设置按钮事件
        if (openSettingsButton) openSettingsButton.onClick.AddListener(OpenSettings);
        if (closeSettingsButton) closeSettingsButton.onClick.AddListener(CloseSettings);
        
        // 设置滑块事件
        if (musicVolumeSlider) musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider) sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        if (fullscreenToggle) fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
    }
    
    public void ToggleSettings()
    {
        settingsPanel.SetActive(!settingsPanel.activeSelf);
    }
    
    public void OpenSettings()
    {
        settingsPanel.SetActive(true);
        Time.timeScale = 0; // 暂停游戏
    }
    
    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        Time.timeScale = 1; // 恢复游戏
        SaveSettings();
    }
    
    void LoadSettings()
    {
        // 从PlayerPrefs加载设置
        musicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume", 1f);
        sfxVolumeSlider.value = PlayerPrefs.GetFloat("SFXVolume", 1f);
        fullscreenToggle.isOn = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
    }
    
    void SaveSettings()
    {
        // 保存设置到PlayerPrefs
        PlayerPrefs.SetFloat("MusicVolume", musicVolumeSlider.value);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolumeSlider.value);
        PlayerPrefs.SetInt("Fullscreen", fullscreenToggle.isOn ? 1 : 0);
        PlayerPrefs.Save();
    }
    
    void OnMusicVolumeChanged(float value)
    {
        // 应用音乐音量设置
        //AudioManager.Instance?.SetMusicVolume(value);
    }
    
    void OnSFXVolumeChanged(float value)
    {
        // 应用音效音量设置
       // AudioManager.Instance?.SetSFXVolume(value);
    }
    
    void OnFullscreenChanged(bool isFullscreen)
    {
        // 应用全屏设置
        Screen.fullScreen = isFullscreen;
    }
}