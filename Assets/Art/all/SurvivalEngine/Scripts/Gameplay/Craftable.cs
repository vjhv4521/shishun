using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// Base class for Items, Constructions, Characters, Plants
    /// </summary>

    [RequireComponent(typeof(Selectable))]
    [RequireComponent(typeof(UniqueID))]
    public abstract class Craftable : SObject
    {
        private Selectable cselect;
        private Destructible cdestruct;
        private Buildable cbuildable;

        protected virtual void Awake()
        {
            cselect = GetComponent<Selectable>();
            cdestruct = GetComponent<Destructible>();
            cbuildable = GetComponent<Buildable>();
        }

        //Get the data based on which type of object it is
        public new CraftData GetData()
        {
            if (this is Item)
                return ((Item)this).data;
            if (this is Plant)
                return ((Plant)this).data;
            if (this is Construction)
                return ((Construction)this).data;
            if (this is Character)
                return ((Character)this).data;
            return null;
        }

        //Destroy the object
        public void Destroy()
        {
            Destructible destruct = GetComponent<Destructible>();
            Item item = GetComponent<Item>();
            if (destruct != null)
                destruct.Kill(); //Kill destruct to spawn loot and save
            else if (item != null)
                item.DestroyItem(); //Or destroy item if its an item
            else if (cselect != null)
                cselect.Destroy(); //Or destroy selectable otherwise
        }

        public Selectable Selectable { get { return cselect; } }
        public Destructible Destructible { get { return cdestruct; } } //Can be null
        public Buildable Buildable { get { return cbuildable; } }    //Can be null

        //--- Static functions for easy access

        public static GameObject Create(CraftData data, Vector3 pos)
        {
            if (data == null)
                return null;

            if (data is ItemData)
            {
                ItemData item = (ItemData)data;
                Item obj = Item.Create(item, pos, 1);
                return obj.gameObject;
            }

            if (data is PlantData)
            {
                PlantData item = (PlantData)data;
                Plant obj = Plant.Create(item, pos, -1);
                return obj.gameObject;
            }

            if (data is ConstructionData)
            {
                ConstructionData item = (ConstructionData)data;
                Construction obj = Construction.Create(item, pos);
                return obj.gameObject;
            }

            if (data is CharacterData)
            {
                CharacterData item = (CharacterData)data;
                Character obj = Character.Create(item, pos);
                return obj.gameObject;
            }

            return null;
        }
    }
}
