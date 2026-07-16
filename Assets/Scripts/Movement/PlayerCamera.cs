using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    public Vector3 cameraDirection;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public void OnLook(InputAction.CallbackContext ctx)
    {
        cameraDirection = ctx.ReadValue<Vector2>();
    }
}
