---
status: "proposed"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 9. Central Package Management (CPM) and Solution-Wide Build Properties

## Context and Problem Statement

As multi-project .NET solutions expand, managing NuGet package versions across multiple `.csproj` files individually 
causes version drift, transitive dependency diamond conflicts, inconsistent security patch levels, and noisy pull requests 
when upgrading libraries.

Furthermore, build configurations such as the target framework (`net10.0`), C# language version, nullable reference type 
checking, and implicit usings can easily diverge between projects if declared separately in each project file.

How should NuGet package versions and common MSBuild compiler properties be managed across the solution?

## Decision Drivers

* **Single Source of Truth:** Centralised control over all NuGet dependency versions in a single repository file.
* **Deterministic Builds:** Guarantee that every project references identical package versions and common compiler 
  settings (`net10.0`, `Nullable=enable`, `ImplicitUsings=enable`).
* **Security & Vulnerability Management:** Ability to update or pin vulnerable transitive dependencies (such as cryptographic 
  packages) across the entire solution in a single commit.
* **Clean Project Files:** Individual project `.csproj` files declare only `<PackageReference Include="PackageName" />` 
  without repetitive `<TargetFramework>` or `<Version>` attributes.

## Considered Options

* **Option 1: NuGet Central Package Management (CPM) via `Directory.Packages.props` and `Directory.Build.props`**
* **Option 2: Individual Project-Level `<PackageReference Version="..." />`**
* **Option 3: Custom Shared MSBuild `.targets` File**

## Decision Outcome

Chosen option: **"Option 1: NuGet Central Package Management (CPM)"**, because it is the native, official mechanism 
supported directly by the .NET SDK, NuGet CLI, and modern IDEs (Rider, Visual Studio):

1. **Centralised Version Control (`Directory.Packages.props`)**:
   - `ManagePackageVersionsCentrally` is set to `true`.
   - All package versions are pinned in this single file.
   - Individual `.csproj` files reference dependencies purely by package name without specifying a `Version` attribute:
     ```xml
     <PackageReference Include="Azure.Storage.Blobs" />
     ```
2. **Transitive Dependency Pinning**:
   `CentralPackageTransitivePinningEnabled` is set to `true`. This allows security patches on indirect/transitive 
   dependencies (such as cryptographic or transport libraries) to be pinned directly at the solution root without waiting 
   for upstream package updates.
3. **Common Compiler Properties (`Directory.Build.props`)**:
   Shared MSBuild properties are declared once in the repository root:
   ```xml
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
     <Nullable>enable</Nullable>
     <ImplicitUsings>enable</ImplicitUsings>
   </PropertyGroup>
   ```

### Consequences

* Good, because package version divergence between projects is completely prevented.
* Good, because updating a package version across the entire solution requires editing only one line in `Directory.Packages.props`.
* Good, because transitive vulnerability alerts can be patched immediately by pinning the transitive package centrally.
* Good, because individual project files remain clean, concise, and easy to review.
* Bad, because developers must be aware of centralized package versioning when adding new packages.

### Confirmation

Compliance is confirmed via:
- `ManagePackageVersionsCentrally` enabled in `Directory.Packages.props`.
- `dotnet build` failing if any `.csproj` specifies an explicit `Version` attribute on a `PackageReference`.
- Automated CI builds verifying that all projects restore from the central package manifest.

## Pros and Cons of the Options

### Option 1: NuGet Central Package Management (CPM) (Chosen)

* Good, because it is the standardized .NET SDK approach.
* Good, because version bumps require touching only one file.
* Good, because it prevents accidental package mismatches between projects.
* Bad, because adding a package requires editing both the `.csproj` and `Directory.Packages.props`.

### Option 2: Individual Project-Level Versions

* Good, because it is familiar in legacy .NET codebases.
* Bad, because projects easily drift out of sync on different versions of core dependencies (e.g. Azure.Identity, 
  System.Text.Json).
* Bad, because updating a shared package requires modifying every `.csproj` file in the repository.

### Option 3: Custom Shared MSBuild .targets File

* Good, because it works on older .NET versions.
* Bad, because it lacks IDE-native tooling support compared to standard CPM.

## More Information

Repository Configuration:
* `Directory.Build.props`: Centralises target framework and compiler rules.
* `Directory.Packages.props`: Governs all dependency versions and transitive pinning.
