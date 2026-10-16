using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MegaCopier.App.Services;

/// <summary>Momentos en que MegaCopier puede sonar.</summary>
public enum SoundEvent
{
    /// <summary>Una copia terminó bien.</summary>
    Done,

    /// <summary>Una copia terminó con errores o falló.</summary>
    Errors,

    /// <summary>Ya no queda ninguna copia en marcha.</summary>
    AllDone,

    /// <summary>Hace falta decidir algo (un archivo ya existe).</summary>
    Attention,
}

/// <summary>Sonidos elegidos: "" = silencio, "default" = el de Windows para ese momento, o la ruta de un archivo.</summary>
public sealed class SoundSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Volumen de 0 a 100.</summary>
    public int Volume { get; set; } = 80;

    /// <summary>No sonar con un juego o una presentación a pantalla completa, o con "No molestar".</summary>
    public bool QuietWhenBusy { get; set; } = true;

    public string Done { get; set; } = "default";

    public string Errors { get; set; } = "default";

    public string AllDone { get; set; } = "";

    public string Attention { get; set; } = "default";

    public string Get(SoundEvent e) => e switch
    {
        SoundEvent.Done => Done,
        SoundEvent.Errors => Errors,
        SoundEvent.AllDone => AllDone,
        _ => Attention,
    };

    public void Set(SoundEvent e, string value)
    {
        switch (e)
        {
            case SoundEvent.Done:
                Done = value;
                break;
            case SoundEvent.Errors:
                Errors = value;
                break;
            case SoundEvent.AllDone:
                AllDone = value;
                break;
            default:
                Attention = value;
                break;
        }
    }
}

/// <summary>Reproduce los sonidos (WAV, MP3, WMA, M4A...) con el volumen elegido, sin bloquear la interfaz.</summary>
internal static partial class Sounds
{
    private static MediaPlayer? _player;

    /// <summary>Sonidos de Windows (C:\Windows\Media) que se ofrecen en la lista.</summary>
    public static IReadOnlyList<string> WindowsSounds { get; } = LoadWindowsSounds();

    public static void Play(SoundSettings settings, SoundEvent e)
    {
        if (!settings.Enabled || (settings.QuietWhenBusy && UserIsBusy()))
        {
            return;
        }

        PlayFile(Resolve(settings.Get(e), e), settings.Volume);
    }

    /// <summary>Prueba un sonido desde Ajustes (suena aunque esté desactivado o haya pantalla completa).</summary>
    public static void Preview(string choice, SoundEvent e, int volume) => PlayFile(Resolve(choice, e), volume);

    /// <summary>Archivo que corresponde a una elección ("default" = el que Windows usa para ese tipo de aviso).</summary>
    public static string? Resolve(string choice, SoundEvent e)
    {
        if (string.IsNullOrWhiteSpace(choice))
        {
            return null;
        }

        if (choice != "default")
        {
            return File.Exists(choice) ? choice : null;
        }

        string scheme = e switch
        {
            SoundEvent.Errors => "SystemHand",
            SoundEvent.Attention => "SystemExclamation",
            SoundEvent.AllDone => "SystemNotification",
            _ => "SystemAsterisk",
        };
        return SchemeSound(scheme) ?? SchemeSound("Notification.Default") ?? Path.Combine(MediaFolder, "Windows Notify System Generic.wav");
    }

    private static string MediaFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    private static void PlayFile(string? path, int volume)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            if (_player is null)
            {
                _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts };
                if (App.TestMode)
                {
                    // Pruebas: deja constancia de que el sonido se abrió y terminó (o por qué falló).
                    string log = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "sound.log");
                    _player.MediaOpened += (p, _) => File.AppendAllText(log, $"abierto {p.Source} {p.PlaybackSession.NaturalDuration}\n");
                    _player.MediaEnded += (_, _) => File.AppendAllText(log, "terminado\n");
                    _player.MediaFailed += (_, e) => File.AppendAllText(log, $"fallo {e.Error} {e.ErrorMessage}\n");
                }
            }

            _player.Volume = Math.Clamp(volume, 0, 100) / 100.0;
            _player.Source = MediaSource.CreateFromUri(new Uri(path));
            _player.Play();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UriFormatException)
        {
            System.Diagnostics.Debug.WriteLine($"No se pudo reproducir {path}: {ex.Message}");
        }
    }

    /// <summary>El sonido que el usuario tiene configurado en Windows para un evento (Panel de control > Sonido).</summary>
    private static string? SchemeSound(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"AppEvents\Schemes\Apps\.Default\{name}\.Current");
            return key?.GetValue(null) is string value && value.Length > 0
                ? Environment.ExpandEnvironmentVariables(value)
                : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static List<string> LoadWindowsSounds()
    {
        try
        {
            return [.. Directory.EnumerateFiles(MediaFolder, "*.wav").Order(StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Pantalla completa, presentación o "No molestar" (SHQueryUserNotificationState).</summary>
    private static bool UserIsBusy()
    {
        if (SHQueryUserNotificationState(out int state) != 0)
        {
            return false;
        }

        // QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4, QUNS_QUIET_TIME = 6
        return state is 2 or 3 or 4 or 6;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
