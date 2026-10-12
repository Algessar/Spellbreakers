using System;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance;
    
    
    [HideInInspector] public Rigidbody Rigidbody { get; private set; }
    [HideInInspector] public CapsuleCollider Collider { get; private set; }


    [Header("Animation")]
    
    [HideInInspector] public Animator animator{ get; private set; }
    
    public AnimationClip idle;
    [HideInInspector] public string idleName;
    public AnimationClip walk;
    [HideInInspector] public string walkName;
    public AnimationClip run;
    [HideInInspector] public string runName;
    
    public AnimationClip walkInGuard;
    [HideInInspector] public string walkInGuardName;
    public AnimationClip walkBackwardInGuard;
    [HideInInspector] public string walkBackwardName;
    public AnimationClip strafeRight;
    [HideInInspector] public string strafeRightName;
    public AnimationClip strafeLeft;
    [HideInInspector]  public string strafeLeftName;
    
    private static readonly int InGuardWalkForwardHash = Animator.StringToHash("InGuard-WalkForward");      // Fallback
    private static readonly int InGuardStrafeLeftHash = Animator.StringToHash("InGuard-Strafe-Left");       // Fallback
    private static readonly int InGuardStrafeRightHash = Animator.StringToHash("InGuard-Strafe-Right");     // Fallback
    private static readonly int InGuardWalkBackwardHash = Animator.StringToHash("InGuard-WalkBackward");    // Fallback

    //Add clips to Mechanim in OnValidate?
    private void OnValidate()
    {
        
    }


    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        animator = GetComponent<Animator>();
        
        Rigidbody = GetComponent<Rigidbody>();
        Collider = GetComponent<CapsuleCollider>();
        
        Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX;
        Rigidbody.isKinematic = true;
        Rigidbody.useGravity = false;

        Collider.center = new Vector3(0, 0.89f, -0.1f);
        Collider.height = 1.8f;
        Collider.radius = 0.3f;
    }

    void SetClipNames()
    {
        idleName = idle.name;
        walkName = walk.name;
        runName = run.name;
        
        
        
        
    }


}
