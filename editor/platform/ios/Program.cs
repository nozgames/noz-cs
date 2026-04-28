//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using Foundation;
using NoZ;
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
        var projectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects");
        Directory.CreateDirectory(projectPath);

        EditorApplication.Run(new EditorApplicationConfig
        {
            ProjectPath = projectPath,
            EditorPath = AppContext.BaseDirectory,
            IsTablet = true,
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
