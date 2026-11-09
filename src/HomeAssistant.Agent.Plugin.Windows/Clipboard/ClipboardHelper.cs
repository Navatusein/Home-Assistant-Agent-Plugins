using DataObject = System.Windows.Forms.DataObject;
using FormsClipboard = System.Windows.Forms.Clipboard;

namespace HomeAssistant.Agent.Plugin.Windows.Clipboard;

internal static class ClipboardHelper
{
    public static string GetText()
    {
        return RunSta(() =>
        {
            try
            {
                return FormsClipboard.ContainsText() ? FormsClipboard.GetText() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    public static void SetText(string text)
    {
        RunSta(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    FormsClipboard.Clear();
                else
                    FormsClipboard.SetText(text);
            }
            catch
            {
                // Clipboard can be locked by another process; ignore.
            }

            return true;
        });
    }

    // Text that came from outside the PC: kept out of the Win+V history, the
    // cloud clipboard sync and clipboard monitors. The formats are documented
    // in "Clipboard Formats" (CanIncludeInClipboardHistory and friends).
    public static bool SetPrivateText(string text)
    {
        return RunSta(() =>
        {
            try
            {
                var data = new DataObject();

                data.SetText(text);
                data.SetData("ExcludeClipboardContentFromMonitorProcessing", Dword(0));
                data.SetData("CanIncludeInClipboardHistory", Dword(0));
                data.SetData("CanUploadToCloudClipboard", Dword(0));

                FormsClipboard.SetDataObject(data, copy: true);

                return true;
            }
            catch
            {
                // Clipboard can be locked by another process.
                return false;
            }
        });
    }

    private static MemoryStream Dword(int value)
    {
        return new MemoryStream(BitConverter.GetBytes(value));
    }

    // Clipboard access must happen on an STA thread; button handlers run on
    // arbitrary threads, so marshal each call onto a short-lived STA thread.
    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;

        var thread = new Thread(() => result = func())
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }
}
