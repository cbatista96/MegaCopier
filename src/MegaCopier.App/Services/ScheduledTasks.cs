using System.Diagnostics;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Con qué frecuencia se repite una tarea guardada.</summary>
public enum TaskFrequency
{
    Once,
    Daily,
    Weekly,
    Monthly,

    /// <summary>Al conectar el disco (el USB de destino, o el de origen si es él el extraíble).</summary>
    OnDevice,
}

/// <summary>
/// Tareas guardadas: una copia con todas sus opciones guardada como lista (.mclist) en
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
    public static string? Create(CopyJobRecord record, string name, TaskFrequency frequency, DateTime start, IReadOnlyDictionary<string, string>? morePairs = null, bool withoutSession = false)
    {
        name = CleanName(name);
        Directory.CreateDirectory(Folder);
        string list = PathOf(name);
        var copy = CopyJobRecord.FromJson(record.ToJson())!;
        copy.StartAtUtc = null;
        foreach (var (source, destination) in morePairs ?? new Dictionary<string, string>())
        {
            copy.ExtraTargets[source] = destination; // otra pareja de carpetas en la misma tarea
        }

        Engine.Jobs.Volumes.Remember(copy); // los discos, por su número de serie: si cambia la letra, la tarea sigue
        if (frequency == TaskFrequency.OnDevice)
        {
            // La lanza MegaCopier al ver el disco (Services/DeviceTasks), sin el Programador de tareas.
            if (TriggerVolume(copy) is not { } trigger)
            {
                return L.Get("Task_NoDevice");
            }

            copy.StartOnVolume = trigger.Serial;
            Reports.SaveList(copy, list);
            return null;
        }

        copy.StartOnVolume = "";
        Reports.SaveList(copy, list);

        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MegaCopier.exe");
        if (withoutSession)
        {
            // Sin sesión no hay escritorio para la ventana: la hace mcopy, que viene junto a la app.
            exe = Path.Combine(AppContext.BaseDirectory, "mcopy.exe");
            if (!File.Exists(exe))
            {
                return L.Get("Task_NoCli");
            }
        }

        // Con una definición XML (y no con /SC) se puede pedir "ejecutar en cuanto se pueda si se pasó la hora": si el
        // equipo estaba apagado o suspendido a esa hora, la copia se hace al volver.
        string xml = Path.Combine(Folder, name + ".task.xml");
        File.WriteAllText(xml, Definition(exe, $"run \"{list}\"", frequency, start, withoutSession), System.Text.Encoding.Unicode);
        return Run(["/Create", "/F", "/TN", TaskName(name), "/XML", xml]);
    }

    /// <summary>La tarea en el formato del Programador de tareas: disparador, "ponerse al día" y la orden.</summary>
    internal static string Definition(string exe, string arguments, TaskFrequency frequency, DateTime start, bool withoutSession = false)
    {
        string boundary = start.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        string trigger = frequency switch
        {
            TaskFrequency.Daily => $"<CalendarTrigger><StartBoundary>{boundary}</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>",
            TaskFrequency.Weekly => $"<CalendarTrigger><StartBoundary>{boundary}</StartBoundary><ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek><{start.DayOfWeek} /></DaysOfWeek></ScheduleByWeek></CalendarTrigger>",
            TaskFrequency.Monthly => $"<CalendarTrigger><StartBoundary>{boundary}</StartBoundary><ScheduleByMonth><DaysOfMonth><Day>{Math.Min(start.Day, 28)}</Day></DaysOfMonth>"
                + "<Months><January /><February /><March /><April /><May /><June /><July /><August /><September /><October /><November /><December /></Months></ScheduleByMonth></CalendarTrigger>",
            _ => $"<TimeTrigger><StartBoundary>{boundary}</StartBoundary></TimeTrigger>",
        };
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
            + "<RegistrationInfo><Description>MegaCopier</Description></RegistrationInfo>"
            + $"<Triggers>{trigger}</Triggers>"
            // S4U: «ejecutar aunque el usuario no haya iniciado sesión», sin guardar su contraseña (sin acceso a carpetas
            // de red que pidan otra cuenta). InteractiveToken: solo con la sesión abierta, con la ventana de MegaCopier.
            + $"<Principals><Principal id=\"Author\">{(withoutSession ? $"<UserId>{System.Security.SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName)}</UserId>" : "")}<LogonType>{(withoutSession ? "S4U" : "InteractiveToken")}</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"
            + "<Settings><StartWhenAvailable>true</StartWhenAvailable><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
            + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy></Settings>"
            + $"<Actions Context=\"Author\"><Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command><Arguments>{System.Security.SecurityElement.Escape(arguments)}</Arguments></Exec></Actions>"
            + "</Task>";
    }

    /// <summary>
    /// El disco que dispara la tarea: el de destino si es extraíble (el USB de las copias), si no el primero de origen
    /// que lo sea (la tarjeta de la cámara); si ninguno lo es, el de destino.
    /// </summary>
    private static Engine.Jobs.VolumeRef? TriggerVolume(CopyJobRecord record)
    {
        static bool Removable(Engine.Jobs.VolumeRef volume)
        {
            try
            {
                return new DriveInfo(volume.Root).DriveType == DriveType.Removable;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var destination = record.Volumes.FirstOrDefault(v => record.Destination.StartsWith(v.Root, StringComparison.OrdinalIgnoreCase));
        var sources = record.Volumes.Where(v => !ReferenceEquals(v, destination)).ToList();
        return destination is not null && Removable(destination) ? destination
            : sources.FirstOrDefault(Removable) ?? destination ?? sources.FirstOrDefault();
    }

    public static string? Delete(string name)
    {
        // Las de «al conectar el disco» no están en el Programador de tareas.
        bool scheduled = File.Exists(Path.Combine(Folder, name + ".task.xml"));
        string? error = scheduled ? Run(["/Delete", "/F", "/TN", TaskName(name)]) : null;
        try
        {
            File.Delete(PathOf(name));
            File.Delete(Path.Combine(Folder, name + ".task.xml"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return error;
    }

    private static string TaskName(string name) => $"MegaCopier - {name}";

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
