---
status: "accepted"
sub-status: "implemented"
date: 2026-10-22
decision-makers: ["Chris Kelly"]
---

# 14. Segregated Multi-Workflow CI/CD with Reusable Composite Actions and Gated Promotion

## Context and Problem Statement

Deploying serverless .NET 10 Azure Functions across multiple environments (**Development**, **Test**, **Production**) using a single monolithic GitHub Actions workflow creates several operational risks:
1. **Coupling PR Validation to Deployment Logic**: Running deployment steps or requiring cloud deployment credentials during pull requests increases the attack surface and risks accidental deployment of unapproved feature branches.
2. **Duplicated Workflow Boilerplate**: Packaging, testing, and Azurite orchestration logic copy-pasted across multiple workflow files causes maintenance drift and inconsistency.
3. **Lack of Rollback / Ad-Hoc Deployment Capability**: A single trigger on `push: branches: [main]` prevents operators from deploying an arbitrary known-good commit SHA directly to an environment or executing an emergency rollback.
4. **Visibility & Governance**: Without explicit environment gating and visual separation, teams cannot determine how far a release has progressed or enforce manual peer review before production rollouts.

How should GitHub Actions workflows and deployment pipelines be structured across Development, Test, and Production?

## Decision Drivers

* **Least Privilege Security:** Pull request workflows must have zero cloud deployment credentials or Azure access.
* **Separation of Concerns:** Distinct workflows for PR validation, continuous release on `main`, and on-demand commit deployment.
* **DRY Pipeline Logic:** Centralise build, test, and deployment steps into reusable local composite actions (`.github/actions/`).
* **Environment Governance & Auditability:** Enforce mandatory team approvals before production deployment using native GitHub Environment Protection Rules.
* **Operational Rollback Flexibility:** Enable one-click manual deployment of any commit SHA to any target environment via `workflow_dispatch`.

## Considered Options

* **Option 1: Segregated Multi-Workflow Architecture with Reusable Local Composite Actions**
* **Option 2: Single Monolithic `deploy.yml` with Complex Branch Conditionals**
* **Option 3: External Release Orchestrator (e.g. Octopus Deploy, Azure DevOps Pipelines)**

## Decision Outcome

Chosen option: **"Option 1: Segregated Multi-Workflow Architecture with Reusable Local Composite Actions"**, because it provides clean separation of concerns, eliminates duplicate pipeline code, and natively integrates with GitHub Environments:

1. **Reusable Composite Actions (`.github/actions/`)**:
   - `setup-and-test`: Sets up .NET 10, executes tests with TRX reporting, extracts test metrics, stamps CalVer (`YYYY.MM.<run>`), and produces a self-contained Linux x64 deployment zip.
   - `deploy-function`: Authenticates via Microsoft Entra ID OIDC (`azure/login@v2`), deploys the pre-built zip via `az functionapp deployment source config-zip`, and runs an automated smoke test against `GET /api/about`.
2. **Dedicated Workflows (`.github/workflows/`)**:
   - `ci-pr.yml`: Executes on pull requests to `main`. Validates tests, calculates code coverage, and posts an updated sticky comment on the PR with test results. Has zero cloud access.
   - `cd-main.yml`: Executes when code is merged to `main`. Packages the release artifact once, deploys to Development, promotes to Test, and pauses at the Production approval gate.
   - `deploy-by-sha.yml`: Manual on-demand workflow (`workflow_dispatch`) taking a target Git commit SHA and destination environment (`development`, `test`, `production`), allowing instant rollbacks or isolated testing.
3. **Octopus-Style Environment Visibility**:
   Leverages GitHub's native Deployments Dashboard (`/deployments`) providing a real-time matrix of active SHAs and versions across all three environments with full audit history.

### Consequences

* Good, because pull request verification runs in complete isolation with zero cloud credentials.
* Good, because packaging and deployment logic is defined once in composite actions, preventing configuration drift.
* Good, because production deployments require mandatory review by designated release approvers.
* Good, because operators can roll back or deploy any historical commit SHA with two clicks.
* Bad, because managing multiple workflow files requires maintaining action inputs and outputs.

### Confirmation

Compliance is confirmed via:
- GitHub Actions executing `ci-pr.yml` on pull requests with sticky comments populated.
- Merging to `main` triggering `cd-main.yml` with visual environment stages in the GitHub UI.
- Manual execution of `deploy-by-sha.yml` deploying directly to selected environments.

## Pros and Cons of the Options

### Option 1: Segregated Multi-Workflow with Composite Actions (Chosen)

* Good, because it adheres strictly to the Principle of Least Privilege.
* Good, because composite actions make workflow files concise and readable (<60 lines each).
* Good, because it enables independent triggers for PRs, main merges, and manual rollbacks.
* Bad, because composite actions have minor limitations compared to standalone Docker actions (e.g. lack of native `services:` block inside composite steps).

### Option 2: Single Monolithic Workflow

* Good, because only one YAML file is needed.
* Bad, because conditional expressions (`if: github.event_name == '...'`) become complex and error-prone.
* Bad, because PR runs request unnecessary OIDC permissions.

### Option 3: External Release Orchestrator

* Good, because specialized tools like Octopus Deploy offer rich deployment matrices.
* Bad, because it introduces extra subscription costs, SaaS maintenance, and sync latency with GitHub.

## More Information

Workflow Files:
* `.github/actions/setup-and-test/action.yml`
* `.github/actions/deploy-function/action.yml`
* `.github/workflows/ci-pr.yml`
* `.github/workflows/cd-main.yml`
* `.github/workflows/deploy-by-sha.yml`
* Documentation: `docs/deployment/github-actions-multi-environment.md` and `docs/deployment/github-deployments.md`
