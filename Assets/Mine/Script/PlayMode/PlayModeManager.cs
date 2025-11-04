using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayModeManager : MonoBehaviour
{
    [Header("References")]
    public GameObject mainMenuUI;
    public GameObject blackScreenPanel;     
    public ArmSwingLocomotion armSwingScript;
    public Transform player;
    public Vector3 resetPosition = new Vector3(19f, 0f, -66f);
    public GameObject NPCs;
    public GameObject guidingArrows;

    [Header("Tutorial")]
    public TutorialDirector tutorialDirector;

    [Header("Roaming Guide (First-Time Arrow Tutorial)")]
    public RoamingGuideManager roamingGuide; // Reference to the one-time arrow guide manager

    private bool isRoamingMode = false;

    void Awake()
    {
        var cg = blackScreenPanel.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 0f;
    }

    void Update()
    {
        if (isRoamingMode && OVRInput.GetDown(OVRInput.Button.Two))
        {
            RequestExitRoaming();
        }
    }

    public void EnterRoamingMode()
    {
        StartCoroutine(BeginTutorialFlow());
    }

    private IEnumerator BeginTutorialFlow()
    {
        if (mainMenuUI) mainMenuUI.SetActive(false);
        if (armSwingScript) armSwingScript.enableMovement = false;

        bool finished = false;
        System.Action onDone = () => finished = true;

        tutorialDirector.gameObject.SetActive(true);
        tutorialDirector.Begin(onDone);

        while (!finished) yield return null;


        armSwingScript.enableMovement = true;
        isRoamingMode = true;
        NPCs.SetActive(true);
        guidingArrows.SetActive(true);
        roamingGuide.StartGuideEveryTime();
    }


    private IEnumerator ExitRoamingMode()
    {
        if (blackScreenPanel) blackScreenPanel.SetActive(true);
        yield return FadeBlack(true, 0.25f);

        player.position = resetPosition;
        if (armSwingScript) armSwingScript.enableMovement = false;
        isRoamingMode = false;

        if (mainMenuUI) mainMenuUI.SetActive(true);
        yield return FadeBlack(false, 0.25f);
    }

    public void RequestExitRoaming()
    {
        NPCs.SetActive(false);
        StartCoroutine(ExitRoamingMode());
    }

    private IEnumerator FadeBlack(bool toBlack, float duration)
    {
        if (!blackScreenPanel) yield break;
        var cg = blackScreenPanel.GetComponent<CanvasGroup>();
        if (!cg)
        {
            blackScreenPanel.SetActive(toBlack);
            yield break;
        }
        blackScreenPanel.SetActive(true);
        float start = cg.alpha;
        float target = toBlack ? 1f : 0f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            cg.alpha = Mathf.Lerp(start, target, t);
            yield return null;
        }
        cg.alpha = target;
        if (!toBlack) blackScreenPanel.SetActive(false);
    }
}

