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
using UnityEngine.UI;

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
        private const string ShopFormPrefabPath = "Assets/GameMain/UI/ShopForm.prefab";
        private const string BattleHudFormPrefabPath = "Assets/GameMain/UI/BattleHudForm.prefab";

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

            // 4. 生成 ShopForm prefab
            CreateShopFormPrefab();

            // 4.5 生成 BattleHudForm prefab
            CreateBattleHudFormPrefab();

            // 5. 创建战斗场景
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

        /// <summary>
        /// 生成 ShopForm prefab（标题 + 金币 + 信息 + 商品根 + 继续按钮）。
        /// </summary>
        private static void CreateShopFormPrefab()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("ShopForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<UI.ShopForm>();

            // 半透明背景
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(root.transform, false);
            var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.7f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // 标题
            var title = CreateUIText("Title", root.transform, "波间商店", 48, new Vector2(0, 380));
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 80);

            // 金币
            var coin = CreateUIText("CoinText", root.transform, "金币：0", 32, new Vector2(0, 300));
            coin.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 60);

            // 信息
            var info = CreateUIText("InfoText", root.transform, "", 26, new Vector2(0, -280));
            info.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 50);

            // 继续按钮
            var continueBtn = CreateUIButton("ContinueButton", root.transform, "继续战斗", new Vector2(0, -350));

            // 通过反射绑定字段
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_CoinText", flags).SetValue(form, coin.GetComponent<UnityEngine.UI.Text>());
            form.GetType().GetField("m_InfoText", flags).SetValue(form, info.GetComponent<UnityEngine.UI.Text>());
            form.GetType().GetField("m_ContinueButton", flags).SetValue(form, continueBtn.GetComponent<UnityEngine.UI.Button>());

            // 确保 EventSystem 存在
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            PrefabUtility.SaveAsPrefabAsset(root, ShopFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[SceneBuilder] ShopForm prefab saved.");
        }

        /// <summary>
        /// 生成战斗 HUD prefab（金币/波次/血量）。
        /// </summary>
        private static void CreateBattleHudFormPrefab()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("BattleHudForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<UI.BattleHudForm>();

            // 左上角信息（锚定左上）
            var coin = CreateUIText("CoinText", root.transform, "金币：0", 28, Vector2.zero);
            SetAnchoredTopLeft(coin.GetComponent<RectTransform>(), new Vector2(30, -20), new Vector2(300, 40));

            var wave = CreateUIText("WaveText", root.transform, "波次：0", 28, Vector2.zero);
            SetAnchoredTopLeft(wave.GetComponent<RectTransform>(), new Vector2(30, -60), new Vector2(300, 40));

            var hp = CreateUIText("HpText", root.transform, "HP: --", 32, Vector2.zero);
            SetAnchoredTopLeft(hp.GetComponent<RectTransform>(), new Vector2(30, -100), new Vector2(300, 50));

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_CoinText", flags).SetValue(form, coin.GetComponent<UnityEngine.UI.Text>());
            form.GetType().GetField("m_WaveText", flags).SetValue(form, wave.GetComponent<UnityEngine.UI.Text>());
            form.GetType().GetField("m_HpText", flags).SetValue(form, hp.GetComponent<UnityEngine.UI.Text>());

            PrefabUtility.SaveAsPrefabAsset(root, BattleHudFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[SceneBuilder] BattleHudForm prefab saved.");
        }

        /// <summary>
        /// 设置 RectTransform 锚定左上角。
        /// </summary>
        private static void SetAnchoredTopLeft(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>
        /// 创建 UI 文本。
        /// </summary>
        private static GameObject CreateUIText(string name, Transform parent, string content, int fontSize, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;

            var text = go.AddComponent<UnityEngine.UI.Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        /// <summary>
        /// 创建 UI 按钮。
        /// </summary>
        private static GameObject CreateUIButton(string name, Transform parent, string label, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(360, 90);

            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.15f, 0.6f, 0.3f, 1f);

            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;

            var labelGo = CreateUIText("Label", go.transform, label, 28, Vector2.zero);
            labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(340, 80);

            return go;
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
