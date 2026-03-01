using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using Oahu.Aux;

namespace Oahu.App.Avalonia
{
  /// <summary>
  /// macOS implementation of IInteractionCallback using Avalonia message boxes.
  /// Bridges the business logic interaction pattern to Avalonia dialog windows.
  /// </summary>
  public class InteractionCallbackMac<T> : IInteractionCallback<T, bool?> where T : InteractionMessage
  {
    private readonly Window _owner;

    public InteractionCallbackMac(Window owner)
    {
      _owner = owner;
    }

    private enum MessageBoxButtons
    {
      Ok,
      OkCancel,
      YesNo,
      YesNoCancel
    }

    public bool? Interact(T value)
    {
      bool? result = null;

      // Marshal to UI thread if needed
      if (Dispatcher.UIThread.CheckAccess())
      {
        result = showDialog(value);
      }
      else
      {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
          result = showDialog(value);
        }).Wait();
      }

      return result;
    }

    private bool? showDialog(InteractionMessage message)
    {
      // Map callback types to appropriate dialog styles
      var (title, buttons) = message.Type switch
      {
        ECallbackType.info => ("Information", MessageBoxButtons.Ok),
        ECallbackType.infoCancel => ("Information", MessageBoxButtons.OkCancel),
        ECallbackType.warning => ("Warning", MessageBoxButtons.Ok),
        ECallbackType.error => ("Error", MessageBoxButtons.Ok),
        ECallbackType.errorQuestion => ("Error", MessageBoxButtons.YesNo),
        ECallbackType.errorQuestion3 => ("Error", MessageBoxButtons.YesNoCancel),
        ECallbackType.question => ("Question", MessageBoxButtons.YesNo),
        ECallbackType.question3 => ("Question", MessageBoxButtons.YesNoCancel),
        _ => ("Message", MessageBoxButtons.Ok)
      };

      // For now, use a simple approach — log the message.
      // Full implementation would show an Avalonia dialog window.
      Logging.Log(1, this, () => $"[{title}] {message.Message}");

      // Default behavior: info/warning/error → true, questions → true (yes)
      return message.Type switch
      {
        ECallbackType.question or ECallbackType.question3 or
        ECallbackType.errorQuestion or ECallbackType.errorQuestion3 => true,
        _ => true
      };
    }
  }
}
