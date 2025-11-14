using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AmbientSoundPlayer : MonoBehaviour
{
    [Header("Playlist")]
    public List<AudioClip> clips = new List<AudioClip>();

    [Tooltip("每次播放之间的随机停顿区间（秒）")]
    public Vector2 gapRange = new Vector2(0.5f, 2.0f);

    [Tooltip("每段音频的随机音量范围（0-1）")]
    public Vector2 volumeRange = new Vector2(0.8f, 1.0f);

    [Tooltip("每段音频的随机音高范围")]
    public Vector2 pitchRange = new Vector2(0.98f, 1.02f);

    [Tooltip("播放起点是否随机（适合长环境音）")]
    public bool randomStartTime = false;

    [Header("3D / 距离设置")]
    public bool use3DAudio = true;            // 3D 或 2D
    public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
    public float minDistance = 2f;
    public float maxDistance = 20f;
    public AnimationCurve customRolloffCurve; // 若rolloff=Custom，将使用该曲线（x=距离, y=衰减）

    [Tooltip("是否让此音源保持固定音量（忽略距离衰减）")]
    public bool fixedVolume = false;

    [Range(0f, 1f)]
    public float fixedVolumeValue = 0.7f;

    [Tooltip("可选：显式指定 Listener（例如玩家相机变换）")]
    public Transform listenerHint;

    [Header("其他")]
    [Tooltip("是否在Awake时自动开始")]
    public bool autoPlayOnAwake = true;

    [Tooltip("在同一个档口里避免立刻重复上一个clip")]
    public bool avoidImmediateRepeat = true;

    private AudioSource _src;
    private int _lastIndex = -1;
    private Coroutine _loopRoutine;

    void Awake()
    {
        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;                 // 我们自己控制循环
        _src.spatialize = use3DAudio;      // 若用插件/VR可开启HRTF
        _src.dopplerLevel = 0f;            // 避免档口相对速度引入多普勒
        ApplySpatialSettings();
    }

    void OnEnable()
    {
        if (autoPlayOnAwake && _loopRoutine == null && clips.Count > 0)
            _loopRoutine = StartCoroutine(PlayLoop());
    }

    void OnDisable()
    {
        if (_loopRoutine != null) StopCoroutine(_loopRoutine);
        _loopRoutine = null;
    }

    void ApplySpatialSettings()
    {
        if (fixedVolume)
        {
            // 方案A：把它设成2D，完全不随距离变（最简单）
            // 方案B：想保留空间化但不衰减 -> 用自定义平直曲线
            // 这里优先使用方案A；如需B请把use3DAudio=true并提供平直customRolloffCurve
            if (!use3DAudio)
            {
                _src.spatialBlend = 0f; // 2D
            }
            else
            {
                _src.spatialBlend = 1f; // 3D但不衰减
                _src.rolloffMode = AudioRolloffMode.Custom;
                if (customRolloffCurve == null || customRolloffCurve.length == 0)
                {
                    // 构造一条“恒为1”的平直曲线
                    customRolloffCurve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(maxDistance, 1f)
                    );
                }
                _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customRolloffCurve);
            }

            _src.volume = fixedVolumeValue;
            _src.ignoreListenerVolume = true;   // 不受AudioListener.volume影响（可按需关闭）
            _src.bypassListenerEffects = true;  // 避免被全局监听器效果改动
        }
        else
        {
            // 正常的3D距离衰减
            if (use3DAudio)
            {
                _src.spatialBlend = 1f;
                _src.rolloffMode = rolloff;
                _src.minDistance = Mathf.Max(0.01f, minDistance);
                _src.maxDistance = Mathf.Max(_src.minDistance + 0.01f, maxDistance);

                if (rolloff == AudioRolloffMode.Custom && customRolloffCurve != null && customRolloffCurve.length > 0)
                {
                    _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customRolloffCurve);
                }
            }
            else
            {
                _src.spatialBlend = 0f;  // 2D
            }

            _src.ignoreListenerVolume = false;
            _src.bypassListenerEffects = false;
        }

        // 可选：手动设置AudioListener到你给的hint（不是必须）
        if (listenerHint != null)
        {
            var listener = FindObjectOfType<AudioListener>();
            if (listener != null && listener.transform != listenerHint)
            {
                // 这里只提醒：Unity的AudioListener一般跟随主相机，
                // 如需切换，请在你的相机上放置/移动AudioListener。
                // 代码层面不强行移动它，避免影响你的相机体系。
            }
        }
    }

    IEnumerator PlayLoop()
    {
        while (true)
        {
            if (clips == null || clips.Count == 0)
            {
                yield return null;
                continue;
            }

            // 选一个随机clip，避免和上一次相同（可选）
            int idx = Random.Range(0, clips.Count);
            if (avoidImmediateRepeat && clips.Count > 1 && idx == _lastIndex)
            {
                idx = (idx + 1) % clips.Count;
            }
            _lastIndex = idx;

            var clip = clips[idx];
            if (clip == null)
            {
                yield return null;
                continue;
            }

            // 随机参数
            float vol = fixedVolume ? fixedVolumeValue : Random.Range(volumeRange.x, volumeRange.y);
            float pit = Random.Range(pitchRange.x, pitchRange.y);

            _src.clip = clip;
            _src.volume = vol;
            _src.pitch = pit;

            if (randomStartTime && clip.length > 1f)
                _src.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
            else
                _src.time = 0f;

            _src.Play();

            // 等待此clip播完
            yield return new WaitWhile(() => _src.isPlaying);

            // 随机间隔
            float gap = Random.Range(gapRange.x, gapRange.y);
            if (gap > 0f) yield return new WaitForSeconds(gap);
        }
    }

    // 在运行时动态切换设置时调用
    public void SetFixedVolume(bool enabled, float value = -1f)
    {
        fixedVolume = enabled;
        if (value >= 0f) fixedVolumeValue = Mathf.Clamp01(value);
        ApplySpatialSettings();
    }

    public void Set3D(bool enabled)
    {
        use3DAudio = enabled;
        ApplySpatialSettings();
    }
}
