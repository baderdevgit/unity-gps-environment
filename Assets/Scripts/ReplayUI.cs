using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// Minimal on-screen controls for SceneRecorder/ScenePlayback, using
    /// Unity's built-in IMGUI (OnGUI) - no Canvas, font, or prefab setup
    /// needed, just attach to any GameObject.
    ///
    /// Recordings are listed straight from disk (Application.persistentDataPath),
    /// not from in-memory state, so a session recorded earlier - even in a
    /// previous Play session, or before the Editor was last closed - can still
    /// be found and played back here.
    /// </summary>
    public class ReplayUI : MonoBehaviour
    {
        [SerializeField] private SceneRecorder recorder;
        [SerializeField] private ScenePlayback playback;

        private string _loadedFile;
        private string _statusMessage = "";
        private List<string> _availableFiles = new List<string>();
        private Vector2 _fileListScroll;

        private void Awake()
        {
            if (recorder == null) recorder = FindObjectOfType<SceneRecorder>();
            if (playback == null) playback = FindObjectOfType<ScenePlayback>();
            RefreshFileList();
        }

        private void RefreshFileList()
        {
            string subFolder = recorder != null ? recorder.subFolder : "Sessions";
            string folder = Path.Combine(Application.persistentDataPath, subFolder);

            _availableFiles = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.jsonl").OrderByDescending(File.GetLastWriteTime).ToList()
                : new List<string>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 420), GUI.skin.box);
            GUILayout.Label("Replay Controls");

            GUILayout.Space(6);
            DrawRecordingControls();

            GUILayout.Space(10);
            DrawPlaybackControls();

            GUILayout.Space(10);
            DrawFileList();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(6);
                GUILayout.Label(_statusMessage);
            }

            GUILayout.EndArea();
        }

        private void DrawRecordingControls()
        {
            if (recorder == null)
            {
                GUILayout.Label("No SceneRecorder found in scene.");
                return;
            }

            GUILayout.Label(recorder.IsRecording ? "Recording..." : "Not recording.");

            if (!recorder.IsRecording)
            {
                if (GUILayout.Button("Start Recording"))
                {
                    recorder.StartRecording();
                    _statusMessage = $"Recording to {recorder.CurrentFilePath}";
                }
            }
            else
            {
                if (GUILayout.Button("Stop Recording"))
                {
                    recorder.StopRecording();
                    _statusMessage = $"Stopped. Saved to {recorder.CurrentFilePath}";
                    RefreshFileList();
                }
            }
        }

        private void DrawPlaybackControls()
        {
            if (playback == null)
            {
                GUILayout.Label("No ScenePlayback found in scene.");
                return;
            }

            GUILayout.Label($"Loaded: {(string.IsNullOrEmpty(_loadedFile) ? "none" : Path.GetFileName(_loadedFile))}");

            GUI.enabled = !string.IsNullOrEmpty(_loadedFile);
            if (GUILayout.Button(playback.isPlaying ? "Pause" : "Play"))
            {
                if (playback.isPlaying) playback.Pause();
                else playback.Play();
            }

            GUILayout.Label($"Time: {playback.CurrentTime:0.0}s / {playback.Duration:0.0}s");
            float newT = GUILayout.HorizontalSlider(playback.normalizedTime, 0f, 1f);
            if (!Mathf.Approximately(newT, playback.normalizedTime))
                playback.SeekNormalized(newT);

            GUILayout.Label($"Speed: {playback.playbackSpeed:0.00}x");
            playback.playbackSpeed = GUILayout.HorizontalSlider(playback.playbackSpeed, 0.1f, 4f);
            GUI.enabled = true;
        }

        private void DrawFileList()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Recordings ({_availableFiles.Count}):");
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                RefreshFileList();
            GUILayout.EndHorizontal();

            if (_availableFiles.Count == 0)
            {
                GUILayout.Label("None found yet.");
                return;
            }

            _fileListScroll = GUILayout.BeginScrollView(_fileListScroll, GUILayout.Height(120));
            foreach (var file in _availableFiles)
            {
                if (GUILayout.Button(Path.GetFileName(file)))
                {
                    _loadedFile = file;
                    bool ok = playback != null && playback.Load(file);
                    _statusMessage = ok ? $"Loaded {Path.GetFileName(file)}" : "Failed to load - see Console.";
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
