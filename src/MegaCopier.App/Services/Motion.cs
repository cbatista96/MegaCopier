using MegaCopier.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace MegaCopier.App.Services;

/// <summary>
/// Animaciones al pulsar: el botón se hunde un instante y su icono se mueve según lo que significa (el engranaje gira,
/// reproducir avanza, pausa se aprieta, la papelera se sacude, el historial gira hacia atrás...). Se engancha una vez a
/// la raíz de una ventana y vale para todos sus botones, también los que se crean después.
///
/// Todas son animaciones de XAML (Storyboard sobre RenderTransform y Opacity), que el sistema ejecuta igual de fluidas
/// fuera del hilo de la interfaz. No se usan animaciones de composición sobre los elementos de XAML: con ellas, al
/// cambiar el tamaño de la ventana, WinUI dejaba otros textos y botones dibujados apagados hasta pasar el ratón.
/// </summary>
internal static class Motion
{
    private static EasingFunctionBase Out => new CubicEase { EasingMode = EasingMode.EaseOut };

    private static EasingFunctionBase In => new CubicEase { EasingMode = EasingMode.EaseIn };

    private static EasingFunctionBase Spring => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

    /// <summary>Anima todos los botones con icono que haya (o vaya a haber) dentro de <paramref name="root"/>.</summary>
    public static void Attach(UIElement root) =>
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => OnPressed(e.OriginalSource as DependencyObject)), handledEventsToo: true);

    private static void OnPressed(DependencyObject? source)
    {
        if (Theming.BarAppearance.ReduceMotion)
        {
            return;
        }

        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            // Pestañas: su icono se anima según lo que significa.
            if (node is Microsoft.UI.Xaml.Controls.SelectorBarItem { IsEnabled: true, Icon: Microsoft.UI.Xaml.Controls.FontIcon tabIcon })
            {
                Icon(tabIcon, tabIcon.Glyph);
                return;
            }

            if (node is ButtonBase { IsEnabled: true } button)
            {
                Press(button);
                // «Morph»: el botón anima su propio icono (pausa ⇄ reproducir).
                if (button.Tag is not "Morph" && Responsive.Descendants<McIcon>(button).FirstOrDefault() is { } icon)
                {
                    Icon(icon);
                }

                return;
            }
        }
    }

    /// <summary>El botón se hunde y vuelve con un rebote suave.</summary>
    public static void Press(UIElement element)
    {
        var t = Prepare(element);
        Run(320, (t, "ScaleX", [(0.3, 0.94, Out), (1, 1, Spring)]), (t, "ScaleY", [(0.3, 0.94, Out), (1, 1, Spring)]));
    }

    /// <summary>Animación propia de cada icono, por su significado.</summary>
    public static void Icon(McIcon icon) => Icon(icon, icon.Glyph);

    /// <summary>Animación de un icono cualquiera (también los de las pestañas), según su glifo.</summary>
    public static void Icon(UIElement icon, string glyph)
    {
        if (Theming.BarAppearance.ReduceMotion || glyph.Length == 0)
        {
            return;
        }

        var t = Prepare(icon);
        switch (glyph[0])
        {
            case '\uE8FD': // lista (Archivos): baja un poco, como al desplazarla
                Nudge(t, 0, 4);
                break;
            case '\uEDA2': // unidad (Destino): se va hacia la derecha y vuelve
                Nudge(t, 5, 0);
                break;
            case '\uE9E9': // controles deslizantes (Opciones): se aprietan
                Squeeze(t, 1.15f, 0.6f);
                break;
            case '\uE9D2': // gráfica (Estado): crece desde abajo
                icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1);
                Squeeze(t, 1, 0.4f);
                break;
            case '\uE7C3': // página (Registro): pasa la hoja
                FlipX(t);
                break;
            case '': // ajustes: el engranaje da media vuelta
                Rotate(t, 180, 520);
                break;
            case '': // actualizar
            case '': // sincronizar
                Rotate(t, 360, 620);
                break;
            case '': // historial: gira hacia atrás
            case '': // restablecer
                Rotate(t, -360, 560);
                break;
            case '': // línea de tiempo / reloj
            case '':
                Rotate(t, 360, 620);
                break;
            case '': // reproducir / continuar: avanza y vuelve
            case '': // saltar
            case '': // flecha a la derecha
            case '': // enviar
                Nudge(t, 6, 0);
                break;
            case '': // pausa: las dos barras se aprietan
                Squeeze(t, 0.55f, 1.1f);
                break;
            case '': // detener: el cuadrado se encoge
            case '': // cerrar
                Squeeze(t, 0.6f, 0.6f);
                break;
            case '': // borrar: la papelera se sacude
            case '': // aviso
                Shake(t);
                break;
            case '': // desplegar / plegar: la flecha da la vuelta
            case '':
                Flip(t);
                break;
            case '': // menú
                Squeeze(t, 1.3f, 0.75f);
                break;
            case '': // información: salta
            case '': // ayuda
                Nudge(t, 0, -5);
                break;
            case '': // subir
                Nudge(t, 0, -5);
                break;
            case '': // bajar
                Nudge(t, 0, 5);
                break;
            default:
                Bounce(t);
                break;
        }
    }

    /// <summary>
    /// Aparece deslizándose un poco hasta su sitio (paneles que se muestran de pronto). Sin fundido: las animaciones de
    /// opacidad hacen que WinUI deje otros controles dibujados apagados.
    /// </summary>
    public static void FadeIn(UIElement element, double distance = 10)
    {
        var t = Prepare(element);
        Stop(element);
        element.Opacity = 1;
        if (Theming.BarAppearance.ReduceMotion)
        {
            t.TranslateY = 0;
            return;
        }

        Run(320, (t, "TranslateY", [(0, distance, null), (1, 0, Out)]));
    }

    /// <summary>Se retira subiendo un poco (lo contrario de <see cref="FadeIn"/>); al acabar queda oculto hasta ResetFade.</summary>
    public static void FadeOut(UIElement element)
    {
        var t = Prepare(element);
        if (Theming.BarAppearance.ReduceMotion)
        {
            element.Opacity = 0;
            return;
        }

        Run(200, () => element.Opacity = 0, (t, "TranslateY", [(1, -12, In)]));
    }

    /// <summary>Deja el elemento como estaba antes de <see cref="FadeOut"/> (visible y en su sitio).</summary>
    public static void ResetFade(UIElement element)
    {
        Stop(element);
        element.Opacity = 1;
        if (element.RenderTransform is CompositeTransform t)
        {
            t.TranslateY = 0;
        }
    }

    /// <summary>
    /// Desliza en horizontal de <paramref name="from"/> a <paramref name="to"/> y, al acabar, avisa. Frena al llegar si va
    /// hacia la derecha (o si <paramref name="decelerate"/> lo pide); si no, acelera al salir.
    /// </summary>
    public static void SlideX(UIElement element, double from, double to, int milliseconds, Action? done, bool? decelerate = null)
    {
        var t = Prepare(element);
        Stop(element);
        if (Theming.BarAppearance.ReduceMotion)
        {
            t.TranslateX = to;
            done?.Invoke();
            return;
        }

        Run(milliseconds, done, (t, "TranslateX", [(0, from, null), (1, to, decelerate ?? to > from ? Out : In)]));
    }

    /// <summary>Lo deja en su sitio (sin desplazamiento ni animación en marcha).</summary>
    public static void ResetSlide(UIElement element)
    {
        Stop(element);
        if (element.RenderTransform is CompositeTransform t)
        {
            t.TranslateX = 0;
        }
    }

    /// <summary>
    /// La marca de lo elegido (la línea bajo la pestaña) se desliza desde donde estaba la de la opción anterior hasta
    /// la nueva, en lugar de saltar.
    /// </summary>
    public static void SlideIndicator(FrameworkElement from, FrameworkElement to, bool vertical)
    {
        if (Theming.BarAppearance.ReduceMotion || from == to || from.ActualWidth == 0 || to.ActualWidth == 0 || to.XamlRoot is null)
        {
            return;
        }

        var shift = from.TransformToVisual(to).TransformPoint(default);
        var t = to.RenderTransform as CompositeTransform ?? Prepare(to);
        Run(320, vertical ? (t, "TranslateY", [(0, shift.Y, null), (1, 0, Out)]) : (t, "TranslateX", [(0, shift.X, null), (1, 0, Out)]));
    }

    /// <summary>La transformación del elemento para animarlo (se crea la primera vez, centrada).</summary>
    private static CompositeTransform Prepare(UIElement element)
    {
        if (element.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            element.RenderTransform = transform;
        }

        Owners.AddOrUpdate(transform, element);

        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        return transform;
    }

    /// <summary>De qué elemento es cada transformación (para parar sus animaciones con <see cref="Stop"/>).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompositeTransform, UIElement> Owners = [];

    /// <summary>Las animaciones en marcha de cada elemento, para pararlas si empieza otra o hay que dejarlo quieto.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, List<Storyboard>> Running = [];

    private static void Stop(UIElement element)
    {
        if (Running.TryGetValue(element, out var stories))
        {
            foreach (var story in stories)
            {
                story.Stop();
            }

            stories.Clear();
        }
    }

    /// <summary>
    /// Lanza a la vez varias pistas (objeto, propiedad, fotogramas): cada fotograma es (momento de 0 a 1, valor,
    /// suavizado hasta ese valor). Sin fotograma en 0, la pista arranca del valor que tenga.
    /// </summary>
    private static void Run(int milliseconds, params (DependencyObject Target, string Property, (double At, double Value, EasingFunctionBase? Ease)[] Frames)[] tracks) =>
        Run(milliseconds, null, tracks);

    /// <summary>Igual, y al terminar ejecuta <paramref name="done"/>.</summary>
    private static void Run(int milliseconds, Action? done, params (DependencyObject Target, string Property, (double At, double Value, EasingFunctionBase? Ease)[] Frames)[] tracks)
    {
        var story = new Storyboard();
        UIElement? owner = null;
        foreach (var (target, property, frames) in tracks)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            foreach (var (at, value, ease) in frames)
            {
                animation.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * at)),
                    Value = value,
                    EasingFunction = ease,
                });
            }

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
            owner ??= target as UIElement ?? (target is CompositeTransform transform && Owners.TryGetValue(transform, out var element) ? element : null);
        }

        if (owner is not null)
        {
            Running.GetOrCreateValue(owner).Add(story);
        }

        // Al terminar, el valor final pasa a ser el valor propio del elemento y la animación se quita: así nada queda
        // «retenido» por una animación (un Opacity = 0 posterior, por ejemplo, vuelve a funcionar).
        story.Completed += (_, _) =>
        {
            if (owner is not null && Running.TryGetValue(owner, out var stories))
            {
                stories.Remove(story);
            }

            story.Stop();
            foreach (var (target, property, frames) in tracks)
            {
                if (frames.Length > 0 && Property(property) is { } dp)
                {
                    target.SetValue(dp, frames[^1].Value);
                }
            }

            done?.Invoke();
        };

        story.Begin();
    }

    private static DependencyProperty? Property(string name) => name switch
    {
        "Opacity" => UIElement.OpacityProperty,
        "ScaleX" => CompositeTransform.ScaleXProperty,
        "ScaleY" => CompositeTransform.ScaleYProperty,
        "Rotation" => CompositeTransform.RotationProperty,
        "TranslateX" => CompositeTransform.TranslateXProperty,
        "TranslateY" => CompositeTransform.TranslateYProperty,
        _ => null,
    };

    private static void Rotate(CompositeTransform t, double degrees, int milliseconds) =>
        Run(milliseconds, (t, "Rotation", [(0, 0, null), (1, degrees, Out)]));

    /// <summary>Se desplaza un poco y vuelve con un rebote.</summary>
    private static void Nudge(CompositeTransform t, double x, double y) =>
        Run(420, (t, "TranslateX", [(0.35, x, Out), (1, 0, Spring)]), (t, "TranslateY", [(0.35, y, Out), (1, 0, Spring)]));

    private static void Squeeze(CompositeTransform t, double x, double y) =>
        Run(380, (t, "ScaleX", [(0.35, x, Out), (1, 1, Spring)]), (t, "ScaleY", [(0.35, y, Out), (1, 1, Spring)]));

    private static void Shake(CompositeTransform t) =>
        Run(460, (t, "Rotation", [(0.15, -14, Out), (0.35, 12, null), (0.55, -8, null), (0.75, 5, null), (1, 0, Out)]));

    private static void FlipX(CompositeTransform t) =>
        Run(380, (t, "ScaleX", [(0.5, -1, Out), (1, 1, Out)]));

    private static void Flip(CompositeTransform t) =>
        Run(360, (t, "ScaleY", [(0.5, -1, Out), (1, 1, Out)]));

    private static void Bounce(CompositeTransform t) =>
        Run(420,
            (t, "ScaleX", [(0.25, 0.78, Out), (0.6, 1.18, Out), (1, 1, Out)]),
            (t, "ScaleY", [(0.25, 0.78, Out), (0.6, 1.18, Out), (1, 1, Out)]),
            (t, "Rotation", [(0.3, -12, Out), (0.7, 8, null), (1, 0, Out)]));
}
