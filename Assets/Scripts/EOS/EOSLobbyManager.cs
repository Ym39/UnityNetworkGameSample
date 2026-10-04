using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS
{
    /// <summary>
    /// Lobbies are the matchmaking room players sit in before a match starts.
    /// EOS hosts them, so no dedicated server is involved: one player owns the
    /// lobby and the rest find it through <see cref="Search"/>.
    /// </summary>
    public static class EOSLobbyManager
    {
        /// <summary>Attribute the search filters on. Must be advertised publicly.</summary>
        private const string GameModeKey = "GAMEMODE";

        /// <summary>Id of the lobby this client is currently in, or null.</summary>
        public static string CurrentLobbyId { get; private set; }

        public static bool IsInLobby => !string.IsNullOrEmpty(CurrentLobbyId);

        // This project disables domain reload when entering play mode, so statics
        // survive from the previous session and would report a lobby that is long
        // gone. Unity calls this before the first scene loads on every play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentLobbyId = null;
        }

        private static LobbyInterface Lobby => EOSSDKManager.GetEOSPlatformInterface()?.GetLobbyInterface();

        /// <summary>
        /// Creates a publicly advertised lobby and tags it with
        /// <paramref name="gameMode"/> so that <see cref="Search"/> can find it.
        /// </summary>
        public static void Create(string gameMode, uint maxMembers, Action<Result> onComplete = null)
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null)
            {
                onComplete?.Invoke(Result.NotConfigured);
                return;
            }

            var options = new CreateLobbyOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                MaxLobbyMembers = maxMembers,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                BucketId = gameMode,
                PresenceEnabled = true,
                AllowInvites = true,
                DisableHostMigration = false
            };

            lobby.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Lobby: CreateLobby failed ({info.ResultCode}).");
                    onComplete?.Invoke(info.ResultCode);
                    return;
                }

                CurrentLobbyId = info.LobbyId;
                Debug.Log($"EOS Lobby: created {CurrentLobbyId}.");

                // The bucket id alone is not searchable, so publish the game mode
                // as an attribute too.
                AddGameModeAttribute(gameMode, onComplete);
            });
        }

        /// <summary>
        /// Finds lobbies tagged with <paramref name="gameMode"/>. The returned
        /// handles are owned by the caller and must be passed to
        /// <see cref="Join"/> or released with <see cref="ReleaseResults"/>.
        /// </summary>
        public static void Search(string gameMode, uint maxResults, Action<Result, List<LobbyDetails>> onComplete)
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null)
            {
                onComplete?.Invoke(Result.NotConfigured, null);
                return;
            }

            var searchOptions = new CreateLobbySearchOptions { MaxResults = maxResults };
            Result result = lobby.CreateLobbySearch(ref searchOptions, out LobbySearch search);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Lobby: CreateLobbySearch failed ({result}).");
                onComplete?.Invoke(result, null);
                return;
            }

            var parameterOptions = new LobbySearchSetParameterOptions
            {
                ComparisonOp = ComparisonOp.Equal,
                Parameter = new AttributeData
                {
                    Key = GameModeKey,
                    Value = gameMode
                }
            };

            result = search.SetParameter(ref parameterOptions);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Lobby: SetParameter failed ({result}).");
                search.Release();
                onComplete?.Invoke(result, null);
                return;
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId()
            };

            search.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Lobby: Find failed ({info.ResultCode}).");
                    search.Release();
                    onComplete?.Invoke(info.ResultCode, null);
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOptions);

                var results = new List<LobbyDetails>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (search.CopySearchResultByIndex(ref copyOptions, out LobbyDetails details) == Result.Success)
                    {
                        results.Add(details);
                    }
                }

                // The search handle is done; the copied detail handles outlive it.
                search.Release();

                Debug.Log($"EOS Lobby: found {results.Count} lobbies for '{gameMode}'.");
                onComplete?.Invoke(Result.Success, results);
            });
        }

        /// <summary>Joins a lobby found by <see cref="Search"/>.</summary>
        public static void Join(LobbyDetails details, Action<Result> onComplete = null)
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null || details == null)
            {
                onComplete?.Invoke(Result.NotConfigured);
                return;
            }

            var options = new JoinLobbyOptions
            {
                LobbyDetailsHandle = details,
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                PresenceEnabled = true
            };

            lobby.JoinLobby(ref options, null, (ref JoinLobbyCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    CurrentLobbyId = info.LobbyId;
                    Debug.Log($"EOS Lobby: joined {CurrentLobbyId}.");
                }
                else
                {
                    Debug.LogError($"EOS Lobby: JoinLobby failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>Leaves the current lobby. The owner should use <see cref="Destroy"/>.</summary>
        public static void Leave(Action<Result> onComplete = null)
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null || !IsInLobby)
            {
                onComplete?.Invoke(Result.NotFound);
                return;
            }

            var options = new LeaveLobbyOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                LobbyId = CurrentLobbyId
            };

            lobby.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    CurrentLobbyId = null;
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>Closes the lobby for everyone. Only the owner may call this.</summary>
        public static void Destroy(Action<Result> onComplete = null)
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null || !IsInLobby)
            {
                onComplete?.Invoke(Result.NotFound);
                return;
            }

            var options = new DestroyLobbyOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                LobbyId = CurrentLobbyId
            };

            lobby.DestroyLobby(ref options, null, (ref DestroyLobbyCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    CurrentLobbyId = null;
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>
        /// Returns everyone currently in the lobby, owner first. Use this to get
        /// the ProductUserIds to open P2P connections to.
        /// </summary>
        public static List<ProductUserId> GetMembers()
        {
            var members = new List<ProductUserId>();

            LobbyInterface lobby = Lobby;
            if (lobby == null || !IsInLobby)
            {
                return members;
            }

            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = EOSManager.Instance.GetProductUserId()
            };

            if (lobby.CopyLobbyDetailsHandle(ref copyOptions, out LobbyDetails details) != Result.Success)
            {
                return members;
            }

            var countOptions = new LobbyDetailsGetMemberCountOptions();
            uint count = details.GetMemberCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                ProductUserId member = details.GetMemberByIndex(ref memberOptions);
                if (member != null)
                {
                    members.Add(member);
                }
            }

            details.Release();
            return members;
        }

        /// <summary>Returns the lobby owner, or null when not in a lobby.</summary>
        public static ProductUserId GetOwner()
        {
            LobbyInterface lobby = Lobby;
            if (lobby == null || !IsInLobby)
            {
                return null;
            }

            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = EOSManager.Instance.GetProductUserId()
            };

            if (lobby.CopyLobbyDetailsHandle(ref copyOptions, out LobbyDetails details) != Result.Success)
            {
                return null;
            }

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            Result result = details.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);
            details.Release();

            return result == Result.Success ? info?.LobbyOwnerUserId : null;
        }

        /// <summary>Releases search results the caller decided not to join.</summary>
        public static void ReleaseResults(List<LobbyDetails> results)
        {
            if (results == null)
            {
                return;
            }

            foreach (LobbyDetails details in results)
            {
                details?.Release();
            }

            results.Clear();
        }

        private static void AddGameModeAttribute(string gameMode, Action<Result> onComplete)
        {
            LobbyInterface lobby = Lobby;

            var modificationOptions = new UpdateLobbyModificationOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                LobbyId = CurrentLobbyId
            };

            Result result = lobby.UpdateLobbyModification(ref modificationOptions, out LobbyModification modification);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Lobby: UpdateLobbyModification failed ({result}).");
                onComplete?.Invoke(result);
                return;
            }

            var attributeOptions = new LobbyModificationAddAttributeOptions
            {
                // Public visibility is what makes the attribute searchable.
                Visibility = LobbyAttributeVisibility.Public,
                Attribute = new AttributeData
                {
                    Key = GameModeKey,
                    Value = gameMode
                }
            };

            result = modification.AddAttribute(ref attributeOptions);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Lobby: AddAttribute failed ({result}).");
                modification.Release();
                onComplete?.Invoke(result);
                return;
            }

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            lobby.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo info) =>
            {
                modification.Release();

                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Lobby: UpdateLobby failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }
    }
}
