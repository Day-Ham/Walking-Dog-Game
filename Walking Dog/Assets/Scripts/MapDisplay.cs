using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;








public class MapDisplay : MonoBehaviour
{
  [SerializeField] private RawImage mapImg;
    [Header("stadia map Api")]
    [SerializeField] private string apiKey = "457ded58-fe3c-49e8-9d6b-14ebc82f084d";
    //testing coordinates for the map
    [Header("Test Location")]
    [SerializeField] private float latitude = 14.5995f;
    [SerializeField] private float longitude = 120.9842f;
    [SerializeField] private int zoom = 15;

    private IEnumerator Start()
    {
        yield return DownloadMap();
    }

    private IEnumerator DownloadMap()
    {
        // Convert GPS coordinates into Stadia tile coordinates.
        Vector2 tile = GPSToTile(latitude, longitude, zoom);

        int x = Mathf.FloorToInt(tile.x);
        int y = Mathf.FloorToInt(tile.y);

        string url =
            $"https://tiles.stadiamaps.com/tiles/alidade_smooth/{zoom}/{x}/{y}.png?api_key={apiKey}";

        Debug.Log($"GPS: {latitude}, {longitude}");
        Debug.Log($"Tile: Z={zoom}, X={x}, Y={y}");
        Debug.Log($"URL: {url}");

        using UnityWebRequest request =
            UnityWebRequestTexture.GetTexture(url);

        yield return request.SendWebRequest();

        Debug.Log($"HTTP Status: {request.responseCode}");
        Debug.Log($"Result: {request.result}");
        Debug.Log($"Error: {request.error}");

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Stadia request failed.");
            yield break;
        }

        Texture2D texture =
            DownloadHandlerTexture.GetContent(request);

        if (texture == null)
        {
            Debug.LogError("Texture is NULL.");
            yield break;
        }

        Debug.Log($"Texture size: {texture.width} x {texture.height}");

        mapImg.texture = texture;
        mapImg.color = Color.white;

        Debug.Log("Stadia map assigned to RawImage.");
    }

    private static Vector2 GPSToTile(
        float latitude,
        float longitude,
        int zoom)
    {
        float latRad = latitude * Mathf.Deg2Rad;
        float n = Mathf.Pow(2f, zoom);

        float x = (longitude + 180f) / 360f * n;

        float y = (1f -
                   Mathf.Log(Mathf.Tan(latRad) + 1f / Mathf.Cos(latRad))
                   / Mathf.PI) / 2f * n;

        return new Vector2(x, y);
    }
}