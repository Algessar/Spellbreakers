using System;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;



namespace Player
{
    public class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private CinemachineCamera _freeLookCamera;
        private CinemachineOrbitalFollow _orbitalFollow;
        /// <summary>The transform whose forward/right define movement space, or null if unassigned.</summary>
        public Transform CameraTransform => _camera != null ? _camera.transform : null;

        /// <summary>Latest raw look input, in case another system wants to read it.</summary>
        public Vector2 cameraDirection;

        private void Awake()
        {
            if (!_camera) _camera = Camera.main;

            //NOTE: did an experiment that didn't work. Keeping this here if I do want to reference the CineCam
            // at some other point.
            
            // if (!_freeLookCamera)
            //     _freeLookCamera = gameObject.GetComponent<CinemachineCamera>();
            //
            // var Orbit = _freeLookCamera.gameObject.GetComponent<CinemachineOrbitalFollow>();
            // Orbit.HorizontalAxis.Recentering.Enabled = true;
        }

        public void OnLook(InputAction.CallbackContext ctx)
        {
            cameraDirection = ctx.ReadValue<Vector2>();
        }

        public Vector3 GetCameraRelativeMoveDirection(Vector2 moveInput)
        {
            if (!_camera)
            {
                Debug.LogWarning("PlayerCamera: No camera assigned. Falling back to world-space direction.");
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
