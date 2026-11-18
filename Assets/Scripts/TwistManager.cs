using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
/// Réglages d'un twist. Chaque TriggerZone possède sa propre copie :
/// on peut donc régler chaque twist indépendamment des autres.
/// Seuls les réglages des effets cochés dans la zone sont utilisés.
/// </summary>
[Serializable]
public class TwistSettings
{
    [Header("Ambiance sombre")]
    [Tooltip("Durée (s) du fondu vers l'ambiance sombre.")]
    public float ambianceFadeInTime = 0.5f;
    [Tooltip("Si coché, l'ambiance revient à la normale. Sinon elle reste sombre définitivement.")]
    public bool revertAmbiance = true;
    [Tooltip("Durée (s) pendant laquelle l'ambiance reste sombre, une fois le fondu terminé.")]
    public float ambianceHoldTime = 3f;
    [Tooltip("Durée (s) du fondu de retour à la normale.")]
    public float ambianceFadeOutTime = 0.5f;

    [Header("Screamer")]
    [Tooltip("Durée totale (s) du flash.")]
    public float screamerDuration = 0.2f;

    [Header("Inversion des touches")]
    [Tooltip("Durée (s réelles) pendant laquelle les touches sont inversées.")]
    public float invertDuration = 3f;

    [Header("Ralenti")]
    [Tooltip("Durée (s réelles) du ralenti.")]
    public float slowMotionDuration = 3f;
    [Range(0.05f, 1f)]
    [Tooltip("Valeur de Time.timeScale pendant le ralenti (1 = normal).")]
    public float slowMotionScale = 0.35f;
}

/// <summary>
/// Exécute les twists : ambiance, screamer et modificateurs de gameplay.
/// Toutes les durées utilisent le temps réel (unscaled) pour rester correctes pendant le ralenti.
/// Les durées et intensités viennent des TwistSettings de chaque TriggerZone.
/// 
/// Ambiance : on mélange (1) la couleur de fond de la caméra, (2) la teinte de sprites de décor,
/// (3) le poids d'un Volume "sombre/monochrome", (4) optionnellement la Global Light 2D.
/// </summary>
public class TwistManager : MonoBehaviour
{
    public static TwistManager Instance { get; private set; }

