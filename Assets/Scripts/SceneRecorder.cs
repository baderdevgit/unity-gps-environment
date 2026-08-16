using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplaySystem
{
    [Serializable]
    public struct ScenePoseFrame
    {
        public double timestamp;
        public string path;   // full hierarchy path, e.g. "Marker/Cube"
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

            foreach (var t in transforms)
            {
                var frame = new ScenePoseFrame
                {
                    timestamp = timestamp,
                    path = GetPath(t),
                    position = t.position,
                    rotation = t.rotation
                };
                _writer.WriteLine(JsonUtility.ToJson(frame));
            }

            if (Time.frameCount % 30 == 0) _writer.Flush();
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
        // playback, not something to play back itself) and the recorder/
        // playback/UI objects themselves.
        private bool ShouldSkip(Transform t)
        {
            return t.GetComponent<FreeFlyCamera>() != null
                || t.GetComponent<SceneRecorder>() != null
                || t.GetComponent<ScenePlayback>() != null
                || t.GetComponent<ReplayUI>() != null;
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
