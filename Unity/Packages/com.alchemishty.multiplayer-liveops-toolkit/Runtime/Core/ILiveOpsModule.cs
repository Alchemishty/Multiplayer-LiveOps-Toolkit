using System.Collections;
using System.Collections.Generic;

namespace MultiplayerLiveOpsToolkit.Core
{
    public interface ILiveOpsModule
    {
        string Id { get; }
        IReadOnlyCollection<string> Dependencies { get; }
        IEnumerator Initialize();
        void Shutdown();
    }
}
