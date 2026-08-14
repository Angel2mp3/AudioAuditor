using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AudioQualityChecker
{
    /// <summary>
    /// Small cross-window helpers shared by every borderless window in the app.
    /// </summary>
    public static class WindowExtensions
    {
        /// <summary>
        /// Starts a title-bar drag without risking the classic WPF crash.
        ///
        /// <see cref="Window.DragMove"/> re-reads the live mouse state and throws
        /// InvalidOperationException if the left button is no longer physically down. Because a
        /// MouseDown handler runs after the event has been queued, a fast click-drag-release can
        /// land here with the button already up. Checking <c>e.ChangedButton</c> does not help —
        /// that says which button changed, not whether it is still held — and even
        /// <c>e.LeftButton</c> is a snapshot taken when the event was raised.
        ///
        /// The live check skips the common case; the catch covers the remaining race.
        /// </summary>
        public static void SafeDragMove(this Window window)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) return;
            try { window.DragMove(); }
            catch (InvalidOperationException) { /* button released mid-call — nothing to drag */ }
        }

        /// <summary>
        /// Observes a fire-and-forget task so a failure is logged instead of vanishing.
        ///
        /// Without this, exceptions surface only via TaskScheduler.UnobservedTaskException in
        /// App.xaml.cs, which marks them observed and returns — leaving the UI stuck in whatever
        /// loading state it was in with no indication anything went wrong.
        ///
        /// Pass <paramref name="onError"/> when the caller owns visible state that must be
        /// corrected (a status label, a spinner). It is invoked on the captured context.
        /// </summary>
        public static void Observe(this Task task, string operation, Action<Exception>? onError = null)
        {
            if (task.IsCompletedSuccessfully) return;
            _ = ObserveAsync(task, operation, onError);
        }

        private static async Task ObserveAsync(Task task, string operation, Action<Exception>? onError)
        {
            try
            {
                // Resume on the UI context only when a handler needs to touch UI state.
                await task.ConfigureAwait(continueOnCapturedContext: onError != null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{operation} failed: {ex}");
                if (onError == null) return;
                try { onError(ex); }
                catch (Exception inner) { Debug.WriteLine($"{operation} error handler failed: {inner}"); }
            }
        }
    }
}
