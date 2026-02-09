# macOS Code Signing & Notarization Setup

This guide explains how to configure the GitHub repository secrets required to code sign and notarize the macOS build of BookLibConnect.

## Prerequisites

- An [Apple Developer Program](https://developer.apple.com/programs/) membership ($99/year)
- A Mac with Xcode command line tools installed
- Access to the GitHub repository **Settings → Secrets and variables → Actions**

## Step 1: Create a Developer ID Application Certificate

If you don't already have one:

1. Open **Xcode → Settings → Accounts** and sign in with your Apple ID.
2. Select your team, click **Manage Certificates…**
3. Click **+** and choose **Developer ID Application**.
4. Xcode will create the certificate and install it in your Keychain.

Alternatively, create it via the [Apple Developer portal](https://developer.apple.com/account/resources/certificates/list).

## Step 2: Export the Certificate as .p12

1. Open **Keychain Access** on your Mac.
2. In the **login** keychain, find your **Developer ID Application** certificate (under **My Certificates**).
3. Right-click the certificate (not the private key) → **Export…**
4. Choose **Personal Information Exchange (.p12)** format.
5. Set a strong password when prompted — you'll need this for `MACOS_CERTIFICATE_PASSWORD`.
6. Save the file (e.g., `DeveloperID.p12`).

## Step 3: Base64-Encode the Certificate

Run this command in Terminal to copy the base64-encoded certificate to your clipboard:

```bash
base64 -i DeveloperID.p12 | pbcopy
```

The clipboard now contains the value for the `MACOS_CERTIFICATE_P12` secret.

## Step 4: Find Your Team ID

1. Go to [Apple Developer → Membership Details](https://developer.apple.com/account#MembershipDetailsCard).
2. Copy your **Team ID** (a 10-character alphanumeric string, e.g., `A1B2C3D4E5`).

## Step 5: Create an App-Specific Password

Apple requires an app-specific password for notarization (regular passwords and 2FA codes won't work):

1. Go to [appleid.apple.com](https://appleid.apple.com/account/manage).
2. Sign in with the Apple ID you use for your developer account.
3. Navigate to **Sign-In and Security → App-Specific Passwords**.
4. Click **Generate an app-specific password**.
5. Name it something like `GitHub Notarization`.
6. Copy the generated password (format: `xxxx-xxxx-xxxx-xxxx`).

## Step 6: Add GitHub Repository Secrets

Go to your GitHub repository → **Settings → Secrets and variables → Actions → New repository secret** and create each of the following:

| Secret Name | Value | Example |
|---|---|---|
| `MACOS_CERTIFICATE_P12` | Base64-encoded `.p12` from Step 3 | *(long base64 string)* |
| `MACOS_CERTIFICATE_PASSWORD` | Password from Step 2 | `MyP12Password!` |
| `APPLE_TEAM_ID` | Team ID from Step 4 | `A1B2C3D4E5` |
| `APPLE_ID` | Your Apple ID email | `you@example.com` |
| `APPLE_ID_PASSWORD` | App-specific password from Step 5 | `abcd-efgh-ijkl-mnop` |

## How It Works

When the GitHub Actions workflow runs:

1. **Certificate import** — The `.p12` is decoded and imported into a temporary macOS Keychain on the runner.
2. **Code signing** — Every binary, dylib, and the `.app` bundle are signed with Hardened Runtime and a secure timestamp using the Developer ID Application identity.
3. **DMG signing** — The `.dmg` disk image is also signed.
4. **Notarization** — The signed `.dmg` is submitted to Apple's notary service via `notarytool`, which scans it for malware and validates the signature.
5. **Stapling** — Once approved, the notarization ticket is stapled to the `.dmg` so users can verify it offline.
6. **Cleanup** — The temporary Keychain is deleted.

## Graceful Degradation

If the secrets are **not configured**, the workflow still runs successfully — it simply produces an unsigned, un-notarized DMG. This is useful for:

- Pull requests from forks (which can't access secrets)
- Testing the build pipeline before obtaining a certificate

## Troubleshooting

### "No signing identity found"
- Verify `MACOS_CERTIFICATE_P12` is correctly base64-encoded (no line breaks or extra whitespace).
- Ensure the certificate is a **Developer ID Application** type, not a development or distribution certificate.

### Notarization rejected
- Check the notarization log: `xcrun notarytool log <submission-id> --apple-id ... --password ... --team-id ...`
- Common issues: unsigned nested binaries, missing Hardened Runtime, missing secure timestamp.
- The entitlements in `build/entitlements.plist` should cover .NET runtime requirements.

### "App is damaged and can't be opened"
- This usually means the app wasn't signed or notarized correctly.
- Verify with: `codesign --verify --deep --strict "Book Lib Connect.app"`
- Check stapling: `stapler validate BookLibConnect-*.dmg`

## Local Signing (Optional)

You can also sign locally by passing the flags to the build script:

```bash
./build/build-macos.sh \
  --codesign-identity "Developer ID Application: Your Name (TEAMID)" \
  --notarize \
  --apple-id "you@example.com" \
  --apple-id-password "xxxx-xxxx-xxxx-xxxx" \
  --apple-team-id "A1B2C3D4E5"
```

To find your signing identity name:

```bash
security find-identity -v -p codesigning
```
