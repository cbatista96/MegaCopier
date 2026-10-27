using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;

namespace MegaCopier.Shell;

/// <summary>
/// Carpetas abiertas en Total Commander, Directory Opus, Multi Commander, xplorer2, Explorer++ y XYplorer, para
/// ofrecerlas como destino igual que las del Explorador. Sin inyectar nada: Total Commander responde a WM_USER+50 con la
/// ventana de la ruta de cada panel (documentado en su ayuda), Directory Opus tiene "dopusrt.exe /info archivo,paths", y
/// los demás muestran la carpeta actual en el título de su ventana.
/// </summary>
public static partial class OtherFileManagers
{
    private const uint WmUser = 0x0400, WmGetText = 0x000D, WmGetTextLength = 0x000E;
    private const int LeftPath = 9, RightPath = 10;

    public static List<string> Open()
    {
        var folders = new List<string>();
        try
        {
            folders.AddRange(TotalCommander());
            folders.AddRange(DirectoryOpus());
            folders.AddRange(TitledManagers());
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or XmlException or InvalidOperationException)
        {
        }

        return [.. folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> TotalCommander()
    {
        var windows = new List<nint>();
        for (nint window = FindWindowExW(0, 0, "TTOTAL_CMD", null); window != 0; window = FindWindowExW(0, window, "TTOTAL_CMD", null))
        {
            windows.Add(window);
        }

        foreach (nint window in windows)
        {
            foreach (int panel in new[] { LeftPath, RightPath })
            {
                if (SendMessageTimeoutW(window, WmUser + 50, panel, 0, 0x2 /* SMTO_ABORTIFHUNG */, 500, out nint control) != 0 && control != 0
                    && TotalCommanderPath(Text(control)) is { } path)
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>"c:\Fotos\*.*" (la ruta del panel con su filtro) → "c:\Fotos".</summary>
    public static string? TotalCommanderPath(string text)
    {
        text = text.Trim();
        if (text.Length < 2)
        {
            return null;
        }

        int slash = text.LastIndexOf('\\');
        if (slash >= 0 && text.AsSpan(slash).IndexOfAny('*', '?') >= 0)
        {
            text = text[..slash];
        }

        return text.Length == 2 && text[1] == ':' ? text + "\\" : text.TrimEnd('\\') is { Length: > 0 } trimmed && trimmed.EndsWith(':') ? trimmed + "\\" : text.TrimEnd('\\');
    }

    private static unsafe string Text(nint control)
    {
        if (SendMessageTimeoutW(control, WmGetTextLength, 0, 0, 0x2, 500, out nint length) == 0 || length <= 0 || length > 32767)
        {
            return "";
        }

        char* buffer = stackalloc char[(int)length + 1];
        return SendMessageTimeoutW(control, WmGetText, length + 1, (nint)buffer, 0x2, 500, out nint copied) == 0 ? "" : new string(buffer, 0, (int)copied);
    }

    private static IEnumerable<string> DirectoryOpus()
    {
        if (Process.GetProcessesByName("dopus").Length == 0 || DopusRt() is not { } tool)
        {
            return [];
        }

        string output = Path.Combine(Path.GetTempPath(), $"megacopier-dopus-{Environment.ProcessId}.xml");
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(tool, $"/info \"{output}\",paths") { UseShellExecute = false, CreateNoWindow = true })!)
            {
                if (!process.WaitForExit(3000))
                {
                    return [];
                }
            }

            return File.Exists(output) ? DirectoryOpusPaths(File.ReadAllText(output)) : [];
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Las rutas del XML de "dopusrt /info ...,paths": &lt;results&gt;&lt;path ...&gt;C:\...&lt;/path&gt;...</summary>
    public static List<string> DirectoryOpusPaths(string xml)
    {
        var paths = new List<string>();
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "path")
            {
                string path = reader.ReadElementContentAsString().Trim();
                if (path.Length > 0)
                {
                    paths.Add(path);
                }

                continue; // ReadElementContentAsString ya dejó el lector en el nodo siguiente
            }

            reader.Read();
        }

        return paths;
    }

    /// <summary>Procesos de los gestores que llevan la carpeta actual en el título de la ventana.</summary>
    private static readonly string[] TitledProcesses =
        ["Explorer++", "ExplorerPlusPlus", "xplorer2", "xplorer2_64", "xplorer2_UC", "xplorer2_lite", "MultiCommander", "XYplorer"];

    private static IEnumerable<string> TitledManagers()
    {
        foreach (string name in TitledProcesses)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    string title;
                    try
                    {
                        title = process.MainWindowTitle;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    if (PathInTitle(title) is { } path)
                    {
                        yield return path;
                    }
                }
            }
        }
    }

    /// <summary>
    /// La ruta que muestra un título como «C:\Fotos - Explorer++», «xplorer² - D:\Trabajo» o «XYplorer 26.0 - C:\Users»:
    /// el trozo (separado por « - », « — », « | » o corchetes) que empieza por una unidad o por \\servidor.
    /// </summary>
    public static string? PathInTitle(string title)
    {
        foreach (string raw in title.Split([" - ", " — ", " – ", " | ", "[", "]"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = raw.Trim().TrimEnd('*', ' ');
            bool drive = part.Length >= 3 && char.IsAsciiLetter(part[0]) && part[1] == ':' && part[2] == '\\';
            bool unc = part.StartsWith(@"\\", StringComparison.Ordinal) && part.Length > 2;
            if (drive || unc)
            {
                return part.Length > 3 ? part.TrimEnd('\\') : part;
            }
        }

        return null;
    }

    private static string? DopusRt()
    {
        foreach (string folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath))
        {
            string candidate = Path.Combine(folder, "GPSoftware", "Directory Opus", "dopusrt.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
