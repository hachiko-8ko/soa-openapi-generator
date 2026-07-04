# Release Workflow Plan

The goal is to implement a GitHub Action that automates the publishing of production releases for the `soa-openapi-generator` project.

## Approach

I will create a new GitHub Actions workflow file `.github/workflows/release.yml` that triggers whenever a new release is published on GitHub. The workflow will build the .NET 10.0 console application in Release mode, package the output, and upload it as a release asset.

## Implementation Steps

1. **Create Workflow Directory**: Create the `.github/workflows` directory if it doesn't exist.
2. **Define the Workflow**:
    - **Trigger**: `on: release: types: [published]`.
    - **Permissions**: Ensure the `GITHUB_TOKEN` has `contents: write` permission to upload assets to the release.
    - **Job Steps**:
        - **Checkout**: Use `actions/checkout@v4` to pull the code.
        - **Setup .NET**: Use `actions/setup-dotnet@v4` to install .NET 10.0.
        - **Publish**: Run `dotnet publish -c Release -o ./publish` to generate the production binaries.
        - **Archive**: Zip the `./publish` directory into a file named `soa-openapi-generator-win-x64.zip` (or a generic name).
        - **Upload**: Use `softprops/action-gh-release@v2` to upload the zip file to the current GitHub release.

## Considerations

- **Runtime**: Since the project targets `net10.0`, the workflow will ensure the correct SDK is installed.
- **Artifacts**: I will package the entire output of `dotnet publish` to ensure `appsettings.json` and all dependencies are included.
- **Cross-Platform**: By default, `dotnet publish` without a RID produces a framework-dependent deployment. I will use this for maximum compatibility, assuming the user has the .NET runtime installed, or I can specify a RID (like `win-x64`) for a self-contained executable if preferred. I'll stick to framework-dependent for now as it's the standard .NET behavior unless specified otherwise.
