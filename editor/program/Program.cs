//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Editor;

IEditorStore? store = null;

for (var i = 0; i < args.Length; i++)
    if (args[i] == "--git")
        store = new GitStore(GitStore.DefaultClientId);

EditorApplication.Run(new EditorApplicationConfig
{
    Store = store,
}, args);
