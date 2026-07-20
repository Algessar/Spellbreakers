using System.Collections.Generic;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using Cache = UnityEngine.Cache;

public class CombatHandler : MonoBehaviour
{

    [SerializeField] private Animator _animator;
    private float _crossFade = 0.2f;

    private int[] _guardHashes;
    private int _vomtagHash;
    private int _alberHash;
    private int _pflugHash;
    private int _ochsHash;
    private int _irongateHash;

    void Start()
    {
        _animator = GetComponent<Animator>();

        _vomtagHash = Animator.StringToHash("VomTag");
        _pflugHash = Animator.StringToHash("Pflug");
        _alberHash = Animator.StringToHash("Alber");
        _ochsHash = Animator.StringToHash("Ochs");
        _irongateHash = Animator.StringToHash("IronGate");


        _guardHashes = new[]
        {
            _vomtagHash,
            _alberHash,
            _pflugHash,
            _ochsHash,
            _irongateHash,
        };


        // AddingNumbersToAnArray();

    }

    void Update()
    {
        
    }
    
    public void OnVomTag(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;

        _animator.CrossFade(_vomtagHash, _crossFade, 1);
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
        // if (!ctx.started) return;
        _animator.CrossFade(_ochsHash,  _crossFade, 1);
    }
    
    public void OnIronGate(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        _animator.CrossFade(_irongateHash,  _crossFade, 1);

    }
}


// private void AddingNumbersToAnArray()
// {
//     int[] addNumbersToArray = new int [10];
//     
//     // The array has been created and currently contains ten 0s.
//     // Let's replace those values with our own.
//     
//     // Arrays and Lists are data structures with indices. At each index, we have a value. Or, we want to have one :D
//     // For example index 2 can have the *value* 5.
//     // For example like this:
//     addNumbersToArray[2] = 5;
//     
//     // But if we want to add a lot of numbers? It would be very annoying to do this: 
//     
//     addNumbersToArray[1] = 2;
//     addNumbersToArray[2] = 4;
//     addNumbersToArray[3] = 6;
//     
//     Debug.Log($"The value at index 3 is {addNumbersToArray[3]}");
//     
//     // And it would take a long time if we had thousands of numbers! So how do we solve that?
//     // We can use a for loop!
//     
//     // for loops have a convention of using a single letter as the value, usually i.
//     // It's not a requirement!
//     
//     // For this, we use i to represent the *index* of our array, and iterate over it in a for loop:
//
//     for (int i = 0; i < addNumbersToArray.Length; i++)
//     {
//         //But how do we actually add the numbers? 
//         
//         // We have the current index in 'i'. Can we use that to assign
//         // a number to each index in the array? Try to make it increase for each loop!
//         
//         Debug.Log($"Currently checking index {i}");
//     }
//     
//     // There is a lot happening here. Take some time to look at all the strange things going on!
// }
//
