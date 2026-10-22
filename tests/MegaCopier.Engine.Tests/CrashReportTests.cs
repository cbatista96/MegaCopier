using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MegaCopier.Engine.Diagnostics;

namespace MegaCopier.Engine.Tests;

public sealed class CrashReportTests
{
    [Theory]
    [InlineData("{\"SendCrashReportsAutomatically\":true}",0)]
    [InlineData("{\"SendCrashReportsAutomatically\":false}",1)]
    [InlineData("{}",2)]
    [InlineData("{\"SendCrashReportsAutomatically\":\"true\"}",2)]
    [InlineData("{malformado",2)]
    public void Instalador_lee_la_eleccion_sin_confundir_ausencia_o_corrupcion(string value,int expected)
    {
        using var temp=new TempDir(); string path=Path.Combine(temp.Path,"settings.json");
        Assert.Equal(2,CrashInstallerChoice.Read(path)); File.WriteAllText(path,value);
        Assert.Equal(expected,CrashInstallerChoice.Read(path)); Assert.Equal(value,File.ReadAllText(path));
    }
    private static CrashReport Report(string trace = "System.InvalidOperationException 0x80131509\nat MegaCopier.App.App.Launch()")
    {
        var report = new CrashReport { Version = "1.7.1", Windows = "10.0.26100.0", Language = "es-ES", Theme = "fluent-dark", Mode = "portable", Trace = trace };
        report.SetHash(); return report;
    }
    [Theory]
    [InlineData(@"Error C:\Users\Carlos\Documentos\nomina.pdf")]
    [InlineData(@"Error \\SERVIDOR\Privado\documento.docx")]
    [InlineData("https://usuario:clave@example.org/ruta 192.168.1.5 persona@example.org")]
    [InlineData("Mi archivo secreto y el nombre de Juan Pérez")]
    public void No_transmite_mensajes_libres_ni_datos_de_archivos(string input) => Assert.Empty(CrashPrivacy.SafeTrace(input));

