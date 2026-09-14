using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// Use your fishing rod to fish a fish!
    /// </summary>
    
    [CreateAssetMenu(fileName = "Action", menuName = "SurvivalEngine/Actions/Fish", order = 50)]
    public class ActionFish : SAction
    {
        public GroupData fishing_rod;
        public float fish_time = 3f; //In seconds

        public override void DoAction(PlayerCharacter character, Selectable select)
        {
            if (select != null)
            {
                ItemProvider pond = select.GetComponent<ItemProvider>();
                if (pond != null && pond.HasItem())
                {
                    character.FaceTorward(pond.transform.position);
                    character.TriggerProgressBusy(this, fish_time, () =>
                    {
                        pond.RemoveItem();
                        pond.GainItem(character, 1);
                        character.Attributes.GainXP("fishing", 10); //Example of XP gain
                    });
                }
            }
        }

        public override bool CanDoAction(PlayerCharacter character, Selectable select)
        {
            ItemProvider pond = select.GetComponent<ItemProvider>();
            return pond != null && pond.HasItem() && character.EquipData.HasItemInGroup(fishing_rod) && !character.IsSwimming();
        }
    }

}