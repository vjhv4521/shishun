using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{

    public class OptimizationZone
    {
        public Vector2Int coord;
        private Dictionary<string, Selectable> object_list = new Dictionary<string, Selectable>();

        public const float optimization_zone_size = 100f;

        public void AddObject(Selectable obj)
        {
            object_list[obj.GetUID()] = obj;
        }

        public void RemoveObject(Selectable obj)
        {
            object_list.Remove(obj.GetUID());
        }

        public Dictionary<string, Selectable> GetObjects()
        {
            return object_list;
        }

        public static Vector2Int ZonePosToCoord(Vector3 wpos)
        {
            float size = optimization_zone_size;
            int x = Mathf.RoundToInt(wpos.x / size);
            int y = Mathf.RoundToInt(wpos.z / size);
            return new Vector2Int(x, y);
        }

        public static Vector3 CoordToZonePos(Vector2Int coord)
        {
            float size = optimization_zone_size;
            float x = coord.x * size;
            float z = coord.y * size;
            return new Vector3(x, 0f, z);
        }
    }
}