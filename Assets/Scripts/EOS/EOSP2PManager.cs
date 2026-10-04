using System;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS
{
    /// <summary>
    /// Sends and receives bytes over an EOS P2P socket. Incoming connections on
    /// <see cref="SocketName"/> are accepted automatically, so the two sides only
    /// need to know each other's ProductUserId to start talking.
    /// </summary>
    public class EOSP2PManager : MonoBehaviour
    {
        /// <summary>Both peers must use the same socket name to reach each other.</summary>
        public const string SocketName = "GAME";

        private const byte Channel = 0;

        public static EOSP2PManager Instance { get; private set; }

        /// <summary>Raised on the main thread once per received packet.</summary>
        public event Action<ProductUserId, byte[]> PacketReceived;

        public bool IsListening => p2p != null;

        private P2PInterface p2p;
        private ulong connectionRequestId;
        private bool released;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            // Runs at the very top of EOSManager.OnShutdown, while the native SDK
            // is still loaded. EOSManager has no matching Remove for this, so the
            // callback guards itself against stale registrations.
            EOSManager.Instance.AddApplicationCloseListener(ReleaseHandle);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
        }

        private void Update()
        {
            // Once released, never touch the SDK again - the platform is going away.
            if (released)
            {
                return;
            }

            if (p2p == null)
            {
                TryStartListening();
                return;
            }

            ReceiveAll();
        }

#if UNITY_EDITOR
        private void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseHandle();
            }
        }
#endif

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif

            ReleaseHandle();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Unsubscribes and drops the handle. Must happen before EOSManager shuts
        /// the platform down: in the editor that unloads EOSSDK-Win64-Shipping.dll,
        /// and any call afterwards jumps into unmapped memory and kills the editor.
        /// Safe to call more than once.
        /// </summary>
        private void ReleaseHandle()
        {
            if (released)
            {
                return;
            }

            released = true;

            if (p2p != null && connectionRequestId != 0)
            {
                p2p.RemoveNotifyPeerConnectionRequest(connectionRequestId);
            }

            connectionRequestId = 0;
            p2p = null;
        }

        /// <summary>Sends <paramref name="data"/> to a peer, reliably and in order.</summary>
        public bool Send(ProductUserId remoteUserId, byte[] data)
        {
            if (p2p == null || remoteUserId == null || !remoteUserId.IsValid())
            {
                return false;
            }

            var options = new SendPacketOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                RemoteUserId = remoteUserId,
                SocketId = new SocketId { SocketName = SocketName },
                Channel = Channel,
                Data = new ArraySegment<byte>(data),
                AllowDelayedDelivery = true,
                Reliability = PacketReliability.ReliableOrdered
            };

            Result result = p2p.SendPacket(ref options);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS P2P: SendPacket failed ({result}).");
                return false;
            }

            return true;
        }

        public void Disconnect(ProductUserId remoteUserId)
        {
            if (p2p == null || remoteUserId == null)
            {
                return;
            }

            var options = new CloseConnectionOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                RemoteUserId = remoteUserId,
                SocketId = new SocketId { SocketName = SocketName }
            };

            p2p.CloseConnection(ref options);
        }

        // The P2P interface keys everything off the local ProductUserId, so this
        // can only run once the Connect login has completed.
        private void TryStartListening()
        {
            if (!EOSLoginManager.IsLoggedIn)
            {
                return;
            }

            PlatformInterface platform = EOSSDKManager.GetEOSPlatformInterface();
            if (platform == null)
            {
                return;
            }

            p2p = platform.GetP2PInterface();

            var options = new AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                SocketId = new SocketId { SocketName = SocketName }
            };

            connectionRequestId = p2p.AddNotifyPeerConnectionRequest(ref options, null, OnConnectionRequest);
            if (connectionRequestId == 0)
            {
                Debug.LogError("EOS P2P: could not subscribe to connection requests.");
                p2p = null;
                return;
            }

            Debug.Log($"EOS P2P: listening on socket '{SocketName}'.");
        }

        private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo info)
        {
            if (info.SocketId?.SocketName != SocketName)
            {
                return;
            }

            var options = new AcceptConnectionOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                RemoteUserId = info.RemoteUserId,
                SocketId = new SocketId { SocketName = SocketName }
            };

            Result result = p2p.AcceptConnection(ref options);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS P2P: AcceptConnection failed ({result}).");
                return;
            }

            Debug.Log($"EOS P2P: accepted a connection from {info.RemoteUserId}.");
        }

        // EOS buffers packets internally; drain the whole queue every frame so
        // that a burst does not spread out over several frames.
        private void ReceiveAll()
        {
            ProductUserId localUserId = EOSManager.Instance.GetProductUserId();
            if (localUserId == null || !localUserId.IsValid())
            {
                return;
            }

            var sizeOptions = new GetNextReceivedPacketSizeOptions
            {
                LocalUserId = localUserId,
                RequestedChannel = Channel
            };

            while (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint packetSize) == Result.Success
                   && packetSize > 0)
            {
                var receiveOptions = new ReceivePacketOptions
                {
                    LocalUserId = localUserId,
                    MaxDataSizeBytes = packetSize,
                    RequestedChannel = Channel
                };

                var buffer = new byte[packetSize];
                ProductUserId sender = null;
                SocketId socketId = default;

                Result result = p2p.ReceivePacket(ref receiveOptions, ref sender, ref socketId,
                    out byte _, new ArraySegment<byte>(buffer), out uint _);

                if (result != Result.Success)
                {
                    // NotFound simply means the queue drained between the two calls.
                    if (result != Result.NotFound)
                    {
                        Debug.LogError($"EOS P2P: ReceivePacket failed ({result}).");
                    }

                    return;
                }

                PacketReceived?.Invoke(sender, buffer);
            }
        }
    }
}
