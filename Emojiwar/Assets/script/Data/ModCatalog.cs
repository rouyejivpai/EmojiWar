using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Data
{
    [Serializable]
    public class ModBasicInfo
    {
        public string modId;
        public string modName;
        public string description;
        public string prefabPath;
        public GameObject modPrefab;
        public string imagePath;
        public Sprite icon;
        public string category;
        public int rarity;
        public float weight;
    }
    [CreateAssetMenu(fileName = "ModCatalog", menuName = "Configs/ModCatalog", order = 7)]
    public class ModCatalog : ScriptableObject
    {
        public List<ModBasicInfo> mods = new List<ModBasicInfo>();
    }
}