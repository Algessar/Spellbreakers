using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class Teleporter : MonoBehaviour 
{
    // Input or location for activating the portal
    // Let's do location for now. I'd very much like visuals for this but I guess we can do material colour

    private int _currentId = -1;
    
    // I can use the direction of the teleport compass to set ID
    // Which then is the scene id in the build

    [SerializeField] private Vector2 _compassDirection;
    [SerializeField] private GameObject _compass;

    // These can be separate empties with colliders on them. Then the index itself is the ID for the Scene
    [SerializeField] private Collider[] _colliders;

    [SerializeField] private EnterRuneTrigger[] _runes;
    
    

    private void Start()
    {


        
        // Debug.Log($"World position: {location}");
        Debug.Log("Why has it been destroyed?");
        //NOTE: The array will be populated manually!

        for (int i = 0; i < _runes.Length; i++)
        {
            _runes[i]._triggerAction += ChangeScene;
            Debug.Log(_runes[i].ToString());
        }
    }

    private void ChangeScene()
    {
        ActivateCompass();
        for (int i = 0; i < _runes.Length; i++)
        {
            if (_runes[i].wasTriggered)
            {
                _currentId = _runes[i].ID;
            }
        }
        Debug.Log($"Triggered ChangeScene from rune ID {_currentId}");
        SceneManager.LoadScene(_currentId);
        Debug.Log($"Why does LoadScene not trigger?");

    }

    /// <summary>
    /// Moves the Compass up to show it is active
    /// </summary>
    private void ActivateCompass()
    {
        var location = _compass.transform.localPosition;

        location = new Vector3(0, -1, 0);
        transform.localPosition = location;
    }

}