using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// Plays back a .jsonl session recorded by SceneRecorder by moving the
    /// *existing* GameObjects already present in the currently loaded scene
    /// (matched by hierarchy path) - nothing is instantiated or destroyed,
    /// since SceneRecorder captured everything that was already there.
    /// </summary>
    public class ScenePlayback : MonoBehaviour
    {
        public float playbackSpeed = 1f;
        public bool isPlaying;
        [Range(0f, 1f)] public float normalizedTime;

        private Dictionary<string, List<ScenePoseFrame>> _tracks = new Dictionary<string, List<ScenePoseFrame>>();
        private Dictionary<string, Transform> _targets = new Dictionary<string, Transform>();
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

            _tracks.Clear();
            _targets.Clear();
            double minT = double.MaxValue, maxT = double.MinValue;

            foreach (var f in frames)
            {
                if (!_tracks.TryGetValue(f.path, out var list))
                {
                    list = new List<ScenePoseFrame>();
                    _tracks[f.path] = list;
                }
                list.Add(f);
                if (f.timestamp < minT) minT = f.timestamp;
                if (f.timestamp > maxT) maxT = f.timestamp;
            }

            int missing = 0;
            foreach (var path in _tracks.Keys)
            {
                _tracks[path].Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
                var found = GameObject.Find(path);
                if (found != null)
                    _targets[path] = found.transform;
                else
                    missing++;
            }
            if (missing > 0)
                Debug.LogWarning($"[ScenePlayback] {missing} recorded object(s) not found in the current scene " +
                                  "(renamed/moved/deleted since recording?) - they'll be skipped during playback.");

            _baseTime = minT;
            _duration = maxT - minT;
            _currentTime = 0;
            ApplyAt(0);
            return true;
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

        private void Update()
        {
            if (!isPlaying || _tracks.Count == 0) return;

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

            foreach (var kvp in _targets)
            {
                var target = kvp.Value;
                if (target == null) continue;

                var frames = _tracks[kvp.Key];
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
        }
    }
}
