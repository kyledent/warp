# warptools Apptainer image

One image for the Warp/M/Cube **Avalonia GUIs** and the **WarpTools/MTools/MCore** command-line
tools, built from this repository's own recipe. A user runs it on a Linux workstation, including
WSL2, and can send WarpTools work to a SLURM cluster from it. **The same image is both the front
end and the worker on the compute nodes.**

## Build

```bash
packaging/apptainer/build_sif.sh --site /path/to/site-config --out /path/to/sifs   # runtime image
packaging/apptainer/build_sif.sh --variant test --out /path/to/sifs                 # + SDK and built tests
packaging/apptainer/build_sif.sh --cuda 13.0 ...                                    # CUDA 13 (default 12.9)
```

`--cuda 12.9` uses `warp_build.yml` and runs on Pascal and Volta as well; `--cuda 13.0` uses
`warp_build_cuda13.yml` and needs an R580+ driver and compute capability 7.5 or newer. Worker
jobs run `warp check-gpu` first, so an image sent to a GPU it has no code for stops with a
reason instead of failing at its first kernel.

- **Source.** The source is `git archive HEAD`, so an image is exactly one commit. A dirty tree is
  refused unless `--allow-dirty`. `PROVENANCE` (in the image at `/opt/warp/PROVENANCE`) records
  the commit, the site directory's commit, the variant, the CUDA architectures and the tarball
  hash. The def refuses a tarball that does not match it.
- **Build stage:** `warp_build.yml` (CUDA 12.9, conda-forge PyTorch `cuda129`, .NET 10),
  `scripts/build-native-unix.sh` and `scripts/publish-unix.sh`, as in the top-level README. The
  only change is `CMAKE_CUDA_ARCHITECTURES` (`--archs`, default `61;70;89;120`), followed by a
  self-contained publish of the three GUIs.
- **Final stage:** Rocky Linux 8.10 (glibc 2.28) with the published apps, a runtime conda env
  pinned to the exact package builds the build env resolved, and the X11 client libraries.
- **Build hosts without a GPU** need `CONDA_OVERRIDE_CUDA=12.9`; the def sets it. On a host whose
  `/tmp` is `noexec`, set `APPTAINER_TMPDIR` to a local disk.
- **The `test` variant** is the build stage itself (SDK, built `Tests/`). It is cut from the same
  def, so it cannot drift from the runtime image.

## Use

Install `launcher/warptools` next to the SIF and run it as an ordinary command:

```bash
warptools WarpTools fs_motion_and_ctf --settings warp_frameseries.settings ...
warptools gui            # m-gui, cube-gui; needs DISPLAY (ssh -X, WSLg, a desktop session)
warptools preflight      # libraries, GPU, versions
```

The launcher always runs `--containall`, so no host conda env or module can shadow the image's
libraries. It binds:
- the working directory and the directory of every path in the arguments;
- every absolute path inside a `.settings` file named in the arguments;
- `$WARPTOOLS_BINDS`, and each `--bind DIR`;
- the X11 socket and Xauthority, `~/.slurm` and the host CA store.

Site defaults (for example `WARPTOOLS_SHARED_PREFIXES`) can sit in a `warptools.conf` next to the
launcher.

## Sending work to a SLURM cluster

WarpTools' cluster mode (`--cluster_config`, `--cluster_script`, `--pool_size`; see the
top-level README) renders a batch-script template and runs `submit`, `cancel` and `status_list`
commands. Workers pull tasks from a queue directory on shared storage.

**Workstations often have no SLURM client**, so the image ships `warp-slurm`, which implements
those commands over **slurmrestd** with the user's own JWT:

- `payload/etc/slurm-rest.json` is the cluster config that points WarpTools at `warp-slurm`.
- `warp-slurm submit` reads the job description from the template's `#SBATCH` lines, so users
  edit one familiar file.
- `payload/etc/slurm/worker-generic.slurm` takes every resource as a `--cluster_var`. A site
  replaces it with per-partition templates.
- **Every template runs the worker command inside the same SIF on the compute node**, with the
  same binds. The launcher exports `WARP_SIF` and `WARP_BINDS` for this. It also forwards
  `CUDA_VISIBLE_DEVICES`, which `--containall` would otherwise strip, because without it a job
  would see every GPU on the node.

```bash
warptools slurm check            # token valid? endpoint reachable? partitions visible?
warptools cluster-help           # the options to add, and the installed templates
```

## Site configuration (`--site DIR`)

The site directory is overlaid onto `/opt/warp/etc` and is kept outside this repository. It holds:
- `site.env`: `WARP_SLURM_URL`, `WARP_SLURM_API`, `WARP_SLURM_TOKEN_FILE`, `WARP_SLURM_TOKEN_HELP`.
  See `payload/etc/site.env.example`.
- `slurm/worker-<profile>.slurm`: one template per partition or GPU type, with its own
  walltime cap.

## Verification

`verify/make_verify_job.sh <runtime.sif> <test.sif>` writes a SLURM job body. Submit it as a
2-GPU job. It:
1. checks that the CUDA device count inside the container equals the allocation;
2. runs the runtime image's preflight;
3. checks that `libNativeAcceleration.so` links one shared `libcudart` and embeds no static copy;
4. runs the test suite with `WARP_RUN_CUDA_TESTS=1`, including the multi-GPU cuFFT regression
   test.
