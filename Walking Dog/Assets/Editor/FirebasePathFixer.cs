#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;

public class FirebasePathFixer : IPreprocessBuildWithReport
{

    public int callbackOrder { get { return 1000; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        string path = "Assets/Plugins/Android/settingsTemplate.gradle";
        if (File.Exists(path))
        {
            string content = File.ReadAllText(path);
            
            string badLine = "def unityProjectPath = $/file:///**DIR_UNITYPROJECT**/$.replace(\"\\\\\", \"/\")";
            
            string goodLine = "def unityProjectPath = $/file:///**DIR_UNITYPROJECT**/$.replace(\"\\\\\", \"/\").replace(\" \", \"%20\")";
            
            if (content.Contains(badLine))
            {
                content = content.Replace(badLine, goodLine);
                File.WriteAllText(path, content);
                UnityEngine.Debug.Log("FirebasePathFixer: Automatically fixed spaces in settingsTemplate.gradle for Firebase.");
            }
        }
    }
}
#endif
