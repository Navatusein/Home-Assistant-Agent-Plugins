using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.MacOS.Native;

// Just enough of the Objective-C runtime to call AppKit (NSPasteboard,
// NSEvent). objc_msgSend is declared once per signature: on arm64 the call
// must match the method's real prototype.
internal static class ObjC
{
    public const string Lib = "/usr/lib/libobjc.A.dylib";

    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    // The desktop app has AppKit loaded already; this only makes sure.
    private static readonly Lazy<bool> AppKitLoaded = new(() => NativeLibrary.TryLoad(AppKit, out _));

    [DllImport(Lib)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Lib)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Lib)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(Lib)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern long SendLong(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern byte SendBool(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2);

    public static IntPtr Class(string name)
    {
        _ = AppKitLoaded.Value;

        var cls = objc_getClass(name);

        return cls != IntPtr.Zero
            ? cls
            : throw new InvalidOperationException($"Objective-C class {name} is not available.");
    }

    public static IntPtr Selector(string name) => sel_registerName(name);

    // AppKit hands back autoreleased objects; outside the main thread nothing
    // drains them unless a pool is in place.
    public static T WithAutoreleasePool<T>(Func<T> action)
    {
        var pool = objc_autoreleasePoolPush();

        try
        {
            return action();
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}
