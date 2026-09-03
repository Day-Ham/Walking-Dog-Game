using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class OSMMapManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The RawImage component to display the map tile")]
    public RawImage mapImage;

    [Header("Map Settings")]
    [Tooltip("Zoom level (0-19). Higher is closer. 15 is good for neighborhoods.")]
    public int zoomLevel = 15;
    

    [Header("Mobile GPS Settings")]
    [Tooltip("If true, uses the device's real GPS instead of the test coordinates above.")]
    public bool useDeviceGPS = true;

    private void Start()
    {
        if (mapImage == null)
        {
            mapImage = GetComponent<RawImage>();
            if (mapImage == null)
            {
                Debug.LogError("OSMMapManager needs a RawImage to display the map!");
                return;
            }
        }

        if (useDeviceGPS)
        {
            StartCoroutine(StartLocationService());
        }
        else
        {
            // Central Park, NY test coordinates
            LoadMapTile(40.7812, -73.9665, zoomLevel);
        }
    }

    private IEnumerator StartLocationService()
    {
        // First, check if user has location service enabled
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("Location services are not enabled by the user. Using test coordinates.");
            LoadMapTile(40.7812, -73.9665, zoomLevel);
            yield break;
        }

        // Start service before querying location
        Input.location.Start(10f, 10f); // 10 meters accuracy, 10 meters update distance

        // Wait until service initializes
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        // Service didn't initialize in 20 seconds
        if (maxWait < 1)
        {
            Debug.LogError("Location service initialization timed out.");
            yield break;
        }

        // Connection has failed
        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("Unable to determine device location.");
            yield break;
        }
        else
        {
            // Access granted and location value could be retrieved
            double latitude = Input.location.lastData.latitude;
            double longitude = Input.location.lastData.longitude;
            Debug.Log($"GPS Location found: Lat: {latitude}, Lon: {longitude}");
            
            LoadMapTile(latitude, longitude, zoomLevel);
        }
    }

    public void LoadMapTile(double lat, double lon, int zoom)
    {
        // Convert Lat/Lon to OSM Tile X and Y
        int tileX = LonToTileX(lon, zoom);
        int tileY = LatToTileY(lat, zoom);

        string url = $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png";
        Debug.Log("Fetching map from: " + url);
        
        StartCoroutine(DownloadAndApplyMap(url));
    }

    private IEnumerator DownloadAndApplyMap(string url)
    {
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
        {
            // OSM strictly requires a valid User-Agent to prevent abuse
            uwr.SetRequestHeader("User-Agent", "WalkingDogGame_MobileTest/1.0 (contact@yourdomain.com)");

            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error downloading map: " + uwr.error);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                
                //pixelated look
                texture.filterMode = FilterMode.Bilinear; 
                
                mapImage.texture = texture;
                
                // Adjust RawImage aspect ratio to match the texture
                AspectRatioFitter fitter = mapImage.gameObject.GetComponent<AspectRatioFitter>();
                if (fitter == null) fitter = mapImage.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = 1f;
            }
        }
    }

    // --- Math functions to convert Lat/Lon to Tile X/Y ---
    // Standard Slippy Map math: https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames#C.23
    
    private int LonToTileX(double lon, int zoom)
    {
        return (int)(Math.Floor((lon + 180.0) / 360.0 * (1 << zoom)));
    }

    private int LatToTileY(double lat, int zoom)
    {
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));
    }
}
