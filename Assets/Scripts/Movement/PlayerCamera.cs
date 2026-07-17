using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    public Vector3 cameraDirection; 
    
    public void OnLook(InputAction.CallbackContext ctx)
    {
        cameraDirection = ctx.ReadValue<Vector2>();
    }
}
