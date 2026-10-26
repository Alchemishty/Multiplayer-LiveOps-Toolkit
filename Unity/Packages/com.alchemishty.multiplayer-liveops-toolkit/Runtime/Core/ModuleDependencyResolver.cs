using System;
using System.Collections.Generic;

namespace MultiplayerLiveOpsToolkit.Core
{
    internal static class ModuleDependencyResolver
    {
        internal static IReadOnlyList<ILiveOpsModule> Resolve(IEnumerable<ILiveOpsModule> modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));

            var modulesById = new Dictionary<string, ILiveOpsModule>(StringComparer.Ordinal);
            var dependenciesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var registrationOrder = new List<string>();
            foreach (var module in modules)
            {
                if (module == null)
                    throw new ArgumentException("Selected modules contain a null module.", nameof(modules));
                string moduleId = module.Id;
                if (string.IsNullOrWhiteSpace(moduleId))
                    throw new ArgumentException("Every selected module requires a non-empty ID.", nameof(modules));
                if (modulesById.ContainsKey(moduleId))
                    throw new ArgumentException($"Module '{moduleId}' is registered more than once.", nameof(modules));
                if (module.Dependencies == null)
                    throw new ArgumentException($"Module '{moduleId}' has a null dependency collection.", nameof(modules));

                var dependencies = new List<string>(module.Dependencies);
                foreach (string dependencyId in dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependencyId))
                        throw new ArgumentException($"Module '{moduleId}' has an empty dependency ID.", nameof(modules));
                }
                modulesById.Add(moduleId, module);
                dependenciesById.Add(moduleId, dependencies);
                registrationOrder.Add(moduleId);
            }

            foreach (string moduleId in registrationOrder)
            {
                foreach (string dependencyId in dependenciesById[moduleId])
                {
                    if (!modulesById.ContainsKey(dependencyId))
                        throw new ArgumentException(
                            $"Module '{moduleId}' requires unselected module '{dependencyId}'.", nameof(modules));
                }
            }

            var resolvedModules = new List<ILiveOpsModule>();
            var visitedIds = new HashSet<string>(StringComparer.Ordinal);
            var activePath = new List<string>();
            foreach (string moduleId in registrationOrder)
                Visit(moduleId, modulesById, dependenciesById, visitedIds, activePath, resolvedModules);
            return resolvedModules.AsReadOnly();
        }

        private static void Visit(string moduleId, Dictionary<string, ILiveOpsModule> modulesById,
            Dictionary<string, List<string>> dependenciesById, HashSet<string> visitedIds,
            List<string> activePath, List<ILiveOpsModule> resolvedModules)
        {
            if (visitedIds.Contains(moduleId)) return;
            if (activePath.Contains(moduleId))
                throw new ArgumentException(
                    $"Cyclic module dependency: {string.Join(" -> ", activePath)} -> {moduleId}.");

            activePath.Add(moduleId);
            foreach (string dependencyId in dependenciesById[moduleId])
                Visit(dependencyId, modulesById, dependenciesById, visitedIds, activePath, resolvedModules);
            activePath.RemoveAt(activePath.Count - 1);
            visitedIds.Add(moduleId);
            resolvedModules.Add(modulesById[moduleId]);
        }
    }
}
