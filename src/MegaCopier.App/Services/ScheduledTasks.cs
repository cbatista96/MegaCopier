using System.Diagnostics;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Con qué frecuencia se repite una tarea guardada.</summary>
public enum TaskFrequency
{
    Once,
    Daily,
    Weekly,
}

/// <summary>
/// Tareas guardadas (como FastCopy y TeraCopy Pro): una copia con todas sus opciones guardada como lista (.mclist) en
/// la carpeta "tasks", que el Programador de tareas de Windows lanza con "MegaCopier.exe run &lt;lista&gt;". Cada tarea
/// es del usuario actual: no hace falta ser administrador.
/// </summary>
internal static class ScheduledTasks
{
    public static string Folder { get; } = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "tasks");

    /// <summary>Las tareas guardadas (por su nombre).</summary>
    public static List<string> List() =>
        Directory.Exists(Folder)
            ? [.. Directory.EnumerateFiles(Folder, "*.mclist").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.CurrentCultureIgnoreCase)]
            : [];

    public static string PathOf(string name) => Path.Combine(Folder, name + ".mclist");

    /// <summary>Nombre válido como archivo y como tarea de Windows.</summary>
    public static string CleanName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/']).ToHashSet();
        string clean = new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]).Trim('.', ' ');
        return clean.Length == 0 ? "Copia" : clean[..Math.Min(clean.Length, 80)];
    }

    /// <summary>Guarda la copia como tarea y la programa. Devuelve el error, o null si fue bien.</summary>
    public static string? Create(CopyJobRecord record, string name, TaskFrequency frequency, DateTime start)
    {
        name = CleanName(name);
        Directory.CreateDirectory(Folder);
        string list = PathOf(name);
        var copy = CopyJobRecord.FromJson(record.ToJson())!;
        copy.StartAtUtc = null;
        Reports.SaveList(copy, list);

        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MegaCopier.exe");
        var arguments = new List<string>
        {
            "/Create", "/F", "/TN", TaskName(name),
            "/TR", $"\"{exe}\" run \"{list}\"",
            "/ST", start.ToString("HH:mm"),
        };
        switch (frequency)
        {
            case TaskFrequency.Daily:
                arguments.AddRange(["/SC", "DAILY"]);
                break;
            case TaskFrequency.Weekly:
                arguments.AddRange(["/SC", "WEEKLY", "/D", start.DayOfWeek.ToString()[..3].ToUpperInvariant()]);
                break;
            default:
                arguments.AddRange(["/SC", "ONCE", "/SD", start.ToString(DateFormat())]);
                break;
        }

        return Run(arguments);
    }

    public static string? Delete(string name)
    {
        string? error = Run(["/Delete", "/F", "/TN", TaskName(name)]);
        try
        {
            File.Delete(PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return error;
    }

    private static string TaskName(string name) => $"MegaCopier - {name}";

    /// <summary>schtasks /SD usa el formato de fecha corto del sistema (dd/MM/yyyy, MM/dd/yyyy...).</summary>
    private static string DateFormat()
    {
        string pattern = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        int day = pattern.IndexOf('d'), month = pattern.IndexOf('M'), year = pattern.IndexOf('y');
        return year < month ? "yyyy/MM/dd" : day < month ? "dd/MM/yyyy" : "MM/dd/yyyy";
    }

    private static string? Run(List<string> arguments)
    {
        if (App.TestMode)
        {
            // En pruebas no se crea ni se borra nada en el Programador de tareas del equipo: solo se anota.
            App.TestLog("schtasks.log", "schtasks " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            return null;
        }

        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info)!;
            string error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            return process.ExitCode == 0 ? null : error.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
