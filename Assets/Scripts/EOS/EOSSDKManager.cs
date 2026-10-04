using Epic.OnlineServices.Platform;
using PlayEveryWare.EpicOnlineServices;

namespace EOS
{
    /// <summary>
    /// Thin accessor for the EOS platform.
    /// Initialization, the Android activity hookup and the per-platform
    /// InitializeOptions are all handled by <see cref="EOSManager"/>, so attach
    /// that component to a GameObject in the startup scene.
    /// </summary>
    public static class EOSSDKManager
    {
        /// <summary>
        /// Returns the platform interface, or null while EOS has not finished
        /// initializing (or after it has been shut down).
        /// </summary>
        public static PlatformInterface GetEOSPlatformInterface()
        {
            return EOSManager.Instance.GetEOSPlatformInterface();
        }

        /// <summary>True once the EOS platform is ready to use.</summary>
        public static bool IsReady => GetEOSPlatformInterface() != null;
    }
}
