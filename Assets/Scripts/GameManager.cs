using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Gère la progression du niveau linéaire :
/// - calcule la distance parcourue (en mètres) à partir de la position X du joueur ;
/// - met à jour le texte HUD (en haut à gauche) ;
/// - convertit "X mètres" en position monde pour placer les TriggerZone ;
/// - relaie les événements quand une zone est atteinte.
/// 
/// 1 unité Unity = 1 mètre par défaut (modifiable via unitsPerMeter).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Références")]
    [Tooltip("Le Transform du joueur (sert à mesurer la distance).")]
    [SerializeField] private Transform player;
    [Tooltip("Texte TextMeshPro affiché en haut à gauche.")]
    [SerializeField] private TMP_Text distanceText;

    [Header("Distance")]
    [Tooltip("Combien d'unités Unity valent 1 mètre.")]
    [SerializeField] private float unitsPerMeter = 1f;
    [SerializeField] private string distanceFormat = "{0} m";

    /// <summary>Distance maximale atteinte, en mètres (ne diminue jamais).</summary>
    public float DistanceMeters { get; private set; }

    /// <summary>Déclenché quand la distance change (valeur en mètres).</summary>
    public event Action<float> OnDistanceChanged;

    /// <summary>Déclenché quand le joueur entre dans une TriggerZone (nom, mètres).</summary>
    public event Action<string, float> OnZoneReached;

    private float startX;
    private int displayedMeters = -1;

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
            Debug.LogError("[GameManager] Le champ 'Player' n'est pas assigné.", this);
            enabled = false;
            return;
        }

        // Point de départ = position du joueur au lancement de la scène.
        startX = player.position.x;
        RefreshUI(0);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        float meters = (player.position.x - startX) / unitsPerMeter;

        // On garde la distance maximale : reculer ne fait pas baisser le score.
        if (meters > DistanceMeters)
        {
            DistanceMeters = meters;
            OnDistanceChanged?.Invoke(DistanceMeters);

            int whole = Mathf.FloorToInt(DistanceMeters);
            if (whole != displayedMeters)
                RefreshUI(whole);
        }
    }

    private void RefreshUI(int meters)
    {
        displayedMeters = meters;
        if (distanceText != null)
            distanceText.text = string.Format(distanceFormat, meters);
    }

    /// <summary>Remet le compteur à zéro (appelé après un respawn).</summary>
    public void ResetRun()
    {
        DistanceMeters = 0f;
        RefreshUI(0);
        OnDistanceChanged?.Invoke(0f);
    }

    /// <summary>Convertit une distance en mètres en position X dans le monde.</summary>
    public float MetersToWorldX(float meters)
    {
        return startX + meters * unitsPerMeter;
    }

    /// <summary>Appelé par les TriggerZone.</summary>
    public void NotifyZoneReached(string zoneName, float atMeters)
    {
        Debug.Log($"[GameManager] Zone '{zoneName}' atteinte à {atMeters} m.");
        OnZoneReached?.Invoke(zoneName, atMeters);
    }
}