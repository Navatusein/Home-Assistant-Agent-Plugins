using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.MacOS.Native;

internal static unsafe class CoreFoundation
{
    public const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly Lazy<IntPtr> Handle = new(() => NativeLibrary.Load(Lib));

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public long Location;
        public long Length;
    }

    [DllImport(Lib)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(Lib)]
    private static extern long CFStringGetLength(IntPtr str);

    [DllImport(Lib)]
    private static extern void CFStringGetCharacters(IntPtr str, CFRange range, char* buffer);

    [DllImport(Lib)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, char* chars, long length);

    [DllImport(Lib)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr* keys, IntPtr* values, long count,
        IntPtr keyCallBacks, IntPtr valueCallBacks);

    public static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(Handle.Value, "kCFBooleanTrue"));

    // A CFStringRef — or an NSString, they are the same object — as a .NET
    // string. Does not release it.
    public static string? GetString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
            return null;

        var length = CFStringGetLength(cfString);

        if (length <= 0)
            return string.Empty;

        var buffer = new char[length];

        fixed (char* chars = buffer)
            CFStringGetCharacters(cfString, new CFRange { Location = 0, Length = length }, chars);

        return new string(buffer);
    }

    // Owned by the caller: CFRelease it.
    public static IntPtr CreateString(string value)
    {
        fixed (char* chars = value)
            return CFStringCreateWithCharacters(IntPtr.Zero, chars, value.Length);
    }

    // A one-entry CFDictionary of CF objects. Owned by the caller.
    public static IntPtr CreateDictionary(IntPtr key, IntPtr value)
    {
        var keyCallBacks = NativeLibrary.GetExport(Handle.Value, "kCFTypeDictionaryKeyCallBacks");
        var valueCallBacks = NativeLibrary.GetExport(Handle.Value, "kCFTypeDictionaryValueCallBacks");

        return CFDictionaryCreate(IntPtr.Zero, &key, &value, 1, keyCallBacks, valueCallBacks);
    }
}

// A CFString for the length of a using block.
internal readonly struct CFString : IDisposable
{
    public CFString(string value)
    {
        Handle = CoreFoundation.CreateString(value);
    }

    public IntPtr Handle { get; }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
            CoreFoundation.CFRelease(Handle);
    }
}
