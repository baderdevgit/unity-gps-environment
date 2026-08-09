using UnityEngine;

public class Marker : MonoBehaviour
{
    [SerializeField]
    DataReceiver _dataReceiver;


    void Start()
    {        
        _dataReceiver.OnGpsFixReceived += fix =>
        {
            Debug.Log($"Lat: {fix.lat}, Lon: {fix.lon}, Status: {fix.fixStatus}");
            // e.g. move a GameObject based on fix.lat/fix.lon here
        };
    }
}