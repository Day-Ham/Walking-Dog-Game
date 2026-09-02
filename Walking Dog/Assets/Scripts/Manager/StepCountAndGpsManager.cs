using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;




public class StepCountAndGpsManager : MonoBehaviour
{
    //PURPOSE: class responsible for storing and sharing values of step and gps data across scenes and to be used and referenced by other scripts
    //step counter values
    [SerializeField]
    private int stepsCounted = 0;

    [SerializeField]
    private float latitude = 0.0f;
    [SerializeField] 
    private float longitude = 0.0f;





    [SerializeField] // store points as user walks here while route is recording via long and latitude values

     private List<Vector2> routePoints = new List<Vector2>();

    private string AccuracyStatus = "";
    /// MANAGER CODE
    /// 
    public static StepCountAndGpsManager Instance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }


    /// 

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    //step counter related function
    
    public void setStep(int steps)
    {
        stepsCounted = steps ;
    }
    public int getSteps()
    {
        return stepsCounted;
    }


    //gps related functions

    public void setGPSLonAndLat(float latitude, float longitude, float Accuracy)
    {
        this.latitude = latitude;
        this.longitude = longitude;
        if(Accuracy < 10f)
        {
            AccuracyStatus = "Accurate";
            // for testing add route here
            routePoints.Add(new Vector2(latitude, longitude));
        }
        else
        {
            AccuracyStatus = "Location accuracy inaccurate";
        }

    }

    public float getLatitude()
    {    return latitude;}
    public float getLongitude()
    { return longitude; }

    }


