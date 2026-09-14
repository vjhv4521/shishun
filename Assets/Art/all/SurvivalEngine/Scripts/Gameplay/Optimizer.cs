using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{

    [DefaultExecutionOrder(-1)]
    public class Optimizer : MonoBehaviour
    {
        private Dictionary<Vector2Int, OptimizationZone> optim_zones = new Dictionary<Vector2Int, OptimizationZone>();
        private LinkedList<OptimizationZone> all_zones = new LinkedList<OptimizationZone>();
        private float update_timer = 0f;

        private static Optimizer instance;

        private void Awake()
        {
            instance = this;
        }

        private void OnDestroy()
        {
            optim_zones.Clear();
            all_zones.Clear();
        }

        private void Start()
        {
            //Turn off all selectables at first
            foreach (Selectable select in Selectable.GetAllDynamic())
                select.SetActive(false);
            foreach (Selectable select in Selectable.GetAllStatic())
                select.SetActive(false);
        }

        private void Update()
        {
            //Slow update
            update_timer += Time.deltaTime;
            if (update_timer > GameData.Get().optim_refresh_rate)
            {
                update_timer = 0f;
                SlowUpdate();
            }
        }

        private void SlowUpdate()
        {
            //Optimization Loop
            Vector3 center_pos = TheCamera.Get().GetTargetPosOffsetFace(GameData.Get().optim_facing_offset);
            float dist_mult = GameData.Get().optim_distance_multiplier;

            //Turn off active
            UnityEngine.Profiling.Profiler.BeginSample("Optimization Turn Off Active");
            List<Selectable> turn_off_list = new List<Selectable>();
            foreach (Selectable obj in Selectable.GetAllActive())
            {
                if (obj != null)
                {
                    float dist = (obj.GetPosition() - center_pos).magnitude;
                    if (dist > obj.active_range * dist_mult)
                        turn_off_list.Add(obj);
                }
            }

            foreach(Selectable obj in turn_off_list)
                obj.SetActive(false);
            UnityEngine.Profiling.Profiler.EndSample();

            //Optimization Loop (dynamic)
            UnityEngine.Profiling.Profiler.BeginSample("Optimization Loop Dynamic");
            LinkedList<Selectable> objs = Selectable.GetAllDynamic();

            foreach (Selectable obj in objs)
            {
                if (obj != null)
                {
                    float dist = (obj.GetPosition() - center_pos).magnitude;
                    obj.SetActive(dist < obj.active_range * dist_mult);
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();

            //Optimization Loop (static)
            UnityEngine.Profiling.Profiler.BeginSample("Optimization Loop Static");
            foreach (OptimizationZone zone in all_zones)
            {
                Vector3 zone_center = OptimizationZone.CoordToZonePos(zone.coord);
                float cdist = (zone_center - center_pos).magnitude;
                if (cdist < OptimizationZone.optimization_zone_size)
                {
                    foreach (KeyValuePair<string, Selectable> opair in zone.GetObjects())
                    {
                        Selectable obj = opair.Value;
                        if (obj != null)
                        {
                            float dist = (obj.GetPosition() - center_pos).magnitude;
                            obj.SetActive(dist < obj.active_range * dist_mult);
                        }
                    }
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        public OptimizationZone GetZone(Vector3 wpos, bool create = false)
        {
            Vector2Int coord = OptimizationZone.ZonePosToCoord(wpos);
            return GetZone(coord, create);
        }

        public OptimizationZone GetZone(Vector2Int coord, bool create = false)
        {
            if (optim_zones.ContainsKey(coord))
                return optim_zones[coord];
            if (create)
            {
                OptimizationZone zone = new OptimizationZone();
                zone.coord = coord;
                optim_zones[coord] = zone;
                all_zones.AddLast(zone);
                return zone;
            }
            return null;
        }

        public static Optimizer Find()
        {
            if (instance == null)
                instance = FindAnyObjectByType<Optimizer>();
            return instance;
        }

        public static Optimizer Get()
        {
            return instance;
        }

        public static bool Exists()
        {
            return instance != null;
        }
    }

}