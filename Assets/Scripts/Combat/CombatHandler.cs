using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class CombatHandler : MonoBehaviour
{

    [SerializeField] private Animator _animator;
    [SerializeField] private float _crossFade = 0.2f;

     
    
    private int[] _guardHashes;
    private int _vomtagHash;
    private int _alberHash;
    private int _pflugHash;
    private int _ochsHash;
    private int _irongateHash;
    

    [Header("Triggered transitions")]
    //Major transitions
    private int _langortHash;
    
    //
    

    void Start()
    {
        _animator = GetComponent<Animator>();

        _vomtagHash = Animator.StringToHash("VomTag");
        _pflugHash = Animator.StringToHash("Pflug");
        _alberHash = Animator.StringToHash("Alber");
        _ochsHash = Animator.StringToHash("Ochs");
        _irongateHash = Animator.StringToHash("IronGate");

        _langortHash = Animator.StringToHash("Langort");


        _guardHashes = new[]
        {
            _vomtagHash,
            _alberHash,
            _pflugHash,
            _ochsHash,
            _irongateHash,
        };
    }

    void Update()
    {
        var trans = _animator.IsInTransition(0);
        
        Debug.Log($"Is in transition: {trans}");
    }
    
    public void OnVomTag(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_vomtagHash, _crossFade, 1);
    }

    void Transition()
    {
        
    }
    
    public void OnAlber(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_alberHash,  _crossFade, 1);
    }
    
    public void OnPflug(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_pflugHash,  _crossFade, 1);
    }
    
    public void OnOchs(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_ochsHash,  _crossFade, 1);
    }
    
    public void OnIronGate(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_irongateHash,  _crossFade, 1);

    }
}