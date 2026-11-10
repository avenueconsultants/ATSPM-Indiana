# Decode existing device-event files through the logger workflow

Read Indiana's copied logs using existing devices, decoder profiles, and archive/signal-timing workflows.

## Source path and client folders

```json
{
  "DecodeEventsConfiguration": {
    "Path": "/mnt/atspm"
  }
}
```

The environment setting is `DecodeEventsConfiguration__Path=/mnt/atspm`. An explicit `--path`/`-p` overrides configuration.

Importing requires location/device folders:

```text
/mnt/atspm/
  <LocationIdentifier>/
    <DeviceIdentifier>/
      <file>.datZ
```

The device folder can be omitted when the location has exactly one configured device. Folder names must match existing identifiers exactly; filenames can stay unchanged.

The address-based flat-file fallback has been removed. Files directly under the source root are skipped with a warning.

## Processing

- Reuse devices from the latest location versions without IP, logging-enabled, or device-status filters. Preserve the shared live-logging lookup and existing decoder behavior.
- Process files sequentially per location. Broadcast decoded events to the existing archive and signal-timing workflows; each branch keeps its batching, and completion awaits both branches. Updating signal timing plans is a required part of importing.
- Honor the existing importer `DeleteSource` setting. Keep dependency scopes alive through importer enumeration.
- Skip inaccessible directories such as the share's protected `#recycle` folder. Warn on unknown or ambiguous device folders.

## Verified

- Compilation, self-contained Linux publishing, VM startup, configured path, and CLI override passed.
- All three folder-only mapping tests passed: root files skipped even with matching addresses, exact identifiers with no addresses, and explicit device folders for locations with multiple devices.
- On the VM, a temporary copy of an actual share `.datZ` file produced 384 ASC events with each `DeleteSource` setting. Device/location attribution and scope lifetime were preserved; the share original was retained.
- Full PostgreSQL import passed on November 1, 2026: four samples produced 5,692 events in four hourly archives and one timing-plan record. Every archived event and the timing-plan start/number matched independently decoded input. Both commands exited successfully and both workflow branches completed, including final partial batches.

## VM test

SSH uses `jbhatia@10.4.4.50`; PostgreSQL uses `atspmsu`. Tests used existing ASC3 device `01-002-010-sig` at location `01-002-010`, with `DeleteSource=false`.

- `decode-events` used configured path `/mnt/atspm`: three staged files decoded 3,767 events into three archives. It skipped 1,035,500 root files and the inaccessible `#recycle` directory.
- Those files contained no pattern-change events. An additional copy containing event 131 was tested under an isolated location folder using `--path`; it produced 1,925 events, one archive, and one timing-plan record (plan 254, start `2026-10-25 00:00:00`). Existing plan defaults were preserved: `Valid=false`, open end stored as PostgreSQL `-infinity`.

The arbitrary samples verified the workflow, not their real controller-to-location association. Cleanup completed on November 2, 2026: the four test archives, one timing-plan record, three NAS sample copies, empty test folders, and temporary VM workspace (including saved test credentials) were removed. All four root originals were hash-checked and preserved. Products, decoder profiles, and all 2,405 device records were verified unchanged by cleanup.

Backups, results, logs, and `cleanup-result.json` are retained in the protected directory `/home/jbhatia/decode-event-logger-records-20261102T162657Z/`. This includes backups of the deleted test records. No additional application code changes were needed for the test.

## Product and decoder setup

Applied the original links from the `atspm-all-locations-csv` import package. Its five existing products were reused, five decoder profiles were created, and all 2,405 devices were assigned to their source product/profile. The decoder choices follow the existing installer rules.

| Product model | Decoder | Devices |
| --- | --- | ---: |
| maxtime | MaxtimeToIndianaDecoder | 270 |
| asc3/asc-1000 | AscToIndianaDecoder | 316 |
| asc3/cobalt-1000 | AscToIndianaDecoder | 1,733 |
| asc3/cobalt-2100 | AscToIndianaDecoder | 76 |
| asc3/asc-2100 | AscToIndianaDecoder | 10 |

`LoggingOffset=0` was agreed for these file-only profiles. Product records and all existing device fields except `DeviceConfigurationId` were verified unchanged. All devices have a profile; a repeat preview reports zero pending changes. No application code changes were needed for this setup.

