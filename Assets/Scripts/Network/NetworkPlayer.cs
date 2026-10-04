using Epic.OnlineServices;
using UnityEngine;

/// <summary>
/// Network identity for one character. Every player owns exactly one of these and
/// is the only authority over it: the owner broadcasts what its character does and
/// everyone else replays it. Nobody validates anyone, which is fine for co-op play
/// but means a modified client can move its own character freely.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NetworkPlayer : MonoBehaviour
{
    /// <summary>Seconds between the owner's position corrections.</summary>
    private const float SyncInterval = 0.2f;

    /// <summary>Distance above which a correction is snapped instead of ignored.</summary>
    private const float SnapDistance = 1.5f;

    public ProductUserId Owner { get; private set; }

    public bool IsLocal { get; private set; }

    private CharacterController _character;
    private float _nextSyncTime;

    private void Awake()
    {
        _character = GetComponent<CharacterController>();
    }

    public void Initialize(ProductUserId owner, bool isLocal)
    {
        Owner = owner;
        IsLocal = isLocal;
        _nextSyncTime = 0.0f;
    }

    private void Update()
    {
        // Remote characters are driven entirely by incoming packets.
        if (!IsLocal || Time.time < _nextSyncTime)
        {
            return;
        }

        _nextSyncTime = Time.time + SyncInterval;

        // Root motion is not reproducible frame-for-frame across machines, so the
        // owner keeps nudging everyone back onto its own truth. This doubles as the
        // way a newly joined player learns where everybody is.
        NetworkGameManager.Instance?.Broadcast(
            NetworkMessage.WriteSync(transform.position, transform.eulerAngles.y));
    }

    /// <summary>Called when the local player clicks a destination.</summary>
    public void OrderMove(Vector3 target)
    {
        _character.OrderTargetPosition(target);

        // Send the order rather than the resulting motion: every peer then runs the
        // same turn-and-walk logic, which keeps the animation identical everywhere.
        NetworkGameManager.Instance?.Broadcast(NetworkMessage.WriteMove(target));
    }

    public void ApplyMove(Vector2 target)
    {
        _character.OrderTargetPosition(ToWorld(target));
    }

    public void ApplySync(Vector2 position, float yaw)
    {
        Vector3 world = ToWorld(position);

        // Small differences are left alone; correcting them every 200ms would make
        // remote characters visibly stutter. Only a real desync gets snapped.
        if ((world - transform.position).sqrMagnitude < SnapDistance * SnapDistance)
        {
            return;
        }

        _character.Teleport(world, Quaternion.Euler(0.0f, yaw, 0.0f));
    }

    /// <summary>Rebuilds a world position from a wire position, keeping our own height.</summary>
    private Vector3 ToWorld(Vector2 planar)
    {
        return new Vector3(planar.x, transform.position.y, planar.y);
    }
}
