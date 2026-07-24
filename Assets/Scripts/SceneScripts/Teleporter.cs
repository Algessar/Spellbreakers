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
    private Collider _collider;


    [Header("Platform mesh")] 
    public GameObject _platformMesh;
    private Material _material;
    
    // I can use the direction of the teleport compass to set ID
    // Which then is the scene id in the build

    // These can be separate empties with colliders on them. Then the index itself is the ID for the Scene
    // [SerializeField] private Collider[] _colliders;

    [SerializeField] private EnterRuneTrigger[] _runes;
    private Timer _timer;

    [SerializeField] private float _duration = 10f;
    [SerializeField] private float _speed = 5f;
    
    [Header("Compass")]
    [SerializeField] private Vector2 _compassDirection;
    [SerializeField] private GameObject _compass;
    
    private Vector3 startPos;
    [SerializeField] private Vector3 targetPos;
    private bool isMoving = false;
    
    
    private void Start()
    {
        _timer = new();
        Renderer rend = _platformMesh.GetComponent<Renderer>();
        if (rend != null)
            _material = rend.material;   // this is the material instance
        else
            Debug.LogError("No Renderer found on " + _platformMesh.name);
        
        _material.EnableKeyword("_EMISSION");
        
        
        // ActivateCompass();

        //NOTE: The array will be populated manually!

        for(int i = 0; i < _runes.Length; i++)
        {
            _runes[i]._triggerAction += ChangeScene;
            _runes[i].gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        SetEmission(Color.red, 5);
        UpdateCompassMovement();
        
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other)
        {
            ActivateCompass();  
            for(int i = 0; i < _runes.Length; i++)
            {
                if (i > 1) return; //NOTE: TEMP
                    _runes[i].gameObject.SetActive(true);
            }
        }
    }

    private void ChangeScene()
    {
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
        
        if (isMoving) return; // already moving, ignore
        
        startPos = _compass.transform.localPosition;
        targetPos = startPos + Vector3.up;   // 1 meter up (or use transform.up for local)
        _timer.StartCountdown(_duration);
        isMoving = true;
    }
    
    private bool isGlowing = false;
    float currentIntensity = 0;
    private float maxIntensity;
    private float duration;
    public void SetEmission(Color color, float intensity)
    {
        
        // Increase intensity over time
        if (!isGlowing) return;

        currentIntensity += (maxIntensity / duration) * Time.deltaTime;
        currentIntensity = Mathf.Min(currentIntensity, maxIntensity); // clamp
            _material.SetColor("_EmissionColor", color * intensity);
            
        if (currentIntensity >= maxIntensity)
                isGlowing = false; // done
    }

    private void UpdateCompassMovement()
    {
        if (!_timer.IsFinished) return;
        if (!isMoving) return;

        float step = _speed * Time.deltaTime;
        _compass.transform.localPosition = Vector3.MoveTowards(_compass.transform.localPosition, targetPos * -1, step);

        if (_compass.transform.position == targetPos)
        {
            isMoving = false;
        }
    }

}