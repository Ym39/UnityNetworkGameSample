using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS.Samples
{
    /// <summary>
    /// Prints the credentials the SDK actually resolved at startup. Use this to
    /// tell "the config file was not picked up" apart from "the Developer Portal
    /// rejected these values"; the secret is masked so the log stays shareable.
    /// Delete this file once the connection works.
    /// </summary>
    public static class EOSConfigDump
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Dump()
        {
            PlatformConfig platformConfig = PlatformManager.GetPlatformConfig();
            if (platformConfig == null)
            {
                Debug.LogError("EOS config: no platform config resolved.");
                return;
            }

            EOSClientCredentials credentials = platformConfig.clientCredentials;

            Debug.Log(
                $"EOS config resolved for platform '{PlatformManager.CurrentPlatform}':\n" +
                $"  ProductId    : {EOSManager.Instance.GetProductId()}\n" +
                $"  SandboxId    : {EOSManager.Instance.GetSandboxId()}\n" +
                $"  DeploymentId : {EOSManager.Instance.GetDeploymentID()}\n" +
                $"  ClientId     : {credentials?.ClientId}\n" +
                $"  ClientSecret : {Mask(credentials?.ClientSecret)}\n" +
                $"  EncryptionKey: {Mask(credentials?.EncryptionKey)}");
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            return $"{value[..4]}...{value[^4..]} (length {value.Length})";
        }
    }
}
