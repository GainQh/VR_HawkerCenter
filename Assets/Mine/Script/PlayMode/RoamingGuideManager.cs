using System;
using System.Collections.Generic;
using UnityEngine;

public class RoamingGuideManager : MonoBehaviour
{
    [Serializable]
    public class GuideStep
    {
        [Tooltip("Trigger collider for this step (must be isTrigger = true).")]
        public Collider trigger;

        [Tooltip("Arrow or visual indicator shown for this step (can be a 3D model or UI element).")]
        public GameObject arrow;

        [HideInInspector] public bool visited = false;
    }

    [Header("Guide Steps (in order)")]
    public List<GuideStep> steps = new List<GuideStep>();

    [Header("Player Detection")]
    [Tooltip("Tag used to identify the player when entering triggers (e.g., 'Player').")]
    public string playerTag = "Player";

    private int _currentIndex = -1;
    private bool _isRunning = false;

    /// <summary>
    /// Called when entering Roaming Mode.
    /// Always restarts the guide (no save state).
    /// </summary>
    public void StartGuideEveryTime()
    {
        // Reset all steps first
        for (int i = 0; i < steps.Count; i++)
        {
            SetArrowActive(i, false);              // Hide all arrows
            steps[i].visited = false;              // Reset visit flags
            if (steps[i].trigger) steps[i].trigger.enabled = false; // Disable all triggers initially
        }

        _currentIndex = 0;
        _isRunning = true;
        SetArrowActive(_currentIndex, true);
        if (steps[_currentIndex].trigger)
        {
            steps[_currentIndex].trigger.enabled = true; // enable trigger #1
        }

        // Start from the very first step only
        AdvanceToNextStep();
    }

    /// <summary>
    /// Called by GuideTrigger when player enters a trigger collider.
    /// </summary>
    public void NotifyStepTriggered(Collider trigger, Collider byWho)
    {
        if (!_isRunning) return;
        if (byWho == null || (byWho.attachedRigidbody == null && !MatchesPlayerTag(byWho))) return;

        int idx = IndexOfTrigger(trigger);
        if (idx < 0) return;

        if (idx == _currentIndex && !steps[idx].visited)
        {
            steps[idx].visited = true;
            SetArrowActive(idx, false);
            if (steps[idx].trigger) steps[idx].trigger.enabled = false;
            AdvanceToNextStep();
        }
    }

    /// <summary>
    /// Advances to the next step, showing only the next arrow.
    /// </summary>
    private void AdvanceToNextStep()
    {
        int next = _currentIndex + 1;

        // If no more steps → finish
        if (next >= steps.Count)
        {
            Finish();
            return;
        }

        _currentIndex = next;

        // Hide all arrows and disable all triggers
        for (int i = 0; i < steps.Count; i++)
        {
            SetArrowActive(i, false);
            if (steps[i].trigger) steps[i].trigger.enabled = false;
        }

        // Enable only the current arrow and trigger
        SetArrowActive(_currentIndex, true);
        if (steps[_currentIndex].trigger) steps[_currentIndex].trigger.enabled = true;
    }

    private void Finish()
    {
        _isRunning = false;
        DisableAll();
    }

    private void DisableAll()
    {
        for (int i = 0; i < steps.Count; i++)
        {
            SetArrowActive(i, false);
            if (steps[i].trigger) steps[i].trigger.enabled = false;
        }
    }

    private int IndexOfTrigger(Collider col)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].trigger == col) return i;
        }
        return -1;
    }

    private void SetArrowActive(int index, bool active)
    {
        if (index < 0 || index >= steps.Count) return;
        if (steps[index].arrow) steps[index].arrow.SetActive(active);
    }

    private bool MatchesPlayerTag(Collider who)
    {
        if (string.IsNullOrEmpty(playerTag)) return true;
        if (who.CompareTag(playerTag)) return true;
        if (who.attachedRigidbody && who.attachedRigidbody.CompareTag(playerTag)) return true;
        if (who.transform.root && who.transform.root.CompareTag(playerTag)) return true;
        return false;
    }
}
