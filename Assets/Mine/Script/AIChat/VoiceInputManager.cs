using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Voice;

public class VoiceInputManager : MonoBehaviour
{
    public AppVoiceExperience voiceExperience;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (OVRInput.GetUp(OVRInput.Button.One))
        {
            voiceExperience.Activate();
        }
    }
}
