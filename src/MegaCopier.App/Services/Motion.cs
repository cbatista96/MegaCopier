using System.Numerics;
using MegaCopier.App.Controls;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Services;

/// <summary>
/// Animaciones al pulsar: el botón se hunde un instante y su icono se mueve según lo que significa (el engranaje gira,
/// reproducir avanza, pausa se aprieta, la papelera se sacude, el historial gira hacia atrás...). Se engancha una vez a
/// la raíz de una ventana y vale para todos sus botones, también los que se crean después.
/// </summary>
internal static class Motion
{
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
            if (node is ButtonBase { IsEnabled: true } button)
            {
                Press(button);
                if (Responsive.Descendants<McIcon>(button).FirstOrDefault() is { } icon)
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
        var visual = Prepare(element, out var compositor);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0.3f, new Vector3(0.93f, 0.93f, 1), compositor.CreateCubicBezierEasingFunction(new(0.3f, 0), new(0.6f, 1)));
        scale.InsertKeyFrame(1f, Vector3.One, compositor.CreateCubicBezierEasingFunction(new(0.2f, 1.6f), new(0.4f, 1)));
        scale.Duration = TimeSpan.FromMilliseconds(320);
        visual.StartAnimation("Scale", scale);
    }

    /// <summary>Animación propia de cada icono, por su significado.</summary>
    public static void Icon(McIcon icon)
    {
        if (Theming.BarAppearance.ReduceMotion || icon.Glyph.Length == 0)
        {
            return;
        }

        var visual = Prepare(icon, out var compositor);
        switch (icon.Glyph[0])
        {
            case '': // ajustes: el engranaje da media vuelta
                Rotate(visual, compositor, 180, 520);
                break;
            case '': // actualizar
            case '': // sincronizar
                Rotate(visual, compositor, 360, 620);
                break;
            case '': // historial: gira hacia atrás
            case '': // restablecer
                Rotate(visual, compositor, -360, 560);
                break;
            case '': // línea de tiempo / reloj
            case '':
                Rotate(visual, compositor, 360, 620);
                break;
            case '': // reproducir / continuar: avanza y vuelve
            case '': // saltar
            case '': // flecha a la derecha
            case '': // enviar
                Nudge(icon, visual, compositor, new Vector3(6, 0, 0));
                break;
            case '': // pausa: las dos barras se aprietan
                Squeeze(visual, compositor, new Vector3(0.55f, 1.1f, 1));
                break;
            case '': // detener: el cuadrado se encoge
            case '': // cerrar
                Squeeze(visual, compositor, new Vector3(0.6f, 0.6f, 1));
                break;
            case '': // borrar: la papelera se sacude
            case '': // aviso
                Shake(visual, compositor);
                break;
            case '': // desplegar / plegar: la flecha da la vuelta
            case '':
                Flip(visual, compositor);
                break;
            case '': // menú
                Squeeze(visual, compositor, new Vector3(1.3f, 0.75f, 1));
                break;
            case '': // información: salta
            case '': // ayuda
                Nudge(icon, visual, compositor, new Vector3(0, -5, 0));
                break;
            case '': // subir
                Nudge(icon, visual, compositor, new Vector3(0, -5, 0));
                break;
            case '': // bajar
                Nudge(icon, visual, compositor, new Vector3(0, 5, 0));
                break;
            default:
                Bounce(visual, compositor);
                break;
        }
    }

    /// <summary>Aparece suave, subiendo un poco (paneles que se muestran de pronto).</summary>
    public static void FadeIn(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (Theming.BarAppearance.ReduceMotion)
        {
            visual.Opacity = 1;
            return;
        }

        var compositor = visual.Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0);
        fade.InsertKeyFrame(1f, 1);
        fade.Duration = TimeSpan.FromMilliseconds(260);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0, 10, 0));
        slide.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1)));
        slide.Duration = TimeSpan.FromMilliseconds(320);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
    }

    private static Visual Prepare(UIElement element, out Compositor compositor)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(element.ActualSize.X / 2, element.ActualSize.Y / 2, 0);
        return visual;
    }

    private static void Rotate(Visual visual, Compositor compositor, float degrees, int milliseconds)
    {
        var rotation = compositor.CreateScalarKeyFrameAnimation();
        rotation.InsertKeyFrame(0f, 0);
        rotation.InsertKeyFrame(1f, degrees, compositor.CreateCubicBezierEasingFunction(new(0.3f, 0), new(0.2f, 1)));
        rotation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("RotationAngleInDegrees", rotation);
    }

    /// <summary>Se desplaza un poco y vuelve (con Translation: el Offset del elemento lo controla el diseño de XAML).</summary>
    private static void Nudge(UIElement element, Visual visual, Compositor compositor, Vector3 by)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0.35f, by, compositor.CreateCubicBezierEasingFunction(new(0.3f, 0), new(0.6f, 1)));
        move.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new(0.2f, 1.4f), new(0.4f, 1)));
        move.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Translation", move);
    }

    private static void Squeeze(Visual visual, Compositor compositor, Vector3 to)
    {
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0.35f, to);
        scale.InsertKeyFrame(1f, Vector3.One, compositor.CreateCubicBezierEasingFunction(new(0.2f, 1.5f), new(0.4f, 1)));
        scale.Duration = TimeSpan.FromMilliseconds(380);
        visual.StartAnimation("Scale", scale);
    }

    private static void Shake(Visual visual, Compositor compositor)
    {
        var rotation = compositor.CreateScalarKeyFrameAnimation();
        rotation.InsertKeyFrame(0.15f, -14);
        rotation.InsertKeyFrame(0.35f, 12);
        rotation.InsertKeyFrame(0.55f, -8);
        rotation.InsertKeyFrame(0.75f, 5);
        rotation.InsertKeyFrame(1f, 0);
        rotation.Duration = TimeSpan.FromMilliseconds(460);
        visual.StartAnimation("RotationAngleInDegrees", rotation);
    }

    private static void Flip(Visual visual, Compositor compositor)
    {
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0.5f, new Vector3(1, -1, 1));
        scale.InsertKeyFrame(1f, Vector3.One);
        scale.Duration = TimeSpan.FromMilliseconds(360);
        visual.StartAnimation("Scale", scale);
    }

    private static void Bounce(Visual visual, Compositor compositor)
    {
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0.25f, new Vector3(0.78f, 0.78f, 1));
        scale.InsertKeyFrame(0.6f, new Vector3(1.18f, 1.18f, 1));
        scale.InsertKeyFrame(1f, Vector3.One);
        scale.Duration = TimeSpan.FromMilliseconds(420);
        var rotation = compositor.CreateScalarKeyFrameAnimation();
        rotation.InsertKeyFrame(0.3f, -12);
        rotation.InsertKeyFrame(0.7f, 8);
        rotation.InsertKeyFrame(1f, 0);
        rotation.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("RotationAngleInDegrees", rotation);
    }
}
