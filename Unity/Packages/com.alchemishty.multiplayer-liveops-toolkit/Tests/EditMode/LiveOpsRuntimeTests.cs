using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using NUnit.Framework;

[TestFixture]
public class LiveOpsRuntimeTests
{
    [Test]
    public void Initialize_DependencyRegisteredLast_StartsDependencyFirst()
    {
        var calls = new List<string>();
        var wallet = new TestModule("wallet", calls, "identity");
        var identity = new TestModule("identity", calls);
        var runtime = new LiveOpsRuntime(new[] { wallet, identity });
        int readyCount = 0;

        Drain(runtime.Initialize(() => readyCount++, error => Assert.Fail(error.Message)));

        CollectionAssert.AreEqual(new[] { "start:identity", "start:wallet" }, calls);
        Assert.That(readyCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Ready));
    }

    [Test]
    public void Initialize_OnlySelectedModules_DoesNotStartUnselectedModule()
    {
        var calls = new List<string>();
        var identity = new TestModule("identity", calls);
        var unusedModule = new TestModule("shop", calls);
        var runtime = new LiveOpsRuntime(new[] { identity });

        Drain(runtime.Initialize(() => { }, error => Assert.Fail(error.Message)));

        Assert.That(unusedModule.InitializationCount, Is.Zero);
        CollectionAssert.AreEqual(new[] { "start:identity" }, calls);
    }

    [Test]
    public void Constructor_MissingDependency_ThrowsBeforeModuleStarts()
    {
        var calls = new List<string>();
        var wallet = new TestModule("wallet", calls, "identity");

        var error = Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[] { wallet }));

        StringAssert.Contains("wallet", error.Message);
        StringAssert.Contains("identity", error.Message);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void Constructor_CyclicDependencies_ReportsInvolvedModules()
    {
        var calls = new List<string>();
        var wallet = new TestModule("wallet", calls, "shop");
        var shop = new TestModule("shop", calls, "wallet");

        var error = Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[] { wallet, shop }));

        StringAssert.Contains("wallet", error.Message);
        StringAssert.Contains("shop", error.Message);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void Constructor_SelfDependency_ThrowsBeforeModuleStarts()
    {
        var module = new TestModule("identity", new List<string>(), "identity");
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[] { module }));
        Assert.That(module.InitializationCount, Is.Zero);
    }

    [Test]
    public void Constructor_DuplicateModuleIds_Throws()
    {
        var calls = new List<string>();
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[]
        {
            new TestModule("wallet", calls), new TestModule("wallet", calls)
        }));
    }

    [Test]
    public void Constructor_NullModules_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveOpsRuntime(null));
    }

    [Test]
    public void Constructor_NullModuleEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new ILiveOpsModule[] { null }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Constructor_InvalidModuleId_Throws(string moduleId)
    {
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[]
        {
            new TestModule(moduleId, new List<string>())
        }));
    }

    [Test]
    public void Constructor_NullDependencies_Throws()
    {
        var module = new TestModule("wallet", new List<string>()) { Dependencies = null };
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[] { module }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Constructor_InvalidDependencyId_Throws(string dependencyId)
    {
        Assert.Throws<ArgumentException>(() => new LiveOpsRuntime(new[]
        {
            new TestModule("wallet", new List<string>(), dependencyId)
        }));
    }

    [Test]
    public void Initialize_ModuleCollectionChangedAfterConstruction_UsesValidatedPlan()
    {
        var calls = new List<string>();
        var modules = new List<ILiveOpsModule> { new TestModule("identity", calls) };
        var runtime = new LiveOpsRuntime(modules);
        modules.Clear();

        Drain(runtime.Initialize(() => { }, error => Assert.Fail(error.Message)));

        CollectionAssert.AreEqual(new[] { "start:identity" }, calls);
    }

    [Test]
    public void Initialize_EmptySelection_BecomesReady()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        int readyCount = 0;
        Drain(runtime.Initialize(() => readyCount++, error => Assert.Fail(error.Message)));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Ready));
        Assert.That(readyCount, Is.EqualTo(1));
    }

    [Test]
    public void Initialize_NullReadyCallback_ThrowsBeforeStarting()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        Assert.Throws<ArgumentNullException>(() => runtime.Initialize(null, error => { }));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Created));
    }

    [Test]
    public void Initialize_NullFailureCallback_ThrowsBeforeStarting()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        Assert.Throws<ArgumentNullException>(() => runtime.Initialize(() => { }, null));
    }

    [Test]
    public void Initialize_ModuleYields_DoesNotBecomeReadyBeforeCompletion()
    {
        var module = new TestModule("identity", new List<string>()) { Routine = OneFrame };
        var runtime = new LiveOpsRuntime(new[] { module });
        int readyCount = 0;
        var operation = runtime.Initialize(() => readyCount++, error => Assert.Fail(error.Message));

        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Initializing));
        Assert.That(readyCount, Is.Zero);
        Drain(operation);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Ready));
        Assert.That(readyCount, Is.EqualTo(1));
    }

    [Test]
    public void Initialize_ConcurrentInitialization_RejectsSecondOperation()
    {
        var module = new TestModule("identity", new List<string>()) { Routine = OneFrame };
        var runtime = new LiveOpsRuntime(new[] { module });
        var operation = runtime.Initialize(() => { }, error => { });
        operation.MoveNext();

        Assert.Throws<InvalidOperationException>(() => runtime.Initialize(() => { }, error => { }));
        runtime.Shutdown();
    }

    [Test]
    public void Initialize_TwoUnstartedOperations_OnlyFirstExecutedOperationCanStart()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        var first = runtime.Initialize(() => { }, error => { });
        var second = runtime.Initialize(() => { }, error => { });
        Drain(first);
        Assert.Throws<InvalidOperationException>(() => second.MoveNext());
    }

    [Test]
    public void Initialize_AlreadyReady_RejectsReinitialization()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        Drain(runtime.Initialize(() => { }, error => { }));
        Assert.Throws<InvalidOperationException>(() => runtime.Initialize(() => { }, error => { }));
    }

    [Test]
    public void Initialize_ModuleThrows_RollsBackAttemptedModulesInReverseOrder()
    {
        var calls = new List<string>();
        var identity = new TestModule("identity", calls);
        var wallet = new TestModule("wallet", calls, "identity")
        {
            Routine = () => throw new InvalidOperationException("Credentials rejected")
        };
        var shop = new TestModule("shop", calls, "wallet");
        var runtime = new LiveOpsRuntime(new[] { identity, wallet, shop });
        LiveOpsError failure = null;
        int readyCount = 0;

        Drain(runtime.Initialize(() => readyCount++, error => failure = error));

        CollectionAssert.AreEqual(new[] { "start:identity", "start:wallet", "stop:wallet", "stop:identity" }, calls);
        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.ModuleInitializationFailed));
        Assert.That(failure.ModuleId, Is.EqualTo("wallet"));
        StringAssert.Contains("Credentials rejected", failure.Message);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
        Assert.That(readyCount, Is.Zero);
    }

    [Test]
    public void Initialize_NestedCoroutineThrows_ReportsFailureAndCleansUp()
    {
        var module = new TestModule("identity", new List<string>()) { Routine = NestedFailure };
        var runtime = new LiveOpsRuntime(new[] { module });
        LiveOpsError failure = null;

        Drain(runtime.Initialize(() => Assert.Fail("Unexpected success"), error => failure = error));

        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.ModuleInitializationFailed));
        StringAssert.Contains("Nested failure", failure.Message);
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Initialize_NullModuleRoutine_ReportsBrokenModuleAndCleansUp()
    {
        var module = new TestModule("identity", new List<string>()) { Routine = () => null };
        var runtime = new LiveOpsRuntime(new[] { module });
        LiveOpsError failure = null;
        Drain(runtime.Initialize(() => Assert.Fail("Unexpected success"), error => failure = error));
        StringAssert.Contains("identity", failure.Message);
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
    }

    [Test]
    public void Shutdown_Ready_StopsModulesInReverseDependencyOrderOnce()
    {
        var calls = new List<string>();
        var runtime = new LiveOpsRuntime(new[]
        {
            new TestModule("shop", calls, "wallet"), new TestModule("wallet", calls)
        });
        Drain(runtime.Initialize(() => { }, error => Assert.Fail(error.Message)));
        calls.Clear();

        runtime.Shutdown();
        runtime.Shutdown();

        CollectionAssert.AreEqual(new[] { "stop:shop", "stop:wallet" }, calls);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Shutdown_Created_DoesNotStopUnstartedModules()
    {
        var module = new TestModule("identity", new List<string>());
        var runtime = new LiveOpsRuntime(new[] { module });
        runtime.Shutdown();
        Assert.That(module.ShutdownCount, Is.Zero);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
        Assert.Throws<InvalidOperationException>(() => runtime.Initialize(() => { }, error => { }));
    }

    [Test]
    public void Shutdown_DuringInitialization_CancelsOnceAndDisposesPendingRoutine()
    {
        bool wasDisposed = false;
        var module = new TestModule("identity", new List<string>())
        {
            Routine = () => PendingRoutine(() => wasDisposed = true)
        };
        var runtime = new LiveOpsRuntime(new[] { module });
        int failureCount = 0;
        LiveOpsError failure = null;
        var operation = runtime.Initialize(() => Assert.Fail("Unexpected success"), error =>
        {
            failureCount++;
            failure = error;
        });
        operation.MoveNext();

        runtime.Shutdown();
        runtime.Shutdown();
        Drain(operation);

        Assert.That(wasDisposed, Is.True);
        Assert.That(failureCount, Is.EqualTo(1));
        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.InitializationCancelled));
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Initialize_OperationDisposed_CancelsAndRollsBack()
    {
        var module = new TestModule("identity", new List<string>()) { Routine = OneFrame };
        var runtime = new LiveOpsRuntime(new[] { module });
        LiveOpsError failure = null;
        var operation = runtime.Initialize(() => { }, error => failure = error);
        operation.MoveNext();

        ((IDisposable)operation).Dispose();

        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.InitializationCancelled));
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Shutdown_ModuleThrows_ContinuesCleanupAndReportsAggregate()
    {
        var calls = new List<string>();
        var identity = new TestModule("identity", calls);
        var wallet = new TestModule("wallet", calls, "identity")
        {
            ShutdownAction = () => throw new InvalidOperationException("Cleanup failed")
        };
        var runtime = new LiveOpsRuntime(new[] { identity, wallet });
        Drain(runtime.Initialize(() => { }, error => { }));

        var failure = Assert.Throws<AggregateException>(() => runtime.Shutdown());

        Assert.That(failure.InnerExceptions.Count, Is.EqualTo(1));
        Assert.That(identity.ShutdownCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
        Assert.DoesNotThrow(() => runtime.Shutdown());
    }

    [Test]
    public void Initialize_RollbackThrows_PreservesInitializationAndCleanupFailures()
    {
        var module = new TestModule("identity", new List<string>())
        {
            Routine = () => throw new InvalidOperationException("Initialization failed"),
            ShutdownAction = () => throw new InvalidOperationException("Cleanup failed")
        };
        var runtime = new LiveOpsRuntime(new[] { module });
        LiveOpsError failure = null;

        Drain(runtime.Initialize(() => { }, error => failure = error));

        var causes = (AggregateException)failure.Cause;
        Assert.That(causes.InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Initialize_ReadyCallbackThrows_DoesNotMisreportInitializationFailure()
    {
        var runtime = new LiveOpsRuntime(Array.Empty<ILiveOpsModule>());
        int failureCount = 0;
        var operation = runtime.Initialize(
            () => throw new InvalidOperationException("Consumer callback failed"), error => failureCount++);

        Assert.Throws<InvalidOperationException>(() => Drain(operation));

        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Ready));
        Assert.That(failureCount, Is.Zero);
    }

    [Test]
    public void Shutdown_ReentrantModuleCleanup_RemainsIdempotent()
    {
        var module = new TestModule("identity", new List<string>());
        var runtime = new LiveOpsRuntime(new[] { module });
        module.ShutdownAction = runtime.Shutdown;
        Drain(runtime.Initialize(() => { }, error => { }));
        runtime.Shutdown();
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
    }

    [Test]
    public void Initialize_ModuleFactoryStopsRuntime_DisposesReturnedRoutineWithoutStartingNextModule()
    {
        int disposalCount = 0;
        var calls = new List<string>();
        var identity = new TestModule("identity", calls);
        var wallet = new TestModule("wallet", calls, "identity");
        var runtime = new LiveOpsRuntime(new[] { identity, wallet });
        identity.Routine = () =>
        {
            runtime.Shutdown();
            return new DisposableRoutine(() => disposalCount++);
        };
        int failureCount = 0;

        Drain(runtime.Initialize(() => Assert.Fail("Unexpected success"), error => failureCount++));

        Assert.That(disposalCount, Is.EqualTo(1));
        Assert.That(failureCount, Is.EqualTo(1));
        Assert.That(wallet.InitializationCount, Is.Zero);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [Test]
    public void Shutdown_RoutineDisposalThrows_StillStopsModuleAndCompletesCancellation()
    {
        var module = new TestModule("identity", new List<string>())
        {
            Routine = () => new DisposableRoutine(() => throw new InvalidOperationException("Dispose failed"))
        };
        var runtime = new LiveOpsRuntime(new[] { module });
        LiveOpsError failure = null;
        var operation = runtime.Initialize(() => { }, error => failure = error);
        operation.MoveNext();

        Assert.Throws<AggregateException>(() => runtime.Shutdown());

        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.InitializationCancelled));
        Assert.That(failure.Cause, Is.TypeOf<AggregateException>());
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
        Assert.DoesNotThrow(() => Drain(operation));
    }

    [Test]
    public void Initialize_FailureCallbackThrows_LeavesRuntimeStopped()
    {
        var module = new TestModule("identity", new List<string>())
        {
            Routine = () => throw new InvalidOperationException("Module failed")
        };
        var runtime = new LiveOpsRuntime(new[] { module });
        var operation = runtime.Initialize(() => { },
            error => throw new InvalidOperationException("Consumer failure handler failed"));

        Assert.Throws<InvalidOperationException>(() => Drain(operation));

        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
        Assert.That(module.ShutdownCount, Is.EqualTo(1));
        Assert.DoesNotThrow(() => runtime.Shutdown());
    }

    [Test]
    public void Initialize_SharedDependency_StartsItOnlyOnce()
    {
        var calls = new List<string>();
        var identity = new TestModule("identity", calls);
        var wallet = new TestModule("wallet", calls, "identity");
        var inventory = new TestModule("inventory", calls, "identity");
        var shop = new TestModule("shop", calls, "wallet", "inventory");
        var runtime = new LiveOpsRuntime(new[] { shop, inventory, wallet, identity });

        Drain(runtime.Initialize(() => { }, error => Assert.Fail(error.Message)));

        Assert.That(identity.InitializationCount, Is.EqualTo(1));
        CollectionAssert.AreEqual(new[] { "start:identity", "start:wallet", "start:inventory", "start:shop" }, calls);
    }

    private sealed class DisposableRoutine : IEnumerator, IDisposable
    {
        private readonly Action onDisposed;
        public DisposableRoutine(Action onDisposed) { this.onDisposed = onDisposed; }
        public object Current => null;
        public bool MoveNext() => true;
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => onDisposed();
    }

    private static void Drain(IEnumerator operation)
    {
        while (operation.MoveNext())
        {
            if (operation.Current is IEnumerator nested) Drain(nested);
        }
    }

    private static IEnumerator EmptyRoutine() { yield break; }
    private static IEnumerator OneFrame() { yield return null; }
    private static IEnumerator NestedFailure() { yield return FailureAfterFrame(); }
    private static IEnumerator FailureAfterFrame()
    {
        yield return null;
        throw new InvalidOperationException("Nested failure");
    }

    private static IEnumerator PendingRoutine(Action onDisposed)
    {
        try { while (true) yield return null; }
        finally { onDisposed(); }
    }

    private sealed class TestModule : ILiveOpsModule
    {
        private readonly List<string> calls;
        public string Id { get; }
        public IReadOnlyCollection<string> Dependencies { get; set; }
        public Func<IEnumerator> Routine { get; set; } = EmptyRoutine;
        public Action ShutdownAction { get; set; }
        public int InitializationCount { get; private set; }
        public int ShutdownCount { get; private set; }

        public TestModule(string moduleId, List<string> calls, params string[] dependencies)
        {
            Id = moduleId;
            this.calls = calls;
            Dependencies = dependencies;
        }

        public IEnumerator Initialize()
        {
            InitializationCount++;
            calls.Add("start:" + Id);
            return Routine();
        }

        public void Shutdown()
        {
            ShutdownCount++;
            calls.Add("stop:" + Id);
            ShutdownAction?.Invoke();
        }
    }
}
