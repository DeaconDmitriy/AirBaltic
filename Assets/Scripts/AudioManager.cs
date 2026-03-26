using System.Collections;
using UnityEngine;

/// <summary>
/// Centralised audio controller for the AirBaltic VR Globe.
///
/// Architecture:
///   Three child AudioSources are created at runtime:
///     • Music    — looping background track (DontDestroyOnLoad)
///     • SFX      — one-shots: takeoff, landing, city click, UI buttons
///     • Ambience — looping city ambience
///
/// Crossfade behaviour:
///   PlayCityAmbience() → fades music OUT, fades ambience IN
///   StopAmbience()     → fades ambience OUT, fades music IN
///
/// Usage:
///   AudioManager.Instance?.PlayTakeOff();
///   AudioManager.Instance?.PlayButtonSound(2);
///   AudioManager.Instance?.SetMusicVolume(0.3f);
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // ── Inspector Fields ──────────────────────────────────────────────

    [Header("Background Music")]
    [Tooltip("Assign: Songs - Warmth And Light.wav")]
    public AudioClip backgroundMusic;

    [Range(0f, 1f)]
    public float musicVolume = 0.4f;

    [Header("Plane SFX")]
    [Tooltip("Assign: Airplane_TakeOff.wav")]
    public AudioClip takeOffSound;

    [Tooltip("Assign: Airplane_LandingBoop.wav")]
    public AudioClip landingSound;

    [Header("Ambience")]
    [Tooltip("Assign: Ambience_CitySounds.wav — plays when camera is near a city")]
    public AudioClip cityAmbienceSound;

    [Range(0f, 1f)]
    public float ambienceVolume = 0.5f;

    [Header("Crossfade")]
    [Tooltip("Seconds to fade between music and city ambience.")]
    public float crossfadeDuration = 1.0f;

    [Header("UI Button Sounds")]
    [Tooltip("Assign Button1.wav → Button4.wav in order. Index 0 = city click.")]
    public AudioClip[] buttonSounds;

    [Range(0f, 1f)]
    public float sfxVolume = 1f;

    // ── Private ───────────────────────────────────────────────────────

    private AudioSource _musicSource;
    private AudioSource _sfxSource;
    private AudioSource _ambienceSource;

    private Coroutine _musicFade;
    private Coroutine _ambienceFade;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _musicSource    = CreateSource("Music",    loop: true,  volume: musicVolume);
        _sfxSource      = CreateSource("SFX",      loop: false, volume: sfxVolume);
        _ambienceSource = CreateSource("Ambience", loop: true,  volume: 0f);  // starts silent
    }

    private void Start()
    {
        PlayMusic();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Music ─────────────────────────────────────────────────────────

    public void PlayMusic()
    {
        if (backgroundMusic == null || _musicSource == null) return;
        if (_musicSource.isPlaying) return;
        _musicSource.clip   = backgroundMusic;
        _musicSource.volume = musicVolume;
        _musicSource.Play();
    }

    public void SetMusicVolume(float v)
    {
        musicVolume = Mathf.Clamp01(v);
        // Only update live volume if music is currently audible (not faded out by ambience)
        if (_ambienceSource == null || !_ambienceSource.isPlaying)
            if (_musicSource != null) _musicSource.volume = musicVolume;
    }

    // ── Plane SFX ─────────────────────────────────────────────────────

    public void PlayTakeOff()  => PlaySFX(takeOffSound);
    public void PlayLanding()  => PlaySFX(landingSound);

    // ── Ambience / Crossfade ──────────────────────────────────────────

    /// <summary>
    /// Crossfade: music fades out, city ambience fades in.
    /// Safe to call repeatedly — ignored if already playing.
    /// </summary>
    public void PlayCityAmbience()
    {
        if (cityAmbienceSound == null || _ambienceSource == null) return;

        // Skip only if already playing at (or very near) full target volume.
        // Do NOT skip if the source is stuck at volume 0 after an interrupted fade.
        bool alreadyFull = _ambienceSource.isPlaying
                           && _ambienceSource.clip == cityAmbienceSound
                           && _ambienceSource.volume >= ambienceVolume * 0.99f;
        if (alreadyFull) return;

        // If the source is mid-fade or stopped, (re)start it from silence.
        if (!_ambienceSource.isPlaying || _ambienceSource.clip != cityAmbienceSound)
        {
            _ambienceSource.clip   = cityAmbienceSound;
            _ambienceSource.volume = 0f;
            _ambienceSource.Play();
        }

        // Fade ambience in, music out
        if (_ambienceFade != null) StopCoroutine(_ambienceFade);
        if (_musicFade    != null) StopCoroutine(_musicFade);
        _ambienceFade = StartCoroutine(FadeSource(_ambienceSource, ambienceVolume, crossfadeDuration));
        _musicFade    = StartCoroutine(FadeSource(_musicSource,    0f,             crossfadeDuration));
    }

    /// <summary>
    /// Crossfade: city ambience fades out (if playing), music always fades back in.
    /// Safe to call even if ambience was never started — music will still restore.
    /// </summary>
    public void StopAmbience()
    {
        if (_ambienceSource == null) return;

        // Cancel any running fades so they don't fight each other
        if (_ambienceFade != null) { StopCoroutine(_ambienceFade); _ambienceFade = null; }
        if (_musicFade    != null) { StopCoroutine(_musicFade);    _musicFade    = null; }

        // Hard-stop the ambience source immediately so it can never be left in a
        // stuck "isPlaying=true, volume=0" state after an interrupted FadeOutAndStop.
        if (_ambienceSource.isPlaying)
        {
            _ambienceFade = StartCoroutine(FadeOutAndStop(_ambienceSource, crossfadeDuration));
        }
        else
        {
            _ambienceSource.Stop();
            _ambienceSource.volume = 0f;
        }

        // Always bring music back — even if it was never faded out this call is harmless
        if (!_musicSource.isPlaying)
        {
            // Music was fully stopped (e.g. app just launched); restart it then fade in
            _musicSource.clip   = backgroundMusic;
            _musicSource.volume = 0f;
            _musicSource.Play();
        }
        _musicFade = StartCoroutine(FadeSource(_musicSource, musicVolume, crossfadeDuration));
    }

    // ── UI Buttons ────────────────────────────────────────────────────

    public void PlayButtonSound(int index = 0)
    {
        if (buttonSounds == null || buttonSounds.Length == 0) return;
        PlaySFX(buttonSounds[Mathf.Clamp(index, 0, buttonSounds.Length - 1)]);
    }

    // ── Master Volume / Mute (Pause Menu Settings) ────────────────────

    /// <summary>Current master volume (0–1). Reflects the last call to SetMasterVolume.</summary>
    public float MasterVolume { get; private set; } = 1f;

    /// <summary>True when audio is globally muted via SetMasterMute.</summary>
    public bool IsMuted { get; private set; } = false;

    /// <summary>
    /// Sets the global master volume via AudioListener.volume.
    /// Has no effect while muted — the new level is remembered and applied on unmute.
    /// </summary>
    public void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        if (!IsMuted)
            AudioListener.volume = MasterVolume;
    }

    /// <summary>
    /// Mutes or unmutes all audio by toggling AudioListener.volume.
    /// Volume level is preserved across mute/unmute cycles.
    /// </summary>
    public void SetMasterMute(bool muted)
    {
        IsMuted              = muted;
        AudioListener.volume = muted ? 0f : MasterVolume;
    }

    // ── Per-source Volume Controls ─────────────────────────────────────

    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        if (_sfxSource != null) _sfxSource.volume = sfxVolume;
    }

    public void SetAmbienceVolume(float v)
    {
        ambienceVolume = Mathf.Clamp01(v);
        // Only update if currently audible
        if (_ambienceSource != null && _ambienceSource.isPlaying)
            _ambienceSource.volume = ambienceVolume;
    }

    // ── Internal Helpers ──────────────────────────────────────────────

    private void PlaySFX(AudioClip clip)
    {
        if (clip == null || _sfxSource == null) return;
        _sfxSource.volume = sfxVolume;
        _sfxSource.PlayOneShot(clip);
    }

    /// <summary>Smoothly ramp an AudioSource to a target volume over duration seconds.</summary>
    private IEnumerator FadeSource(AudioSource src, float targetVolume, float duration)
    {
        if (src == null) yield break;

        float startVolume = src.volume;
        float elapsed     = 0f;

        while (elapsed < duration)
        {
            elapsed      += Time.unscaledDeltaTime;
            src.volume    = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
            yield return null;
        }

        src.volume = targetVolume;
    }

    /// <summary>Fade out an AudioSource then stop it cleanly.</summary>
    private IEnumerator FadeOutAndStop(AudioSource src, float duration)
    {
        yield return FadeSource(src, 0f, duration);
        src.Stop();
        src.volume = 0f;
    }

    private AudioSource CreateSource(string sourceName, bool loop, float volume)
    {
        var child            = new GameObject(sourceName);
        child.transform.SetParent(transform);
        var src              = child.AddComponent<AudioSource>();
        src.loop             = loop;
        src.volume           = volume;
        src.playOnAwake      = false;
        src.spatialBlend     = 0f;
        return src;
    }
}
