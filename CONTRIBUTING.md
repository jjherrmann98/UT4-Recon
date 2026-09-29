# Contributing to UT4 Recon

Thank you for considering a contribution. This is an experimental fan project
with limited maintainer availability, so focused changes with clear evidence are
the easiest to review.

## Before opening an issue

Search existing issues first. For a bug, include:

- the UT4 Recon version;
- the editor version, changelist, and compatible changelist;
- the client and community-plugin versions used for the playtest;
- the command or panel action that failed;
- the complete error text and a small, redacted report or log where useful;
- the object's displayed fidelity class and supported operations;
- what you expected and what happened.

Do not attach map paks, extracted game assets, Unreal Tournament or Unreal
Engine source, access tokens, SSH private keys, machine-specific recovery
projects, or content you do not have permission to distribute. If reproducing a
bug requires a map, first describe it without uploading the file.

Issues are welcome as a public record, but there is no guaranteed response time
and no promise that the maintainer will personally implement a request.

## Pull requests

1. Fork the repository and create a focused branch.
2. Keep generated files, local editor paths, paks, Epic content, and private
   research fixtures out of the commit.
3. Run `dotnet build .\Ut4Recon.sln -c Release` and
   `dotnet test .\Ut4Recon.sln -c Release`.
4. Add tests for package-writing or validation behavior when a public fixture is
   sufficient. Do not weaken validation merely to accept a new map.
5. Explain the user-visible change, the cooked-package invariants involved, and
   how the result was checked.

Native panel changes should be tested in the supported UT4 editor build. A pull
request does not need to include a compiled DLL.

By submitting a contribution, you represent that you have the right to submit
it and agree that it may be distributed under the repository's
[MIT License](LICENSE). Dependencies and material under separate licenses
retain those terms.

## Design rules

- The cooked baseline remains authoritative for unchanged data.
- The editor must clearly distinguish exact editable content, constrained
  markers, reconstructed context, and preserved-only evidence.
- An operation is not supported until a writer and a validator both understand
  it.
- Unexplained package changes fail the build.
- Generated maps receive a distinct title, package name, and URL.
- Runtime loading is useful evidence, but gameplay claims require a playtest.
