#:property TargetFramework=net10.0
#:property PublishAot=false
// Genera los sonidos propios de MegaCopier (src/MegaCopier.App/Assets/Sounds/*.wav): campanillas, marimbas, arpas,
// gotas y acordes sintetizados con matemáticas (ondas, armónicos, filtros y eco), sin muestras de terceros. Uso:
//   dotnet run tools/SoundGen/SoundGen.cs [carpeta]
// Los cuatro primeros son los de cada momento («terminado», «error»...); los «mc-*» forman la colección que se puede
// elegir para cualquier momento: avisos cortos (menos de 1 s), notificaciones (1,5-3 s) y melodías (hasta 4 s).
string output = args.Length > 0 ? args[0] : Path.Combine("src", "MegaCopier.App", "Assets", "Sounds");
Directory.CreateDirectory(output);
int rate = 44100;
var random = new Random(20261018); // siempre la misma semilla: el mismo archivo en cada generación

// Notas en Hz.
const double C4 = 261.63, E4 = 329.63, F4 = 349.23, G4 = 392.00, A4 = 440.00;
const double C5 = 523.25, D5 = 587.33, E5 = 659.25, F5 = 698.46, G5 = 783.99, A5 = 880.00, B5 = 987.77;
const double C6 = 1046.50, D6 = 1174.66, E6 = 1318.51, G6 = 1567.98, A6 = 1760.00, C7 = 2093.00;

// --- Los de cada momento (no cambian) ---

// Copia terminada: arpegio ascendente y luminoso (do-mi-sol).
Save("terminado", Mix(1.6, (0.00, C6, 0.9, 0), (0.11, E6, 0.85, 0), (0.22, G6, 0.8, 0)));

// Todas las copias terminadas: arpegio más amplio que acaba arriba.
Save("todo-terminado", Mix(2.0, (0.00, C5, 0.8, 0), (0.10, E5, 0.8, 0), (0.20, G5, 0.8, 0), (0.32, C6, 0.95, 0), (0.32, E6, 0.35, 0)));

// Error: dos notas suaves que bajan (la-fa), más graves y cortas.
Save("error", Mix(1.3, (0.00, A4, 0.9, 0.55), (0.20, F4, 0.9, 0.7)));

// Atención (hace falta decidir algo): dos toques iguales y claros.
Save("atencion", Mix(1.1, (0.00, D5 * 2, 0.75, 0), (0.16, D5 * 2, 0.75, 0)));

// --- La colección (a 32 kHz: suenan igual de bien y ocupan menos) ---
rate = 32000;

// Avisos cortos.
Save("mc-bip", Finish(Beep(0.32, [(0.0, A5, 0.10)]), 0.06, echo: 0.10));
Save("mc-doble-bip", Finish(Beep(0.55, [(0.0, E6, 0.08), (0.16, E6, 0.08)]), 0.08, echo: 0.12));
Save("mc-gota", Finish(Drop(0.55), 0.10, echo: 0.22));
Save("mc-burbuja", Finish(Bubbles(0.75), 0.10, echo: 0.18));
Save("mc-toque", Finish(Notes(0.9, Marimba, (0.0, G5, 1.0)), 0.15, echo: 0.20));
Save("mc-pop", Finish(Pop(0.30), 0.05, echo: 0.08));

// Notificaciones.
Save("mc-cristal", Finish(Notes(2.2, Glass, (0.0, E6, 0.9), (0.12, B5, 0.7), (0.24, G6, 0.8)), 0.3, echo: 0.30));
Save("mc-arpa", Finish(Notes(2.6, Harp, (0.0, C5, 0.9), (0.09, E5, 0.9), (0.18, G5, 0.9), (0.27, C6, 0.9), (0.36, E6, 0.8)), 0.4, echo: 0.28));
Save("mc-marimba", Finish(Notes(1.8, Marimba, (0.0, C5, 0.9), (0.14, G5, 0.9), (0.28, C6, 1.0)), 0.3, echo: 0.22));
Save("mc-brillo", Finish(Sparkle(2.0), 0.4, echo: 0.35));
Save("mc-logro", Finish(Notes(2.6, Brass, (0.0, G4, 0.8), (0.14, C5, 0.8), (0.28, E5, 0.85), (0.46, G5, 1.0), (0.46, C5, 0.5)), 0.5, echo: 0.25));
Save("mc-eco", Finish(Notes(2.4, Bell, (0.0, A5, 1.0)), 0.5, echo: 0.55));
Save("mc-pregunta", Finish(Notes(1.4, Marimba, (0.0, E5, 0.9), (0.18, A5, 1.0)), 0.3, echo: 0.20));
Save("mc-aviso", Finish(Notes(1.5, Bell, (0.0, D6, 0.8), (0.20, A5, 0.8)), 0.3, echo: 0.25));
Save("mc-alerta", Finish(Notes(1.6, Soft, (0.0, E4, 0.9), (0.22, C4, 1.0)), 0.3, echo: 0.20));

