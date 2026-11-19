#!/usr/bin/env bash
# Write a SLURM job body that verifies the warptools images on the GPU node it lands on.
#
#   ./make_verify_job.sh <runtime.sif> <test.sif> > run.sh
#   submit run.sh as a 2-GPU job (the second GPU exercises the multi-GPU cuFFT test)
#
# Both SIFs must be on storage the compute nodes see. The job calls apptainer itself, exactly as a
# WarpTools worker job does. Everything it prints goes to the job's stdout.log; the test results
# (TRX) are copied into the job directory. Node-local scratch is removed at the end.
set -euo pipefail
RUNTIME_SIF="${1:?runtime sif}"; TEST_SIF="${2:?test sif}"
cat <<EOF
#!/bin/bash
set -uo pipefail
RUNTIME_SIF='${RUNTIME_SIF}'
TEST_SIF='${TEST_SIF}'
EOF
cat <<'EOF'
JOBDIR="$PWD"
T0=$(date +%s)
section() { printf '\n######## %s  (+%ds)\n' "$1" $(( $(date +%s) - T0 )); }
W=$(mktemp -d "/tmp/warpverify.XXXXXX")
trap 'rm -rf -- "$W"' EXIT
AX=(apptainer exec --containall --nv)
[ -n "${CUDA_VISIBLE_DEVICES:-}" ] && AX+=(--env "CUDA_VISIBLE_DEVICES=${CUDA_VISIBLE_DEVICES}")

section "node"
echo "host=$(hostname)  slurm_job=${SLURM_JOB_ID:-?}  partition=${SLURM_JOB_PARTITION:-?}  CUDA_VISIBLE_DEVICES=${CUDA_VISIBLE_DEVICES:-unset}"
nvidia-smi --query-gpu=index,name,driver_version,compute_cap,memory.total --format=csv,noheader
nvidia-smi | grep -o 'CUDA Version: [0-9.]*'
echo "apptainer: $(command -v apptainer) $(apptainer --version 2>&1)"
for s in "$RUNTIME_SIF" "$TEST_SIF"; do ls -la "$s"; done

section "0 GPUs the container sees (must equal the allocation, never the whole node)"
# nvidia-smi (NVML) ignores CUDA_VISIBLE_DEVICES and shows whatever device nodes it can open, so it
# is reported but not trusted; the gate is the CUDA runtime's own count, which is what tests open.
echo "host nvidia-smi -L: $(nvidia-smi -L | wc -l) GPU(s)"
"${AX[@]}" "$RUNTIME_SIF" nvidia-smi -L | sed 's/^/  in container (NVML): /'
cuda_n=$("${AX[@]}" "$RUNTIME_SIF" python -c 'import torch; print(torch.cuda.device_count())' 2>&1 | tail -1)
alloc_n=$(echo "${CUDA_VISIBLE_DEVICES:-}" | tr ',' '\n' | grep -c .)
echo "CUDA devices opened in container: ${cuda_n}; allocated (CUDA_VISIBLE_DEVICES=${CUDA_VISIBLE_DEVICES:-unset}): ${alloc_n}"
if [ -n "${CUDA_VISIBLE_DEVICES:-}" ] && [ "${cuda_n}" != "${alloc_n}" ]; then
    echo "ABORT: the container can open ${cuda_n} CUDA devices but this job was allocated ${alloc_n}"; exit 4
fi

section "1 runtime image preflight"
"${AX[@]}" "$RUNTIME_SIF" warp preflight; echo "preflight_rc=$?"

section "2 CUDA runtime linkage (3aa0112: one shared libcudart, no static copy)"
"${AX[@]}" "$TEST_SIF" bash -c '
    so=/opt/warp/bin/libNativeAcceleration.so
    echo "libcudart in NEEDED: $(readelf -d $so | grep -c "NEEDED.*libcudart")"
    echo "cudart symbols DEFINED inside NativeAcceleration (static embedding would show many): $(nm -D --defined-only $so | grep -c -E " cuda(SetDevice|Malloc|LaunchKernel|GetDevice)$")"
    echo "distinct libcudart files resolved: $(ldd $so | grep libcudart | sort -u | wc -l)"
    ldd $so | grep -E "libcudart|libcufft|libcublas|libcurand|libtorch_cuda" | sed "s/^/  /"'

section "3 upstream test suite, CUDA tests enabled"
# The image is read-only; run from a node-local copy of the built tree.
"${AX[@]}" -B "$W:$W" --env WARP_RUN_CUDA_TESTS=1 --env "W=$W" "$TEST_SIF" bash -c '
    set -o pipefail
    cp -a /opt/build/warp "$W/warp" && cd "$W/warp" || exit 9
    export HOME="$W/home" DOTNET_CLI_HOME="$W/home"; mkdir -p "$HOME"
    timeout 2400 dotnet test Tests --no-build --no-restore \
        --results-directory "$W/results" --logger "trx;LogFileName=tests.trx" \
        --logger "console;verbosity=normal" 2>&1 | tail -400
    echo "dotnet_test_rc=$?"'
cp "$W"/results/*.trx "$JOBDIR/" 2>/dev/null && echo "copied $(ls "$JOBDIR"/*.trx)"

section "4 summary"
f=$(ls "$JOBDIR"/*.trx 2>/dev/null | head -1)
if [ -n "$f" ]; then
    grep -o '<Counters [^>]*>' "$f"
    echo "failed tests:"; grep -o 'testName="[^"]*" [^>]*outcome="Failed"' "$f" | sed 's/^/  /' | head -50
    echo "CUDA/multi-GPU tests executed:"; grep -o -E 'testName="[^"]*(Cuda|Gpu|Cufft)[^"]*" [^>]*outcome="[A-Za-z]*"' "$f" | sed -E 's/ [^>]*outcome=/ outcome=/; s/^/  /' | head -60
fi
echo "elapsed=$(( $(date +%s) - T0 ))s"
EOF
