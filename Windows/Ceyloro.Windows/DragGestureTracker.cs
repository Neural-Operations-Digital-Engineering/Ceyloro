namespace Ceyloro.Windows;

internal sealed class DragGestureTracker
{
    private readonly int threshold;
    private Point startPoint;
    private bool isTracking;
    private bool gestureTriggered;

    public DragGestureTracker(int threshold = 6)
    {
        this.threshold = Math.Max(threshold, 1);
    }

    public bool IsTracking => isTracking;
    public bool GestureTriggered => gestureTriggered;
    public RadialWheelMode Mode { get; private set; } = RadialWheelMode.Convert;

    public bool OnMouseDown(Point point, bool modifierPressed, bool isExplorer, RadialWheelMode mode = RadialWheelMode.Convert)
    {
        gestureTriggered = false;
        if (modifierPressed && isExplorer)
        {
            isTracking = true;
            startPoint = point;
            Mode = mode;
            return true;
        }

        isTracking = false;
        return false;
    }

    public bool OnMouseMove(Point point, out Point triggerPoint)
    {
        triggerPoint = point;
        if (!isTracking || gestureTriggered)
            return false;

        var dx = point.X - startPoint.X;
        var dy = point.Y - startPoint.Y;
        if (dx * dx + dy * dy >= threshold * threshold)
        {
            gestureTriggered = true;
            isTracking = false;
            triggerPoint = point;
            return true;
        }

        return false;
    }

    public bool OnMouseUp()
    {
        var wasTriggered = gestureTriggered;
        isTracking = false;
        gestureTriggered = false;
        return wasTriggered;
    }

    public void Reset()
    {
        isTracking = false;
        gestureTriggered = false;
    }
}
