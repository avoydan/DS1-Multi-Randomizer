# Antivirus warnings and false-positive reports

Public DS1 Multi Randomizer releases are currently unsigned. Windows SmartScreen or an antivirus product may warn about a new, unsigned installer, a self-extracting portable executable, or a large self-contained helper. A warning alone does not prove a file is malicious, and absence of a warning does not prove a file is safe.

We do not recommend disabling protection, adding antivirus exclusions, bypassing SmartScreen, or repacking a file to evade scanning.

## Verify the file first

1. Download the release only from the project's [GitHub releases](https://github.com/avoydan/DS1-Multi-Randomizer/releases).
2. In PowerShell, compute the SHA-256 of the exact file:

   ```powershell
   Get-FileHash .\DS1-Multi-Randomizer-0.38.3-Setup-x64.exe -Algorithm SHA256
   ```

3. Compare it with the same filename in that release's `SHA256SUMS.txt`. Also compare the versioned release manifest if you need the build commit and executable inventory. The sums file covers the five staged release payload files; it does not contain its own hash.
4. A match means the file bytes match the checksum published with that release. It does not establish publisher identity. Current releases are unsigned.
5. The matching source code and release notes are linked from the same release tag in [`avoydan/DS1-Multi-Randomizer`](https://github.com/avoydan/DS1-Multi-Randomizer). The project is GPL-3.0-only. The app does not upload reports or support bundles; users choose whether to share them.

If the hash does not match, do not run or submit that file as a project build. Keep its origin and hash for investigation.

## Microsoft Defender

Use the official [Microsoft Security Intelligence file submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission) as a **software developer**. Select the incorrect malware/PUA classification, enter the exact detection name and definition version when available, identify the product/release URL and explain that the sample is the public GPL-3.0-only DS1 Multi Randomizer release. Include the release version, SHA-256, source tag, Windows version, and what action produced the alert. The portal currently limits the file to 50 MB and asks for a specific sample rather than a large installer/package. If the exact affected file is larger than the limit, contact Microsoft through its official support route; do not split, rebuild, or alter the file to fit. If using an archive, follow the portal's current archive/password instructions and include only the sample under review.

The form requests company and contact details. Use accurate project/individual information; do not invent a company or submit on another user's behalf. The owner should review all entered details before sending.

## Norton

Use Norton's official [file or URL submission portal](https://support.norton.com/sp/en/us/home/current/solutions/kb20090410134005EN). Select **False positive**, then provide the detection name, Alert ID, source URL or exact file, and a concise description. Norton's current instructions allow one file, up to 500 MB, placed in a ZIP/RAR archive if needed; the archive must not be password-protected. If the sample exceeds the limit, ask Norton support for the proper route instead of changing the sample.

## Suggested report text

> I am reporting a possible false positive for DS1 Multi Randomizer v0.38.3, a public GPL-3.0-only Windows application. The file was downloaded from https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.38.3. Filename: [exact file]. SHA-256: [hash]. Detection: [name and alert/definition ID]. The source code and release manifest are available at the matching public tag. The current release is unsigned. The program randomizes a user's own Dark Souls Remastered data after an explicit Apply action. Please review this exact sample and advise whether it is incorrectly classified.

Do not claim that a vendor has cleared the file until the vendor confirms that exact sample/detection.

## What signing changes

If a future release is signed through an accepted, verified setup, Windows can display the certificate publisher and users can verify that the signed bytes have not changed. A signature does not guarantee that SmartScreen or antivirus products will immediately stop warning, and it does not mean that a vendor has cleared the file. See [`CODE_SIGNING.md`](CODE_SIGNING.md) for the current preparation status.
