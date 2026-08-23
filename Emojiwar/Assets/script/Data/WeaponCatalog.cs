using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Data
{
    [Serializable]
    public class WeaponEntry
    {
        public string weaponId;
        public string weaponName;
        public string description;
        public string prefabPath;
        public GameObject prefab;
        public string category;
        public int rarity;
        public float weight;
    }
    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "Configs/WeaponCatalog", order = 8)]
    public class WeaponCatalog : ScriptableObject
    {
        public List<WeaponEntry> weapons = new List<WeaponEntry>();
    }
}