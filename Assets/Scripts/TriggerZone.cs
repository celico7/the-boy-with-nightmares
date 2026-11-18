using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Déclencheur invisible (BoxCollider2D en mode Trigger) placé à un métrage fixe.
/// Quand le joueur le traverse, il demande au TwistManager d'exécuter les effets choisis
/// (ambiance sombre, screamer, inversion, ralenti...) et lance un UnityEvent optionnel.
/// 
/// Utilisation : un GameObject vide + ce script, puis régler "Trigger At Meters" et les effets.
/// Après une mort, RespawnManager appelle TriggerZone.ResetAll() pour réarmer toutes les zones.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class TriggerZone : MonoBehaviour
{
    // Registre de toutes les zones actives, pour pouvoir les réarmer après un respawn.
    private static readonly List<TriggerZone> allZones = new List<TriggerZone>();

    [Header("Position")]
    [Tooltip("Métrage auquel la zone se trouve (ex : 200, 500, 800).")]
    [SerializeField] private float triggerAtMeters = 200f;
    [Tooltip("Si activé, la zone se place toute seule à ce métrage au lancement du jeu.")]
    [SerializeField] private bool placeAtMeters = true;

    [Header("Twist déclenché")]
    [SerializeField] private TwistEffects effects = TwistEffects.None;
    [Tooltip("Durée (secondes réelles) de l'inversion, du ralenti et de l'ambiance sombre.")]
    [SerializeField] private float twistDuration = 3f;
    [Range(0.1f, 1f)]
    [Tooltip("Valeur de Time.timeScale pendant le ralenti.")]
    [SerializeField] private float slowMotionScale = 0.35f;
    [Tooltip("Si activé, l'ambiance sombre disparaît après 'Twist Duration' secondes. Sinon elle reste.")]
    [SerializeField] private bool revertAmbiance = true;

    [Header("Options")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Pour brancher d'autres actions depuis l'inspecteur (son, dialogue, ...).")]
    [SerializeField] private UnityEvent onTriggered;

    private bool hasTriggered;

    /// <summary>Réarme toutes les zones (appelé après une mort).</summary>
    public static void ResetAll()
    {
        foreach (TriggerZone zone in allZones)
            zone.hasTriggered = false;
    }

    // Appelé quand on ajoute le composant : configure la zone correctement.
    private void Reset()
    {
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(1f, 30f); // haute pour attraper le joueur même en plein saut
    }

    private void Awake()
    {
        GetComponent<BoxCollider2D>().isTrigger = true; // sécurité
    }

    private void OnEnable() => allZones.Add(this);
    private void OnDisable() => allZones.Remove(this);

    private void Start()
    {
        if (placeAtMeters && GameManager.Instance != null)
        {
            Vector3 p = transform.position;
            p.x = GameManager.Instance.MetersToWorldX(triggerAtMeters);
            transform.position = p;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggerOnce && hasTriggered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        hasTriggered = true;

        if (GameManager.Instance != null)
            GameManager.Instance.NotifyZoneReached(name, triggerAtMeters);

        if (effects != TwistEffects.None && TwistManager.Instance != null)
            TwistManager.Instance.ExecuteTwist(effects, twistDuration, slowMotionScale, revertAmbiance);

        onTriggered?.Invoke();
    }

    // Visualisation dans la Scene view : la zone est invisible en jeu.
    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.DrawCube(box.bounds.center, box.bounds.size);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
        Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(box.bounds.center + Vector3.up * 2f,
            $"{triggerAtMeters} m  |  {effects}");
#endif
    }
}