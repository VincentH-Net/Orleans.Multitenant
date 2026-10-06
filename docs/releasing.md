# Releasing Orleans.Multitenant

The steps for a release, in order. The package that is uploaded is the package that was tested: the pull request is merged with a fast-forward, so that `main` points at the exact commit that was built and tested. The examples use version 5.0.1 and branch `v5.0.1-json-stream-events`.

## 1. Prepare the release in a pull request

- **Version**: set `PackageVersion` in `src/Orleans.Multitenant/Orleans.Multitenant.csproj`. The release tag (`5-0-1`) and the release notes link in the package readme are derived from it; do not write the tag anywhere else
- **Readme**: update `README.md` for the changes, including the requirements and the upgrade section (breaking changes, new, fixed)
- **Tests**: all tests pass and the build has no warnings:

  ```bash
  dotnet test src/Tests/Orleans.Multitenant.Tests.csproj
  ```

- **Example**: set the `Orleans.Multitenant` version in `src/Example/Apis/Apis.csproj` and `src/Example/Services.Tenant/Services.Tenant.csproj`, and check that the example runs. Until the package is published, the example restores it from the `multitenant-local` feed in `src/NuGet.config`, which is the folder that a Release build of the library puts the package in

## 2. Build and verify the package on the branch

Do this on the branch, when the pull request is reviewed and ready to merge.

1. Check that the branch contains everything on `main`; this command must print nothing:

   ```bash
   git fetch origin && git log --oneline HEAD..origin/main
   ```

   If it prints commits, merge `origin/main` into the branch, push, and start step 2 again.

2. Build the package. This creates `Orleans.Multitenant.5.0.1.nupkg` and `Orleans.Multitenant.5.0.1.snupkg` in `src/Orleans.Multitenant/bin/Release`:

   ```bash
   dotnet build src/Orleans.Multitenant/Orleans.Multitenant.csproj -c Release
   ```

3. Run the tests on the same build:

   ```bash
   dotnet test src/Tests/Orleans.Multitenant.Tests.csproj -c Release
   ```

4. Check that the library that the tests used is the library in the package; these two commands must print the same hash. Keep the hash for step 3:

   ```bash
   shasum -a 256 src/Tests/bin/Release/net10.0/Orleans.Multitenant.dll
   ```

   ```bash
   unzip -p src/Orleans.Multitenant/bin/Release/Orleans.Multitenant.5.0.1.nupkg lib/net10.0/Orleans.Multitenant.dll | shasum -a 256
   ```

## 3. Merge with a fast-forward and build the package to upload

1. Merge locally with a fast-forward, so that `main` points at the tested commit. Do not use the merge button of the pull request: each of its options creates a new commit, so the released build would differ from the tested build:

   ```bash
   git switch main && git merge --ff-only origin/main && git merge --ff-only v5.0.1-json-stream-events && git push origin main
   ```

   GitHub marks the pull request as merged. Delete the branch:

   ```bash
   git push origin --delete v5.0.1-json-stream-events && git branch -d v5.0.1-json-stream-events
   ```

2. Build the package on `main`, so that the package records `main` as the branch it was built from:

   ```bash
   dotnet build src/Orleans.Multitenant/Orleans.Multitenant.csproj -c Release
   ```

3. Check that this is the tested library. The hash must equal the hash from step 2.4, and the commit in the package must be `git rev-parse HEAD`:

   ```bash
   unzip -p src/Orleans.Multitenant/bin/Release/Orleans.Multitenant.5.0.1.nupkg lib/net10.0/Orleans.Multitenant.dll | shasum -a 256
   ```

   ```bash
   unzip -p src/Orleans.Multitenant/bin/Release/Orleans.Multitenant.5.0.1.nupkg Orleans.Multitenant.nuspec | grep -o '<repository[^>]*>'
   ```

   The hashes are equal because the build is deterministic and the commit is the same; only the branch name in the package metadata changes. A different hash means that the checkout or the build environment differs from what was tested: stop and find out why.

## 4. Publish

1. **GitHub release**: create a release with tag `5-0-1` on `main` and title `5.0.1`. Write the release notes for users of the library: the breaking changes and the areas they are limited to, what is new, what is fixed, and then the list of commits since the previous release. The package readme links to this release, so publish it together with the package
2. **NuGet**: upload the `.nupkg` from step 3 to [nuget.org](https://www.nuget.org/packages/Orleans.Multitenant), and the `.snupkg` (symbols) next to it
3. **Local package cache**: when the package is listed on nuget.org, delete `~/.nuget/packages/orleans.multitenant/5.0.1`, so that this machine uses the published package instead of a local build

## 5. Update the agent skill

The `orleans-multitenant` skill in [.NET Agentic Engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering) (`plugins/orleans/skills/orleans-multitenant`) lets agents use this library. It repeats parts of `README.md`, so update it for each release:

| Readme section | Skill file |
|---|---|
| Requirements, Installation, Scope and limitations | `SKILL.md` |
| Add multitenant communication separation, Access tenant grains and streams, Subscribe to tenant streams, Grain/stream key and tenant id | `SKILL.md` |
| Add multitenant storage | `SKILL.md` (Azure Table Storage) and `references/storage-providers.md` |
| Add multitenant streams, Stream filters; start position and batches in Subscribe to tenant streams | `SKILL.md` and `references/stream-filters-and-start-position.md` |
| The null tenant, Tenant unaware streams | `references/null-tenant-and-tenant-unaware-streams.md` |
| Upgrade from 4.x to 5.0 | `references/upgrade-4x-to-5x.md` |

Also:

- Set `library-version` in the front matter of `SKILL.md` to the released version, and raise the `version` of the skill and of the `orleans` plugin
- Add new or changed exceptions and log messages to the troubleshooting table in `SKILL.md`
- For a new major version, add a reference file for the upgrade and link it from `SKILL.md`
- Check that the code samples in the skill compile against the released package
