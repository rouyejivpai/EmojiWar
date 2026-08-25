//------------------------------------------------------------
// EmojiWar GameMain - 玩家实体
// 读取输入：WASD 移动 + 鼠标瞄准/射击。
// 注意：为后续网络化，输入逻辑与数据分离（Move 调用由数据驱动）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Entity
{
    /// <summary>
    /// 玩家实体：本地输入驱动。
    /// </summary>
    public class PlayerEntity : EntityBase
    {
        [Header("武器槽位")]
        [SerializeField]
        private Weapon.WeaponBase m_PrimaryWeapon = null;

        [SerializeField]
        private Weapon.WeaponBase m_SecondaryWeapon = null;

        /// <summary>当前武器（主武器）。</summary>
        public Weapon.WeaponBase PrimaryWeapon
        {
            get { return m_PrimaryWeapon; }
            set { m_PrimaryWeapon = value; }
        }

        /// <summary>副武器。</summary>
        public Weapon.WeaponBase SecondaryWeapon
        {
            get { return m_SecondaryWeapon; }
            set { m_SecondaryWeapon = value; }
        }

        /// <summary>本机玩家在网络中的实体 ID（用于与网络实体颜色统一）。</summary>
        public int NetworkEntityId = -1;

        protected override void Start()
        {
            base.Start();

            // 绑定武器持有者
            if (m_PrimaryWeapon != null)
            {
                m_PrimaryWeapon.SetOwner(this);
            }
            if (m_SecondaryWeapon != null)
            {
                m_SecondaryWeapon.SetOwner(this);
            }
        }

        protected override void ApplyArtSprite()
        {
            SetSprite(Art.ArtManager.GetPlayerSprite());

            // 颜色与网络实体统一：按 EntityId 着色（两窗口看到同一玩家同色）
            if (NetworkEntityId >= 0 && m_SpriteRenderer != null)
            {
                float hue = (NetworkEntityId * 0.61803398875f) % 1f;
                m_SpriteRenderer.color = Color.HSVToRGB(hue, 0.55f, 1f);
            }
        }

        private void Update()
        {
            if (!IsAlive)
            {
                return;
            }

            HandleMoveInput();
            HandleShootInput();
        }

        private void HandleMoveInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector2 inputDirection = new Vector2(horizontal, vertical);

            if (inputDirection.sqrMagnitude > 1f)
            {
                inputDirection = inputDirection.normalized;
            }

            Move(inputDirection);
            FaceDirection(inputDirection);
        }

        private void HandleShootInput()
        {
            if (m_PrimaryWeapon == null)
            {
                return;
            }

            // 鼠标左键：主武器
            if (Input.GetMouseButton(0))
            {
                Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                m_PrimaryWeapon.TryFire(new Vector2(mouseWorldPos.x, mouseWorldPos.y));
            }

            // 鼠标右键：副武器
            if (Input.GetMouseButton(1) && m_SecondaryWeapon != null)
            {
                Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                m_SecondaryWeapon.TryFire(new Vector2(mouseWorldPos.x, mouseWorldPos.y));
            }

            // R 键装弹
            if (Input.GetKeyDown(KeyCode.R))
            {
                m_PrimaryWeapon.Reload();
            }
        }

        /// <summary>玩家死亡事件（供流程切换）。</summary>
        public static event System.Action OnPlayerDied;

        protected override void OnTakeDamage()
        {
            base.OnTakeDamage();
        }

        protected override void Die()
        {
            base.Die();
            Debug.Log("[PlayerEntity] 玩家死亡");
            OnPlayerDied?.Invoke();
        }
    }
}
