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
        EditorApplication.Run(new EditorApplicationConfig
        {
            ResourceAssembly = Assembly.GetExecutingAssembly(),
        }, []);

        return true;
    }
}