    [Header("Références")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Camera mainCamera;

    [Header("Ambiance sombre : rendu")]
    [SerializeField] private Color darkBackgroundColor = new Color(0.04f, 0.04f, 0.06f);
    [Tooltip("Volume global (priorité haute) avec saturation -100 + vignette. Son poids est piloté par le script.")]
    [SerializeField] private Volume darkVolume;
    [Tooltip("Sprites de décor à assombrir (ciel, nuages, collines...).")]
    [SerializeField] private SpriteRenderer[] backgroundSprites;
    [SerializeField] private Color darkSpriteTint = new Color(0.3f, 0.3f, 0.3f);

    [Header("Ambiance sombre : Global Light 2D (optionnel)")]
    [SerializeField] private Light2D globalLight;
    [SerializeField] private Color darkLightColor = new Color(0.35f, 0.4f, 0.65f);
    [SerializeField] private float darkLightIntensity = 0.35f;

    [Header("Screamer : rendu")]
    [Tooltip("Image UI plein écran (Raycast Target désactivé).")]
    [SerializeField] private Image screamerPanel;
    [Tooltip("Couleurs alternées pendant le flash (réparties sur la durée du screamer).")]
    [SerializeField] private Color[] flashColors = { Color.white, Color.black, Color.white, Color.black };
    [Tooltip("Son optionnel joué au déclenchement.")]
    [SerializeField] private AudioSource screamerAudio;

    private float defaultFixedDelta;
    private Color lightBackgroundColor;
    private Color[] originalSpriteColors;
    private Color lightColorOriginal;
    private float lightIntensityOriginal;
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

        if (globalLight != null)
        {
            lightColorOriginal = globalLight.color;
            lightIntensityOriginal = globalLight.intensity;
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
    public void ExecuteTwist(TwistEffects effects, TwistSettings settings)
    {
        if (settings == null) settings = new TwistSettings();

        if (Has(effects, TwistEffects.DarkAmbiance))
        {
            SetDarkAmbiance(true,
                settings.ambianceFadeInTime,
                settings.revertAmbiance ? settings.ambianceHoldTime : 0f,
                settings.ambianceFadeOutTime);
        }

        if (Has(effects, TwistEffects.Screamer))
            TriggerScreamer(settings.screamerDuration);

        // Twist aléatoire : on ajoute une des deux options de gameplay.
        if (Has(effects, TwistEffects.RandomGameplayTwist))
            effects |= UnityEngine.Random.value < 0.5f ? TwistEffects.InvertControls : TwistEffects.SlowMotion;

        if (Has(effects, TwistEffects.InvertControls))
            InvertControls(settings.invertDuration);

        if (Has(effects, TwistEffects.SlowMotion))
            SlowMotion(settings.slowMotionDuration, settings.slowMotionScale);
    }

    private static bool Has(TwistEffects value, TwistEffects flag) => (value & flag) != 0;

    /// <summary>
    /// Remet tout à zéro (ambiance, screamer, inversion, ralenti). Appelé à chaque respawn.
    /// </summary>
    public void ResetAll()
    {
        StopAllCoroutines();
        ambianceRoutine = null;
        screamerRoutine = null;
        invertRoutine = null;
        slowRoutine = null;

        SetTimeScale(1f);
        if (player != null) player.isInverted = false;

        if (screamerAudio != null) screamerAudio.Stop();
        if (screamerPanel != null) screamerPanel.color = Color.clear;

        ApplyAmbiance(0f);
    }

    // ------------------------------------------------------------------
    // Ambiance
    // ------------------------------------------------------------------
    /// <param name="fadeInTime">Durée du fondu vers l'ambiance (ou vers le clair si dark = false).</param>
    /// <param name="revertAfter">
    /// &gt; 0 : une fois le fondu terminé, l'ambiance reste sombre ce nombre de secondes puis revient.
    /// 0 : l'ambiance reste sombre définitivement.
    /// </param>
    /// <param name="fadeOutTime">Durée du fondu de retour à la normale.</param>
    public void SetDarkAmbiance(bool dark, float fadeInTime = 0.5f, float revertAfter = 0f, float fadeOutTime = 0.5f)
    {
        if (ambianceRoutine != null) StopCoroutine(ambianceRoutine);
        ambianceRoutine = StartCoroutine(AmbianceRoutine(dark ? 1f : 0f, fadeInTime, revertAfter, fadeOutTime));
    }

    private IEnumerator AmbianceRoutine(float target, float fadeInTime, float revertAfter, float fadeOutTime)
    {
        yield return FadeAmbiance(target, fadeInTime);

        if (target > 0f && revertAfter > 0f)
        {
            yield return new WaitForSecondsRealtime(revertAfter);
            yield return FadeAmbiance(0f, fadeOutTime);
        }

        ambianceRoutine = null;
    }

    private IEnumerator FadeAmbiance(float target, float fadeTime)
    {
        float from = ambianceLevel;
        float t = 0f;

        while (fadeTime > 0f && t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            ApplyAmbiance(Mathf.Lerp(from, target, t / fadeTime));
            yield return null;
        }

        ApplyAmbiance(target);
    }

    private void ApplyAmbiance(float k)
    {
        ambianceLevel = k;

        if (mainCamera != null)
            mainCamera.backgroundColor = Color.Lerp(lightBackgroundColor, darkBackgroundColor, k);

        if (darkVolume != null)
            darkVolume.weight = k;

        if (globalLight != null)
        {
            globalLight.color = Color.Lerp(lightColorOriginal, darkLightColor, k);
            globalLight.intensity = Mathf.Lerp(lightIntensityOriginal, darkLightIntensity, k);
        }

        if (backgroundSprites != null && originalSpriteColors != null)
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
    public void TriggerScreamer() => TriggerScreamer(0.2f);

    public void TriggerScreamer(float duration)
    {
        if (screamerPanel == null) return;
        if (screamerRoutine != null) StopCoroutine(screamerRoutine);
        screamerRoutine = StartCoroutine(ScreamerRoutine(duration));
    }

    private IEnumerator ScreamerRoutine(float duration)
    {
        if (screamerAudio != null) screamerAudio.Play();

        Color[] colors = (flashColors != null && flashColors.Length > 0)
            ? flashColors
            : new[] { Color.white };

        float step = Mathf.Max(0.01f, duration) / colors.Length;
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