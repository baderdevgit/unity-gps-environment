using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

// Periodically captures a Camera's view and uploads it as JPEG to the
// server's /snapshot endpoint, so the mobile control page can show a
// low-frequency preview. Token must match Server/Program.cs's authToken.
public class CameraSnapshotUploader : MonoBehaviour
{
    [SerializeField] private Camera sourceCamera;
    [SerializeField] private string serverHost = "127.0.0.1";
    [SerializeField] private int serverPort = 5003;
    [SerializeField] private string authToken = "changeme-please-set-a-real-token";
    [SerializeField] private int captureWidth = 480;
    [SerializeField] private int captureHeight = 270;
    [SerializeField] private float intervalSeconds = 3f;
    [SerializeField] [Range(1, 100)] private int jpegQuality = 85;

    private void Start()
    {
        // Without this, Unity throttles rendering/coroutines whenever the
        // window loses focus, which stalls snapshot capture entirely.
        Application.runInBackground = true;

        if (sourceCamera == null)
            sourceCamera = Camera.main;

        StartCoroutine(CaptureLoop());
    }

    private IEnumerator CaptureLoop()
    {
        var renderTexture = new RenderTexture(captureWidth, captureHeight, 16);
        var readTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

        while (true)
        {
            yield return new WaitForSeconds(intervalSeconds);

            if (sourceCamera == null)
                continue;

            var previousTarget = sourceCamera.targetTexture;
            var previousActive = RenderTexture.active;

            // Render()+ReadPixels()+EncodeToJPG() all run synchronously on
            // the main thread, and ReadPixels (GPU->CPU readback) is a known
            // frame-hitch source in Unity - unlike the upload itself (below),
            // which yields through UnityWebRequest and never blocks. Timing
            // this separately from the [PERF] frame-hitch check in
            // DataReceiver isolates whether snapshot capture specifically is
            // the cause of any hitch that shows up there.
            var sw = Stopwatch.StartNew();

            sourceCamera.targetTexture = renderTexture;
            sourceCamera.Render();
            RenderTexture.active = renderTexture;
            readTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            readTexture.Apply();

            sourceCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            byte[] jpg = readTexture.EncodeToJPG(jpegQuality);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 50)
                Debug.LogWarning($"[PERF] Snapshot capture+encode took {sw.ElapsedMilliseconds}ms.");

            yield return Upload(jpg);
        }
    }

    private IEnumerator Upload(byte[] jpg)
    {
        string url = $"http://{serverHost}:{serverPort}/snapshot?token={UnityWebRequest.EscapeURL(authToken)}";
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(jpg);
        req.uploadHandler.contentType = "image/jpeg";
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"Snapshot upload failed: {req.error}");
    }
}
