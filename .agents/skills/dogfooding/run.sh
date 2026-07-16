#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# 1. Environment
FLOWENGINE_API_KEY="${FLOWENGINE_API_KEY:-fe_VPY2kMdr74jIFVz4Hr6f834AVJjyN3kKpXzBAvB9odU}"
export FLOWENGINE_API_KEY

# 2. Ensure Host is up
if ! ss -tln | grep -q ':8001'; then
  echo "[Dogfooding] Host not running, starting FlowEngine.Host ..."
  BACKEND_DIR="$(cd "$SCRIPT_DIR/../../../../backend" && pwd)"
  dotnet run --project "$BACKEND_DIR/FlowEngine.Host" &
  HOST_PID=$!
  for i in $(seq 1 60); do
    sleep 1
    if ss -tln | grep -q ':8001'; then
      echo "[Dogfooding] FlowEngine.Host ready (PID $HOST_PID)"
      break
    fi
  done
  if ! ss -tln | grep -q ':8001'; then
    echo "[Dogfooding] Host startup timeout" >&2
    exit 1
  fi
else
  echo "[Dogfooding] FlowEngine.Host already on :8001"
fi

# 3. Run one round
echo "[Dogfooding] Starting orchestrator ..."
npx tsx src/orchestrator.ts

# 4. Show latest report
REPORT_DIR="$SCRIPT_DIR/../../docs/superpowers/dogfooding/runs"
if [ -d "$REPORT_DIR" ]; then
  LATEST=$(ls -t "$REPORT_DIR" | head -1)
  if [ -n "$LATEST" ]; then
    echo ""
    echo "[Dogfooding] Latest report: $REPORT_DIR/$LATEST"
  fi
fi
