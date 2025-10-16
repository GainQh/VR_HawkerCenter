using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayModeManager : MonoBehaviour
{
    [Header("References")]
    public GameObject mainMenuUI;
    public GameObject blackScreenPanel;
    public GameObject tipText;
    public ArmSwingLocomotion armSwingScript;
    public Transform player;
    public Vector3 resetPosition = new Vector3(19f, 0f, -66f);
    public GameObject NPCs;
    private bool isRoamingMode = false;
    private bool waitingForAKey = false;

    void Update()
    {
        if (waitingForAKey && OVRInput.GetDown(OVRInput.Button.One))  
        {
            waitingForAKey = false;
            StartCoroutine(FadeFromBlackAndStartRoaming());
        }

        if (isRoamingMode && OVRInput.GetDown(OVRInput.Button.Two))  
        {
            NPCs.SetActive(false);
            StartCoroutine(ExitRoamingMode());
        }
    }

    public void EnterRoamingMode()
    {
        StartCoroutine(StartRoamingMode());
    }

    private IEnumerator StartRoamingMode()
    {
        mainMenuUI.SetActive(false);

        blackScreenPanel.SetActive(true);
        tipText.SetActive(true);

        waitingForAKey = true;
        yield return null;
    }

    private IEnumerator FadeFromBlackAndStartRoaming()
    {
        blackScreenPanel.SetActive(false);
        tipText.SetActive(false);

        armSwingScript.enableMovement = true;
        isRoamingMode = true;

        yield return null;
    }

    private IEnumerator ExitRoamingMode()
    {
        blackScreenPanel.SetActive(true);
        tipText.SetActive(false);

        yield return new WaitForSeconds(1f);

        player.position = resetPosition;

        armSwingScript.enableMovement = false;
        isRoamingMode = false;

        mainMenuUI.SetActive(true);
        blackScreenPanel.SetActive(false);
    }
}
