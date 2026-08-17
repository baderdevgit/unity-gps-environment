using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplaySystem
{
    public enum SceneFrameEventType { Update, Spawn, Despawn }

    [Serializable]
    public struct ScenePoseFrame
    {
        public double timestamp;
        public string path;   // full hierarchy path, e.g. "Marker/Cube"
        public SceneFrameEventType eventType;
        public string prefabKey;   // only meaningful on Spawn - see ReplayPrefabSource.cs
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>
    /// Automatically records every GameObject's position/rotation in the active
    /// scene, at a fixed sample rate, with no per-object setup required. Meant
    /// for capturing full playback footage (e.g. for reviewing/filming from a
    /// free camera afterward) rather than a specific tracked entity.
    /// </summary>
    public class SceneRecorder : MonoBehaviour
    {
        [Tooltip("Folder relative to persistentDataPath, or leave blank for root.")]
        public string subFolder = "Sessions";

        [Tooltip("Samples per second for every object. 30 matches typical video " +
                 "output and keeps file size reasonable versus recording every frame.")]
        public float recordFps = 30f;

        [Tooltip("Optional. If set (or found in the scene), Start/Stop Recording " +
                 "can also be triggered from the mobile web UI.")]
        [SerializeField] private DataReceiver receiver;

        private StreamWriter _writer;
        private double _sessionStartTime;
        private float _recordTimer;
        private HashSet<string> _previousPaths = new HashSet<string>();
        public bool IsRecording { get; private set; }
        public string CurrentFilePath { get; private set; }

        private void Awake()
        {
            if (receiver == null)
                receiver = FindObjectOfType<DataReceiver>();
        }

        private void OnEnable()
        {
            if (receiver != null)
            {
                receiver.OnStartRecordingRequested += HandleStartRecordingRequested;
                receiver.OnStopRecordingRequested += HandleStopRecordingRequested;
            }
        }

        private void OnDisable()
        {
            if (receiver != null)
            {
                receiver.OnStartRecordingRequested -= HandleStartRecordingRequested;
                receiver.OnStopRecordingRequested -= HandleStopRecordingRequested;
            }
        }

        private void HandleStartRecordingRequested() => StartRecording();
        private void HandleStopRecordingRequested() => StopRecording();

        public void StartRecording(string sessionName = null)
        {
            if (IsRecording) StopRecording();

            var folder = Path.Combine(Application.persistentDataPath, subFolder);
            Directory.CreateDirectory(folder);

            var fileName = string.IsNullOrEmpty(sessionName)
                ? $"scene_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl"
                : $"{sessionName}.jsonl";

            CurrentFilePath = Path.Combine(folder, fileName);
            _writer = new StreamWriter(CurrentFilePath, append: false, Encoding.UTF8) { AutoFlush = false };
            _sessionStartTime = Time.realtimeSinceStartupAsDouble;
            _recordTimer = 0f;
            _previousPaths.Clear();
            IsRecording = true;

            Debug.Log($"[SceneRecorder] Recording to {CurrentFilePath}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            _writer.Flush();
            _writer.Close();
            _writer = null;
            IsRecording = false;
            Debug.Log($"[SceneRecorder] Stopped. Saved to {CurrentFilePath}");
        }

        private void Update()
        {
            if (!IsRecording) return;

            _recordTimer += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, recordFps);
            if (_recordTimer < interval) return;
            _recordTimer = 0f;

            double timestamp = Time.realtimeSinceStartupAsDouble - _sessionStartTime;
            var transforms = new List<Transform>();
            CollectAll(transforms);

            var currentPaths = new HashSet<string>();
            foreach (var t in transforms)
            {
                string path = GetPath(t);
                currentPaths.Add(path);

                // New this tick (and not just the very first tick of the whole
                // recording) - emit an explicit Spawn event so ScenePlayback can
                // recreate it later instead of expecting to find it already there.
                if (!_previousPaths.Contains(path) && _previousPaths.Count > 0)
                {
                    var source = t.GetComponent<ReplayPrefabSource>();
                    WriteFrame(new ScenePoseFrame
                    {
                        timestamp = timestamp,
                        path = path,
                        eventType = SceneFrameEventType.Spawn,
                        prefabKey = source != null ? source.prefabKey : null,
                        position = t.position,
                        rotation = t.rotation
                    });
                }

                WriteFrame(new ScenePoseFrame
                {
                    timestamp = timestamp,
                    path = path,
                    eventType = SceneFrameEventType.Update,
                    position = t.position,
                    rotation = t.rotation
                });
            }

            foreach (var oldPath in _previousPaths)
            {
                if (!currentPaths.Contains(oldPath))
                {
                    WriteFrame(new ScenePoseFrame
                    {
                        timestamp = timestamp,
                        path = oldPath,
                        eventType = SceneFrameEventType.Despawn
                    });
                }
            }

            _previousPaths = currentPaths;

            if (Time.frameCount % 30 == 0) _writer.Flush();
        }

        private void WriteFrame(ScenePoseFrame frame)
        {
            _writer.WriteLine(JsonUtility.ToJson(frame));
        }

        private void CollectAll(List<Transform> result)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                CollectRecursive(root.transform, result);
        }

        private void CollectRecursive(Transform t, List<Transform> result)
        {
            if (ShouldSkip(t)) return;
            result.Add(t);
            for (int i = 0; i < t.childCount; i++)
                CollectRecursive(t.GetChild(i), result);
        }

        // Excludes the free-fly viewing camera (it's controlled live during
        // playback, not something to play back itself), the recorder/playback/
        // UI objects themselves, and anything with an Animator - a rigged
        // character's skeleton can be 50+ bone Transforms, and recording/
        // replaying them directly would fight the Animator for control of the
        // same bones every frame. The model still moves correctly as a child
        // of whatever's actually tracked (e.g. "Marker"), just via normal
        // parent-child inheritance instead of its own explicit track.
        //
        // Grid3D's generated "GridLine" children are excluded for the same
        // reason: they're static relative to the grid, so they already move/
        // rotate correctly as children of the tracked Grid3D transform - no
        // need to record dozens of line Transforms individually every frame.
        private bool ShouldSkip(Transform t)
        {
            return t.GetComponent<FreeFlyCamera>() != null
                || t.GetComponent<SceneRecorder>() != null
                || t.GetComponent<ScenePlayback>() != null
                || t.GetComponent<ReplayUI>() != null
                || t.GetComponent<Animator>() != null
                || (t.parent != null && t.parent.GetComponent<Grid3D>() != null);
        }

        // Sibling index is included so that multiple instances sharing the same
        // name (e.g. several "Cylinder(Clone)" from repeated Instantiate calls)
        // still get distinct, stable paths instead of colliding under one key.
        // ScenePlayback uses this exact same scheme to find objects again -
        // don't change one without the other.
        public static string GetPath(Transform t)
        {
            string segment = t.name + "#" + t.GetSiblingIndex();
            if (t.parent == null) return segment;
            return GetPath(t.parent) + "/" + segment;
        }

        private void OnApplicationQuit() => StopRecording();
    }
}
