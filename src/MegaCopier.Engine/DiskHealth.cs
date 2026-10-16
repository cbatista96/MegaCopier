using System.Diagnostics;
using System.Text.Json;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>Salud de un disco físico según Windows (SMART y el propio controlador).</summary>
public sealed record DiskStatus(int Number, string Name, DiskHealthState Health, string Detail)
{
    public bool IsBad => Health != DiskHealthState.Healthy && Health != DiskHealthState.Unknown;
}

public enum DiskHealthState
{
    Healthy,
    Warning,
    Unhealthy,
    Unknown,
}

/// <summary>
/// Pregunta a Windows (MSFT_PhysicalDisk, el mismo dato que "Configuración > Discos") si algún disco avisa de fallo:
/// un disco con "fallo previsto" o "degradado" puede perder datos pronto. No necesita permisos de administrador.
/// </summary>
public static class DiskHealth
{
    /// <summary>Todos los discos físicos. Tarda alrededor de un segundo (PowerShell): llamar fuera de la interfaz.</summary>
    public static List<DiskStatus> Query()
    {
        var disks = new List<DiskStatus>();
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("@(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk | " +
                "Select-Object DeviceId, FriendlyName, HealthStatus, @{n='Status';e={@($_.OperationalStatus)}}) | ConvertTo-Json -Compress");
            using var process = Process.Start(info)!;
            string json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            disks.AddRange(Parse(json));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or JsonException)
        {
        }

        return Simulated(disks);
    }

    /// <summary>Disco físico de una ruta (null en red o si no se sabe).</summary>
    public static int? DiskOf(string path) => VolumeInfo.DiskNumber(path) is { } disk ? (int)disk : null;

    internal static List<DiskStatus> Parse(string json)
    {
        var disks = new List<DiskStatus>();
        if (json.Trim().Length == 0)
        {
            return disks;
        }

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        foreach (var disk in items)
        {
            int number = int.TryParse(disk.GetProperty("DeviceId").ToString(), out int n) ? n : -1;
            string name = disk.TryGetProperty("FriendlyName", out var friendly) ? friendly.GetString() ?? "" : "";
            int health = disk.TryGetProperty("HealthStatus", out var h) && h.TryGetInt32(out int value) ? value : 5;
            var codes = disk.TryGetProperty("Status", out var status) && status.ValueKind == JsonValueKind.Array
                ? status.EnumerateArray().Select(s => s.TryGetInt32(out int code) ? code : 0).ToList()
                : [];
            string detail = string.Join(", ", codes.Select(Describe).Where(d => d.Length > 0));
            var state = health switch
            {
                0 => DiskHealthState.Healthy,
                1 => DiskHealthState.Warning,
                2 => DiskHealthState.Unhealthy,
                _ => DiskHealthState.Unknown,
            };

            // "Fallo previsto" (SMART) cuenta aunque el estado general aún diga "correcto".
            if (state == DiskHealthState.Healthy && codes.Contains(5))
            {
                state = DiskHealthState.Warning;
            }

            disks.Add(new DiskStatus(number, name, state, detail));
        }

        return disks;
    }

    private static string Describe(int code) => code switch
    {
        3 => Texts.Get("E_Disk_Degraded"),
        5 => Texts.Get("E_Disk_Predictive"),
        6 => Texts.Get("E_Disk_Error"),
        7 => Texts.Get("E_Disk_Unrecoverable"),
        10 => Texts.Get("E_Disk_Stopped"),
        13 => Texts.Get("E_Disk_Lost"),
        0xD00B => Texts.Get("E_Disk_Transient"),
        0xD00D => Texts.Get("E_Disk_Metadata"),
        _ => "",
    };

    /// <summary>Pruebas: MEGACOPIER_TEST_DISKHEALTH="2=1" hace como si el disco 2 avisara de fallo.</summary>
    private static List<DiskStatus> Simulated(List<DiskStatus> disks)
    {
        if (Environment.GetEnvironmentVariable("MEGACOPIER_TEST") != "1"
            || Environment.GetEnvironmentVariable("MEGACOPIER_TEST_DISKHEALTH") is not { Length: > 2 } fake
            || fake.Split('=') is not [var disk, var health] || !int.TryParse(disk, out int number) || !int.TryParse(health, out int state))
        {
            return disks;
        }

        return [.. disks.Select(d => d.Number == number ? d with { Health = (DiskHealthState)state, Detail = Texts.Get("E_Disk_Predictive") } : d)];
    }
}
