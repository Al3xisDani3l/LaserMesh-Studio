using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LaserMesh.Studio;

/// <summary>Numeric editor with focused wheel stepping and reversible edit transactions.</summary>
public sealed class NumericInput : TextBox
{
    private int wheelRemainder;
    private string initialText = "";
    /// <summary>Default increment, expressed in the field's physical unit.</summary>
    public double Step { get; set; } = 1;
    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { initialText = Text; base.OnGotKeyboardFocus(e); }
    /// <inheritdoc/>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsKeyboardFocusWithin || !IsMouseOver) { base.OnPreviewMouseWheel(e); return; }
        if (!double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return;
        wheelRemainder += e.Delta;
        var ticks = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        var multiplier = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10 : 1;
        if (ticks != 0) SetCurrentValue(TextProperty, (value + ticks * Step * multiplier).ToString("G9", CultureInfo.CurrentCulture));
        e.Handled = true;
    }
    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DataContext is StudioViewModel model)
        {
            if (e.Key == Key.Escape) { SetCurrentValue(TextProperty, initialText); model.CancelNumericEdit(); e.Handled = true; }
            if (e.Key == Key.Enter) { model.CommitNumericEdit(); e.Handled = true; }
        }
        base.OnPreviewKeyDown(e);
    }
    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (!double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            SetCurrentValue(TextProperty, initialText);
        if (DataContext is StudioViewModel model) model.CommitNumericEdit();
        base.OnLostKeyboardFocus(e);
    }
}
