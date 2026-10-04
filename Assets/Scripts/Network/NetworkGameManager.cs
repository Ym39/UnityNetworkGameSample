using System.Collections.Generic;
using EOS;
using Epic.OnlineServices;
using UnityEngine;

/// <summary>
/// Spawns a character per lobby member and routes P2P packets to the right one.
/// The character already placed in the scene is reused as the local player and as
/// the template every remote character is cloned from, so no prefab is required.
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    /// <summary>How often the lobby roster is re-read, in seconds.</summary>
    private const float RosterPollInterval = 1.0f;

    /// <summary>Spacing used to fan spawned characters out around the template.</summary>
    private const float SpawnSpread = 2.0f;

    public static NetworkGameManager Instance { get; private set; }

    [Tooltip("The character already present in the scene. Becomes the local player.")]
    [SerializeField]
    private CharacterController _characterTemplate;

    public NetworkPlayer LocalPlayer { get; private set; }

    private readonly Dictionary<ProductUserId, NetworkPlayer> _players = new();

    /// <summary>Last roster read from the lobby. Cached because every read copies
    /// and releases a native lobby handle, and Broadcast runs far more often.</summary>
    private List<ProductUserId> _members = new();

    private float _nextRosterPoll;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (EOSP2PManager.Instance != null)
        {
            EOSP2PManager.Instance.PacketReceived -= OnPacketReceived;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // EOSBootstrap creates the P2P manager during AfterSceneLoad, later than
        // this object's OnEnable, so subscribe as soon as it shows up.
        if (EOSP2PManager.Instance != null && LocalPlayer == null && EOSLoginManager.IsLoggedIn)
        {
            EOSP2PManager.Instance.PacketReceived -= OnPacketReceived;
            EOSP2PManager.Instance.PacketReceived += OnPacketReceived;
            SpawnLocalPlayer();
        }

        if (LocalPlayer == null || !EOSLobbyManager.IsInLobby || Time.time < _nextRosterPoll)
        {
            return;
        }

        _nextRosterPoll = Time.time + RosterPollInterval;
        SyncRoster();
    }

    /// <summary>
    /// Sends a packet to every other lobby member. EOS P2P has no broadcast, so
    /// this is a loop; traffic grows with the square of the player count.
    /// </summary>
    public void Broadcast(byte[] payload)
    {
        if (EOSP2PManager.Instance == null)
        {
            return;
        }

        ProductUserId self = EOSLoginManager.LocalProductUserId;

        foreach (ProductUserId member in _members)
        {
            if (member == self)
            {
                continue;
            }

            EOSP2PManager.Instance.Send(member, payload);
        }
    }

    private void SpawnLocalPlayer()
    {
        if (_characterTemplate == null)
        {
            Debug.LogError($"{nameof(NetworkGameManager)}: no character template assigned.");
            enabled = false;
            return;
        }

        ProductUserId self = EOSLoginManager.LocalProductUserId;

        LocalPlayer = Attach(_characterTemplate, self, isLocal: true);
        _players[self] = LocalPlayer;

        Debug.Log($"Network: local player spawned for {self}.");
    }

    private void SyncRoster()
    {
        List<ProductUserId> members = EOSLobbyManager.GetMembers();

        // A lobby we are in always lists at least ourselves, so an empty result
        // means the query failed rather than that everyone left. Acting on it would
        // wipe every character on screen.
        if (members.Count == 0)
        {
            return;
        }

        _members = members;

        foreach (ProductUserId member in members)
        {
            if (!_players.ContainsKey(member))
            {
                SpawnRemotePlayer(member);
            }
        }

        // Anyone no longer in the lobby left or timed out; drop their character.
        var departed = new List<ProductUserId>();
        foreach (KeyValuePair<ProductUserId, NetworkPlayer> entry in _players)
        {
            if (!members.Contains(entry.Key))
            {
                departed.Add(entry.Key);
            }
        }

        foreach (ProductUserId id in departed)
        {
            Despawn(id);
        }
    }

    private void SpawnRemotePlayer(ProductUserId owner)
    {
        Transform template = _characterTemplate.transform;

        // Fan the newcomers out so they do not all start inside each other. The
        // owner's first Sync packet corrects this within a fraction of a second.
        float angle = _players.Count * Mathf.PI * 0.5f;
        Vector3 offset = new(Mathf.Cos(angle) * SpawnSpread, 0.0f, Mathf.Sin(angle) * SpawnSpread);

        CharacterController clone = Instantiate(
            _characterTemplate, template.position + offset, template.rotation);
        clone.name = $"RemoteCharacter_{owner}";

        _players[owner] = Attach(clone, owner, isLocal: false);

        Debug.Log($"Network: remote player joined ({owner}).");
    }

    private void Despawn(ProductUserId owner)
    {
        if (!_players.TryGetValue(owner, out NetworkPlayer player))
        {
            return;
        }

        // Our own character is ours to keep: it stays put so the scene remains
        // playable even if we drop out of the lobby.
        if (player == LocalPlayer)
        {
            return;
        }

        _players.Remove(owner);

        if (player != null)
        {
            Destroy(player.gameObject);
        }

        Debug.Log($"Network: player left ({owner}).");
    }

    /// <summary>
    /// The scene character does not carry a NetworkPlayer, so it is added on the
    /// fly. Clones inherit it and only need re-initializing.
    /// </summary>
    private static NetworkPlayer Attach(CharacterController character, ProductUserId owner, bool isLocal)
    {
        if (!character.TryGetComponent(out NetworkPlayer player))
        {
            player = character.gameObject.AddComponent<NetworkPlayer>();
        }

        player.Initialize(owner, isLocal);
        return player;
    }

    private void OnPacketReceived(ProductUserId sender, byte[] data)
    {
        if (!_players.TryGetValue(sender, out NetworkPlayer player) || player == null)
        {
            // A packet can arrive before the roster poll notices the sender. Spawning
            // here would race with SyncRoster, so simply wait for the next Sync.
            return;
        }

        switch (NetworkMessage.ReadType(data))
        {
            case NetworkMessageType.Move:
                if (NetworkMessage.TryReadMove(data, out Vector2 target))
                {
                    player.ApplyMove(target);
                }
                break;

            case NetworkMessageType.Sync:
                if (NetworkMessage.TryReadSync(data, out Vector2 position, out float yaw))
                {
                    player.ApplySync(position, yaw);
                }
                break;
        }
    }
}
