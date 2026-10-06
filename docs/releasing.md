# Releasing Orleans.Multitenant

The steps for a release, in order. The package is built and tested on the pull request branch before the merge, and that same package file is uploaded: the pull request is merged with a fast-forward, so that `main` points at the exact commit the package was built from.

In the commands below, `<version>` is the version that is released (e.g. `5.0.1`), `<tag>` is that version with hyphens instead of dots (e.g. `5-0-1`), and `<branch>` is the branch of the pull request.

## 1. Prepare the release in a pull request

- **Version**: set `VersionPrefix` in `src/Orleans.Multitenant/Orleans.Multitenant.csproj` (and `VersionSuffix` for a prerelease), following [semantic versioning](https://semver.org): a major version for breaking changes, a minor version for new functionality that does not break existing functionality, a patch version for fixes that do not change functionality. The package version, the file version, the assembly version (major version only), the release tag and the release notes link in the package readme are derived from it; do not write any of these anywhere else
- **Readme**: update `README.md` for the changes, including the requirements and the upgrade section (breaking changes, new, fixed)
- **Tests**: all tests pass and the build has no warnings:

  ```bash
  dotnet test src/Tests/Orleans.Multitenant.Tests.csproj
  ```

- **Example**: set the `Orleans.Multitenant` version in `src/Example/Apis/Apis.csproj` and `src/Example/Services.Tenant/Services.Tenant.csproj` to `<version>`, and check that the example runs. Until the package is published, the example restores it from the `multitenant-local` feed in `src/NuGet.config`, which is the folder that a Release build of the library puts the package in

## 2. Build and verify the package on the branch

Do this on the branch, when the pull request is reviewed and ready to merge. The package from this step is the package that is uploaded in step 4. If anything fails, fix it on the branch and start step 2 again.

1. Check that the branch contains everything on `main`; this command must print nothing:

   ```bash
   git fetch origin && git log --oneline HEAD..origin/main
   ```

   If it prints commits, merge `origin/main` into the branch, push, and start step 2 again.

2. Build the package. This creates `Orleans.Multitenant.<version>.nupkg` and `Orleans.Multitenant.<version>.snupkg` in `src/Orleans.Multitenant/bin/Release`:

   ```bash
   dotnet build src/Orleans.Multitenant/Orleans.Multitenant.csproj -c Release
   ```

3. Run the tests on the same build:

   ```bash
   dotnet test src/Tests/Orleans.Multitenant.Tests.csproj -c Release
   ```

4. Check that the library that the tests used is the library in the package; these two commands must print the same hash:

   ```bash
   shasum -a 256 src/Tests/bin/Release/net10.0/Orleans.Multitenant.dll
   ```

   ```bash
   unzip -p src/Orleans.Multitenant/bin/Release/Orleans.Multitenant.<version>.nupkg lib/net10.0/Orleans.Multitenant.dll | shasum -a 256
   ```

## 3. Merge with a fast-forward

1. Merge locally with a fast-forward, so that `main` points at the commit that the package was built from. Do not use the merge button of the pull request: each of its options creates a new commit, which the package was not built from:

   ```bash
   git switch main && git merge --ff-only origin/main && git merge --ff-only <branch> && git push origin main
   ```

   GitHub marks the pull request as merged. Delete the branch:

   ```bash
   git push origin --delete <branch> && git branch -d <branch>
   ```

2. Check that the package was built from the commit that `main` now points at; the commit in the package metadata must equal `git rev-parse HEAD`:

   ```bash
   unzip -p src/Orleans.Multitenant/bin/Release/Orleans.Multitenant.<version>.nupkg Orleans.Multitenant.nuspec | grep -o '<repository[^>]*>'
   ```

   Do not build again: a build records the commit it was built from, so a rebuild on `main` gives a different file for the same source. The package metadata names the branch it was built on; tools do not use that, Source Link works from the commit.

## 4. Publish

1. **GitHub release**: create a release with tag `<tag>` on `main` and title `<version>`. Write the release notes for users of the library: the breaking changes and the areas they are limited to, what is new, what is fixed, and then the list of commits since the previous release. The package readme links to this release, so publish it together with the package
2. **NuGet**: upload the `.nupkg` that was built and tested in step 2 to [nuget.org](https://www.nuget.org/packages/Orleans.Multitenant), and the `.snupkg` (symbols) next to it
3. **Local package cache**: when the package is listed on nuget.org, delete `~/.nuget/packages/orleans.multitenant/<version>`, so that this machine uses the published package instead of a local build

## 5. Update the agent skill when the release changes how agents use the library

The `orleans-multitenant` skill in [.NET Agentic Engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering) (`plugins/orleans/skills/orleans-multitenant`) tells agents how to use this library. Its `library-version` is the minimum library version that its instructions apply to. Change the skill only when the release changes what the instructions should say; a skill update that changes nothing for agents only costs them a re-install. What to do depends on the kind of release:

- **Patch**: normally nothing. Check the troubleshooting table in `SKILL.md`: when the release fixes a symptom that is listed there, or the skill names a version that a fix requires, update that text and set `library-version` to the patch version
- **Minor**: existing instructions stay valid. Add to the skill only what agents need to use the new functionality, such as a new API, a new rule or a new symptom, and then set `library-version` to the minor version, because the skill now describes functionality that older versions do not have. When agents do not need the new functionality, leave the skill unchanged
- **Major**: breaking changes change the instructions. Review the whole skill against the readme with the table below, update the rules, code samples and troubleshooting table, add a `references/upgrade-<previous major>x-to-<new major>x.md` file for the upgrade and link it from `SKILL.md`, and set `library-version` to the major version

| Readme section | Skill file |
|---|---|
| Requirements, Installation, Scope and limitations | `SKILL.md` |
| Add multitenant communication separation, Access tenant grains and streams, Subscribe to tenant streams, Grain/stream key and tenant id | `SKILL.md` |
| Add multitenant storage | `SKILL.md` (Azure Table Storage) and `references/storage-providers.md` |
| Add multitenant streams, Stream filters; start position and batches in Subscribe to tenant streams | `SKILL.md` and `references/stream-filters-and-start-position.md` |
| The null tenant, Tenant unaware streams | `references/null-tenant-and-tenant-unaware-streams.md` |
| Upgrade sections | `references/upgrade-*.md` |

When the skill changes:

- Raise the `version` of the skill and of the `orleans` plugin
- Check that the code samples in the skill compile against the released package
- Release the skill as described in `docs/releasing.md` of that repository
