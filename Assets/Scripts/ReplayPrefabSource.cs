using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// Attach to a prefab so SceneRecorder can identify it by a stable key when
    /// it's dynamically instantiated during recording, letting ScenePlayback
    /// re-instantiate it later. Not needed on objects that exist for the whole
    /// recording (static scene geometry, the GPS-tracked marker, etc.) - only on
    /// things created/destroyed at runtime that should also spawn/despawn during replay.
    /// </summary>
    public class ReplayPrefabSource : MonoBehaviour
    {
        public string prefabKey;
    }
}
