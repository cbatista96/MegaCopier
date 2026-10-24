using System.Collections.Concurrent;

namespace MegaCopier.Shell;

/// <summary>Lee el punto inicial fuera del gancho del ratón, sin retrasar ningún clic.</summary>
internal sealed class DragOriginProbe(int x, int y)
{
    public volatile DropSpot Spot = DropSpot.Unknown;
    private static readonly Lazy<BlockingCollection<DragOriginProbe>> Queue = new(() =>
    {
        var queue = new BlockingCollection<DragOriginProbe>(1);
        var worker = new Thread(() =>
        {
            try
            {
                foreach (var probe in queue.GetConsumingEnumerable())
                {
                    try { probe.Spot = UiaHitTest.At(probe.X, probe.Y, out _); }
                    catch { probe.Spot = DropSpot.Unknown; }
                }
            }
            finally { UiaHitTest.ReleaseThread(); }
        }) { IsBackground = true, Name = "MegaCopier.DragOrigin" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return queue;
    });

    private int X { get; } = x;
    private int Y { get; } = y;

    public static DragOriginProbe Start(int x, int y)
    {
        var probe = new DragOriginProbe(x, y);
        var queue = Queue.Value;
        // Solo interesa el gesto más reciente. Nunca esperar a accesibilidad desde el gancho.
        if (!queue.TryAdd(probe))
        {
            queue.TryTake(out _);
            queue.TryAdd(probe);
        }
        return probe;
    }

    internal static bool CanAwaitOle(string captureClass, DropSpot origin, bool movedFar) =>
        movedFar && origin == DropSpot.Item
        && captureClass is "" or "DirectUIHWND" or "SysListView32" or "SysTreeView32";
}
