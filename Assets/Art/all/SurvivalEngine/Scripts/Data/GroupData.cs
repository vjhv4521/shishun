using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{

    /// <summary>
    /// A group doesn't do anything by itself, but it serves to reference objects as a group instead of referencing them one by one.
    /// Ex: all tools that can cut trees could be in the group 'CutTree', and trees would have a requirement that need the player to hold a 'CutTree' item.
    /// This avoid having to reference the new item to each tree everytime you create a new type of axe. You could just add the new axe to the group that is already attached to every exisitng tree.
    /// </summary>

    [CreateAssetMenu(fileName = "GroupData", menuName = "SurvivalEngine/GroupData", order = 1)]
    public class GroupData : IdData
    {
        public string title;
        public Sprite icon;

        private static List<GroupData> group_data = new List<GroupData>();

        public static void Load(string folder = "")
        {
            group_data.Clear();
            group_data.AddRange(Resources.LoadAll<GroupData>(folder));
            TheData.CheckDuplicates(group_data);
        }

        public static GroupData Get(string id)
        {
            foreach (GroupData data in group_data)
            {
                if (data.id == id)
                    return data;
            }
            return null;
        }

        public static List<GroupData> GetAll()
        {
            return group_data;
        }
    }

}