//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz;

Application.Init(new ApplicationConfig
{
    Title = "NoZ Editor",
    Width = 1600,
    Height = 900,
    
    Render = new RenderConfig
    {
        MaxCommands = 2048
    },
    
    Vtable = new ApplicationVtable
    {
        Update = Editor.Update
    }
});

Editor.Init();

Application.Run();

Editor.Shutdown();
Application.Shutdown();
