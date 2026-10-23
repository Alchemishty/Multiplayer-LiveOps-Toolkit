#!/bin/bash
set -euo pipefail

# Injects the harness into a Unity project's working directory.
# Uses a per-repo doc cache to avoid regenerating docs for every task.
#
# Usage with unity-gamedev-bench:
#   ./scripts/run-all.sh --agent "/path/to/unity-agent.sh" \
#       --label my-run \
#       --setup "/path/to/unity-gamedev-harness/harness-setup.sh" \
#       s01 m01 b04
#
# Usage standalone (any Unity project):
#   cd /path/to/unity-project && /path/to/harness-setup.sh
#
# Environment variables:
#   HARNESS_BENCH_MODE=1    Append benchmark mode override to CLAUDE.md

SCRIPT_PATH="$0"
[[ -L "$SCRIPT_PATH" ]] && SCRIPT_PATH="$(readlink "$SCRIPT_PATH")"
HARNESS_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"
CACHE_DIR="${HARNESS_DIR}/.doc-cache"

# Copy universal harness files
cp "$HARNESS_DIR/CLAUDE.md" "$HARNESS_DIR/AGENTS.md" "$HARNESS_DIR/harness.yaml" .
cp -R "$HARNESS_DIR/enforcement" .
# Install Unity MCP plugin if not already present
if [[ -f "Packages/manifest.json" ]]; then
    if ! grep -q "com.coplaydev.unity-mcp" Packages/manifest.json; then
        python3 -c "
import json, sys
with open('Packages/manifest.json') as f:
    m = json.load(f)
m.setdefault('dependencies', {})['com.coplaydev.unity-mcp'] = 'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.0.0'
with open('Packages/manifest.json', 'w') as f:
    json.dump(m, f, indent=2)
    f.write('
')
"
        echo "MCP plugin added to Packages/manifest.json."
    else
        echo "MCP plugin already in Packages/manifest.json."
    fi
else
    echo "WARNING: No Packages/manifest.json found — MCP plugin not installed."
fi

# Force MCP bridge connection via an InitializeOnLoad script.
# HttpAutoStartHandler has a race condition (runs before prefs are set),
# so we explicitly start the bridge ourselves after a delay.
mkdir -p Assets/Editor
cat > Assets/Editor/McpBridgeForce.cs << 'MCPEOF'
#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class McpBridgeForce
{
    private static int _ticks = 0;
    private static bool _done = false;

    static McpBridgeForce()
    {
        Debug.Log("[McpBridgeForce] Registered update hook");
        EditorApplication.update += OnUpdate;
    }

    static void OnUpdate()
    {
        if (_done) return;
        _ticks++;
        // Wait 60 ticks (~1s) for services to initialize
        if (_ticks < 60) return;
        if (_ticks == 60) Debug.Log("[McpBridgeForce] Starting bridge attempt...");
        // Try every 60 ticks for 5 attempts
        if (_ticks % 60 != 0) return;
        if (_ticks > 360) { _done = true; EditorApplication.update -= OnUpdate; return; }

        try
        {
            var asm = Assembly.Load("MCPForUnity.Editor");
            if (asm == null) { Debug.Log("[McpBridgeForce] No assembly"); return; }

            var locatorType = asm.GetType("MCPForUnity.Editor.Services.MCPServiceLocator");
            if (locatorType == null) { Debug.Log("[McpBridgeForce] No locator"); return; }

            // Try Server.StartLocalHttpServer first
            var serverProp = locatorType.GetProperty("Server",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (serverProp != null)
            {
                var server = serverProp.GetValue(null);
                // Check if reachable
                var isReachable = server.GetType().GetMethod("IsLocalHttpServerReachable",
                    BindingFlags.Public | BindingFlags.Instance);
                bool reachable = isReachable != null && (bool)isReachable.Invoke(server, null);
                Debug.Log($"[McpBridgeForce] Server reachable: {reachable}");

                if (!reachable)
                {
                    var startServer = server.GetType().GetMethod("StartLocalHttpServer",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (startServer != null)
                    {
                        bool started = (bool)startServer.Invoke(server, new object[] { true });
                        Debug.Log($"[McpBridgeForce] StartLocalHttpServer: {started}");
                    }
                    // Server just launched — wait for it to boot before connecting the bridge
                    return;
                }
            }

            // Now start the bridge
            var bridgeProp = locatorType.GetProperty("Bridge",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (bridgeProp != null)
            {
                var bridge = bridgeProp.GetValue(null);
                var startAsync = bridge.GetType().GetMethod("StartAsync",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (startAsync != null)
                {
                    startAsync.Invoke(bridge, null);
                    Debug.Log("[McpBridgeForce] Bridge.StartAsync invoked");
                    _done = true;
                    EditorApplication.update -= OnUpdate;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"[McpBridgeForce] Error: {ex.Message}");
        }
    }
}
#endif
MCPEOF
echo "MCP bridge force-start script installed."

# Cache key: hash of the C# file listing (stable per repo+SHA)
CACHE_KEY=$(find . -name "*.cs" -not -path "*/Library/*" -not -path "*/Temp/*" | sort | md5 -q 2>/dev/null || find . -name "*.cs" -not -path "*/Library/*" -not -path "*/Temp/*" | sort | md5sum | cut -c1-12)

CACHED_DOCS="${CACHE_DIR}/${CACHE_KEY}"

if [[ -d "$CACHED_DOCS" && -f "$CACHED_DOCS/architecture.md" ]]; then
    mkdir -p docs
    cp "$CACHED_DOCS"/*.md docs/
    echo "Harness installed with cached docs (${CACHE_KEY:0:12})."
else
    "$HARNESS_DIR/generate-docs.sh"
    mkdir -p "$CACHED_DOCS"
    cp docs/*.md "$CACHED_DOCS/"
    echo "Harness installed — docs generated and cached (${CACHE_KEY:0:12})."
fi

# Benchmark override: single-turn mode, implement directly
if [[ "${HARNESS_BENCH_MODE:-}" == "1" ]]; then
cat >> CLAUDE.md << 'BENCHEOF'

## Benchmark Mode

This is a single-turn benchmark evaluation. You will receive one prompt and must produce a complete implementation in your response. Do NOT:
- Ask for confirmation or approval
- Present a design and wait for feedback
- Say "I'll implement once you approve"
- Output only a plan without code

Instead: read the codebase, understand the task, and write all the code changes directly. Include tests as required by the rules above.
BENCHEOF
echo "Benchmark mode enabled."
fi
