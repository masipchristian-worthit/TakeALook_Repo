using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using DG.Tweening;

/// <summary>
/// Manager global de audio. Singleton DontDestroyOnLoad.
/// Usa una pool de AudioSources para evitar crear/destruir continuamente.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Mixers")]
    [SerializeField] private AudioMixerGroup sfxMixer;
    [SerializeField] private AudioMixerGroup uiMixer;
    [SerializeField] private AudioMixerGroup musicMixer;

    [Header("Pool")]
    [SerializeField] private int sfxPoolSize = 12;
    [SerializeField] private int uiPoolSize = 4;

    [Header("Library")]
    [SerializeField] private SoundLibrary library;

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;

    private List<AudioSource> _sfxPool = new List<AudioSource>();
    private List<AudioSource> _uiPool = new List<AudioSource>();
    private Dictionary<string, AudioClip> _libraryDict;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPools();
        BuildLibraryDict();
    }

    private void BuildPools()
    {
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go = new GameObject($"SFX_Source_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.outputAudioMixerGroup = sfxMixer;
            src.spatialBlend = 1f; // 3D por defecto
            _sfxPool.Add(src);
        }

        for (int i = 0; i < uiPoolSize; i++)
        {
            var go = new GameObject($"UI_Source_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.outputAudioMixerGroup = uiMixer;
            src.spatialBlend = 0f; // 2D para UI
            _uiPool.Add(src);
        }
    }

    private void BuildLibraryDict()
    {
        _libraryDict = new Dictionary<string, AudioClip>();
        if (library == null) return;

        foreach (var entry in library.entries)
        {
            if (entry.clip != null && !_libraryDict.ContainsKey(entry.id))
                _libraryDict.Add(entry.id, entry.clip);
        }
    }

    private AudioSource GetFreeSource(List<AudioSource> pool)
    {
        for (int i = 0; i < pool.Count; i++)
            if (!pool[i].isPlaying) return pool[i];

        // Si todos ocupados, usamos el de menor tiempo restante
        return pool[0];
    }

    #region Public API
    public void PlaySFX(string id, Vector3 position, float volume = 1f, float pitchVariation = 0.05f)
    {
        if (!_libraryDict.TryGetValue(id, out var clip)) return;
        PlaySFX(clip, position, volume, pitchVariation);
    }

    public void PlaySFX(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariation = 0.05f)
    {
        if (clip == null) return;
        var src = GetFreeSource(_sfxPool);
        src.transform.position = position;
        src.clip = clip;
        src.volume = volume;
        src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        src.spatialBlend = 1f;
        src.Play();
    }

    public void PlayUI(string id, float volume = 1f, float pitchVariation = 0.02f)
    {
        if (!_libraryDict.TryGetValue(id, out var clip)) return;
        PlayUI(clip, volume, pitchVariation);
    }

    public void PlayUI(AudioClip clip, float volume = 1f, float pitchVariation = 0.02f)
    {
        if (clip == null) return;
        var src = GetFreeSource(_uiPool);
        src.clip = clip;
        src.volume = volume;
        src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        src.spatialBlend = 0f;
        src.Play();
    }

    public void PlayMusic(AudioClip clip, float fadeTime = 1f, float volume = 0.7f)
    {
        if (musicSource == null || clip == null) return;

        DOTween.Kill(musicSource);

        if (musicSource.isPlaying)
        {
            musicSource.DOFade(0f, fadeTime * 0.5f).SetTarget(musicSource).OnComplete(() =>
            {
                musicSource.clip = clip;
                musicSource.Play();
                musicSource.DOFade(volume, fadeTime * 0.5f).SetTarget(musicSource);
            });
        }
        else
        {
            musicSource.clip = clip;
            musicSource.volume = 0f;
            musicSource.Play();
            musicSource.DOFade(volume, fadeTime).SetTarget(musicSource);
        }
    }

    public void StopMusic(float fadeTime = 1f)
    {
        if (musicSource == null) return;
        DOTween.Kill(musicSource);
        musicSource.DOFade(0f, fadeTime).SetTarget(musicSource).OnComplete(() => musicSource.Stop());
    }
    #endregion
}