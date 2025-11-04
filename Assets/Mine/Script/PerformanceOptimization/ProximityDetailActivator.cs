using UnityEngine;
using System.Collections.Generic;

public class ProximityDetailActivator : MonoBehaviour
{
    public enum DetectionMode { Tag, LayerMask }
    public enum ToggleMode { SetActive, RenderersOnly }

    [Header("Target Details")]
    [Tooltip("List of parent objects containing local details to show/hide.")]
    public List<GameObject> detailParents = new List<GameObject>();

    [Tooltip("If true, hides all details when the scene starts.")]
    public bool startHidden = true;

    [Header("Player Detection")]
    [Tooltip("Choose whether to detect the player by Tag or LayerMask.")]
    public DetectionMode detectionMode = DetectionMode.Tag;

    [Tooltip("Tag of the player object (e.g., XR Origin).")]
    public string playerTag = "player";

    [Tooltip("Layers considered as player when using LayerMask mode.")]
    public LayerMask playerLayers = ~0;

    [Header("Display Toggle Mode")]
    [Tooltip("SetActive: enable/disable GameObjects.\nRenderersOnly: toggle visibility of renderers/lights/particles only.")]
    public ToggleMode toggleMode = ToggleMode.SetActive;

    [Header("Advanced Settings")]
    [Tooltip("Ignore incoming colliders that are marked as Trigger (e.g., head sensors).")]
    public bool ignoreIncomingTriggers = true;

    [Tooltip("Enable overlap counting to prevent flicker if the player has multiple colliders.")]
    public bool useOverlapCounting = true;

    private int overlapCount = 0;

    void Reset()
    {
        // Ensure this collider is set as a trigger
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Awake()
    {
        if (startHidden) SetDetailsVisible(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (ignoreIncomingTriggers && other.isTrigger) return;
        if (!IsPlayer(other)) return;

        if (useOverlapCounting)
        {
            overlapCount++;
            if (overlapCount == 1) SetDetailsVisible(true);
        }
        else
        {
            SetDetailsVisible(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (ignoreIncomingTriggers && other.isTrigger) return;
        if (!IsPlayer(other)) return;

        if (useOverlapCounting)
        {
            overlapCount = Mathf.Max(0, overlapCount - 1);
            if (overlapCount == 0) SetDetailsVisible(false);
        }
        else
        {
            SetDetailsVisible(false);
        }
    }

    private bool IsPlayer(Collider other)
    {
        // Supports both Tag and Layer-based detection
        if (detectionMode == DetectionMode.Tag)
        {
            if (other.CompareTag(playerTag)) return true;
            if (other.attachedRigidbody && other.attachedRigidbody.CompareTag(playerTag)) return true;
            if (other.transform.root && other.transform.root.CompareTag(playerTag)) return true;
            return false;
        }
        else
        {
            if (IsInLayerMask(other.gameObject.layer, playerLayers)) return true;
            if (other.attachedRigidbody &&
                IsInLayerMask(other.attachedRigidbody.gameObject.layer, playerLayers))
                return true;
            if (other.transform.root &&
                IsInLayerMask(other.transform.root.gameObject.layer, playerLayers)) return true;
            return false;
        }
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return ((1 << layer) & mask.value) != 0;
    }

    private void SetDetailsVisible(bool visible)
    {
        if (detailParents == null || detailParents.Count == 0)
        {
            Debug.LogWarning($"[{nameof(ProximityDetailActivator)}] No detail parents assigned on {name}");
            return;
        }

        foreach (var parent in detailParents)
        {
            if (!parent) continue;

            if (toggleMode == ToggleMode.SetActive)
            {
                if (IsSelfOrAncestor(parent.transform, transform))
                {
                    Debug.LogWarning($"[{nameof(ProximityDetailActivator)}] DetailParent includes this trigger; it may disable itself. Keep them separate.");
                }
                parent.SetActive(visible);
            }
            else
            {
                // RenderersOnly mode
                var renderers = parent.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers) r.enabled = visible;

                var lights = parent.GetComponentsInChildren<Light>(true);
                foreach (var l in lights) l.enabled = visible;

                var particles = parent.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particles)
                {
                    ps.gameObject.SetActive(visible);
                }
            }
        }
    }

    private bool IsSelfOrAncestor(Transform candidate, Transform self)
    {
        var t = self;
        while (t != null)
        {
            if (t == candidate) return true;
            t = t.parent;
        }
        return false;
    }
}
