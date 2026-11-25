using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CrystalGlow.Controller.Controls;

/// <summary>
/// A rotary "ratchet" control -- snaps to one of StepCount discrete
/// positions spread across a fixed rotation arc, same idea as the
/// snapping sliders already used for options-based DMX channels (colour
/// macros, show modes, etc.), just drawn as a turning knob instead of a
/// slider. There is no continuous mode -- a DMX ratchet channel is
/// always a fixed menu of named settings, never a smooth range, so
/// anything in between two steps wouldn't correspond to a real option.
///
/// Drag vertically to turn it: up increases Value, down decreases it.
/// A full drag across PixelsPerStep*StepCount pixels sweeps the whole
/// arc, so the feel stays roughly the same regardless of StepCount.
/// </summary>
public partial class Knob : UserControl
{
    private const double MinAngle = -135;
    private const double MaxAngle = 135;
    private const double PixelsPerStep = 20;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(Knob),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty StepCountProperty = DependencyProperty.Register(
        nameof(StepCount), typeof(int), typeof(Knob),
        new PropertyMetadata(8, OnStepCountChanged));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, Math.Max(StepCount - 1, 0)));
    }

    public int StepCount
    {
        get => (int)GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, value);
    }

    /// <summary>Raised whenever Value changes, whether by dragging or by setting the property directly.</summary>
    public event Action<int>? ValueChanged;

    private bool _dragging;
    private Point _dragStart;
    private int _valueAtDragStart;

    public Knob()
    {
        InitializeComponent();
        UpdateRotation();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (Knob)d;
        knob.UpdateRotation();
        knob.ValueChanged?.Invoke((int)e.NewValue);
    }

    private static void OnStepCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Knob)d).UpdateRotation();

    private void UpdateRotation()
    {
        var steps = Math.Max(StepCount - 1, 1);
        var fraction = (double)Value / steps;
        KnobRotation.Angle = MinAngle + fraction * (MaxAngle - MinAngle);
    }

    private void KnobImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _valueAtDragStart = Value;
        KnobImage.CaptureMouse();
    }

    private void KnobImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var current = e.GetPosition(this);
        var deltaY = _dragStart.Y - current.Y;
        var deltaSteps = (int)Math.Round(deltaY / PixelsPerStep);
        Value = Math.Clamp(_valueAtDragStart + deltaSteps, 0, Math.Max(StepCount - 1, 0));
    }

    private void KnobImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        KnobImage.ReleaseMouseCapture();
    }
}
