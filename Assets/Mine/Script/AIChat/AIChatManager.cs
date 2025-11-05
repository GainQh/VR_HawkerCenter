using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Text.RegularExpressions;

public class AIChatManager : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI userUtteranceText;   // From "User Utterance"
    public TextMeshProUGUI aiAnswerText;        // Plain text answer display
    public TextMeshProUGUI debugText;           // Optional debug
    public AIAnimatorManager animatorManager;

    [Header("Server Settings")]
    public string serverIP = "10.249.157.127";
    public int port = 5006;

    [Header("JSON Parser (Sibling)")]
    public JsonAIManager jsonManager;           // ← Assign the JsonAIManager on the same GameObject

    public void UpdateAIReply(string json)
    {
        // 1) Try legacy wrapper { "reply": "..." }
        ReplyData reply = null;
        try { reply = JsonUtility.FromJson<ReplyData>(json); } catch { }

        string payload = reply != null ? reply.reply : json;

        // 2) If the payload looks like a JSON object, delegate to JsonAIManager
        if (!string.IsNullOrEmpty(payload) && LooksLikeJsonObject(payload))
        {
            if (jsonManager != null)
            {
                jsonManager.ProcessIncoming(payload); // parse + UI + menu switching handled there
                if (debugText) debugText.text = "Delegated to JsonAIManager";
            }
            else
            {
                Debug.LogWarning("[AIChatManager] JsonAIManager not assigned.");
                if (aiAnswerText) StartCoroutine(TypeTextEffect(payload));
            }
            return;
        }

        // 3) Otherwise treat as plain text
        if (aiAnswerText) StartCoroutine(TypeTextEffect(payload ?? ""));
        if (debugText) debugText.text = "Response shown as plain text";
    }

    public void OnSendClicked()
    {
        string prompt = userUtteranceText ? userUtteranceText.text.Trim() : "";
        if (!string.IsNullOrEmpty(prompt))
        {
            StartCoroutine(SendPrompt(prompt));
        }
        else
        {
            Debug.LogWarning("⚠️ Empty user input.");
            if (debugText) debugText.text = "Input is empty";
        }
    }

    IEnumerator SendPrompt(string prompt)
    {
        string url = $"https://{serverIP}:{port}/ask";
        if (debugText) debugText.text = $"POST: {url}";

        string json = JsonUtility.ToJson(new PromptData { prompt = prompt });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.certificateHandler = new AcceptAllCertificates();

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                UpdateAIReply(request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("❌ HTTP error: " + request.error);
                if (debugText) debugText.text = "Error " + request.error;
            }
        }
    }

    IEnumerator GetLatestReply()
    {
        string url = $"https://{serverIP}:{port}/latest";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new AcceptAllCertificates();
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                UpdateAIReply(request.downloadHandler.text);
                if (debugText) debugText.text = "Loaded initial reply";
            }
            else
            {
                if (debugText) debugText.text = "Load fail: " + request.error;
            }
        }
    }

    // ---------- helpers ----------
    private bool LooksLikeJsonObject(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var m = Regex.Match(s, @"\{[\s\S]*\}");
        return m.Success;
    }

    [System.Serializable] public class PromptData { public string prompt; }
    [System.Serializable] public class ReplyData { public string reply; }

    // Dev-only: accept self-signed
    private class AcceptAllCertificates : CertificateHandler { protected override bool ValidateCertificate(byte[] d) => true; }

    IEnumerator TypeTextEffect(string text)
    {
        if (animatorManager) animatorManager.PlayTalkingAnimation();
        if (aiAnswerText) aiAnswerText.text = "";
        foreach (char c in text)
        {
            if (aiAnswerText) aiAnswerText.text += c;
            yield return new WaitForSeconds(0.03f);
        }
        if (animatorManager) animatorManager.PlayIdle();
    }
}
