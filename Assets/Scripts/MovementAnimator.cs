using UnityEngine;

// Attach alongside an Animator (e.g. on a Mixamo character) to automatically
// switch between an Idle and Walk state based on whether this object's
// position is actually changing frame to frame. Works with any mover
// (GpsPositioner, manual movement, etc.) since it only watches the Transform
// - no coupling to GPS specifics.
[RequireComponent(typeof(Animator))]
public class MovementAnimator : MonoBehaviour
{
    [Tooltip("Name of the Animator bool parameter to set true while moving " +
             "(must match a parameter you've added to the Animator Controller).")]
    [SerializeField] private string isMovingParameter = "IsMoving";

    [Tooltip("Minimum meters/second of actual movement before switching to " +
             "the walk animation - filters out tiny jitter while stationary.")]
    [SerializeField] private float movementThreshold = 0.05f;

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

        _animator.SetBool(isMovingParameter, speed > movementThreshold);
    }
}
