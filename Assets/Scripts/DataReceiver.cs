using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Matches the JSON line shape sent by PiScript/gps.py's ServerRelay.
[Serializable]
public struct GpsData
{
    public double lat;
    public double lon;
    public double alt;
    public int fixQuality;
    public string fixStatus;
    // Compass degrees (0=North, 90=East), from the Pi's IMU, re-anchored to
    // GPS course whenever the rover has moved far enough for that to be
    // reliable. 0 if no IMU is attached on the Pi.
    public double heading;
    public double timestamp;
}

// Control messages from the server (e.g. the mobile web UI's reset button)
// carry a "type" field that GpsData lines never have.
[Serializable]
internal struct MessageEnvelope
{
    public string type;
}

// Sent by gps.py's runImuLoop() at ~10Hz, independent of the ~1Hz GPS fix
// rate, so heading can update much faster than position does.
[Serializable]
internal struct ImuData
{
    public double heading;
    public double timestamp;
}

// Sent by the mobile web UI's grid size slider ({"type":"grid-size","cells":N}).
[Serializable]
internal struct GridSizeData
{
    public string type;
    public int cells;
}

// Attach to any GameObject. Connects to the C# server running on the same PC
// and receives whatever data the server relayed from the Raspberry Pi.
public class DataReceiver : MonoBehaviour
{
    [SerializeField] private string serverHost = "127.0.0.1";
    [SerializeField] private int serverPort = 5001;

    private TcpClient _client;
    private Thread _readThread;
    private volatile bool _running;
    private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();

    // Fired on the main thread with each raw line of data received.
    public event Action<string> OnDataReceived;

    // Fired on the main thread when a line successfully parses as a GpsFix.
    public event Action<GpsData> OnGpsFixReceived;

    // Fired on the main thread when a {"type":"reset"} control message arrives
    // (sent by the mobile web UI's Reset button).
    public event Action OnResetRequested;

    // Fired on the main thread when a {"type":"place"} control message arrives
    // (sent by the mobile web UI's Place Marker button).
    public event Action OnPlaceMarkerRequested;

    // Fired on the main thread when a {"type":"clear"} control message arrives
    // (sent by the mobile web UI's Clear Markers button).
    public event Action OnClearMarkersRequested;

    // Fired on the main thread when a {"type":"imu"} heading-only message
    // arrives (~10Hz from gps.py's runImuLoop, faster than GPS fixes).
    public event Action<double> OnImuHeadingReceived;

    // Fired on the main thread when a {"type":"start-recording"} control
    // message arrives (sent by the mobile web UI's Start Recording button).
    public event Action OnStartRecordingRequested;

    // Fired on the main thread when a {"type":"stop-recording"} control
    // message arrives (sent by the mobile web UI's Stop Recording button).
    public event Action OnStopRecordingRequested;

    // Fired on the main thread when a {"type":"grid-size"} control message
    // arrives (sent by the mobile web UI's grid size slider).
    public event Action<int> OnGridSizeChanged;

    private void Start()
    {
        _running = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        while (_running)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(serverHost, serverPort);

                using (var stream = _client.GetStream())
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while (_running && (line = reader.ReadLine()) != null)
                    {
                        _incoming.Enqueue(line);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Server connection lost/unavailable: {e.Message}. Retrying in 2s...");
            }

            if (_running)
                Thread.Sleep(2000); // retry connection
        }
    }

    private void Update()
    {
        while (_incoming.TryDequeue(out var line))
        {
            OnDataReceived?.Invoke(line);

            try
            {
                var envelope = JsonUtility.FromJson<MessageEnvelope>(line);
                if (envelope.type == "reset")
                {
                    Debug.Log("DataReceiver: reset message received from server.");
                    OnResetRequested?.Invoke();
                    continue;
                }
                if (envelope.type == "place")
                {
                    Debug.Log("DataReceiver: place marker message received from server.");
                    OnPlaceMarkerRequested?.Invoke();
                    continue;
                }
                if (envelope.type == "clear")
                {
                    Debug.Log("DataReceiver: clear markers message received from server.");
                    OnClearMarkersRequested?.Invoke();
                    continue;
                }
                if (envelope.type == "imu")
                {
                    var imu = JsonUtility.FromJson<ImuData>(line);
                    OnImuHeadingReceived?.Invoke(imu.heading);
                    continue;
                }
                if (envelope.type == "start-recording")
                {
                    Debug.Log("DataReceiver: start recording message received from server.");
                    OnStartRecordingRequested?.Invoke();
                    continue;
                }
                if (envelope.type == "stop-recording")
                {
                    Debug.Log("DataReceiver: stop recording message received from server.");
                    OnStopRecordingRequested?.Invoke();
                    continue;
                }
                if (envelope.type == "grid-size")
                {
                    var gridSize = JsonUtility.FromJson<GridSizeData>(line);
                    Debug.Log($"DataReceiver: grid-size message received from server ({gridSize.cells}).");
                    OnGridSizeChanged?.Invoke(gridSize.cells);
                    continue;
                }

                var fix = JsonUtility.FromJson<GpsData>(line);
                OnGpsFixReceived?.Invoke(fix);
            }
            catch (ArgumentException)
            {
                // Not JSON at all; subscribers to OnDataReceived still got
                // the raw text above.
            }
        }
    }

    private void OnDestroy()
    {
        _running = false;
        _client?.Close();
        _readThread?.Join(500);
    }
}
