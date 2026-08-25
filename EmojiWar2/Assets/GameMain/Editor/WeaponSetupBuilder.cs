//------------------------------------------------------------
// EmojiWar GameMain - 武器搭建工具（Editor）
// 菜单：EmojiWar/Setup/03 - Build Weapon Setup
// 功能：
//   1. 创建 Projectile.prefab（子弹：SpriteRenderer + Rigidbody2D + 触发器碰撞 + Projectile）
//   2. 修复 Player.prefab 武器子对象绑定（m_ProjectilePrefab / m_FirePoint）
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 武器搭建工具：子弹预制体 + 玩家武器绑定。
    /// </summary>
    public static class WeaponSetupBuilder
    {
        private const string ProjectilePrefabPath = "Assets/GameMain/Resources/Entities/Projectile.prefab";
        private const string PlayerPrefabPath = "Assets/GameMain/Resources/Entities/Player.prefab";

        [MenuItem("EmojiWar/Setup/03 - Build Weapon Setup")]
        public static void BuildWeaponSetup()
        {
            CreateProjectilePrefab();
            FixPlayerWeapon();
            AssetDatabase.SaveAssets();
            Debug.Log("[WeaponSetup] 完成：子弹预制体已创建 + 玩家武器已绑定");
        }

        private static void CreateProjectilePrefab()
        {
            var go = new GameObject("Projectile");
            go.layer = LayerMask.NameToLayer("Default");

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 20;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.15f;

            go.AddComponent<Weapon.Projectile>();

            PrefabUtility.SaveAsPrefabAsset(go, ProjectilePrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[WeaponSetup] 子弹预制体: " + ProjectilePrefabPath);
        }

        private static void FixPlayerWeapon()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Debug.LogError("[WeaponSetup] Player prefab not found: " + PlayerPrefabPath);
                return;
            }

            var ranged = playerPrefab.GetComponentInChildren<Weapon.RangedWeapon>();
            if (ranged == null)
            {
                Debug.LogError("[WeaponSetup] RangedWeapon not found in Player prefab");
                return;
            }

            var projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
            if (projectilePrefab == null)
            {
                Debug.LogError("[WeaponSetup] Projectile prefab not found");
                return;
            }

            var so = new SerializedObject(ranged);
            so.FindProperty("m_ProjectilePrefab").objectReferenceValue = projectilePrefab;
            // 射击点 = 武器子对象自身（发射位置在玩家身上）
            so.FindProperty("m_FirePoint").objectReferenceValue = ranged.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 确保 WeaponModComponent 存在（Mod 系统依赖）
            if (ranged.GetComponent<Weapon.WeaponModComponent>() == null)
            {
                ranged.gameObject.AddComponent<Weapon.WeaponModComponent>();
            }

            EditorUtility.SetDirty(playerPrefab);
            Debug.Log("[WeaponSetup] 玩家武器已绑定 ProjectilePrefab + FirePoint");
        }
    }
}
