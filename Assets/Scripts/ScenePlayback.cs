using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplaySystem
{
    [Serializable]
    public struct PrefabEntry
    {
        public string key;
        public GameObject prefab;
    }

    /// <summary>
    /// Plays back a .jsonl session recorded by SceneRecorder. Objects that
    /// existed for the whole recording are found directly in the currently
    /// loaded scene (matched via SceneRecorder.GetPath) and just moved -
    /// nothing is instantiated for them. Objects that were dynamically
    /// spawned/destroyed during the original recording (tagged with
    /// ReplayPrefabSource) are instantiated/destroyed again here as scrubbing
    /// crosses their recorded spawn/despawn moments, in either direction.
    /// </summary>
    public class ScenePlayback : MonoBehaviour
    {
        public float playbackSpeed = 1f;
        public bool isPlaying;
        [Range(0f, 1f)] public float normalizedTime;

        [Tooltip("Prefab lookup for objects that were spawned/destroyed during " +
                 "the original recording (tagged with ReplayPrefabSource). Objects " +
                 "that existed for the whole recording don't need an entry here - " +
                 "they're found directly in the current scene instead.")]
        public List<PrefabEntry> prefabs = new List<PrefabEntry>();

        // True for as long as ScenePlayback has control of at least one object.
        // Gameplay scripts (e.g. collision handlers) should check this and skip
        // their normal logic while it's true, since movement here is replay,
        // not a real simulation - e.g.:
        //   void OnCollisionEnter(Collision c) {
        //       if (ScenePlayback.IsPlayingBack) return;
        //       ...
        //   }
        public static bool IsPlayingBack { get; private set; }

        private class TrackedEntity
        {
            public string prefabKey;
            public bool everSpawned; // has an explicit Spawn event - i.e. wasn't there from the start
            public double? despawnTime;
            public List<ScenePoseFrame> updates = new List<ScenePoseFrame>();
            public Transform existingTransform; // set if found already in the scene (pre-existing object)
            public GameObject spawnedInstance;  // set once instantiated for a dynamic object
        }

        private readonly Dictionary<string, TrackedEntity> _entities = new Dictionary<string, TrackedEntity>();
        private double _baseTime;
        private double _duration;
        private double _currentTime;

        public double CurrentTime => _currentTime;
        public double Duration => _duration;

        public bool Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[ScenePlayback] File not found: {filePath}");
                return false;
            }

            var frames = File.ReadLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonUtility.FromJson<ScenePoseFrame>(l))
                .ToList();

            if (frames.Count == 0)
            {
                Debug.LogWarning("[ScenePlayback] No frames loaded.");
                return false;
            }

            ReleaseAll();
            _entities.Clear();
            double minT = double.MaxValue, maxT = double.MinValue;

            foreach (var f in frames)
            {
                if (!_entities.TryGetValue(f.path, out var entity))
                {
                    entity = new TrackedEntity();
                    _entities[f.path] = entity;
                }

                switch (f.eventType)
                {
                    case SceneFrameEventType.Spawn:
                        entity.everSpawned = true;
                        entity.prefabKey = f.prefabKey;
                        entity.updates.Add(f);
                        break;
                    case SceneFrameEventType.Despawn:
                        entity.despawnTime = f.timestamp;
                        break;
                    default:
                        entity.updates.Add(f);
                        break;
                }

                if (f.timestamp < minT) minT = f.timestamp;
                if (f.timestamp > maxT) maxT = f.timestamp;
            }

            var currentPaths = BuildCurrentPathMap();
            int missing = 0;
            foreach (var kvp in _entities)
            {
                kvp.Value.updates.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
                if (kvp.Value.everSpawned)
                    continue; // dynamic - instantiated on demand in ApplyAt, not found now

                if (currentPaths.TryGetValue(kvp.Key, out var t))
                {
                    kvp.Value.existingTransform = t;
                    var positioner = t.GetComponent<GpsPositioner>();
                    if (positioner != null) positioner.enabled = false;
                }
                else
                {
                    missing++;
                }
            }
            if (missing > 0)
                Debug.LogWarning($"[ScenePlayback] {missing} recorded object(s) present for the whole original " +
                                  "recording were not found in the current scene - they'll be skipped.");

            IsPlayingBack = true;

            _baseTime = minT;
            _duration = maxT - minT;
            _currentTime = 0;
            ApplyAt(0);
            return true;
        }

        private Dictionary<string, Transform> BuildCurrentPathMap()
        {
            var map = new Dictionary<string, Transform>();
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                CollectRecursive(root.transform, map);
            return map;
        }

        private void CollectRecursive(Transform t, Dictionary<string, Transform> map)
        {
            map[SceneRecorder.GetPath(t)] = t;
            for (int i = 0; i < t.childCount; i++)
                CollectRecursive(t.GetChild(i), map);
        }

        public void Play() => isPlaying = true;
        public void Pause() => isPlaying = false;

        public void Seek(double timeSeconds)
        {
            _currentTime = Math.Clamp(timeSeconds, 0, _duration);
            ApplyAt(_currentTime);
            normalizedTime = _duration > 0 ? (float)(_currentTime / _duration) : 0f;
        }

        public void SeekNormalized(float t) => Seek(t * _duration);

        // Hands control back to live scripts (e.g. re-enables GpsPositioner)
        // and forgets the currently loaded recording, without needing to load
        // a different file or restart Play mode to get unstuck.
        public void ReleaseControl()
        {
            ReleaseAll();
            _entities.Clear();
            isPlaying = false;
            _currentTime = 0;
            _duration = 0;
            normalizedTime = 0;
        }

        private void Update()
        {
            if (!isPlaying || _entities.Count == 0) return;

            _currentTime += Time.deltaTime * playbackSpeed;
            if (_currentTime >= _duration)
            {
                _currentTime = _duration;
                isPlaying = false;
            }
            else if (_currentTime <= 0)
            {
                _currentTime = 0;
                isPlaying = false;
            }

            ApplyAt(_currentTime);
            normalizedTime = _duration > 0 ? (float)(_currentTime / _duration) : 0f;
        }

        private void ApplyAt(double t)
        {
            double absoluteT = _baseTime + t;

            foreach (var entity in _entities.Values)
            {
                Transform target;

                if (!entity.everSpawned)
                {
                    target = entity.existingTransform;
                    if (target == null || entity.updates.Count == 0) continue;
                }
                else
                {
                    double spawnTime = entity.updates.Count > 0 ? entity.updates[0].timestamp : double.MaxValue;
                    bool shouldExist = absoluteT >= spawnTime &&
                                        (!entity.despawnTime.HasValue || absoluteT < entity.despawnTime.Value);

                    if (shouldExist && entity.spawnedInstance == null)
                        entity.spawnedInstance = SpawnFor(entity);
                    else if (!shouldExist && entity.spawnedInstance != null)
                    {
                        Destroy(entity.spawnedInstance);
                        entity.spawnedInstance = null;
                    }

                    if (!shouldExist || entity.spawnedInstance == null || entity.updates.Count == 0) continue;
                    target = entity.spawnedInstance.transform;
                }

                ApplyInterpolatedPose(entity.updates, absoluteT, target);
            }
        }

        private void ApplyInterpolatedPose(List<ScenePoseFrame> frames, double absoluteT, Transform target)
        {
            int i = frames.FindLastIndex(f => f.timestamp <= absoluteT);
            if (i < 0) i = 0;
            int j = Math.Min(i + 1, frames.Count - 1);

            var a = frames[i];
            var b = frames[j];

            float lerp = 0f;
            if (b.timestamp > a.timestamp)
                lerp = Mathf.InverseLerp((float)a.timestamp, (float)b.timestamp, (float)absoluteT);

            target.position = Vector3.Lerp(a.position, b.position, lerp);
            target.rotation = Quaternion.Slerp(a.rotation, b.rotation, lerp);
        }

        private GameObject SpawnFor(TrackedEntity entity)
        {
            var entry = prefabs.FirstOrDefault(p => p.key == entity.prefabKey);
            if (entry.prefab == null)
            {
                Debug.LogWarning($"[ScenePlayback] No prefab registered for key '{entity.prefabKey}' - " +
                                  "can't recreate this object during playback.");
                return null;
            }
            var go = Instantiate(entry.prefab);
            go.name = "(replay) " + entry.prefab.name;
            return go;
        }

        private void ReleaseAll()
        {
            IsPlayingBack = false;
            foreach (var entity in _entities.Values)
            {
                if (entity.existingTransform != null)
                {
                    var positioner = entity.existingTransform.GetComponent<GpsPositioner>();
                    if (positioner != null) positioner.enabled = true;
                }
                if (entity.spawnedInstance != null)
                    Destroy(entity.spawnedInstance);
                entity.spawnedInstance = null;
            }
        }

        private void OnDestroy() => ReleaseAll();
    }
}
