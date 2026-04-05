//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using NoZ.Editor;
using NoZ.Platform;

iOSPlatformSetup.Init();
EditorApplication.Run(new EditorApplicationConfig
{
    ResourceAssembly = Assembly.GetExecutingAssembly(),
}, args);
