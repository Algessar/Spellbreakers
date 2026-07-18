using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class PlayerCamera : MonoBehaviour
    {
        private Input _input;
        [SerializeField] private Camera _camera;
        public Vector3 cameraDirection;

        public void OnLook(InputAction.CallbackContext ctx)
        {
            cameraDirection = ctx.ReadValue<Vector2>();
        }

        public Vector3 GetCameraRelativeMoveDirection()
        {
            if (!_input) return Vector3.zero;
            Vector2 input = Vector2.ClampMagnitude(cameraDirection, 1f);
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            if (!_camera) return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }
    }
}