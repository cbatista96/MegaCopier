using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace MegaCopier.Engine.Diagnostics;

/// <summary>Lee únicamente fallos de esta sesión y esta ruta. No necesita EventLog ni reflexión en AOT.</summary>
public static partial class NativeCrashEvents
{
    [LibraryImport("wevtapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint EvtQuery(nint session, string path, string query, int flags);
    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNext(nint result, int count, out nint item, int timeout, int flags, out int returned);
    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtRender(nint context, nint item, int flags, int bytes, nint buffer, out int used, out int properties);
    [LibraryImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(nint handle);

    public static NativeFault? Find(CrashRun run)
    {
        nint query = 0;
        try
        {
            string time = run.Started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            query = EvtQuery(0, "Application", $"*[System[Provider[@Name='Application Error'] and EventID=1000 and TimeCreated[@SystemTime>='{time}']]]", 0x201);
            if (query == 0) return null;
            for (int i = 0; i < 64 && EvtNext(query, 1, out nint item, 100, 0, out _); i++)
            {
                try
                {
                    EvtRender(0, item, 1, 0, 0, out int used, out _);
                    if (used <= 0 || used > 262144) continue;
                    nint buffer = Marshal.AllocHGlobal(used);
                    try
                    {
                        if (EvtRender(0, item, 1, used, buffer, out _, out _) && Marshal.PtrToStringUni(buffer) is { } xml && Parse(xml, run) is { } fault) return fault;
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { EvtClose(item); }
            }
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) { }
        finally { if (query != 0) EvtClose(query); }
        return null;
    }
    public static NativeFault? Parse(string xml, CrashRun run)
    {
        using var input = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 262144 });
        var document = XDocument.Load(input); XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var system = document.Root?.Element(ns + "System");
        if ((string?)system?.Element(ns + "EventID") != "1000" || (string?)system?.Element(ns + "Provider")?.Attribute("Name") != "Application Error"
            || !DateTimeOffset.TryParse((string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime"), out var time) || time < run.Started) return null;
        var data = document.Descendants(ns + "Data").ToArray();
        string Get(string key, int index) => data.FirstOrDefault(x => (string?)x.Attribute("Name") == key)?.Value ?? data.ElementAtOrDefault(index)?.Value ?? "";
        if (!Get("AppName", 0).Equals("MegaCopier.exe", StringComparison.OrdinalIgnoreCase)
            || !Get("AppPath", 10).Equals(run.Executable, StringComparison.OrdinalIgnoreCase)) return null;
        string pid = Get("ProcessId", 8);
        bool hex = pid.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (run.Pid > 0 && (!int.TryParse(hex ? pid[2..] : pid, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value != run.Pid)) return null;
        string code = CrashPrivacy.Hex(Get("ExceptionCode", 6)), offset = CrashPrivacy.Hex(Get("FaultingOffset", 7));
        return code.Length == 0 || offset.Length == 0 ? null : new(CrashPrivacy.Module(Get("ModuleName", 3)), code, offset);
    }
}
