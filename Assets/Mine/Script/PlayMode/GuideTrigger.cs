using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GuideTrigger : MonoBehaviour
{
    public RoamingGuideManager manager;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager) manager.NotifyStepTriggered(GetComponent<Collider>(), other);
    }
}
