using UnityEngine;
using TMPro;
using System.Text.RegularExpressions;

public class JsonAIManager : MonoBehaviour
{
    [Header("Panel (Structured Fields)")]
    public GameObject infoPanel;
    public TextMeshProUGUI intentText;
    public TextMeshProUGUI dishText;
    public TextMeshProUGUI inMenuText;
    public TextMeshProUGUI indexedMatchText;
    public TextMeshProUGUI menuIndexText;
    public TextMeshProUGUI debugText;   // optional

    [Header("Menu Routing")]
    public MarkerChildSwitcher markerSwitcher;

    /// <summary>
    /// Entry point called by AIChatManager when a JSON-looking payload arrives.
    /// Accepts either raw AIResponse JSON or wrapped { "reply": { ... } }.
    /// </summary>
    public void ProcessIncoming(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            SetDebug("Empty JSON payload.");
            return;
        }

        string cleaned = PrepForParse(raw);

        // Try direct AIResponse
        AIResponse ar = TryParseAIResponse(cleaned);
        if (ar == null)
        {
            // Try wrapped { "reply": { ... } }
            var wrap = TryParseWrapper(cleaned);
            ar = wrap?.reply;
        }

        if (ar == null)
        {
            SetDebug("JSON parse failed.");
            return;
        }

        UpdateStructuredPanel(ar);
        MaybeTriggerMenu(ar); // only 'order' ¡ú switch
        SetDebug("JSON parsed OK.");
    }

    // ---------- UI ----------
    private void UpdateStructuredPanel(AIResponse d)
    {
        if (infoPanel) infoPanel.SetActive(true);

        string ui_intent = string.IsNullOrEmpty(d.user_intent) ? "<empty>" : d.user_intent;
        string ui_dish = string.IsNullOrEmpty(d.mentioned_dish) ? "<empty>" : d.mentioned_dish;

        if (intentText) intentText.text = "Intent: " + ui_intent;
        if (dishText) dishText.text = "Dish: " + ui_dish;
        if (inMenuText) inMenuText.text = "In Menu: " + (d.dish_in_menu ? "True" : "False");
        if (indexedMatchText) indexedMatchText.text = "Indexed Menu: " + (d.indexed_menu_match ? "True" : "False");
        if (menuIndexText) menuIndexText.text = "Index: " + (d.menu_index >= 0 ? d.menu_index.ToString() : "<none>");
    }

    // ---------- Switching (order-only filter) ----------
    private void MaybeTriggerMenu(AIResponse d)
    {
        var intent = (d.user_intent ?? "").Trim().ToLowerInvariant();
        if (intent != "order")
        {
            SetDebug($"[Menu] Not 'order' ¡ú skip. intent={d.user_intent}");
            return;
        }

        if (!d.indexed_menu_match || d.menu_index < 0)
        {
            SetDebug($"[Menu] indexed_menu_match={d.indexed_menu_match}, menu_index={d.menu_index} ¡ú skip.");
            return;
        }

        var switcher = markerSwitcher != null ? markerSwitcher : FindObjectOfType<MarkerChildSwitcher>();
        if (switcher == null)
        {
            SetDebug("[Menu] MarkerChildSwitcher missing.");
            return;
        }

        switcher.ShowOnlyChildAtIndex(d.menu_index);
        SetDebug($"[Menu] Switched to index {d.menu_index} ({d.mentioned_dish})");
    }

    // ---------- Parsing ----------
    private AIResponse TryParseAIResponse(string s)
    {
        try
        {
            var parsed = JsonUtility.FromJson<AIResponse>(s);
            if (IsMeaningful(parsed)) return parsed;
        }
        catch { }
        // Regex fallback (tolerant)
        var intentM = Regex.Match(s, "\"\\s*(user_intent|intent)\\s*\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        var dishM = Regex.Match(s, "\"\\s*(mentioned_dish|dish)\\s*\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        var inMenuM = Regex.Match(s, "\"\\s*dish_in_menu\\s*\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
        var idxHitM = Regex.Match(s, "\"\\s*indexed_menu_match\\s*\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
        var idxM = Regex.Match(s, "\"\\s*menu_index\\s*\"\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase);

        if (intentM.Success || dishM.Success || inMenuM.Success || idxHitM.Success || idxM.Success)
        {
            return new AIResponse
            {
                user_intent = intentM.Success ? intentM.Groups[2].Value : null,
                mentioned_dish = dishM.Success ? dishM.Groups[2].Value : null,
                dish_in_menu = inMenuM.Success && bool.Parse(inMenuM.Groups[1].Value.ToLowerInvariant()),
                indexed_menu_match = idxHitM.Success && bool.Parse(idxHitM.Groups[1].Value.ToLowerInvariant()),
                menu_index = idxM.Success ? int.Parse(idxM.Groups[1].Value) : -1
            };
        }
        return null;
    }

    private ReplyWrapper TryParseWrapper(string s)
    {
        try
        {
            var w = JsonUtility.FromJson<ReplyWrapper>(s);
            if (w != null && IsMeaningful(w.reply)) return w;
        }
        catch { }
        return null;
    }

    private bool IsMeaningful(AIResponse r)
    {
        if (r == null) return false;
        return
            !string.IsNullOrEmpty(r.user_intent) ||
            !string.IsNullOrEmpty(r.mentioned_dish) ||
            r.dish_in_menu == true || r.dish_in_menu == false ||
            r.indexed_menu_match == true || r.indexed_menu_match == false ||
            r.menu_index != 0 || r.menu_index == 0;
    }

    // ---------- Preprocess ----------
    private string PrepForParse(string raw)
    {
        string s = StripToFirstJsonObject(raw);
        s = NormalizeQuotes(s);
        s = CanonicalizeKeys(s);
        return s;
    }

    private string StripToFirstJsonObject(string raw)
    {
        var m = Regex.Match(raw ?? "", @"\{[\s\S]*\}");
        return m.Success ? m.Value.Trim() : (raw ?? "").Trim();
        // If your backend returns arrays, extend here to support [ ... ] too.
    }

    private string NormalizeQuotes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("¡°", "\"").Replace("¡±", "\"");
    }

    private string CanonicalizeKeys(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // canonical keys + legacy aliases
        s = Regex.Replace(s, "\"\\s*user_intent\\s*\"", "\"user_intent\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "\"\\s*mentioned_dish\\s*\"", "\"mentioned_dish\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "\"\\s*dish_in_menu\\s*\"", "\"dish_in_menu\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "\"\\s*indexed_menu_match\\s*\"", "\"indexed_menu_match\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "\"\\s*menu_index\\s*\"", "\"menu_index\"", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, "\"\\s*intent\\s*\"", "\"user_intent\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "\"\\s*dish\\s*\"", "\"mentioned_dish\"", RegexOptions.IgnoreCase);

        return s;
    }

    private void SetDebug(string msg)
    {
        if (debugText) debugText.text = msg;
        Debug.Log($"[JsonAIManager] {msg}");
    }

    // ---------- DTOs ----------
    [System.Serializable] private class ReplyWrapper { public AIResponse reply; }

    [System.Serializable]
    public class AIResponse
    {
        public string user_intent;
        public string mentioned_dish;
        public bool dish_in_menu;
        public bool indexed_menu_match;
        public int menu_index = -1;
    }
}
