using System.Windows.Input;
using Juju.Tools.Toto.Application.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Juju.App.Views;

internal static class TotoShortcutKey
{
    public static bool TryGet(KeyEventArgs e, out string stroke)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var name = key switch
        {
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            Key.Return => "Enter", Key.Back => "Backspace", Key.Prior => "PageUp", Key.Next => "PageDown",
            Key.Escape => "Escape", Key.Space => "Space", Key.Tab => "Tab", Key.Delete => "Delete",
            Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
            Key.Home => "Home", Key.End => "End", Key.Insert => "Insert",
            _ => key.ToString()
        };
        var modifiers = Keyboard.Modifiers;
        var parts = new[]
        {
            modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : null,
            name
        };
        return WindowShortcut.TryNormalize(string.Join('+', parts.Where(part => part is not null)), out stroke);
    }
}
