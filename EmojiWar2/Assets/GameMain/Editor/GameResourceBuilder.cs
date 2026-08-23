//------------------------------------------------------------
// EmojiWar GameMain - 运行时资源一键构建（Editor）
// 菜单：EmojiWar/Tools/Build Runtime AssetBundles
// 用途：构建 GameFramework Package 模式所需的 AssetBundle + version list，
//       并把 Package 输出拷贝到 StreamingAssets，供构建版 exe 加载。
// 资源范围：UI 窗体 prefab（5 个）+ 数据表 txt（3 个）。
// 实体 prefab 与 emoji 美术走 Unity 原生 Resources（Assets/GameMain/Resources/）。
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 运行时资源构建工具（GameFramework Package 模式）。
    /// </summary>
    public static class GameResourceBuilder
    {
        private const string ResourceName = "game";
        private const string OutputDirectory = "D:/EmojiWarStudio/BuildRes";

        private static readonly string[] AssetPaths =
        {
            "Assets/GameMain/UI/MenuForm.prefab",
            "Assets/GameMain/UI/LobbyForm.prefab",
            "Assets/GameMain/UI/ShopForm.prefab",
            "Assets/GameMain/UI/BattleHudForm.prefab",
            "Assets/GameMain/UI/GameOverForm.prefab",
            "Assets/GameMain/DataTables/Character.txt",
            "Assets/GameMain/DataTables/Weapon.txt",
            "Assets/GameMain/DataTables/Mod.txt",
        };

        [MenuItem("EmojiWar/Tools/Build Runtime AssetBundles", false, 100)]
        public static void BuildRuntimeAssetBundles()
        {
            // 0) 校验资源存在
            foreach (var path in AssetPaths)
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                {
                    Debug.LogError("[ResBuilder] 资源不存在: " + path);
                    return;
                }
            }

            // 1) 生成 GameFramework 配置文件（幂等覆盖）
            WriteResourceCollectionXml();
            WriteResourceBuilderXml();

            // 2) 调用官方 ResourceBuilder 构建 AssetBundle + version list
            var controller = new ResourceBuilderController();
            if (!controller.Load())
            {
                Debug.LogError("[ResBuilder] ResourceBuilder 配置加载失败");
                return;
            }

            controller.SelectPlatform(Platform.Windows64, true);

            // 确保输出目录存在（IsValidOutputDirectory 要求目录已存在）
            if (!Directory.Exists(controller.OutputDirectory))
            {
                Directory.CreateDirectory(controller.OutputDirectory);
            }

            if (!controller.BuildResources())
            {
                Debug.LogError("[ResBuilder] BuildResources 失败，详见 BuildReport");
                return;
            }

            // 3) 拷贝 Package 输出到 StreamingAssets（read-only path）
            string srcDir = Path.Combine(controller.OutputPackagePath, "Windows64");
            string dstDir = Path.Combine(Application.dataPath, "StreamingAssets");
            if (!Directory.Exists(srcDir))
            {
                Debug.LogError("[ResBuilder] Package 输出目录不存在: " + srcDir);
                return;
            }

            Directory.CreateDirectory(dstDir);

            // 清理旧的 version list 与资源包（保留其他文件）
            foreach (var f in Directory.GetFiles(dstDir, "*.dat"))
            {
                File.Delete(f);
            }
            foreach (var f in Directory.GetFiles(dstDir, ResourceName + ".*"))
            {
                File.Delete(f);
            }

            foreach (var file in Directory.GetFiles(srcDir))
            {
                string dest = Path.Combine(dstDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
                Debug.Log("[ResBuilder] 拷贝: " + file + " -> " + dest);
            }

            AssetDatabase.Refresh();
            Debug.Log("===== [ResBuilder] 运行时资源构建完成 ✅ =====");
        }

        private static void WriteResourceCollectionXml()
        {
            string dir = Path.Combine(Application.dataPath, "GameFramework/Configs");
            Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<UnityGameFramework>");
            sb.AppendLine("  <ResourceCollection>");
            sb.AppendLine("    <Resources>");
            sb.AppendLine(string.Format("      <Resource Name=\"{0}\" LoadType=\"0\" Packed=\"False\" />", ResourceName));
            sb.AppendLine("    </Resources>");
            sb.AppendLine("    <Assets>");
            foreach (var path in AssetPaths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                sb.AppendLine(string.Format("      <Asset Guid=\"{0}\" ResourceName=\"{1}\" />", guid, ResourceName));
            }
            sb.AppendLine("    </Assets>");
            sb.AppendLine("  </ResourceCollection>");
            sb.AppendLine("</UnityGameFramework>");

            string file = Path.Combine(dir, "ResourceCollection.xml");
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[ResBuilder] 写入 " + file);
        }

        private static void WriteResourceBuilderXml()
        {
            string dir = Path.Combine(Application.dataPath, "GameFramework/Configs");
            Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<UnityGameFramework>");
            sb.AppendLine("  <ResourceBuilder>");
            sb.AppendLine("    <Settings>");
            sb.AppendLine("      <InternalResourceVersion>1</InternalResourceVersion>");
            sb.AppendLine("      <Platforms>2</Platforms>");
            sb.AppendLine("      <AssetBundleCompression>1</AssetBundleCompression>");
            sb.AppendLine("      <CompressionHelperTypeName></CompressionHelperTypeName>");
            sb.AppendLine("      <AdditionalCompressionSelected>False</AdditionalCompressionSelected>");
            sb.AppendLine("      <ForceRebuildAssetBundleSelected>True</ForceRebuildAssetBundleSelected>");
            sb.AppendLine("      <BuildEventHandlerTypeName></BuildEventHandlerTypeName>");
            sb.AppendLine(string.Format("      <OutputDirectory>{0}</OutputDirectory>", OutputDirectory));
            sb.AppendLine("      <OutputPackageSelected>True</OutputPackageSelected>");
            sb.AppendLine("      <OutputFullSelected>False</OutputFullSelected>");
            sb.AppendLine("      <OutputPackedSelected>False</OutputPackedSelected>");
            sb.AppendLine("    </Settings>");
            sb.AppendLine("  </ResourceBuilder>");
            sb.AppendLine("</UnityGameFramework>");

            string file = Path.Combine(dir, "ResourceBuilder.xml");
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[ResBuilder] 写入 " + file);
        }
    }
}
