using UnityEngine;

public class MarkerChildSwitcher : MonoBehaviour
{
    public GameObject markerVirtualObject;
    public void ShowOnlyChildAtIndex(int index)
    {
        if (markerVirtualObject == null) return;

        int count = markerVirtualObject.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform child = markerVirtualObject.transform.GetChild(i);
            child.gameObject.SetActive(i == index);
        }
    }
}

