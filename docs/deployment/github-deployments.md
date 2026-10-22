# GitHub Deployments Dashboard & Workflow Architecture

This document describes:
1. The segregated multi-workflow architecture (**CI on PRs**, **CD on Main**, and **On-Demand SHA Deployment**).

## Rich Summary in Workflow Runs

Every deployment run in GitHub Actions also produces an **Deployment Summary Card** at the top of the workflow run:

```
🚀 Deployments
├── development:   ✅ Deployed in 42s  (https://func-edc-dev...)
├── test:          ✅ Deployed in 38s  (https://func-edc-test...)
└── production:    ⏸️ Awaiting Review by @dfe/edc-lead-engineers
```

## Segregated Workflows

We have replaced the single monolithic file with **3 specialised workflows** powered by shared composite actions:

```
.github/
├── actions/
│   ├── setup-and-test/      # Reusable: SDK setup, tests, TRX parsing, CalVer stamp, Linux x64 zip
│   └── deploy-function/     # Reusable: OIDC login, az config-zip, smoke test
└── workflows/
    ├── ci-pr.yml            # 1. CI Only: Runs on PRs to main, tests, updates sticky comment
    ├── cd-main.yml          # 2. CD Only: Runs on merge to main (Build -> Dev -> Test -> Prod Gate)
    └── deploy-by-sha.yml    # 3. Manual CD: Provide any Git SHA + Environment to deploy
```

---

### Workflow 1: `ci-pr.yml` (Pull Request Quality Gate)
- **Trigger**: `pull_request` targeting `main`.
- **Permissions**: `pull-requests: write` (no Azure cloud access).
- **Behavior**:
  - Restores and executes all 78 tests.
  - Generates TRX reports and code coverage.
  - Verifies that self-contained Linux x64 compilation succeeds.
  - Posts/updates a **Sticky Comment** on the PR with CalVer candidate, test pass count, and duration.
  - **Does NOT touch Azure**.

---

### Workflow 2: `cd-main.yml` (Main Release Promotion)
- **Trigger**: `push` on `main` (when a PR merges).
- **Behavior**:
  1. **Build Once**: Stamps official CalVer (e.g. `2026.10.42`), compiles, packages `edc-release-bundle`.
  2. **Deploy Development**: Deploys to `development` and verifies `/api/about`.
  3. **Deploy Test**: Automatically promotes identical zip to `test` environment.
  4. **Deploy Production**: **Pauses at GitHub Environment Gate**. Designated reviewers approve the release before deployment executes.

---

### Workflow 3: `deploy-by-sha.yml` (Ad-Hoc / Rollback by Git SHA)
- **Trigger**: `workflow_dispatch` (Manual button in GitHub Actions UI).
- **Inputs**:
  - `commit_sha`: Any Git commit SHA (e.g. `a1b2c3d` or full 40-character SHA).
  - `target_environment`: Choice of `development`, `test`, or `production`.
- **Behavior**:
  - Checks out the exact historical commit specified.
  - Builds and packages using the shared `setup-and-test` action.
  - Deploys directly to the target environment (respecting environment approval protection if targeting `production`).
  - Perfect for hotfixes, testing feature branches in Dev, or rolling back Production to a known good SHA.
