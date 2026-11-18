using UnityEngine;

/// <summary>
/// Zone mortelle : tue le joueur au contact (pics, ennemi, vide, piège...).
/// Attache-la à un GameObject avec un Collider2D en mode Is Trigger.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DeathZone : MonoBehaviour
{
    // Appelé quand on ajoute le composant dans l'éditeur.
    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;

        if (RespawnManager.Instance != null)
            RespawnManager.Instance.Kill();
    }

    // Visualisation dans la Scene view.
    private void OnDrawGizmos()
    {
        var c = GetComponent<Collider2D>();
        if (c == null) return;

        Gizmos.color = new Color(0.6f, 0f, 0.8f, 0.3f);
        Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        Gizmos.color = new Color(0.6f, 0f, 0.8f, 1f);
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
