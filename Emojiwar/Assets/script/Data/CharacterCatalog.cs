using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Data
{
    [Serializable]
    public class CharacterEntry
    {
        public string characterId;
        public string characterName;
        public string description;
        public string prefabPath;
        public GameObject prefab;
        public int defaultWeaponId;
        public int secondWeaponId;
        public int maxHealth;
        public float moveSpeed;
        public Sprite icon;

        public int coin;
    }
    [CreateAssetMenu(fileName = "CharacterCatalog", menuName = "Configs/CharacterCatalog", order = 5)]
    public class CharacterCatalog : ScriptableObject
    {
        public string defaultCharacterId;
        public List<CharacterEntry> characters = new List<CharacterEntry>();
    }
}