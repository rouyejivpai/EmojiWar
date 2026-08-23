using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Game.Data{
       
         [Serializable]
        public class EnemySpawnData
        {
            public string enemyId;
            public int maxCount;
            public float spawnInterval;
            public float spawnRadius;
            
        }
        
        [Serializable]
        public class BattlePhase
        {
            public string phaseName;
            public float phaseDuration;
            public float phaseDelay;
            public bool waitForClear;
            public int requiredKills;
            public List<EnemySpawnData> enemies = new List<EnemySpawnData>();
        }
        // [CreateAssetMenu( fileName = "BattleConfigAsset", menuName = "Configs/BattleConfigAsset", order = 1 )]
        [Serializable]
        public class BattleConfig 
        {
            public string battleName;
            public string description;
            public List<BattlePhase> phases = new List<BattlePhase>();
            public float totalDuration;
            public bool autoStart;
        }
         [CreateAssetMenu( fileName = "BattleAsset", menuName = "Configs/BattleAsset", order = 0 )]
        public class BattleAsset : ScriptableObject
        {
            
            public List<BattleConfig> phases = new List<BattleConfig>();
          
        }
}