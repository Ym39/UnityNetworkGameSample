using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using UnityEngine;

namespace EOS.Samples
{
    /// <summary>
    /// Runnable walkthrough of the whole flow: log in, open or find a lobby,
    /// then exchange P2P packets with everyone in it. Drop this on any GameObject
    /// in the scene and press Play; the controls are drawn with IMGUI so no UI
    /// setup is needed.
    /// </summary>
    public class EOSMatchSample : MonoBehaviour
    {
        [SerializeField] private string gameMode = "DEFAULT";
        [SerializeField] private uint maxPlayers = 4;

        private readonly List<string> log = new();
        private List<LobbyDetails> searchResults;
        private string status = "Not logged in";
        private bool subscribed;

        // EOSBootstrap creates the P2P manager during AfterSceneLoad, which is
        // later than this object's OnEnable, so subscribe once it shows up.
        private void Update()
        {
            if (subscribed || EOSP2PManager.Instance == null)
            {
                return;
            }

            EOSP2PManager.Instance.PacketReceived += OnPacketReceived;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (EOSP2PManager.Instance != null)
            {
                EOSP2PManager.Instance.PacketReceived -= OnPacketReceived;
            }

            subscribed = false;
            EOSLobbyManager.ReleaseResults(searchResults);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20), GUI.skin.box);

            GUILayout.Label($"<b>Status:</b> {status}", RichLabel);
            GUILayout.Label($"<b>User:</b> {EOSLoginManager.LocalProductUserId}", RichLabel);
            GUILayout.Label($"<b>Lobby:</b> {EOSLobbyManager.CurrentLobbyId ?? "-"}", RichLabel);
            GUILayout.Space(8);

            if (!EOSLoginManager.IsLoggedIn)
            {
                if (GUILayout.Button("1. Login (Device ID)"))
                {
                    status = "Logging in...";
                    EOSLoginManager.LoginWithDeviceId(SystemInfo.deviceName, result =>
                    {
                        status = result == Result.Success ? "Logged in" : $"Login failed: {result}";
                        Append($"Login: {result}");
                    });
                }

                GUILayout.EndArea();
                return;
            }

            DrawLobbyControls();
            DrawP2PControls();
            DrawSessionControls();
            DrawLog();

            GUILayout.EndArea();
        }

        private void DrawLobbyControls()
        {
            GUILayout.Label("<b>Lobby</b>", RichLabel);

            if (!EOSLobbyManager.IsInLobby)
            {
                if (GUILayout.Button("2a. Create lobby (host)"))
                {
                    EOSLobbyManager.Create(gameMode, maxPlayers, result => Append($"Create lobby: {result}"));
                }

                if (GUILayout.Button("2b. Search lobbies (guest)"))
                {
                    EOSLobbyManager.ReleaseResults(searchResults);
                    EOSLobbyManager.Search(gameMode, 10, (result, results) =>
                    {
                        searchResults = results;
                        Append($"Search: {result}, {results?.Count ?? 0} found");
                    });
                }

                if (searchResults != null)
                {
                    for (int i = 0; i < searchResults.Count; i++)
                    {
                        if (GUILayout.Button($"    Join result #{i}"))
                        {
                            LobbyDetails chosen = searchResults[i];
                            // Hand ownership of this handle to Join, release the rest.
                            searchResults.RemoveAt(i);
                            EOSLobbyManager.ReleaseResults(searchResults);
                            searchResults = null;

                            EOSLobbyManager.Join(chosen, result =>
                            {
                                Append($"Join lobby: {result}");
                                chosen.Release();
                            });
                            break;
                        }
                    }
                }
            }
            else
            {
                foreach (ProductUserId member in EOSLobbyManager.GetMembers())
                {
                    GUILayout.Label($"    member: {member}");
                }

                if (GUILayout.Button("Leave lobby"))
                {
                    EOSLobbyManager.Leave(result => Append($"Leave: {result}"));
                }

                if (GUILayout.Button("Destroy lobby (host only)"))
                {
                    EOSLobbyManager.Destroy(result => Append($"Destroy: {result}"));
                }
            }

            GUILayout.Space(8);
        }

        private void DrawP2PControls()
        {
            GUILayout.Label("<b>P2P</b>", RichLabel);

            bool listening = EOSP2PManager.Instance != null && EOSP2PManager.Instance.IsListening;
            GUILayout.Label($"    socket '{EOSP2PManager.SocketName}': {(listening ? "listening" : "not ready")}");

            if (EOSLobbyManager.IsInLobby && GUILayout.Button("3. Say hello to everyone"))
            {
                SendToLobby($"hello from {EOSLoginManager.LocalProductUserId}");
            }

            GUILayout.Space(8);
        }

        private void DrawSessionControls()
        {
            GUILayout.Label("<b>Session</b>", RichLabel);

            if (!EOSSessionManager.HasSession)
            {
                if (GUILayout.Button("4. Create session"))
                {
                    EOSSessionManager.Create(gameMode, maxPlayers, result => Append($"Create session: {result}"));
                }
            }
            else
            {
                if (GUILayout.Button("Start session (match begins)"))
                {
                    EOSSessionManager.Start(result => Append($"Start session: {result}"));
                }

                if (GUILayout.Button("End session (match over)"))
                {
                    EOSSessionManager.End(result => Append($"End session: {result}"));
                }

                if (GUILayout.Button("Destroy session"))
                {
                    EOSSessionManager.Destroy(result => Append($"Destroy session: {result}"));
                }
            }

            GUILayout.Space(8);
        }

        private void DrawLog()
        {
            GUILayout.Label("<b>Log</b>", RichLabel);
            for (int i = log.Count - 1; i >= 0; i--)
            {
                GUILayout.Label($"    {log[i]}");
            }
        }

        /// <summary>
        /// P2P is addressed per peer, so a "broadcast" is just a send to every
        /// lobby member except yourself.
        /// </summary>
        private void SendToLobby(string message)
        {
            if (EOSP2PManager.Instance == null)
            {
                return;
            }

            byte[] payload = Encoding.UTF8.GetBytes(message);
            ProductUserId self = EOSLoginManager.LocalProductUserId;

            foreach (ProductUserId member in EOSLobbyManager.GetMembers())
            {
                if (member == self)
                {
                    continue;
                }

                EOSP2PManager.Instance.Send(member, payload);
            }

            Append($"sent: {message}");
        }

        private void OnPacketReceived(ProductUserId sender, byte[] data)
        {
            Append($"recv from {sender}: {Encoding.UTF8.GetString(data)}");
        }

        private void Append(string line)
        {
            log.Add(line);
            if (log.Count > 12)
            {
                log.RemoveAt(0);
            }

            Debug.Log($"EOSMatchSample: {line}");
        }

        private static GUIStyle RichLabel => new(GUI.skin.label) { richText = true };
    }
}
