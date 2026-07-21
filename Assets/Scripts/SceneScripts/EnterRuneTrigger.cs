using System;
using UnityEngine;

public class EnterRuneTrigger : MonoBehaviour
{

    public Action _triggerAction;

    [SerializeField] public int ID;

    public bool wasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other)
        {
            Debug.Log($"OnTriggerEnter of Rune with ID :: {ID}");
            wasTriggered = true;
            _triggerAction?.Invoke();
        }
    }

    public override string ToString()
    {
        return $"EnterRuneTrigger ID :: {ID}";
    }
    
    
}
