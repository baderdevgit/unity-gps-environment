using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplaySystem;
using UnityEditor;
using UnityEngine;

// Editor-only replay controls (Window > Replay Controls) - lives in a
// "toolbar" style dockable window instead of ReplayUI's in-game OnGUI
// overlay, so it never shows up when recording the Game view for footage.
public class ReplayEditorWindow : EditorWindow
{
    private SceneRecorder _recorder;
    private ScenePlayback _playback;
    private Grid3D _grid;
    private string _loadedFile;
    private string _statusMessage = "";
    private List<string> _availableFiles = new List<string>();
    private Vector2 _fileListScroll;

    [MenuItem("Window/Replay Controls")]
    public static void ShowWindow()
    {
        GetWindow<ReplayEditorWindow>("Replay Controls");
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    // The Game window doesn't drive repaints for this window, so force one
    // while playing - otherwise the time/slider only update when this
    // window happens to get mouse input.
    private void OnEditorUpdate()
    {
        if (Application.isPlaying)
            Repaint();
    }

    private void OnGUI()
    {
        if (_recorder == null) _recorder = FindObjectOfType<SceneRecorder>();
        if (_playback == null) _playback = FindObjectOfType<ScenePlayback>();
        if (_grid == null) _grid = FindObjectOfType<Grid3D>();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use replay controls.", MessageType.Info);
            return;
        }

        DrawRecordingControls();
        EditorGUILayout.Space(10);
        DrawPlaybackControls();
        EditorGUILayout.Space(10);
        DrawGridControls();
        EditorGUILayout.Space(10);
        DrawFileList();

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
        }
    }

    private void DrawRecordingControls()
    {
        EditorGUILayout.LabelField("Recording", EditorStyles.boldLabel);

        if (_recorder == null)
        {
            EditorGUILayout.LabelField("No SceneRecorder found in scene.");
            return;
        }

        EditorGUILayout.LabelField(_recorder.IsRecording ? "Recording..." : "Not recording.");

        if (!_recorder.IsRecording)
        {
            if (GUILayout.Button("Start Recording"))
            {
                _recorder.StartRecording();
                _statusMessage = $"Recording to {_recorder.CurrentFilePath}";
            }
        }
        else
        {
            if (GUILayout.Button("Stop Recording"))
            {
                _recorder.StopRecording();
                _statusMessage = $"Stopped. Saved to {_recorder.CurrentFilePath}";
                RefreshFileList();
            }
        }
    }

    private void DrawPlaybackControls()
    {
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);

        if (_playback == null)
        {
            EditorGUILayout.LabelField("No ScenePlayback found in scene.");
            return;
        }

        EditorGUILayout.LabelField($"Loaded: {(string.IsNullOrEmpty(_loadedFile) ? "none" : Path.GetFileName(_loadedFile))}");

        GUI.enabled = !string.IsNullOrEmpty(_loadedFile);
        if (GUILayout.Button(_playback.isPlaying ? "Pause" : "Play"))
        {
            if (_playback.isPlaying) _playback.Pause();
            else _playback.Play();
        }

        if (GUILayout.Button("Release Control (back to live)"))
        {
            _playback.ReleaseControl();
            _loadedFile = null;
            _statusMessage = "Released - live scripts back in control.";
        }

        EditorGUILayout.LabelField($"Time: {_playback.CurrentTime:0.0}s / {_playback.Duration:0.0}s");
        float newT = EditorGUILayout.Slider(_playback.normalizedTime, 0f, 1f);
        if (!Mathf.Approximately(newT, _playback.normalizedTime))
            _playback.SeekNormalized(newT);

        EditorGUILayout.LabelField($"Speed: {_playback.playbackSpeed:0.00}x");
        _playback.playbackSpeed = EditorGUILayout.Slider(_playback.playbackSpeed, 0.1f, 4f);
        GUI.enabled = true;
    }

    private void DrawGridControls()
    {
        EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);

        if (_grid == null)
        {
            EditorGUILayout.LabelField("No Grid3D found in scene.");
            return;
        }

        // Independent of playback - grid size (unlike rotation) is never
        // recorded/replayed, so this is safe to change at any time,
        // including mid-scrub, without fighting the replay system.
        int newCells = EditorGUILayout.IntSlider("Grid Cells", _grid.columns, 1, 60);
        if (newCells != _grid.columns)
            _grid.SetCellCount(newCells);
    }

    private void DrawFileList()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Recordings ({_availableFiles.Count})", EditorStyles.boldLabel);
        if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            RefreshFileList();
        EditorGUILayout.EndHorizontal();

        if (_availableFiles.Count == 0)
        {
            EditorGUILayout.LabelField("None found yet.");
            return;
        }

        _fileListScroll = EditorGUILayout.BeginScrollView(_fileListScroll, GUILayout.Height(140));
        foreach (var file in _availableFiles)
        {
            if (GUILayout.Button(Path.GetFileName(file)))
            {
                _loadedFile = file;
                bool ok = _playback != null && _playback.Load(file);
                _statusMessage = ok ? $"Loaded {Path.GetFileName(file)}" : "Failed to load - see Console.";
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void RefreshFileList()
    {
        string subFolder = _recorder != null ? _recorder.subFolder : "Sessions";
        string folder = Path.Combine(Application.persistentDataPath, subFolder);

        _availableFiles = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.jsonl").OrderByDescending(File.GetLastWriteTime).ToList()
            : new List<string>();
    }
}
