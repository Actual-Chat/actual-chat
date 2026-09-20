# ci-watchdog-data

Machine-written records of red CI runs. No source code lives here, and nothing
here is ever merged anywhere.

`.github/workflows/ci-watchdog.yml` writes one file per red run attempt:

    records/<year>/<month>/<runId>-<attempt>.json

One file per attempt, so concurrent runs never contend for the same path. Each
record carries the run's branch, head SHA, workflow, the failed jobs with the
step that failed and its category, and for test jobs the failed test names with
their durations, error messages and whether they are known flakes.

The point of keeping every run — including runs on feature branches — is that
flake and regression are only distinguishable across branches: a test failing on
several unrelated branches inside a narrow window is a regression, while the
same test failing once here and once there across a week is a flake.