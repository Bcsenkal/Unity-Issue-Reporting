# Issue Reporting for Unity

One report contains a description and one bounded file snapshot. The package owns the form, upload, status and cancellation. The host game owns file logging and supplies a stable snapshot through `IReportAttachmentSource`.

## Add to a game

Install this repository through Unity Package Manager using **Add package from git URL**:

```
https://github.com/Bcsenkal/Unity-Issue-Reporting.git#v0.1.2
```

The repository is public, so Unity can fetch the package without repository access. Pin a tag or commit instead of a moving branch. Unity stores the resolved package in its own cache; keep your editable repository checkout separately from each game project.

Place `Runtime/Prefabs/ReportWindow.prefab` under an active Canvas, then reference its `ReportWindow` component from the host UI owner. The prefab uses UGUI and TextMeshPro; configure a default TMP font in the host project.

Implement `IReportAttachmentSource.TryCreate`. The source must flush or snapshot its logger, reject files above `maxSourceBytes`, prepare a ready-to-upload byte array no larger than `maxUploadBytes`, and return a safe filename, content type, and optional context. The package verifies the returned upload size again. Supported relay types are gzip (`.gz` or `.log.gz`), plain text (`.log` or `.txt`), JSON (`.json`), and ZIP (`.zip`). The host decides which file is appropriate to share.

Configure the window from the host's lifecycle owner:

```csharp
reportWindow.Configure(
    () => new ReportConfiguration(endpoint, "MyGame.ReportCode", timeoutSeconds,
        maxSourceBytes, maxUploadBytes),
    attachmentSource);
```

Call `reportWindow.Open()` from a visible button. Call `reportWindow.Close()` on navigation away and `reportWindow.Cleanup()` when the host owner is disabled or reset. The code is stored in PlayerPrefs under the supplied key. The upload code and Slack credentials must be configured separately in the relay; no Slack credential belongs in the game.

The host implements its own file snapshot adapter and keeps its logging system independent of the package. It can open the package window from any button or menu.

## Relay

`Relay~/` contains a Cloudflare Worker and tests. Deploy one configured Worker per project. See `Relay~/README.md`. The `/reports` endpoint accepts the four file types above. `/logs` and the legacy log headers remain available for older builds. Keep deployment secrets in Cloudflare and out of both the Unity package and Git.
