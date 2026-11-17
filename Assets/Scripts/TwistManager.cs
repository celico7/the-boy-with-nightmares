using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Effets combinables (case à cocher multiple dans l'inspecteur de TriggerZone).</summary>
[Flags]
public enum TwistEffects
{
    None = 0,
    DarkAmbiance = 1 << 0,        // fond clair -> sombre / monochrome
    Screamer = 1 << 1,            // flash noir/blanc (+ son optionnel)
    InvertControls = 1 << 2,      // isInverted = true pendant X secondes
    SlowMotion = 1 << 3,          // Time.timeScale réduit pendant X secondes
    RandomGameplayTwist = 1 << 4  // tire au hasard entre inversion et ralenti
}

/// <summary>
/// Exécute les twists : ambiance, screamer et modificateurs de gameplay.
/// Toutes les durées utilisent le temps réel (unscaled) pour rester correctes pendant le ralenti.
/// 
/// Ambiance : on mélange (1) la couleur de fond de la caméra, (2) la teinte de sprites de décor,
/// (3) le poids d'un Volume "sombre/monochrome" (URP : Color Adjustments saturation -100, Vignette...).
/// </summary>
public class TwistManager : MonoBehaviour
{
    public static TwistManager Instance { get; private set; }

    [Header("Références")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Camera mainCamera;

    [Header("Ambiance sombre")]
    [SerializeField] private Color darkBackgroundColor = new Color(0.04f, 0.04f, 0.06f);
    [Tooltip("Volume global (priorité haute, poids 0 au départ) avec saturation -100 + vignette.")]
    [SerializeField] private Volume darkVolume;
    [Tooltip("Sprites de décor à assombrir (ciel, nuages, collines...).")]
    [SerializeField] private SpriteRenderer[] backgroundSprites;
    [SerializeField] private Color darkSpriteTint = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private float ambianceTransitionTime = 1.5f;

    [Header("Screamer")]
    [Tooltip("Image UI plein écran (Raycast Target désactivé).")]
    [SerializeField] private Image screamerPanel;
    [SerializeField] private float screamerDuration = 0.2f;
    [Tooltip("Couleurs alternées pendant le flash (réparties sur la durée).")]
    [SerializeField] private Color[] flashColors = { Color.white, Color.black, Color.white, Color.black };
    [Tooltip("Son optionnel joué au déclenchement.")]
    [SerializeField] private AudioSource screamerAudio;

    private float defaultFixedDelta;
    private Color lightBackgroundColor;
    private Color[] originalSpriteColors;
    private float ambianceLevel; // 0 = clair, 1 = sombre

    private Coroutine ambianceRoutine;
    private Coroutine screamerRoutine;
    private Coroutine invertRoutine;
    private Coroutine slowRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        defaultFixedDelta = Time.fixedDeltaTime;

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null) lightBackgroundColor = mainCamera.backgroundColor;

        // On mémorise les couleurs d'origine des sprites de décor.
        if (backgroundSprites != null)
        {
            originalSpriteColors = new Color[backgroundSprites.Length];
            for (int i = 0; i < backgroundSprites.Length; i++)
                if (backgroundSprites[i] != null)
                    originalSpriteColors[i] = backgroundSprites[i].color;
        }

        if (darkVolume != null) darkVolume.weight = 0f;
        if (screamerPanel != null) screamerPanel.color = Color.clear;
    }

    private void OnDestroy()
    {
        // Évite de rester en ralenti après un rechargement de scène.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : 0.02f;
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Point d'entrée principal (appelé par TriggerZone)
    // ------------------------------------------------------------------
    public void ExecuteTwist(TwistEffects effects, float duration = 3f, float slowScale = 0.35f)
    {
        if (Has(effects, TwistEffects.DarkAmbiance)) SetDarkAmbiance(true);
        if (Has(effects, TwistEffects.Screamer)) TriggerScreamer();

        // Twist aléatoire : on ajoute une des deux options de gameplay.
        if (Has(effects, TwistEffects.RandomGameplayTwist))
            effects |= UnityEngine.Random.value < 0.5f ? TwistEffects.InvertControls : TwistEffects.SlowMotion;

        if (Has(effects, TwistEffects.InvertControls)) InvertControls(duration);
        if (Has(effects, TwistEffects.SlowMotion)) SlowMotion(duration, slowScale);
    }

    private static bool Has(TwistEffects value, TwistEffects flag) => (value & flag) != 0;

    // ------------------------------------------------------------------
    // Ambiance
    // ------------------------------------------------------------------
    public void SetDarkAmbiance(bool dark)
    {
        if (ambianceRoutine != null) StopCoroutine(ambianceRoutine);
        ambianceRoutine = StartCoroutine(AmbianceRoutine(dark ? 1f : 0f));
    }

    private IEnumerator AmbianceRoutine(float target)
    {
        float from = ambianceLevel;
        float t = 0f;

        while (ambianceTransitionTime > 0f && t < ambianceTransitionTime)
        {
            t += Time.unscaledDeltaTime;
            ApplyAmbiance(Mathf.Lerp(from, target, t / ambianceTransitionTime));
            yield return null;
        }

        ApplyAmbiance(target);
        ambianceRoutine = null;
    }

    private void ApplyAmbiance(float k)
    {
        ambianceLevel = k;

        if (mainCamera != null)
            mainCamera.backgroundColor = Color.Lerp(lightBackgroundColor, darkBackgroundColor, k);

        if (darkVolume != null)
            darkVolume.weight = k;

        if (backgroundSprites != null)
        {
            for (int i = 0; i < backgroundSprites.Length; i++)
            {
                if (backgroundSprites[i] == null) continue;
                backgroundSprites[i].color = Color.Lerp(originalSpriteColors[i], darkSpriteTint, k);
            }
        }
    }

    // ------------------------------------------------------------------
    // Screamer (flash noir/blanc)
    // ------------------------------------------------------------------
    public void TriggerScreamer()
    {
        if (screamerPanel == null) return;
        if (screamerRoutine != null) StopCoroutine(screamerRoutine);
        screamerRoutine = StartCoroutine(ScreamerRoutine());
    }

    private IEnumerator ScreamerRoutine()
    {
        if (screamerAudio != null) screamerAudio.Play();

        Color[] colors = (flashColors != null && flashColors.Length > 0)
            ? flashColors
            : new[] { Color.white };

        float step = screamerDuration / colors.Length;
        foreach (Color c in colors)
        {
            screamerPanel.color = c;
            yield return new WaitForSecondsRealtime(step);
        }

        screamerPanel.color = Color.clear;
        screamerRoutine = null;
    }

    // ------------------------------------------------------------------
    // Twists de gameplay
    // ------------------------------------------------------------------
    public void InvertControls(float duration)
    {
        if (player == null) return;
        if (invertRoutine != null) StopCoroutine(invertRoutine); // on relance le chrono
        invertRoutine = StartCoroutine(InvertRoutineCo(duration));
    }

    private IEnumerator InvertRoutineCo(float duration)
    {
        player.isInverted = true;
        yield return new WaitForSecondsRealtime(duration);
        player.isInverted = false;
        invertRoutine = null;
    }

    public void SlowMotion(float duration, float scale)
    {
        if (slowRoutine != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(SlowMotionRoutine(duration, Mathf.Clamp(scale, 0.05f, 1f)));
    }

    private IEnumerator SlowMotionRoutine(float duration, float scale)
    {
        SetTimeScale(scale);
        yield return new WaitForSecondsRealtime(duration);

        // Retour progressif à la vitesse normale (0,4 s) pour ne pas être brutal.
        const float easeOut = 0.4f;
        float t = 0f;
        while (t < easeOut)
        {
            t += Time.unscaledDeltaTime;
            SetTimeScale(Mathf.Lerp(scale, 1f, t / easeOut));
            yield return null;
        }

        SetTimeScale(1f);
        slowRoutine = null;
    }

    // On adapte aussi fixedDeltaTime pour que la physique reste fluide en ralenti.
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = defaultFixedDelta * scale;
    }
}
