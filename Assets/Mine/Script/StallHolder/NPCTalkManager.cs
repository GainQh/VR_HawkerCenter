using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class NPCTalkManager : MonoBehaviour
{
    [Header("Player/XR")]
    public Transform xrOrigin;        // XR Origin root (the rig)
    public Transform playerCamera;    // HMD/Camera (player head)

    [Header("Snap Targets")]
    public Transform headSnapPoint;   // Desired head position after snap (set in Inspector)
    public Transform lookAtTarget;    // NPC head or chest to face (set in Inspector)

    [Header("UI")]
    public GameObject promptUI;       // "Press A to talk"
    public GameObject dialogUI;       // Dialog root (enabled when dialog starts)
    public CanvasGroup fadeGroup;     // Fullscreen black overlay with CanvasGroup (alpha 0 on start)
    public GameObject talkIcon;

    [Header("Timings")]
    public float fadeDuration = 0.2f; // Fade-out and fade-in duration

    [Header("Movement Control")]
    public ArmSwingLocomotion locomotion; // Reference to your locomotion script

    [Header("Dialog Data")]
    [TextArea(2, 4)]
    public System.Collections.Generic.List<string> dialogTexts = new System.Collections.Generic.List<string>();
    public System.Collections.Generic.List<AudioClip> dialogVoices = new System.Collections.Generic.List<AudioClip>();

    private bool _dialogActive = false;
    private int _dialogIndex = -1;
    private bool _inRange = false;
    private bool _busy = false;

    // Saved pose to restore after dialog
    private Vector3 _savedOriginPos;
    private Quaternion _savedOriginRot;

    void Awake()
    {
        if (promptUI != null) promptUI.SetActive(false);
        if (dialogUI != null) dialogUI.SetActive(false);
        if (fadeGroup != null) fadeGroup.alpha = 0f;

        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void Update()
    {
        if (_busy || !_inRange || _dialogActive) return;

        // OVR "A" button = Button.One
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            StartCoroutine(SnapAndShowDialog());
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            _inRange = true;
            if (promptUI != null && promptUI != gameObject) promptUI.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            _inRange = false;
            if (promptUI != null && promptUI != gameObject) promptUI.SetActive(false);
        }
    }

    private bool IsPlayer(Collider other)
    {
        if (playerCamera == null) return false;
        var root = playerCamera.root;
        return other.transform == root || other.transform.IsChildOf(root);
    }

    private IEnumerator SnapAndShowDialog()
    {
        _busy = true;
        _dialogActive = true;
        talkIcon.SetActive(false);
        promptUI.SetActive(false);
        // Save original rig pose
        if (xrOrigin != null)
        {
            _savedOriginPos = xrOrigin.position;
            _savedOriginRot = xrOrigin.rotation;
        }

        // Disable movement while in dialog
        if (locomotion != null) locomotion.enableMovement = false;

        // Hide prompt immediately
        if (promptUI != null && promptUI != gameObject) promptUI.SetActive(false);

        // Fade out
        yield return FadeTo(1f, fadeDuration);

        // Snap player pose
        SnapPlayerPose();

        // Reset dialog index and show dialog root
        _dialogIndex = -1;
        if (dialogUI != null)
        {
            dialogUI.SetActive(true);

            // explicitly start the first line once
            var ui = dialogUI.GetComponent<DialogUIManager>();
            if (ui != null) ui.StartDialog();
        }


        // Fade in
        yield return FadeTo(0f, fadeDuration);

        _busy = false;

        // Optionally auto-start first line: leave control to DialogUIManager.OnEnable()
    }

    private void SnapPlayerPose()
    {
        if (xrOrigin == null || playerCamera == null || headSnapPoint == null) return;

        // Compute desired yaw
        float desiredYaw = xrOrigin.rotation.eulerAngles.y;
        if (lookAtTarget != null)
        {
            Vector3 headPos = playerCamera.position;
            Vector3 toTarget = lookAtTarget.position - headPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 1e-6f)
            {
                desiredYaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
            }
        }

        // Rotate rig by delta yaw so that the camera faces desiredYaw
        float currentCameraYaw = playerCamera.rotation.eulerAngles.y;
        float deltaYaw = Mathf.DeltaAngle(currentCameraYaw, desiredYaw);
        xrOrigin.rotation = Quaternion.Euler(0f, xrOrigin.rotation.eulerAngles.y + deltaYaw, 0f);

        // Translate rig so that the camera lands on headSnapPoint
        Vector3 camPosAfterRotate = playerCamera.position;
        Vector3 offset = headSnapPoint.position - camPosAfterRotate;
        xrOrigin.position += offset;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeGroup == null || duration <= 0f) yield break;

        float start = fadeGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            fadeGroup.alpha = Mathf.Lerp(start, targetAlpha, t);
            yield return null;
        }
        fadeGroup.alpha = targetAlpha;
    }

    // Public API for DialogUIManager to advance lines
    public bool NextLine(out string text, out AudioClip voice)
    {
        _dialogIndex++;
        if (_dialogIndex < 0 || _dialogIndex >= GetDialogCount())
        {
            text = null;
            voice = null;
            return false;
        }

        text = dialogTexts[_dialogIndex];
        voice = GetVoiceAt(_dialogIndex);
        return true;
    }

    public bool GetCurrentLine(out string text, out AudioClip voice)
    {
        if (_dialogIndex < 0 || _dialogIndex >= GetDialogCount())
        {
            text = null;
            voice = null;
            return false;
        }
        text = dialogTexts[_dialogIndex];
        voice = GetVoiceAt(_dialogIndex);
        return true;
    }

    public void ResetDialog()
    {
        _dialogIndex = -1;
    }

    public int GetDialogCount()
    {
        return dialogTexts != null ? dialogTexts.Count : 0;
    }

    private AudioClip GetVoiceAt(int index)
    {
        if (dialogVoices == null) return null;
        if (index < 0 || index >= dialogVoices.Count) return null;
        return dialogVoices[index];
    }

    // Called by DialogUIManager when there is no next line
    public void RequestEndDialog()
    {
        if (!_busy) StartCoroutine(EndDialogRoutine());
    }

    private IEnumerator EndDialogRoutine()
    {
        _busy = true;

        // Fade out
        yield return FadeTo(1f, fadeDuration);

        talkIcon.SetActive(true);
        // Hide dialog
        if (dialogUI != null) dialogUI.SetActive(false);

        // Restore player pose
        if (xrOrigin != null)
        {
            xrOrigin.position = _savedOriginPos;
            xrOrigin.rotation = _savedOriginRot;
        }

        // Re-enable movement
        if (locomotion != null) locomotion.enableMovement = true;

        _dialogActive = false;

        // Show prompt again if still in range
        if (_inRange && promptUI != null && promptUI != gameObject)
            promptUI.SetActive(true);

        // Fade in
        yield return FadeTo(0f, fadeDuration);

        _busy = false;
    }


}
