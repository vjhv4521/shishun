using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// Allow you to ride the animal
    /// </summary>
    
    [RequireComponent(typeof(Character))]
    public class AnimalRide : MonoBehaviour
    {
        public float ride_speed = 5f;
        public Transform ride_root;
        public bool use_navmesh = true;

        private Character character;
        private Selectable select;
        private Animator animator;
        private AnimalWild wild;
        //private AnimalLivestock livestock;
        private float regular_speed;
        private bool default_avoid;
        private bool default_navmesh;

        private PlayerCharacter rider = null;

        private static List<AnimalRide> animal_list = new List<AnimalRide>();

        void Awake()
        {
            animal_list.Add(this);
            character = GetComponent<Character>();
            select = GetComponent<Selectable>();
            wild = GetComponent<AnimalWild>();
            //livestock = GetComponent<AnimalLivestock>();
            animator = GetComponentInChildren<Animator>();
            regular_speed = character.move_speed;
            default_avoid = character.avoid_obstacles;
            default_navmesh = character.use_navmesh;
            character.onDeath += OnDeath;
        }

        private void OnDestroy()
        {
            animal_list.Remove(this);
        }

        void FixedUpdate()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (IsDead())
                return;

            if (rider == null)
                return;


        }

        void Update()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (IsDead())
                return;

            if (rider != null)
            {
                PlayerControls controls = PlayerControls.Get(rider.player_id);
                JoystickMobile joystick = JoystickMobile.Get();

                Vector3 cmove = controls.GetMove();
                Vector3 wmove = new Vector3(cmove.x, 0f, cmove.y);
                Vector3 cam_move = TheCamera.Get().GetRotation() * wmove;
                if (joystick != null && joystick.IsActive())
                {
                    Vector2 joy_dir = joystick.GetDir();
                    cam_move = TheCamera.Get().GetRotation() * new Vector3(joy_dir.x, 0f, joy_dir.y);
                }

                Vector3 tmove = cam_move * ride_speed;
                if(tmove.magnitude > 0.1f)
                    character.DirectMoveToward(tmove);

                //Character stuck
                if (tmove.magnitude < 0.1f && character.IsStuck())
                    character.Stop();
            }

            //Animations
            if (animator.enabled)
            {
                animator.SetBool("Move", IsMoving());
                animator.SetBool("Run", IsMoving());
            }
        }

        public void SetRider(PlayerCharacter player)
        {
            if (rider == null) {
                rider = player;
                character.move_speed = ride_speed;
                character.avoid_obstacles = false;
                character.use_navmesh = use_navmesh;
                character.Stop();
                if (wild != null)
                    wild.enabled = false;
                //if (livestock != null)
                //    livestock.enabled = false;
            }
        }

        public void StopRide()
        {
            if (rider != null)
            {
                rider = null;
                character.move_speed = regular_speed;
                character.avoid_obstacles = default_avoid;
                character.use_navmesh = default_navmesh;
                StopMove();
                if (wild != null)
                    wild.enabled = true;
                //if (livestock != null)
                //    livestock.enabled = true;
            }
        }

        public void StopMove()
        {
            character.Stop();
            animator.SetBool("Move", false);
            animator.SetBool("Run", false);
        }

        public void RemoveRider()
        {
            if (rider != null)
            {
                rider.Riding.StopRide();
            }
        }

        public void MoveTo(Vector3 pos)
        {
            character.MoveTo(pos);
        }

        public void DirectMoveTo(Vector3 pos)
        {
            character.DirectMoveTo(pos);
        }

        void OnDeath()
        {
            animator.SetTrigger("Death");
        }

        public bool IsDead()
        {
            return character.IsDead();
        }

        public bool IsMoving()
        {
            return character.IsMoving();
        }

        public Vector3 GetMove()
        {
            return character.GetMove();
        }

        public Vector3 GetFacing()
        {
            return character.GetFacing();
        }

        public Vector3 GetRideRoot()
        {
            return ride_root != null ? ride_root.position : transform.position;
        }

        public Character GetCharacter()
        {
            return character;
        }

        public static AnimalRide GetNearest(Vector3 pos, float range = 999f)
        {
            float min_dist = range;
            AnimalRide nearest = null;
            foreach (AnimalRide animal in animal_list)
            {
                float dist = (animal.transform.position - pos).magnitude;
                if (dist < min_dist)
                {
                    min_dist = dist;
                    nearest = animal;
                }
            }
            return nearest;
        }

        public static List<AnimalRide> GetAll()
        {
            return animal_list;
        }
    }

}
