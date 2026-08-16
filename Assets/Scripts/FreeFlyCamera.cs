using UnityEngine;
using UnityEngine.InputSystem;

namespace ReplaySystem
{
    /// <summary>
    /// Spectator-style fly camera, fully decoupled from any tracked/replayed
    /// object. Hold the right mouse button to look around; WASD/QE to move;
    /// Shift to boost; scroll to adjust move speed. Requires the Input System package.
    /// </summary>
    public class FreeFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 8f;
        public float boostMultiplier = 3f;
        public float lookSensitivity = 0.15f;
        public float minMoveSpeed = 0.5f;
        public float maxMoveSpeed = 100f;

        private float _yaw;
        private float _pitch;

        private void Start()
        {
            var e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            if (mouse.rightButton.isPressed)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * lookSensitivity;
                _pitch -= delta.y * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                moveSpeed = Mathf.Clamp(moveSpeed + scroll * 0.02f, minMoveSpeed, maxMoveSpeed);

            Vector3 dir = Vector3.zero;
            if (keyboard.wKey.isPressed) dir += transform.forward;
            if (keyboard.sKey.isPressed) dir -= transform.forward;
            if (keyboard.dKey.isPressed) dir += transform.right;
            if (keyboard.aKey.isPressed) dir -= transform.right;
            if (keyboard.eKey.isPressed) dir += Vector3.up;
            if (keyboard.qKey.isPressed) dir -= Vector3.up;

            float speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? boostMultiplier : 1f);
            transform.position += dir.normalized * speed * Time.deltaTime;
        }
    }
}
