using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Data
{
    // Reuse BuffConfig from BuffFactory for direct compatibility
    [Serializable]
    public class BuffComboAsset
    {
        public string name;
        public List<BuffConfig> buffs = new List<BuffConfig>();
    }
    [CreateAssetMenu(fileName = "BuffCatalog", menuName = "Configs/BuffCatalog", order = 4)]
    public class BuffCatalog : ScriptableObject
    {
        public List<BuffConfig> buffs = new List<BuffConfig>();
        public List<BuffComboAsset> buffCombos = new List<BuffComboAsset>();
    }
}