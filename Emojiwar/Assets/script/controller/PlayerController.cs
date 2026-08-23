using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 玩家控制器（仅移动）
public class PlayerController : MonoBehaviour
{
    public Entity entity;//玩家实体
    public bool isActive=true;//
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;       // 移动速度（单位/秒）
    [SerializeField] private bool usePhysics = true;      // 使用Rigidbody2D物理移动
    
    //装备事件
    public Action<GameObject> Weapon1update = delegate { };
    public Action<GameObject> Weapon2update = delegate { };
    
    public GameObject Weapon1Panel;
    public GameObject Weapon2Panel;
    public GameObject SWeapon1Panel;
    public GameObject SWeapon2Panel;
    public WeaponBase Weapon1
    {
        get
        {
            return entity.GetCurrentWeapon();
        }
        set
        {

            if (value == null)
            {
                //让实体装备该武器
                entity.EquipWeapon(null);
                //更新武器页面ui
                Weapon1update.Invoke(null);
            }
            else
            {
                //让实体装备该武器
                entity.EquipWeapon(value.gameObject);
                //更新武器页面ui
                Weapon1update.Invoke(value.gameObject);
                //Debug.Log("武器1更新事件被触发");
            }
        }
    }
    public WeaponBase Weapon2
    {
        get
        {
            return entity.secondWeapon;
        }
        set
        {
            if (value == null)
            {
                //让实体装备该武器
                entity.EquipWeapon2(null);
                //更新武器页面ui
                Weapon2update.Invoke(null);
            }
            else
            {
                //让实体装备该武器
                            entity.EquipWeapon2(value.gameObject);
                           //更新武器页面ui
                          Weapon2update.Invoke(value.gameObject); 
            }
           
        }
    }
    
    
    private Rigidbody2D rb;
    private Vector2 inputDirection;

    private void Awake()
    {
        GameManager.Instance.OnPlayerReady += init;
        init(this.gameObject);
    }

    private void init(GameObject Player)
    {
        GameManager.Instance.player = this.gameObject;
                rb = GetComponent<Rigidbody2D>();
                if (usePhysics && rb == null)
                {
                    // 若未挂载Rigidbody2D则自动回退为Transform位移
                    usePhysics = false;
                }

                if (entity.currentWeapon != null)
                {
                    entity.GetCurrentWeapon().owner=entity;
                }

                if (entity.secondWeapon!= null)
                {
                    entity.secondWeapon.owner=entity;
                }
                
                
                Debug.Log("完成武器设置");
    }
    private void Start()
    {
        Weapon1= GameManager.Instance.player.GetComponent<Entity>().currentWeapon;
                Weapon2 = GameManager.Instance.player.GetComponent<Entity>().secondWeapon;
    }

    private void Update()
    {
        if (isActive==false)return;
        // 读取输入（WASD/方向键）
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(horizontal, vertical);

        // 斜向移动归一化，保持等速
        if (inputDirection.sqrMagnitude > 1f)
        {
            inputDirection = inputDirection.normalized;
        }
        
        // 应用移动速度
        if (entity != null)
        {
            
            // 使用Entity的移动系统
            entity.Move(inputDirection);
            //Debug.Log("触发移动"+inputDirection);
        }
        else
        {
            // 如果没有Entity组件，使用简单的Transform移动
            Vector2 movement = inputDirection * moveSpeed * Time.deltaTime;
            transform.position += (Vector3)movement;
        }

        // 处理射击输入
        HandleShootInput();
    }

    //取出武器方法
    public GameObject RemoveWeapon1()
    {
        GameObject Weapon = entity.currentWeapon.gameObject;
       
        Weapon1 = null;
        return Weapon;
    }

    public GameObject RemoveWeapon2()
    {
        GameObject Weapon = entity.secondWeapon.gameObject;
        
        Weapon2 = null;
        return Weapon;
    }
    
    // 动态调整移动速度（可供外部调用）
    public void SetMoveSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0f, speed);
    }

    // 处理射击输入
    private void HandleShootInput()
    {
        // 鼠标左键射击
        if (Input.GetMouseButton(0))
        {
            // 获取鼠标世界坐标
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 targetPosition = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
            
            // 使用Entity的武器系统，传递目标位置
            if (entity != null)
            {
                entity.FireWeapon(targetPosition);
            }
        }
        
        // R键装弹
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (entity != null)
            {
                entity.ReloadWeapon();
            }
        }
        
        // 鼠标左键射击
        if (Input.GetMouseButton(1))
        {
            // 获取鼠标世界坐标
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 targetPosition = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
            
            // 使用Entity的武器系统，传递目标位置
            if (entity != null)
            {
                entity.FireWeapon2(targetPosition);
            }
        }
    }
}
