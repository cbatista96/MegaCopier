#:property TargetFramework=net10.0
#:property PublishAot=false
// Genera los sonidos propios de MegaCopier (src/MegaCopier.App/Assets/Sounds/*.wav): campanillas sintetizadas con
// matemáticas (ondas, armónicos y eco), sin muestras de terceros. Uso:  dotnet run tools/SoundGen/SoundGen.cs [carpeta]
string output = args.Length > 0 ? args[0] : Path.Combine("src", "MegaCopier.App", "Assets", "Sounds");
Directory.CreateDirectory(output);
const int Rate = 44100;

// Notas en Hz.
const double C5 = 523.25, E5 = 659.25, G5 = 783.99, C6 = 1046.50, E6 = 1318.51, G6 = 1567.98, A4 = 440.00, F4 = 349.23, D5 = 587.33;

// Copia terminada: arpegio ascendente y luminoso (do-mi-sol).
Save("terminado", Mix(1.6, (0.00, C6, 0.9, 0), (0.11, E6, 0.85, 0), (0.22, G6, 0.8, 0)));

// Todas las copias terminadas: arpegio más amplio que acaba arriba.
Save("todo-terminado", Mix(2.0, (0.00, C5, 0.8, 0), (0.10, E5, 0.8, 0), (0.20, G5, 0.8, 0), (0.32, C6, 0.95, 0), (0.32, E6, 0.35, 0)));

// Error: dos notas suaves que bajan (la-fa), más graves y cortas.
Save("error", Mix(1.3, (0.00, A4, 0.9, 0.55), (0.20, F4, 0.9, 0.7)));

// Atención (hace falta decidir algo): dos toques iguales y claros.
Save("atencion", Mix(1.1, (0.00, D5 * 2, 0.75, 0), (0.16, D5 * 2, 0.75, 0)));

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
    writer.Write(Rate);
    writer.Write(Rate * 2);
    writer.Write((short)2);
    writer.Write((short)16);
    writer.Write("data"u8);
    writer.Write(bytes);
    foreach (float s in samples)
    {
        writer.Write((short)Math.Clamp(s * 32767, -32767, 32767));
    }

    Console.WriteLine($"{path} ({new FileInfo(path).Length / 1024} KB)");
}

// Mezcla notas de campanilla (inicio en s, frecuencia, volumen y, opcional, decaimiento en s) con un eco suave.
float[] Mix(double seconds, params (double Start, double Frequency, double Volume, double Decay)[] notes)
{
    var buffer = new float[(int)(seconds * Rate)];
    foreach (var (start, frequency, volume, decay) in notes)
    {
        int offset = (int)(start * Rate);
        for (int i = 0; offset + i < buffer.Length; i++)
        {
            double t = i / (double)Rate;
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
    int delay = (int)(0.085 * Rate);
    for (int i = delay; i < buffer.Length; i++)
    {
        buffer[i] += buffer[i - delay] * 0.28f;
    }

    int fade = (int)(0.15 * Rate);
    for (int i = 0; i < fade; i++)
    {
        buffer[buffer.Length - 1 - i] *= i / (float)fade;
    }

    float peak = buffer.Max(Math.Abs);
    if (peak > 0.95f)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= 0.95f / peak;
        }
    }

    return buffer;
}
