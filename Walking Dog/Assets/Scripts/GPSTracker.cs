using System.Collections;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;

using UnityEngine.Android;
#endif   
public class GPSTracker : MonoBehaviour
{
    [Header("GPS Settings")]
    [SerializeField]
    private float desiredAccuracyInMeters = 1f; // how accurate the gps should be, measured in meaters

    //update distance: distance needed for gps to update
    //update interval: time needed before gps updates
    [SerializeField]
    private float updateDistance = .5f;
    [SerializeField]
    private float updateInterval = 1f;



    private bool gpsRunning = false;
    void Start()
    {
        StartCoroutine(StartGPS());

    }
    private IEnumerator StartGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    
      
   if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
      {
          Permission.RequestUserPermission(Permission.FineLocation);
          yield return new WaitForSeconds(1f);

          //another check if user denied request 
          if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
          {
              Debug.LogWarning("Location permission was denied. GPS cannot start.");
              yield break;
          }
      }

if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("GPS is disabled on the device.");

            // Stop the coroutine because there is no GPS data
            // available to collect.
            yield break;
            }
            
        Debug.Log("Starting GPS...");


     
        Input.location.Start(
            desiredAccuracyInMeters,
            updateDistance
        );


       // gps initialization wait time
        int waitTime = 20;

        while (Input.location.status ==
               LocationServiceStatus.Initializing &&
               waitTime > 0)
        {
            Debug.Log("Waiting for GPS...");

            // Wait one second before checking again.
            yield return new WaitForSeconds(1f);

            waitTime--;
        }

        //timeout after 20 seconds
        if (waitTime <= 0)
        {
            Debug.LogWarning("GPS initialization timed out.");

            yield break;
        }

        //gps failed
        if (Input.location.status ==
            LocationServiceStatus.Failed)
        {
            Debug.LogError("Unable to determine device location.");

            yield break;
        }


       //gps runs
        gpsRunning = true;

        Debug.Log("GPS successfully started!");


     
        while (gpsRunning)
        {
            // Get the latest GPS information and send it to
            // StepCountAndGpsManager.
           
            
            
            UpdateGPSData();

            
            yield return new WaitForSeconds(updateInterval);
        }


#else

        // This code is executed when running inside the Unity
        // Editor or on a non-Android platform.
        //
        // We cannot test the real phone GPS inside the normal
        // Unity Editor this way.
        Debug.Log("GPS Tracker requires a physical Android device.");

        yield break;

#endif

    }




    private void UpdateGPSData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        // Make sure the GPS tracker is actually running.
        if (!gpsRunning)
        {
            return;
        }


        // --------------------------------------------------------
        // GET LATEST LOCATION
        // --------------------------------------------------------

        LocationInfo location = Input.location.lastData;


        float latitude = location.latitude;
        float longitude = location.longitude;


       
        Debug.Log(
            $"GPS Location: {latitude}, {longitude}"
        );


   
        if (StepCountAndGpsManager.Instance != null)
        {
         
            StepCountAndGpsManager.Instance.setGPSLonAndLat(
                latitude,
                longitude
            );
        }
        else
        {
            
            Debug.LogWarning( "StepCountAndGpsManager.Instance is null" );
        }

#endif
    }




 
    private void OnDestroy()
    {
        // Make sure the GPS service is stopped when this
        // GameObject is destroyed.
        StopGPS();
    }


    // ============================================================
    // STOP GPS
    // ============================================================

    private void StopGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        // Only attempt to stop the GPS if our tracker says
        // that it is currently running.
        if (gpsRunning)
        {
            // Stop Unity's location service.
            Input.location.Stop();

            // Mark the GPS tracker as no longer running.
            gpsRunning = false;

            Debug.Log("GPS stopped.");
        }

#endif
    }





}
