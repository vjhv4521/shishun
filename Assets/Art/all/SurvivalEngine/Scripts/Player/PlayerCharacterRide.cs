using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// Script to allow player swimming
    /// Make sure the player character has a unique layer set to it (like Player layer)
    /// </summary>

    [RequireComponent(typeof(PlayerCharacter))]
    public class PlayerCharacterRide : MonoBehaviour
    {
        private PlayerCharacter character;
        private bool is_riding = false;
        private AnimalRide riding_animal = null;

        void Awake()
        {
            character = GetComponent<PlayerCharacter>();
        }

        private void Start()
        {
            PlayerControlsMouse mouse = PlayerControlsMouse.Get();
            mouse.onClick += OnClick;
            mouse.onHold += OnMouseHold;
            mouse.onLongClick += OnLongClick;
            mouse.onRightClick += OnRightClick;
        }

        private void OnDestroy()
        {
            PlayerControlsMouse mouse = PlayerControlsMouse.Get();
            mouse.onClick -= OnClick;
            mouse.onHold -= OnMouseHold;
            mouse.onLongClick -= OnLongClick;
            mouse.onRightClick -= OnRightClick;
        }

        void Update()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (character.IsDead())
                return;

            if (is_riding)
            {
                if (riding_animal == null || riding_animal.IsDead())
                {
                    StopRide();
                    return;
                }

                transform.position = riding_animal.GetRideRoot();
                transform.rotation = Quaternion.LookRotation(riding_animal.transform.forward, Vector3.up);

                //Stop riding
                PlayerControls controls = PlayerControls.Get(character.player_id);
                if (character.IsControlsEnabled())
                {
                    if (controls.IsPressJump() || controls.IsPressAction() || controls.IsPressUICancel())
                        StopRide();
                }
            }
        }

        public void RideNearest()
        {
            AnimalRide animal = AnimalRide.GetNearest(transform.position, 2f);
            RideAnimal(animal);
        }

        public void RideAnimal(AnimalRide animal)
        {
            if (!is_riding && character.IsMovementEnabled() && animal != null)
            {
                is_riding = true;
                character.SetBusy(true);
                character.DisableMovement();
                character.DisableCollider();
                riding_animal = animal;
                transform.position = animal.GetRideRoot();
                animal.SetRider(character);
            }
        }

        public void StopRide()
        {
            if (is_riding)
            {
                if (riding_animal != null)
                    riding_animal.StopRide();
                is_riding = false;
                character.SetBusy(false);
                character.EnableMovement();
                character.EnableCollider();
                character.FaceDir(transform.forward);
                character.StopMove();
                riding_animal = null;
            }
        }


        //--- on Click

        private void OnClick(Vector3 pos, Selectable select)
        {
            if (is_riding)
            {
                if (character.interact_type == PlayerInteractBehavior.MoveAndInteract)
                    riding_animal.MoveTo(pos);
            }
        }

        private void OnMouseHold(Vector3 pos)
        {
            if (TheGame.IsMobile())
                return; //On mobile, use joystick instead, no mouse hold

            if (is_riding)
            {
                if (character.interact_type == PlayerInteractBehavior.MoveAndInteract)
                    riding_animal.DirectMoveTo(pos);
            }
        }

        private void OnLongClick(Vector3 pos)
        {
            if (is_riding)
            {
                float diff = (riding_animal.transform.position - pos).magnitude;
                if (diff < 2f)
                {
                    riding_animal.RemoveRider();
                }
            }
        }

        private void OnRightClick(Vector3 pos, Selectable select)
        {
            if (is_riding)
            {
                riding_animal.RemoveRider();
            }
        }

        public bool IsRiding()
        {
            return is_riding;
        }

        public AnimalRide GetAnimal()
        {
            return riding_animal;
        }

        public PlayerCharacter GetCharacter()
        {
            return character;
        }
    }

}
