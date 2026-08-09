using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

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

            sourceCamera.targetTexture = renderTexture;
            sourceCamera.Render();
            RenderTexture.active = renderTexture;
            readTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            readTexture.Apply();

            sourceCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            byte[] jpg = readTexture.EncodeToJPG(jpegQuality);
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
