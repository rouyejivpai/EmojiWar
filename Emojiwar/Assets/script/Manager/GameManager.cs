using System;
using UnityEngine;
using Game.Data;

namespace Game
{
    
}
public enum GameState{
    Menu,
    ChooseCharacter,
    Fighting,
    Store,
    GameOver
}
public class GameManager : MonoBehaviour
{
    [Header("单例设置")]
    private static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<GameManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("EnemyManager");
                    _instance = go.AddComponent<GameManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
        set => _instance = value;
    }

    public GameObject player;
    public GameState gameState;//当前游戏阶段
    public Action<GameState> onGameStateChanged;//游戏阶段切换事件
    public bool CoinMagnet;
    public GameObject LabPanel;
    public GameObject PACK;
    public GameObject Pack
    {
        get => PACK;
        set => PACK = value;
    }

    public CharacterCatalog characterCatalog;
    public EnemyCatalog enemyCatalog;
    public WeaponCatalog weaponCatalog;
    public BuffCatalog buffCatalog;
    public ModCatalog modCatalog;
    public BattleAsset battleAsset;

    public event Action<GameObject> OnPlayerReady;

    private int _coin;
    public int Coin
    {
        get => _coin;
        set
        {
            var delta = value - _coin;
            _coin = Mathf.Max(0, value);
            if (delta != 0)
            {
                RunData.Instance.AddCoin(delta);
            }
            if (LabPanel != null)
            {
                var panel = LabPanel.GetComponent<labPanel>();
                if (panel != null)
                {
                    panel.UpdateCoin(_coin);
                }
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ChangeGameState(GameState gameState)
    {
        this.gameState = gameState;
        onGameStateChanged?.Invoke(gameState);
        
    }
    //无参具体方法
    public void GameState2chooseCharacter()
    {
        ChangeGameState(GameState.ChooseCharacter);
    }
    public void StartGame(CharacterEntry entry)
    {
        if (entry == null) return;

        RunData.Instance.ResetRun();
        RunData.Instance.SetCharacter(entry.characterId);
        Coin = entry.coin;

        GameObject prefab = entry.prefab;
        if (prefab == null && !string.IsNullOrEmpty(entry.prefabPath))
        {
            prefab = Resources.Load<GameObject>(entry.prefabPath);
        }
        if (prefab == null) return;

        if (player != null)
        {
            Destroy(player);
            player = null;
        }

        player = Instantiate(prefab);

        OnPlayerReady?.Invoke(player);
        ChangeGameState(GameState.Fighting);
    }

    public void RegisterLabPanel(GameObject panel)
    {
        LabPanel = panel;
    }

    public void levelComplete()
    {
    }

    public void Addpack(GameObject obj)
    {
        if (obj == null) return;
        var snap = obj.GetComponent<SnapDrag>();
        if (snap != null)
        {
            var grids = GameObject.FindObjectsOfType<packGrid>();
            foreach (var g in grids)
            {
                if (g != null && g.empty && g.gameObject.activeInHierarchy)
                {
                    var target = g.GetComponent<DragTarget>();
                    if (target != null)
                    {
                        snap.SnapToTarget(target);
                        return;
                    }
                }
            }
        }
        if (Pack != null)
        {
            obj.transform.SetParent(Pack.transform, false);
        }
    }
}
