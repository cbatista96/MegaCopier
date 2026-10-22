using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCopier.Engine.Updates;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

[CollectionDefinition("UpdateEnvironment", DisableParallelization = true)]
public sealed class UpdateEnvironment;

[Collection("UpdateEnvironment")]
public sealed class UpdateTests
{
    [Fact]
    public void Busca_cada_dos_dias_y_recupera_comprobacion_vencida()
    {
        var now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
        Assert.True(UpdatePolicy.CheckDue(null, now));
        Assert.False(UpdatePolicy.CheckDue(now, now));
        Assert.False(UpdatePolicy.CheckDue(now.AddHours(-24), now));
        Assert.False(UpdatePolicy.CheckDue(now.AddHours(-48).AddSeconds(1), now));
        Assert.True(UpdatePolicy.CheckDue(now.AddHours(-48), now));
        Assert.True(UpdatePolicy.CheckDue(now.AddDays(-3), now));
        Assert.True(UpdatePolicy.CheckDue(now.AddHours(1), now));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void Solo_la_deteccion_automatica_y_ambos_permisos_autorizan_descargar(bool check, bool download, bool automatic, bool expected)
    {
        var cache = new UpdateCache { Release = new() { Tag = "v2.0.0" }, AutomaticVersion = automatic ? "v2.0.0" : "" };
        Assert.Equal(expected, UpdatePolicy.CanDownloadAutomatically(cache, check, download));
        cache.CancelledVersion = "v2.0.0";
        Assert.False(UpdatePolicy.CanDownloadAutomatically(cache, check, download));
        cache.CancelledVersion = ""; cache.Release.Tag = "v2.0.1";
        Assert.False(UpdatePolicy.CanDownloadAutomatically(cache, check, download));
    }

    [Theory]
    [InlineData("1.7.0", "1.7.1", "1.7.1", true)]
    [InlineData("1.7.0", "1.7.1", "1.7.0", false)]
    [InlineData("1.7.1", "1.7.1", "1.7.1", false)]
    [InlineData("1.7.2", "1.7.1", "1.7.1", false)]
    [InlineData("", "", "1.7.1", false)]
    public void Aviso_solo_despues_de_instalar_la_version_prevista(string from, string target, string running, bool expected)
    {
        var cache = new UpdateCache { InstallingFromVersion = from, InstallingVersion = target };
        Assert.Equal(expected, UpdatePolicy.CompleteInstallation(cache, ReleaseVersion.Parse(running)!));
        Assert.Equal(expected ? running : "", cache.UpdatedVersion);
        Assert.False(UpdatePolicy.CompleteInstallation(cache, ReleaseVersion.Parse(running)!));
    }

    [Fact]
    public void Resumen_editorial_conserva_explicacion_desarrollada()
    {
        string text = "Al abrir MegaCopier con una copia a una carpeta que ya existía, la ventana podía preguntar antes de estar lista. Ahora espera a que termine de abrirse y muestra correctamente las opciones para continuar. " + new string('a', 160);
        Assert.Equal("- " + text, ReleaseNotes.Summary("### Resumen\n- " + text + "\n### Arreglado\n- Detalle completo"));
    }
    [Theory]
    [InlineData(4)][InlineData(10)][InlineData(16)]
    public void Resumen_editorial_no_pierde_novedades_en_versiones_grandes(int count)
    {
        string editorial = string.Join("\n", Enumerable.Range(1, count).Select(i => "- Novedad principal " + i));
        string notes = "### Resumen\n" + editorial + "\n### Arreglado\n- Diez detalles técnicos agrupados en el resumen";
        Assert.Equal(count, ReleaseNotes.Parse(ReleaseNotes.Summary(notes)).Count);
        Assert.DoesNotContain("Diez detalles", ReleaseNotes.Summary(notes));
    }

    [Fact]
    public void Vista_previa_usa_resumen_editorial_y_conserva_documento_completo()
    {
        string notes = "### Resumen\n\n- Novedad importante\n- Otra mejora\n\n### Arreglado\n\n- Detalle completo\n\n![Foto](imagen.png)";
        Assert.Equal("- Novedad importante\n\n- Otra mejora", ReleaseNotes.Summary(notes));
        Assert.Contains(ReleaseNotes.Parse(notes), b => b.Kind == NoteKind.Image);
        Assert.Contains("Detail", ReleaseNotes.Summary("### Highlights\n- Detail\n### Fixed\n- Other"));
    }

    [Fact]
    public void Vista_previa_antigua_limita_puntos_y_longitud_sin_imagenes()
    {
        string notes = string.Join("\n", Enumerable.Range(1, 9).Select(i => "- **Punto " + i + "** " + new string('a', 400))) + "\n![Foto](imagen.png)";
        var summary = ReleaseNotes.Parse(ReleaseNotes.Summary(notes));
        Assert.Equal(5, summary.Count);
        Assert.All(summary, b => { Assert.Equal(NoteKind.ListItem, b.Kind); Assert.True(b.Text.Length <= 280); });
        Assert.Empty(ReleaseNotes.Parse(ReleaseNotes.Summary("![Solo imagen](imagen.png)")));
    }

    [Theory]
    [InlineData("v1.10.0", "1.9.9", 1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.2.3+build", "1.2.3", 0)]
    public void Compara_versiones_semanticas(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(ReleaseVersion.Parse(a)!.CompareTo(ReleaseVersion.Parse(b))));

    [Theory]
    [InlineData("1.2")][InlineData("01.2.3")][InlineData("1.2.3-")][InlineData("1.2.3-01")][InlineData("<script>")]
    [InlineData("1.2.3+")][InlineData("1.2.3+a+b")][InlineData("1.2.3+a..b")]
    public void Rechaza_versiones_invalidas(string value) => Assert.Null(ReleaseVersion.Parse(value));

    [Theory]
    [InlineData(true, false, false, false, true, false, true)]
    [InlineData(true, false, true, false, true, false, false)]
    [InlineData(false, true, false, false, true, false, false)]
    [InlineData(false, true, true, false, false, true, true)]
    [InlineData(true, false, true, false, true, true, true)]
    [InlineData(true, false, false, true, true, true, false)]
    public void Respeta_tipo_y_coste_de_red(bool local, bool mobile, bool metered, bool restricted, bool allowLocal, bool allowMobile, bool allowed) =>
        Assert.Equal(allowed, UpdatePolicy.Allows(new(true, local, mobile, metered, restricted), allowLocal, allowMobile));

    [Theory]
    [InlineData("https://github.com/cbatista96/MegaCopier/releases/download/v1.0.0/a.exe", false, true)]
    [InlineData("https://release-assets.githubusercontent.com/path", true, true)]
    [InlineData("https://release-assets.githubusercontent.com/path", false, false)]
    [InlineData("https://github.com.evil.example/cbatista96/MegaCopier/a", true, false)]
    [InlineData("https://github.com/other/repo/a", false, false)]
    [InlineData("http://github.com/cbatista96/MegaCopier/a", false, false)]
    [InlineData("https://user@github.com/cbatista96/MegaCopier/a", false, false)]
    public void Valida_urls_sin_coincidencias_parciales(string url, bool redirect, bool expected) =>
        Assert.Equal(expected, UpdateClient.ProductionUrl(new(url), redirect));

    [Fact]
    public void Firma_verifica_bytes_version_y_plataforma()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new SignedUpdateManifest { Version = "1.8.0" }, UpdateJsonContext.Default.SignedUpdateManifest);
        byte[] signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal("1.8.0", UpdateClient.VerifyManifest(data, signature, key.ExportSubjectPublicKeyInfoPem(), "1.8.0").Version);
        Assert.Throws<UpdateException>(() => UpdateClient.VerifyManifest(data, signature, other.ExportSubjectPublicKeyInfoPem(), "1.8.0"));
        Assert.Throws<UpdateException>(() => UpdateClient.VerifyManifest(data, signature, key.ExportSubjectPublicKeyInfoPem(), "1.9.0"));
        Assert.Equal(UpdateError.MissingTrust, Assert.Throws<UpdateException>(() => UpdateClient.VerifyManifest(data, signature, "", "1.8.0")).Error);
        data[0] ^= 1;
        Assert.Throws<UpdateException>(() => UpdateClient.VerifyManifest(data, signature, key.ExportSubjectPublicKeyInfoPem(), "1.8.0"));
    }

    [Fact]
    public async Task Detecta_archivo_alterado_incluso_con_igual_tamano()
    {
        using var temp = new TempDir();
        string path = temp.Combine("paquete.exe");
        await File.WriteAllTextAsync(path, "correcto");
        var asset = new SignedUpdateAsset { Size = 8, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("correcto"))) };
        await UpdateClient.VerifyFileAsync(path, asset, default);
        await File.WriteAllTextAsync(path, "alterado");
        await Assert.ThrowsAsync<UpdateException>(() => UpdateClient.VerifyFileAsync(path, asset, default));
    }

    [Fact]
    public void Historial_completo_excluye_sin_publicar_y_orden_instalado()
    {
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../CHANGELOG.md"));
        string markdown = File.ReadAllText(source);
        var history = ReleaseNotes.History(markdown, ReleaseVersion.Parse("1.7.1")!);
        Assert.Equal("1.7.1", history[0].Version);
        Assert.Contains(history, h => h.Version == "1.6.0");
        Assert.All(history, h => Assert.NotEmpty(h.Notes));
        var blocks = ReleaseNotes.Parse(markdown);
        Assert.Contains(blocks, b => b.Kind == NoteKind.Image && b.Url!.Contains("docs/capturas/", StringComparison.Ordinal));
        Assert.Contains(blocks, b => b.Kind == NoteKind.Heading);
        Assert.Contains(ReleaseNotes.Inline("**negrita** y `código` y [enlace](https://example.com)"), i => i.Bold);
        Assert.Contains(ReleaseNotes.Inline("**negrita** y `código` y [enlace](https://example.com)"), i => i.Code);
        Assert.DoesNotContain(blocks, b => b.Kind != NoteKind.Code && (b.Text.Contains("<td>") || b.Text.Contains("<table>")));
    }

    [Fact]
    public void Historial_ingles_contiene_todas_las_versiones_y_capturas()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var installed = ReleaseVersion.Parse("1.7.1")!;
        var spanish = ReleaseNotes.History(File.ReadAllText(Path.Combine(root, "CHANGELOG.md")), installed);
        var english = ReleaseNotes.History(File.ReadAllText(Path.Combine(root, "CHANGELOG.en.md")), installed);
        Assert.Equal(spanish.Select(v => v.Version), english.Select(v => v.Version));
        foreach (var es in spanish)
        {
            var en = english.Single(v => v.Version == es.Version);
            Assert.Equal(es.Date, en.Date);
            var original = ReleaseNotes.Parse(es.Notes);
            var translated = ReleaseNotes.Parse(en.Notes);
            Assert.Equal(original.Where(b => b.Kind == NoteKind.Image).Select(b => b.Url), translated.Where(b => b.Kind == NoteKind.Image).Select(b => b.Url));
            Assert.Equal(original.Count(b => b.Kind == NoteKind.ListItem), translated.Count(b => b.Kind == NoteKind.ListItem));
        }
    }

    [Fact]
    public void Enlaces_de_referencia_en_listas_y_tablas_son_datos_nativos()
    {
        var blocks = ReleaseNotes.Parse("- **Read** [guide][g]\n<table><tr><td><img src=\"https://example.com/a.png\" alt=\"A\"><br>A</td></tr></table>\n[g]: https://example.com/guide");
        Assert.Contains(ReleaseNotes.Inline(blocks[0].Text), x => x.Url == "https://example.com/guide");
        Assert.Single(blocks, b => b.Kind == NoteKind.Image);
        Assert.DoesNotContain(blocks, b => b.Text.Contains("<td>"));
    }

    [Fact]
    public void Capturas_conservan_ancho_y_fila_sin_perder_imagenes_ni_texto()
    {
        var blocks = ReleaseNotes.Parse("Antes\n\n<img src=\"https://example.com/a.png\" width=\"420\" alt=\"A\"> <img src=\"https://example.com/b.png\" width=\"260\" alt=\"B\">\n\nDespués");
        var images = blocks.Where(b => b.Kind == NoteKind.Image).ToArray();
        Assert.Equal(2, images.Length);
        Assert.Equal(new[] { 420, 260 }, images.Select(b => b.ImageWidth));
        Assert.True(images[0].ImageGroup > 0);
        Assert.Equal(images[0].ImageGroup, images[1].ImageGroup);
        Assert.Equal("Antes", blocks[0].Text);
        Assert.Equal("Después", blocks[^1].Text);
    }

    [Fact]
    public async Task Mantenimiento_guarda_sin_arrancar_y_se_puede_cancelar()
    {
        using var temp = new TempDir();
        var queue = new CopyQueue(new JobStore(temp.Combine("jobs")));
        string file = temp.Combine("origen.txt");
        File.WriteAllText(file, "dato");
        Assert.True(queue.TryBeginMaintenance());
        var job = queue.Submit([file], temp.Combine("destino"), new CopyOptions());
        Assert.Equal(CopyJobState.Queued, job.State);
        Assert.False(File.Exists(Path.Combine(temp.Combine("destino"), "origen.txt")));
        Assert.False(queue.TryBeginMaintenance());
        queue.EndMaintenance();
        for (int i = 0; i < 100 && job.IsActive; i++) await Task.Delay(20);
        Assert.Equal(CopyJobState.Completed, job.State);
    }

    [Fact]
    public async Task Descarga_local_firmada_redireccion_y_checksum_sin_red_externa()
    {
        string? previous = Environment.GetEnvironmentVariable("MEGACOPIER_TEST");
        Environment.SetEnvironmentVariable("MEGACOPIER_TEST", "1");
        try
        {
            using var temp = new TempDir();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var listener = new HttpListener();
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            string address = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(address); listener.Start();
            byte[] package = Encoding.UTF8.GetBytes("solo archivos de prueba");
            var asset = new SignedUpdateAsset { Name = "MegaCopier-Setup-1.8.0.exe", Size = package.Length, Sha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant() };
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new SignedUpdateManifest { Version = "1.8.0", Assets = [asset] }, UpdateJsonContext.Default.SignedUpdateManifest);
            var files = new Dictionary<string, byte[]> { [asset.Name] = package, ["update.json"] = manifest,
                ["update.sig"] = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                ["SHA256SUMS.txt"] = Encoding.UTF8.GetBytes(asset.Sha256 + "  " + asset.Name + "\n"),
                ["release-notes.es.md"] = Encoding.UTF8.GetBytes("### Añadido\n- Novedad"),
                ["release-notes.en.md"] = Encoding.UTF8.GetBytes("### Added\n- New feature") };
            var release = new UpdateRelease { Tag = "v1.8.0", Assets = files.Select(f => new ReleaseAsset { Name = f.Key, Url = address + f.Key, Size = f.Value.Length }).ToList() };
            using var stop = new CancellationTokenSource();
            var server = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var context = await listener.GetContextAsync();
                        if (context.Request.Url!.AbsolutePath == "/escape")
                        {
                            context.Response.Redirect($"http://127.0.0.2:{port}/untrusted"); context.Response.Close(); continue;
                        }
                        if (context.Request.Headers["If-None-Match"] == "\"test-release\"")
                        {
                            context.Response.StatusCode = 304; context.Response.Close(); continue;
                        }
                        byte[] response = context.Request.Url!.AbsolutePath == "/latest"
                            ? JsonSerializer.SerializeToUtf8Bytes(release, UpdateJsonContext.Default.UpdateRelease)
                            : files[context.Request.Url.AbsolutePath.TrimStart('/')];
                        context.Response.ContentLength64 = response.Length;
                        context.Response.Headers["ETag"] = "\"test-release\"";
                        await context.Response.OutputStream.WriteAsync(response);
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
            });
            using var client = new UpdateClient("1.7.1", key.ExportSubjectPublicKeyInfoPem(), new Uri(address + "latest"));
            var found = await client.LatestAsync(null, default);
            Assert.Equal("\"test-release\"", client.ETag);
            Assert.Null(await client.LatestAsync(client.ETag, default));
            Assert.Contains("Añadido", await client.NotesAsync(found!, true, default));
            Assert.Contains("Added", await client.NotesAsync(found!, false, default));
            var verified = await client.VerifyReleaseAsync(found!, false, default, temp.Combine("metadata"));
            Assert.Equal(verified.Sha256, client.VerifyCachedRelease(found!, false, temp.Combine("metadata")).Sha256);
            var progress = new List<UpdateProgress>();
            string path = await client.DownloadAsync(found!, verified, temp.Combine("downloads"), progress.Add, default);
            Assert.Equal(package, await File.ReadAllBytesAsync(path));
            Assert.Contains(progress, p => p.Verifying);
            Assert.Empty(Directory.GetFiles(temp.Combine("downloads"), "*.partial"));
            using var cancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(found!, verified, temp.Combine("cancelled"), _ => cancel.Cancel(), cancel.Token));
            Assert.Empty(Directory.GetFiles(temp.Combine("cancelled")));
            found!.Assets.Single(a => a.Name == asset.Name).Url = address + "escape";
            Assert.Equal(UpdateError.UntrustedUrl, (await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(found, verified, temp.Combine("redirect"), _ => { }, default))).Error);
            File.WriteAllText(Path.Combine(temp.Combine("metadata"), "SHA256SUMS.txt"), "alterado");
            Assert.Equal(UpdateError.InvalidChecksum, Assert.Throws<UpdateException>(() => client.VerifyCachedRelease(found, false, temp.Combine("metadata"))).Error);
            stop.Cancel(); listener.Stop(); await server;
        }
        finally { Environment.SetEnvironmentVariable("MEGACOPIER_TEST", previous); }
    }
}
