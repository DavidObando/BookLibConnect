namespace Oahu.Aux {
  public interface IInteractionCallback<T, out TResult> {
    TResult Interact (T value);
  }
}
