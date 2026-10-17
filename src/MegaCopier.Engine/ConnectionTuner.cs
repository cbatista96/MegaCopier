namespace MegaCopier.Engine;

/// <summary>
/// Cuántas conexiones usar con un servidor, ajustado sobre la marcha. Se empieza con 2 y se sube mientras la velocidad
/// total mejore claramente; en cuanto una subida no aporta, se vuelve al mejor número visto. Después, de vez en cuando se
/// prueba con una más (por si la red o el servidor han cambiado), y si la velocidad cae mucho se baja una. Así no hace
/// falta adivinar: con un NAS lento se queda en 2 o 3, con S3 o una fibra rápida sube hasta el máximo.
/// </summary>
internal sealed class ConnectionTuner(int maximum)
{
    private const double Better = 1.10;  // +10 %: la subida mereció la pena
    private const double Collapse = 0.60; // −40 % sin tocar nada: algo cambió, se baja una
    private const int Cooldown = 5;       // intervalos tranquilos antes de volver a probar

    private double _bestRate;
    private int _bestLimit;
    private bool _probing;
    private bool _firstClimb = true;
    private int _quiet;

    public int Maximum { get; } = Math.Max(1, maximum);

    public int Limit { get; private set; } = Math.Min(2, Math.Max(1, maximum));

    /// <summary>Recibe la velocidad del último intervalo (bytes por segundo) y devuelve el nuevo número de conexiones.</summary>
    public int Next(double rate)
    {
        if (rate <= 0)
        {
            return Limit; // sin datos (pausa, archivos que se estaban abriendo...): no se decide nada
        }

        if (_bestRate == 0)
        {
            Remember(rate);
            return Grow();
        }

        if (_probing)
        {
            if (rate > _bestRate * Better)
            {
                Remember(rate);
                return Grow(); // sigue subiendo mientras compense
            }

            // La última subida no aportó: al mejor número visto, y un rato sin probar.
            Limit = _bestLimit;
            _probing = false;
            _firstClimb = false;
            _quiet = Cooldown;
            return Limit;
        }

        if (rate < _bestRate * Collapse && Limit > 1)
        {
            Limit--;
            Remember(rate);
            _quiet = Cooldown;
            return Limit;
        }

        if (--_quiet > 0)
        {
            _bestRate = Math.Max(_bestRate * 0.9, rate); // la referencia se va adaptando a lo que hay ahora
            return Limit;
        }

        // Toca probar con una más, con la velocidad de ahora como referencia.
        Remember(rate);
        return Grow();
    }

    private void Remember(double rate)
    {
        _bestRate = rate;
        _bestLimit = Limit;
    }

    private int Grow()
    {
        if (Limit >= Maximum)
        {
            _probing = false;
            _quiet = Cooldown;
            return Limit;
        }

        // La primera subida va deprisa (2, 4, 8, 12, 16...); al volver a probar, de una en una.
        int step = _firstClimb ? Math.Min(Limit, 4) : 1;
        Limit = Math.Min(Maximum, Limit + step);
        _probing = true;
        return Limit;
    }
}
