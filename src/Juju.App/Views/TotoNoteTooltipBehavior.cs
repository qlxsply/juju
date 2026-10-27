using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Juju.App.Views;

/// <summary>在备注单元格与提示浮层之间保持鼠标可交互的悬停备注提示。</summary>
public static class TotoNoteTooltipBehavior
{
    public static readonly DependencyProperty NoteProperty = DependencyProperty.RegisterAttached(
        "Note", typeof(string), typeof(TotoNoteTooltipBehavior), new PropertyMetadata(null, OnNoteChanged));

    private static readonly DependencyProperty PopupControllerProperty = DependencyProperty.RegisterAttached(
        "PopupController", typeof(NotePopupController), typeof(TotoNoteTooltipBehavior));

    public static void SetNote(DependencyObject element, string? value) => element.SetValue(NoteProperty, value);
    public static string? GetNote(DependencyObject element) => (string?)element.GetValue(NoteProperty);

    private static void OnNoteChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock textBlock) return;
        var controller = (NotePopupController?)textBlock.GetValue(PopupControllerProperty);
        if (args.NewValue is string note && !string.IsNullOrWhiteSpace(note))
        {
            if (controller is null)
            {
                controller = new NotePopupController(textBlock);
                textBlock.SetValue(PopupControllerProperty, controller);
            }
            controller.Update(note);
            return;
        }

        controller?.Dispose();
        textBlock.ClearValue(PopupControllerProperty);
    }

    private sealed class NotePopupController : IDisposable
    {
        private const double MaxWidth = 480;
        private const double MaxHeight = 320;
        private const double ParagraphSpacing = 4;

        private readonly TextBlock _target;
        private readonly Border _surface;
        private readonly ScrollViewer _scrollViewer;
        private readonly Popup _popup;
        private readonly DispatcherTimer _openTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _disposed;
        private string _note = "";

        public NotePopupController(TextBlock target)
        {
            _target = target;
            _scrollViewer = new ScrollViewer
            {
                MaxWidth = MaxWidth,
                MaxHeight = MaxHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _surface = new Border
            {
                MaxWidth = MaxWidth + 18,
                MaxHeight = MaxHeight + 18,
                Padding = new Thickness(9),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xE0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = _scrollViewer,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x20, 0x39, 0x5C),
                    BlurRadius = 12,
                    ShadowDepth = 3,
                    Opacity = 0.16
                }
            };
            _popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                Focusable = false,
                Placement = PlacementMode.MousePoint,
                HorizontalOffset = 12,
                VerticalOffset = 14,
                PlacementTarget = target,
                Child = _surface
            };

            _target.MouseEnter += Target_MouseEnter;
            _target.MouseLeave += Pointer_MouseLeave;
            _target.Unloaded += Target_Unloaded;
            _surface.MouseEnter += Pointer_MouseEnter;
            _surface.MouseLeave += Pointer_MouseLeave;
            _openTimer.Tick += OpenTimer_Tick;
            _closeTimer.Tick += CloseTimer_Tick;
        }

        public void Update(string note)
        {
            _note = note;
            _scrollViewer.Content = CreateParagraphs(note);
        }

        private static StackPanel CreateParagraphs(string note)
        {
            var lines = note.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None);
            var paragraphs = new StackPanel();
            for (var index = 0; index < lines.Length; index++)
            {
                paragraphs.Children.Add(new TextBlock
                {
                    Text = lines[index].Length == 0 ? "\u00A0" : lines[index],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = MaxWidth,
                    Margin = new Thickness(0, 0, 0, index < lines.Length - 1 ? ParagraphSpacing : 0)
                });
            }
            return paragraphs;
        }

        private void Target_MouseEnter(object sender, WpfMouseEventArgs e)
        {
            _closeTimer.Stop();
            if (!_popup.IsOpen) _openTimer.Start();
        }

        private void Pointer_MouseEnter(object sender, WpfMouseEventArgs e) => _closeTimer.Stop();

        private void Pointer_MouseLeave(object sender, WpfMouseEventArgs e)
        {
            _openTimer.Stop();
            if (_popup.IsOpen)
            {
                _closeTimer.Stop();
                _closeTimer.Start();
            }
        }

        private void OpenTimer_Tick(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            if (_disposed || !_target.IsLoaded || !_target.IsMouseOver || string.IsNullOrWhiteSpace(_note)) return;
            _popup.IsOpen = true;
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (_target.IsMouseOver || _surface.IsMouseOver) return;
            _popup.IsOpen = false;
        }

        private void Target_Unloaded(object sender, RoutedEventArgs e)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _popup.IsOpen = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _target.MouseEnter -= Target_MouseEnter;
            _target.MouseLeave -= Pointer_MouseLeave;
            _target.Unloaded -= Target_Unloaded;
            _surface.MouseEnter -= Pointer_MouseEnter;
            _surface.MouseLeave -= Pointer_MouseLeave;
            _openTimer.Stop();
            _closeTimer.Stop();
            _popup.IsOpen = false;
            _popup.Child = null;
        }
    }
}
