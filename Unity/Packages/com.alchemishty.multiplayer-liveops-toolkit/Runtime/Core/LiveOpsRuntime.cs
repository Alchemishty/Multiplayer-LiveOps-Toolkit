using System;
using System.Collections;
using System.Collections.Generic;

namespace MultiplayerLiveOpsToolkit.Core
{
    public sealed class LiveOpsRuntime
    {
        private readonly IReadOnlyList<ILiveOpsModule> modules;
        private readonly List<ILiveOpsModule> attemptedModules = new List<ILiveOpsModule>();
        private readonly Stack<IEnumerator> initializationRoutines = new Stack<IEnumerator>();
        private Action<LiveOpsError> failureCallback;
        private string initializingModuleId;

        public LiveOpsRuntimeState State { get; private set; } = LiveOpsRuntimeState.Created;

        public LiveOpsRuntime(IEnumerable<ILiveOpsModule> modules)
        {
            this.modules = ModuleDependencyResolver.Resolve(modules);
        }

        public IEnumerator Initialize(Action onReady, Action<LiveOpsError> onFailure)
        {
            if (onReady == null) throw new ArgumentNullException(nameof(onReady));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureCreated();
            return InitializeModules(onReady, onFailure);
        }

        public void Shutdown()
        {
            if (State == LiveOpsRuntimeState.Stopped || State == LiveOpsRuntimeState.Stopping) return;

            bool wasInitializing = State == LiveOpsRuntimeState.Initializing;
            string cancelledModuleId = initializingModuleId;
            Action<LiveOpsError> onCancelled = wasInitializing ? TakeFailureCallback() : null;
            var cleanupFailures = StopModules();
            if (wasInitializing)
            {
                onCancelled(new LiveOpsError(
                    LiveOpsErrorCode.InitializationCancelled, cancelledModuleId,
                    "Toolkit initialization was cancelled by shutdown.",
                    cleanupFailures.Count == 0 ? null : new AggregateException(cleanupFailures)));
            }
            if (cleanupFailures.Count > 0)
                throw new AggregateException("Toolkit shutdown could not release every module cleanly.", cleanupFailures);
        }

        private IEnumerator InitializeModules(Action onReady, Action<LiveOpsError> onFailure)
        {
            // Recheck when execution starts: callers may have created more than one unstarted iterator.
            EnsureCreated();
            State = LiveOpsRuntimeState.Initializing;
            failureCallback = onFailure;
            try
            {
                foreach (var module in modules)
                {
                    if (!TryStartModule(module, out Exception startFailure))
                    {
                        FailInitialization(startFailure);
                        yield break;
                    }

                    while (State == LiveOpsRuntimeState.Initializing && initializationRoutines.Count > 0)
                    {
                        bool hasYield = TryAdvanceInitialization(out object yieldedValue, out Exception stepFailure);
                        if (stepFailure != null)
                        {
                            FailInitialization(stepFailure);
                            yield break;
                        }
                        if (State != LiveOpsRuntimeState.Initializing) yield break;
                        if (hasYield) yield return yieldedValue;
                    }
                    if (State != LiveOpsRuntimeState.Initializing) yield break;
                }

                State = LiveOpsRuntimeState.Ready;
                initializingModuleId = null;
                failureCallback = null;
                onReady();
            }
            finally
            {
                // Disposing an unfinished operation must release resources just like explicit shutdown.
                if (State == LiveOpsRuntimeState.Initializing) Shutdown();
            }
        }

        private bool TryStartModule(ILiveOpsModule module, out Exception failure)
        {
            failure = null;
            initializingModuleId = module.Id;
            attemptedModules.Add(module);
            IEnumerator routine;
            try
            {
                routine = module.Initialize()
                    ?? throw new InvalidOperationException($"Module '{initializingModuleId}' returned a null initialization routine.");
            }
            catch (Exception exception)
            {
                if (State != LiveOpsRuntimeState.Initializing) throw;
                failure = exception;
                return false;
            }
            if (State != LiveOpsRuntimeState.Initializing)
            {
                (routine as IDisposable)?.Dispose();
                return true;
            }
            initializationRoutines.Push(routine);
            return true;
        }

        private bool TryAdvanceInitialization(out object yieldedValue, out Exception failure)
        {
            yieldedValue = null;
            failure = null;
            try
            {
                // Drive nested iterators here so their exceptions cannot bypass lifecycle rollback.
                while (State == LiveOpsRuntimeState.Initializing && initializationRoutines.Count > 0)
                {
                    var routine = initializationRoutines.Peek();
                    bool hasNext = routine.MoveNext();
                    if (State != LiveOpsRuntimeState.Initializing) return false;
                    if (!hasNext)
                    {
                        initializationRoutines.Pop();
                        (routine as IDisposable)?.Dispose();
                        continue;
                    }
                    object current = routine.Current;
                    if (State != LiveOpsRuntimeState.Initializing) return false;
                    if (current is IEnumerator nestedRoutine)
                    {
                        initializationRoutines.Push(nestedRoutine);
                        continue;
                    }
                    yieldedValue = current;
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                if (State != LiveOpsRuntimeState.Initializing) throw;
                failure = exception;
                return false;
            }
        }

        private void FailInitialization(Exception initializationFailure)
        {
            string failedModuleId = initializingModuleId;
            var onFailure = TakeFailureCallback();
            var cleanupFailures = StopModules();
            Exception cause = initializationFailure;
            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, initializationFailure);
                cause = new AggregateException(cleanupFailures);
            }
            onFailure(new LiveOpsError(
                LiveOpsErrorCode.ModuleInitializationFailed, failedModuleId,
                $"Module '{failedModuleId}' could not initialize: {initializationFailure.Message}", cause));
        }

        private List<Exception> StopModules()
        {
            State = LiveOpsRuntimeState.Stopping;
            var failures = new List<Exception>();
            try
            {
                while (initializationRoutines.Count > 0)
                {
                    var routine = initializationRoutines.Pop();
                    try { (routine as IDisposable)?.Dispose(); }
                    catch (Exception exception)
                    {
                        failures.Add(new InvalidOperationException(
                            $"Module '{initializingModuleId}' initialization cleanup failed.", exception));
                    }
                }
                for (int moduleIndex = attemptedModules.Count - 1; moduleIndex >= 0; moduleIndex--)
                {
                    var module = attemptedModules[moduleIndex];
                    try { module.Shutdown(); }
                    catch (Exception exception)
                    {
                        failures.Add(new InvalidOperationException($"Module '{module.Id}' shutdown failed.", exception));
                    }
                }
            }
            finally
            {
                attemptedModules.Clear();
                initializingModuleId = null;
                State = LiveOpsRuntimeState.Stopped;
            }
            return failures;
        }

        private Action<LiveOpsError> TakeFailureCallback()
        {
            var callback = failureCallback
                ?? throw new InvalidOperationException("Toolkit initialization has no failure callback.");
            failureCallback = null;
            return callback;
        }

        private void EnsureCreated()
        {
            if (State != LiveOpsRuntimeState.Created)
                throw new InvalidOperationException($"Cannot initialize toolkit in state '{State}'. Create a new runtime after shutdown.");
        }
    }
}
