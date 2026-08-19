using System.Collections.Generic;
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

    [Tooltip("Renders position slightly in the past, interpolating between " +
             "the last two real fixes, instead of chasing the newest one. " +
             "Trades a small fixed delay for motion with zero prediction/" +
             "estimation error - no drift or 'chasing' artifacts.")]
    [SerializeField] private float positionInterpolationDelay = 1f;

    [Tooltip("Use fix.alt for height instead of keeping the object's current Y.")]
    [SerializeField] private bool useAltitude = false;

    [Tooltip("Rotate to face the Pi's IMU-derived heading. Rotation only - " +
             "position still comes purely from GPS fixes, no dead-reckoning.")]
    [SerializeField] private bool useHeadingRotation = true;

    [Tooltip("How quickly the GameObject turns to face the new heading.")]
    [SerializeField] private float rotateSpeed = 10f;

    [Tooltip("Optional. On reset, this Transform's rotation is set to whichever " +
             "way the player was actually facing (real-world heading) at that " +
             "moment - e.g. a floor/grid object - so the scene's 'forward' " +
             "lines up with the player's real facing direction instead of " +
             "always being locked to true compass North.")]
    [SerializeField] private Transform gridTransform;

    private const double MetersPerDegreeLat = 111320.0;
    private const int MaxBufferedSamples = 30;

    private struct PositionSample
    {
        public float time;
        public Vector3 position;
    }

    private bool _originSet;
    private double _originAlt;
    private float _headingDeg;             // compass heading, 0 = true North - drives the object's own facing directly, never reframed
    private float _worldRotationOffsetDeg; // set at reset time from _headingDeg; used only to reframe POSITION, not facing
    private readonly List<PositionSample> _positionBuffer = new List<PositionSample>();

    private void Awake()
    {
        if (receiver == null)
            receiver = FindObjectOfType<DataReceiver>();

        _positionBuffer.Add(new PositionSample { time = Time.time, position = transform.position });

        if (!useFirstFixAsOrigin)
            _originSet = true; // origin already supplied via inspector fields
    }

    private void OnEnable()
    {
        if (receiver != null)
        {
            receiver.OnGpsFixReceived += HandleFix;
            receiver.OnResetRequested += HandleReset;
            receiver.OnImuHeadingReceived += HandleImuHeading;
        }
    }

    private void OnDisable()
    {
        if (receiver != null)
        {
            receiver.OnGpsFixReceived -= HandleFix;
            receiver.OnResetRequested -= HandleReset;
            receiver.OnImuHeadingReceived -= HandleImuHeading;
        }
    }

    // Updates heading at ~10Hz (independent of the ~1Hz GPS fix rate) so
    // rotation tracks the IMU closely; HandleFix below still periodically
    // overwrites this with the GPS-corrected value to prevent gyro drift.
    private void HandleImuHeading(double heading)
    {
        if (useHeadingRotation)
            _headingDeg = (float)heading;
    }

    // Re-anchors the origin to wherever the next fix comes in, and snaps
    // immediately back to (0,0,0) rather than gliding there. Clears the
    // position buffer too, since old samples are in the pre-reset coordinate
    // space and would otherwise blend weirdly across the reset instant.
    //
    // Also captures whatever direction the player was actually facing
    // (real-world compass heading) at this exact moment as the new "forward"
    // for POSITION only - HandleFix below rotates future position offsets
    // into this frame. The player's own facing rotation is left alone: it's
    // an absolute world-space quantity (0=North) that's already correct
    // regardless of any reset, so it must not be touched here. gridTransform
    // (if assigned) is rotated to match so the visual floor/grid lines up.
    private void HandleReset()
    {
        Debug.Log("GpsPositioner: reset received, re-anchoring origin and rotation.");
        _originSet = false;
        _worldRotationOffsetDeg = _headingDeg;

        Vector3 resetPosition = new Vector3(0, transform.position.y, 0);
        _positionBuffer.Clear();
        _positionBuffer.Add(new PositionSample { time = Time.time, position = resetPosition });
        transform.position = resetPosition;

        if (gridTransform != null)
            gridTransform.rotation = Quaternion.Euler(0, _worldRotationOffsetDeg, 0);
    }

    private float _lastFixProcessedAt = -1f;

    private void HandleFix(GpsData fix)
    {
        // Gap between fixes as GpsPositioner itself processes them, on
        // Unity's own clock - if this is clean while position still looked
        // stuck on screen, the bug is downstream of here (interpolation/
        // rendering), not in message delivery, which every other [PERF]
        // check so far has already covered.
        if (_lastFixProcessedAt > 0f && Time.time - _lastFixProcessedAt > 2f)
            Debug.LogWarning($"[PERF] GpsPositioner: {Time.time - _lastFixProcessedAt:F1}s since previous fix was processed.");
        _lastFixProcessedAt = Time.time;

        if (!_originSet)
        {
            originLat = fix.lat;
            originLon = fix.lon;
            _originAlt = fix.alt;
            _originSet = true;
        }

        double metersPerDegreeLon = MetersPerDegreeLat * Mathf.Cos((float)(originLat * Mathf.Deg2Rad));

        float xAbs = (float)((fix.lon - originLon) * metersPerDegreeLon);
        float zAbs = (float)((fix.lat - originLat) * MetersPerDegreeLat);
        float y = useAltitude ? (float)(fix.alt - _originAlt) : transform.position.y;

        // Rotate the absolute (compass-aligned) offset into the current
        // reset frame, so position stays consistent with whatever direction
        // was set as "forward" at the last reset.
        Vector3 rotated = Quaternion.Euler(0, -_worldRotationOffsetDeg, 0) * new Vector3(xAbs, y, zAbs);

        _positionBuffer.Add(new PositionSample { time = Time.time, position = rotated });
        if (_positionBuffer.Count > MaxBufferedSamples)
            _positionBuffer.RemoveAt(0);

        if (useHeadingRotation)
            _headingDeg = (float)fix.heading;
    }

    private Vector3 ComputeInterpolatedPosition()
    {
        if (_positionBuffer.Count == 0)
            return transform.position;

        float renderTime = Time.time - positionInterpolationDelay;

        // Before the earliest sample, or only one sample so far: hold there.
        if (_positionBuffer.Count == 1 || renderTime <= _positionBuffer[0].time)
            return _positionBuffer[0].position;

        var last = _positionBuffer[_positionBuffer.Count - 1];

        // After the latest sample: hold at the latest known position rather
        // than extrapolate - a stale fix means we genuinely don't know where
        // it is next, so guessing isn't worth the risk (learned that already).
        if (renderTime >= last.time)
            return last.position;

        for (int i = 0; i < _positionBuffer.Count - 1; i++)
        {
            var a = _positionBuffer[i];
            var b = _positionBuffer[i + 1];
            if (renderTime >= a.time && renderTime <= b.time)
            {
                float t = b.time > a.time ? (renderTime - a.time) / (b.time - a.time) : 0f;
                return Vector3.Lerp(a.position, b.position, t);
            }
        }

        return last.position;
    }

    private void Update()
    {
        if (useHeadingRotation)
        {
            Quaternion targetRotation = Quaternion.Euler(0, _headingDeg, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }

        transform.position = ComputeInterpolatedPosition();
    }
}