// Melodías (hasta 4 s).
Save("mc-aurora", Finish(Aurora(3.8), 0.9, echo: 0.35));
Save("mc-amanecer", Finish(Sunrise(4.0), 0.9, echo: 0.30));
Save("mc-cascada", Finish(Notes(3.6, Glass, (0.0, C7, 0.6), (0.12, G6, 0.65), (0.24, E6, 0.7), (0.36, C6, 0.75), (0.48, G5, 0.8), (0.60, E5, 0.85), (0.72, C5, 0.9)), 0.8, echo: 0.40));
Save("mc-orbital", Finish(Orbital(3.8), 0.8, echo: 0.30));

void Save(string name, float[] samples)
{
    string path = Path.Combine(output, name + ".wav");
    using var writer = new BinaryWriter(File.Create(path));
    int bytes = samples.Length * 2;
    writer.Write("RIFF"u8);
    writer.Write(36 + bytes);
    writer.Write("WAVE"u8);
    writer.Write("fmt "u8);
    writer.Write(16);
    writer.Write((short)1);     // PCM
    writer.Write((short)1);     // mono
    writer.Write(rate);
    writer.Write(rate * 2);
    writer.Write((short)2);
    writer.Write((short)16);
    writer.Write("data"u8);
    writer.Write(bytes);
    foreach (float s in samples)
    {
        writer.Write((short)Math.Clamp(s * 32767, -32767, 32767));
    }

    Console.WriteLine($"{path} ({samples.Length / (double)rate:0.0} s, {new FileInfo(path).Length / 1024} KB)");
}

// Mezcla notas de campanilla (inicio en s, frecuencia, volumen y, opcional, decaimiento en s) con un eco suave.
float[] Mix(double seconds, params (double Start, double Frequency, double Volume, double Decay)[] notes)
{
    var buffer = new float[(int)(seconds * rate)];
    foreach (var (start, frequency, volume, decay) in notes)
    {
        int offset = (int)(start * rate);
        for (int i = 0; offset + i < buffer.Length; i++)
        {
            double t = i / (double)rate;
            double attack = Math.Min(1, t / 0.004);            // 4 ms de ataque: sin chasquido
            double envelope = attack * Math.Exp(-t / (decay > 0 ? decay : 0.42));
            // Timbre de campanilla: fundamental y armónicos inarmónicos que se apagan antes.
            double tone = Math.Sin(2 * Math.PI * frequency * t)
                + (0.42 * Math.Sin(2 * Math.PI * frequency * 2.01 * t) * Math.Exp(-t / 0.18))
                + (0.20 * Math.Sin(2 * Math.PI * frequency * 3.02 * t) * Math.Exp(-t / 0.09))
                + (0.08 * Math.Sin(2 * Math.PI * frequency * 4.97 * t) * Math.Exp(-t / 0.05));
            buffer[offset + i] += (float)(tone * envelope * volume * 0.32);
        }
    }

    // Eco breve (como una sala pequeña) y fundido final.
    int delay = (int)(0.085 * rate);
    for (int i = delay; i < buffer.Length; i++)
    {
        buffer[i] += buffer[i - delay] * 0.28f;
    }

    return Normalize(FadeOut(buffer, 0.15));
}

// ---------------------------------------------------------------------------------------------------------
// Instrumentos: muestra de una nota en el instante t (s) desde que empieza.

// Campanilla: fundamental y armónicos inarmónicos.
static double Bell(double f, double t) =>
    Math.Exp(-t / 0.55) * (Math.Sin(Tau(f, t)) + (0.42 * Math.Sin(Tau(f * 2.01, t)) * Math.Exp(-t / 0.18))
        + (0.20 * Math.Sin(Tau(f * 3.02, t)) * Math.Exp(-t / 0.09)));

// Cristal (copa): parciales de una barra libre, muy limpios y largos.
static double Glass(double f, double t) =>
    Math.Exp(-t / 0.9) * (Math.Sin(Tau(f, t)) + (0.35 * Math.Sin(Tau(f * 2.76, t)) * Math.Exp(-t / 0.35))
        + (0.15 * Math.Sin(Tau(f * 5.40, t)) * Math.Exp(-t / 0.15)));

// Marimba: madera, con el parcial a 4x que se apaga enseguida.
static double Marimba(double f, double t) =>
    Math.Exp(-t / 0.30) * (Math.Sin(Tau(f, t)) + (0.5 * Math.Sin(Tau(f * 4.0, t)) * Math.Exp(-t / 0.04))
        + (0.12 * Math.Sin(Tau(f * 9.9, t)) * Math.Exp(-t / 0.02)));

