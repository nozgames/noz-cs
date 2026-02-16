//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices.JavaScript;

namespace NoZ.Platform.Web;

public static partial class WebStorageInterop
{
    [JSImport("globalThis.localStorage.getItem")]
    public static partial string? GetItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    public static partial void SetItem(string key, string value);
}
