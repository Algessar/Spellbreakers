using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class PlayerCamera : MonoBehaviour
    {
        private InputHandler _input;
        [SerializeField] private Camera _camera;
        public Vector3 cameraDirection;

        // private void OnValidate()
        // {
        //     _camera = Camera.main;
        // }

        public void OnLook(InputAction.CallbackContext ctx)
        {
            cameraDirection = ctx.ReadValue<Vector2>();
        }

        public Vector3 GetCameraRelativeMoveDirection(Vector2 moveInput)
        {
            if (!_camera)
            {
                Debug.LogWarning("PlayerCamera: No camera assigned. Falling back to world‑space direction.");
                return new Vector3(moveInput.x, 0f, moveInput.y).normalized;
            }

            // Flatten camera forward and right onto the horizontal plane
            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            Vector3 right   = Vector3.ProjectOnPlane(_camera.transform.right,   Vector3.up).normalized;

            // Combine based on the input axes: y = forward/back, x = left/right
            Vector3 worldDirection = forward * moveInput.y + right * moveInput.x;

            // Clamp magnitude and normalise to avoid faster diagonal movement
            return Vector3.ClampMagnitude(worldDirection, 1f).normalized;
        }
        
        
    }
}