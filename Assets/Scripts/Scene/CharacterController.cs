using UnityEngine;

[RequireComponent(typeof(Animator))]
public class CharacterController : MonoBehaviour
{
    public enum State
    {
        None = 0,
        Idle,
        Walk,
        Turn,
    }

    private static readonly int WalkParameter = Animator.StringToHash("Walk");

    [SerializeField]
    private State _state = State.Idle;

    [Tooltip("Seconds a full 180 degree turn takes. Smaller turns are scaled down from this.")]
    [SerializeField]
    private float _turningTime = 1.0f;

    [Tooltip("Heading change above which the character turns on the spot before walking.")]
    [SerializeField]
    private float _turnEnterAngle = 45.0f;

    [Tooltip("How close to the target counts as having arrived.")]
    [SerializeField]
    private float _arrivalDistance = 0.5f;

    [SerializeField]
    private Vector3 _targetPosition;

    [SerializeField]
    private float _currentTurnTime;

    private float _startTurnTime;
    private float _endTurnTime;

    private Vector3 _lookDirection;

    private Quaternion _prevRotation;
    private Quaternion _targetRotation;

    private Animator _animator;

    public State CurrentState => _state;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _targetPosition = transform.position;
        _lookDirection = transform.forward;
        _prevRotation = transform.rotation;
        _targetRotation = transform.rotation;
    }

    private void OnAnimatorMove()
    {
        // Root motion is applied in every state on purpose. Switching to Idle only
        // starts the blend out of the walk clip, and that blend still carries the
        // deceleration of the last footsteps - gating this on Walk would cut it off
        // and stop the character dead the frame it arrives.
        float distance = _animator.deltaPosition.magnitude;
        transform.position += _lookDirection * distance;

        if (_state == State.Walk && HasArrived())
        {
            SetState(State.Idle);
        }
    }

    /// <summary>
    /// Hard-sets the transform, discarding any order in progress. Used when a
    /// character is spawned and when a networked owner corrects a large drift.
    /// </summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);

        _targetPosition = position;
        _lookDirection = rotation * Vector3.forward;
        _prevRotation = rotation;
        _targetRotation = rotation;
        _currentTurnTime = 0.0f;
        _startTurnTime = Time.time;
        _endTurnTime = Time.time;

        SetState(State.Idle);
    }

    public void OrderTargetPosition(Vector3 position)
    {
        Vector3 currentPosition = transform.position;

        // The order comes from a ground raycast, so its height belongs to the ground,
        // not to the character. Using it as-is would sink the character or lift it.
        _targetPosition = new Vector3(position.x, currentPosition.y, position.z);

        Vector3 diff = _targetPosition - currentPosition;
        diff.y = 0.0f;

        if (diff.sqrMagnitude <= _arrivalDistance * _arrivalDistance)
        {
            // Clicking on the character's own feet should not make it walk.
            SetState(State.Idle);
            return;
        }

        _lookDirection = diff.normalized;
        _prevRotation = transform.rotation;
        _targetRotation = Quaternion.LookRotation(_lookDirection);

        float rotDiff = Quaternion.Angle(_prevRotation, _targetRotation);

        _startTurnTime = Time.time;
        _currentTurnTime = _turningTime * (rotDiff / 180.0f);
        _endTurnTime = _startTurnTime + _currentTurnTime;

        SetState(rotDiff > _turnEnterAngle ? State.Turn : State.Walk);
    }

    private void Update()
    {
        switch (_state)
        {
            case State.Turn:
                // Turn on the spot first: movement follows _lookDirection, so walking
                // before the model has turned would make it slide sideways.
                UpdateRotation();

                if (Time.time >= _endTurnTime)
                {
                    SetState(State.Walk);
                }
                break;

            case State.Walk:
                // Small heading changes are absorbed while already walking.
                UpdateRotation();
                break;
        }
    }

    private bool HasArrived()
    {
        Vector3 diff = _targetPosition - transform.position;
        diff.y = 0.0f;

        if (diff.sqrMagnitude <= _arrivalDistance * _arrivalDistance)
        {
            return true;
        }

        // A single root motion step can jump past the target. Treat that as arrival,
        // otherwise the character turns around and oscillates around the point.
        return Vector3.Dot(diff, _lookDirection) < 0.0f;
    }

    private void UpdateRotation()
    {
        // A turn of zero degrees gives a zero duration, which would divide by zero.
        if (_currentTurnTime <= 0.0f)
        {
            transform.rotation = _targetRotation;
            return;
        }

        float elapsed = Time.time - _startTurnTime;
        float turningRatio = Mathf.Clamp01(elapsed / _currentTurnTime);
        transform.rotation = Quaternion.Slerp(_prevRotation, _targetRotation, turningRatio);
    }

    private void SetState(State state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _animator.SetBool(WalkParameter, state == State.Walk);
    }
}
