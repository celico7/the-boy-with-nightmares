using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gère la mort et le respawn au DÉBUT du niveau (pas de checkpoint).
/// Séquence : le joueur est figé, courte pause, fondu au noir, remise à zéro du monde
/// (twists, triggers, compteur de mètres, joueur), puis fondu inverse et reprise.
/// </summary>
public class RespawnManager : MonoBehaviour
{
    public static RespawnManager Instance { get; private set; }

    [Header("Références")]
    [SerializeField] private PlayerController player;
    [Tooltip("Optionnel : point de respawn. Sinon, la position du joueur au lancement de la scène.")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Optionnel : Image UI noire plein écran (Raycast Target désactivé) pour le fondu.")]
    [SerializeField] private Image fadePanel;

    [Header("Timing")]
    [Tooltip("Pause (s) entre la mort et le début du fondu au noir.")]
    [SerializeField] private float deathPause = 0.6f;
    [Tooltip("Durée (s) de chaque fondu (vers le noir, puis retour).")]
    [SerializeField] private float fadeDuration = 0.4f;

    [Header("Chute dans le vide")]
    [SerializeField] private bool killBelowY = true;
    [Tooltip("Si le joueur descend sous cette hauteur, il meurt.")]
    [SerializeField] private float killY = -15f;

    public bool IsDead { get; private set; }
    public int Deaths { get; private set; }

    public event Action OnPlayerDied;
    public event Action OnPlayerRespawned;

    private Vector3 spawnPosition;
    private Rigidbody2D playerBody;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (player == null)
        {
            Debug.LogError("[RespawnManager] Le champ 'Player' n'est pas assigné.", this);
            enabled = false;
            return;
        }

        spawnPosition = spawnPoint != null ? spawnPoint.position : player.transform.position;
        playerBody = player.GetComponent<Rigidbody2D>();

        if (fadePanel != null) fadePanel.color = Color.clear;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsDead && killBelowY && player.transform.position.y < killY)
            Kill();
    }

    /// <summary>Tue le joueur et lance la séquence de respawn. Sans effet s'il est déjà mort.</summary>
    public void Kill()
    {
        if (IsDead) return;

        IsDead = true;
        Deaths++;
        OnPlayerDied?.Invoke();
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        player.SetFrozen(true);

        // Les durées sont en temps réel : elles restent correctes même si le jeu était en ralenti.
        yield return new WaitForSecondsRealtime(deathPause);
        yield return Fade(0f, 1f);

        // --- Écran noir : on remet tout à zéro ---
        if (TwistManager.Instance != null) TwistManager.Instance.ResetAll();
        TriggerZone.ResetAll();
        if (GameManager.Instance != null) GameManager.Instance.ResetRun();

        player.ResetState();
        player.transform.position = spawnPosition;
        playerBody.position = spawnPosition;
        Physics2D.SyncTransforms();

        yield return null; // laisse une frame à Unity pour appliquer la nouvelle position

        yield return Fade(1f, 0f);

        player.SetFrozen(false);
        IsDead = false;
        OnPlayerRespawned?.Invoke();
    }

    private IEnumerator Fade(float from, float to)
    {
        if (fadePanel == null || fadeDuration <= 0f) yield break;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            SetFadeAlpha(Mathf.Lerp(from, to, t / fadeDuration));
            yield return null;
        }
        SetFadeAlpha(to);
    }

    private void SetFadeAlpha(float a)
    {
        fadePanel.color = new Color(0f, 0f, 0f, a);
    }
}
