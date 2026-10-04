using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Sessions;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS
{
    /// <summary>
    /// Sessions describe a match rather than a waiting room. They carry a
    /// started/ended lifecycle, so use them when a match needs a visible state
    /// (in progress, joinable, finished) rather than just a member list.
    /// See <see cref="EOSLobbyManager"/> for the pre-match room.
    /// </summary>
    public static class EOSSessionManager
    {
        /// <summary>Attribute the search filters on. Must be advertised.</summary>
        private const string GameModeKey = "GAMEMODE";

        /// <summary>Local name for the session. Not visible to other players.</summary>
        public const string LocalSessionName = "MainSession";

        public static bool HasSession { get; private set; }

        // Domain reload is disabled for play mode in this project, so without this
        // the flag would still claim a session from the previous session exists.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            HasSession = false;
        }

        private static SessionsInterface Sessions => EOSSDKManager.GetEOSPlatformInterface()?.GetSessionsInterface();

        /// <summary>
        /// Creates and publishes a session tagged with <paramref name="gameMode"/>.
        /// The session exists but is not running until <see cref="Start"/>.
        /// </summary>
        public static void Create(string gameMode, uint maxPlayers, Action<Result> onComplete = null)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null)
            {
                onComplete?.Invoke(Result.NotConfigured);
                return;
            }

            var createOptions = new CreateSessionModificationOptions
            {
                SessionName = LocalSessionName,
                BucketId = gameMode,
                MaxPlayers = maxPlayers,
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                PresenceEnabled = true
            };

            Result result = sessions.CreateSessionModification(ref createOptions, out SessionModification modification);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Session: CreateSessionModification failed ({result}).");
                onComplete?.Invoke(result);
                return;
            }

            var permissionOptions = new SessionModificationSetPermissionLevelOptions
            {
                PermissionLevel = OnlineSessionPermissionLevel.PublicAdvertised
            };
            modification.SetPermissionLevel(ref permissionOptions);

            var joinInProgressOptions = new SessionModificationSetJoinInProgressAllowedOptions
            {
                AllowJoinInProgress = true
            };
            modification.SetJoinInProgressAllowed(ref joinInProgressOptions);

            // Advertise the game mode so that Search can filter on it.
            var attributeOptions = new SessionModificationAddAttributeOptions
            {
                AdvertisementType = SessionAttributeAdvertisementType.Advertise,
                SessionAttribute = new AttributeData
                {
                    Key = GameModeKey,
                    Value = gameMode
                }
            };
            modification.AddAttribute(ref attributeOptions);

            var updateOptions = new UpdateSessionOptions { SessionModificationHandle = modification };
            sessions.UpdateSession(ref updateOptions, null, (ref UpdateSessionCallbackInfo info) =>
            {
                modification.Release();

                if (info.ResultCode == Result.Success)
                {
                    HasSession = true;
                    Debug.Log($"EOS Session: created {info.SessionId}.");
                }
                else
                {
                    Debug.LogError($"EOS Session: UpdateSession failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>
        /// Finds sessions tagged with <paramref name="gameMode"/>. The returned
        /// handles are owned by the caller; pass them to <see cref="Join"/> or
        /// release them with <see cref="ReleaseResults"/>.
        /// </summary>
        public static void Search(string gameMode, uint maxResults, Action<Result, List<SessionDetails>> onComplete)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null)
            {
                onComplete?.Invoke(Result.NotConfigured, null);
                return;
            }

            var searchOptions = new CreateSessionSearchOptions { MaxSearchResults = maxResults };
            Result result = sessions.CreateSessionSearch(ref searchOptions, out SessionSearch search);
            if (result != Result.Success)
            {
                Debug.LogError($"EOS Session: CreateSessionSearch failed ({result}).");
                onComplete?.Invoke(result, null);
                return;
            }

            var parameterOptions = new SessionSearchSetParameterOptions
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
                Debug.LogError($"EOS Session: SetParameter failed ({result}).");
                search.Release();
                onComplete?.Invoke(result, null);
                return;
            }

            var findOptions = new SessionSearchFindOptions
            {
                LocalUserId = EOSManager.Instance.GetProductUserId()
            };

            search.Find(ref findOptions, null, (ref SessionSearchFindCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Session: Find failed ({info.ResultCode}).");
                    search.Release();
                    onComplete?.Invoke(info.ResultCode, null);
                    return;
                }

                var countOptions = new SessionSearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOptions);

                var results = new List<SessionDetails>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var copyOptions = new SessionSearchCopySearchResultByIndexOptions { SessionIndex = i };
                    if (search.CopySearchResultByIndex(ref copyOptions, out SessionDetails details) == Result.Success)
                    {
                        results.Add(details);
                    }
                }

                search.Release();

                Debug.Log($"EOS Session: found {results.Count} sessions for '{gameMode}'.");
                onComplete?.Invoke(Result.Success, results);
            });
        }

        public static void Join(SessionDetails details, Action<Result> onComplete = null)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null || details == null)
            {
                onComplete?.Invoke(Result.NotConfigured);
                return;
            }

            var options = new JoinSessionOptions
            {
                SessionName = LocalSessionName,
                SessionHandle = details,
                LocalUserId = EOSManager.Instance.GetProductUserId(),
                PresenceEnabled = true
            };

            sessions.JoinSession(ref options, null, (ref JoinSessionCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    HasSession = true;
                    Debug.Log("EOS Session: joined.");
                }
                else
                {
                    Debug.LogError($"EOS Session: JoinSession failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>Marks the match as in progress. Call when gameplay begins.</summary>
        public static void Start(Action<Result> onComplete = null)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null || !HasSession)
            {
                onComplete?.Invoke(Result.NotFound);
                return;
            }

            var options = new StartSessionOptions { SessionName = LocalSessionName };
            sessions.StartSession(ref options, null, (ref StartSessionCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Session: StartSession failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>Marks the match as finished, making the session joinable again.</summary>
        public static void End(Action<Result> onComplete = null)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null || !HasSession)
            {
                onComplete?.Invoke(Result.NotFound);
                return;
            }

            var options = new EndSessionOptions { SessionName = LocalSessionName };
            sessions.EndSession(ref options, null, (ref EndSessionCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"EOS Session: EndSession failed ({info.ResultCode}).");
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        public static void Destroy(Action<Result> onComplete = null)
        {
            SessionsInterface sessions = Sessions;
            if (sessions == null || !HasSession)
            {
                onComplete?.Invoke(Result.NotFound);
                return;
            }

            var options = new DestroySessionOptions { SessionName = LocalSessionName };
            sessions.DestroySession(ref options, null, (ref DestroySessionCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    HasSession = false;
                }

                onComplete?.Invoke(info.ResultCode);
            });
        }

        /// <summary>Returns the host of a search result, for opening a P2P connection.</summary>
        public static ProductUserId GetOwner(SessionDetails details)
        {
            if (details == null)
            {
                return null;
            }

            var infoOptions = new SessionDetailsCopyInfoOptions();
            return details.CopyInfo(ref infoOptions, out SessionDetailsInfo? info) == Result.Success
                ? info?.OwnerUserId
                : null;
        }

        public static void ReleaseResults(List<SessionDetails> results)
        {
            if (results == null)
            {
                return;
            }

            foreach (SessionDetails details in results)
            {
                details?.Release();
            }

            results.Clear();
        }
    }
}