    [Fact]
    public void Traza_conserva_funciones_y_codigos_pero_no_parametros_rutas_ni_mensajes()
    {
        string value = CrashPrivacy.SafeTrace("System.IO.IOException: Ana ana@example.com C:\\Users\\Ana\\nomina.pdf 0x80070020\n   at MegaCopier.Engine.CopyFile(System.String Ana) in C:\\Privado\\codigo.cs:line 123\nSecreto 192.168.1.2");
        Assert.Equal("System.IO.IOException 0x80070020\nat MegaCopier.Engine.CopyFile()", value);
        Assert.DoesNotContain("Ana", value); Assert.DoesNotContain("Privado", value);
    }
    [Fact]
    public void Traza_larga_recorta_lineas_completas_y_sigue_siendo_un_informe_valido()
    {
        string filtered=CrashPrivacy.SafeTrace(string.Join('\n',Enumerable.Repeat("at MegaCopier.Engine."+new string('A',250)+"()",50)));
        Assert.InRange(filtered.Length,1,12000); Assert.EndsWith("()",filtered); Assert.Equal(filtered,CrashPrivacy.SafeTrace(filtered));
        Assert.True(Report(filtered).IsSafe());
    }
    [Fact]
    public void Redacta_identidad_conocida_y_archivo_suelto()
    {
        string value = CrashPrivacy.Redact("Carlos en EQUIPO-JUAN documento.pdf", "Carlos", "EQUIPO-JUAN");
        Assert.DoesNotContain("Carlos", value); Assert.DoesNotContain("JUAN", value); Assert.Contains("<archivo>.pdf", value);
    }
    [Fact]
    public void Formato_fuente_generada_y_hash_estable_sin_identificador_de_equipo()
    {
        var a=Report(); var b=Report(); Assert.Equal(a.Hash,b.Hash); Assert.NotEqual(a.Id,b.Id); Assert.True(a.IsSafe());
        var copy=JsonSerializer.Deserialize(CrashQueue.Preview(a),CrashJsonContext.Default.CrashReport)!;
        Assert.True(copy.IsSafe()); Assert.DoesNotContain("machine",CrashQueue.Preview(a),StringComparison.OrdinalIgnoreCase);
        copy.Trace="Mis documentos secretos"; copy.SetHash(); Assert.False(copy.IsSafe());
        copy=Report(); copy.Native=new("archivo-personal.dll","1","1"); copy.SetHash(); Assert.False(copy.IsSafe());
        copy=Report(); copy.Language=null!; Assert.False(copy.IsSafe());
    }
    [Fact]
    public void Marca_detecta_cierre_sin_inventar_fallo_y_se_borra_al_salir_bien()
    {
        using var temp=new TempDir(); var first=new CrashSession(temp.Path,"1.7.1"); Assert.Null(first.Previous);
        first.Activity("copying"); var second=new CrashSession(temp.Path,"1.7.1"); Assert.Equal("copying",second.Previous!.Activity);
        second.Clean(); Assert.Null(new CrashSession(temp.Path,"1.7.1").Previous);
    }
    private static string Event(CrashRun run, string executable, string module="Microsoft.UI.Input.dll") => $$"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><Provider Name="Application Error"/><EventID>1000</EventID><TimeCreated SystemTime="{{run.Started.AddSeconds(1):O}}"/></System><EventData><Data Name="AppName">MegaCopier.exe</Data><Data Name="AppPath">{{executable}}</Data><Data Name="ProcessId">0x7b</Data><Data Name="ModuleName">{{module}}</Data><Data Name="ExceptionCode">c0000005</Data><Data Name="FaultingOffset">0000abcd</Data></EventData></Event>
        """;
    [Fact]
    public void Evento_nativo_exige_fecha_ruta_y_proceso_de_la_sesion()
    {
        var run=new CrashRun { Executable=@"C:\App\MegaCopier.exe", Pid=123 };
        var fault=NativeCrashEvents.Parse(Event(run,run.Executable),run); Assert.Equal("Microsoft.UI.Input.dll",fault!.Module); Assert.Equal("c0000005",fault.Code);
        Assert.Equal("CoreMessagingXP.dll",NativeCrashEvents.Parse(Event(run,run.Executable,"CoreMessagingXP.dll"),run)!.Module);
        Assert.Null(NativeCrashEvents.Parse(Event(run,@"D:\Otra\MegaCopier.exe"),run));
        Assert.Null(NativeCrashEvents.Parse(Event(run,run.Executable),new CrashRun { Executable=run.Executable, Pid=124, Started=run.Started }));
        Assert.Null(NativeCrashEvents.Parse(Event(run,run.Executable),new CrashRun { Executable=run.Executable, Pid=123, Started=run.Started.AddMinutes(1) }));
    }
    [Fact]
    public void Rechaza_redirecciones_y_destinos_de_prueba_fuera_de_loopback()
    {
        Assert.False(CrashQueue.ValidEndpoint(new Uri("http://example.com/report"),true));
        Assert.False(CrashQueue.ValidEndpoint(new Uri("https://user:pass@example.com/report")));
        Assert.True(CrashQueue.ValidEndpoint(new Uri("http://127.0.0.1:1234/report"),true));
    }
    private sealed class Receiver : IDisposable
    {
        private readonly HttpListener _listener=new(); private readonly Task _loop;
        public Uri Url {get;} public List<string> Bodies {get;}=[]; public int Status {get;set;}=202;
        public Receiver()
        {
            var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();int port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();
            Url=new($"http://127.0.0.1:{port}/report/");_listener.Prefixes.Add(Url.AbsoluteUri);_listener.Start();
            _loop=Task.Run(async()=>{try{while(true){var ctx=await _listener.GetContextAsync();using var input=new StreamReader(ctx.Request.InputStream);Bodies.Add(await input.ReadToEndAsync());ctx.Response.StatusCode=Status;
                if(Status==302)ctx.Response.RedirectLocation="http://127.0.0.1:1/blocked";byte[] bytes=Encoding.UTF8.GetBytes("{\"accepted\":true}");ctx.Response.ContentLength64=bytes.Length;await ctx.Response.OutputStream.WriteAsync(bytes);ctx.Response.Close();}}
                catch(Exception ex)when(ex is HttpListenerException or ObjectDisposedException){} });
        }
        public void Dispose()=>_listener.Close();
    }
    [Fact]
    public async Task Sin_consentimiento_no_guarda_ni_transmite_y_deduplica_despues_de_reabrir()
    {
        using var temp=new TempDir();using var receiver=new Receiver();using(var queue=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true))
        {
            Assert.False(await queue.AuthorizeAsync(Report(),false));Assert.Equal(0,await queue.FlushAsync());Assert.Empty(receiver.Bodies);
            Assert.True(await queue.AuthorizeAsync(Report(),true));Assert.False(await queue.AuthorizeAsync(Report(),true));
            Assert.Equal(1,await queue.FlushAsync());Assert.Single(receiver.Bodies);Assert.True(JsonSerializer.Deserialize(receiver.Bodies[0],CrashJsonContext.Default.CrashReport)!.IsSafe());
        }
        using var reopened=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true);Assert.False(await reopened.AuthorizeAsync(Report(),true));
    }
    [Fact]
    public async Task Limite_de_cinco_persistente_y_reintento_al_dia_siguiente()
    {
        using var temp=new TempDir();using var receiver=new Receiver();
        using(var queue=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true))
        {for(int i=0;i<6;i++)Assert.True(await queue.AuthorizeAsync(Report($"at MegaCopier.Engine.Copy{i}()"),true));Assert.Equal(5,await queue.FlushAsync());}
        using var next=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true);Assert.Equal(0,await next.FlushAsync());Assert.Equal(1,await next.FlushAsync(clock:DateTimeOffset.UtcNow.AddDays(1)));Assert.Equal(6,receiver.Bodies.Count);
    }
    [Fact]
    public async Task Pendiente_sin_red_conserva_id_y_no_sigue_redirecciones()
    {
        using var temp=new TempDir();using var receiver=new Receiver{Status=302};using var queue=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true);
        var report=Report();await queue.AuthorizeAsync(report,true);Assert.Equal(0,await queue.FlushAsync());Assert.Single(receiver.Bodies);
        receiver.Status=202;Assert.Equal(1,await queue.FlushAsync());Assert.Equal(receiver.Bodies[0],receiver.Bodies[1]);
    }
    [Fact]
    public async Task Revocacion_elimina_pendientes_y_el_opt_out_descarta_consentimiento_automatico()
    {
        using var temp=new TempDir();using var receiver=new Receiver();using(var queue=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true))
        {await queue.AuthorizeAsync(Report(),true,automatic:true);Assert.Equal(0,await queue.FlushAsync(automaticConsent:false));Assert.Empty(receiver.Bodies);await queue.AuthorizeAsync(Report(),true);await queue.RevokeAsync();}
        using var reopened=new CrashQueue(temp.Path,receiver.Url,"1.7.1",true);Assert.Equal(0,await reopened.FlushAsync());Assert.Empty(receiver.Bodies);
    }
}
