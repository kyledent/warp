#!/usr/bin/env bash
# Build the warptools SIF from this repository's committed tree.
#
#   packaging/apptainer/build_sif.sh [--variant runtime|test] [--site DIR] [--out DIR]
#                                    [--context DIR] [--jobs N] [--archs "61;70;89;120"] [--allow-dirty]
#                                    [--pkgs-cache DIR]
#
# runtime: two-stage image, published apps + pinned runtime env (what users get).
# test:    the build stage itself (SDK, sources, built Tests), cut from the same def so the two
#          cannot drift; used to run the test suite on GPU nodes.
# --site:  a directory overlaid onto /opt/warp/etc (site.env, slurm/worker-*.slurm): the cluster
#          endpoint and partition templates for one site. Kept out of this repository.
#
# The source is `git archive HEAD`, so the image is exactly one commit; PROVENANCE records it (and the
# site directory's commit, if it is a git tree) and the def refuses a tarball that does not match.
# A dirty tree is refused unless --allow-dirty, which records the uncommitted diff's hash instead.
# Build state goes to $APPTAINER_TMPDIR; on hosts whose /tmp is noexec, point it at a local disk.
# --pkgs-cache: a host directory bound in as the conda package cache, so rebuilds do not re-download.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(git -C "$HERE" rev-parse --show-toplevel)"
VARIANT=runtime SITE="" OUT="${WARP_SIF_OUT:-$PWD}" CONTEXT="" JOBS=4 ARCHS="61;70;89;120" DIRTY=0 PKGS=""
while [ $# -gt 0 ]; do
    case "$1" in
        --variant) VARIANT="$2"; shift 2 ;;
        --site) SITE="$(cd "$2" && pwd)"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        --context) CONTEXT="$2"; shift 2 ;;
        --jobs) JOBS="$2"; shift 2 ;;
        --archs) ARCHS="$2"; shift 2 ;;
        --allow-dirty) DIRTY=1; shift ;;
        --pkgs-cache) PKGS="$2"; shift 2 ;;
        *) echo "unknown option $1" >&2; exit 2 ;;
    esac
done
case "$VARIANT" in runtime|test) ;; *) echo "--variant runtime|test" >&2; exit 2 ;; esac

dirty_files=$(git -C "$REPO" status --porcelain | wc -l)
if [ "$dirty_files" -gt 0 ] && [ "$DIRTY" -ne 1 ]; then
    echo "the working tree has $dirty_files uncommitted change(s); commit them or pass --allow-dirty" >&2
    exit 1
fi

CONTEXT="${CONTEXT:-$(mktemp -d "${APPTAINER_TMPDIR:-/tmp}/warptools-context.XXXXXX")}"
mkdir -p "$CONTEXT" "$OUT"
rm -rf "$CONTEXT/payload" "$CONTEXT/site"
cp -a "$HERE/payload" "$CONTEXT/payload"
mkdir -p "$CONTEXT/site"
[ -n "$SITE" ] && cp -a "$SITE/." "$CONTEXT/site/" && rm -rf "$CONTEXT/site/.git"

# Source: the committed tree (plus, with --allow-dirty, the working tree's tracked + untracked files).
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/warp-src.XXXXXX")"
trap 'rm -rf -- "$STAGE"' EXIT
mkdir -p "$STAGE/warp"
if [ "$DIRTY" -eq 1 ]; then
    (cd "$REPO" && git ls-files -co --exclude-standard -z | tar --null -T - -cf -) | tar -xf - -C "$STAGE/warp"
else
    git -C "$REPO" archive --format=tar HEAD | tar -xf - -C "$STAGE/warp"
fi
tar -C "$STAGE" --sort=name --owner=0 --group=0 --numeric-owner --mtime='2026-01-01 00:00Z' \
    -czf "$CONTEXT/warp-src.tar.gz" warp

head=$(git -C "$REPO" rev-parse HEAD)
{
    echo "staged_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "repo_head=$head"
    echo "repo_branch=$(git -C "$REPO" rev-parse --abbrev-ref HEAD)"
    echo "repo_describe=$(git -C "$REPO" describe --tags --always 2>/dev/null)"
    echo "repo_uncommitted_files=$dirty_files"
    [ "$DIRTY" -eq 1 ] && echo "repo_uncommitted_sha256_16=$(git -C "$REPO" diff HEAD | sha256sum | cut -c1-16)"
    if [ -n "$SITE" ]; then
        echo "site_dir=$(basename "$SITE")"
        git -C "$SITE" rev-parse HEAD >/dev/null 2>&1 && echo "site_head=$(git -C "$SITE" rev-parse HEAD)" \
            && echo "site_uncommitted_files=$(git -C "$SITE" status --porcelain | wc -l)"
    fi
    echo "variant=$VARIANT"
    echo "cuda_archs=$ARCHS"
    echo "tarball_sha256=$(sha256sum "$CONTEXT/warp-src.tar.gz" | cut -d' ' -f1)"
} > "$CONTEXT/PROVENANCE"
cat "$CONTEXT/PROVENANCE"

DEF="$CONTEXT/warptools.def"
if [ "$VARIANT" = test ]; then
    # Everything up to the final stage, then a trailer that makes the build stage runnable.
    awk 'NR>1 && /^Bootstrap:/{exit} {print}' "$HERE/warptools.def" > "$DEF"
    cat >> "$DEF" <<'EOF'

%environment
    export WARP_HOME=/opt/warp
    export PATH=/opt/warp/bin:/opt/warp_build/bin:/usr/local/bin:/usr/bin:/bin
    export LD_LIBRARY_PATH=/opt/warp_runtime/lib:/opt/warp/bin${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
    export LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8 DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES=/opt/nuget
    export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${TMPDIR:-/tmp}/warp-dotnet-bundle"

%runscript
    exec /opt/warp/bin/warp-entry "$@"
EOF
else
    cp "$HERE/warptools.def" "$DEF"
fi

SIF="$OUT/warptools-${VARIANT}-cuda12.9-$(date -u +%Y%m%d)-${head:0:8}.sif"
echo "building $SIF  (archs $ARCHS, jobs $JOBS, context $CONTEXT)"
cd "$CONTEXT"
BIND_ARGS=()
if [ -n "$PKGS" ]; then
    mkdir -p "$PKGS" && touch "$PKGS/.warp-pkgs-cache"
    BIND_ARGS=(--bind "$(cd "$PKGS" && pwd):/mnt")
fi
apptainer build --fakeroot --force "${BIND_ARGS[@]}" \
    --build-arg "CUDA_ARCHS=$ARCHS" --build-arg "BUILD_JOBS=$JOBS" --build-arg "VARIANT=$VARIANT" \
    "$SIF" "$DEF"
sha256sum "$SIF" | tee "$SIF.sha256"
ls -la "$SIF"
