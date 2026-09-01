using System;
using System.Collections;
using System.Collections.Generic;

namespace Haven.Framework.Core
{
    /// <summary>
    /// Executes nested enumerators while converting otherwise unhandled coroutine exceptions
    /// into an explicit failure callback.
    /// </summary>
    public static class SafeCoroutine
    {
        public static IEnumerator Run(IEnumerator routine, Action<Exception> failed)
        {
            if (routine == null)
                yield break;

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                bool movedNext;
                object yielded = null;

                try
                {
                    movedNext = current.MoveNext();
                    if (movedNext)
                        yielded = current.Current;
                }
                catch (Exception exception)
                {
                    DisposeStack(stack);
                    failed?.Invoke(exception);
                    yield break;
                }

                if (!movedNext)
                {
                    stack.Pop();
                    try
                    {
                        (current as IDisposable)?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        DisposeStack(stack);
                        failed?.Invoke(exception);
                        yield break;
                    }
                    continue;
                }

                if (yielded is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }

                yield return yielded;
            }
        }

        private static void DisposeStack(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                try
                {
                    (stack.Pop() as IDisposable)?.Dispose();
                }
                catch
                {
                    // Preserve the original coroutine exception.
                }
            }
        }
    }
}
