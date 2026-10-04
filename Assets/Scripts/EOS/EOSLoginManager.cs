using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS
{
    /// <summary>
    /// Device ID login. Produces a ProductUserId without requiring the player to
    /// own an Epic Games account, which is the baseline login path on Android
    /// and iOS (Dev Auth and Exchange Code are not available there).
    /// </summary>
    public static class EOSLoginManager
    {
        /// <summary>The logged in user, or null when no login has completed.</summary>
        public static ProductUserId LocalProductUserId => EOSManager.Instance.GetProductUserId();

        public static bool IsLoggedIn
        {
            get
            {
                ProductUserId userId = LocalProductUserId;
                return userId != null && userId.IsValid();
            }
        }

        /// <summary>
        /// Registers a device ID if this device does not have one yet, then logs
        /// in with it. <paramref name="onComplete"/> is given Result.Success once
        /// <see cref="LocalProductUserId"/> is usable.
        /// </summary>
        public static void LoginWithDeviceId(string displayName, Action<Result> onComplete = null)
        {
            if (EOSSDKManager.GetEOSPlatformInterface() == null)
            {
                Debug.LogError("EOS: the platform is not initialized yet.");
                onComplete?.Invoke(Result.NotConfigured);
                return;
            }

            var options = new CreateDeviceIdOptions
            {
                // Shown in account linking management, so keep it human readable.
                DeviceModel = SystemInfo.deviceModel
            };

            EOSManager.Instance.GetEOSConnectInterface().CreateDeviceId(ref options, null,
                (ref CreateDeviceIdCallbackInfo info) =>
                {
                    // DuplicateNotAllowed just means this device already has an id.
                    if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed)
                    {
                        ConnectWithDeviceId(displayName, onComplete);
                    }
                    else
                    {
                        Debug.LogError($"EOS: CreateDeviceId failed ({info.ResultCode}).");
                        onComplete?.Invoke(info.ResultCode);
                    }
                });
        }

        private static void ConnectWithDeviceId(string displayName, Action<Result> onComplete)
        {
            EOSManager.Instance.StartConnectLoginWithOptions(
                ExternalCredentialType.DeviceidAccessToken, null, displayName,
                loginInfo =>
                {
                    switch (loginInfo.ResultCode)
                    {
                        case Result.Success:
                            Debug.Log($"EOS: logged in as {loginInfo.LocalUserId}.");
                            onComplete?.Invoke(Result.Success);
                            break;

                        case Result.InvalidUser:
                            // No EOS user is attached to this device id yet, which is
                            // expected on the first run. Create one from the token.
                            CreateUser(loginInfo.ContinuanceToken, onComplete);
                            break;

                        default:
                            Debug.LogError($"EOS: connect login failed ({loginInfo.ResultCode}).");
                            onComplete?.Invoke(loginInfo.ResultCode);
                            break;
                    }
                });
        }

        private static void CreateUser(ContinuanceToken token, Action<Result> onComplete)
        {
            EOSManager.Instance.CreateConnectUserWithContinuanceToken(token, createInfo =>
            {
                if (createInfo.ResultCode == Result.Success)
                {
                    Debug.Log($"EOS: created a new user, {createInfo.LocalUserId}.");
                }
                else
                {
                    Debug.LogError($"EOS: CreateUser failed ({createInfo.ResultCode}).");
                }

                onComplete?.Invoke(createInfo.ResultCode);
            });
        }
    }
}
