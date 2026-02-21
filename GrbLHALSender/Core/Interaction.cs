using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.Core
{
    /// <summary>
    /// Simple implementation of Interaction pattern from ReactiveUI framework.
    /// https://www.reactiveui.net/docs/handbook/interactions/
    /// </summary>
    public sealed class Interaction<TInput, TOutput> : ICommand
    {
        // this is a reference to the registered interaction handler.
        private Func<TInput, Task<TOutput>>? _handler;

        /// <summary>
        /// Performs the requested interaction <see langword="async"/>. Returns the result provided by the View
        /// </summary>
        /// <param name="input">The input parameter</param>
        /// <returns>The result of the interaction</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Task<TOutput> HandleAsync(TInput input)
        {
            if (_handler is null)
            {
                throw new InvalidOperationException("Handler wasn't registered");
            }

            return _handler(input);
        }

        /// <summary>
        /// Registers a handler to our Interaction
        /// </summary>
        /// <param name="handler">the handler to register</param>
        /// <returns>a disposable object to clean up memory if not in use anymore/></returns>
        public IDisposable RegisterHandler(Func<TInput, Task<TOutput>> handler)
        {
            _handler = handler;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

            // Return a token that only clears _handler if it's still the one we set
            var capturedHandler = handler;
            return new HandlerDisposable(() =>
            {
                if (_handler == capturedHandler)
                {
                    _handler = null;
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public bool CanExecute(object? parameter) => _handler is not null;

        public void Execute(object? parameter) => HandleAsync((TInput?)parameter!);

        public event EventHandler? CanExecuteChanged;

        private sealed class HandlerDisposable(Action onDispose) : IDisposable
        {
            private Action? _onDispose = onDispose;

            public void Dispose()
            {
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }
    }
}
