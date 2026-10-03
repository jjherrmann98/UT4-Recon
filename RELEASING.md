# Publishing UT4 Recon on GitHub

This guide keeps source control separate from downloadable release files.

## 1. Create the GitHub repository

Create an empty repository on GitHub, for example `ut4-recon`. Do not initialize
it with a README, license, or `.gitignore`; those files already exist locally.
Enable **Issues** in **Settings > General > Features**. Leave pull requests
enabled. GitHub contributors normally fork the project and open pull requests;
they do not need direct write access.

Recommended repository description:

> Experimental open-source toolkit for constrained repair of cooked Unreal Tournament 4 maps.

Suggested topics: `unreal-tournament`, `ut4`, `unreal-engine-4`, `modding`,
`cooked-assets`, `map-tools`.

## 2. Authenticate

SSH is the most convenient long-term option.

```powershell
ssh-keygen -t ed25519 -C "YOUR_GITHUB_EMAIL"
Get-Content "$HOME\.ssh\id_ed25519.pub"
```

Copy only the `.pub` output into **GitHub > Settings > SSH and GPG keys > New
SSH key**. Never upload or share `id_ed25519`. Test it with:

```powershell
ssh -T git@github.com
```

Alternatively, use HTTPS with Git Credential Manager. A fine-grained personal
access token should be limited to the new repository and needs **Contents:
Read and write** permission. Paste the token when Git asks for a password; do
not place it in a command, remote URL, file, commit, or issue.

## 3. Review and push the source repository

Before the first commit:

```powershell
git status --short --untracked-files=all
git check-ignore -v artifacts research
git diff --check
dotnet build .\Ut4Recon.sln -c Release
dotnet test .\Ut4Recon.sln -c Release
```

Confirm that no `.pak`, third-party map, Epic source file, generated workspace,
credential, or local research artifact appears in the files to be committed.
Then commit and push:

```powershell
git add .
git status --short
git commit -m "Release UT4 Recon 0.1.0 alpha 1"
git branch -M main
git remote add origin git@github.com:YOUR_ACCOUNT/ut4-recon.git
git push -u origin main
```

For HTTPS, use
`https://github.com/YOUR_ACCOUNT/ut4-recon.git` as the remote instead.

## 4. Build and tag the release

Build from the clean tagged source with the supported editor and matching source
tree:

```powershell
.\release\Build-Prototype.ps1 `
  -EditorRoot "E:\path\to\UnrealTournamentEditor" `
  -SourceRoot "E:\path\to\UnrealTournament-source"
```

Run the documented smoke test, then create and push the tag:

```powershell
git tag -a v0.1.0-alpha.1 -m "UT4 Recon 0.1.0 alpha 1"
git push origin v0.1.0-alpha.1
```

On GitHub, open **Releases > Draft a new release**, select the tag, mark it as a
**pre-release**, and attach:

- `ut4recon-0.1.0-alpha.1-win-x64.zip`;
- a small text file containing the archive's SHA-256, or include the checksum in
  the release notes.

Do not commit the zip to the repository. The zip contains the self-contained
CLI, editor plugin binary, schemas, certified project-authored templates,
license, notices, and documentation.

Build the setup executable from that exact portable payload with Inno Setup 6:

```powershell
winget install --id JRSoftware.InnoSetup --exact
.\release\Build-Installer.ps1 `
  -PayloadRoot ".\artifacts\release\ut4recon-0.1.0-alpha.1-win-x64"
.\release\Test-Installer.ps1 `
  -Installer ".\artifacts\release\UT4Recon-Setup-0.1.0-alpha.1.exe" `
  -PayloadRoot ".\artifacts\release\ut4recon-0.1.0-alpha.1-win-x64"
```

The smoke test installs into a temporary synthetic editor tree, verifies every
portable payload file and the native plugin, launches the installed CLI,
confirms that a mismatched editor API is rejected, verifies the working-folder
layout, and verifies uninstall cleanup without deleting user data. Attach the
tested setup executable alongside the portable ZIP and add its SHA-256 to the
release notes.

## 5. Suggested release notes

```text
First public alpha of UT4 Recon.

Tested with UT4 Editor 4.15.0 CL 3525360 / API CL 3525109 and the UT4ever
Installer 1.1.0 client with UT4UU 10.1.6 and NetcodePlus 2.0.

Confirmed in the focused playtest:
- static-mesh transform changes
- blocking-volume duplication and deletion
- player-start movement
- distinct map title/package/URL
- validated pak generation and client loading

This is an experimental pre-release. Keep the original pak and playtest every
output. Read the README for the reconstruction model and limitations.
```

## 6. Contributions

Keep branch protection lightweight at first. Once the initial push succeeds,
enable a branch rule for `main` that requires pull requests and the managed
build workflow to pass. Avoid requiring approval from a maintainer if you want
other trusted contributors to merge while you are unavailable; grant repository
roles deliberately through **Settings > Collaborators**.

Issue templates and a pull-request template are included. Labels such as
`bug`, `compatibility`, `documentation`, `enhancement`, `good first issue`, and
`needs reproduction` will help organize reports.
