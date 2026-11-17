using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float xOffset = 4f;   // le joueur reste à gauche de l'écran
    [SerializeField] private float smooth = 8f;

    private void LateUpdate()
    {
        if (target == null) return;
        float x = Mathf.Lerp(transform.position.x, target.position.x + xOffset,
                             smooth * Time.unscaledDeltaTime);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }
}