using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SurvivalEngine
{

    /// <summary>
    /// Mouse/Touch controls manager
    /// </summary>

    public class PlayerControlsMouse : MonoBehaviour
    {
        public LayerMask selectable_layer = ~0;
        public LayerMask floor_layer = (1 << 9); //Put to none to always return 0 as floor height

        public UnityAction<Vector3, Selectable> onClick; //Always triggered on left click
        public UnityAction<Vector3, Selectable> onRightClick; //Always triggered on right click
        public UnityAction<Vector3> onLongClick; //When holding the left click down for 1+ sec
        public UnityAction<Vector3> onHold; //While holding the left click down
        public UnityAction<Vector3> onRelease; //Release mouse button

        private bool mouse_hold_left = false;
        private bool mouse_hold_right = false;

        private float using_timer = 1f;
        private float hold_timer = 0f;
        private float hold_total_timer = 0f;
        private bool is_holding = false;
        private bool has_mouse = false;
        private bool can_long_click = false;
        private Vector2 hold_start;
        private Vector2 last_pos;
        private Vector3 floor_pos; //World position the floor pointing at

        private float zoom_value = 0f;
        private float rotate_value = 0f;
        private bool is_zoom_mode = false;
        private Vector2 prev_touch1 = Vector3.zero;
        private Vector2 prev_touch2 = Vector3.zero;

        private HashSet<Selectable> raycast_list = new HashSet<Selectable>();

        private static PlayerControlsMouse _instance;

        void Awake()
        {
            _instance = this;
            last_pos = GetMousePosition();
        }

        void Update()
        {
            Pointer input = GetInput();

            //If not mobile, always check for raycast (on mobile it does it only after a click)
            if (!TheGame.IsMobile())
            {
                RaycastSelectables();
                RaycastFloorPos();
            }

            //Mouse click
            if (IsLeftClick() && IsInGameplay())
            {
                hold_start = input.position.ReadValue();
                is_holding = true;
                can_long_click = true;
                hold_timer = 0f;
                hold_total_timer = 0f;
                OnMouseClick();
            }

            if (IsLeftRelease())
            {
                OnMouseRelease();
            }

            if (IsRightClick() && IsInGameplay())
            {
                OnRightMouseClick();
            }

            //Check for mouse usage
            Vector2 mouse_pos = GetMousePosition();
            Vector2 diff = (mouse_pos - last_pos);
            float dist = diff.magnitude;
            if (dist > 0.01f)
            {
                using_timer = 0f;
                last_pos = mouse_pos;
            }

            mouse_hold_left = IsLeftHold() && IsInGameplay();
            mouse_hold_right = IsRightHold() && IsInGameplay();
            if (mouse_hold_left || mouse_hold_right)
                using_timer = 0f;

            //Is using mouse? (vs keyboard)
            using_timer += Time.deltaTime;
            has_mouse = has_mouse || IsUsingMouse();

            //Long mouse click
            float dist_hold = (mouse_pos - hold_start).magnitude;
            is_holding = is_holding && mouse_hold_left;
            can_long_click = can_long_click && mouse_hold_left && dist_hold < 5f;

            if (is_holding)
            {
                hold_timer += Time.deltaTime;
                hold_total_timer += Time.deltaTime;
                if (can_long_click && hold_timer > 0.5f)
                {
                    can_long_click = false;
                    hold_timer = 0f;
                    OnLongMouseClick();
                }
                else if (!can_long_click && hold_timer > 0.2f)
                {
                    OnMouseHold();
                }
            }

            //Mobile joystick and zoom
            if (TheGame.IsMobile())
            {
                zoom_value = 0f;
                rotate_value = 0f;
                if (Touchscreen.current != null && CountActiveTouch() == 2)
                {
                    Vector2 pos1 = Touchscreen.current.touches[0].position.ReadValue();
                    Vector2 pos2 = Touchscreen.current.touches[1].position.ReadValue();
                    if (is_zoom_mode)
                    {
                        float distance = Vector2.Distance(pos1, pos2);
                        float prev_distance = Vector2.Distance(prev_touch1, prev_touch2);
                        zoom_value = (distance - prev_distance) / (float)Screen.height;

                        var pDir = prev_touch2 - prev_touch1;
                        var cDir = pos2 - pos1;
                        rotate_value = Vector2.SignedAngle(pDir, cDir);
                        rotate_value = Mathf.Clamp(rotate_value, -45f, 45f);
                    }
                    prev_touch1 = pos1;
                    prev_touch2 = pos2;
                    is_zoom_mode = true; //Wait one frame to make sure distance has been calculated once
                }
                else
                {
                    is_zoom_mode = false;
                }
            }
        }

        public void RaycastSelectables()
        {
            foreach (Selectable select in raycast_list)
                select.SetHover(false);
            raycast_list.Clear();

            if (TheUI.Get().IsBlockingPanelOpened())
                return;

            PlayerUI ui = PlayerUI.GetFirst();
            if (ui != null && ui.IsBuildMode())
                return; //Dont hover/select things in build mode

            RaycastHit[] hits = Physics.RaycastAll(GetMouseCameraRay(), 99f, selectable_layer.value);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider != null)
                {
                    Selectable select = hit.collider.GetComponentInParent<Selectable>();
                    if (select != null)
                    {
                        raycast_list.Add(select);
                        select.SetHover(true);
                    }
                }
            }
        }

        public void RaycastFloorPos()
        {
            Ray ray = GetMouseCameraRay();
            RaycastHit hit;
            bool success = Physics.Raycast(ray, out hit, 100f, floor_layer.value, QueryTriggerInteraction.Ignore);
            if (success)
            {
                floor_pos = ray.GetPoint(hit.distance);
            }
            else
            {
                Plane plane = new Plane(Vector3.up, 0f);
                float dist;
                bool phit = plane.Raycast(ray, out dist);
                if (phit)
                {
                    floor_pos = ray.GetPoint(dist);
                }
            }

            //Debug.DrawLine(TheCamera.GetCamera().transform.position, floor_pos);
        }

        private void MobileCheckRaycast()
        {
            //If mobile, only check for raycast on click (on desktop it does it every frame in Update)
            if (TheGame.IsMobile())
            {
                RaycastSelectables();
                RaycastFloorPos();
            }
        }

        private void OnMouseClick()
        {
            if (IsMouseOverUI())
                return;

            MobileCheckRaycast();

            Selectable hovered = GetNearestRaycastList(floor_pos);
            onClick?.Invoke(floor_pos, hovered);
        }

        private void OnMouseRelease()
        {
            if (IsMouseOverUI())
                return;

            MobileCheckRaycast();

            onRelease?.Invoke(floor_pos);
        }

        private void OnRightMouseClick()
        {
            if (IsMouseOverUI())
                return;

            MobileCheckRaycast();

            Selectable hovered = GetNearestRaycastList(floor_pos);
            onRightClick?.Invoke(floor_pos, hovered);
        }

        //When holding for 1+ sec
        private void OnLongMouseClick()
        {
            if (IsMouseOverUI())
                return;

            MobileCheckRaycast();

            onLongClick?.Invoke(floor_pos);
        }

        private void OnMouseHold()
        {
            if (IsMouseOverUI())
                return;

            MobileCheckRaycast();

            onHold?.Invoke(floor_pos);
        }

        public Selectable GetNearestRaycastList(Vector3 pos)
        {
            Selectable nearest = null;
            float min_dist = 99f;
            foreach (Selectable select in raycast_list)
            {
                if (select != null && select.CanBeClicked())
                {
                    float dist = (select.transform.position - pos).magnitude;
                    if (dist < min_dist)
                    {
                        min_dist = dist;
                        nearest = select;
                    }
                }
            }
            return nearest;
        }

        public Vector2 GetScreenPos()
        {
            //In percentage
            Vector3 mpos = GetMousePosition();
            return new Vector2(mpos.x / (float)Screen.width, mpos.y / (float)Screen.height);
        }

        public Vector3 GetPointingPos()
        {
            return floor_pos;
        }

        public bool IsInRaycast(Selectable select)
        {
            return raycast_list.Contains(select);
        }

        public HashSet<Selectable> GetRaycastList()
        {
            return raycast_list;
        }

        //Is the user curently using the mouse?
        public bool IsUsingMouse()
        {
            return IsMovingMouse(1f); //Using mouse if moved it in the last second
        }

        public bool IsMovingMouse(float offset = 0.1f)
        {
            return GetTimeSinceLastMouseMove() <= offset;
        }

        public float GetTimeSinceLastMouseMove()
        {
            return using_timer;
        }

        public bool IsMouseHold()
        {
            return mouse_hold_left;
        }

        public bool IsMouseHoldRight()
        {
            return mouse_hold_right;
        }
		
		public bool IsMouseHoldUI()
        {
            return IsLeftHold();
        }

        public float GetMouseHoldDuration()
        {
            return hold_total_timer;
        }

        public float GetTouchZoom()
        {
            return zoom_value;
        }

        public float GetTouchRotate()
        {
            return rotate_value;
        }

        public int CountActiveTouch()
        {
            int count = 0;
            if (Touchscreen.current != null)
            {
                foreach (UnityEngine.InputSystem.Controls.TouchControl touch in Touchscreen.current.touches)
                {
                    if (touch.press.isPressed)
                        count++;
                }
            }
            return count;
        }

        public bool IsDoubleTouch()
        {
            return CountActiveTouch() >= 2;
        }

        public bool IsLeftClick()
        {
            Pointer input = GetInput();
            if (input != null)
                return input.press.wasPressedThisFrame;
            return false;
        }

        public bool IsRightClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.wasPressedThisFrame;
            return false;
        }

        public bool IsLeftHold()
        {
            Pointer input = GetInput();
            if (input != null)
                return input.press.isPressed;
            return false;
        }

        public bool IsRightHold()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.isPressed;
            return false;
        }

        public bool IsLeftRelease()
        {
            Pointer input = GetInput();
            if(input != null)
                return input.press.wasReleasedThisFrame;
            return false;
        }

        public bool IsRightRelease()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.wasReleasedThisFrame;
            return false;
        }

        public Vector2 GetMousePosition()
        {
            Pointer input = GetInput();
            if (input != null)
                return input.position.ReadValue();
            return Vector2.zero;
        }

        public Vector2 GetMouseDelta()
        {
            Pointer input = GetInput();
            if (input != null)
                return input.delta.ReadValue() * 0.1f; //Multiply by 0.1f to be more consistant with previous input system and gamepads
            return Vector2.zero;
        }

        public float GetMouseScroll()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
                return mouse.scroll.ReadValue().y;
            return 0f;
        }

        public Pointer GetInput()
        {
            if (IsTouchscreen())
                return Touchscreen.current;
            else
                return Mouse.current;
        }

        public bool IsTouchscreen()
        {
            return SystemInfo.deviceType == DeviceType.Handheld;
        }

        //Clamped to screen mouse pos
        private Vector2 GetClampMousePos()
        {
            Vector2 mousePos = GetMousePosition();
            mousePos.x = Mathf.Clamp(mousePos.x, 0f, Screen.width);
            mousePos.y = Mathf.Clamp(mousePos.y, 0f, Screen.height);
            return mousePos;
        }

        //Get ray from camera to mouse
        public Ray GetMouseCameraRay()
        {
            return TheCamera.GetCamera().ScreenPointToRay(GetCursorPosition());
        }

        //Return mouse position, unless its invisible, return center of screen (usually freelook mode)
        public Vector2 GetCursorPosition()
        {
            Vector2 pos = GetClampMousePos();
            if (!Cursor.visible)
                pos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            return pos;
        }

        //Get world position of the mouse
        public Vector3 GetMouseWorldPosition()
        {
            Vector2 mouse = GetMousePosition();
            Vector3 wmouse = new Vector3(mouse.x, mouse.y, 10f);
            return TheCamera.GetCamera().ScreenToWorldPoint(wmouse);
        }

        //Check if mouse is on top of any UI element
        public bool IsMouseOverUI()
        {
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = GetMousePosition();
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }
		
		//Mouse is inside camera bounds
        public bool IsMouseInsideCamera()
        {
            return TheCamera.Get().IsInside(GetMousePosition());
        }

        //Mouse is inside camera bounds and not over UI
        public bool IsInGameplay()
        {
            return !IsMouseOverUI() && IsMouseInsideCamera();
        }

        public static PlayerControlsMouse Get()
        {
            return _instance;
        }
    }

}
