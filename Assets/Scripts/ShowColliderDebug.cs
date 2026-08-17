using UnityEngine;

public class ShowColliderDebug : MonoBehaviour
{
    private Collider col;

    void Start() {
        col = GetComponent<Collider>(); // Use Collider2D if 2D
    }

    void Update() {
        if (col != null) {
            Bounds b = col.bounds;
            // Draw box outline representing collider bounds every frame
            Debug.DrawLine(b.min, new Vector3(b.max.x, b.min.y, b.min.z), Color.green);
            Debug.DrawLine(b.min, new Vector3(b.min.x, b.max.y, b.min.z), Color.green);
            // (Expand with remaining bounds lines as needed)
        }
    }
}