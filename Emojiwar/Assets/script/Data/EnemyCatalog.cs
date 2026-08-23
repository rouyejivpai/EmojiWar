using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Data
{
    [Serializable]
    public class EnemyStats
    {
        public float health;
        public float speed;
        public float damage;
        public float attackRange;
        public float defense;
    }

    [Serializable]
    public class EnemyEntry
    {
        public string enemyId;
        public string enemyName;
        public string prefabPath;
        public GameObject prefab;
        public string description;
        public EnemyStats baseStats;
        public string difficulty;
        public string enemyType;
    }
    [CreateAssetMenu(fileName = "EnemyCatalog", menuName = "Configs/EnemyCatalog", order = 6)]
    public class EnemyCatalog : ScriptableObject
    {
        public List<EnemyEntry> enemies = new List<EnemyEntry>();
    }
}