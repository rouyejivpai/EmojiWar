//------------------------------------------------------------
// EmojiWar GameMain - version list 诊断（Editor）
// 菜单：EmojiWar/Diagnostics/Dump Version List
// 反序列化 Assets/StreamingAssets/GameFrameworkVersion.dat 并打印内容。
//------------------------------------------------------------

using System.IO;
using GameFramework.Resource;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Editor
{
    public static class VersionListDiag
    {
        [MenuItem("EmojiWar/Diagnostics/Dump Version List")]
        public static void Dump()
        {
            // StreamingAssets 不生成 AssetDatabase 条目，直接读文件
            string fullPath = Path.Combine(Application.dataPath, "StreamingAssets/GameFrameworkVersion.dat");
            if (!File.Exists(fullPath))
            {
                Debug.LogError("[VersionListDiag] version list not found: " + fullPath);
                return;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            using (var ms = new MemoryStream(bytes))
            {
                var serializer = new PackageVersionListSerializer();
                serializer.RegisterDeserializeCallback(0, BuiltinVersionListSerializer.PackageVersionListDeserializeCallback_V0);
                serializer.RegisterDeserializeCallback(1, BuiltinVersionListSerializer.PackageVersionListDeserializeCallback_V1);
                serializer.RegisterDeserializeCallback(2, BuiltinVersionListSerializer.PackageVersionListDeserializeCallback_V2);
                var list = serializer.Deserialize(ms);
                if (ReferenceEquals(list, null))
                {
                    Debug.LogError("[VersionListDiag] deserialize returned null (bytes=" + bytes.Length + ")");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[VersionListDiag] version=" + list.ApplicableGameVersion +
                    " internal=" + list.InternalResourceVersion +
                    " assets=" + list.GetAssets().Length +
                    " resources=" + list.GetResources().Length);
                foreach (var a in list.GetAssets())
                {
                    sb.AppendLine("  asset: " + a.Name);
                }
                foreach (var r in list.GetResources())
                {
                    sb.AppendLine("  res: " + r.Name + " ext=" + r.Extension + " len=" + r.Length + " loadType=" + r.LoadType);
                }
                Debug.Log(sb.ToString());
            }
        }
    }
}