// Arpa: cuerda pulsada (armónicos que se apagan más deprisa cuanto más agudos).
static double Harp(double f, double t)
{
    double sum = 0;
    for (int h = 1; h <= 6; h++)
    {
        sum += Math.Sin(Tau(f * h, t)) * Math.Exp(-t * h / 0.9) / (h * h * 0.6);
    }

    return sum * Math.Exp(-t / 1.2);
}

// Metal suave (fanfarria corta): armónicos impares con un ataque algo más lento.
static double Brass(double f, double t)
{
    double attack = Math.Min(1, t / 0.03);
    double sum = Math.Sin(Tau(f, t)) + (0.45 * Math.Sin(Tau(f * 2, t))) + (0.25 * Math.Sin(Tau(f * 3, t))) + (0.1 * Math.Sin(Tau(f * 4, t)));
    return attack * sum * Math.Exp(-t / 0.7) * 0.6;
}

// Tono suave y grave (avisos de error sin sobresaltar).
static double Soft(double f, double t) =>
    Math.Min(1, t / 0.02) * Math.Exp(-t / 0.5) * (Math.Sin(Tau(f, t)) + (0.25 * Math.Sin(Tau(f * 2, t))));

static double Tau(double f, double t) => 2 * Math.PI * f * t;

// ---------------------------------------------------------------------------------------------------------
// Sonidos compuestos

float[] Notes(double seconds, Func<double, double, double> instrument, params (double Start, double Frequency, double Volume)[] notes)
{
    var buffer = new float[(int)(seconds * rate)];
    foreach (var (start, frequency, volume) in notes)
    {
        int offset = (int)(start * rate);
        for (int i = 0; offset + i < buffer.Length; i++)
        {
            double t = i / (double)rate;
            buffer[offset + i] += (float)(Math.Min(1, t / 0.003) * instrument(frequency, t) * volume * 0.3);
        }
    }

    return buffer;
}

// Pitidos limpios (inicio, frecuencia, duración).
float[] Beep(double seconds, (double Start, double Frequency, double Length)[] beeps)
{
    var buffer = new float[(int)(seconds * rate)];
    foreach (var (start, frequency, length) in beeps)
    {
        int offset = (int)(start * rate);
        int count = (int)(length * rate);
        for (int i = 0; i < count && offset + i < buffer.Length; i++)
        {
            double t = i / (double)rate;
            double envelope = Math.Min(1, t / 0.005) * Math.Min(1, (length - t) / 0.02);
            buffer[offset + i] += (float)(envelope * (Math.Sin(Tau(frequency, t)) + (0.15 * Math.Sin(Tau(frequency * 2, t)))) * 0.35);
        }
    }

    return buffer;
}

// Gota de agua: una nota que sube de golpe y se apaga.
float[] Drop(double seconds)
{
    var buffer = new float[(int)(seconds * rate)];
    double phase = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        double t = i / (double)rate;
        double frequency = 600 + (1400 * (1 - Math.Exp(-t / 0.05)));
        phase += Tau(frequency, 1.0 / rate);
        buffer[i] = (float)(Math.Min(1, t / 0.002) * Math.Exp(-t / 0.08) * Math.Sin(phase) * 0.6);
    }

    return buffer;
}

// Tres burbujas pequeñas, cada una algo más aguda.
float[] Bubbles(double seconds)
{
    var buffer = new float[(int)(seconds * rate)];
    foreach (var (start, low, volume) in new[] { (0.0, 500.0, 0.8), (0.14, 650.0, 0.7), (0.26, 820.0, 0.6) })
    {
        double phase = 0;
        int offset = (int)(start * rate);
        for (int i = 0; offset + i < buffer.Length; i++)
        {
            double t = i / (double)rate;
            phase += Tau(low * (1 + (1.8 * (1 - Math.Exp(-t / 0.03)))), 1.0 / rate);
            buffer[offset + i] += (float)(Math.Min(1, t / 0.002) * Math.Exp(-t / 0.05) * Math.Sin(phase) * volume * 0.5);
        }
    }

    return buffer;
}

// Un "pop" corto: golpe de ruido filtrado y un tono que cae.
float[] Pop(double seconds)
{
    var buffer = new float[(int)(seconds * rate)];
    double phase = 0, low = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        double t = i / (double)rate;
        phase += Tau(900 * Math.Exp(-t / 0.04) + 180, 1.0 / rate);
        low += 0.25 * ((random.NextDouble() * 2) - 1 - low);
        buffer[i] = (float)(Math.Exp(-t / 0.03) * ((Math.Sin(phase) * 0.7) + (low * 0.25 * Math.Exp(-t / 0.008))) * 0.6);
    }

    return buffer;
}

