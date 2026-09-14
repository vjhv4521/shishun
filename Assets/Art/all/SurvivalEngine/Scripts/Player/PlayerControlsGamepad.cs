using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SurvivalEngine
{
    /// <summary>
    /// Wrapper class for integrating gamepads with the new Input System
    /// </summary>

    public class PlayerControlsGamepad : MonoBehaviour
    {
        public int player_id = 0; 

        public GamepadButton action = GamepadButton.A;
        public GamepadButton attack = GamepadButton.X;
        public GamepadButton attack2 = GamepadButton.RightShoulder;
        public GamepadButton jump = GamepadButton.Y;
        public GamepadButton use = GamepadButton.B;
        public GamepadButton craft = GamepadButton.LeftShoulder;
        
        public GamepadButton menu_accept = GamepadButton.A;
        public GamepadButton menu_cancel = GamepadButton.B;
        public GamepadButton menu_pause = GamepadButton.Start;
        public GamepadButton camera_left = GamepadButton.LeftTrigger;
        public GamepadButton camera_right = GamepadButton.RightTrigger;

        private Gamepad active_gamepad;
        private Vector2 prev_leftStick = Vector2.zero;
        private Vector2 prev_righStick = Vector2.zero;
        private Vector2 prev_dPad = Vector2.zero;

        private void Awake()
        {
            
        }

        void Start()
        {
            active_gamepad = Gamepad.current;

            PlayerControls controls = PlayerControls.Get(player_id);
            controls.gamepad_linked = true;

            controls.gamepad_action = () => { return WasPressed(active_gamepad, action); };
            controls.gamepad_attack = () => { return WasPressed(active_gamepad, attack) || WasPressed(active_gamepad, attack2); };
            controls.gamepad_jump = () => { return WasPressed(active_gamepad, jump); };
            controls.gamepad_use = () => { return WasPressed(active_gamepad, use); };
            controls.gamepad_craft = () => { return WasPressed(active_gamepad, craft); };
            controls.gamepad_accept = () => { return WasPressed(active_gamepad, menu_accept); };
            controls.gamepad_cancel = () => { return WasPressed(active_gamepad, menu_cancel); };
            controls.gamepad_pause = () => { return WasPressed(active_gamepad, menu_pause); };

            controls.gamepad_move = () => { return GetLeftStick(active_gamepad); };
            controls.gamepad_freelook = () => { return GetRightStick(active_gamepad); };
            controls.gamepad_menu = () => { return GetLeftStickPress(active_gamepad) + GetDPadPress(active_gamepad); };
            controls.gamepad_dpad = () => { return GetDPadPress(active_gamepad); };
            controls.gamepad_camera = () => { return new Vector2(-GetAxis(active_gamepad, camera_left) + GetAxis(active_gamepad, camera_right), 0f); };
            controls.gamepad_update += UpdateSync;
        }

        void Update()
        {
            active_gamepad = Gamepad.current;
        }

        void UpdateSync()
        {
            prev_leftStick = GetLeftStick(active_gamepad);
            prev_righStick = GetRightStick(active_gamepad);
            prev_dPad = GetDPad(active_gamepad);
        }

        private bool WasPressed(Gamepad device, GamepadButton type)
        {
            if (device != null)
                return device[type].wasPressedThisFrame;
            return false;
        }

        private float GetAxis(Gamepad device, GamepadButton type)
        {
            if (device != null)
                return device[type].ReadValue();
            return 0f;
        }

        private Vector2 GetLeftStick(Gamepad device)
        {
            if (device != null)
                return device.leftStick.ReadValue();
            return Vector2.zero;
        }

        private Vector2 GetRightStick(Gamepad device)
        {
            if (device != null)
                return device.rightStick.ReadValue();
            return Vector2.zero;
        }

        private Vector2 GetDPad(Gamepad device)
        {
            if (device != null)
                return device.dpad.ReadValue();
            return Vector2.zero;
        }

        private Vector2 GetLeftStickPress(Gamepad device)
        {
            if (device != null)
            {
                Vector2 val = device.leftStick.ReadValue();
                float x = (Mathf.Abs(prev_leftStick.x) < 0.5f && Mathf.Abs(val.x) >= 0.5f) ? Mathf.Sign(val.x) : 0f;
                float y = (Mathf.Abs(prev_leftStick.y) < 0.5f && Mathf.Abs(val.y) >= 0.5f) ? Mathf.Sign(val.y) : 0f;
                return new Vector2(x, y);

            }
            return Vector2.zero;
        }

        private Vector2 GetRightStickPress(Gamepad device)
        {
            if (device != null)
            {
                Vector2 val = device.rightStick.ReadValue();
                float x = (Mathf.Abs(prev_righStick.x) < 0.5f && Mathf.Abs(val.x) >= 0.5f) ? Mathf.Sign(val.x) : 0f;
                float y = (Mathf.Abs(prev_righStick.y) < 0.5f && Mathf.Abs(val.y) >= 0.5f) ? Mathf.Sign(val.y) : 0f;
                return new Vector2(x, y);

            }
            return Vector2.zero;
        }

        private Vector2 GetDPadPress(Gamepad device)
        {
            if (device != null)
            {
                Vector2 val = device.dpad.ReadValue();
                float x = (Mathf.Abs(prev_dPad.x) < 0.5f && Mathf.Abs(val.x) >= 0.5f) ? Mathf.Sign(val.x) : 0f;
                float y = (Mathf.Abs(prev_dPad.y) < 0.5f && Mathf.Abs(val.y) >= 0.5f) ? Mathf.Sign(val.y) : 0f;
                return new Vector2(x, y);

            }
            return Vector2.zero;
        }
    }
}