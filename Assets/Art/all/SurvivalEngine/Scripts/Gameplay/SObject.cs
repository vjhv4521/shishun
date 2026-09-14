using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// Base class for either Craftables or Spawnables, has generic static useful functions
    /// </summary>
    public abstract class SObject : MonoBehaviour
    {
        public IdData GetData()
        {
            if (this is Spawnable)
                return ((Spawnable)this).data;
            if (this is Craftable)
                return ((Craftable)this).GetData();
            return null;
        }

        public static int CountSceneObjects(IdData data)
        {
            return CountInRange(data, Vector3.zero, float.MaxValue); //All objects in scene
        }

        public static int CountInRange(IdData data, Vector3 pos, float range)
        {
            if (data is SpawnData)
                return Spawnable.CountInRange(data, pos, range);
            if (data is CharacterData)
                return Character.CountInRange(data, pos, range);
            if (data is ConstructionData)
                return Construction.CountInRange(data, pos, range);
            if (data is PlantData)
                return Plant.CountInRange(data, pos, range);
            if (data is ItemData)
                return Item.CountInRange(data, pos, range);
            return 0; //Ovewritten
        }

        public static int CountInRegion(IdData data, Zone zone)
        {
            if (data is SpawnData)
                return Spawnable.CountInRegion(data, zone);
            if (data is CharacterData)
                return Character.CountInRegion(data, zone);
            if (data is ConstructionData)
                return Construction.CountInRegion(data, zone);
            if (data is PlantData)
                return Plant.CountInRegion(data, zone);
            if (data is ItemData)
                return Item.CountInRegion(data, zone);
            return 0; //Ovewritten
        }

        public static List<SObject> GetAllOf(IdData data)
        {
            if (data is SpawnData)
                return Spawnable.GetAllOf(data);
            if (data is CharacterData)
                return Character.GetAllOf(data);
            if (data is ConstructionData)
                return Construction.GetAllOf(data);
            if (data is PlantData)
                return Plant.GetAllOf(data);
            if (data is ItemData)
                return Item.GetAllOf(data);
            return new List<SObject>();
        }

        //Compability with older version
        public static int CountSceneObjects(IdData data, Vector3 pos, float radius) { return CountInRange(data, pos, radius); }
        public static int CountObjectInRadius(IdData data, Vector3 pos, float radius) { return CountInRange(data, pos, radius); }
        public static List<SObject> GetAllObjectsOf(IdData data) { return GetAllOf(data); }

        //Create a new spawn object in save file and spawn it, also determing its type automatically (Item, Plants, or just Spawn...)
        public static GameObject Create(SData data, Vector3 pos)
        {
            if (data == null)
                return null;

            if (data is CraftData)
            {
                CraftData cdata = (CraftData)data;
                return Craftable.Create(cdata, pos);
            }
            if (data is SpawnData)
            {
                SpawnData spawn_data = (SpawnData)data;
                return Spawnable.Create(spawn_data, pos);
            }
            if (data is LootData)
            {
                LootData loot = (LootData)data;
                if (Random.value <= loot.probability)
                {
                    Item item = Item.Create(loot.item, pos, loot.quantity);
                    return item.gameObject;
                }
            }
            return null;
        }
    }
}