// Destellos: notas agudas de cristal al azar dentro de una escala, cada vez más suaves.
float[] Sparkle(double seconds)
{
    double[] scale = [C6, D6, E6, G6, A6, C7];
    var notes = new List<(double, double, double)>();
    for (int n = 0; n < 9; n++)
    {
        notes.Add((n * 0.075, scale[random.Next(scale.Length)], 0.75 - (n * 0.06)));
    }

    return Notes(seconds, Glass, [.. notes]);
}

// Acorde largo y suave que se abre (pad), con un brillo de cristal encima.
float[] Aurora(double seconds)
{
    var buffer = new float[(int)(seconds * rate)];
    double[] chord = [C4, G4, E5, B5];
    for (int i = 0; i < buffer.Length; i++)
    {
        double t = i / (double)rate;
        double envelope = Math.Min(1, t / 0.6) * Math.Exp(-Math.Max(0, t - 1.2) / 1.1);
        double sum = 0;
        foreach (double f in chord)
        {
            // Dos osciladores casi iguales: el leve desafinado da el movimiento del "pad".
            sum += Math.Sin(Tau(f, t)) + Math.Sin(Tau(f * 1.004, t)) + (0.3 * Math.Sin(Tau(f * 2, t)));
        }

        buffer[i] = (float)(envelope * sum * 0.06);
    }

    var shine = Notes(seconds, Glass, (0.35, E6, 0.35), (0.55, G6, 0.3), (0.80, B5 * 2, 0.25));
    for (int i = 0; i < buffer.Length; i++)
    {
        buffer[i] += shine[i];
    }

    return buffer;
}

// Amanecer: arpegio que sube despacio sobre un acorde que crece.
float[] Sunrise(double seconds)
{
    var pad = new float[(int)(seconds * rate)];
    for (int i = 0; i < pad.Length; i++)
    {
        double t = i / (double)rate;
        double envelope = Math.Min(1, t / 1.5) * Math.Exp(-Math.Max(0, t - 2.4) / 0.8);
        pad[i] = (float)(envelope * (Math.Sin(Tau(F4, t)) + Math.Sin(Tau(A4 * 1.003, t)) + Math.Sin(Tau(C5, t))) * 0.07);
    }

    var melody = Notes(seconds, Harp, (0.2, F5, 0.7), (0.5, A5, 0.75), (0.8, C6, 0.8), (1.15, E6, 0.85), (1.55, F5 * 2, 0.9));
    for (int i = 0; i < pad.Length; i++)
    {
        pad[i] += melody[i];
    }

    return pad;
}

// Orbital: tono sintético que gira (modulación de frecuencia lenta) y se aleja.
float[] Orbital(double seconds)
{
    var buffer = new float[(int)(seconds * rate)];
    double phase = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        double t = i / (double)rate;
        double frequency = E5 * (1 + (0.012 * Math.Sin(Tau(5.5, t)))) * (1 + (0.25 * Math.Min(1, t / 0.8)));
        phase += Tau(frequency, 1.0 / rate);
        double modulator = Math.Sin(phase * 2.0) * 1.2 * Math.Exp(-t / 1.4);
        double envelope = Math.Min(1, t / 0.08) * Math.Exp(-t / 1.3);
        buffer[i] = (float)(envelope * Math.Sin(phase + modulator) * 0.45);
    }

    var ping = Notes(seconds, Glass, (0.9, B5, 0.4), (1.5, E6, 0.3));
    for (int i = 0; i < buffer.Length; i++)
    {
        buffer[i] += ping[i];
    }

    return buffer;
}

// Sala (varios ecos con filtro suave), fundido al final y volumen igualado entre todos los sonidos.
float[] Finish(float[] buffer, double fade, double echo)
{
    if (echo > 0)
    {
        foreach (var (delay, gain) in new[] { (0.073, 0.55), (0.131, 0.38), (0.197, 0.24), (0.263, 0.15) })
        {
            int samples = (int)(delay * rate);
            float g = (float)(gain * echo);
            for (int i = buffer.Length - 1; i >= samples; i--)
            {
                buffer[i] += buffer[i - samples] * g;
            }
        }
    }

    return Normalize(FadeOut(buffer, fade), 0.85f);
}

float[] FadeOut(float[] buffer, double seconds)
{
    int fade = Math.Min(buffer.Length, (int)(seconds * rate));
    for (int i = 0; i < fade; i++)
    {
        buffer[buffer.Length - 1 - i] *= i / (float)fade;
    }

    return buffer;
}

static float[] Normalize(float[] buffer, float target = 0.95f)
{
    float peak = buffer.Max(Math.Abs);
    if (peak > target || target < 0.95f)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= target / peak;
        }
    }

    return buffer;
}