The pre-import backup and profile verification reports are in `device-profiles-20261102T051033Z/` within the retained directory above.

## Backend deployment

On November 9, 2026, the share was verified mounted and readable on backend VM `10.4.4.51`. It contained 432,860 `.datZ` files in 1,680 Commission ID folders, with no root files. Of those folders, 1,679 matched live ATSPM locations. Folder `05-027-001` has no matching location and is intentionally ignored at the user's request; the existing workflow skips it. Empty folders are valid.

On November 9, 2026, image `atspm/indiana-eventlogutil:decode-events.20261109.1` was deployed on `.51`. Its imported identity matched the build (`sha256:36d8cb21b34a39564105e765df6ada9b5495e144d9b32fbba4afcc993bdd7a92`). The scoped config follows the offline installer's `configurationFiles.EventlogConfiguration.json` format and sets `DecodeEventsConfiguration.Path=/mnt/atspm` and `EventLogImporterConfiguration.EventLogFileImporter.DeleteSource=true` in both the live Secret and saved installation configuration.

The existing `eventlogutility` CronJob now runs `decode-events` every 15 minutes, with the NAS directory available through a persistent-volume claim. Both copied-sample container tests passed at real location `01-002-119`: 1,901 events, two hourly archives, and two timing-plan records matched decoded inputs. `DeleteSource=false` preserved the copies; `DeleteSource=true` removed them. NAS originals were preserved during testing. Scheduled production ingestion is now enabled with deletion active; its first job was observed running as UID 1654.

Protected deployment backups, results, and test logs are in `/opt/atspm/release/indiana-eventlogutility-20261109.1-20261109T222910Z/`. The updater also saves `/etc/atspm/eventlogutility-workload.json`; after a full `atspmctl 1.0.3 reconfigure`, reapply that manifest because the older installer still generates the legacy logger command. Deployment files and instructions are in `C:\Users\jbhatia\Documents\atspm-kubernetes-offline`: `config/indiana-eventlogutility.json`, `scripts/Deploy-IndianaEventLogUtility.py`, and `docs/INDIANA_EVENTLOG_UPDATE.md`.

The initial production job was observed processing inputs: the file count fell from 432,860 to 427,593 while new files continued arriving. Its inherited one-hour deadline was extended to 24 hours for the active job, future jobs, and saved workload manifest so the initial backlog can finish. The 15-minute schedule and `Forbid` overlap policy were preserved. Verification found the production pod running with zero restarts. Deadline-change backups are in `/opt/atspm/release/indiana-eventlog-deadline-20261109T224023Z/`.

Production persistence was verified with read-only SQL on `.50`: 113,259 hourly archives (2,560,828,576 compressed bytes) and 13,131 signal timing plans across 865 locations/devices, excluding `05-027-001`. Consecutive scheduled jobs `eventlogutility-29904690` and `eventlogutility-29904705` completed with zero restarts. The initial bulk backlog has cleared; a scan between scheduled runs found 872 files awaiting ingestion. The implementation, deployment, and production verification are complete.

VM cleanup removed the complete home staging directories on both VMs, including `.51`'s remaining verifier and records after preserving a hash-verified workstation copy. K3s recovery images, protected configuration/backups, and ingestion inputs were preserved. On `.50`, compact database backups and SQL records remain at `~/atspm-import-records-20260929`. Full staging copies and cleanup receipts are under the offline project's `build/vm-cleanup-20261109/` on the workstation. Future direct read-only SQL checks use the workstation's `scripts/Check-IndianaDatabaseTables.ps1` and require no VM staging folder.

## Detection and measure configuration

On November 9, 2026, `ATSPM-Config.public."DetectionTypeMeasureType"` was populated with the 23 associations from UDOT's supplied `DetectionTypeMeasureType.csv`, covering six detection types and 19 measure types. The committed SQL report and live Config API both matched the supplied mapping. All 13 detection-type and 38 measure-type definitions were preserved.

The offline project retains `config/indiana-detection-type-measures.csv` and the guarded, repeatable `scripts/Populate-IndianaDetectionMeasureMapping.ps1`/`.sql` import. The result report is `~/atspm-import-records-20260929/detection-measure-apply-latest.jsonl` on `.50`, with a workstation copy at `build/indiana-detection-measure-apply-20261109.jsonl`. No VM staging folders were created. Application and deployment source changes remain uncommitted.
