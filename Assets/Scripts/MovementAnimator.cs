using UnityEngine;

// Attach alongside an Animator (e.g. on a Mixamo character) to automatically
// switch between Idle, Walk, and Run states based on whether this object's
// position is actually changing frame to frame. Works with any mover
// (GpsPositioner, manual movement, etc.) since it only watches the Transform
// - no coupling to GPS specifics.
[RequireComponent(typeof(Animator))]
public class MovementAnimator : MonoBehaviour
{
    [Tooltip("Name of the Animator bool parameter to set true while moving " +
             "at all (walk or run) - must match a parameter on the Animator Controller.")]
    [SerializeField] private string isMovingParameter = "IsMoving";

    [Tooltip("Name of the Animator bool parameter to set true while moving " +
             "fast enough to run - must match a parameter on the Animator Controller.")]
    [SerializeField] private string isRunningParameter = "IsRunning";

    [Tooltip("Minimum meters/second before switching from Idle to Walk - " +
             "filters out tiny jitter while stationary.")]
    [SerializeField] private float walkThreshold = 0.01f;

    [Tooltip("Minimum meters/second before switching from Walk to Run.")]
    [SerializeField] private float runThreshold = 0.1f;

    private Animator _animator;
    private Vector3 _lastPosition;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _lastPosition = transform.position;
    }

    private void Update()
    {
        float speed = Time.deltaTime > 0f
            ? (transform.position - _lastPosition).magnitude / Time.deltaTime
            : 0f;
        _lastPosition = transform.position;

        _animator.SetBool(isMovingParameter, speed > walkThreshold);
        _animator.SetBool(isRunningParameter, speed > runThreshold);
    }
}
