using UnityEngine;

// Attach to the GameObject you want to move. Requires a
// DataReceiver somewhere in the scene.
//
// Converts incoming lat/lon into a local X/Z offset (in meters) from a
// reference point, using a flat-earth approximation - accurate enough for
// distances up to a few km, which covers typical drone/rover ranges.
public class GpsPositioner : MonoBehaviour
{
    [SerializeField] private DataReceiver receiver;

    [Tooltip("If true, the first fix received becomes the origin (0,0,0). " +
             "If false, originLat/originLon below are used instead.")]
    [SerializeField] private bool useFirstFixAsOrigin = true;
    [SerializeField] private double originLat;
    [SerializeField] private double originLon;

    [Tooltip("How quickly the GameObject glides to each new fix, since GPS " +
             "updates arrive only ~1x/second and snapping looks jerky.")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("Use fix.alt for height instead of keeping the object's current Y.")]
    [SerializeField] private bool useAltitude = false;

    private const double MetersPerDegreeLat = 111320.0;

    private bool _originSet;
    private double _originAlt;
    private Vector3 _targetPosition;

    private void Awake()
    {
        if (receiver == null)
            receiver = FindObjectOfType<DataReceiver>();

        _targetPosition = transform.position;

        if (!useFirstFixAsOrigin)
            _originSet = true; // origin already supplied via inspector fields
    }

    private void OnEnable()
    {
        if (receiver != null)
        {
            receiver.OnGpsFixReceived += HandleFix;
            receiver.OnResetRequested += HandleReset;
        }
    }

    private void OnDisable()
    {
        if (receiver != null)
        {
            receiver.OnGpsFixReceived -= HandleFix;
            receiver.OnResetRequested -= HandleReset;
        }
    }

    // Re-anchors the origin to wherever the next fix comes in, and snaps
    // immediately back to (0,0,0) rather than gliding there.
    private void HandleReset()
    {
        Debug.Log("GpsPositioner: reset received, re-anchoring origin.");
        _originSet = false;
        _targetPosition = new Vector3(0, transform.position.y, 0);
        transform.position = _targetPosition;
    }

    private void HandleFix(GpsData fix)
    {
        if (!_originSet)
        {
            originLat = fix.lat;
            originLon = fix.lon;
            _originAlt = fix.alt;
            _originSet = true;
        }

        double metersPerDegreeLon = MetersPerDegreeLat * Mathf.Cos((float)(originLat * Mathf.Deg2Rad));

        float x = (float)((fix.lon - originLon) * metersPerDegreeLon);
        float z = (float)((fix.lat - originLat) * MetersPerDegreeLat);
        float y = useAltitude ? (float)(fix.alt - _originAlt) : transform.position.y;

        _targetPosition = new Vector3(x, y, z);
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, _targetPosition, moveSpeed * Time.deltaTime);
    }
}
