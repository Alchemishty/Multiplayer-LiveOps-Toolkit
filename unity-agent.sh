#!/usr/bin/env bash
set -uo pipefail

# Launches Unity, waits for the MCP plugin to start its own server and
# connect, then runs the agent with live compilation and test feedback.
#
# Architecture: The Unity MCP plugin launches its own server process
# (includes a WebSocket hub that Unity connects to). We don't launch
# the server externally — the plugin manages the full lifecycle.
# The agent connects via ~/.claude.json (localhost:8080/mcp).
#
# Used as the --agent command for unity-gamedev-bench:
#   ./scripts/run-all.sh \
#       --agent "/path/to/unity-agent.sh" \
#       --setup "/path/to/harness-setup.sh" ...
#
# Environment variables:
#   UNITY_BIN           Path to Unity editor binary
#   REVIEW_LOOP_MODEL   Model for the agent (default: opus)
#   UNITY_AGENT_MODE    "review" for review loop, "single" for single pass (default: review)
#   MCP_WAIT_TIMEOUT    Max seconds total (default: 300)

PROMPT="$1"
PROJECT_PATH="$(pwd)"

UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/2022.3.33f1/Unity.app/Contents/MacOS/Unity}"
MODE="${UNITY_AGENT_MODE:-review}"
MCP_TIMEOUT="${MCP_WAIT_TIMEOUT:-300}"
MODEL="${REVIEW_LOOP_MODEL:-opus}"

# Resolve symlinks to find the real harness directory
SCRIPT_PATH="$0"
if [[ -L "$SCRIPT_PATH" ]]; then
    SCRIPT_PATH="$(readlink "$SCRIPT_PATH")"
fi
HARNESS_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"

export UNITY_MCP_ALLOW_BATCH=1
export REVIEW_LOOP_MODEL="$MODEL"

# ---- Set EditorPrefs BEFORE Unity launches ----
# HttpAutoStartHandler reads these on first domain reload.
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.UseHttpTransport" -bool true
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.AutoStartOnLoad" -bool true
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.HttpServerLaunchConfirmed" -bool true
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.SetupCompleted" -bool true
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.SetupDismissed" -bool true
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.HttpTransportScope" -string "local"
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.HttpUrl" -string "http://127.0.0.1:8080"
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.DebugLogs" -bool true
# Critical: Unity doesn't inherit the user's shell PATH. The plugin can't
# find uvx without this override, so StartLocalHttpServer fails silently.
UVX_PATH=$(command -v uvx 2>/dev/null || echo "/opt/homebrew/bin/uvx")
defaults write com.unity3d.UnityEditor5.x "MCPForUnity.UvxPath" -string "$UVX_PATH"

# Force sync preferences so Unity reads them immediately
defaults synchronize com.unity3d.UnityEditor5.x 2>/dev/null || true

# ---- Free port 8080 for the plugin ----
# The plugin needs port 8080 to launch its own server with the WebSocket hub.
EXISTING_PID=$(lsof -ti :8080 2>/dev/null | head -1)
if [[ -n "$EXISTING_PID" ]]; then
    echo "[unity-agent] Port 8080 in use by PID ${EXISTING_PID}. Killing..."
    kill "$EXISTING_PID" 2>/dev/null || true
    sleep 3
fi

# ---- Cleanup handler ----
UNITY_PID=""
cleanup() {
    echo "[unity-agent] Cleaning up..."
    [[ -n "$UNITY_PID" ]] && kill "$UNITY_PID" 2>/dev/null || true
    sleep 2
    [[ -n "$UNITY_PID" ]] && kill -9 "$UNITY_PID" 2>/dev/null || true
    echo "[unity-agent] Done."
}
trap cleanup EXIT INT TERM

# ---- Prevent non-code files from polluting the diff ----
cat >> "$PROJECT_PATH/.gitignore" << 'GIEOF'
*.log
GIEOF

# ---- Launch Unity ----
echo "[unity-agent] Launching Unity for: ${PROJECT_PATH}"

UNITY_LOG="${PROJECT_PATH}/unity-agent.log"
"$UNITY_BIN" \
    -projectPath "$PROJECT_PATH" \
    -logFile "$UNITY_LOG" &
UNITY_PID=$!

# ---- Wait for compile + MCP bridge ----
echo "[unity-agent] Waiting for Unity to compile and MCP to connect (timeout: ${MCP_TIMEOUT}s)..."

WAIT_START=$(date +%s)
COMPILED=false
COMPILE_TIME=0

while true; do
    ELAPSED=$(( $(date +%s) - WAIT_START ))

    if [[ $ELAPSED -ge $MCP_TIMEOUT ]]; then
        echo "[unity-agent] ERROR: Not ready after ${MCP_TIMEOUT}s"
        exit 2
    fi

    if ! kill -0 "$UNITY_PID" 2>/dev/null; then
        echo "[unity-agent] ERROR: Unity process died."
        tail -20 "$UNITY_LOG" 2>/dev/null
        exit 2
    fi

    # Detect compilation complete
    if ! $COMPILED && grep -q "Application.AssetDatabase Initial Refresh End" "$UNITY_LOG" 2>/dev/null; then
        COMPILED=true
        COMPILE_TIME=$ELAPSED
        echo "[unity-agent] Unity compiled after ${ELAPSED}s. Waiting for MCP bridge..."
    fi

    # Detect MCP bridge connected
    if $COMPILED && grep -q "Session connected\|Registered with session" "$UNITY_LOG" 2>/dev/null; then
        echo "[unity-agent] MCP bridge connected after ${ELAPSED}s."
        break
    fi

    # Bridge timeout: 90s after compilation (bridge takes 30-70s to connect)
    if $COMPILED && [[ $((ELAPSED - COMPILE_TIME)) -gt 90 ]]; then
        echo "[unity-agent] WARNING: MCP bridge not connected 90s after compile. Proceeding without."
        break
    fi

    if [[ $((ELAPSED % 30)) -eq 0 && $ELAPSED -gt 0 ]]; then
        if $COMPILED; then
            echo "[unity-agent] Compiled. Waiting for MCP bridge... (${ELAPSED}s)"
        else
            echo "[unity-agent] Still compiling... (${ELAPSED}s)"
        fi
    fi

    sleep 5
done

# ---- Determine project name ----
PROJECT_NAME=$(basename "$PROJECT_PATH")

# ---- Augment prompt with instance targeting ----
AUGMENTED_PROMPT="$PROMPT

Note: If multiple Unity instances are connected, call set_active_instance to target '${PROJECT_NAME}'."

echo "[unity-agent] Starting agent..."

# ---- Run the agent ----
# Agent uses ~/.claude.json which points to localhost:8080/mcp — the plugin's server
if [[ "$MODE" == "review" ]]; then
    "$HARNESS_DIR/review-loop.sh" "$AUGMENTED_PROMPT" 2>&1
else
    claude -p --dangerously-skip-permissions --model "$MODEL" -- "$AUGMENTED_PROMPT" 2>&1
fi

AGENT_EXIT=$?
echo "[unity-agent] Agent finished (exit ${AGENT_EXIT})."
exit $AGENT_EXIT
