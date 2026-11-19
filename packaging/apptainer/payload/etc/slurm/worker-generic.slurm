#!/bin/bash
# WarpTools worker job, generic: every resource is a --cluster_var. A site replaces this with
# per-partition templates via build_sif.sh --site. Example:
#   WarpTools <command> ... --cluster_config /opt/warp/etc/slurm-rest.json \
#       --cluster_script /opt/warp/etc/slurm/worker-generic.slurm --pool_size 4 \
#       --cluster_var partition=gpu --cluster_var constraint=a100 --cluster_var time=04:00:00 \
#       --cluster_var cpus=8 --cluster_var mem=64G
# Rendered by WarpTools (cluster mode) and submitted by warp-slurm over slurmrestd, which reads these
# #SBATCH lines. {{command}}, {{tasks_dir}} and {{logs_dir}} are filled in by WarpTools.
#SBATCH --job-name=warp-worker
#SBATCH --partition={{partition}}
#SBATCH --constraint={{constraint}}
#SBATCH --gres=gpu:1
#SBATCH --cpus-per-task={{cpus}}
#SBATCH --mem={{mem}}
#SBATCH --time={{time}}
#SBATCH --output={{logs_dir}}/warp-worker-%j.out
#SBATCH --error={{logs_dir}}/warp-worker-%j.out
set -eu
: "${WARP_SIF:?WARP_SIF is not set: submit through the warptools launcher}"
command -v apptainer >/dev/null 2>&1 || { echo "apptainer is not on PATH on $(hostname)" >&2; exit 3; }

# The worker command is written to a file on shared storage, then run INSIDE the same image the
# job was submitted from. A quoted heredoc keeps it verbatim (it contains $(hostname), &, wait).
WARP_CMD="{{logs_dir}}/warp-worker-${SLURM_JOB_ID}.cmd.sh"
cat > "$WARP_CMD" <<'WARP_WORKER_COMMAND'
{{command}}
WARP_WORKER_COMMAND

BINDS="{{tasks_dir}},{{logs_dir}}${WARP_BINDS:+,$WARP_BINDS}"
# Fail fast, with a reason, on a GPU this image has no code for (e.g. a CUDA 13 image on Volta).
apptainer exec --containall --nv "$WARP_SIF" warp check-gpu || exit 3
# --containall drops the scheduler's GPU scoping, so CUDA_VISIBLE_DEVICES is passed explicitly,
# and only when set (an empty value would hide every GPU).
exec apptainer exec --containall --nv ${CUDA_VISIBLE_DEVICES:+--env CUDA_VISIBLE_DEVICES=$CUDA_VISIBLE_DEVICES} \
    -B "$BINDS" "$WARP_SIF" bash "$WARP_CMD"
