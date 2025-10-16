using UnityEngine;

public class TeleportPlayerToOrigin : MonoBehaviour
{
    public Transform player;                    
    public GameObject mainMenuUI;
    public Vector3 playerOriginPosition;
    public ArmSwingLocomotion armSwingScript;

    public void TeleportToOrigin()
    {
        player.position = playerOriginPosition;
        mainMenuUI.SetActive(true);
        armSwingScript.enableMovement = false;
    }
}
