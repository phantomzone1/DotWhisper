using System.Runtime.InteropServices;

namespace DotWhisper.UI;

public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Clipboard locked by another process — skip
        }
    }
}
