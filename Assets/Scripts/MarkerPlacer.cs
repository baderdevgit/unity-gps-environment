using System.Collections.Generic;
using UnityEngine;

// Attach anywhere in the scene. Spawns a copy of Prefab at Target's current
// position whenever the mobile web UI's "Place Marker" button is pressed,
// and destroys every spawned copy on "Clear Markers".
public class MarkerPlacer : MonoBehaviour
{
    [SerializeField] private DataReceiver receiver;
    [SerializeField] private Transform target;
    [SerializeField] private GameObject prefab;

    private readonly List<GameObject> _placed = new List<GameObject>();

    private void Awake()
    {
        if (receiver == null)
            receiver = FindObjectOfType<DataReceiver>();
    }

    private void OnEnable()
    {
        if (receiver != null)
        {
            receiver.OnPlaceMarkerRequested += HandlePlace;
            receiver.OnClearMarkersRequested += HandleClear;
        }
    }

    private void OnDisable()
    {
        if (receiver != null)
        {
            receiver.OnPlaceMarkerRequested -= HandlePlace;
            receiver.OnClearMarkersRequested -= HandleClear;
        }
    }

    private void HandlePlace()
    {
        if (prefab == null || target == null)
        {
            Debug.LogWarning("MarkerPlacer: Prefab or Target not assigned, ignoring place request.");
            return;
        }

        var instance = Instantiate(prefab, target.position, target.rotation);
        _placed.Add(instance);
    }

    private void HandleClear()
    {
        foreach (var marker in _placed)
        {
            if (marker != null)
                Destroy(marker);
        }
        _placed.Clear();
    }
}
