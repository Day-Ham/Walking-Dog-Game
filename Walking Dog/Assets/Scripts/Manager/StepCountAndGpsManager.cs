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

    public void setGPSLonAndLat(float latitude, float longitude)
    {
        this.latitude = latitude;
        this.longitude = longitude;
    }

    public float getLatitude()
    {    return latitude;}
    public float getLongitude()
    { return longitude; }

    }
