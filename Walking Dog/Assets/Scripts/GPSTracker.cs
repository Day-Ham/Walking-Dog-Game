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
    private float desiredAccuracyInMeters = 5f; // how accurate the gps should be, measured in meaters

    //update distance: distance needed for gps to update
    //update interval: time needed before gps updates
    [SerializeField]
    private float updateDistance = 3f;
    [SerializeField]
    private float updateInterval = 1.5f;



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


    // update latest location here 

        LocationInfo location = Input.location.lastData;


        float latitude = location.latitude;
        float longitude = location.longitude;
        float horizontalAccuracy = location.horizontalAccuracy;

       
        Debug.Log(
            $"GPS Location: {latitude}, {longitude}"
        );


     if (horizontalAccuracy > 10f)
  {
      Debug.LogWarning($"GPS reading ignored: accuracy inaccurate.");
      return;
  }

        if (StepCountAndGpsManager.Instance != null)
        {
         
            StepCountAndGpsManager.Instance.setGPSLonAndLat(
                latitude,
                longitude,
                horizontalAccuracy
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
        
        StopGPS();
    }



    private void StopGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

     
        if (gpsRunning)
        {
            Input.location.Stop();

            
            gpsRunning = false;

            Debug.Log("GPS stopped.");
        }

#endif
    }





}
