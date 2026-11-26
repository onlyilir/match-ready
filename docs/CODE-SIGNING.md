# Code signing policy

**Status:** releases are **not code-signed yet**. Free signing for open-source projects has been requested from the SignPath Foundation. Until it's approved, Windows may show "Windows protected your PC": click **More info**, then **Run anyway**, and check the file against the SHA256 listed in the release notes.

When signing is approved, this line will apply: *Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).*

## How releases are made

- Releases are built **only by GitHub Actions** ([release.yml](../.github/workflows/release.yml)) from a version tag in this repository. Nothing is built or uploaded by hand.
- Every release lists the SHA256 of each file in its notes and in `SHA256SUMS.txt`. The app's **Update now** refuses any download that doesn't match that list.
- Release builds include all tests (`dotnet test`); a failing test stops the release.

## Team roles

| Role | Who |
|---|---|
| Committer and reviewer | [onlyilir](https://github.com/onlyilir) |
| Release approver (publishes the draft release) | [onlyilir](https://github.com/onlyilir) |

## Privacy

Match Ready collects no data and has no telemetry. Its only network use is optional: asking GitHub for the newest release and, if you click **Update now**, downloading it. See [SECURITY.md](../SECURITY.md).
