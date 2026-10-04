using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace EOS
{
    /// <summary>
    /// Creates the <see cref="EOSManager"/> host object at startup so that no
    /// scene has to carry it manually. EOSManager marks itself DontDestroyOnLoad
    /// and disables duplicates, so a scene-placed instance keeps working too.
    /// </summary>
    public static class EOSBootstrap
    {
        // AfterSceneLoad, not BeforeSceneLoad: the per-platform modules (for
        // example AndroidPlatformSpecifics) register themselves during
        // BeforeSceneLoad, and the order within a single load type is undefined.
        // EOSManager.Awake runs Init(), which needs those already registered.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Object.FindAnyObjectByType<EOSManager>() != null)
            {
                return;
            }

            var host = new GameObject(nameof(EOSManager));
            host.AddComponent<EOSManager>();

            // Lives on the same object so it survives scene loads too. It waits
            // for the Connect login before touching the P2P interface.
            host.AddComponent<EOSP2PManager>();
        }
    }
}
