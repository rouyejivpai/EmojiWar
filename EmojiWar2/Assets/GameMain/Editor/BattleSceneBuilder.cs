//------------------------------------------------------------
// EmojiWar GameMain - 战斗场景搭建工具（Editor）
// 菜单：EmojiWar/Setup/02 - Build Battle Scene
// 生成：Player.prefab / Enemy.prefab / BattleManager.prefab / Battle.unity
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 战斗场景搭建工具。
    /// </summary>
    public static class BattleSceneBuilder
    {
        private const string EntitiesDir = "Assets/GameMain/Entities";
        private const string BattleScenePath = "Assets/GameMain/Scenes/Battle.unity";
        private const string PlayerPrefabPath = "Assets/GameMain/Entities/Player.prefab";
        private const string EnemyPrefabPath = "Assets/GameMain/Entities/Enemy.prefab";
        private const string BattleManagerPrefabPath = "Assets/GameMain/Entities/BattleManager.prefab";

        [MenuItem("EmojiWar/Setup/02 - Build Battle Scene")]
        public static void BuildBattleScene()
        {
            EnsureFolder(EntitiesDir);
            EnsureFolder("Assets/GameMain/Scenes");

            // 1. 生成 Player prefab
            CreatePlayerPrefab();

            // 2. 生成 Enemy prefab
            CreateEnemyPrefab();

            // 3. 生成 BattleManager prefab
            CreateBattleManagerPrefab();

            // 4. 创建战斗场景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            // 2D 相机
            var cameraGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera");
            var cam = cameraGo.GetComponent<Camera>();
            if (cam == null) cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            // 背景
            var bgGo = new GameObject("Background");
            var bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.color = new Color(0.12f, 0.14f, 0.17f, 1f);
            bgGo.transform.localScale = new Vector3(50f, 30f, 1f);

            // EventSystem（按钮等 UI 需要）
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            EditorSceneManager.SaveScene(scene, BattleScenePath);

            // 重新打开 Menu 场景（保持编辑器状态）
            EditorSceneManager.OpenScene("Assets/GameMain/Scenes/Menu.unity");

            Debug.Log("[SceneBuilder] Battle scene built: " + BattleScenePath);
        }

        private static void CreatePlayerPrefab()
        {
            var go = new GameObject("Player");
            var sr = go.AddComponent<SpriteRenderer>();
            // 用内置圆形 Sprite 占位（后续替换美术）
            sr.sprite = CreateCircleSprite(0.5f, new Color(0.9f, 0.8f, 0.2f, 1f));
            sr.sortingOrder = 5;

            go.AddComponent<Rigidbody2D>();
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.45f;
            collider.isTrigger = false;

            var player = go.AddComponent<Entity.PlayerEntity>();

            // 武器挂载点
            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(go.transform);
            weaponGo.transform.localPosition = new Vector3(0f, 0f, 0f);
            var weapon = weaponGo.AddComponent<Weapon.RangedWeapon>();

            // 子弹预制体（运行时简单生成）
            var bulletGo = CreateProjectilePrefab();
            var weaponField = typeof(Weapon.RangedWeapon).GetField("m_ProjectilePrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            weaponField.SetValue(weapon, bulletGo);

            PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[SceneBuilder] Player prefab saved.");
        }

        private static void CreateEnemyPrefab()
        {
            var go = new GameObject("Enemy");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(0.4f, new Color(0.85f, 0.2f, 0.2f, 1f));
            sr.sortingOrder = 4;

            go.AddComponent<Rigidbody2D>();
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.38f;
            collider.isTrigger = false;

            go.AddComponent<Entity.EnemyEntity>();

            PrefabUtility.SaveAsPrefabAsset(go, EnemyPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[SceneBuilder] Enemy prefab saved.");
        }

        private static void CreateBattleManagerPrefab()
        {
            var go = new GameObject("BattleManager");
            go.AddComponent<Battle.BattleManager>();

            PrefabUtility.SaveAsPrefabAsset(go, BattleManagerPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[SceneBuilder] BattleManager prefab saved.");
        }

        private static GameObject CreateProjectilePrefab()
        {
            var go = new GameObject("Bullet");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(0.15f, new Color(1f, 1f, 0.6f, 1f));
            sr.sortingOrder = 6;

            go.AddComponent<Rigidbody2D>();
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.12f;
            collider.isTrigger = true;

            go.AddComponent<Weapon.Projectile>();
            return go;
        }

        /// <summary>
        /// 生成内置圆形 Sprite（占位美术）。
        /// </summary>
        private static Sprite CreateCircleSprite(float radius, Color color)
        {
            int size = 64;
            var texture = new Texture2D(size, size);
            var center = new Vector2(size / 2f, size / 2f);
            float r = radius * size;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    texture.SetPixel(x, y, dist <= r ? color : Color.clear);
                }
            }
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
