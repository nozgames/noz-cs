//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using Foundation;
using NoZ.Editor;
using NoZ.Platform;
using UIKit;

iOSPlatformSetup.Init();

UIApplication.Main(args, null, typeof(EditorAppDelegate));

[Register("EditorAppDelegate")]
public class EditorAppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var projectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Project");
        Directory.CreateDirectory(projectPath);

        EditorApplication.Run(new EditorApplicationConfig
        {
            ProjectPath = projectPath,
            ResourceAssembly = Assembly.GetExecutingAssembly(),
        }, []);

        return true;
    }

    public override void WillTerminate(UIApplication application)
    {
        base.WillTerminate(application);
        Application.Shutdown();     
    }
}
