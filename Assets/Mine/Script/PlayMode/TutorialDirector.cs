using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using TMPro;

public class TutorialDirector : MonoBehaviour
{
    [Serializable]
    public class DialogEntry
    {
        [TextArea(2, 4)]
        public string text;
        public AudioClip voice;
        public VideoClip videoClip;
        public bool showVideo = false;
        public bool hideVideoOnEnd = true;
    }

    [Header("UI Root (all CanvasGroups under this will fade in/out as a whole)")]
    public Transform uiRoot;                 // assign to TutorialCanvas (or a parent under it)
    public bool includeUiRootCanvasGroup = true;
    public bool excludeVideoGroupFromBatch = true;

    [Header("Per-line Video UI (kept as-is)")]
    public CanvasGroup videoGroup;           // fades per line
    public VideoPlayer videoPlayer;

    [Header("Text/Voice")]
    public TMP_Text dialogText;
    public AudioSource voiceSource;

    [Header("Input")]
    public OVRInput.Button advanceKey = OVRInput.Button.One;

    [Header("Timings")]
    public float uiFade = 0.25f;             // global UI in/out
    public float videoFade = 0.3f;           // per-line video fade

    [Header("Dialog Script")]
    public List<DialogEntry> entries = new List<DialogEntry>();

    // Internals
    private int _index = -1;
    private bool _busy = false;
    private Action _onFinished;
    private readonly List<CanvasGroup> _batchGroups = new List<CanvasGroup>();

    void Awake()
    {
        // Prepare video group
        if (videoGroup) videoGroup.alpha = 0f;
        if (videoPlayer)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.loopPointReached += OnVideoEnded;
            videoPlayer.skipOnDrop = true;
        }

        // Collect all CanvasGroups under uiRoot
        _batchGroups.Clear();
        if (uiRoot)
        {
            if (includeUiRootCanvasGroup)
            {
                var cg = uiRoot.GetComponent<CanvasGroup>();
                if (cg) _batchGroups.Add(cg);
            }
            var childGroups = uiRoot.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in childGroups)
            {
                // Optionally exclude the per-line video group from batch
                if (excludeVideoGroupFromBatch && videoGroup && cg == videoGroup) continue;
                if (!_batchGroups.Contains(cg)) _batchGroups.Add(cg);
            }
        }

        // Ensure all batch groups start hidden (alpha=0) without changing active state
        foreach (var cg in _batchGroups)
        {
            if (!cg) continue;
            cg.alpha = 0f;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Begin tutorial: fade-in all CanvasGroups under uiRoot, then show first line.
    /// </summary>
    public void Begin(Action onFinished)
    {
        _onFinished = onFinished;
        gameObject.SetActive(true);
        StartCoroutine(BeginRoutine());
    }

    private IEnumerator BeginRoutine()
    {
        _index = -1;

        // Fade-in all UI groups as a whole
        yield return FadeGroups(_batchGroups, 1f, uiFade);

        // Start first line
        yield return NextLineRoutine();
    }

    void Update()
    {
        if (_busy) return;
        if (_index < 0 || _index >= entries.Count) return;

        if (OVRInput.GetDown(advanceKey))
        {
            StartCoroutine(NextLineRoutine());
        }
    }

    private IEnumerator NextLineRoutine()
    {
        _busy = true;

        // —— Clean previous line —— //
        if (_index >= 0 && _index < entries.Count)
        {
            var prev = entries[_index];
            if (prev.hideVideoOnEnd && videoGroup && videoGroup.alpha > 0.01f)
            {
                yield return FadeGroup(videoGroup, 0f, videoFade);
            }


            if (videoPlayer)
            {
                if (videoPlayer.isPlaying) videoPlayer.Stop();
                ClearVideoOutput();        
                videoPlayer.clip = null;   
            }

            if (voiceSource && voiceSource.isPlaying) voiceSource.Stop();
        }

        // —— Advance —— //
        _index++;

        // —— Finished all —— //
        if (_index >= entries.Count)
        {
            yield return FadeGroups(_batchGroups, 0f, uiFade);
            _busy = false;
            _onFinished?.Invoke();
            gameObject.SetActive(false);
            yield break;
        }

        // —— Render current —— //
        var cur = entries[_index];
        if (dialogText) dialogText.text = cur.text ?? string.Empty;

        if (voiceSource && cur.voice)
        {
            voiceSource.clip = cur.voice;
            voiceSource.Play();
        }

        if (cur.showVideo && videoPlayer && videoGroup)
        {

            videoGroup.alpha = 0f;
            videoPlayer.clip = cur.videoClip;
            videoPlayer.isLooping = true;  
            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared)
                yield return null;


            videoPlayer.frame = 0;
            // videoPlayer.time = 0d;

            videoPlayer.Play();
            yield return FadeGroup(videoGroup, 1f, videoFade);
        }

        _busy = false;
    }


    private void OnVideoEnded(VideoPlayer vp)
    {
        // If you prefer auto-advance after video ends, uncomment:
        // StartCoroutine(NextLineRoutine());
    }

    // ---------- Fade helpers ----------
    private IEnumerator FadeGroup(CanvasGroup cg, float target, float dur)
    {
        if (!cg) yield break;
        cg.gameObject.SetActive(true);
        float start = cg.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, dur);
            cg.alpha = Mathf.Lerp(start, target, t);
            yield return null;
        }
        cg.alpha = target;
        // Keep active so layout stays; alpha=0 makes it invisible anyway
    }

    private IEnumerator FadeGroups(List<CanvasGroup> groups, float target, float dur)
    {
        if (groups == null || groups.Count == 0) yield break;

        float t = 0f;
        // Ensure all active
        foreach (var g in groups) if (g) g.gameObject.SetActive(true);

        // Parallel fade (single coroutine loop)
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, dur);
            float a;
            if (dur <= 0.0001f) a = target;
            else
            {
                // We need each to lerp from its current alpha to target
                // For simplicity we sample current alpha at loop start per group
                // (Could cache starts; this is acceptable for UI counts)
            }
            foreach (var g in groups)
            {
                if (!g) continue;
                // Linear interpolation per-frame using current value to target
                g.alpha = Mathf.Lerp(g.alpha, target, Time.deltaTime / Mathf.Max(0.01f, dur));
            }
            yield return null;
        }
        // Snap to target
        foreach (var g in groups) if (g) g.alpha = target;
    }
    private void ClearVideoOutput()
    {
        if (!videoPlayer) return;

        var rt = videoPlayer.targetTexture;
        if (rt == null) return;

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);  
        RenderTexture.active = prev;
    }

}
